using System.Collections.Generic;
using IndustrialCommSdk.Abstractions;
using IndustrialCommSdk.Configuration;
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
        public int QualityOfService { get; set; }
        public bool Retain { get; set; }
        public int ConnectTimeoutMilliseconds { get; set; } = 5000;
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
                settings.ConnectTimeoutMilliseconds <= 0 ? "connectTimeoutMilliseconds must be positive." : null);
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
                QualityOfService = settings.QualityOfService,
                Retain = settings.Retain,
                ConnectTimeoutMilliseconds = settings.ConnectTimeoutMilliseconds,
                OperationTimeoutMilliseconds = device.Runtime.OperationTimeoutMilliseconds,
            }, logger);
        }
    }
}
