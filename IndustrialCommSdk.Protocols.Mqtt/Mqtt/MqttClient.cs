using System;
using System.Collections.Concurrent;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using IndustrialCommSdk.Abstractions;
using IndustrialCommSdk.Diagnostics;
using IndustrialCommSdk.Exceptions;
using IndustrialCommSdk.Runtime;
using IndustrialCommSdk.Runtime.Polling;
using IndustrialCommSdk.Protocols.Common;
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
        public int QualityOfService { get; set; }
        public bool Retain { get; set; }
        public int ConnectTimeoutMilliseconds { get; set; } = 5000;
        public int OperationTimeoutMilliseconds { get; set; } = 5000;
    }

    /// <summary>MQTT 客户端。地址映射为 Topic；写入发布消息，读取返回订阅到的最新消息。</summary>
    public sealed class MqttClient : IndustrialClientBase
    {
        private readonly MqttClientOptions _options;
        private readonly IMqttClient _client;
        private readonly ConcurrentDictionary<string, byte[]> _latest = new ConcurrentDictionary<string, byte[]>(StringComparer.Ordinal);
        private readonly ConcurrentDictionary<string, TaskCompletionSource<byte[]>> _waiters = new ConcurrentDictionary<string, TaskCompletionSource<byte[]>>(StringComparer.Ordinal);

        public MqttClient(MqttClientOptions options, IIndustrialLogger logger = null, IPollingScheduler pollingScheduler = null)
            : base(GetDeviceId(options), ProtocolKind.Mqtt, pollingScheduler ?? new PollingScheduler(logger),
                logger ?? NullIndustrialLogger.Instance, options.OperationTimeoutMilliseconds)
        {
            _options = options;
            if (string.IsNullOrWhiteSpace(options.Host)) throw new ArgumentException("MQTT host is required.", nameof(options));
            if (options.Port <= 0 || options.Port > 65535) throw new ArgumentOutOfRangeException(nameof(options.Port));
            if (options.QualityOfService < 0 || options.QualityOfService > 2) throw new ArgumentOutOfRangeException(nameof(options.QualityOfService));
            if (options.ConnectTimeoutMilliseconds <= 0 || options.OperationTimeoutMilliseconds <= 0)
                throw new ArgumentOutOfRangeException(nameof(options), "Timeouts must be positive.");
            _client = new MqttFactory().CreateMqttClient();
            _client.ApplicationMessageReceivedAsync += OnMessageAsync;
        }

        private static string GetDeviceId(MqttClientOptions options)
        {
            if (options == null) throw new ArgumentNullException(nameof(options));
            if (string.IsNullOrWhiteSpace(options.DeviceId)) throw new ArgumentException("Device ID is required.", nameof(options));
            return options.DeviceId;
        }

        public override bool IsConnected { get { return _client.IsConnected; } }

        protected override async Task ConnectCoreAsync(CancellationToken cancellationToken)
        {
            if (_client.IsConnected) await _client.DisconnectAsync(new MqttClientDisconnectOptions(), cancellationToken).ConfigureAwait(false);
            var builder = new MqttClientOptionsBuilder()
                .WithClientId(string.IsNullOrWhiteSpace(_options.ClientId) ? "IndustrialCommSdk-" + Guid.NewGuid().ToString("N") : _options.ClientId)
                .WithTcpServer(_options.Host, _options.Port)
                .WithCleanSession();
            if (!string.IsNullOrWhiteSpace(_options.Username)) builder.WithCredentials(_options.Username, _options.Password);
            if (_options.UseTls) builder.WithTlsOptions(new MqttClientTlsOptions { UseTls = true });
            try { await _client.ConnectAsync(builder.Build(), cancellationToken).ConfigureAwait(false); }
            catch (OperationCanceledException) { throw; }
            catch (Exception ex) { throw new IndustrialConnectionException("Failed to connect MQTT broker.", ex); }
        }

        protected override async Task DisconnectCoreAsync(CancellationToken cancellationToken)
        {
            if (_client.IsConnected) await _client.DisconnectAsync(new MqttClientDisconnectOptions(), cancellationToken).ConfigureAwait(false);
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
                    var filter = new MqttTopicFilterBuilder().WithTopic(request.Address).WithQualityOfServiceLevel(ToQos()).Build();
                    await _client.SubscribeAsync(new MqttClientSubscribeOptionsBuilder().WithTopicFilter(filter).Build(), cancellationToken).ConfigureAwait(false);
                    using (cancellationToken.Register(() => waiter.TrySetCanceled())) payload = await waiter.Task.ConfigureAwait(false);
                }
                finally { TaskCompletionSource<byte[]> ignored; _waiters.TryRemove(request.Address, out ignored); }
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

        private Task OnMessageAsync(MqttApplicationMessageReceivedEventArgs args)
        {
            var bytes = args.ApplicationMessage.PayloadSegment.ToArray();
            _latest[args.ApplicationMessage.Topic] = bytes;
            TaskCompletionSource<byte[]> waiter;
            if (_waiters.TryGetValue(args.ApplicationMessage.Topic, out waiter)) waiter.TrySetResult(bytes);
            return Task.CompletedTask;
        }

        private MqttQualityOfServiceLevel ToQos() { return (MqttQualityOfServiceLevel)_options.QualityOfService; }
        private void EnsureConnected() { if (!_client.IsConnected) throw new IndustrialConnectionException("MQTT client is not connected."); }
        protected override void DisposeCore() { _client.ApplicationMessageReceivedAsync -= OnMessageAsync; _client.Dispose(); }
    }
}
