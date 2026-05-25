using System;
using System.Drawing;
using System.Windows.Forms;
using System.IO.Ports;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;

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
        private string portName = "COM8";

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
                Text = "0.000",
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
                Text = "0000",
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

            reconnectTimer = new System.Windows.Forms.Timer();
            reconnectTimer.Interval = 1000; 
            reconnectTimer.Tick += AttemptReconnectEvent;

            FormClosing += MainWindow_FormClosing;

            InitializeHardwareConnection();
        }

        private void InitializeHardwareConnection()
        {
            statusDisplay.Text = "STATUS: CONNECTING...";
            statusDisplay.ForeColor = Color.FromArgb(255, 171, 0);

            try
            {
                bool isReady = initialize_sensor(4);
                if (!isReady)
                {
                    HandleDisconnectState("STATUS: HARDWARE ERROR");
                    return;
                }

                mySerialPort = new SerialPort(portName, 9600, Parity.None, 8, StopBits.One);
                mySerialPort.Open();

                isFirstRead = true; 
                statusDisplay.Text = "STATUS: ZEROING SCALE (TARE)...";
                statusDisplay.ForeColor = Color.FromArgb(255, 171, 0);

                // Start the background data listening task loop
                cts = new CancellationTokenSource();
                Task.Run(() => StreamDataTask(cts.Token));
                reconnectTimer.Stop();
            }
            catch
            {
                HandleDisconnectState("DISCONNECTED: CHECK CABLE");
            }
        }

        private async Task StreamDataTask(CancellationToken token)
        {
            while (!token.IsCancellationRequested)
            {
                try
                {
                    // If the port object became null outside, exit immediately
                    if (mySerialPort == null || !mySerialPort.IsOpen)
                    {
                        throw new Exception("Port disconnected");
                    }

                    // 🔴 NON-BLOCKING CHECK: Look at the software layer buffer instead of blocking native drivers
                    if (mySerialPort.BytesToRead > 0)
                    {
                        string rawDataStr = mySerialPort.ReadLine().Trim();
                        if (int.TryParse(rawDataStr, out int liveADCValue))
                        {
                            ProcessIncomingMetrics(liveADCValue);
                        }
                    }
                }
                catch
                {
                    // If any hardware line error happens, safely drop straight to UI updating
                    this.Invoke(new Action(() => HandleDisconnectState("CONNECTION LOST: RECONNECTING...")));
                    break;
                }

                // Sleep the background worker thread briefly to reduce CPU load
                await Task.Delay(30, token);
            }
        }

        private void ProcessIncomingMetrics(int liveADCValue)
        {
            if (isFirstRead)
            {
                zeroOffset = liveADCValue;
                isFirstRead = false;
                int cleanFirstADC = liveADCValue - (int)zeroOffset;
                smoothedWeightKg = read_weight_kg(cleanFirstADC, calibrationFactor);

                this.Invoke(new Action(() => {
                    statusDisplay.Text = "STATUS: OPERATIONAL";
                    statusDisplay.ForeColor = Color.FromArgb(0, 230, 118);
                }));
                return;
            }

            int cleanADCValue = liveADCValue - (int)zeroOffset;
            float currentWeightKg = read_weight_kg(cleanADCValue, calibrationFactor);
            if (currentWeightKg < 0) {
                currentWeightKg = 0.0f;
            }
            float difference = Math.Abs(currentWeightKg - smoothedWeightKg);
            float dynamicSmoothingFactor = difference > 0.150f ? 1.0f : (difference > 0.030f ? 0.4f : 0.04f);

            smoothedWeightKg = (dynamicSmoothingFactor * currentWeightKg) + ((1.0f - dynamicSmoothingFactor) * smoothedWeightKg);

            this.Invoke(new Action(() => {
                rawAdcDisplay.Text = $"{liveADCValue} ticks";
                weightDisplay.Text = smoothedWeightKg.ToString("F3");
            }));
        }

        private void HandleDisconnectState(string message)
        {
            // Kill background task token loops safely
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

            // Destroy the port reference cleanly without blocking via a fire-and-forget worker
            if (mySerialPort != null)
            {
                var targetPort = mySerialPort;
                mySerialPort = null;
                Task.Run(() => { try { targetPort.Close(); targetPort.Dispose(); } catch { } });
            }

            if (!reconnectTimer.Enabled)
            {
                reconnectTimer.Start();
            }
        }

        private void AttemptReconnectEvent(object? sender, EventArgs e)
        {
            if (mySerialPort == null)
            {
                string[] activePorts = SerialPort.GetPortNames();
                if (Array.Exists(activePorts, port => port.Equals(portName, StringComparison.OrdinalIgnoreCase)))
                {
                    InitializeHardwareConnection();
                }
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