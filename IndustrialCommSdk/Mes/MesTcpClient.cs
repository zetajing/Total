using System;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Diagnostics;
using IndustrialCommSdk.Diagnostics;
using IndustrialCommSdk.Exceptions;

namespace IndustrialCommSdk.Mes
{
    /// <summary>FA MES TCP 长连接客户端。连接、重连、接收循环均在后台执行。</summary>
    public sealed class MesTcpClient : IMesClient
    {
        private readonly MesClientOptions _options;
        private readonly IIndustrialLogger _logger;
        private readonly SemaphoreSlim _sendGate = new SemaphoreSlim(1, 1);
        private readonly object _lifecycleGate = new object();
        private CancellationTokenSource _runCancellation;
        private Task _runTask;
        private TcpClient _client;
        private NetworkStream _stream;
        private int _state = (int)MesConnectionState.Disconnected;
        private bool _disposed;

        public MesTcpClient(MesClientOptions options, IIndustrialLogger logger = null)
        {
            _options = options ?? throw new ArgumentNullException(nameof(options));
            ValidateOptions(options);
            _logger = logger ?? NullIndustrialLogger.Instance;
        }

        public bool IsConnected { get { return State == MesConnectionState.Connected && IsSocketConnected(); } }
        public MesConnectionState State { get { return (MesConnectionState)Volatile.Read(ref _state); } }

        public event EventHandler<MesConnectionStateChangedEventArgs> ConnectionStateChanged;
        public event EventHandler<MesMessageEventArgs<FaCheckMessage>> FaCheckReceived;
        public event EventHandler<MesMessageEventArgs<FaNumMessage>> FaNumReceived;
        public event EventHandler<MesRawMessageEventArgs> RawMessage;
        public event EventHandler<MesProtocolErrorEventArgs> ProtocolError;

        public async Task ConnectAsync(CancellationToken cancellationToken)
        {
            ThrowIfDisposed();
            _logger.Info(string.Format("MES CONNECT begin | Endpoint={0}:{1} | Timeout={2}ms | AutoReconnect={3}", _options.Host, _options.Port, _options.ConnectTimeoutMilliseconds, _options.AutoReconnect));
            TaskCompletionSource<bool> firstConnection;
            lock (_lifecycleGate)
            {
                if (_runTask != null && !_runTask.IsCompleted)
                {
                    if (IsConnected) return;
                    throw new InvalidOperationException("MES client is already connecting or reconnecting.");
                }
                _runCancellation = new CancellationTokenSource();
                firstConnection = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
                _runTask = RunAsync(firstConnection, _runCancellation.Token);
            }

            using (cancellationToken.Register(() => firstConnection.TrySetCanceled()))
                await firstConnection.Task.ConfigureAwait(false);
            _logger.Info("MES CONNECT completed | Endpoint=" + _options.Host + ":" + _options.Port);
        }

        public async Task DisconnectAsync(CancellationToken cancellationToken)
        {
            _logger.Info("MES DISCONNECT begin.");
            Task task;
            CancellationTokenSource cancellation;
            lock (_lifecycleGate)
            {
                task = _runTask;
                cancellation = _runCancellation;
                _runTask = null;
                _runCancellation = null;
            }
            if (cancellation != null) cancellation.Cancel();
            CloseSocket();
            if (task != null)
            {
                var completed = await Task.WhenAny(task, Task.Delay(Timeout.Infinite, cancellationToken)).ConfigureAwait(false);
                if (completed != task) cancellationToken.ThrowIfCancellationRequested();
                try { await task.ConfigureAwait(false); } catch (OperationCanceledException) { }
            }
            if (cancellation != null) cancellation.Dispose();
            ChangeState(MesConnectionState.Stopped, null);
            _logger.Info("MES DISCONNECT completed.");
        }

        public Task SendOnlineAsync(CancellationToken cancellationToken)
        {
            return SendTextAsync(MesProtocolCodec.CreateOnline(_options, DateTimeOffset.Now), cancellationToken);
        }

        public Task SendTrackAsync(FaTrackMessage message, CancellationToken cancellationToken)
        {
            if (message == null) throw new ArgumentNullException(nameof(message));
            if (message.Message == null) throw new ArgumentException("FATRACK message body is required.", nameof(message));
            ApplyHeader(message, "FATRACK");
            return SendTextAsync(MesProtocolCodec.Serialize(message), cancellationToken);
        }

        private async Task RunAsync(TaskCompletionSource<bool> firstConnection, CancellationToken cancellationToken)
        {
            var reconnectDelay = _options.InitialReconnectDelayMilliseconds;
            var firstAttempt = true;
            while (!cancellationToken.IsCancellationRequested)
            {
                try
                {
                    _logger.Info(string.Format("MES connection attempt | Endpoint={0}:{1} | Mode={2}", _options.Host, _options.Port, firstAttempt ? "initial" : "reconnect"));
                    ChangeState(firstAttempt ? MesConnectionState.Connecting : MesConnectionState.Reconnecting, null);
                    await OpenSocketAsync(cancellationToken).ConfigureAwait(false);
                    ChangeState(MesConnectionState.Connected, null);
                    reconnectDelay = _options.InitialReconnectDelayMilliseconds;
                    await SendOnlineAsync(cancellationToken).ConfigureAwait(false);
                    firstConnection.TrySetResult(true);
                    await ReceiveLoopAsync(cancellationToken).ConfigureAwait(false);
                    throw new IndustrialConnectionException("MES server closed the connection.");
                }
                catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
                {
                    break;
                }
                catch (Exception ex)
                {
                    CloseSocket();
                    _logger.Warn("MES connection failed: " + ex.Message);
                    ChangeState(MesConnectionState.Disconnected, ex.Message);
                    if (firstAttempt) firstConnection.TrySetException(ex);
                    if (!_options.AutoReconnect)
                    {
                        firstConnection.TrySetException(ex);
                        break;
                    }
                    firstAttempt = false;
                    _logger.Info("MES reconnect scheduled | Delay=" + reconnectDelay + "ms");
                    try { await Task.Delay(reconnectDelay, cancellationToken).ConfigureAwait(false); }
                    catch (OperationCanceledException) { break; }
                    reconnectDelay = Math.Min(reconnectDelay * 2, _options.MaximumReconnectDelayMilliseconds);
                }
            }
            CloseSocket();
            if (!firstConnection.Task.IsCompleted)
                firstConnection.TrySetCanceled();
        }

        private async Task OpenSocketAsync(CancellationToken cancellationToken)
        {
            CloseSocket();
            var stopwatch = Stopwatch.StartNew();
            var client = new TcpClient();
            var connectTask = client.ConnectAsync(_options.Host, _options.Port);
            var timeoutTask = Task.Delay(_options.ConnectTimeoutMilliseconds, cancellationToken);
            if (await Task.WhenAny(connectTask, timeoutTask).ConfigureAwait(false) != connectTask)
            {
                client.Close();
                cancellationToken.ThrowIfCancellationRequested();
                throw new IndustrialTimeoutException("MES connect timeout.");
            }
            await connectTask.ConfigureAwait(false);
            lock (_lifecycleGate)
            {
                if (cancellationToken.IsCancellationRequested) { client.Close(); cancellationToken.ThrowIfCancellationRequested(); }
                _client = client;
                _stream = client.GetStream();
            }
            _logger.Info("MES socket opened | Elapsed=" + stopwatch.ElapsedMilliseconds + "ms");
        }

        private async Task ReceiveLoopAsync(CancellationToken cancellationToken)
        {
            var bytes = new byte[8192];
            var chars = new char[Encoding.UTF8.GetMaxCharCount(bytes.Length)];
            var decoder = Encoding.UTF8.GetDecoder();
            var parser = new MesJsonFrameParser(_options.MaximumMessageCharacters);
            while (!cancellationToken.IsCancellationRequested)
            {
                var stream = GetConnectedStream();
                var read = await stream.ReadAsync(bytes, 0, bytes.Length, cancellationToken).ConfigureAwait(false);
                if (read == 0) return;
                _logger.Trace("MES RX chunk | Bytes=" + read);
                var charCount = decoder.GetChars(bytes, 0, read, chars, 0, false);
                foreach (var frame in parser.Append(new string(chars, 0, charCount)))
                    ProcessFrame(frame);
            }
        }

        private void ProcessFrame(string frame)
        {
            RaiseRaw(frame, false);
            try
            {
                var type = MesProtocolCodec.ReadType(frame);
                _logger.Info(string.Format("MES RX frame | Type={0} | Characters={1}", type ?? "(missing)", frame.Length));
                if (string.Equals(type, "FACHECK", StringComparison.OrdinalIgnoreCase))
                {
                    var message = MesProtocolCodec.Deserialize<FaCheckMessage>(frame);
                    Raise(FaCheckReceived, new MesMessageEventArgs<FaCheckMessage>(message, frame));
                }
                else if (string.Equals(type, "FANUM", StringComparison.OrdinalIgnoreCase))
                {
                    var message = MesProtocolCodec.Deserialize<FaNumMessage>(frame);
                    Raise(FaNumReceived, new MesMessageEventArgs<FaNumMessage>(message, frame));
                }
                else
                    RaiseProtocolError(frame, "Unsupported MES message type: " + (type ?? "(missing)"), null);
            }
            catch (Exception ex) { RaiseProtocolError(frame, "Invalid MES JSON message.", ex); }
        }

        private async Task SendTextAsync(string text, CancellationToken cancellationToken)
        {
            ThrowIfDisposed();
            var payload = Encoding.UTF8.GetBytes(text);
            var messageType = GetMessageTypeForLog(text);
            var stopwatch = Stopwatch.StartNew();
            _logger.Info(string.Format("MES TX begin | Type={0} | Bytes={1}", messageType, payload.Length));
            await _sendGate.WaitAsync(cancellationToken).ConfigureAwait(false);
            try
            {
                var stream = GetConnectedStream();
                var write = stream.WriteAsync(payload, 0, payload.Length, cancellationToken);
                if (await Task.WhenAny(write, Task.Delay(_options.SendTimeoutMilliseconds, cancellationToken)).ConfigureAwait(false) != write)
                {
                    CloseSocket();
                    cancellationToken.ThrowIfCancellationRequested();
                    throw new IndustrialTimeoutException("MES send timeout.");
                }
                await write.ConfigureAwait(false);
                await stream.FlushAsync(cancellationToken).ConfigureAwait(false);
                RaiseRaw(text, true);
                _logger.Info(string.Format("MES TX completed | Type={0} | Bytes={1} | Elapsed={2}ms", messageType, payload.Length, stopwatch.ElapsedMilliseconds));
            }
            finally { _sendGate.Release(); }
        }

        private void ApplyHeader(MesMessage message, string type)
        {
            message.Type = type;
            if (string.IsNullOrWhiteSpace(message.DeviceNo)) message.DeviceNo = _options.DeviceNo;
            if (string.IsNullOrWhiteSpace(message.DeviceName)) message.DeviceName = _options.DeviceName;
            if (string.IsNullOrWhiteSpace(message.DeviceIp)) message.DeviceIp = _options.DeviceIp;
            if (string.IsNullOrWhiteSpace(message.DeviceMac)) message.DeviceMac = _options.DeviceMac;
            if (string.IsNullOrWhiteSpace(message.DeviceTime)) message.DeviceTime = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss");
        }

        private static string GetMessageTypeForLog(string text)
        {
            if (string.IsNullOrWhiteSpace(text)) return "(empty)";
            if (text.StartsWith("START ", StringComparison.OrdinalIgnoreCase)) return "ONLINE";
            if (text[0] != '{') return "(non-json)";
            try { return MesProtocolCodec.ReadType(text) ?? "(missing)"; }
            catch { return "(invalid-json)"; }
        }

        private NetworkStream GetConnectedStream()
        {
            lock (_lifecycleGate)
            {
                if (_stream == null || !IsSocketConnected()) throw new IndustrialConnectionException("MES client is not connected.");
                return _stream;
            }
        }

        private bool IsSocketConnected()
        {
            var client = _client;
            if (client == null || !client.Connected) return false;
            try { return !(client.Client.Poll(1, SelectMode.SelectRead) && client.Client.Available == 0); }
            catch { return false; }
        }

        private void CloseSocket()
        {
            lock (_lifecycleGate)
            {
                if (_stream != null) { try { _stream.Dispose(); } catch { } _stream = null; }
                if (_client != null) { try { _client.Close(); } catch { } _client = null; }
            }
        }

        private void ChangeState(MesConnectionState state, string error)
        {
            Interlocked.Exchange(ref _state, (int)state);
            _logger.Info("MES state changed | State=" + state + (string.IsNullOrWhiteSpace(error) ? string.Empty : " | Error=" + error));
            Raise(ConnectionStateChanged, new MesConnectionStateChangedEventArgs(state, error));
        }

        private void RaiseRaw(string text, bool sent) { Raise(RawMessage, new MesRawMessageEventArgs(text, sent)); }
        private void RaiseProtocolError(string raw, string message, Exception exception)
        {
            _logger.Warn(message + (exception == null ? string.Empty : " " + exception.Message));
            Raise(ProtocolError, new MesProtocolErrorEventArgs(raw, message, exception));
        }

        private void Raise<T>(EventHandler<T> handler, T args) where T : EventArgs
        {
            if (handler == null) return;
            try { handler(this, args); }
            catch (Exception ex) { _logger.Error("MES event handler failed.", ex); }
        }

        private static void ValidateOptions(MesClientOptions options)
        {
            if (string.IsNullOrWhiteSpace(options.Host)) throw new ArgumentException("MES host is required.", nameof(options));
            if (options.Port < 1 || options.Port > 65535) throw new ArgumentOutOfRangeException(nameof(options));
            if (string.IsNullOrWhiteSpace(options.DeviceNo)) throw new ArgumentException("MES device number is required.", nameof(options));
            if (string.IsNullOrWhiteSpace(options.DeviceName)) throw new ArgumentException("MES device name is required.", nameof(options));
            if (string.IsNullOrWhiteSpace(options.DeviceIp)) throw new ArgumentException("MES device IP is required.", nameof(options));
            if (string.IsNullOrWhiteSpace(options.DeviceMac)) throw new ArgumentException("MES device MAC is required.", nameof(options));
            if (options.ConnectTimeoutMilliseconds <= 0 || options.SendTimeoutMilliseconds <= 0 ||
                options.InitialReconnectDelayMilliseconds <= 0 || options.MaximumReconnectDelayMilliseconds < options.InitialReconnectDelayMilliseconds ||
                options.MaximumMessageCharacters <= 0) throw new ArgumentOutOfRangeException(nameof(options), "MES timeout, reconnect and message-size values must be positive and consistent.");
        }

        private void ThrowIfDisposed() { if (_disposed) throw new ObjectDisposedException(nameof(MesTcpClient)); }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            var cancellation = _runCancellation;
            if (cancellation != null) cancellation.Cancel();
            CloseSocket();
            // 接收循环可能刚因关闭 Socket 而退出；不在这里释放发送门，避免与尾部 finally 竞争。
        }
    }
}
