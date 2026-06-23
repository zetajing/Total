using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace IndustrialCommDemo.SocketDebug
{
    internal sealed class LineBasedTcpServer : IDisposable
    {
        private readonly ConcurrentDictionary<Guid, ServerSession> _sessions = new ConcurrentDictionary<Guid, ServerSession>();
        private TcpListener _listener;
        private CancellationTokenSource _cts;
        private Task _acceptLoopTask;

        public bool IsRunning { get; private set; }

        public int SessionCount
        {
            get { return _sessions.Count; }
        }

        public event EventHandler<SocketSessionEventArgs> ClientConnected;
        public event EventHandler<SocketSessionEventArgs> ClientDisconnected;
        public event EventHandler<SocketTextMessageEventArgs> MessageReceived;

        public Task StartAsync(IPAddress address, int port, CancellationToken cancellationToken)
        {
            if (IsRunning)
            {
                return Task.CompletedTask;
            }

            _cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            _listener = new TcpListener(address, port);
            _listener.Start();
            IsRunning = true;
            _acceptLoopTask = Task.Run(() => AcceptLoopAsync(_cts.Token), _cts.Token);
            return Task.CompletedTask;
        }

        public async Task StopAsync(CancellationToken cancellationToken)
        {
            if (!IsRunning)
            {
                return;
            }

            IsRunning = false;

            var linkedSource = _cts;
            _cts = null;

            if (linkedSource != null)
            {
                linkedSource.Cancel();
            }

            try
            {
                _listener.Stop();
            }
            catch
            {
            }

            var sessions = new List<ServerSession>(_sessions.Values);
            foreach (var session in sessions)
            {
                session.Dispose();
            }

            _sessions.Clear();

            if (_acceptLoopTask != null)
            {
                try
                {
                    await _acceptLoopTask.ConfigureAwait(false);
                }
                catch
                {
                }
                finally
                {
                    _acceptLoopTask = null;
                }
            }

            if (linkedSource != null)
            {
                linkedSource.Dispose();
            }

            _listener = null;
        }

        public async Task BroadcastAsync(string message, CancellationToken cancellationToken)
        {
            if (string.IsNullOrWhiteSpace(message) || _sessions.IsEmpty)
            {
                return;
            }

            var payload = EncodeLine(message);
            var tasks = new List<Task>();
            foreach (var session in _sessions.Values)
            {
                tasks.Add(session.SendAsync(payload, cancellationToken));
            }

            await Task.WhenAll(tasks).ConfigureAwait(false);
        }

        public Task SendToAsync(Guid sessionId, string message, CancellationToken cancellationToken)
        {
            ServerSession session;
            if (!_sessions.TryGetValue(sessionId, out session) || string.IsNullOrWhiteSpace(message))
            {
                return Task.CompletedTask;
            }

            return session.SendAsync(EncodeLine(message), cancellationToken);
        }

        public void Dispose()
        {
            StopAsync(CancellationToken.None).GetAwaiter().GetResult();
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
                catch (SocketException)
                {
                    if (cancellationToken.IsCancellationRequested)
                    {
                        break;
                    }

                    continue;
                }

                var session = new ServerSession(client);
                if (_sessions.TryAdd(session.SessionId, session))
                {
                    ClientConnected?.Invoke(this, new SocketSessionEventArgs(session.SessionId, session.RemoteEndPoint, SessionCount));
                    _ = Task.Run(() => RunSessionAsync(session, cancellationToken), cancellationToken);
                }
                else
                {
                    session.Dispose();
                }
            }
        }

        private async Task RunSessionAsync(ServerSession session, CancellationToken cancellationToken)
        {
            try
            {
                await session.ReceiveLoopAsync(
                    (currentSession, message) => MessageReceived?.Invoke(this, new SocketTextMessageEventArgs(currentSession.SessionId, currentSession.RemoteEndPoint, message)),
                    cancellationToken).ConfigureAwait(false);
            }
            finally
            {
                ServerSession removed;
                _sessions.TryRemove(session.SessionId, out removed);
                session.Dispose();
                ClientDisconnected?.Invoke(this, new SocketSessionEventArgs(session.SessionId, session.RemoteEndPoint, SessionCount));
            }
        }

        private static byte[] EncodeLine(string message)
        {
            return Encoding.UTF8.GetBytes((message ?? string.Empty) + "\r\n");
        }

        private sealed class ServerSession : IDisposable
        {
            private readonly TcpClient _client;
            private readonly NetworkStream _stream;
            private readonly SemaphoreSlim _sendLock = new SemaphoreSlim(1, 1);
            private readonly List<byte> _pendingBytes = new List<byte>();

            public ServerSession(TcpClient client)
            {
                _client = client ?? throw new ArgumentNullException(nameof(client));
                _client.NoDelay = true;
                _stream = _client.GetStream();
                SessionId = Guid.NewGuid();
            }

            public Guid SessionId { get; private set; }

            public string RemoteEndPoint
            {
                get { return _client.Client.RemoteEndPoint == null ? "(unknown)" : _client.Client.RemoteEndPoint.ToString(); }
            }

            public async Task ReceiveLoopAsync(Action<ServerSession, string> onMessage, CancellationToken cancellationToken)
            {
                var buffer = new byte[4096];
                while (!cancellationToken.IsCancellationRequested)
                {
                    int read;
                    try
                    {
                        read = await _stream.ReadAsync(buffer, 0, buffer.Length, cancellationToken).ConfigureAwait(false);
                    }
                    catch
                    {
                        break;
                    }

                    if (read <= 0)
                    {
                        break;
                    }

                    AppendAndDispatch(buffer, read, onMessage);
                }
            }

            public async Task SendAsync(byte[] payload, CancellationToken cancellationToken)
            {
                await _sendLock.WaitAsync(cancellationToken).ConfigureAwait(false);
                try
                {
                    await _stream.WriteAsync(payload, 0, payload.Length, cancellationToken).ConfigureAwait(false);
                    await _stream.FlushAsync(cancellationToken).ConfigureAwait(false);
                }
                finally
                {
                    _sendLock.Release();
                }
            }

            public void Dispose()
            {
                try
                {
                    _stream.Dispose();
                }
                catch
                {
                }

                try
                {
                    _client.Close();
                }
                catch
                {
                }

                _sendLock.Dispose();
            }

            private void AppendAndDispatch(byte[] buffer, int count, Action<ServerSession, string> onMessage)
            {
                for (var index = 0; index < count; index++)
                {
                    _pendingBytes.Add(buffer[index]);
                }

                while (true)
                {
                    var lineEndIndex = FindLineEnding(_pendingBytes);
                    if (lineEndIndex < 0)
                    {
                        return;
                    }

                    var lineBytes = _pendingBytes.GetRange(0, lineEndIndex).ToArray();
                    _pendingBytes.RemoveRange(0, lineEndIndex + 2);
                    onMessage?.Invoke(this, Encoding.UTF8.GetString(lineBytes));
                }
            }

            private static int FindLineEnding(List<byte> buffer)
            {
                for (var index = 0; index < buffer.Count - 1; index++)
                {
                    if (buffer[index] == '\r' && buffer[index + 1] == '\n')
                    {
                        return index;
                    }
                }

                return -1;
            }
        }
    }
}
