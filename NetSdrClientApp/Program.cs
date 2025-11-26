using System;
using System.Diagnostics.CodeAnalysis; // <--- 1. Додай
using System.Threading.Tasks;
using NetSdrClientApp.Networking;

namespace NetSdrClientApp
{
    [ExcludeFromCodeCoverage] // <--- 2. Додай
    class Program
    {
        // Важливо: Main має бути async Task, бо ми використовуємо await
        static async Task Main(string[] args)
        {
            Console.WriteLine(@"Usage:
C - connect
D - disconnect
F - set frequency
S - Start/Stop IQ listener
Q - quit");

            // Налаштовуємо підключення до локального сервера (EchoServer)
            var tcpClient = new TcpClientWrapper("127.0.0.1", 5000);
            var udpClient = new UdpClientWrapper(60000);

            var netSdr = new NetSdrClient(tcpClient, udpClient);

            while (true)
            {
                var key = Console.ReadKey(intercept: true).Key;

                if (key == ConsoleKey.C)
                {
                    Console.WriteLine("Connecting...");
                    await netSdr.ConnectAsync();
                    Console.WriteLine("Connected!");
                }
                else if (key == ConsoleKey.D)
                {
                    Console.WriteLine("Disconnecting...");
                    netSdr.Disconect();
                }
                else if (key == ConsoleKey.F)
                {
                    Console.WriteLine("Changing frequency...");
                    await netSdr.ChangeFrequencyAsync(20000000, 1);
                }
                else if (key == ConsoleKey.S)
                {
                    if (netSdr.IQStarted)
                    {
                        Console.WriteLine("Stopping IQ...");
                        await netSdr.StopIQAsync();
                    }
                    else
                    {
                        Console.WriteLine("Starting IQ...");
                        await netSdr.StartIQAsync();
                    }
                }
                else if (key == ConsoleKey.Q)
                {
                    break;
                }
            }
        }
    }
}