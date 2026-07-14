using System.Collections.Generic;
using IndustrialCommSdk.Abstractions;
using IndustrialCommSdk.Configuration;
using IndustrialCommSdk.Diagnostics;

namespace IndustrialCommSdk.Protocols.Mc
{
    public sealed class MitsubishiMcSettings : IProtocolSettings
    {
        public string Host { get; set; }
        public int Port { get; set; } = 5000;
        public int SendTimeoutMilliseconds { get; set; } = 3000;
        public int ReceiveTimeoutMilliseconds { get; set; } = 5000;
    }

    public sealed class MitsubishiMcProtocolProvider : IndustrialProtocolProvider<MitsubishiMcSettings>
    {
        public override string Protocol { get { return "mitsubishi-mc"; } }

        protected override IReadOnlyList<string> Validate(MitsubishiMcSettings settings)
        {
            return Errors(
                string.IsNullOrWhiteSpace(settings.Host) ? "host is required." : null,
                settings.Port < 1 || settings.Port > 65535 ? "port must be between 1 and 65535." : null,
                settings.SendTimeoutMilliseconds <= 0 ? "sendTimeoutMilliseconds must be positive." : null,
                settings.ReceiveTimeoutMilliseconds <= 0 ? "receiveTimeoutMilliseconds must be positive." : null);
        }

        protected override IIndustrialClient CreateClient(IndustrialDeviceConfig device, MitsubishiMcSettings settings, IIndustrialLogger logger)
        {
            return new MitsubishiMcClient(new MitsubishiMcClientOptions
            {
                DeviceId = device.EffectiveDeviceId,
                Host = settings.Host,
                Port = settings.Port,
                SendTimeoutMilliseconds = settings.SendTimeoutMilliseconds,
                ReceiveTimeoutMilliseconds = settings.ReceiveTimeoutMilliseconds,
                OperationTimeoutMilliseconds = device.Runtime.OperationTimeoutMilliseconds,
            }, logger);
        }
    }
}
