using System;
using IndustrialCommSdk.Diagnostics;
using IndustrialCommSdk.Protocols.Mc;
using IndustrialCommSdk.Protocols.Modbus;
using IndustrialCommSdk.Protocols.S7;
using IndustrialCommSdk.Protocols.Socket;

namespace IndustrialCommSdk
{
    /// <summary>
    /// 工业通信客户端工厂类，提供创建各种工业协议客户端的便捷静态方法。
    /// </summary>
    /// <remarks>
    /// 通过此工厂类，您可以统一创建 Modbus TCP、西门子 S7、三菱 MC 以及 Socket 桥接等多种工业通信协议的客户端实例，
    /// 而无需直接实例化各协议的具体实现类。每个工厂方法均支持传入可选的日志记录器。
    /// </remarks>
    public static class IndustrialClientFactory
    {
        /// <summary>
        /// 创建一个 Modbus TCP 协议客户端。
        /// </summary>
        /// <param name="options">Modbus TCP 客户端配置选项，包含远程主机地址、端口号等参数。</param>
        /// <param name="logger">可选的工业通信日志记录器实例，用于记录通信过程中的日志信息。</param>
        /// <returns>返回配置好的 <see cref="ModbusTcpClient"/> 实例。</returns>
        /// <exception cref="ArgumentNullException">当 <paramref name="options"/> 为 null 时抛出。</exception>
        /// <example>
        /// <code>
        /// var options = new ModbusTcpClientOptions("192.168.1.100", 502);
        /// var client = IndustrialClientFactory.CreateModbus(options);
        /// </code>
        /// </example>
        public static ModbusTcpClient CreateModbus(ModbusTcpClientOptions options, IIndustrialLogger logger = null)
        {
            return new ModbusTcpClient(options, logger);
        }

        /// <summary>
        /// 创建一个 Modbus RTU 串口协议客户端。
        /// </summary>
        /// <param name="options">Modbus RTU 客户端配置选项，包含串口名称、波特率等参数。</param>
        /// <param name="logger">可选的工业通信日志记录器实例，用于记录通信过程中的日志信息。</param>
        /// <returns>返回配置好的 <see cref="ModbusRtuClient"/> 实例。</returns>
        /// <exception cref="ArgumentNullException">当 <paramref name="options"/> 为 null 时抛出。</exception>
        public static ModbusRtuClient CreateModbusRtu(ModbusRtuClientOptions options, IIndustrialLogger logger = null)
        {
            return new ModbusRtuClient(options, logger);
        }

        /// <summary>
        /// 创建一个西门子 S7 协议客户端（用于与 SIMATIC S7 系列 PLC 通信）。
        /// </summary>
        /// <param name="options">西门子 S7 客户端配置选项，包含 IP 地址、机架号、槽号等参数。</param>
        /// <param name="logger">可选的工业通信日志记录器实例，用于记录通信过程中的日志信息。</param>
        /// <returns>返回配置好的 <see cref="SiemensS7Client"/> 实例。</returns>
        /// <exception cref="ArgumentNullException">当 <paramref name="options"/> 为 null 时抛出。</exception>
        /// <example>
        /// <code>
        /// var options = new SiemensS7ClientOptions("192.168.1.10", 0, 1);
        /// var client = IndustrialClientFactory.CreateSiemensS7(options);
        /// </code>
        /// </example>
        public static SiemensS7Client CreateSiemensS7(SiemensS7ClientOptions options, IIndustrialLogger logger = null)
        {
            return new SiemensS7Client(options, logger);
        }

        /// <summary>
        /// 创建一个三菱 MC 协议客户端（用于与三菱 MELSEC 系列 PLC 通信）。
        /// </summary>
        /// <param name="options">三菱 MC 客户端配置选项，包含 IP 地址、端口号、网络编号等参数。</param>
        /// <param name="logger">可选的工业通信日志记录器实例，用于记录通信过程中的日志信息。</param>
        /// <returns>返回配置好的 <see cref="MitsubishiMcClient"/> 实例。</returns>
        /// <exception cref="ArgumentNullException">当 <paramref name="options"/> 为 null 时抛出。</exception>
        /// <example>
        /// <code>
        /// var options = new MitsubishiMcClientOptions("192.168.1.20", 6000);
        /// var client = IndustrialClientFactory.CreateMitsubishiMc(options);
        /// </code>
        /// </example>
        public static MitsubishiMcClient CreateMitsubishiMc(MitsubishiMcClientOptions options, IIndustrialLogger logger = null)
        {
            return new MitsubishiMcClient(options, logger);
        }

        /// <summary>
        /// 创建一个 Socket 桥接协议客户端，用于通过自定义 Socket 协议适配器进行通信。
        /// </summary>
        /// <param name="options">Socket 桥接客户端配置选项，包含远程主机地址、端口号等参数。</param>
        /// <param name="adapter">Socket 协议适配器实例，用于自定义协议数据的封装与解析。</param>
        /// <param name="logger">可选的工业通信日志记录器实例，用于记录通信过程中的日志信息。</param>
        /// <returns>返回配置好的 <see cref="SocketBridgeClient"/> 实例。</returns>
        /// <exception cref="ArgumentNullException">
        /// 当 <paramref name="options"/> 或 <paramref name="adapter"/> 为 null 时抛出。
        /// </exception>
        /// <example>
        /// <code>
        /// var options = new SocketBridgeClientOptions("192.168.1.200", 4000);
        /// var adapter = new MyCustomProtocolAdapter();
        /// var client = IndustrialClientFactory.CreateSocketBridge(options, adapter);
        /// </code>
        /// </example>
        public static SocketBridgeClient CreateSocketBridge(SocketBridgeClientOptions options, ISocketProtocolAdapter adapter, IIndustrialLogger logger = null)
        {
            return new SocketBridgeClient(options, adapter, logger);
        }
    }
}
