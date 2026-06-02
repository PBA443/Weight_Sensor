using System.IO.Ports;

class Program
{
    static void Main()
    {
        SerialPort port = new SerialPort("COM7", 9600);

        try
        {
            port.Open();
            Console.WriteLine("Serial Port opened successfully.");
            port.Close();
        }
        catch (Exception ex)
        {
            Console.WriteLine("An error occurred: " + ex.Message);
        }
    }
}