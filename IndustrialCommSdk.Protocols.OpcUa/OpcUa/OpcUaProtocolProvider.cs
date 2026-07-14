using System.Collections.Generic;
using IndustrialCommSdk.Abstractions;
using IndustrialCommSdk.Configuration;
using IndustrialCommSdk.Diagnostics;

namespace IndustrialCommSdk.Protocols.OpcUa
{
    public sealed class OpcUaSettings : IProtocolSettings
    {
        public string EndpointUrl { get; set; }
        public string Username { get; set; }
        public string Password { get; set; }
        public bool UseSecurity { get; set; }
        public bool AutoAcceptUntrustedCertificates { get; set; } = true;
        public int ConnectTimeoutMilliseconds { get; set; } = 10000;
        public int SessionTimeoutMilliseconds { get; set; } = 60000;
    }

    public sealed class OpcUaProtocolProvider : IndustrialProtocolProvider<OpcUaSettings>
    {
        public override string Protocol { get { return "opc-ua"; } }

        protected override IReadOnlyList<string> Validate(OpcUaSettings settings)
        {
            return Errors(
                string.IsNullOrWhiteSpace(settings.EndpointUrl) ? "endpointUrl is required." : null,
                settings.ConnectTimeoutMilliseconds <= 0 ? "connectTimeoutMilliseconds must be positive." : null,
                settings.SessionTimeoutMilliseconds <= 0 ? "sessionTimeoutMilliseconds must be positive." : null);
        }

        protected override IIndustrialClient CreateClient(IndustrialDeviceConfig device, OpcUaSettings settings, IIndustrialLogger logger)
        {
            return new OpcUaClient(new OpcUaClientOptions
            {
                DeviceId = device.EffectiveDeviceId,
                EndpointUrl = settings.EndpointUrl,
                Username = settings.Username,
                Password = settings.Password,
                UseSecurity = settings.UseSecurity,
                AutoAcceptUntrustedCertificates = settings.AutoAcceptUntrustedCertificates,
                ConnectTimeoutMilliseconds = settings.ConnectTimeoutMilliseconds,
                SessionTimeoutMilliseconds = settings.SessionTimeoutMilliseconds,
                OperationTimeoutMilliseconds = device.Runtime.OperationTimeoutMilliseconds,
            }, logger);
        }
    }
}
