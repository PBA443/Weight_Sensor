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
    private static int zeroOffset = 0;
    private static bool isFirstRead = true;
    private static bool isRunning = true;
    private static float smoothedWeightKg = 0.0f;

    static void Main(string[] args)
    {
        Console.OutputEncoding = System.Text.Encoding.UTF8;

        Console.WriteLine("Starting Scale Reader...");

        try
        {
            if (!initialize_sensor(0x04))
            {
                Console.WriteLine("DLL initialization failed.");
                return;
            }

            Console.WriteLine("DLL initialized successfully.");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"DLL Error: {ex}");
            return;
        }

        while (isRunning)
        {
            Console.WriteLine("\nSearching COM ports...");

            string targetPort = AutoFindScalePort();

            if (string.IsNullOrEmpty(targetPort))
            {
                Console.WriteLine("No scale detected. Retrying...");
                Thread.Sleep(2000);
                continue;
            }

            Console.WriteLine($"Scale found on {targetPort}");

            try
            {
                mySerialPort = new SerialPort(
                    targetPort,
                    9600,
                    Parity.None,
                    8,
                    StopBits.One);

                mySerialPort.ReadTimeout = 1000;
                mySerialPort.Open();
                mySerialPort.DiscardInBuffer();
                Console.WriteLine($"Connected to {targetPort}");

                mySerialPort.DataReceived += DataReceivedHandler;

                while (mySerialPort.IsOpen && isRunning)
                {
                    Thread.Sleep(500);
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Port Error: {ex.Message}");
            }
            finally
            {
                CleanupPort();
                isFirstRead = true;
                Console.WriteLine("Disconnected.");
            }
        }
    }

    private static void DataReceivedHandler(
        object sender,
        SerialDataReceivedEventArgs e)
    {
        try
        {
            SerialPort sp = (SerialPort)sender;

            while (sp.BytesToRead >= 6)
            {
                int header = sp.ReadByte();

                if (header != 0xAA)
                {
                    continue;
                }

                byte[] buffer = new byte[5];

                int totalRead = 0;

                while (totalRead < buffer.Length)
                {
                    int n = sp.Read(
                        buffer,
                        totalRead,
                        buffer.Length - totalRead);

                    if (n <= 0)
                        return;

                    totalRead += n;
                }

                int liveADCValue =
                    BitConverter.ToInt32(buffer, 0);

                byte receivedChecksum = buffer[4];

                byte calculatedChecksum =
                    (byte)(
                        buffer[0] +
                        buffer[1] +
                        buffer[2] +
                        buffer[3]);

                if (calculatedChecksum != receivedChecksum)
                {
                    Console.WriteLine(
                        $"\nChecksum Error " +
                        $"RX={receivedChecksum:X2} " +
                        $"CALC={calculatedChecksum:X2}");

                    continue;
                }

                if (isFirstRead)
                {
                    zeroOffset = liveADCValue;
                    isFirstRead = false;

                    Console.WriteLine(
                        $"\nZero Offset Set = {zeroOffset}");
                }

                Console.Write(
                    $"\rADC={liveADCValue}  ");

                try
                {
                    float currentWeightKg = read_weight_kg(liveADCValue - zeroOffset, calibrationFactor);

                    // 1. Dead-zone & NaN filtering
                    if (currentWeightKg < 0.005f || float.IsNaN(currentWeightKg))
                        currentWeightKg = 0.0f;

                    // 2. Dynamic Smoothing Filter
                    float diff = Math.Abs(currentWeightKg - smoothedWeightKg);

                    float alpha = diff > 0.150f ? 1.0f 
                                : diff > 0.020f ? 0.2f 
                                : 0.01f;

                    smoothedWeightKg = (alpha * currentWeightKg) + ((1.0f - alpha) * smoothedWeightKg);

                    // 3. UI update
                    Console.Write($"\rWeight={smoothedWeightKg:F3} kg     ");
                }
                catch (Exception ex)
                {
                    Console.WriteLine(
                        $"\nWeight Calculation Error: {ex}");
                }
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine(
                $"\nData Receive Error: {ex}");
        }
    }

    private static string AutoFindScalePort()
    {
        foreach (string port in SerialPort.GetPortNames())
        {
            try
            {
                Console.WriteLine($"Checking {port}...");

                using (SerialPort tp =
                    new SerialPort(port, 9600))
                {
                    tp.ReadTimeout = 500;
                    tp.Open();

                    int firstByte = tp.ReadByte();

                    Console.WriteLine(
                        $"{port} First Byte = 0x{firstByte:X2}");

                    if (firstByte == 0xAA)
                    {
                        return port;
                    }
                }
            }
            catch
            {
            }
        }

        return string.Empty;
    }

    private static void CleanupPort()
    {
        try
        {
            if (mySerialPort != null)
            {
                if (mySerialPort.IsOpen)
                {
                    mySerialPort.Close();
                }

                mySerialPort.Dispose();
                mySerialPort = null;
            }
        }
        catch
        {
        }
    }
}