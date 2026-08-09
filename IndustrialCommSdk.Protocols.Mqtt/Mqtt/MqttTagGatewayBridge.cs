using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using IndustrialCommSdk.Diagnostics;
using IndustrialCommSdk.Runtime;
using Newtonsoft.Json;
using Newtonsoft.Json.Converters;
using Newtonsoft.Json.Linq;
using Newtonsoft.Json.Serialization;

namespace IndustrialCommSdk.Protocols.Mqtt
{
    /// <summary>MQTT Tag 网关主题、批量和心跳配置。</summary>
    public sealed class MqttTagGatewayOptions
    {
        public string RootTopic { get; set; } = "industrial/v1";
        public int QualityOfService { get; set; } = 1;
        public bool RetainTelemetry { get; set; } = true;
        public int MaxCommandItems { get; set; } = 200;
        public int MaxCommandPayloadBytes { get; set; } = 64 * 1024;
        public int MaxPendingWorkItems { get; set; } = 256;
        public int MaxConcurrentCommands { get; set; } = 4;
        public TimeSpan HeartbeatInterval { get; set; } = TimeSpan.FromSeconds(15);
        public TimeSpan ShutdownTimeout { get; set; } = TimeSpan.FromSeconds(3);
    }

    public interface IMqttTagGatewayBridge : IDisposable
    {
        bool IsRunning { get; }
        Task StartAsync(CancellationToken cancellationToken);
        Task StopAsync(CancellationToken cancellationToken);
    }

    /// <summary>在 MQTT Broker 与统一工业 Tag 网关之间提供快照、变化推送和读写命令。</summary>
    public sealed class MqttTagGatewayBridge : IMqttTagGatewayBridge
    {
        private readonly IMqttBrokerService _broker;
        private readonly IIndustrialTagGateway _gateway;
        private readonly MqttTagGatewayOptions _options;
        private readonly IIndustrialLogger _logger;
        private readonly SemaphoreSlim _lifecycleGate = new SemaphoreSlim(1, 1);
        private readonly ConcurrentDictionary<string, string> _valueSignatures =
            new ConcurrentDictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        private readonly JsonSerializerSettings _jsonSettings;
        private WorkRun _run;
        private Task _heartbeatTask;
        private int _running;
        private int _disposed;

        public MqttTagGatewayBridge(
            IMqttBrokerService broker,
            IIndustrialTagGateway gateway,
            MqttTagGatewayOptions options = null,
            IIndustrialLogger logger = null)
        {
            _broker = broker ?? throw new ArgumentNullException(nameof(broker));
            _gateway = gateway ?? throw new ArgumentNullException(nameof(gateway));
            _options = CloneOptions(options ?? new MqttTagGatewayOptions());
            _logger = logger ?? NullIndustrialLogger.Instance;
            ValidateOptions(_options);
            _jsonSettings = new JsonSerializerSettings
            {
                ContractResolver = new CamelCasePropertyNamesContractResolver(),
                NullValueHandling = NullValueHandling.Include,
                DateTimeZoneHandling = DateTimeZoneHandling.Utc,
            };
            _jsonSettings.Converters.Add(new StringEnumConverter(new CamelCaseNamingStrategy()));
        }

        public bool IsRunning { get { return Volatile.Read(ref _running) != 0; } }

        public async Task StartAsync(CancellationToken cancellationToken)
        {
            WorkRun run;
            ThrowIfDisposed();
            await _lifecycleGate.WaitAsync(cancellationToken).ConfigureAwait(false);
            try
            {
                ThrowIfDisposed();
                if (IsRunning) return;
                if (!_broker.IsRunning) throw new InvalidOperationException("The MQTT broker must be running before the Tag gateway bridge starts.");

                run = new WorkRun(_options.MaxConcurrentCommands);
                _run = run;
                _broker.MessageReceived += BrokerOnMessageReceived;
                _gateway.ValuesChanged += GatewayOnValuesChanged;
                _gateway.DeviceStateChanged += GatewayOnDeviceStateChanged;
                Volatile.Write(ref _running, 1);
                _heartbeatTask = HeartbeatLoopAsync(run.Token);
            }
            finally
            {
                _lifecycleGate.Release();
            }

            using (var startupCancellation = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, run.Token))
            {
                try
                {
                    await PublishSnapshotAsync(startupCancellation.Token).ConfigureAwait(false);
                    await PublishDeviceStatesAsync(startupCancellation.Token).ConfigureAwait(false);
                }
                catch
                {
                    try { await StopAsync(CancellationToken.None).ConfigureAwait(false); }
                    catch (ObjectDisposedException) when (Volatile.Read(ref _disposed) != 0) { }
                    throw;
                }
            }
        }

        public async Task StopAsync(CancellationToken cancellationToken)
        {
            WorkRun run;
            Task heartbeat;
            await _lifecycleGate.WaitAsync(cancellationToken).ConfigureAwait(false);
            try
            {
                if (!IsRunning) return;
                Volatile.Write(ref _running, 0);
                _broker.MessageReceived -= BrokerOnMessageReceived;
                _gateway.ValuesChanged -= GatewayOnValuesChanged;
                _gateway.DeviceStateChanged -= GatewayOnDeviceStateChanged;
                run = _run;
                heartbeat = _heartbeatTask;
                _run = null;
                _heartbeatTask = null;
                if (run != null) run.StopAccepting();
            }
            finally
            {
                _lifecycleGate.Release();
            }

            _valueSignatures.Clear();
            if (run != null) run.Cancel();

            var tasks = new List<Task>();
            if (heartbeat != null) tasks.Add(heartbeat);
            if (run != null) tasks.AddRange(run.SnapshotWork());
            var completion = IgnoreFailureAsync(Task.WhenAll(tasks));
            if (run != null)
            {
                _ = completion.ContinueWith(
                    completed => run.Dispose(),
                    CancellationToken.None,
                    TaskContinuationOptions.ExecuteSynchronously,
                    TaskScheduler.Default);
            }

            if (!await WaitWithinAsync(completion, _options.ShutdownTimeout, cancellationToken).ConfigureAwait(false))
                _logger.Warn(string.Format("MQTT Tag gateway shutdown timed out after {0} ms; detached work will finish in the background.",
                    (long)_options.ShutdownTimeout.TotalMilliseconds));
        }

        public static bool IsClientPublishAllowed(string rootTopic, string clientId, string topic)
        {
            if (string.IsNullOrWhiteSpace(clientId) || string.IsNullOrWhiteSpace(topic)) return false;
            var prefix = NormalizeRoot(rootTopic) + "/requests/" + EncodeSegment(clientId) + "/";
            return string.Equals(topic, prefix + "read", StringComparison.Ordinal) ||
                   string.Equals(topic, prefix + "write", StringComparison.Ordinal);
        }

        public static bool IsClientSubscriptionAllowed(string rootTopic, string clientId, string topicFilter)
        {
            if (string.IsNullOrWhiteSpace(clientId) || string.IsNullOrWhiteSpace(topicFilter)) return false;
            var root = NormalizeRoot(rootTopic);
            if (string.Equals(topicFilter, root + "/devices/#", StringComparison.Ordinal) ||
                string.Equals(topicFilter, root + "/gateway/heartbeat", StringComparison.Ordinal)) return true;
            var responsePrefix = root + "/responses/" + EncodeSegment(clientId) + "/";
            return string.Equals(topicFilter, responsePrefix + "#", StringComparison.Ordinal) ||
                   topicFilter.StartsWith(responsePrefix, StringComparison.Ordinal);
        }

        private async Task PublishSnapshotAsync(CancellationToken cancellationToken)
        {
            var requests = new List<TagGatewayReadItem>();
            foreach (var device in _gateway.Devices)
            {
                foreach (var tag in _gateway.GetTags(device.Name))
                {
                    requests.Add(new TagGatewayReadItem(device.Name, tag.Name));
                }
            }

            for (var offset = 0; offset < requests.Count; offset += _options.MaxCommandItems)
            {
                var batch = requests.Skip(offset).Take(_options.MaxCommandItems).ToList();
                var values = await _gateway.ReadAsync(batch, cancellationToken).ConfigureAwait(false);
                foreach (var value in values) await PublishValueAsync(value, true, cancellationToken).ConfigureAwait(false);
            }
        }

        private async Task PublishDeviceStatesAsync(CancellationToken cancellationToken)
        {
            foreach (var device in _gateway.Devices)
            {
                await PublishJsonAsync(
                    DeviceStateTopic(device.Name),
                    new { type = "deviceState", device, timestampUtc = DateTimeOffset.UtcNow },
                    _options.QualityOfService,
                    true,
                    cancellationToken).ConfigureAwait(false);
            }
        }

        private void BrokerOnMessageReceived(object sender, MqttBrokerMessageReceivedEventArgs args)
        {
            if (!IsRunning || !TryParseRequestTopic(args.Topic, out var encodedClientId, out var operation)) return;
            if (string.IsNullOrWhiteSpace(args.ClientId) ||
                !string.Equals(encodedClientId, EncodeSegment(args.ClientId), StringComparison.Ordinal))
            {
                _logger.Warn(string.Format("MQTT Tag gateway command rejected because the topic client does not match the connected client | Client={0} | Topic={1}",
                    args.ClientId, args.Topic));
                return;
            }
            var payloadLength = args.Payload == null ? 0 : args.Payload.Length;
            if (payloadLength > _options.MaxCommandPayloadBytes)
            {
                _logger.Warn(string.Format("MQTT Tag gateway command rejected because its payload is too large | Client={0} | Bytes={1} | Limit={2}",
                    args.ClientId, payloadLength, _options.MaxCommandPayloadBytes));
                return;
            }
            if (!QueueWork(token => HandleCommandAsync(encodedClientId, operation, args.Payload, token)))
                _logger.Warn(string.Format("MQTT Tag gateway command rejected because the work queue is full | Client={0} | Limit={1}",
                    args.ClientId, _options.MaxPendingWorkItems));
        }

        private async Task HandleCommandAsync(string encodedClientId, string operation, byte[] payload, CancellationToken cancellationToken)
        {
            string correlationId = null;
            try
            {
                var command = JsonConvert.DeserializeObject<MqttGatewayCommand>(Encoding.UTF8.GetString(payload ?? new byte[0]), _jsonSettings);
                if (command == null) throw new InvalidOperationException("Command payload cannot be null.");
                correlationId = NormalizeCorrelationId(command.CorrelationId);
                if (command.Items == null || command.Items.Count == 0) throw new InvalidOperationException("At least one command item is required.");
                if (command.Items.Count > _options.MaxCommandItems) throw new InvalidOperationException("The command exceeds the configured item limit.");
                if (command.Items.Any(item => item == null)) throw new InvalidOperationException("Command items cannot contain null entries.");

                if (string.Equals(operation, "read", StringComparison.Ordinal))
                {
                    var values = await _gateway.ReadAsync(command.Items.Select(item =>
                        new TagGatewayReadItem(item.Device, item.Tag)).ToList(), cancellationToken).ConfigureAwait(false);
                    await PublishResponseAsync(encodedClientId, correlationId, new
                    {
                        type = "readResult",
                        correlationId,
                        items = values,
                        timestampUtc = DateTimeOffset.UtcNow,
                    }, cancellationToken).ConfigureAwait(false);
                }
                else
                {
                    var results = await _gateway.WriteAsync(command.Items.Select(item =>
                        new TagGatewayWriteItem(item.Device, item.Tag, item.Value)).ToList(), cancellationToken).ConfigureAwait(false);
                    await PublishResponseAsync(encodedClientId, correlationId, new
                    {
                        type = "writeResult",
                        correlationId,
                        items = results,
                        timestampUtc = DateTimeOffset.UtcNow,
                    }, cancellationToken).ConfigureAwait(false);
                }
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
            }
            catch (Exception ex)
            {
                correlationId = string.IsNullOrWhiteSpace(correlationId) ? Guid.NewGuid().ToString("N") : correlationId;
                _logger.Error("MQTT Tag gateway command failed.", ex);
                await PublishResponseAsync(encodedClientId, correlationId, new
                {
                    type = "error",
                    correlationId,
                    error = new { code = "command_failed", message = ex.Message },
                    timestampUtc = DateTimeOffset.UtcNow,
                }, cancellationToken).ConfigureAwait(false);
            }
        }

        private void GatewayOnValuesChanged(object sender, TagGatewayValuesChangedEventArgs args)
        {
            if (!QueueWork(async token =>
            {
                foreach (var value in args.Values)
                {
                    if (value == null || string.IsNullOrWhiteSpace(value.TagName)) continue;
                    var key = value.DeviceName + "\u001f" + value.TagName;
                    var signature = JsonConvert.SerializeObject(new { value.DataType, value.Value, value.Quality, value.ErrorMessage }, _jsonSettings);
                    string previous;
                    if (_valueSignatures.TryGetValue(key, out previous) && string.Equals(previous, signature, StringComparison.Ordinal)) continue;
                    _valueSignatures[key] = signature;
                    await PublishValueAsync(value, false, token).ConfigureAwait(false);
                }
            })) _logger.Warn("MQTT Tag gateway value change was dropped because the work queue is full.");
        }

        private void GatewayOnDeviceStateChanged(object sender, TagGatewayDeviceStateChangedEventArgs args)
        {
            if (!QueueWork(token => PublishJsonAsync(
                DeviceStateTopic(args.Device.Name),
                new { type = "deviceState", device = args.Device, timestampUtc = DateTimeOffset.UtcNow },
                _options.QualityOfService,
                true,
                token))) _logger.Warn("MQTT Tag gateway device state change was dropped because the work queue is full.");
        }

        private Task PublishValueAsync(TagGatewayValue value, bool snapshot, CancellationToken cancellationToken)
        {
            if (value == null || string.IsNullOrWhiteSpace(value.DeviceName) || string.IsNullOrWhiteSpace(value.TagName)) return Task.CompletedTask;
            var key = value.DeviceName + "\u001f" + value.TagName;
            _valueSignatures[key] = JsonConvert.SerializeObject(new { value.DataType, value.Value, value.Quality, value.ErrorMessage }, _jsonSettings);
            return PublishJsonAsync(
                ValueTopic(value.DeviceName, value.TagName),
                new { type = snapshot ? "snapshot" : "change", item = value, timestampUtc = DateTimeOffset.UtcNow },
                _options.QualityOfService,
                _options.RetainTelemetry,
                cancellationToken);
        }

        private async Task HeartbeatLoopAsync(CancellationToken cancellationToken)
        {
            while (!cancellationToken.IsCancellationRequested)
            {
                try { await Task.Delay(_options.HeartbeatInterval, cancellationToken).ConfigureAwait(false); }
                catch (OperationCanceledException) { break; }
                await PublishJsonAsync(
                    Root + "/gateway/heartbeat",
                    new { type = "heartbeat", timestampUtc = DateTimeOffset.UtcNow },
                    0,
                    false,
                    cancellationToken).ConfigureAwait(false);
            }
        }

        private Task PublishResponseAsync(string encodedClientId, string correlationId, object response, CancellationToken cancellationToken)
        {
            return PublishJsonAsync(
                Root + "/responses/" + encodedClientId + "/" + EncodeSegment(correlationId),
                response,
                _options.QualityOfService,
                false,
                cancellationToken);
        }

        private Task PublishJsonAsync(string topic, object value, int qualityOfService, bool retain, CancellationToken cancellationToken)
        {
            var payload = Encoding.UTF8.GetBytes(JsonConvert.SerializeObject(value, _jsonSettings));
            return _broker.PublishAsync(topic, payload, qualityOfService, retain, cancellationToken);
        }

        private string ValueTopic(string device, string tag)
        {
            return Root + "/devices/" + EncodeSegment(device) + "/tags/" + EncodeSegment(tag);
        }

        private string DeviceStateTopic(string device)
        {
            return Root + "/devices/" + EncodeSegment(device) + "/state";
        }

        private bool TryParseRequestTopic(string topic, out string encodedClientId, out string operation)
        {
            encodedClientId = null;
            operation = null;
            var prefix = Root + "/requests/";
            if (string.IsNullOrWhiteSpace(topic) || !topic.StartsWith(prefix, StringComparison.Ordinal)) return false;
            var remainder = topic.Substring(prefix.Length);
            var segments = remainder.Split('/');
            if (segments.Length != 2 || string.IsNullOrWhiteSpace(segments[0])) return false;
            if (segments[1] != "read" && segments[1] != "write") return false;
            encodedClientId = segments[0];
            operation = segments[1];
            return true;
        }

        private bool QueueWork(Func<CancellationToken, Task> action)
        {
            var run = _run;
            return IsRunning && run != null && run.TryQueue(action, _options.MaxPendingWorkItems, _logger);
        }

        private static string NormalizeCorrelationId(string value)
        {
            if (string.IsNullOrWhiteSpace(value)) return Guid.NewGuid().ToString("N");
            value = value.Trim();
            if (value.Length > 128) throw new InvalidOperationException("Correlation ID cannot exceed 128 characters.");
            return value;
        }

        private static string EncodeSegment(string value)
        {
            if (string.IsNullOrWhiteSpace(value)) throw new ArgumentException("MQTT topic segment cannot be empty.", nameof(value));
            return Uri.EscapeDataString(value.Trim());
        }

        private static string NormalizeRoot(string rootTopic)
        {
            if (string.IsNullOrWhiteSpace(rootTopic)) throw new ArgumentException("RootTopic cannot be empty.", nameof(rootTopic));
            return rootTopic.Trim().Trim('/');
        }

        private string Root { get { return NormalizeRoot(_options.RootTopic); } }

        private static void ValidateOptions(MqttTagGatewayOptions options)
        {
            NormalizeRoot(options.RootTopic);
            if (options.QualityOfService < 0 || options.QualityOfService > 2) throw new ArgumentOutOfRangeException(nameof(options.QualityOfService));
            if (options.MaxCommandItems <= 0) throw new ArgumentOutOfRangeException(nameof(options.MaxCommandItems));
            if (options.MaxCommandPayloadBytes <= 0) throw new ArgumentOutOfRangeException(nameof(options.MaxCommandPayloadBytes));
            if (options.MaxPendingWorkItems <= 0) throw new ArgumentOutOfRangeException(nameof(options.MaxPendingWorkItems));
            if (options.MaxConcurrentCommands <= 0 || options.MaxConcurrentCommands > options.MaxPendingWorkItems)
                throw new ArgumentOutOfRangeException(nameof(options.MaxConcurrentCommands));
            if (options.HeartbeatInterval <= TimeSpan.Zero) throw new ArgumentOutOfRangeException(nameof(options.HeartbeatInterval));
            if (options.ShutdownTimeout <= TimeSpan.Zero) throw new ArgumentOutOfRangeException(nameof(options.ShutdownTimeout));
        }

        private static MqttTagGatewayOptions CloneOptions(MqttTagGatewayOptions options)
        {
            return new MqttTagGatewayOptions
            {
                RootTopic = options.RootTopic,
                QualityOfService = options.QualityOfService,
                RetainTelemetry = options.RetainTelemetry,
                MaxCommandItems = options.MaxCommandItems,
                MaxCommandPayloadBytes = options.MaxCommandPayloadBytes,
                MaxPendingWorkItems = options.MaxPendingWorkItems,
                MaxConcurrentCommands = options.MaxConcurrentCommands,
                HeartbeatInterval = options.HeartbeatInterval,
                ShutdownTimeout = options.ShutdownTimeout,
            };
        }

        private static async Task<bool> WaitWithinAsync(Task task, TimeSpan timeout, CancellationToken cancellationToken)
        {
            if (task.IsCompleted)
            {
                await task.ConfigureAwait(false);
                return true;
            }
            var delay = Task.Delay(timeout, cancellationToken);
            var completed = await Task.WhenAny(task, delay).ConfigureAwait(false);
            if (completed == task)
            {
                await task.ConfigureAwait(false);
                return true;
            }
            cancellationToken.ThrowIfCancellationRequested();
            return false;
        }

        private static async Task IgnoreFailureAsync(Task task)
        {
            if (task == null) return;
            try { await task.ConfigureAwait(false); } catch { }
        }

        private void ThrowIfDisposed()
        {
            if (Volatile.Read(ref _disposed) != 0) throw new ObjectDisposedException(nameof(MqttTagGatewayBridge));
        }

        public void Dispose()
        {
            if (Interlocked.Exchange(ref _disposed, 1) != 0) return;
            StopAsync(CancellationToken.None).GetAwaiter().GetResult();
            _lifecycleGate.Dispose();
        }

        private sealed class MqttGatewayCommand
        {
            public string CorrelationId { get; set; }
            public List<MqttGatewayCommandItem> Items { get; set; }
        }

        private sealed class MqttGatewayCommandItem
        {
            public string Device { get; set; }
            public string Tag { get; set; }
            public JToken Value { get; set; }
        }

        private sealed class WorkRun : IDisposable
        {
            private readonly object _sync = new object();
            private readonly ConcurrentDictionary<int, Task> _work = new ConcurrentDictionary<int, Task>();
            private readonly SemaphoreSlim _concurrency;
            private readonly CancellationTokenSource _stopSource = new CancellationTokenSource();
            private int _nextWorkId;
            private bool _accepting = true;
            private int _disposed;

            public WorkRun(int maxConcurrency)
            {
                _concurrency = new SemaphoreSlim(maxConcurrency, maxConcurrency);
            }

            public CancellationToken Token { get { return _stopSource.Token; } }

            public bool TryQueue(Func<CancellationToken, Task> action, int maxPendingWorkItems, IIndustrialLogger logger)
            {
                if (action == null) throw new ArgumentNullException(nameof(action));
                lock (_sync)
                {
                    if (!_accepting || _work.Count >= maxPendingWorkItems) return false;
                    var id = Interlocked.Increment(ref _nextWorkId);
                    var task = Task.Run(async () =>
                    {
                        var entered = false;
                        try
                        {
                            await _concurrency.WaitAsync(Token).ConfigureAwait(false);
                            entered = true;
                            await action(Token).ConfigureAwait(false);
                        }
                        catch (OperationCanceledException) when (Token.IsCancellationRequested) { }
                        catch (Exception ex) { logger.Error("MQTT Tag gateway background operation failed.", ex); }
                        finally
                        {
                            if (entered) _concurrency.Release();
                        }
                    });
                    _work[id] = task;
                    _ = task.ContinueWith(
                        completed => { Task ignored; _work.TryRemove(id, out ignored); },
                        CancellationToken.None,
                        TaskContinuationOptions.ExecuteSynchronously,
                        TaskScheduler.Default);
                    return true;
                }
            }

            public void StopAccepting()
            {
                lock (_sync) _accepting = false;
            }

            public void Cancel()
            {
                if (!_stopSource.IsCancellationRequested) _stopSource.Cancel();
            }

            public Task[] SnapshotWork()
            {
                lock (_sync) return _work.Values.ToArray();
            }

            public void Dispose()
            {
                if (Interlocked.Exchange(ref _disposed, 1) != 0) return;
                _concurrency.Dispose();
                _stopSource.Dispose();
            }
        }
    }
}
