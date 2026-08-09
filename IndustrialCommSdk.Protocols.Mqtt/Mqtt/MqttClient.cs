using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Net.Security;
using System.Security.Authentication;
using System.Security.Cryptography.X509Certificates;
using System.Threading;
using System.Threading.Tasks;
using IndustrialCommSdk.Abstractions;
using IndustrialCommSdk.Diagnostics;
using IndustrialCommSdk.Exceptions;
using IndustrialCommSdk.Protocols.Common;
using IndustrialCommSdk.Runtime;
using IndustrialCommSdk.Runtime.Polling;
using MQTTnet;
using MQTTnet.Client;
using MQTTnet.Protocol;

namespace IndustrialCommSdk.Protocols.Mqtt
{
    public sealed class MqttClientOptions
    {
        public string DeviceId { get; set; }
        public string Host { get; set; }
        public int Port { get; set; } = 1883;
        public string ClientId { get; set; }
        public string Username { get; set; }
        public string Password { get; set; }
        public bool UseTls { get; set; }
        public string TlsTargetHost { get; set; }
        public bool AllowUntrustedCertificates { get; set; }
        public bool IgnoreCertificateChainErrors { get; set; }
        public bool IgnoreCertificateRevocationErrors { get; set; }
        public SslProtocols? TlsProtocols { get; set; }
        public Func<X509Certificate, X509Chain, SslPolicyErrors, bool> CertificateValidationCallback { get; set; }
        public int QualityOfService { get; set; }
        public bool Retain { get; set; }
        public int ConnectTimeoutMilliseconds { get; set; } = 5000;
        public int OperationTimeoutMilliseconds { get; set; } = 5000;
        public int KeepAliveSeconds { get; set; } = 30;
        public bool CleanSession { get; set; } = true;
        public bool AutoReconnect { get; set; }
        public int ReconnectInitialDelayMilliseconds { get; set; } = 1000;
        public int ReconnectMaxDelayMilliseconds { get; set; } = 30000;
        public int MaxApplicationMessagePayloadBytes { get; set; } = 1024 * 1024;
        public int MaxCachedTopics { get; set; } = 10000;
        public string WillTopic { get; set; }
        public byte[] WillPayload { get; set; }
        public int WillQualityOfService { get; set; }
        public bool WillRetain { get; set; }
    }

    public sealed class MqttMessageReceivedEventArgs : EventArgs
    {
        public MqttMessageReceivedEventArgs(string topic, byte[] payload, int qualityOfService, bool retain)
        {
            Topic = topic;
            Payload = payload == null ? new byte[0] : (byte[])payload.Clone();
            QualityOfService = qualityOfService;
            Retain = retain;
            ReceivedUtc = DateTimeOffset.UtcNow;
        }

        public string Topic { get; private set; }
        public byte[] Payload { get; private set; }
        public int QualityOfService { get; private set; }
        public bool Retain { get; private set; }
        public DateTimeOffset ReceivedUtc { get; private set; }
    }

    public sealed class MqttConnectionChangedEventArgs : EventArgs
    {
        public MqttConnectionChangedEventArgs(bool isConnected, string reason, Exception exception)
        {
            IsConnected = isConnected;
            Reason = reason;
            Exception = exception;
            TimestampUtc = DateTimeOffset.UtcNow;
        }

        public bool IsConnected { get; private set; }
        public string Reason { get; private set; }
        public Exception Exception { get; private set; }
        public DateTimeOffset TimestampUtc { get; private set; }
    }

    /// <summary>MQTT 客户端。地址映射为 Topic；写入发布消息，读取返回订阅到的最新消息。</summary>
    public sealed class MqttClient : IndustrialClientBase
    {
        private readonly MqttClientOptions _options;
        private readonly IMqttClient _client;
        private readonly string _clientId;
        private readonly ConcurrentDictionary<string, byte[]> _latest = new ConcurrentDictionary<string, byte[]>(StringComparer.Ordinal);
        private readonly ConcurrentQueue<string> _cacheOrder = new ConcurrentQueue<string>();
        private readonly ConcurrentDictionary<string, TaskCompletionSource<byte[]>> _waiters = new ConcurrentDictionary<string, TaskCompletionSource<byte[]>>(StringComparer.Ordinal);
        private readonly ConcurrentDictionary<string, MqttQualityOfServiceLevel> _subscriptions = new ConcurrentDictionary<string, MqttQualityOfServiceLevel>(StringComparer.Ordinal);
        private readonly object _cacheSync = new object();
        private readonly object _reconnectSync = new object();
        private CancellationTokenSource _reconnectCancellation = new CancellationTokenSource();
        private Task _reconnectTask = Task.CompletedTask;
        private int _manualDisconnect = 1;
        private int _disposeRequested;

        public MqttClient(MqttClientOptions options, IIndustrialLogger logger = null, IPollingScheduler pollingScheduler = null)
            : base(GetDeviceId(options), ProtocolKind.Mqtt, pollingScheduler ?? new PollingScheduler(logger),
                logger ?? NullIndustrialLogger.Instance, options.OperationTimeoutMilliseconds)
        {
            _options = CloneOptions(options);
            ValidateOptions(_options);
            _clientId = string.IsNullOrWhiteSpace(_options.ClientId)
                ? "IndustrialCommSdk-" + Guid.NewGuid().ToString("N")
                : _options.ClientId;
            _client = new MqttFactory().CreateMqttClient();
            _client.ApplicationMessageReceivedAsync += OnMessageAsync;
            _client.DisconnectedAsync += OnDisconnectedAsync;
        }

        public event EventHandler<MqttMessageReceivedEventArgs> MessageReceived;
        public event EventHandler<MqttConnectionChangedEventArgs> ConnectionChanged;

        public override bool IsConnected { get { return _client.IsConnected; } }

        public Task SubscribeTopicAsync(string topicFilter, CancellationToken cancellationToken)
        {
            if (string.IsNullOrWhiteSpace(topicFilter)) throw new ArgumentException("MQTT topic filter is required.", nameof(topicFilter));
            return ExecuteExclusiveAsync(async token =>
            {
                EnsureConnected();
                await SubscribeInternalAsync(topicFilter, ToQos(), token).ConfigureAwait(false);
                _subscriptions[topicFilter] = ToQos();
            }, cancellationToken);
        }

        public Task UnsubscribeTopicAsync(string topicFilter, CancellationToken cancellationToken)
        {
            if (string.IsNullOrWhiteSpace(topicFilter)) throw new ArgumentException("MQTT topic filter is required.", nameof(topicFilter));
            return ExecuteExclusiveAsync(async token =>
            {
                EnsureConnected();
                var options = new MqttClientUnsubscribeOptionsBuilder().WithTopicFilter(topicFilter).Build();
                await _client.UnsubscribeAsync(options, token).ConfigureAwait(false);
                MqttQualityOfServiceLevel ignored;
                _subscriptions.TryRemove(topicFilter, out ignored);
            }, cancellationToken);
        }

        private static string GetDeviceId(MqttClientOptions options)
        {
            if (options == null) throw new ArgumentNullException(nameof(options));
            if (string.IsNullOrWhiteSpace(options.DeviceId)) throw new ArgumentException("Device ID is required.", nameof(options));
            return options.DeviceId;
        }

        private static void ValidateOptions(MqttClientOptions options)
        {
            if (string.IsNullOrWhiteSpace(options.Host)) throw new ArgumentException("MQTT host is required.", nameof(options));
            if (options.Port <= 0 || options.Port > 65535) throw new ArgumentOutOfRangeException(nameof(options.Port));
            if (options.QualityOfService < 0 || options.QualityOfService > 2) throw new ArgumentOutOfRangeException(nameof(options.QualityOfService));
            if (options.WillQualityOfService < 0 || options.WillQualityOfService > 2) throw new ArgumentOutOfRangeException(nameof(options.WillQualityOfService));
            if (options.ConnectTimeoutMilliseconds <= 0 || options.OperationTimeoutMilliseconds <= 0)
                throw new ArgumentOutOfRangeException(nameof(options), "Timeouts must be positive.");
            if (options.KeepAliveSeconds <= 0) throw new ArgumentOutOfRangeException(nameof(options.KeepAliveSeconds));
            if (options.ReconnectInitialDelayMilliseconds <= 0 || options.ReconnectMaxDelayMilliseconds <= 0 ||
                options.ReconnectInitialDelayMilliseconds > options.ReconnectMaxDelayMilliseconds)
                throw new ArgumentOutOfRangeException(nameof(options), "Reconnect delays must be positive and the initial delay cannot exceed the maximum delay.");
            if (options.MaxApplicationMessagePayloadBytes <= 0) throw new ArgumentOutOfRangeException(nameof(options.MaxApplicationMessagePayloadBytes));
            if (options.MaxCachedTopics <= 0) throw new ArgumentOutOfRangeException(nameof(options.MaxCachedTopics));
            if (options.WillPayload != null && string.IsNullOrWhiteSpace(options.WillTopic))
                throw new ArgumentException("WillTopic is required when a will payload is configured.", nameof(options));
            if (options.TlsProtocols.HasValue)
            {
                var protocols = options.TlsProtocols.Value;
                if ((protocols & (SslProtocols.Ssl2 | SslProtocols.Ssl3)) != 0)
                    throw new ArgumentException("SSL 2.0 and SSL 3.0 are not permitted for MQTT TLS connections.", nameof(options));
                if (protocols != SslProtocols.None && (protocols & SslProtocols.Tls12) == 0)
                    throw new ArgumentException("MQTT TLS connections require TLS 1.2 or a system-default protocol set.", nameof(options));
            }
        }

        protected override async Task ConnectCoreAsync(CancellationToken cancellationToken)
        {
            Interlocked.Exchange(ref _manualDisconnect, 0);
            EnsureReconnectCancellationAvailable();

            if (_client.IsConnected)
            {
                Interlocked.Exchange(ref _manualDisconnect, 1);
                await _client.DisconnectAsync(new MqttClientDisconnectOptions(), cancellationToken).ConfigureAwait(false);
                Interlocked.Exchange(ref _manualDisconnect, 0);
            }

            var clientOptions = BuildClientOptions();
            using (var timeoutCancellation = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken))
            {
                timeoutCancellation.CancelAfter(_options.ConnectTimeoutMilliseconds);
                try
                {
                    await _client.ConnectAsync(clientOptions, timeoutCancellation.Token).ConfigureAwait(false);
                    await RestoreSubscriptionsAsync(timeoutCancellation.Token).ConfigureAwait(false);
                    RaiseConnectionChanged(new MqttConnectionChangedEventArgs(true, "Success", null));
                }
                catch (OperationCanceledException ex) when (!cancellationToken.IsCancellationRequested)
                {
                    throw new IndustrialConnectionException(
                        string.Format("MQTT connection timed out after {0} ms.", _options.ConnectTimeoutMilliseconds),
                        new TimeoutException("MQTT connection timed out.", ex));
                }
                catch (OperationCanceledException)
                {
                    throw;
                }
                catch (Exception ex)
                {
                    throw new IndustrialConnectionException("Failed to connect MQTT broker.", ex);
                }
            }
        }

        protected override async Task DisconnectCoreAsync(CancellationToken cancellationToken)
        {
            Interlocked.Exchange(ref _manualDisconnect, 1);
            CancelReconnect();
            if (_client.IsConnected)
                await _client.DisconnectAsync(new MqttClientDisconnectOptions(), cancellationToken).ConfigureAwait(false);
        }

        protected override async Task<DataValue> ReadCoreAsync(ReadRequest request, CancellationToken cancellationToken)
        {
            EnsureConnected();
            byte[] payload;
            if (!_latest.TryGetValue(request.Address, out payload))
            {
                var waiter = new TaskCompletionSource<byte[]>(TaskCreationOptions.RunContinuationsAsynchronously);
                _waiters[request.Address] = waiter;
                try
                {
                    await SubscribeInternalAsync(request.Address, ToQos(), cancellationToken).ConfigureAwait(false);
                    _subscriptions[request.Address] = ToQos();
                    using (cancellationToken.Register(() => waiter.TrySetCanceled()))
                        payload = await waiter.Task.ConfigureAwait(false);
                }
                finally
                {
                    TaskCompletionSource<byte[]> ignored;
                    _waiters.TryRemove(request.Address, out ignored);
                }
            }
            return new DataValue(request.Address, request.DataType, TextValueCodec.Decode(request.DataType, payload), payload,
                QualityStatus.Good, DateTimeOffset.UtcNow, null);
        }

        protected override async Task WriteCoreAsync(WriteRequest request, CancellationToken cancellationToken)
        {
            EnsureConnected();
            var message = new MqttApplicationMessageBuilder().WithTopic(request.Address)
                .WithPayload(TextValueCodec.Encode(request.DataType, request.Value)).WithQualityOfServiceLevel(ToQos())
                .WithRetainFlag(_options.Retain).Build();
            var result = await _client.PublishAsync(message, cancellationToken).ConfigureAwait(false);
            if (result.ReasonCode >= MqttClientPublishReasonCode.UnspecifiedError)
                throw new IndustrialProtocolException("MQTT publish failed: " + result.ReasonCode);
        }

        private MQTTnet.Client.MqttClientOptions BuildClientOptions()
        {
            var builder = new MqttClientOptionsBuilder()
                .WithClientId(_clientId)
                .WithTcpServer(_options.Host, _options.Port)
                .WithCleanSession(_options.CleanSession)
                .WithKeepAlivePeriod(TimeSpan.FromSeconds(_options.KeepAliveSeconds))
                .WithTimeout(TimeSpan.FromMilliseconds(_options.ConnectTimeoutMilliseconds));

            if (!string.IsNullOrWhiteSpace(_options.Username)) builder.WithCredentials(_options.Username, _options.Password);
            if (_options.UseTls)
            {
                var tlsOptions = new MqttClientTlsOptions
                {
                    UseTls = true,
                    TargetHost = string.IsNullOrWhiteSpace(_options.TlsTargetHost) ? _options.Host : _options.TlsTargetHost,
                    AllowUntrustedCertificates = _options.AllowUntrustedCertificates,
                    IgnoreCertificateChainErrors = _options.IgnoreCertificateChainErrors,
                    IgnoreCertificateRevocationErrors = _options.IgnoreCertificateRevocationErrors,
                    SslProtocol = _options.TlsProtocols ?? SslProtocols.Tls12,
                };
                if (_options.CertificateValidationCallback != null)
                {
                    tlsOptions.CertificateValidationHandler = args =>
                        _options.CertificateValidationCallback(args.Certificate, args.Chain, args.SslPolicyErrors);
                }
                builder.WithTlsOptions(tlsOptions);
            }

            if (!string.IsNullOrWhiteSpace(_options.WillTopic))
            {
                builder.WithWillTopic(_options.WillTopic)
                    .WithWillPayload(_options.WillPayload ?? new byte[0])
                    .WithWillQualityOfServiceLevel((MqttQualityOfServiceLevel)_options.WillQualityOfService)
                    .WithWillRetain(_options.WillRetain);
            }

            return builder.Build();
        }

        private async Task SubscribeInternalAsync(string topicFilter, MqttQualityOfServiceLevel qos, CancellationToken cancellationToken)
        {
            var filter = new MqttTopicFilterBuilder().WithTopic(topicFilter).WithQualityOfServiceLevel(qos).Build();
            var options = new MqttClientSubscribeOptionsBuilder().WithTopicFilter(filter).Build();
            var result = await _client.SubscribeAsync(options, cancellationToken).ConfigureAwait(false);
            var failure = result.Items.FirstOrDefault(item => item.ResultCode >= MqttClientSubscribeResultCode.UnspecifiedError);
            if (failure != null)
                throw new IndustrialProtocolException(string.Format("MQTT subscription failed for '{0}': {1}", topicFilter, failure.ResultCode));
        }

        private async Task RestoreSubscriptionsAsync(CancellationToken cancellationToken)
        {
            foreach (var subscription in _subscriptions.ToArray())
                await SubscribeInternalAsync(subscription.Key, subscription.Value, cancellationToken).ConfigureAwait(false);
        }

        private Task OnDisconnectedAsync(MqttClientDisconnectedEventArgs args)
        {
            RaiseConnectionChanged(new MqttConnectionChangedEventArgs(false,
                string.IsNullOrWhiteSpace(args.ReasonString) ? args.Reason.ToString() : args.ReasonString,
                args.Exception));

            if (_options.AutoReconnect && Volatile.Read(ref _manualDisconnect) == 0 && Volatile.Read(ref _disposeRequested) == 0)
                ScheduleReconnect();
            return Task.CompletedTask;
        }

        private Task OnMessageAsync(MqttApplicationMessageReceivedEventArgs args)
        {
            if (args.ApplicationMessage.PayloadSegment.Count > _options.MaxApplicationMessagePayloadBytes)
            {
                Logger.Warn(string.Format("MQTT message dropped because its payload is too large | Device={0} | Topic={1} | Bytes={2} | Limit={3}",
                    DeviceId, args.ApplicationMessage.Topic, args.ApplicationMessage.PayloadSegment.Count,
                    _options.MaxApplicationMessagePayloadBytes));
                return Task.CompletedTask;
            }

            var bytes = args.ApplicationMessage.PayloadSegment.ToArray();
            lock (_cacheSync)
            {
                if (!_latest.TryAdd(args.ApplicationMessage.Topic, bytes))
                {
                    _latest[args.ApplicationMessage.Topic] = bytes;
                }
                else
                {
                    _cacheOrder.Enqueue(args.ApplicationMessage.Topic);
                    TrimCache();
                }
            }
            TaskCompletionSource<byte[]> waiter;
            if (_waiters.TryGetValue(args.ApplicationMessage.Topic, out waiter)) waiter.TrySetResult(bytes);

            RaiseMessageReceived(new MqttMessageReceivedEventArgs(
                args.ApplicationMessage.Topic,
                bytes,
                (int)args.ApplicationMessage.QualityOfServiceLevel,
                args.ApplicationMessage.Retain));
            return Task.CompletedTask;
        }

        private void TrimCache()
        {
            string topic;
            byte[] ignored;
            while (_latest.Count > _options.MaxCachedTopics && _cacheOrder.TryDequeue(out topic))
                _latest.TryRemove(topic, out ignored);
        }

        private static MqttClientOptions CloneOptions(MqttClientOptions source)
        {
            return new MqttClientOptions
            {
                DeviceId = source.DeviceId,
                Host = source.Host,
                Port = source.Port,
                ClientId = source.ClientId,
                Username = source.Username,
                Password = source.Password,
                UseTls = source.UseTls,
                TlsTargetHost = source.TlsTargetHost,
                AllowUntrustedCertificates = source.AllowUntrustedCertificates,
                IgnoreCertificateChainErrors = source.IgnoreCertificateChainErrors,
                IgnoreCertificateRevocationErrors = source.IgnoreCertificateRevocationErrors,
                TlsProtocols = source.TlsProtocols,
                CertificateValidationCallback = source.CertificateValidationCallback,
                QualityOfService = source.QualityOfService,
                Retain = source.Retain,
                ConnectTimeoutMilliseconds = source.ConnectTimeoutMilliseconds,
                OperationTimeoutMilliseconds = source.OperationTimeoutMilliseconds,
                KeepAliveSeconds = source.KeepAliveSeconds,
                CleanSession = source.CleanSession,
                AutoReconnect = source.AutoReconnect,
                ReconnectInitialDelayMilliseconds = source.ReconnectInitialDelayMilliseconds,
                ReconnectMaxDelayMilliseconds = source.ReconnectMaxDelayMilliseconds,
                MaxApplicationMessagePayloadBytes = source.MaxApplicationMessagePayloadBytes,
                MaxCachedTopics = source.MaxCachedTopics,
                WillTopic = source.WillTopic,
                WillPayload = source.WillPayload == null ? null : (byte[])source.WillPayload.Clone(),
                WillQualityOfService = source.WillQualityOfService,
                WillRetain = source.WillRetain,
            };
        }

        private void ScheduleReconnect()
        {
            lock (_reconnectSync)
            {
                if (_reconnectCancellation.IsCancellationRequested || (_reconnectTask != null && !_reconnectTask.IsCompleted)) return;
                var token = _reconnectCancellation.Token;
                _reconnectTask = Task.Run(() => ReconnectLoopAsync(token), token);
            }
        }

        private async Task ReconnectLoopAsync(CancellationToken cancellationToken)
        {
            var delay = _options.ReconnectInitialDelayMilliseconds;
            while (!cancellationToken.IsCancellationRequested && Volatile.Read(ref _manualDisconnect) == 0 && Volatile.Read(ref _disposeRequested) == 0)
            {
                try
                {
                    await Task.Delay(delay, cancellationToken).ConfigureAwait(false);
                    if (_client.IsConnected) return;
                    await ConnectAsync(cancellationToken).ConfigureAwait(false);
                    if (_client.IsConnected) return;
                }
                catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
                {
                    return;
                }
                catch (Exception ex)
                {
                    Logger.Warn(string.Format("MQTT reconnect failed | Device={0} | RetryIn={1}ms | Error={2}",
                        DeviceId, delay, ex.Message));
                }

                delay = (int)Math.Min((long)_options.ReconnectMaxDelayMilliseconds, (long)delay * 2L);
            }
        }

        private void EnsureReconnectCancellationAvailable()
        {
            lock (_reconnectSync)
            {
                if (!_reconnectCancellation.IsCancellationRequested) return;
                _reconnectCancellation.Dispose();
                _reconnectCancellation = new CancellationTokenSource();
                _reconnectTask = Task.CompletedTask;
            }
        }

        private void CancelReconnect()
        {
            lock (_reconnectSync)
            {
                if (!_reconnectCancellation.IsCancellationRequested) _reconnectCancellation.Cancel();
            }
        }

        private void RaiseMessageReceived(MqttMessageReceivedEventArgs args)
        {
            var handler = MessageReceived;
            if (handler == null) return;
            foreach (EventHandler<MqttMessageReceivedEventArgs> subscriber in handler.GetInvocationList())
            {
                try { subscriber(this, args); }
                catch (Exception ex) { Logger.Error("MQTT message event handler failed.", ex); }
            }
        }

        private void RaiseConnectionChanged(MqttConnectionChangedEventArgs args)
        {
            var handler = ConnectionChanged;
            if (handler == null) return;
            foreach (EventHandler<MqttConnectionChangedEventArgs> subscriber in handler.GetInvocationList())
            {
                try { subscriber(this, args); }
                catch (Exception ex) { Logger.Error("MQTT connection event handler failed.", ex); }
            }
        }

        private MqttQualityOfServiceLevel ToQos() { return (MqttQualityOfServiceLevel)_options.QualityOfService; }
        private void EnsureConnected() { if (!_client.IsConnected) throw new IndustrialConnectionException("MQTT client is not connected."); }

        protected override void DisposeCore()
        {
            Interlocked.Exchange(ref _disposeRequested, 1);
            Interlocked.Exchange(ref _manualDisconnect, 1);
            CancelReconnect();
            _client.ApplicationMessageReceivedAsync -= OnMessageAsync;
            _client.DisconnectedAsync -= OnDisconnectedAsync;
            _client.Dispose();
            _reconnectCancellation.Dispose();
        }
    }
}
