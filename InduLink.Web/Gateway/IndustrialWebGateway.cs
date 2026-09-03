using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.WebSockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using InduLink.Abstractions;
using InduLink.Diagnostics;
using InduLink.Runtime;
using InduLink.Web.Internal;
using InduLink.Web.WebSockets;
using Newtonsoft.Json;
using Newtonsoft.Json.Converters;
using Newtonsoft.Json.Linq;
using Newtonsoft.Json.Serialization;

namespace InduLink.Web.Gateway
{
    /// <summary>基于 HTTP.sys 的工业 Tag WebAPI 与实时 WebSocket 网关。</summary>
    public sealed class IndustrialWebGateway : IIndustrialWebGateway
    {
        private readonly IIndustrialTagGateway _tagGateway;
        private readonly IndustrialWebGatewayOptions _options;
        private readonly IIndustrialLogger _logger;
        private readonly ConcurrentDictionary<string, GatewaySession> _sessions = new ConcurrentDictionary<string, GatewaySession>();
        private readonly ConcurrentDictionary<int, Task> _requests = new ConcurrentDictionary<int, Task>();
        private readonly SemaphoreSlim _lifecycleGate = new SemaphoreSlim(1, 1);
        private readonly SemaphoreSlim _requestSlots;
        private readonly JsonSerializerSettings _jsonSettings;
        private HttpListener _listener;
        private CancellationTokenSource _stopSource;
        private Task _acceptTask;
        private Task _heartbeatTask;
        private int _nextRequestId;
        private int _admittedSessions;
        private int _running;
        private int _disposed;

        public IndustrialWebGateway(
            IIndustrialTagGateway tagGateway,
            IndustrialWebGatewayOptions options,
            IIndustrialLogger logger = null)
        {
            _tagGateway = tagGateway ?? throw new ArgumentNullException(nameof(tagGateway));
            if (options == null) throw new ArgumentNullException(nameof(options));
            _options = options.Clone();
            _logger = logger ?? NullIndustrialLogger.Instance;
            ValidateOptions(_options);
            _requestSlots = new SemaphoreSlim(_options.MaxConcurrentRequests, _options.MaxConcurrentRequests);
            _jsonSettings = new JsonSerializerSettings
            {
                ContractResolver = new CamelCasePropertyNamesContractResolver(),
                NullValueHandling = NullValueHandling.Include,
                DateTimeZoneHandling = DateTimeZoneHandling.Utc,
            };
            _jsonSettings.Converters.Add(new StringEnumConverter(new CamelCaseNamingStrategy()));
        }

        public IndustrialWebGatewayOptions Options { get { return _options.Clone(); } }
        public bool IsRunning { get { return Volatile.Read(ref _running) != 0; } }
        public IReadOnlyCollection<WebSocketSessionInfo> WebSocketSessions { get { return _sessions.Values.Select(value => value.Info).ToArray(); } }
        public event EventHandler<IndustrialWebRequestEventArgs> RequestCompleted;

        public async Task StartAsync(CancellationToken cancellationToken)
        {
            ThrowIfDisposed();
            await _lifecycleGate.WaitAsync(cancellationToken).ConfigureAwait(false);
            try
            {
                ThrowIfDisposed();
                if (IsRunning) return;
                ValidateOptions(Options);
                var listener = new HttpListener();
                listener.Prefixes.Add(Options.ListenPrefix);
                try { listener.Start(); }
                catch { listener.Close(); throw; }

                _listener = listener;
                _stopSource = new CancellationTokenSource();
                _tagGateway.ValuesChanged += TagGatewayOnValuesChanged;
                _tagGateway.DeviceStateChanged += TagGatewayOnDeviceStateChanged;
                Volatile.Write(ref _running, 1);
                _acceptTask = AcceptLoopAsync(listener, _stopSource.Token);
                _heartbeatTask = HeartbeatLoopAsync(_stopSource.Token);
                _logger.Info("Industrial Web gateway started | Prefix=" + Options.ListenPrefix);
            }
            finally { _lifecycleGate.Release(); }
        }

        public async Task StopAsync(CancellationToken cancellationToken)
        {
            Task acceptTask;
            Task heartbeatTask;
            CancellationTokenSource stopSource;
            await _lifecycleGate.WaitAsync(cancellationToken).ConfigureAwait(false);
            try
            {
                if (!IsRunning) return;
                Volatile.Write(ref _running, 0);
                _tagGateway.ValuesChanged -= TagGatewayOnValuesChanged;
                _tagGateway.DeviceStateChanged -= TagGatewayOnDeviceStateChanged;
                stopSource = _stopSource;
                acceptTask = _acceptTask;
                heartbeatTask = _heartbeatTask;
                _stopSource = null;
                _acceptTask = null;
                _heartbeatTask = null;
                try { _listener?.Close(); } catch { }
                _listener = null;
                stopSource.Cancel();
            }
            finally { _lifecycleGate.Release(); }

            using (var shutdownSource = new CancellationTokenSource(Options.ShutdownTimeout))
            {
                var sessionCloseTasks = _sessions.Values.Select(session =>
                    CloseSessionAsync(session, WebSocketCloseStatus.EndpointUnavailable, "Gateway stopping.", shutdownSource.Token)).ToArray();
                if (sessionCloseTasks.Length > 0) await IgnoreCancellationAsync(Task.WhenAll(sessionCloseTasks)).ConfigureAwait(false);

                await AwaitBoundedAsync(acceptTask, Options.ShutdownTimeout).ConfigureAwait(false);
                await AwaitBoundedAsync(heartbeatTask, Options.ShutdownTimeout).ConfigureAwait(false);
                var active = _requests.Values.ToArray();
                if (active.Length > 0)
                {
                    var allActive = Task.WhenAll(active);
                    await Task.WhenAny(allActive, Task.Delay(Options.ShutdownTimeout, CancellationToken.None)).ConfigureAwait(false);
                }
            }

            stopSource.Dispose();
            _logger.Info("Industrial Web gateway stopped.");
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
                    _logger.Error("Industrial Web gateway accept failed.", ex);
                    continue;
                }

                if (!_requestSlots.Wait(0))
                {
                    WriteImmediateError(context, 429, "too_many_requests", "The gateway is busy.");
                    continue;
                }

                var requestId = Interlocked.Increment(ref _nextRequestId);
                var task = HandleContextAndReleaseAsync(context, cancellationToken);
                _requests[requestId] = task;
                _ = task.ContinueWith(
                    completed => { Task ignored; _requests.TryRemove(requestId, out ignored); },
                    CancellationToken.None,
                    TaskContinuationOptions.ExecuteSynchronously,
                    TaskScheduler.Default);
            }
        }

        private async Task HandleContextAndReleaseAsync(HttpListenerContext context, CancellationToken cancellationToken)
        {
            using (var requestSource = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken))
            {
                requestSource.CancelAfter(_options.RequestTimeout);
                try { await HandleContextAsync(context, requestSource.Token).ConfigureAwait(false); }
                catch (Exception ex)
                {
                    _logger.Error("Industrial Web gateway request failed unexpectedly.", ex);
                    try { await WriteErrorAsync(context.Response, 500, "internal_error", "An internal error occurred.", null).ConfigureAwait(false); } catch { }
                }
                finally { _requestSlots.Release(); }
            }
        }

        private async Task HandleContextAsync(HttpListenerContext context, CancellationToken cancellationToken)
        {
            var statusCode = 500;
            var path = NormalizePath(context.Request.Url.AbsolutePath);
            var correlationId = context.Request.Headers["X-Correlation-ID"];
            if (string.IsNullOrWhiteSpace(correlationId)) correlationId = Guid.NewGuid().ToString("N");
            try
            {
                EnsureOriginAllowed(context.Request);
                AddCorsHeaders(context);

                if (string.Equals(context.Request.HttpMethod, "OPTIONS", StringComparison.OrdinalIgnoreCase))
                {
                    context.Response.StatusCode = 204;
                    context.Response.Close();
                    statusCode = 204;
                    return;
                }
                EnsureAuthorized(context.Request);

                if (string.Equals(path, NormalizePath(Options.WebSocketPath), StringComparison.OrdinalIgnoreCase))
                {
                    if (!string.Equals(context.Request.HttpMethod, "GET", StringComparison.OrdinalIgnoreCase) || !context.Request.IsWebSocketRequest)
                        throw new GatewayHttpException(400, "websocket_upgrade_required", "A WebSocket upgrade request is required.");
                    await UpgradeWebSocketAsync(context, cancellationToken).ConfigureAwait(false);
                    statusCode = 101;
                    return;
                }

                object response;
                if (string.Equals(context.Request.HttpMethod, "GET", StringComparison.OrdinalIgnoreCase) && path == "/api/v1/health")
                {
                    response = new { status = "healthy", running = IsRunning, timestampUtc = DateTimeOffset.UtcNow };
                }
                else if (string.Equals(context.Request.HttpMethod, "GET", StringComparison.OrdinalIgnoreCase) && path == "/api/v1/devices")
                {
                    response = new { devices = _tagGateway.Devices, timestampUtc = DateTimeOffset.UtcNow };
                }
                else if (string.Equals(context.Request.HttpMethod, "GET", StringComparison.OrdinalIgnoreCase) && path.StartsWith("/api/v1/devices/", StringComparison.OrdinalIgnoreCase) && path.EndsWith("/tags", StringComparison.OrdinalIgnoreCase))
                {
                    var deviceName = ExtractDeviceName(path);
                    response = new { device = deviceName, tags = _tagGateway.GetTags(deviceName), timestampUtc = DateTimeOffset.UtcNow };
                }
                else if (string.Equals(context.Request.HttpMethod, "POST", StringComparison.OrdinalIgnoreCase) && path == "/api/v1/read")
                {
                    var request = await ReadJsonAsync<GatewayReadRequest>(context.Request, cancellationToken).ConfigureAwait(false);
                    correlationId = NormalizeCorrelationId(request.CorrelationId, correlationId);
                    ValidateItems(request.Items);
                    var values = await _tagGateway.ReadAsync(request.Items.Select(item => new TagGatewayReadItem(item.Device, item.Tag)).ToList(), cancellationToken).ConfigureAwait(false);
                    response = new { correlationId, items = values };
                }
                else if (string.Equals(context.Request.HttpMethod, "POST", StringComparison.OrdinalIgnoreCase) && path == "/api/v1/write")
                {
                    if (!_tagGateway.Options.EnableRemoteWrites)
                        throw new GatewayHttpException(403, "remote_writes_disabled", "Remote writes are disabled.");
                    var request = await ReadJsonAsync<GatewayWriteRequest>(context.Request, cancellationToken).ConfigureAwait(false);
                    correlationId = NormalizeCorrelationId(request.CorrelationId, correlationId);
                    ValidateItems(request.Items);
                    var results = await _tagGateway.WriteAsync(request.Items.Select(item =>
                        new TagGatewayWriteItem(item.Device, item.Tag, item.Value)).ToList(), cancellationToken).ConfigureAwait(false);
                    response = new { correlationId, items = results };
                }
                else if (string.Equals(context.Request.HttpMethod, "POST", StringComparison.OrdinalIgnoreCase) && path == "/api/v1/read-address")
                {
                    if (!_tagGateway.Options.AllowRawAddressReads)
                        throw new GatewayHttpException(403, "raw_reads_disabled", "Raw address reads are disabled.");
                    var request = await ReadJsonAsync<GatewayRawReadRequest>(context.Request, cancellationToken).ConfigureAwait(false);
                    correlationId = NormalizeCorrelationId(request.CorrelationId, correlationId);
                    ValidateItems(request.Items);
                    var values = new List<TagGatewayValue>(request.Items.Count);
                    foreach (var item in request.Items)
                    {
                        if (item.Length == 0) throw new GatewayHttpException(400, "invalid_length", "Raw read length must be greater than zero.");
                        values.Add(await _tagGateway.ReadAddressAsync(
                            new TagGatewayRawReadItem(item.Device, item.Address, item.DataType, item.Length), cancellationToken).ConfigureAwait(false));
                    }
                    response = new { correlationId, items = values };
                }
                else
                {
                    throw new GatewayHttpException(404, "not_found", "The requested endpoint was not found.");
                }

                statusCode = 200;
                await WriteJsonAsync(context.Response, statusCode, response).ConfigureAwait(false);
            }
            catch (GatewayHttpException ex)
            {
                statusCode = ex.StatusCode;
                await WriteErrorAsync(context.Response, ex.StatusCode, ex.Code, ex.Message, correlationId).ConfigureAwait(false);
            }
            catch (JsonException ex)
            {
                statusCode = 400;
                await WriteErrorAsync(context.Response, 400, "invalid_json", ex.Message, correlationId).ConfigureAwait(false);
            }
            catch (KeyNotFoundException ex)
            {
                statusCode = 404;
                await WriteErrorAsync(context.Response, 404, "not_found", ex.Message, correlationId).ConfigureAwait(false);
            }
            catch (UnauthorizedAccessException ex)
            {
                statusCode = 403;
                await WriteErrorAsync(context.Response, 403, "forbidden", ex.Message, correlationId).ConfigureAwait(false);
            }
            catch (ArgumentException ex)
            {
                statusCode = 400;
                await WriteErrorAsync(context.Response, 400, "invalid_request", ex.Message, correlationId).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                statusCode = IsRunning ? 408 : 503;
                try
                {
                    await WriteErrorAsync(
                        context.Response,
                        statusCode,
                        IsRunning ? "request_timeout" : "gateway_stopping",
                        IsRunning ? "The request timed out." : "The gateway is stopping.",
                        correlationId).ConfigureAwait(false);
                }
                catch { try { context.Response.Abort(); } catch { } }
            }
            finally
            {
                RaiseRequestCompleted(context.Request, path, statusCode);
            }
        }

        private async Task UpgradeWebSocketAsync(HttpListenerContext context, CancellationToken cancellationToken)
        {
            if (Interlocked.Increment(ref _admittedSessions) > Options.MaxWebSocketSessions)
            {
                Interlocked.Decrement(ref _admittedSessions);
                throw new GatewayHttpException(429, "too_many_sessions", "The WebSocket session limit has been reached.");
            }

            var admissionTransferred = false;
            try
            {
                var accepted = await context.AcceptWebSocketAsync(null).ConfigureAwait(false);
                var id = Guid.NewGuid().ToString("N");
                var remote = context.Request.RemoteEndPoint == null ? null : context.Request.RemoteEndPoint.ToString();
                var session = new GatewaySession(
                    new WebSocketSessionInfo(id, remote, DateTimeOffset.UtcNow),
                    new ManagedWebSocket(accepted.WebSocket, Options.MaxWebSocketMessageBytes, Options.ReceiveBufferBytes));
                if (!_sessions.TryAdd(id, session))
                {
                    session.Connection.Dispose();
                    throw new GatewayHttpException(500, "session_failed", "Could not create the WebSocket session.");
                }
                admissionTransferred = true;
                session.PushTask = PushLoopAsync(session, session.StopSource.Token);
                _ = ReceiveWebSocketAsync(session, session.StopSource.Token);
            }
            finally
            {
                if (!admissionTransferred) Interlocked.Decrement(ref _admittedSessions);
            }
        }

        private async Task ReceiveWebSocketAsync(GatewaySession session, CancellationToken cancellationToken)
        {
            WebSocketCloseStatus closeStatus = WebSocketCloseStatus.NormalClosure;
            string description = "Session closed.";
            try
            {
                while (!cancellationToken.IsCancellationRequested && session.Connection.State == WebSocketState.Open)
                {
                    var message = await session.Connection.ReceiveAsync(cancellationToken).ConfigureAwait(false);
                    if (message.IsClose)
                    {
                        closeStatus = message.CloseStatus ?? WebSocketCloseStatus.NormalClosure;
                        description = message.CloseDescription ?? description;
                        break;
                    }
                    if (message.MessageType != WebSocketMessageType.Text)
                    {
                        await SendSocketErrorAsync(session, null, "text_required", "Gateway commands must be UTF-8 text.", cancellationToken).ConfigureAwait(false);
                        continue;
                    }
                    await HandleWebSocketCommandAsync(session, Encoding.UTF8.GetString(message.Payload), cancellationToken).ConfigureAwait(false);
                }
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { }
            catch (WebSocketMessageTooLargeException)
            {
                closeStatus = WebSocketCloseStatus.MessageTooBig;
                description = "Message exceeds configured limit.";
            }
            catch (Exception ex)
            {
                closeStatus = WebSocketCloseStatus.InternalServerError;
                description = "WebSocket session failed.";
                _logger.Error(description, ex);
            }
            finally { await CloseSessionAsync(session, closeStatus, description, CancellationToken.None).ConfigureAwait(false); }
        }

        private async Task HandleWebSocketCommandAsync(GatewaySession session, string json, CancellationToken cancellationToken)
        {
            GatewaySubscriptionCommand command;
            try { command = JsonConvert.DeserializeObject<GatewaySubscriptionCommand>(json, _jsonSettings); }
            catch (JsonException ex)
            {
                await SendSocketErrorAsync(session, null, "invalid_json", ex.Message, cancellationToken).ConfigureAwait(false);
                return;
            }
            if (command == null || string.IsNullOrWhiteSpace(command.Type))
            {
                await SendSocketErrorAsync(session, command == null ? null : command.CorrelationId, "invalid_command", "A command type is required.", cancellationToken).ConfigureAwait(false);
                return;
            }

            if (string.Equals(command.Type, "ping", StringComparison.OrdinalIgnoreCase))
            {
                await SendJsonAsync(session, new { type = "pong", correlationId = command.CorrelationId, timestampUtc = DateTimeOffset.UtcNow }, cancellationToken).ConfigureAwait(false);
                return;
            }

            try
            {
                ValidateItems(command.Items);
                if (string.Equals(command.Type, "subscribe", StringComparison.OrdinalIgnoreCase))
                {
                    var uniqueItems = command.Items
                        .GroupBy(item => SubscriptionKey(item.Device, item.Tag), StringComparer.OrdinalIgnoreCase)
                        .Select(group => group.First())
                        .ToList();
                    var additions = uniqueItems.Count(item => !session.Subscriptions.ContainsKey(SubscriptionKey(item.Device, item.Tag)));
                    if (session.Subscriptions.Count + additions > _options.MaxSubscriptionsPerSession)
                        throw new InvalidOperationException("The session subscription limit has been reached.");

                    foreach (var item in uniqueItems)
                    {
                        EnsureConfiguredTag(item.Device, item.Tag);
                        session.Subscriptions.TryAdd(SubscriptionKey(item.Device, item.Tag), new GatewaySubscription(item.Device, item.Tag));
                    }
                    var values = await _tagGateway.ReadAsync(uniqueItems.Select(item => new TagGatewayReadItem(item.Device, item.Tag)).ToList(), cancellationToken).ConfigureAwait(false);
                    RememberSnapshot(session, values);
                    await SendJsonAsync(session, new { type = "snapshot", correlationId = command.CorrelationId, items = values, timestampUtc = DateTimeOffset.UtcNow }, cancellationToken).ConfigureAwait(false);
                }
                else if (string.Equals(command.Type, "unsubscribe", StringComparison.OrdinalIgnoreCase))
                {
                    foreach (var item in command.Items)
                    {
                        GatewaySubscription removed;
                        var key = SubscriptionKey(item.Device, item.Tag);
                        session.Subscriptions.TryRemove(key, out removed);
                        string ignored;
                        session.ValueSignatures.TryRemove(key, out ignored);
                    }
                    await SendJsonAsync(session, new { type = "unsubscribed", correlationId = command.CorrelationId, items = command.Items, timestampUtc = DateTimeOffset.UtcNow }, cancellationToken).ConfigureAwait(false);
                }
                else
                {
                    await SendSocketErrorAsync(session, command.CorrelationId, "unknown_command", "Supported commands are subscribe, unsubscribe and ping.", cancellationToken).ConfigureAwait(false);
                }
            }
            catch (Exception ex) when (!(ex is OperationCanceledException))
            {
                await SendSocketErrorAsync(session, command.CorrelationId, "invalid_subscription", ex.Message, cancellationToken).ConfigureAwait(false);
            }
        }

        private void TagGatewayOnValuesChanged(object sender, TagGatewayValuesChangedEventArgs args)
        {
            foreach (var session in _sessions.Values)
            {
                if (Volatile.Read(ref session.IsClosing) != 0) continue;
                var queued = false;
                foreach (var value in args.Values.Where(value => value != null && !string.IsNullOrWhiteSpace(value.TagName)))
                {
                    if (!IsChangedForSession(session, value)) continue;
                    session.PendingValues[SubscriptionKey(value.DeviceName, value.TagName)] = value;
                    queued = true;
                }
                if (queued) SignalPush(session);
            }
        }

        private void TagGatewayOnDeviceStateChanged(object sender, TagGatewayDeviceStateChangedEventArgs args)
        {
            foreach (var session in _sessions.Values)
            {
                if (Volatile.Read(ref session.IsClosing) != 0 || !session.Subscriptions.Values.Any(item =>
                    string.Equals(item.Device, args.Device.Name, StringComparison.OrdinalIgnoreCase))) continue;
                session.PendingDeviceStates[args.Device.Name] = args.Device;
                SignalPush(session);
            }
        }

        private async Task PushLoopAsync(GatewaySession session, CancellationToken cancellationToken)
        {
            try
            {
                while (!cancellationToken.IsCancellationRequested)
                {
                    await session.PushSignal.WaitAsync(cancellationToken).ConfigureAwait(false);
                    var values = Drain(session.PendingValues);
                    var devices = Drain(session.PendingDeviceStates);
                    if (values.Count > 0)
                        await SendJsonAsync(session, new { type = "change", items = values, timestampUtc = DateTimeOffset.UtcNow }, cancellationToken).ConfigureAwait(false);
                    if (devices.Count > 0)
                        await SendJsonAsync(session, new { type = "deviceStates", items = devices, timestampUtc = DateTimeOffset.UtcNow }, cancellationToken).ConfigureAwait(false);
                }
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { }
            catch (Exception ex)
            {
                _logger.Error("WebSocket push loop stopped for a slow or failed client.", ex);
                await CloseSessionAsync(session, WebSocketCloseStatus.EndpointUnavailable, "Push delivery failed.", CancellationToken.None).ConfigureAwait(false);
            }
        }

        private static List<TValue> Drain<TKey, TValue>(ConcurrentDictionary<TKey, TValue> source)
        {
            var values = new List<TValue>();
            foreach (var key in source.Keys)
            {
                TValue value;
                if (source.TryRemove(key, out value)) values.Add(value);
            }
            return values;
        }

        private static void SignalPush(GatewaySession session)
        {
            if (Volatile.Read(ref session.IsClosing) != 0 || session.PushSignal.CurrentCount != 0) return;
            try { session.PushSignal.Release(); } catch (SemaphoreFullException) { }
        }

        private async Task HeartbeatLoopAsync(CancellationToken cancellationToken)
        {
            while (!cancellationToken.IsCancellationRequested)
            {
                try { await Task.Delay(Options.HeartbeatInterval, cancellationToken).ConfigureAwait(false); }
                catch (OperationCanceledException) { break; }
                var sends = _sessions.Values.Select(session => SendHeartbeatAsync(session, cancellationToken)).ToArray();
                if (sends.Length > 0) await Task.WhenAll(sends).ConfigureAwait(false);
            }
        }

        private async Task SendHeartbeatAsync(GatewaySession session, CancellationToken cancellationToken)
        {
            try { await SendJsonAsync(session, new { type = "heartbeat", timestampUtc = DateTimeOffset.UtcNow }, cancellationToken).ConfigureAwait(false); }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { }
            catch (Exception ex)
            {
                _logger.Error("WebSocket heartbeat failed.", ex);
                await CloseSessionAsync(session, WebSocketCloseStatus.EndpointUnavailable, "Heartbeat delivery failed.", CancellationToken.None).ConfigureAwait(false);
            }
        }

        private Task SendSocketErrorAsync(GatewaySession session, string correlationId, string code, string message, CancellationToken cancellationToken)
        {
            return SendJsonAsync(session, new { type = "error", correlationId, error = new { code, message }, timestampUtc = DateTimeOffset.UtcNow }, cancellationToken);
        }

        private async Task SendJsonAsync(GatewaySession session, object value, CancellationToken cancellationToken)
        {
            var payload = Encoding.UTF8.GetBytes(JsonConvert.SerializeObject(value, _jsonSettings));
            if (payload.Length > Options.MaxWebSocketMessageBytes) throw new WebSocketMessageTooLargeException(Options.MaxWebSocketMessageBytes);
            using (var sendSource = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, session.StopSource.Token))
            {
                sendSource.CancelAfter(_options.WebSocketSendTimeout);
                await session.Connection.SendAsync(payload, WebSocketMessageType.Text, sendSource.Token).ConfigureAwait(false);
            }
        }

        private void RememberSnapshot(GatewaySession session, IEnumerable<TagGatewayValue> values)
        {
            foreach (var value in values.Where(item => item != null && !string.IsNullOrWhiteSpace(item.TagName)))
                session.ValueSignatures[SubscriptionKey(value.DeviceName, value.TagName)] = CreateValueSignature(value);
        }

        private bool IsChangedForSession(GatewaySession session, TagGatewayValue value)
        {
            var key = SubscriptionKey(value.DeviceName, value.TagName);
            if (!session.Subscriptions.ContainsKey(key)) return false;
            var signature = CreateValueSignature(value);
            while (true)
            {
                string previous;
                if (!session.ValueSignatures.TryGetValue(key, out previous))
                    return session.ValueSignatures.TryAdd(key, signature);
                if (string.Equals(previous, signature, StringComparison.Ordinal)) return false;
                if (session.ValueSignatures.TryUpdate(key, signature, previous)) return true;
            }
        }

        private string CreateValueSignature(TagGatewayValue value)
        {
            return JsonConvert.SerializeObject(new
            {
                value.DataType,
                value.Value,
                value.Quality,
                value.ErrorMessage,
            }, _jsonSettings);
        }

        private async Task CloseSessionAsync(GatewaySession session, WebSocketCloseStatus status, string description, CancellationToken cancellationToken)
        {
            if (Interlocked.CompareExchange(ref session.IsClosing, 1, 0) != 0) return;
            session.StopSource.Cancel();
            try
            {
                using (var closeSource = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken))
                {
                    closeSource.CancelAfter(_options.CloseHandshakeTimeout);
                    try { await session.Connection.CloseAsync(status, description, closeSource.Token).ConfigureAwait(false); }
                    catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested) { }
                }
            }
            finally
            {
                GatewaySession removed;
                if (_sessions.TryRemove(session.Info.Id, out removed)) Interlocked.Decrement(ref _admittedSessions);
                session.Connection.Dispose();
            }
        }

        private void EnsureAuthorized(HttpListenerRequest request)
        {
            if (Options.RequireApiKey && !WebSecurity.FixedTimeEquals(Options.ApiKey, request.Headers[WebSecurity.ApiKeyHeaderName]))
                throw new GatewayHttpException(401, "unauthorized", "A valid X-Industrial-Api-Key header is required.");
        }

        private void EnsureOriginAllowed(HttpListenerRequest request)
        {
            if (!WebSecurity.IsRequestOriginAllowed(
                request.Headers["Origin"],
                Options.RequireApiKey,
                Options.AllowedOrigins.ToArray()))
                throw new GatewayHttpException(403, "origin_forbidden", "The request Origin is not allowed.");
        }

        private void AddCorsHeaders(HttpListenerContext context)
        {
            var origin = context.Request.Headers["Origin"];
            if (string.IsNullOrWhiteSpace(origin)) return;
            context.Response.Headers["Access-Control-Allow-Origin"] = origin;
            context.Response.Headers["Vary"] = "Origin";
            context.Response.Headers["Access-Control-Allow-Headers"] = "Content-Type, X-Industrial-Api-Key, X-Correlation-ID";
            context.Response.Headers["Access-Control-Allow-Methods"] = "GET, POST, OPTIONS";
        }

        private async Task<T> ReadJsonAsync<T>(HttpListenerRequest request, CancellationToken cancellationToken)
        {
            if (request.ContentLength64 > Options.MaxRequestContentBytes)
                throw new GatewayHttpException(413, "request_too_large", "The request body exceeds the configured limit.");
            if (!string.IsNullOrWhiteSpace(request.ContentType) && request.ContentType.IndexOf("application/json", StringComparison.OrdinalIgnoreCase) < 0)
                throw new GatewayHttpException(400, "json_required", "Content-Type must be application/json.");

            using (var output = new MemoryStream())
            {
                var buffer = new byte[8192];
                while (true)
                {
                    var read = await request.InputStream.ReadAsync(buffer, 0, buffer.Length, cancellationToken).ConfigureAwait(false);
                    if (read == 0) break;
                    if (output.Length + read > Options.MaxRequestContentBytes)
                        throw new GatewayHttpException(413, "request_too_large", "The request body exceeds the configured limit.");
                    output.Write(buffer, 0, read);
                }
                if (output.Length == 0) throw new GatewayHttpException(400, "body_required", "A JSON request body is required.");
                var value = JsonConvert.DeserializeObject<T>(Encoding.UTF8.GetString(output.ToArray()), _jsonSettings);
                if (value == null) throw new GatewayHttpException(400, "invalid_json", "The JSON request body cannot be null.");
                return value;
            }
        }

        private Task WriteJsonAsync(HttpListenerResponse response, int statusCode, object value)
        {
            var bytes = Encoding.UTF8.GetBytes(JsonConvert.SerializeObject(value, _jsonSettings));
            response.StatusCode = statusCode;
            response.ContentType = "application/json; charset=utf-8";
            response.ContentLength64 = bytes.Length;
            return WriteAndCloseAsync(response, bytes);
        }

        private Task WriteErrorAsync(HttpListenerResponse response, int statusCode, string code, string message, string correlationId)
        {
            return WriteJsonAsync(response, statusCode, new { correlationId, error = new { code, message }, timestampUtc = DateTimeOffset.UtcNow });
        }

        private static async Task WriteAndCloseAsync(HttpListenerResponse response, byte[] bytes)
        {
            try { await response.OutputStream.WriteAsync(bytes, 0, bytes.Length).ConfigureAwait(false); }
            finally { try { response.Close(); } catch { } }
        }

        private static void WriteImmediateError(HttpListenerContext context, int statusCode, string code, string message)
        {
            try
            {
                var bytes = Encoding.UTF8.GetBytes(JsonConvert.SerializeObject(new { error = new { code, message }, timestampUtc = DateTimeOffset.UtcNow }));
                context.Response.StatusCode = statusCode;
                context.Response.ContentType = "application/json; charset=utf-8";
                context.Response.ContentLength64 = bytes.Length;
                context.Response.OutputStream.Write(bytes, 0, bytes.Length);
                context.Response.Close();
            }
            catch { try { context.Response.Abort(); } catch { } }
        }

        private void EnsureConfiguredTag(string device, string tag)
        {
            if (string.IsNullOrWhiteSpace(device) || string.IsNullOrWhiteSpace(tag))
                throw new ArgumentException("Device and tag are required.");
            if (!_tagGateway.GetTags(device).Any(value => string.Equals(value.Name, tag, StringComparison.OrdinalIgnoreCase)))
                throw new KeyNotFoundException(string.Format("Tag '{0}' was not found on device '{1}'.", tag, device));
        }

        private void ValidateItems<T>(IReadOnlyCollection<T> items)
        {
            if (items == null || items.Count == 0) throw new GatewayHttpException(400, "items_required", "At least one item is required.");
            if (items.Count > Options.MaxBatchItems) throw new GatewayHttpException(413, "too_many_items", "The batch exceeds the configured item limit.");
            if (items.Any(item => item == null)) throw new GatewayHttpException(400, "invalid_item", "Items cannot contain null entries.");
        }

        private static string ExtractDeviceName(string path)
        {
            const string prefix = "/api/v1/devices/";
            const string suffix = "/tags";
            var encoded = path.Substring(prefix.Length, path.Length - prefix.Length - suffix.Length);
            if (string.IsNullOrWhiteSpace(encoded) || encoded.Contains("/")) throw new GatewayHttpException(404, "not_found", "Device route was not found.");
            return Uri.UnescapeDataString(encoded);
        }

        private static string NormalizeCorrelationId(string requested, string fallback)
        {
            if (string.IsNullOrWhiteSpace(requested)) return fallback;
            requested = requested.Trim();
            if (requested.Length > 128) throw new GatewayHttpException(400, "invalid_correlation_id", "Correlation ID cannot exceed 128 characters.");
            return requested;
        }

        private static string NormalizePath(string value)
        {
            var normalized = "/" + (value ?? string.Empty).Trim('/');
            return normalized.Length > 1 ? normalized : "/";
        }

        private static string SubscriptionKey(string device, string tag)
        {
            return (device ?? string.Empty).Trim() + "\u001f" + (tag ?? string.Empty).Trim();
        }

        private void RaiseRequestCompleted(HttpListenerRequest request, string path, int statusCode)
        {
            try
            {
                RequestCompleted?.Invoke(this, new IndustrialWebRequestEventArgs(
                    request.HttpMethod,
                    path,
                    statusCode,
                    request.RemoteEndPoint == null ? null : request.RemoteEndPoint.ToString(),
                    DateTimeOffset.UtcNow));
            }
            catch { }
        }

        private static async Task IgnoreCancellationAsync(Task task)
        {
            if (task == null) return;
            try { await task.ConfigureAwait(false); } catch { }
        }

        private static async Task AwaitBoundedAsync(Task task, TimeSpan timeout)
        {
            if (task == null) return;
            if (await Task.WhenAny(task, Task.Delay(timeout, CancellationToken.None)).ConfigureAwait(false) == task)
                await IgnoreCancellationAsync(task).ConfigureAwait(false);
        }

        private static void ValidateOptions(IndustrialWebGatewayOptions options)
        {
            WebSecurity.ValidateListenerSecurity(options.ListenPrefix, options.RequireApiKey, options.ApiKey, options.AllowedOrigins.ToArray(), nameof(options));
            if (!options.RequireApiKey)
                throw new ArgumentException("The industrial WebAPI and Tag WebSocket gateway must require an API key.", nameof(options));
            if (string.IsNullOrWhiteSpace(options.WebSocketPath) || !options.WebSocketPath.StartsWith("/", StringComparison.Ordinal))
                throw new ArgumentException("WebSocketPath must start with '/'.", nameof(options));
            if (options.MaxRequestContentBytes <= 0 || options.MaxWebSocketMessageBytes <= 0 || options.ReceiveBufferBytes <= 0 ||
                options.MaxConcurrentRequests <= 0 || options.MaxWebSocketSessions <= 0 || options.MaxBatchItems <= 0 ||
                options.MaxSubscriptionsPerSession <= 0)
                throw new ArgumentOutOfRangeException(nameof(options));
            if (options.HeartbeatInterval <= TimeSpan.Zero || options.ShutdownTimeout <= TimeSpan.Zero ||
                options.RequestTimeout <= TimeSpan.Zero || options.WebSocketSendTimeout <= TimeSpan.Zero ||
                options.CloseHandshakeTimeout <= TimeSpan.Zero)
                throw new ArgumentOutOfRangeException(nameof(options));
        }

        private void ThrowIfDisposed()
        {
            if (Volatile.Read(ref _disposed) != 0) throw new ObjectDisposedException(nameof(IndustrialWebGateway));
        }

        public void Dispose()
        {
            if (Interlocked.Exchange(ref _disposed, 1) != 0) return;
            StopAsync(CancellationToken.None).GetAwaiter().GetResult();
        }

        private sealed class GatewaySession
        {
            internal GatewaySession(WebSocketSessionInfo info, ManagedWebSocket connection)
            {
                Info = info;
                Connection = connection;
                Subscriptions = new ConcurrentDictionary<string, GatewaySubscription>(StringComparer.OrdinalIgnoreCase);
                ValueSignatures = new ConcurrentDictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                PendingValues = new ConcurrentDictionary<string, TagGatewayValue>(StringComparer.OrdinalIgnoreCase);
                PendingDeviceStates = new ConcurrentDictionary<string, TagGatewayDevice>(StringComparer.OrdinalIgnoreCase);
                PushSignal = new SemaphoreSlim(0, 1);
                StopSource = new CancellationTokenSource();
            }

            internal WebSocketSessionInfo Info { get; private set; }
            internal ManagedWebSocket Connection { get; private set; }
            internal ConcurrentDictionary<string, GatewaySubscription> Subscriptions { get; private set; }
            internal ConcurrentDictionary<string, string> ValueSignatures { get; private set; }
            internal ConcurrentDictionary<string, TagGatewayValue> PendingValues { get; private set; }
            internal ConcurrentDictionary<string, TagGatewayDevice> PendingDeviceStates { get; private set; }
            internal SemaphoreSlim PushSignal { get; private set; }
            internal CancellationTokenSource StopSource { get; private set; }
            internal Task PushTask { get; set; }
            internal int IsClosing;
        }

        private sealed class GatewaySubscription
        {
            internal GatewaySubscription(string device, string tag) { Device = device; Tag = tag; }
            internal string Device { get; private set; }
            internal string Tag { get; private set; }
        }
    }
}
