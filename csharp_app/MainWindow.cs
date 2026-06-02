using System;
using System.Drawing;
using System.Windows.Forms;
using System.IO.Ports;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using System.Text;

namespace csharp_app
{
    public class MainWindow : Form
    {
        [DllImport("libsensor", CallingConvention = CallingConvention.Cdecl)]
        public static extern bool initialize_sensor(byte activation_byte);

        [DllImport("libsensor", CallingConvention = CallingConvention.Cdecl)]
        public static extern float read_weight_kg(int raw_adc_value, float calibration_factor);

        private Label weightDisplay;
        private Label statusDisplay;
        private Label rawAdcDisplay;

        private SerialPort? mySerialPort;
        private System.Windows.Forms.Timer reconnectTimer;
        private CancellationTokenSource? cts;

        private float calibrationFactor = 21000.0f;
        private long zeroOffset = 0;
        private bool isFirstRead = true;
        private float smoothedWeightKg = 0.0f;
        
        // 🔥 Hardcoded "COM8" වෙනුවට Auto අල්ලගන්න පෝට් එක දාන්න Variable එකක්
        private string detectedPortName = ""; 

        // ඩේටා පැකට් එක එකවර කියවීමට බෆර් එක
        private readonly byte[] rawBuffer = new byte[4];

        [STAThread]
        public static void Main()
        {
            Application.EnableVisualStyles();
            Application.Run(new MainWindow());
        }

        public MainWindow()
        {
            this.Text = "Smart Scale Live Monitor";
            this.Size = new Size(460, 420);
            this.BackColor = Color.FromArgb(18, 18, 24);
            this.StartPosition = FormStartPosition.CenterScreen;
            this.FormBorderStyle = FormBorderStyle.FixedSingle;
            this.MaximizeBox = false;

            Label headerLabel = new Label
            {
                Text = "SYSTEM METRICS",
                ForeColor = Color.FromArgb(110, 115, 140),
                Font = new Font("Segoe UI", 10, FontStyle.Bold),
                Location = new Point(30, 25),
                Size = new Size(400, 20),
                TextAlign = ContentAlignment.MiddleLeft
            };
            this.Controls.Add(headerLabel);

            Panel displayCard = new Panel
            {
                Size = new Size(384, 180),
                Location = new Point(30, 55),
                BackColor = Color.FromArgb(28, 28, 38)
            };
            this.Controls.Add(displayCard);

            Panel accentLine = new Panel
            {
                Size = new Size(384, 4),
                Location = new Point(0, 0),
                BackColor = Color.FromArgb(0, 230, 118)
            };
            displayCard.Controls.Add(accentLine);

            weightDisplay = new Label
            {
                Text = "---",
                ForeColor = Color.FromArgb(240, 244, 255),
                Font = new Font("Consolas", 44, FontStyle.Bold),
                Size = new Size(384, 80),
                Location = new Point(0, 35),
                TextAlign = ContentAlignment.MiddleCenter
            };
            displayCard.Controls.Add(weightDisplay);

            Label unitLabel = new Label
            {
                Text = "KILOGRAMS (KG)",
                ForeColor = Color.FromArgb(0, 230, 118),
                Font = new Font("Segoe UI Semibold", 10, FontStyle.Bold),
                Size = new Size(384, 25),
                Location = new Point(0, 115),
                TextAlign = ContentAlignment.MiddleCenter
            };
            displayCard.Controls.Add(unitLabel);

            Panel infoPanel = new Panel
            {
                Size = new Size(384, 50),
                Location = new Point(30, 250),
                BackColor = Color.FromArgb(24, 24, 32)
            };
            this.Controls.Add(infoPanel);

            Label rawLabel = new Label
            {
                Text = "RAW STREAM DATA:",
                ForeColor = Color.FromArgb(140, 145, 170),
                Font = new Font("Segoe UI", 9, FontStyle.Regular),
                Location = new Point(15, 15),
                Size = new Size(150, 20),
                TextAlign = ContentAlignment.MiddleLeft
            };
            infoPanel.Controls.Add(rawLabel);

            rawAdcDisplay = new Label
            {
                Text = "----",
                ForeColor = Color.FromArgb(240, 244, 255),
                Font = new Font("Consolas", 11, FontStyle.Bold),
                Location = new Point(165, 15),
                Size = new Size(204, 20),
                TextAlign = ContentAlignment.MiddleRight
            };
            infoPanel.Controls.Add(rawAdcDisplay);

            statusDisplay = new Label
            {
                Text = "STATUS: INITIALIZING...",
                ForeColor = Color.FromArgb(255, 171, 0),
                Font = new Font("Segoe UI", 9, FontStyle.Bold),
                Location = new Point(30, 325),
                Size = new Size(384, 25),
                TextAlign = ContentAlignment.MiddleLeft
            };
            this.Controls.Add(statusDisplay);

            // Reconnect Timer එක (තත්පර 2න් 2කට පෝට් ස්කෑන් කරන්න සකස් කලා)
            reconnectTimer = new System.Windows.Forms.Timer();
            reconnectTimer.Interval = 2000;
            reconnectTimer.Tick += AttemptReconnectEvent;

            FormClosing += MainWindow_FormClosing;

            // මුලින්ම කනෙක්ෂන් එක පටන් ගන්නවා
            InitializeHardwareConnection();
        }

        private void InitializeHardwareConnection()
        {
            statusDisplay.Text = "STATUS: SCANNING PORTS...";
            statusDisplay.ForeColor = Color.FromArgb(255, 171, 0);
            reconnectTimer.Stop();

            Task.Run(() =>
            {
                try
                {
                    // 1. DLL Check
                    bool isReady = initialize_sensor(4);
                    if (!isReady)
                    {
                        this.Invoke(new Action(() => HandleDisconnectState("STATUS: DLL HARDWARE ERROR")));
                        return;
                    }

                    // 2. 🔥 Console එකේ වගේම ඔටෝමැටිකව පෝට් එක හොයනවා
                    detectedPortName = AutoFindScalePort();

                    if (string.IsNullOrEmpty(detectedPortName))
                    {
                        this.Invoke(new Action(() => HandleDisconnectState("STATUS: SCALE NOT FOUND. RETRYING...")));
                        return;
                    }

                    // 3. පෝට් එක Open කිරීම
                    var port = new SerialPort(detectedPortName, 9600, Parity.None, 8, StopBits.One);
                    port.Open();

                    mySerialPort = port;
                    isFirstRead = true;

                    this.Invoke(new Action(() =>
                    {
                        statusDisplay.Text = $"STATUS: CONNECTED ON {detectedPortName} (TARE)...";
                        statusDisplay.ForeColor = Color.FromArgb(255, 171, 0);
                    }));

                    cts = new CancellationTokenSource();
                    Task.Run(() => StreamDataTask(cts.Token));
                }
                catch
                {
                    this.Invoke(new Action(() => HandleDisconnectState("DISCONNECTED: CHECK CABLE")));
                }
            });
        }

        // 🕵️‍♂️ පෝට් එක ඔටෝමැටිකව ස්කෑන් කරන ලොජික් එක
        private string AutoFindScalePort()
        {
            string[] ports = SerialPort.GetPortNames();
            foreach (string port in ports)
            {
                try
                {
                    using (SerialPort testPort = new SerialPort(port, 9600, Parity.None, 8, StopBits.One))
                    {
                        testPort.ReadTimeout = 300;
                        testPort.Open();
                        
                        for (int i = 0; i < 15; i++)
                        {
                            if (testPort.BytesToRead > 0 && testPort.ReadByte() == 0xAA)
                            {
                                testPort.Close();
                                return port; 
                            }
                            Thread.Sleep(20);
                        }
                        testPort.Close();
                    }
                }
                catch { }
            }
            return string.Empty;
        }

        // 📥 High-Speed බයිට් ස්ට්‍රීම් එක බැක්ග්‍රවුන්ඩ් එකේ කියවන Task එක
        private async Task StreamDataTask(CancellationToken token)
        {
            while (!token.IsCancellationRequested)
            {
                try
                {
                    if (mySerialPort == null || !mySerialPort.IsOpen)
                        throw new Exception("Port closed");

                    // 🔥 TEXT වෙනුවට බයිට් 6ක පැකට් එක කෙළින්ම චෙක් කරනවා
                    if (mySerialPort.BytesToRead >= 6)
                    {
                        byte header = (byte)mySerialPort.ReadByte();
                        if (header != 0xAA) continue;

                        mySerialPort.Read(rawBuffer, 0, 4);
                        byte receivedChecksum = (byte)mySerialPort.ReadByte();
                        byte calculatedChecksum = (byte)(rawBuffer[0] + rawBuffer[1] + rawBuffer[2] + rawBuffer[3]);

                        if (calculatedChecksum == receivedChecksum)
                        {
                            // Bit stitching
                            int liveADCValue = (rawBuffer[0] << 24) | (rawBuffer[1] << 16) | (rawBuffer[2] << 8) | rawBuffer[3];

                            // UI එක ආරක්ෂිතව අප්ඩේට් කරන්න Invoke පාවිච්චි කරනවා
                            this.BeginInvoke(new Action(() => ProcessIncomingMetrics(liveADCValue)));
                        }
                    }
                }
                catch (OperationCanceledException)
                {
                    break;
                }
                catch
                {
                    this.Invoke(new Action(() => HandleDisconnectState("CONNECTION LOST: RECONNECTING...")));
                    break;
                }

                await Task.Delay(5, token);
            }
        }

        private void ProcessIncomingMetrics(int liveADCValue)
        {
            if (isFirstRead)
            {
                zeroOffset = liveADCValue;
                isFirstRead = false;
                smoothedWeightKg = 0.0f;

                statusDisplay.Text = $"STATUS: OPERATIONAL ({detectedPortName})";
                statusDisplay.ForeColor = Color.FromArgb(0, 230, 118);
                return;
            }

            int cleanADCValue = liveADCValue - (int)zeroOffset;
            float currentWeightKg = read_weight_kg(cleanADCValue, calibrationFactor);

            if (currentWeightKg < 0 || float.IsNaN(currentWeightKg))
                currentWeightKg = 0.0f;

            // Dynamic Smoothing Filter
            float difference = Math.Abs(currentWeightKg - smoothedWeightKg);
            float dynamicSmoothingFactor = difference > 0.150f ? 1.0f
                                         : difference > 0.030f ? 0.4f
                                         : 0.04f;

            smoothedWeightKg = (dynamicSmoothingFactor * currentWeightKg) + ((1.0f - dynamicSmoothingFactor) * smoothedWeightKg);

            // ලස්සනට UI එක අප්ඩේට් කිරීම
            rawAdcDisplay.Text = $"0x{liveADCValue:X8} ({liveADCValue} ticks)";
            weightDisplay.Text = smoothedWeightKg.ToString("F3");
        }

        private void HandleDisconnectState(string message)
        {
            if (cts != null)
            {
                cts.Cancel();
                cts.Dispose();
                cts = null;
            }

            statusDisplay.Text = message;
            statusDisplay.ForeColor = Color.FromArgb(255, 23, 68);
            weightDisplay.Text = "---";
            rawAdcDisplay.Text = "----";

            if (mySerialPort != null)
            {
                var targetPort = mySerialPort;
                mySerialPort = null;
                Task.Run(() => { try { targetPort.Close(); targetPort.Dispose(); } catch { } });
            }

            if (!reconnectTimer.Enabled)
                reconnectTimer.Start();
        }

        private void AttemptReconnectEvent(object? sender, EventArgs e)
        {
            if (mySerialPort == null)
            {
                InitializeHardwareConnection();
            }
        }

        private void MainWindow_FormClosing(object? sender, FormClosingEventArgs e)
        {
            reconnectTimer.Stop();
            if (cts != null) { cts.Cancel(); }
            if (mySerialPort != null) { try { mySerialPort.Close(); } catch { } }
        }
    }
}