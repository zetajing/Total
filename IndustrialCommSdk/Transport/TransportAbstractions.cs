using System;
using System.Net;
using System.Threading;
using System.Threading.Tasks;

namespace IndustrialCommSdk.Transport
{
    public sealed class TcpTransportOptions
    {
        public string Host { get; set; }
        public int Port { get; set; }
        public int ConnectTimeoutMilliseconds { get; set; } = 3000;
        public int SendTimeoutMilliseconds { get; set; } = 3000;
        public int ReceiveTimeoutMilliseconds { get; set; } = 5000;
        public bool AutoReconnect { get; set; } = true;
    }

    public interface ITransportClient : IDisposable
    {
        bool IsConnected { get; }
        EndPoint RemoteEndPoint { get; }

        Task ConnectAsync(CancellationToken cancellationToken);
        Task DisconnectAsync(CancellationToken cancellationToken);
        Task SendAsync(byte[] payload, CancellationToken cancellationToken);
        Task<byte[]> ReceiveExactAsync(int length, CancellationToken cancellationToken);
    }

    public interface ITransportServer : IDisposable
    {
        bool IsRunning { get; }
        event EventHandler<TransportSessionEventArgs> SessionConnected;
        event EventHandler<TransportSessionEventArgs> SessionClosed;
        event EventHandler<TransportDataReceivedEventArgs> DataReceived;

        Task StartAsync(CancellationToken cancellationToken);
        Task StopAsync(CancellationToken cancellationToken);
    }

    public sealed class TransportSessionEventArgs : EventArgs
    {
        public TransportSessionEventArgs(TcpTransportSession session)
        {
            Session = session;
        }

        public TcpTransportSession Session { get; private set; }
    }

    public sealed class TransportDataReceivedEventArgs : EventArgs
    {
        public TransportDataReceivedEventArgs(TcpTransportSession session, byte[] payload)
        {
            Session = session;
            Payload = payload;
        }

        public TcpTransportSession Session { get; private set; }
        public byte[] Payload { get; private set; }
    }
}
