using System;
using System.IO.Ports;
using System.Runtime.InteropServices;
using System.Threading;

class Program
{
    [DllImport("libsensor", CallingConvention = CallingConvention.Cdecl)]
    public static extern bool initialize_sensor(byte activation_byte);

    [DllImport("libsensor", CallingConvention = CallingConvention.Cdecl)]
    public static extern float read_weight_kg(int raw_adc_value, float calibration_factor);

    private static SerialPort? mySerialPort; 
    private static float calibrationFactor = 21000.0f; 
    private static long zeroOffset = 0; 
    private static bool isFirstRead = true;
    private static float smoothedWeightKg = 0.0f;
    
    private static readonly object streamLock = new object();
    private static readonly byte[] rawBuffer = new byte[4]; 
    private static bool isRunning = true;

    static void Main(string[] args)
    {
        Console.OutputEncoding = System.Text.Encoding.UTF8;
        Console.WriteLine("=============================================");
        Console.WriteLine("   SMART SCALE - AUTO-DETECT & RECONNECT     ");
        Console.WriteLine("=============================================");
        
        // DLL Activation
        if (!initialize_sensor(0x04))
        {
            Console.WriteLine("❌ [Error] Sensor activation hardware check failed.");
            return;
        }
        Console.WriteLine("✔ [Status] Sensor DLL verified successfully.");

        while (isRunning)
        {
            string targetPort = AutoFindScalePort();

            if (string.IsNullOrEmpty(targetPort))
            {
                Console.Write("\r🔍 [System] Scale not found. Retrying in 2 seconds...   ");
                Thread.Sleep(2000);
                continue;
            }

            Console.WriteLine($"\n⚡ [System] Found Scale on {targetPort}! Connecting...");
            
            mySerialPort = new SerialPort(targetPort, 9600, Parity.None, 8, StopBits.One);
            
            try
            {
                mySerialPort.Open();
                Console.WriteLine($"✔ [{targetPort}] Connected successfully!");
                Console.WriteLine("👉 Leave the scale empty for auto-zero (Tare)...");

                mySerialPort.DataReceived += DataReceivedHandler;

                while (mySerialPort.IsOpen && isRunning)
                {
                    Thread.Sleep(500);
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"\n❌ [Hardware Error] Connection lost: {ex.Message}");
            }
            finally
            {
                CleanupPort();
                isFirstRead = true;
                Console.WriteLine("\n🔄 [System] Attempting to reconnect...");
                Thread.Sleep(2000);
            }
        }
    }

    private static string AutoFindScalePort()
    {
        string[] ports = SerialPort.GetPortNames();
        
        foreach (string port in ports)
        {
            try 
            {
                using (SerialPort testPort = new SerialPort(port, 9600, Parity.None, 8, StopBits.One))
                {
                    testPort.ReadTimeout = 500;
                    testPort.Open();
                    
                    int bytesToScan = 20; 
                    for (int i = 0; i < bytesToScan; i++)
                    {
                        if (testPort.BytesToRead > 0)
                        {
                            if (testPort.ReadByte() == 0xAA)
                            {
                                testPort.Close();
                                return port;
                            }
                        }
                        Thread.Sleep(20);
                    }
                    testPort.Close();
                }
            }
            catch { /* dismiss any port that can't be opened or read from */ }
        }
        return string.Empty;
    }

    private static void DataReceivedHandler(object sender, SerialDataReceivedEventArgs e)
    {
        SerialPort sp = (SerialPort)sender;
        if (!sp.IsOpen) return;

        lock (streamLock)
        {
            try
            {
                while (sp.BytesToRead >= 6)
                {
                    byte header = (byte)sp.ReadByte();
                    if (header != 0xAA) continue; 

                    sp.Read(rawBuffer, 0, 4);
                    byte receivedChecksum = (byte)sp.ReadByte();
                    byte calculatedChecksum = (byte)(rawBuffer[0] + rawBuffer[1] + rawBuffer[2] + rawBuffer[3]);
                    
                    if (calculatedChecksum == receivedChecksum)
                    {
                        int liveADCValue = (rawBuffer[0] << 24) | (rawBuffer[1] << 16) | (rawBuffer[2] << 8) | rawBuffer[3];

                        if (isFirstRead)
                        {
                            zeroOffset = liveADCValue;
                            isFirstRead = false;
                            Console.WriteLine("\n⭐ Scale Tarred and Ready! Place load now...\n");
                        }

                        int cleanADCValue = liveADCValue - (int)zeroOffset;
                        float currentWeightKg = read_weight_kg(cleanADCValue, calibrationFactor);

                        float difference = Math.Abs(currentWeightKg - smoothedWeightKg);
                        float dynamicSmoothingFactor = difference > 0.150f ? 1.0f : (difference > 0.030f ? 0.4f : 0.04f);

                        smoothedWeightKg = (dynamicSmoothingFactor * currentWeightKg) + ((1.0f - dynamicSmoothingFactor) * smoothedWeightKg);
                        float weightGrams = smoothedWeightKg * 1000.0f;

                        Console.Write($"\r[COM Port Active] --> Weight: {smoothedWeightKg:F3} kg  ({weightGrams:F0} g)       ");
                    }
                }
            }
            catch { /* dismiss any port that can't be opened or read from */ }
        }
    }

    private static void CleanupPort()
    {
        if (mySerialPort != null)
        {
            try
            {
                mySerialPort.DataReceived -= DataReceivedHandler;
                if (mySerialPort.IsOpen) mySerialPort.Close();
                mySerialPort.Dispose();
            }
            catch { }
            mySerialPort = null;
        }
    }
}