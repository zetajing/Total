using IndustrialCommSdk.Abstractions;
using IndustrialCommSdk.Diagnostics;
using IndustrialCommSdk.Protocols.Mc;
using IndustrialCommSdk.Protocols.Modbus;
using IndustrialCommSdk.Protocols.S7;

namespace IndustrialCommSdk
{
    /// <summary>
    /// Provides small factory helpers for the SDK's built-in protocol clients.
    /// </summary>
    public static class IndustrialClientFactory
    {
        public static IIndustrialClient CreateModbus(
            ModbusTcpClientOptions options,
            IIndustrialLogger logger = null)
        {
            return new ModbusTcpClient(options, logger);
        }

        public static IIndustrialClient CreateModbusRtu(
            ModbusRtuClientOptions options,
            IIndustrialLogger logger = null)
        {
            return new ModbusRtuClient(options, logger);
        }

        public static IIndustrialClient CreateSiemensS7(
            SiemensS7ClientOptions options,
            IIndustrialLogger logger = null)
        {
            return new SiemensS7Client(options, logger);
        }

        public static IIndustrialClient CreateMitsubishiMc(
            MitsubishiMcClientOptions options,
            IIndustrialLogger logger = null)
        {
            return new MitsubishiMcClient(options, logger);
        }
    }
}
