using System.Collections.Generic;
using IndustrialCommSdk.Abstractions;
using IndustrialCommSdk.Diagnostics;
using IndustrialCommSdk.Runtime.Configuration;

namespace IndustrialCommSdk.Protocols.Ads
{
    /// <summary>Industrial SDK 的 TwinCAT ADS 协议 Provider。</summary>
    public sealed class AdsProtocolProvider : IndustrialProtocolProvider<AdsSettings>
    {
        public override string Protocol { get { return "ads"; } }

        protected override IReadOnlyList<string> Validate(AdsSettings settings)
        {
            return Errors(
                settings.Port < 1 || settings.Port > 65535 ? "port must be between 1 and 65535." : null,
                settings.ConnectTimeoutMilliseconds <= 0 ? "connectTimeoutMilliseconds must be positive." : null,
                settings.MaxBatchItems <= 0 ? "maxBatchItems must be positive." : null,
                settings.MaxBatchPayloadBytes < 4096 ? "maxBatchPayloadBytes must be at least 4096." : null,
                string.IsNullOrWhiteSpace(settings.AmsNetId) ? null : ValidateAmsNetId(settings.AmsNetId));
        }

        protected override IIndustrialClient CreateClient(IndustrialDeviceConfig device, AdsSettings settings, IIndustrialLogger logger)
        {
            return new AdsClient(new AdsClientOptions
            {
                DeviceId = device.EffectiveDeviceId,
                AmsNetId = settings.AmsNetId,
                Port = settings.Port,
                ConnectTimeoutMilliseconds = settings.ConnectTimeoutMilliseconds,
                EnableSumCommands = settings.EnableSumCommands,
                MaxBatchItems = settings.MaxBatchItems,
                MaxBatchPayloadBytes = settings.MaxBatchPayloadBytes,
                ValidateTargetStateOnConnect = settings.ValidateTargetStateOnConnect,
                SynchronizeNotifications = settings.SynchronizeNotifications,
                OperationTimeoutMilliseconds = device.Runtime.OperationTimeoutMilliseconds,
            }, logger);
        }

        internal static string ValidateAmsNetId(string value)
        {
            var parts = (value ?? string.Empty).Trim().Split('.');
            if (parts.Length != 6) return "amsNetId must contain six decimal octets.";
            for (var i = 0; i < parts.Length; i++)
            {
                int octet;
                if (!int.TryParse(parts[i], out octet) || octet < 0 || octet > 255)
                    return "amsNetId must contain six decimal octets between 0 and 255.";
            }
            return null;
        }
    }
}
