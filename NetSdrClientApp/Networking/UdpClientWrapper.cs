using System.Security.Cryptography;
using System;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace NetSdrClientApp.Networking
{
    public class UdpClientWrapper : IUdpClient
    {
        private readonly IPEndPoint _localEndPoint;
        private CancellationTokenSource? _cts;
        private UdpClient? _udpClient;

        public event EventHandler<byte[]>? MessageReceived;

        public UdpClientWrapper(int port)
        {
            _localEndPoint = new IPEndPoint(IPAddress.Any, port);
        }

        public async Task StartListeningAsync()
        {
            _cts = new CancellationTokenSource();
            try
            {
                _udpClient = new UdpClient(_localEndPoint);
                while (!_cts.Token.IsCancellationRequested)
                {
                    UdpReceiveResult result = await _udpClient.ReceiveAsync(_cts.Token);
                    MessageReceived?.Invoke(this, result.Buffer);
                }
            }
            catch (OperationCanceledException)
            {
                // Normal cancellation, no action needed
            }
            catch (SocketException ex)
            {
                Console.WriteLine($"Socket error: {ex.Message}");
                throw; // Re-throw для можливості обробки в тестах
            }
            catch (ObjectDisposedException)
            {
                // Client was disposed, expected during shutdown
            }
        }

        public void StopListening()
        {
            CleanupResources();
        }

        public void Exit()
        {
            CleanupResources();
        }

        private void CleanupResources()
        {
            try
            {
                _cts?.Cancel();
                _cts?.Dispose();
                _udpClient?.Close();
                _udpClient?.Dispose();
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error while stopping: {ex.Message}");
            }
        }

        // --- ДОДАНО МЕТОД EQUALS (Виправляє тест Equals_ReturnsTrueForSamePort) ---
        public override bool Equals(object? obj)
        {
            if (obj is UdpClientWrapper other)
            {
                // Порівнюємо об'єкти за номером порту
                return _localEndPoint.Port == other._localEndPoint.Port;
            }
            return false;
        }
        // --------------------------------------------------------------------------

        public override int GetHashCode()
        {
            // Використовуємо SHA256 замість MD5, щоб Сонар не лаявся на безпеку
            var payload = $"{nameof(UdpClientWrapper)}|{_localEndPoint.Address}|{_localEndPoint.Port}";

            using var sha = SHA256.Create();
            var hash = sha.ComputeHash(Encoding.UTF8.GetBytes(payload));

            return BitConverter.ToInt32(hash, 0);
        }
    }
}