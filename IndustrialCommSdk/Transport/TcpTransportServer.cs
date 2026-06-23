using System;
using System.Collections.Concurrent;
using System.Net;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;

namespace IndustrialCommSdk.Transport
{
    public sealed class TcpTransportServer : ITransportServer
    {
        private readonly IPAddress _address;
        private readonly int _port;
        private readonly ConcurrentDictionary<Guid, TcpTransportSession> _sessions = new ConcurrentDictionary<Guid, TcpTransportSession>();
        private TcpListener _listener;
        private CancellationTokenSource _cts;

        public TcpTransportServer(IPAddress address, int port)
        {
            _address = address ?? IPAddress.Any;
            _port = port;
        }

        public bool IsRunning { get; private set; }

        public event EventHandler<TransportSessionEventArgs> SessionConnected;
        public event EventHandler<TransportSessionEventArgs> SessionClosed;
        public event EventHandler<TransportDataReceivedEventArgs> DataReceived;

        public Task StartAsync(CancellationToken cancellationToken)
        {
            if (IsRunning)
            {
                return Task.CompletedTask;
            }

            _cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            _listener = new TcpListener(_address, _port);
            _listener.Start();
            IsRunning = true;
            Task.Run(() => AcceptLoopAsync(_cts.Token), _cts.Token);
            return Task.CompletedTask;
        }

        public Task StopAsync(CancellationToken cancellationToken)
        {
            if (!IsRunning)
            {
                return Task.CompletedTask;
            }

            _cts.Cancel();
            try
            {
                _listener.Stop();
            }
            catch
            {
            }

            foreach (var session in _sessions.Values)
            {
                session.Dispose();
            }
            _sessions.Clear();
            IsRunning = false;
            return Task.CompletedTask;
        }

        private async Task AcceptLoopAsync(CancellationToken cancellationToken)
        {
            while (!cancellationToken.IsCancellationRequested)
            {
                TcpClient client = null;
                try
                {
                    client = await _listener.AcceptTcpClientAsync().ConfigureAwait(false);
                }
                catch (ObjectDisposedException)
                {
                    break;
                }
                catch (InvalidOperationException)
                {
                    break;
                }
                catch
                {
                    if (cancellationToken.IsCancellationRequested)
                    {
                        break;
                    }
                    continue;
                }

                var session = new TcpTransportSession(client);
                if (_sessions.TryAdd(session.SessionId, session))
                {
                    session.DataReceived += OnSessionDataReceived;
                    session.Closed += OnSessionClosed;
                    SessionConnected?.Invoke(this, new TransportSessionEventArgs(session));
                    session.Start();
                }
            }
        }

        private void OnSessionClosed(object sender, EventArgs e)
        {
            var session = (TcpTransportSession)sender;
            TcpTransportSession removed;
            _sessions.TryRemove(session.SessionId, out removed);
            SessionClosed?.Invoke(this, new TransportSessionEventArgs(session));
        }

        private void OnSessionDataReceived(object sender, byte[] payload)
        {
            var session = (TcpTransportSession)sender;
            DataReceived?.Invoke(this, new TransportDataReceivedEventArgs(session, payload));
        }

        public void Dispose()
        {
            StopAsync(CancellationToken.None).GetAwaiter().GetResult();
            _cts?.Dispose();
        }
    }
}
