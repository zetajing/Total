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
        /// <summary>线圈（Coil）区域，对应功能码 01/05/15。</summary>
        Coil = 1,
        /// <summary>离散量输入（Discrete Input）区域，对应功能码 02。</summary>
        DiscreteInput = 2,
        /// <summary>输入寄存器（Input Register）区域，对应功能码 04。</summary>
        InputRegister = 3,
        /// <summary>保持寄存器（Holding Register）区域，对应功能码 03/06/16。</summary>
        HoldingRegister = 4,
    }

    /// <summary>
    /// 表示一个已解析的 Modbus 地址，包含地址区域和基于零的偏移地址。
    /// 同时实现平台统一地址接口，便于批量规划、诊断、文档和 Demo 使用。
    /// </summary>
    public sealed class ModbusAddress : IIndustrialAddress
    {
        /// <summary>
        /// 使用指定的地址区域和基于零的偏移地址初始化 <see cref="ModbusAddress"/> 的新实例。
        /// </summary>
        public ModbusAddress(ModbusArea area, ushort zeroBasedAddress)
            : this(area, zeroBasedAddress, null, null)
        {
        }

        /// <summary>
        /// 使用指定的地址区域、偏移地址和地址文本初始化 <see cref="ModbusAddress"/> 的新实例。
        /// </summary>
        public ModbusAddress(ModbusArea area, ushort zeroBasedAddress, string original, string normalized)
        {
            Area = area;
            ZeroBasedAddress = zeroBasedAddress;
            Original = original ?? CreateDefaultText(area, zeroBasedAddress);
            Normalized = normalized ?? NormalizeText(Original, area, zeroBasedAddress);
        }

        /// <summary>获取地址所属的 Modbus 区域。</summary>
        public ModbusArea Area { get; private set; }

        /// <summary>获取基于零的绝对偏移地址。</summary>
        public ushort ZeroBasedAddress { get; private set; }

        /// <summary>原始地址文本。</summary>
        public string Original { get; private set; }

        /// <summary>规范化后的地址文本。</summary>
        public string Normalized { get; private set; }

        /// <summary>平台统一区域名。</summary>
        string IIndustrialAddress.Area { get { return Area.ToString(); } }

        /// <summary>平台统一偏移量。</summary>
        public int Offset { get { return ZeroBasedAddress; } }

        /// <summary>Modbus 位地址没有独立 bit offset；线圈/离散量输入的每个地址本身就是一个 bit。</summary>
        public int? Bit { get { return null; } }

        /// <summary>当前地址是否属于位（bit）区域。</summary>
        public bool IsBitAddress { get { return IsBitArea; } }

        /// <summary>获取一个值，指示当前地址是否属于位（bit）区域。</summary>
        public bool IsBitArea { get { return Area == ModbusArea.Coil || Area == ModbusArea.DiscreteInput; } }

        /// <summary>获取一个值，指示当前地址是否属于寄存器（word）区域。</summary>
        public bool IsRegisterArea { get { return Area == ModbusArea.HoldingRegister || Area == ModbusArea.InputRegister; } }

        /// <summary>返回带有原始输入文本的新地址实例。</summary>
        public ModbusAddress WithSource(string original)
        {
            var normalized = string.IsNullOrWhiteSpace(original)
                ? CreateDefaultText(Area, ZeroBasedAddress)
                : original.Trim().ToUpperInvariant();
            return new ModbusAddress(Area, ZeroBasedAddress, original, normalized);
        }

        public override string ToString()
        {
            return Normalized;
        }

        private static string CreateDefaultText(ModbusArea area, ushort zeroBasedAddress)
        {
            return area + ":" + zeroBasedAddress;
        }

        private static string NormalizeText(string original, ModbusArea area, ushort zeroBasedAddress)
        {
            if (string.IsNullOrWhiteSpace(original)) return CreateDefaultText(area, zeroBasedAddress);
            return original.Trim().ToUpperInvariant();
        }
    }

    /// <summary>
    /// Modbus 地址解析器。旧的 <see cref="IAddressParser"/> 接口仍保留，新的平台内部代码应优先使用强类型接口。
    /// </summary>
    public sealed class ModbusAddressParser : IAddressParser, IAddressParser<ModbusAddress>
    {
        private readonly IModbusDeviceProfile _deviceProfile;

        /// <summary>
        /// 使用指定的设备配置文件初始化 <see cref="ModbusAddressParser"/> 的新实例。
        /// </summary>
        public ModbusAddressParser(IModbusDeviceProfile deviceProfile = null)
        {
            _deviceProfile = deviceProfile ?? ModbusDeviceProfiles.InovanceEasyPlc;
        }

        /// <summary>
        /// 将地址字符串解析为 <see cref="ModbusAddress"/> 对象。
        /// </summary>
        public object Parse(string address)
        {
            return ParseTyped(address);
        }

        ModbusAddress IAddressParser<ModbusAddress>.Parse(string address)
        {
            return ParseTyped(address);
        }

        /// <summary>
        /// 将地址字符串解析为强类型 <see cref="ModbusAddress"/> 对象。
        /// </summary>
        public ModbusAddress ParseTyped(string address)
        {
            if (string.IsNullOrWhiteSpace(address))
            {
                throw new IndustrialAddressParseException("Address is required.");
            }

            var parsed = _deviceProfile.ParseAddress(address);
            if (parsed == null)
            {
                throw new IndustrialAddressParseException("Modbus address parser returned no address.");
            }

            return parsed.WithSource(address);
        }
    }
}