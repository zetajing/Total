using System.Collections.Generic;
using IndustrialCommSdk.Abstractions;
using IndustrialCommSdk.Runtime.Configuration;
using IndustrialCommSdk.Diagnostics;
using S7.Net;

namespace IndustrialCommSdk.Protocols.S7
{
    public sealed class SiemensS7Settings : IProtocolSettings
    {
        public string Host { get; set; }
        public CpuType CpuType { get; set; } = CpuType.S71200;
        public short Rack { get; set; }
        public short Slot { get; set; } = 1;
        public int ConnectTimeoutMilliseconds { get; set; } = 5000;
    }

    public sealed class SiemensS7ProtocolProvider : IndustrialProtocolProvider<SiemensS7Settings>
    {
        public override string Protocol { get { return "siemens-s7"; } }

        protected override IReadOnlyList<string> Validate(SiemensS7Settings settings)
        {
            return Errors(
                string.IsNullOrWhiteSpace(settings.Host) ? "host is required." : null,
                settings.Rack < 0 ? "rack cannot be negative." : null,
                settings.Slot < 0 ? "slot cannot be negative." : null,
                settings.ConnectTimeoutMilliseconds <= 0 ? "connectTimeoutMilliseconds must be positive." : null);
        }

        protected override IIndustrialClient CreateClient(IndustrialDeviceConfig device, SiemensS7Settings settings, IIndustrialLogger logger)
        {
            return new SiemensS7Client(new SiemensS7ClientOptions
            {
                DeviceId = device.EffectiveDeviceId,
                Host = settings.Host,
                CpuType = settings.CpuType,
                Rack = settings.Rack,
                Slot = settings.Slot,
                ConnectTimeoutMilliseconds = settings.ConnectTimeoutMilliseconds,
                OperationTimeoutMilliseconds = device.Runtime.OperationTimeoutMilliseconds,
            }, logger);
        }
    }
}
