using System;
using System.Linq;
using System.Net.WebSockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using IndustrialCommSdk.Web.Internal;

namespace IndustrialCommSdk.Web.WebSockets
{
    /// <summary>保留消息边界、支持分片重组和指数退避重连的 WebSocket 客户端。</summary>
    public sealed class IndustrialWebSocketClient : IWebSocketClient
    {
        private readonly IndustrialWebSocketClientOptions _options;
        private readonly SemaphoreSlim _lifecycleGate = new SemaphoreSlim(1, 1);
        private readonly CancellationTokenSource _lifetimeSource = new CancellationTokenSource();
        private ManagedWebSocket _connection;
        private int _explicitlyClosed;
        private int _reconnectRunning;
        private int _disposed;

        public IndustrialWebSocketClient(IndustrialWebSocketClientOptions options)
        {
            if (options == null) throw new ArgumentNullException(nameof(options));
            _options = options.Clone();
            ValidateOptions(_options);
        }

        public bool IsConnected
        {
            get
            {
                var connection = Volatile.Read(ref _connection);
                return connection != null && connection.State == WebSocketState.Open;
            }
        }

        public event EventHandler Connected;
        public event EventHandler<WebSocketMessageEventArgs> MessageReceived;
        public event EventHandler<WebSocketClosedEventArgs> Closed;

        public async Task ConnectAsync(CancellationToken cancellationToken)
        {
            ThrowIfDisposed();
            Interlocked.Exchange(ref _explicitlyClosed, 0);
            await ConnectCoreAsync(cancellationToken).ConfigureAwait(false);
        }

        public Task SendTextAsync(string text, CancellationToken cancellationToken)
        {
            return SendAsync(Encoding.UTF8.GetBytes(text ?? string.Empty), WebSocketMessageType.Text, cancellationToken);
        }

        public Task SendBinaryAsync(byte[] payload, CancellationToken cancellationToken)
        {
            return SendAsync(payload ?? throw new ArgumentNullException(nameof(payload)), WebSocketMessageType.Binary, cancellationToken);
        }

        private async Task SendAsync(byte[] payload, WebSocketMessageType type, CancellationToken cancellationToken)
        {
            ThrowIfDisposed();
            if (payload.Length > _options.MaxMessageBytes) throw new WebSocketMessageTooLargeException(_options.MaxMessageBytes);
            var connection = Volatile.Read(ref _connection);
            if (connection == null || connection.State != WebSocketState.Open)
                throw new InvalidOperationException("WebSocket client is not connected.");
            using (var sendSource = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken))
            {
                sendSource.CancelAfter(_options.SendTimeout);
                await connection.SendAsync(payload, type, sendSource.Token).ConfigureAwait(false);
            }
        }

        private async Task ConnectCoreAsync(CancellationToken cancellationToken)
        {
            ManagedWebSocket connected = null;
            await _lifecycleGate.WaitAsync(cancellationToken).ConfigureAwait(false);
            try
            {
                ThrowIfDisposed();
                var existing = _connection;
                if (existing != null && existing.State == WebSocketState.Open) return;

                var socket = new ClientWebSocket();
                try
                {
                    socket.Options.KeepAliveInterval = _options.KeepAliveInterval;
                    if (!string.IsNullOrWhiteSpace(_options.ApiKey))
                        socket.Options.SetRequestHeader(WebSecurity.ApiKeyHeaderName, _options.ApiKey);
                    if (!string.IsNullOrWhiteSpace(_options.Origin))
                        socket.Options.SetRequestHeader("Origin", _options.Origin);
                    foreach (var header in _options.Headers)
                    {
                        if (string.IsNullOrWhiteSpace(header.Key)) continue;
                        if (string.Equals(header.Key, WebSecurity.ApiKeyHeaderName, StringComparison.OrdinalIgnoreCase) ||
                            string.Equals(header.Key, "Origin", StringComparison.OrdinalIgnoreCase)) continue;
                        socket.Options.SetRequestHeader(header.Key, header.Value);
                    }
                    foreach (var protocol in _options.SubProtocols.Where(value => !string.IsNullOrWhiteSpace(value)))
                        socket.Options.AddSubProtocol(protocol);

                    using (var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, _lifetimeSource.Token))
                    {
                        linked.CancelAfter(_options.ConnectTimeout);
                        await socket.ConnectAsync(_options.Uri, linked.Token).ConfigureAwait(false);
                    }

                    connected = new ManagedWebSocket(socket, _options.MaxMessageBytes, _options.ReceiveBufferBytes);
                    var old = Interlocked.Exchange(ref _connection, connected);
                    old?.Dispose();
                }
                catch
                {
                    socket.Dispose();
                    throw;
                }
            }
            finally { _lifecycleGate.Release(); }

            if (connected != null && ReferenceEquals(Volatile.Read(ref _connection), connected) && connected.State == WebSocketState.Open)
            {
                RaiseConnected();
                if (ReferenceEquals(Volatile.Read(ref _connection), connected))
                    _ = ReceiveLoopAsync(connected, _lifetimeSource.Token);
            }
        }

        private async Task ReceiveLoopAsync(ManagedWebSocket connection, CancellationToken cancellationToken)
        {
            Exception failure = null;
            WebSocketCloseStatus? status = null;
            string description = null;
            try
            {
                while (!cancellationToken.IsCancellationRequested && connection.State == WebSocketState.Open)
                {
                    var message = await connection.ReceiveAsync(cancellationToken).ConfigureAwait(false);
                    if (message.IsClose)
                    {
                        status = message.CloseStatus;
                        description = message.CloseDescription;
                        await CloseWithTimeoutAsync(connection, status ?? WebSocketCloseStatus.NormalClosure, description, CancellationToken.None).ConfigureAwait(false);
                        break;
                    }
                    RaiseMessage(new WebSocketMessageEventArgs(null, message.MessageType, message.Payload));
                }
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { }
            catch (WebSocketMessageTooLargeException ex)
            {
                failure = ex;
                status = WebSocketCloseStatus.MessageTooBig;
                description = "Message exceeds configured limit.";
                await CloseWithTimeoutAsync(connection, status.Value, description, CancellationToken.None).ConfigureAwait(false);
            }
            catch (Exception ex) { failure = ex; }
            finally
            {
                Interlocked.CompareExchange(ref _connection, null, connection);
                connection.Dispose();
                RaiseClosed(new WebSocketClosedEventArgs(null, status, description, failure));
                if (Volatile.Read(ref _explicitlyClosed) == 0 && Volatile.Read(ref _disposed) == 0 && _options.AutoReconnect)
                    StartReconnectLoop();
            }
        }

        private void StartReconnectLoop()
        {
            if (Interlocked.CompareExchange(ref _reconnectRunning, 1, 0) != 0) return;
            _ = ReconnectLoopAsync();
        }

        private async Task ReconnectLoopAsync()
        {
            try
            {
                var delay = _options.InitialReconnectDelay;
                while (Volatile.Read(ref _explicitlyClosed) == 0 && Volatile.Read(ref _disposed) == 0 && !IsConnected)
                {
                    try
                    {
                        await Task.Delay(delay, _lifetimeSource.Token).ConfigureAwait(false);
                        if (Volatile.Read(ref _explicitlyClosed) != 0 || Volatile.Read(ref _disposed) != 0) return;
                        await ConnectCoreAsync(_lifetimeSource.Token).ConfigureAwait(false);
                        return;
                    }
                    catch (OperationCanceledException) when (_lifetimeSource.IsCancellationRequested) { return; }
                    catch
                    {
                        var nextTicks = Math.Min(delay.Ticks * 2, _options.MaxReconnectDelay.Ticks);
                        delay = TimeSpan.FromTicks(nextTicks);
                    }
                }
            }
            finally
            {
                Interlocked.Exchange(ref _reconnectRunning, 0);
                if (Volatile.Read(ref _explicitlyClosed) == 0 && Volatile.Read(ref _disposed) == 0 && !IsConnected)
                    StartReconnectLoop();
            }
        }

        public async Task CloseAsync(CancellationToken cancellationToken)
        {
            Interlocked.Exchange(ref _explicitlyClosed, 1);
            await _lifecycleGate.WaitAsync(cancellationToken).ConfigureAwait(false);
            try
            {
                var connection = Interlocked.Exchange(ref _connection, null);
                if (connection == null) return;
                try { await CloseWithTimeoutAsync(connection, WebSocketCloseStatus.NormalClosure, "Client closing.", cancellationToken).ConfigureAwait(false); }
                finally { connection.Dispose(); }
            }
            finally { _lifecycleGate.Release(); }
        }

        private void RaiseConnected() { try { Connected?.Invoke(this, EventArgs.Empty); } catch { } }
        private void RaiseMessage(WebSocketMessageEventArgs args) { try { MessageReceived?.Invoke(this, args); } catch { } }
        private void RaiseClosed(WebSocketClosedEventArgs args) { try { Closed?.Invoke(this, args); } catch { } }

        private async Task CloseWithTimeoutAsync(
            ManagedWebSocket connection,
            WebSocketCloseStatus status,
            string description,
            CancellationToken cancellationToken)
        {
            using (var closeSource = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken))
            {
                closeSource.CancelAfter(_options.CloseHandshakeTimeout);
                try { await connection.CloseAsync(status, description, closeSource.Token).ConfigureAwait(false); }
                catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested) { }
            }
        }

        private static void ValidateOptions(IndustrialWebSocketClientOptions options)
        {
            if (options.Uri == null || !options.Uri.IsAbsoluteUri ||
                (options.Uri.Scheme != "ws" && options.Uri.Scheme != "wss"))
                throw new ArgumentException("WebSocket client URI must be absolute ws:// or wss://.", nameof(options));
            if (!WebSecurity.IsLoopbackHost(options.Uri) && options.Uri.Scheme != "wss")
                throw new ArgumentException("Non-loopback WebSocket connections must use WSS.", nameof(options));
            if (options.MaxMessageBytes <= 0 || options.ReceiveBufferBytes <= 0)
                throw new ArgumentOutOfRangeException(nameof(options));
            if (options.KeepAliveInterval <= TimeSpan.Zero || options.ConnectTimeout <= TimeSpan.Zero ||
                options.CloseHandshakeTimeout <= TimeSpan.Zero || options.SendTimeout <= TimeSpan.Zero || options.InitialReconnectDelay <= TimeSpan.Zero ||
                options.MaxReconnectDelay < options.InitialReconnectDelay)
                throw new ArgumentOutOfRangeException(nameof(options));
        }

        private void ThrowIfDisposed()
        {
            if (Volatile.Read(ref _disposed) != 0) throw new ObjectDisposedException(nameof(IndustrialWebSocketClient));
        }

        public void Dispose()
        {
            if (Interlocked.Exchange(ref _disposed, 1) != 0) return;
            Interlocked.Exchange(ref _explicitlyClosed, 1);
            _lifetimeSource.Cancel();
            var connection = Interlocked.Exchange(ref _connection, null);
            connection?.Dispose();
            _lifetimeSource.Dispose();
        }
    }
}
