using System.Text;
using System.Collections.Generic;
using IndustrialCommSdk.Abstractions;
using IndustrialCommSdk.Runtime.Configuration;
using IndustrialCommSdk.Diagnostics;

namespace IndustrialCommSdk.Protocols.Mqtt
{
    public sealed class MqttSettings : IProtocolSettings
    {
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
        public int QualityOfService { get; set; }
        public bool Retain { get; set; }
        public int ConnectTimeoutMilliseconds { get; set; } = 5000;
        public int KeepAliveSeconds { get; set; } = 30;
        public bool CleanSession { get; set; } = true;
        public bool AutoReconnect { get; set; }
        public int ReconnectInitialDelayMilliseconds { get; set; } = 1000;
        public int ReconnectMaxDelayMilliseconds { get; set; } = 30000;
        public int MaxApplicationMessagePayloadBytes { get; set; } = 1024 * 1024;
        public int MaxCachedTopics { get; set; } = 10000;
        public string WillTopic { get; set; }
        public string WillPayload { get; set; }
        public int WillQualityOfService { get; set; }
        public bool WillRetain { get; set; }
    }

    public sealed class MqttProtocolProvider : IndustrialProtocolProvider<MqttSettings>
    {
        public override string Protocol { get { return "mqtt"; } }

        protected override IReadOnlyList<string> Validate(MqttSettings settings)
        {
            return Errors(
                string.IsNullOrWhiteSpace(settings.Host) ? "host is required." : null,
                settings.Port < 1 || settings.Port > 65535 ? "port must be between 1 and 65535." : null,
                settings.QualityOfService < 0 || settings.QualityOfService > 2 ? "qualityOfService must be between 0 and 2." : null,
                settings.WillQualityOfService < 0 || settings.WillQualityOfService > 2 ? "willQualityOfService must be between 0 and 2." : null,
                settings.ConnectTimeoutMilliseconds <= 0 ? "connectTimeoutMilliseconds must be positive." : null,
                settings.KeepAliveSeconds <= 0 ? "keepAliveSeconds must be positive." : null,
                settings.ReconnectInitialDelayMilliseconds <= 0 ? "reconnectInitialDelayMilliseconds must be positive." : null,
                settings.ReconnectMaxDelayMilliseconds < settings.ReconnectInitialDelayMilliseconds ? "reconnectMaxDelayMilliseconds must be greater than or equal to reconnectInitialDelayMilliseconds." : null,
                settings.MaxApplicationMessagePayloadBytes <= 0 ? "maxApplicationMessagePayloadBytes must be positive." : null,
                settings.MaxCachedTopics <= 0 ? "maxCachedTopics must be positive." : null,
                settings.WillPayload != null && string.IsNullOrWhiteSpace(settings.WillTopic) ? "willTopic is required when willPayload is configured." : null);
        }

        protected override IIndustrialClient CreateClient(IndustrialDeviceConfig device, MqttSettings settings, IIndustrialLogger logger)
        {
            return new MqttClient(new MqttClientOptions
            {
                DeviceId = device.EffectiveDeviceId,
                Host = settings.Host,
                Port = settings.Port,
                ClientId = settings.ClientId,
                Username = settings.Username,
                Password = settings.Password,
                UseTls = settings.UseTls,
                TlsTargetHost = settings.TlsTargetHost,
                AllowUntrustedCertificates = settings.AllowUntrustedCertificates,
                IgnoreCertificateChainErrors = settings.IgnoreCertificateChainErrors,
                IgnoreCertificateRevocationErrors = settings.IgnoreCertificateRevocationErrors,
                QualityOfService = settings.QualityOfService,
                Retain = settings.Retain,
                ConnectTimeoutMilliseconds = settings.ConnectTimeoutMilliseconds,
                OperationTimeoutMilliseconds = device.Runtime.OperationTimeoutMilliseconds,
                KeepAliveSeconds = settings.KeepAliveSeconds,
                CleanSession = settings.CleanSession,
                AutoReconnect = settings.AutoReconnect,
                ReconnectInitialDelayMilliseconds = settings.ReconnectInitialDelayMilliseconds,
                ReconnectMaxDelayMilliseconds = settings.ReconnectMaxDelayMilliseconds,
                MaxApplicationMessagePayloadBytes = settings.MaxApplicationMessagePayloadBytes,
                MaxCachedTopics = settings.MaxCachedTopics,
                WillTopic = settings.WillTopic,
                WillPayload = settings.WillPayload == null ? null : Encoding.UTF8.GetBytes(settings.WillPayload),
                WillQualityOfService = settings.WillQualityOfService,
                WillRetain = settings.WillRetain,
            }, logger);
        }
    }
}
