using System;
using System.Diagnostics.CodeAnalysis;
using System.Net.Sockets;

namespace EchoServer.Abstractions
{
    [ExcludeFromCodeCoverage]
    public class TcpClientWrapper : ITcpClientWrapper
    {
        private readonly TcpClient _client;

        public TcpClientWrapper(TcpClient client)
        {
            _client = client;
        }

        public INetworkStreamWrapper GetStream()
        {
            return new NetworkStreamWrapper(_client.GetStream());
        }

        public void Close()
        {
            _client.Close();
        }

        public void Dispose()
        {
            _client?.Dispose();
            GC.SuppressFinalize(this);
        }
    }
}
