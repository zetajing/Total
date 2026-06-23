using System;
using IndustrialCommSdk.Abstractions;
using IndustrialCommSdk.Exceptions;

namespace IndustrialCommSdk.Protocols.Modbus
{
    /// <summary>
    /// 定义 Modbus 协议中的地址区域类型。
    /// </summary>
    /// <remarks>
    /// Modbus 协议将数据分为四种不同的区域：线圈（Coil）、离散量输入（DiscreteInput）、
    /// 输入寄存器（InputRegister）和保持寄存器（HoldingRegister）。
    /// 每种区域对应不同的功能码和访问方式。
    /// </remarks>
    public enum ModbusArea
    {
        /// <summary>
        /// 线圈（Coil）区域，对应 Modbus 功能码 01（读线圈）、05（写单个线圈）、15（写多个线圈）。
        /// 线圈为 1 位（bit）数据，支持读写操作。
        /// </summary>
        Coil = 1,

        /// <summary>
        /// 离散量输入（Discrete Input）区域，对应 Modbus 功能码 02（读离散量输入）。
        /// 离散量输入为 1 位（bit）数据，仅支持只读操作。
        /// </summary>
        DiscreteInput = 2,

        /// <summary>
        /// 输入寄存器（Input Register）区域，对应 Modbus 功能码 04（读输入寄存器）。
        /// 输入寄存器为 16 位（word）数据，仅支持只读操作。
        /// </summary>
        InputRegister = 3,

        /// <summary>
        /// 保持寄存器（Holding Register）区域，对应 Modbus 功能码 03（读保持寄存器）、
        /// 06（写单个保持寄存器）、16（写多个保持寄存器）。
        /// 保持寄存器为 16 位（word）数据，支持读写操作。
        /// </summary>
        HoldingRegister = 4,
    }

    /// <summary>
    /// 表示一个已解析的 Modbus 地址，包含地址区域和基于零的偏移地址。
    /// </summary>
    /// <remarks>
    /// <see cref="ModbusAddress"/> 是地址解析后的结果，由 <see cref="ModbusAddressParser"/>
    /// 或设备配置文件的 <c>ParseAddress</c> 方法生成。
    /// 其中 <see cref="ZeroBasedAddress"/> 是基于零的绝对地址，可直接用于 Modbus 协议通信。
    /// </remarks>
    public sealed class ModbusAddress
    {
        /// <summary>
        /// 使用指定的地址区域和基于零的偏移地址初始化 <see cref="ModbusAddress"/> 的新实例。
        /// </summary>
        /// <param name="area">地址所属的 Modbus 区域（<see cref="ModbusArea"/>）。</param>
        /// <param name="zeroBasedAddress">基于零的绝对偏移地址。</param>
        public ModbusAddress(ModbusArea area, ushort zeroBasedAddress)
        {
            Area = area;
            ZeroBasedAddress = zeroBasedAddress;
        }

        /// <summary>
        /// 获取地址所属的 Modbus 区域。
        /// </summary>
        /// <value>
        /// <see cref="ModbusArea"/> 枚举值之一，表示该地址属于线圈、离散量输入、输入寄存器或保持寄存器。
        /// </value>
        public ModbusArea Area { get; private set; }

        /// <summary>
        /// 获取基于零的绝对偏移地址。
        /// </summary>
        /// <value>
        /// 基于零的 16 位无符号整数地址，可直接作为 Modbus 协议中的寄存器或线圈索引使用。
        /// </value>
        public ushort ZeroBasedAddress { get; private set; }

        /// <summary>
        /// 获取一个值，指示当前地址是否属于位（bit）区域。
        /// </summary>
        /// <value>
        /// 如果 <see cref="Area"/> 为 <see cref="ModbusArea.Coil"/> 或
        /// <see cref="ModbusArea.DiscreteInput"/>，则为 <c>true</c>；否则为 <c>false</c>。
        /// </value>
        /// <remarks>
        /// 位区域中的地址对应单个位（bit）数据，每个地址仅占用 1 位。
        /// </remarks>
        public bool IsBitArea { get { return Area == ModbusArea.Coil || Area == ModbusArea.DiscreteInput; } }

        /// <summary>
        /// 获取一个值，指示当前地址是否属于寄存器（word）区域。
        /// </summary>
        /// <value>
        /// 如果 <see cref="Area"/> 为 <see cref="ModbusArea.HoldingRegister"/> 或
        /// <see cref="ModbusArea.InputRegister"/>，则为 <c>true</c>；否则为 <c>false</c>。
        /// </value>
        /// <remarks>
        /// 寄存器区域中的地址对应 16 位（word）数据，每个地址占用 1 个寄存器（16 位）。
        /// </remarks>
        public bool IsRegisterArea { get { return Area == ModbusArea.HoldingRegister || Area == ModbusArea.InputRegister; } }
    }

    /// <summary>
    /// Modbus 地址解析器，实现 <see cref="IAddressParser"/> 接口。
    /// </summary>
    /// <remarks>
    /// <see cref="ModbusAddressParser"/> 将设备相关的地址字符串（如 "D100"、"M0"）
    /// 解析为通用的 <see cref="ModbusAddress"/> 对象。它内部依赖于 <see cref="IModbusDeviceProfile"/>
    /// 提供的地址映射规则进行解析。
    /// <para>
    /// 如果未指定设备配置文件，默认使用 <see cref="ModbusDeviceProfiles.InovanceEasyPlc"/> 配置。
    /// </para>
    /// </remarks>
    public sealed class ModbusAddressParser : IAddressParser
    {
        private readonly IModbusDeviceProfile _deviceProfile;

        /// <summary>
        /// 使用指定的设备配置文件初始化 <see cref="ModbusAddressParser"/> 的新实例。
        /// </summary>
        /// <param name="deviceProfile">
        /// 设备配置文件，用于定义地址解析规则。
        /// 如果为 <c>null</c>，则默认使用 <see cref="ModbusDeviceProfiles.InovanceEasyPlc"/>。
        /// </param>
        public ModbusAddressParser(IModbusDeviceProfile deviceProfile = null)
        {
            _deviceProfile = deviceProfile ?? ModbusDeviceProfiles.InovanceEasyPlc;
        }

        /// <summary>
        /// 将地址字符串解析为 <see cref="ModbusAddress"/> 对象。
        /// </summary>
        /// <param name="address">
        /// 要解析的地址字符串，例如 "D100"、"M0"、"X17" 等。
        /// 具体格式取决于设备配置文件的地址映射规则。
        /// </param>
        /// <returns>
        /// 解析后的 <see cref="ModbusAddress"/> 对象，包含地址区域和基于零的偏移地址。
        /// </returns>
        /// <exception cref="IndustrialAddressParseException">
        /// 当 <paramref name="address"/> 为 <c>null</c>、空字符串或格式无法识别时抛出。
        /// </exception>
        public object Parse(string address)
        {
            if (string.IsNullOrWhiteSpace(address))
            {
                throw new IndustrialAddressParseException("Address is required.");
            }

            return _deviceProfile.ParseAddress(address);
        }
    }
}
