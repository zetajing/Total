using System.IO.Ports;
using IndustrialCommSdk.Diagnostics;
using IndustrialCommSdk.Protocols.Mc;
using IndustrialCommSdk.Protocols.Modbus;
using IndustrialCommSdk.Protocols.S7;

namespace IndustrialCommSdk
{
    /// <summary>
    /// 面向普通桌面应用和最小验证程序的协议专用简单入口。
    /// 该类型只负责收敛常用参数，客户端实现、校验和生命周期仍由现有 SDK 负责。
    /// 需要自定义超时、设备映射或底层选项时，应继续使用 <see cref="IndustrialClientFactory"/>。
    /// </summary>
    public static class SimpleClient
    {
        /// <summary>
        /// 创建通用 Modbus TCP 客户端。
        /// 默认端口为 502、站号为 1，并使用不绑定具体厂商的通用地址映射。
        /// </summary>
        public static ModbusTcpClient ModbusTcp(
            string host,
            int port = 502,
            byte slaveId = 1,
            IIndustrialLogger logger = null,
            int operationTimeoutMilliseconds = 5000,
            IModbusDeviceProfile deviceProfile = null)
        {
            return IndustrialClientFactory.ModbusTcp(
                host,
                port,
                slaveId,
                logger: logger,
                deviceProfile: deviceProfile ?? ModbusDeviceProfiles.Generic,
                operationTimeoutMilliseconds: operationTimeoutMilliseconds);
        }

        /// <summary>
        /// 创建通用 Modbus RTU 客户端。
        /// 默认使用 9600 波特率、站号 1、8 数据位、偶校验和 1 停止位。
        /// </summary>
        public static ModbusRtuClient ModbusRtu(
            string portName,
            int baudRate = 9600,
            byte slaveId = 1,
            IIndustrialLogger logger = null,
            int operationTimeoutMilliseconds = 5000)
        {
            return IndustrialClientFactory.ModbusRtu(
                portName,
                baudRate,
                slaveId,
                logger: logger,
                deviceProfile: ModbusDeviceProfiles.Generic,
                dataBits: 8,
                parity: Parity.Even,
                stopBits: StopBits.One,
                operationTimeoutMilliseconds: operationTimeoutMilliseconds);
        }

        /// <summary>
        /// 创建 Siemens S7 客户端。
        /// 默认使用 S7-1200 CPU、机架 0 和插槽 1，适合常见的 S7-1200/1500 直连验证。
        /// </summary>
        public static SiemensS7Client S7(
            string host,
            short rack = 0,
            short slot = 1,
            IIndustrialLogger logger = null,
            int operationTimeoutMilliseconds = 5000)
        {
            return IndustrialClientFactory.SiemensS7(
                host,
                rack: rack,
                slot: slot,
                operationTimeoutMilliseconds: operationTimeoutMilliseconds,
                logger: logger);
        }

        /// <summary>
        /// 创建 Mitsubishi MC 3E 客户端。
        /// 默认端口为 5000，适合 PLC 已启用 MC 二进制 3E 帧监听的场景。
        /// </summary>
        public static MitsubishiMcClient Mc(
            string host,
            int port = 5000,
            int receiveTimeoutMilliseconds = 5000,
            IIndustrialLogger logger = null,
            int operationTimeoutMilliseconds = 5000)
        {
            return IndustrialClientFactory.MitsubishiMc(
                host,
                port,
                receiveTimeoutMilliseconds: receiveTimeoutMilliseconds,
                operationTimeoutMilliseconds: operationTimeoutMilliseconds,
                logger: logger);
        }
    }
}
