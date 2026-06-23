using System;
using IndustrialCommSdk.Diagnostics;
using IndustrialCommSdk.Protocols.Mc;
using IndustrialCommSdk.Protocols.Modbus;
using IndustrialCommSdk.Protocols.S7;
using IndustrialCommSdk.Protocols.Socket;

namespace IndustrialCommSdk
{
    public static class IndustrialClientFactory
    {
        public static ModbusTcpClient CreateModbus(ModbusTcpClientOptions options, IIndustrialLogger logger = null)
        {
            return new ModbusTcpClient(options, logger);
        }

        public static SiemensS7Client CreateSiemensS7(SiemensS7ClientOptions options, IIndustrialLogger logger = null)
        {
            return new SiemensS7Client(options, logger);
        }

        public static MitsubishiMcClient CreateMitsubishiMc(MitsubishiMcClientOptions options, IIndustrialLogger logger = null)
        {
            return new MitsubishiMcClient(options, logger);
        }

        public static SocketBridgeClient CreateSocketBridge(SocketBridgeClientOptions options, ISocketProtocolAdapter adapter, IIndustrialLogger logger = null)
        {
            return new SocketBridgeClient(options, adapter, logger);
        }
    }
}
