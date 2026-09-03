using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.WebSockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using InduLink.Diagnostics;
using InduLink.Web.Internal;

namespace InduLink.Web.WebSockets
{
    /// <summary>基于 HTTP.sys 的独立 WebSocket 服务端。</summary>
    public sealed class IndustrialWebSocketServer : IWebSocketServer
    {
        private readonly IndustrialWebSocketServerOptions _options;
        private readonly IIndustrialLogger _logger;
        private readonly ConcurrentDictionary<string, ServerSession> _sessions = new ConcurrentDictionary<string, ServerSession>();
        private readonly ConcurrentDictionary<int, Task> _requests = new ConcurrentDictionary<int, Task>();
        private readonly SemaphoreSlim _lifecycleGate = new SemaphoreSlim(1, 1);
        private HttpListener _listener;
        private CancellationTokenSource _stopSource;
        private Task _acceptTask;
        private int _nextRequestId;
        private int _admittedSessions;
        private int _running;
        private int _disposed;

        public IndustrialWebSocketServer(IndustrialWebSocketServerOptions options, IIndustrialLogger logger = null)
        {
            if (options == null) throw new ArgumentNullException(nameof(options));
            _options = options.Clone();
            _logger = logger ?? NullIndustrialLogger.Instance;
            ValidateOptions(_options);
        }

        public bool IsRunning { get { return Volatile.Read(ref _running) != 0; } }
        public IReadOnlyCollection<WebSocketSessionInfo> Sessions { get { return _sessions.Values.Select(value => value.Info).ToArray(); } }
        public event EventHandler<WebSocketSessionEventArgs> SessionConnected;
        public event EventHandler<WebSocketMessageEventArgs> MessageReceived;
        public event EventHandler<WebSocketClosedEventArgs> SessionClosed;

        public async Task StartAsync(CancellationToken cancellationToken)
        {
            ThrowIfDisposed();
            await _lifecycleGate.WaitAsync(cancellationToken).ConfigureAwait(false);
            try
            {
                ThrowIfDisposed();
                if (IsRunning) return;
                ValidateOptions(_options);
                var listener = new HttpListener();
                listener.Prefixes.Add(_options.ListenPrefix);
                try { listener.Start(); }
                catch { listener.Close(); throw; }
                _listener = listener;
                _stopSource = new CancellationTokenSource();
                Volatile.Write(ref _running, 1);
                _acceptTask = AcceptLoopAsync(listener, _stopSource.Token);
                _logger.Info("WebSocket server started | Prefix=" + _options.ListenPrefix + " Path=" + _options.WebSocketPath);
            }
            finally { _lifecycleGate.Release(); }
        }

        public async Task StopAsync(CancellationToken cancellationToken)
        {
            await _lifecycleGate.WaitAsync(cancellationToken).ConfigureAwait(false);
            Task acceptTask;
            CancellationTokenSource source;
            try
            {
                if (!IsRunning) return;
                Volatile.Write(ref _running, 0);
                source = _stopSource;
                acceptTask = _acceptTask;
                _stopSource = null;
                _acceptTask = null;
                try { _listener?.Close(); } catch { }
                _listener = null;
                source.Cancel();
            }
            finally { _lifecycleGate.Release(); }

            using (var shutdownSource = new CancellationTokenSource(_options.ShutdownTimeout))
            {
                var closeTasks = _sessions.Values.Select(session => CloseSessionCoreAsync(session, WebSocketCloseStatus.EndpointUnavailable, "Server stopping.", shutdownSource.Token)).ToArray();
                if (closeTasks.Length > 0)
                {
                    try { await Task.WhenAll(closeTasks).ConfigureAwait(false); } catch { }
                }
            }
            if (acceptTask != null)
            {
                await Task.WhenAny(acceptTask, Task.Delay(_options.ShutdownTimeout, CancellationToken.None)).ConfigureAwait(false);
            }
            var active = _requests.Values.ToArray();
            if (active.Length > 0)
            {
                var allActive = Task.WhenAll(active);
                await Task.WhenAny(allActive, Task.Delay(_options.ShutdownTimeout, CancellationToken.None)).ConfigureAwait(false);
            }
            source.Dispose();
            _logger.Info("WebSocket server stopped.");
        }

        private async Task AcceptLoopAsync(HttpListener listener, CancellationToken cancellationToken)
        {
            while (!cancellationToken.IsCancellationRequested)
            {
                HttpListenerContext context;
                try { context = await listener.GetContextAsync().ConfigureAwait(false); }
                catch (ObjectDisposedException) { break; }
                catch (HttpListenerException) when (cancellationToken.IsCancellationRequested) { break; }
                catch (Exception ex)
                {
                    if (cancellationToken.IsCancellationRequested) break;
                    _logger.Error("WebSocket server accept failed.", ex);
                    continue;
                }
                var requestId = Interlocked.Increment(ref _nextRequestId);
                var task = HandleContextAsync(context, cancellationToken);
                _requests[requestId] = task;
                _ = task.ContinueWith(
                    completed => { Task ignored; _requests.TryRemove(requestId, out ignored); },
                    CancellationToken.None,
                    TaskContinuationOptions.ExecuteSynchronously,
                    TaskScheduler.Default);
            }
        }

        private async Task HandleContextAsync(HttpListenerContext context, CancellationToken cancellationToken)
        {
            if (!string.Equals(NormalizePath(context.Request.Url.AbsolutePath), NormalizePath(_options.WebSocketPath), StringComparison.OrdinalIgnoreCase))
            {
                Reject(context, 404, "Not Found");
                return;
            }
            if (!context.Request.IsWebSocketRequest)
            {
                Reject(context, 400, "WebSocket upgrade required");
                return;
            }
            if (_options.RequireApiKey && !WebSecurity.FixedTimeEquals(_options.ApiKey, context.Request.Headers[WebSecurity.ApiKeyHeaderName]))
            {
                Reject(context, 401, "Unauthorized");
                return;
            }
            if (!WebSecurity.IsRequestOriginAllowed(
                context.Request.Headers["Origin"],
                _options.RequireApiKey,
                _options.AllowedOrigins.ToArray()))
            {
                Reject(context, 403, "Origin forbidden");
                return;
            }
            var selectedSubProtocol = SelectSubProtocol(context.Request.Headers["Sec-WebSocket-Protocol"]);
            if (HasConfiguredSubProtocols() && selectedSubProtocol == null)
            {
                Reject(context, 400, "A supported WebSocket subprotocol is required");
                return;
            }
            if (Interlocked.Increment(ref _admittedSessions) > _options.MaxSessions)
            {
                Interlocked.Decrement(ref _admittedSessions);
                Reject(context, 429, "Too Many Sessions");
                return;
            }

            var sessionOwnsAdmission = false;
            try
            {
                var accepted = await context.AcceptWebSocketAsync(selectedSubProtocol).ConfigureAwait(false);
                var id = Guid.NewGuid().ToString("N");
                var remote = context.Request.RemoteEndPoint == null ? null : context.Request.RemoteEndPoint.ToString();
                var session = new ServerSession(
                    new WebSocketSessionInfo(id, remote, DateTimeOffset.UtcNow),
                    new ManagedWebSocket(accepted.WebSocket, _options.MaxMessageBytes, _options.ReceiveBufferBytes));
                if (!_sessions.TryAdd(id, session))
                {
                    session.Connection.Dispose();
                    return;
                }
                sessionOwnsAdmission = true;
                RaiseSessionConnected(session.Info);
                await ReceiveSessionAsync(session, cancellationToken).ConfigureAwait(false);
            }
            catch (Exception ex) { _logger.Error("WebSocket upgrade or session failed.", ex); }
            finally
            {
                if (!sessionOwnsAdmission) Interlocked.Decrement(ref _admittedSessions);
            }
        }

        private async Task ReceiveSessionAsync(ServerSession session, CancellationToken cancellationToken)
        {
            Exception failure = null;
            WebSocketCloseStatus? status = null;
            string description = null;
            try
            {
                while (!cancellationToken.IsCancellationRequested && session.Connection.State == WebSocketState.Open)
                {
                    var message = await session.Connection.ReceiveAsync(cancellationToken).ConfigureAwait(false);
                    if (message.IsClose)
                    {
                        status = message.CloseStatus;
                        description = message.CloseDescription;
                        break;
                    }
                    RaiseMessage(new WebSocketMessageEventArgs(session.Info.Id, message.MessageType, message.Payload));
                }
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { }
            catch (WebSocketMessageTooLargeException ex)
            {
                failure = ex;
                status = WebSocketCloseStatus.MessageTooBig;
                description = "Message exceeds configured limit.";
            }
            catch (Exception ex) { failure = ex; }
            finally
            {
                await CloseSessionCoreAsync(session, status ?? WebSocketCloseStatus.NormalClosure, description ?? "Session closed.", CancellationToken.None).ConfigureAwait(false);
                RaiseSessionClosed(new WebSocketClosedEventArgs(session.Info.Id, status, description, failure));
            }
        }

        public Task SendTextAsync(string sessionId, string text, CancellationToken cancellationToken)
        {
            return SendAsync(sessionId, Encoding.UTF8.GetBytes(text ?? string.Empty), WebSocketMessageType.Text, cancellationToken);
        }

        public Task SendBinaryAsync(string sessionId, byte[] payload, CancellationToken cancellationToken)
        {
            return SendAsync(sessionId, payload ?? throw new ArgumentNullException(nameof(payload)), WebSocketMessageType.Binary, cancellationToken);
        }

        private Task SendAsync(string sessionId, byte[] payload, WebSocketMessageType type, CancellationToken cancellationToken)
        {
            if (string.IsNullOrWhiteSpace(sessionId)) throw new ArgumentException("Session ID cannot be empty.", nameof(sessionId));
            if (payload.Length > _options.MaxMessageBytes) throw new WebSocketMessageTooLargeException(_options.MaxMessageBytes);
            ServerSession session;
            if (!_sessions.TryGetValue(sessionId, out session)) throw new KeyNotFoundException("WebSocket session was not found: " + sessionId);
            return SendWithTimeoutAsync(session.Connection, payload, type, cancellationToken);
        }

        public Task BroadcastTextAsync(string text, CancellationToken cancellationToken)
        {
            return BroadcastAsync(Encoding.UTF8.GetBytes(text ?? string.Empty), WebSocketMessageType.Text, cancellationToken);
        }

        public Task BroadcastBinaryAsync(byte[] payload, CancellationToken cancellationToken)
        {
            return BroadcastAsync(payload ?? throw new ArgumentNullException(nameof(payload)), WebSocketMessageType.Binary, cancellationToken);
        }

        private async Task BroadcastAsync(byte[] payload, WebSocketMessageType type, CancellationToken cancellationToken)
        {
            if (payload.Length > _options.MaxMessageBytes) throw new WebSocketMessageTooLargeException(_options.MaxMessageBytes);
            var tasks = _sessions.Values.Select(session => SendWithTimeoutAsync(session.Connection, payload, type, cancellationToken)).ToArray();
            if (tasks.Length > 0) await Task.WhenAll(tasks).ConfigureAwait(false);
        }

        public Task CloseSessionAsync(string sessionId, CancellationToken cancellationToken)
        {
            if (string.IsNullOrWhiteSpace(sessionId)) throw new ArgumentException("Session ID cannot be empty.", nameof(sessionId));
            ServerSession session;
            if (!_sessions.TryGetValue(sessionId, out session)) throw new KeyNotFoundException("WebSocket session was not found: " + sessionId);
            return CloseSessionCoreAsync(session, WebSocketCloseStatus.NormalClosure, "Session closed by server.", cancellationToken);
        }

        private async Task CloseSessionCoreAsync(ServerSession session, WebSocketCloseStatus status, string description, CancellationToken cancellationToken)
        {
            ServerSession removed;
            if (!_sessions.TryRemove(session.Info.Id, out removed)) return;
            Interlocked.Decrement(ref _admittedSessions);
            try
            {
                using (var closeSource = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken))
                {
                    closeSource.CancelAfter(_options.CloseHandshakeTimeout);
                    try { await removed.Connection.CloseAsync(status, description, closeSource.Token).ConfigureAwait(false); }
                    catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested) { }
                }
            }
            finally { removed.Connection.Dispose(); }
        }

        private async Task SendWithTimeoutAsync(ManagedWebSocket connection, byte[] payload, WebSocketMessageType type, CancellationToken cancellationToken)
        {
            using (var sendSource = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken))
            {
                sendSource.CancelAfter(_options.SendTimeout);
                await connection.SendAsync(payload, type, sendSource.Token).ConfigureAwait(false);
            }
        }

        private bool HasConfiguredSubProtocols()
        {
            return !string.IsNullOrWhiteSpace(_options.SubProtocol) || _options.SubProtocols.Any(value => !string.IsNullOrWhiteSpace(value));
        }

        private string SelectSubProtocol(string requestedHeader)
        {
            if (!HasConfiguredSubProtocols()) return null;
            var supported = _options.SubProtocols.Where(value => !string.IsNullOrWhiteSpace(value)).ToList();
            if (!string.IsNullOrWhiteSpace(_options.SubProtocol)) supported.Insert(0, _options.SubProtocol);
            var requested = (requestedHeader ?? string.Empty).Split(',').Select(value => value.Trim());
            return supported.FirstOrDefault(candidate => requested.Any(value => string.Equals(value, candidate, StringComparison.OrdinalIgnoreCase)));
        }

        private static string NormalizePath(string value) { return "/" + (value ?? string.Empty).Trim('/'); }

        private static void Reject(HttpListenerContext context, int statusCode, string description)
        {
            try
            {
                var bytes = Encoding.UTF8.GetBytes(description ?? string.Empty);
                context.Response.StatusCode = statusCode;
                context.Response.ContentType = "text/plain; charset=utf-8";
                context.Response.ContentLength64 = bytes.Length;
                context.Response.OutputStream.Write(bytes, 0, bytes.Length);
                context.Response.Close();
            }
            catch { try { context.Response.Abort(); } catch { } }
        }

        private void RaiseSessionConnected(WebSocketSessionInfo info) { try { SessionConnected?.Invoke(this, new WebSocketSessionEventArgs(info)); } catch { } }
        private void RaiseMessage(WebSocketMessageEventArgs args) { try { MessageReceived?.Invoke(this, args); } catch { } }
        private void RaiseSessionClosed(WebSocketClosedEventArgs args) { try { SessionClosed?.Invoke(this, args); } catch { } }

        private static void ValidateOptions(IndustrialWebSocketServerOptions options)
        {
            WebSecurity.ValidateListenerSecurity(options.ListenPrefix, options.RequireApiKey, options.ApiKey, options.AllowedOrigins.ToArray(), nameof(options));
            if (string.IsNullOrWhiteSpace(options.WebSocketPath) || !options.WebSocketPath.StartsWith("/", StringComparison.Ordinal))
                throw new ArgumentException("WebSocketPath must start with '/'.", nameof(options));
            if (options.MaxMessageBytes <= 0 || options.ReceiveBufferBytes <= 0 || options.MaxSessions <= 0)
                throw new ArgumentOutOfRangeException(nameof(options));
            if (options.ShutdownTimeout <= TimeSpan.Zero || options.CloseHandshakeTimeout <= TimeSpan.Zero || options.SendTimeout <= TimeSpan.Zero)
                throw new ArgumentOutOfRangeException(nameof(options));
        }

        private void ThrowIfDisposed()
        {
            if (Volatile.Read(ref _disposed) != 0) throw new ObjectDisposedException(nameof(IndustrialWebSocketServer));
        }

        public void Dispose()
        {
            if (Interlocked.Exchange(ref _disposed, 1) != 0) return;
            StopAsync(CancellationToken.None).GetAwaiter().GetResult();
        }

        private sealed class ServerSession
        {
            internal ServerSession(WebSocketSessionInfo info, ManagedWebSocket connection)
            {
                Info = info;
                Connection = connection;
            }
            internal WebSocketSessionInfo Info { get; private set; }
            internal ManagedWebSocket Connection { get; private set; }
        }
    }
}
