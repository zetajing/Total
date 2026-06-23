using System;
using System.Net;
using System.Threading;
using System.Threading.Tasks;
using IndustrialCommSdk.Abstractions;
using IndustrialCommSdk.Diagnostics;
using IndustrialCommSdk.Internal;
using IndustrialCommSdk.Polling;
using IndustrialCommSdk.Transport;

namespace IndustrialCommSdk.Protocols.Socket
{
    public interface ISocketProtocolAdapter
    {
        Task<DataValue> ReadAsync(ITransportClient transportClient, ReadRequest request, CancellationToken cancellationToken);
        Task WriteAsync(ITransportClient transportClient, WriteRequest request, CancellationToken cancellationToken);
    }

    public sealed class SocketBridgeClientOptions
    {
        public string DeviceId { get; set; }
        public TcpTransportOptions Transport { get; set; }
    }

    public sealed class SocketBridgeClient : IndustrialClientBase
    {
        private readonly TcpTransportClient _transport;
        private readonly ISocketProtocolAdapter _adapter;

        public SocketBridgeClient(SocketBridgeClientOptions options, ISocketProtocolAdapter adapter, IIndustrialLogger logger = null, IPollingScheduler pollingScheduler = null)
            : base(options.DeviceId, ProtocolKind.TcpSocket, pollingScheduler ?? new PollingScheduler(logger), logger ?? NullIndustrialLogger.Instance)
        {
            if (options == null) throw new ArgumentNullException(nameof(options));
            _adapter = adapter ?? throw new ArgumentNullException(nameof(adapter));
            _transport = new TcpTransportClient(options.Transport);
        }

        public override bool IsConnected
        {
            get { return _transport.IsConnected; }
        }

        protected override Task ConnectCoreAsync(CancellationToken cancellationToken)
        {
            return _transport.ConnectAsync(cancellationToken);
        }

        protected override Task DisconnectCoreAsync(CancellationToken cancellationToken)
        {
            return _transport.DisconnectAsync(cancellationToken);
        }

        protected override Task<DataValue> ReadCoreAsync(ReadRequest request, CancellationToken cancellationToken)
        {
            return _adapter.ReadAsync(_transport, request, cancellationToken);
        }

        protected override Task WriteCoreAsync(WriteRequest request, CancellationToken cancellationToken)
        {
            return _adapter.WriteAsync(_transport, request, cancellationToken);
        }

        protected override void DisposeCore()
        {
            _transport.Dispose();
        }
    }

    public sealed class TcpSocketServerHost : IDisposable
    {
        private readonly TcpTransportServer _server;

        public TcpSocketServerHost(IPAddress address, int port)
        {
            _server = new TcpTransportServer(address, port);
        }

        public event EventHandler<TransportSessionEventArgs> SessionConnected
        {
            add { _server.SessionConnected += value; }
            remove { _server.SessionConnected -= value; }
        }

        public event EventHandler<TransportSessionEventArgs> SessionClosed
        {
            add { _server.SessionClosed += value; }
            remove { _server.SessionClosed -= value; }
        }

        public event EventHandler<TransportDataReceivedEventArgs> DataReceived
        {
            add { _server.DataReceived += value; }
            remove { _server.DataReceived -= value; }
        }

        public Task StartAsync(CancellationToken cancellationToken)
        {
            return _server.StartAsync(cancellationToken);
        }

        public Task StopAsync(CancellationToken cancellationToken)
        {
            return _server.StopAsync(cancellationToken);
        }

        public void Dispose()
        {
            _server.Dispose();
        }
    }
}
