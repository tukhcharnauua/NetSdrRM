using System;
using System.Net;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

// Додаємо реалізацію IDisposable та IEquatable
public class UdpClientWrapper : IUdpClient, IDisposable, IEquatable<UdpClientWrapper>
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
        Console.WriteLine("Start listening for UDP messages...");

        try
        {
            _udpClient = new UdpClient(_localEndPoint);
            while (!_cts.Token.IsCancellationRequested)
            {
                UdpReceiveResult result = await _udpClient.ReceiveAsync(_cts.Token);
                MessageReceived?.Invoke(this, result.Buffer);

                Console.WriteLine($"Received from {result.RemoteEndPoint}");
            }
        }
        catch (OperationCanceledException)
        {
            // Це очікуваний виняток при зупинці, тому він порожній
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Error receiving message: {ex.Message}");
        }
    }

    public void StopListening()
    {
        try
        {
            _cts?.Cancel();
            Console.WriteLine("Stopped listening for UDP messages.");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Error while stopping: {ex.Message}");
        }
        finally
        {
            // Викликаємо Dispose для очищення ресурсів
            Dispose();
        }
    }

    public void Exit()
    {
        // Метод Exit робить те саме, що й StopListening, можна просто викликати його
        StopListening();
    }

    // НОВИЙ МЕТОД: реалізація IDisposable
    public void Dispose()
    {
        _cts?.Dispose();
        _udpClient?.Dispose();
    }

    public override int GetHashCode()
    {
        var payload = $"{nameof(UdpClientWrapper)}|{_localEndPoint.Address}|{_localEndPoint.Port}";

        using var md5 = MD5.Create();
        var hash = md5.ComputeHash(Encoding.UTF8.GetBytes(payload));

        return BitConverter.ToInt32(hash, 0);
    }
    
    // НОВИЙ МЕТОД: реалізація Equals
    public override bool Equals(object? obj)
    {
        // Використовуємо наш новий, типізований метод Equals
        return Equals(obj as UdpClientWrapper);
    }

    // НОВИЙ МЕТОД: реалізація IEquatable<T> для правильного порівняння
    public bool Equals(UdpClientWrapper? other)
    {
        if (other is null)
        {
            return false;
        }

        // Якщо об'єкти посилаються на одне й те саме місце в пам'яті
        if (ReferenceEquals(this, other))
        {
            return true;
        }

        // Порівнюємо за тими самими полями, що і в GetHashCode
        return this._localEndPoint.Address.Equals(other._localEndPoint.Address) &&
               this._localEndPoint.Port == other._localEndPoint.Port;
    }
    
    // НОВІ ОПЕРАТОРИ: найкраща практика при реалізації Equals
    public static bool operator ==(UdpClientWrapper? left, UdpClientWrapper? right)
    {
        if (left is null)
        {
            return right is null;
        }
        return left.Equals(right);
    }

    public static bool operator !=(UdpClientWrapper? left, UdpClientWrapper? right)
    {
        return !(left == right);
    }
}
