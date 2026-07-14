using System.IO.Ports;
using IndustrialCommSdk.Abstractions;

namespace IndustrialCommSdk.Protocols.Modbus
{
    /// <summary>
    /// Modbus RTU 串口客户端的配置选项。
    /// </summary>
    public sealed class ModbusRtuClientOptions
    {
        /// <summary>
        /// 获取或设置设备标识符，用于在系统中唯一标识该设备。
        /// </summary>
        public string DeviceId { get; set; }

        /// <summary>
        /// 获取或设置串口名称（如 COM1、COM3 等）。
        /// </summary>
        public string PortName { get; set; }

        /// <summary>
        /// 获取或设置串口波特率。默认值为 9600。
        /// </summary>
        public int BaudRate { get; set; } = 9600;

        /// <summary>
        /// 获取或设置数据位长度。Modbus RTU 固定使用 8 数据位。
        /// </summary>
        public int DataBits { get; set; } = 8;

        /// <summary>
        /// 获取或设置奇偶校验方式。默认值为 Modbus 串行规范推荐的 <see cref="System.IO.Ports.Parity.Even"/>。
        /// </summary>
        public Parity Parity { get; set; } = Parity.Even;

        /// <summary>
        /// 获取或设置停止位数量。默认值为 <see cref="System.IO.Ports.StopBits.One"/>。
        /// </summary>
        public StopBits StopBits { get; set; } = StopBits.One;

        /// <summary>
        /// 获取或设置串口读取超时（毫秒）。默认值为 3000。
        /// </summary>
        public int ReadTimeout { get; set; } = 3000;

        /// <summary>
        /// 获取或设置串口写入超时（毫秒）。默认值为 3000。
        /// </summary>
        public int WriteTimeout { get; set; } = 3000;

        /// <summary>获取或设置 SDK 单次读写操作的默认总超时（毫秒）。</summary>
        public int OperationTimeoutMilliseconds { get; set; } = 5000;

        /// <summary>
        /// 获取或设置通信失败后的重试次数。默认值为 2。
        /// </summary>
        public int Retries { get; set; } = 2;

        /// <summary>
        /// 获取或设置从站返回确认或忙异常后，重试前等待的毫秒数。默认值为 100。
        /// </summary>
        public int WaitToRetryMilliseconds { get; set; } = 100;

        /// <summary>
        /// 获取或设置 Modbus 从站 ID（站号）。默认值为 1。
        /// </summary>
        public byte SlaveId { get; set; } = 1;

        /// <summary>
        /// 获取或设置 Modbus 设备配置文件，用于定义设备的寄存器映射和字节序等特性。默认使用通用 Modbus 配置。
        /// </summary>
        public IModbusDeviceProfile DeviceProfile { get; set; } = ModbusDeviceProfiles.Generic;
    }
}
