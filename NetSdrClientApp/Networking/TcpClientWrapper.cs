using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace NetSdrClientApp.Networking
{
    // ЗМІНЕНО: Додано реалізацію IDisposable
    public class TcpClientWrapper : ITcpClient, IDisposable
    {
        // ЗМІНЕНО: Поля зроблені readonly
        private readonly string _host;
        private readonly int _port;
        private TcpClient? _tcpClient;
        private NetworkStream? _stream;
        // ЗМІНЕНО: _cts тепер nullable
        private CancellationTokenSource? _cts;

        public bool Connected => _tcpClient != null && _tcpClient.Connected && _stream != null;

        public event EventHandler<byte[]>? MessageReceived;

        public TcpClientWrapper(string host, int port)
        {
            _host = host;
            _port = port;
        }

        public void Connect()
        {
            if (Connected)
            {
                Console.WriteLine($"Already connected to {_host}:{_port}");
                return;
            }

            try
            {
                _tcpClient = new TcpClient();
                _cts = new CancellationTokenSource();
                _tcpClient.Connect(_host, _port);
                _stream = _tcpClient.GetStream();
                Console.WriteLine($"Connected to {_host}:{_port}");
                _ = StartListeningAsync();
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Failed to connect: {ex.Message}");
                Dispose(); // Важливо очистити ресурси при невдалому підключенні
            }
        }

        public void Disconnect()
        {
            if (Connected)
            {
                Console.WriteLine("Disconnecting...");
                _cts?.Cancel(); // Сигнал для зупинки циклу прослуховування
                Dispose();      // Очищення всіх ресурсів
                Console.WriteLine("Disconnected.");
            }
            else
            {
                Console.WriteLine("No active connection to disconnect.");
            }
        }
        
        // НОВИЙ МЕТОД: Реалізація IDisposable для очищення ресурсів
        public void Dispose()
        {
            _stream?.Dispose();
            _tcpClient?.Dispose();
            _cts?.Dispose();
            _stream = null;
            _tcpClient = null;
            _cts = null;
        }

        public async Task SendMessageAsync(byte[] data)
        {
            if (Connected && _stream != null && _stream.CanWrite && _cts != null)
            {
                Console.WriteLine($"Message sent: " + data.Select(b => Convert.ToString(b, toBase: 16)).Aggregate((l, r) => $"{l} {r}"));
                // ЗМІНЕНО: Використовуємо сучасний overload
                await _stream.WriteAsync(data, _cts.Token);
            }
            else
            {
                throw new InvalidOperationException("Not connected to a server.");
            }
        }

        public async Task SendMessageAsync(string str)
        {
            var data = Encoding.UTF8.GetBytes(str);
            // Викликаємо перевантажений метод, щоб уникнути дублювання коду
            await SendMessageAsync(data);
        }

        private async Task StartListeningAsync()
        {
            if (Connected && _stream != null && _stream.CanRead && _cts != null)
            {
                try
                {
                    Console.WriteLine($"Starting listening for incoming messages.");
                    byte[] buffer = new byte[8194];

                    while (!_cts.Token.IsCancellationRequested)
                    {
                        int bytesRead = await _stream.ReadAsync(buffer, _cts.Token);
                        if (bytesRead > 0)
                        {
                            MessageReceived?.Invoke(this, buffer.AsSpan(0, bytesRead).ToArray());
                        }
                        else
                        {
                            // З'єднання було закрито з іншого боку
                            break; 
                        }
                    }
                }
                // ЗМІНЕНО: Прибрано невикористану змінну 'ex'
                catch (OperationCanceledException)
                {
                    // Це очікуваний виняток при Disconnect
                }
                catch (IOException)
                {
                    // Це очікуваний виняток, якщо з'єднання розірвано
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"Error in listening loop: {ex.Message}");
                }
                finally
                {
                    Console.WriteLine("Listener stopped.");
                    // Автоматично відключаємось, якщо цикл прослуховування завершився
                    Disconnect();
                }
            }
            else
            {
                throw new InvalidOperationException("Not connected to a server.");
            }
        }
    }
}
