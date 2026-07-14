using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;
using IndustrialCommSdk.Abstractions;
using IndustrialCommSdk.Exceptions;

namespace IndustrialCommSdk.Protocols.Modbus
{
    /// <summary>
    /// 三菱 PLC 的 Modbus TCP 设备配置文件。
    /// </summary>
    /// <remarks>
    /// <para>
    /// 本类根据三菱 PLC 的官方 Modbus 寄存器映射表，将 PLC 地址转换为
    /// Modbus 协议中的绝对地址。
    /// </para>
    /// <para>
    /// 支持的地址类型：
    /// <list type="table">
    ///   <item><term>Y（输出点）</term><description>线圈，基址 0，1024 点，十六进制索引</description></item>
    ///   <item><term>M（中间继电器）</term><description>线圈，基址 8192，7680 点，十进制索引</description></item>
    ///   <item><term>SM（特殊继电器）</term><description>线圈，基址 20480，2048 点，十进制索引</description></item>
    ///   <item><term>L（锁存继电器）</term><description>线圈，基址 22528，7680 点，十进制索引</description></item>
    ///   <item><term>B（辅助继电器）</term><description>线圈，基址 30720，256 点，十进制索引</description></item>
    ///   <item><term>X（输入点）</term><description>离散量输入，基址 0，1024 点，十六进制索引</description></item>
    ///   <item><term>D（数据寄存器）</term><description>保持寄存器，基址 0，8000 点，十进制索引</description></item>
    ///   <item><term>SD（特殊数据寄存器）</term><description>保持寄存器，基址 20480，10000 点，十进制索引</description></item>
    ///   <item><term>W（链接寄存器）</term><description>保持寄存器，基址 30720，512 点，十六进制索引</description></item>
    ///   <item><term>SW（特殊链接寄存器）</term><description>保持寄存器，基址 40960，512 点，十六进制索引</description></item>
    ///   <item><term>TN（定时器当前值）</term><description>保持寄存器，基址 53248，512 点，十进制索引</description></item>
    /// </list>
    /// </para>
    /// <para>
    /// 计算公式：Modbus 绝对地址 = 基址偏移 + 索引
    /// </para>
    /// </remarks>
    public sealed class MitsubishiModbusTcpProfile : IModbusDeviceProfile
    {
        /// <summary>
        /// 地址映射规则字典。
        /// Item1: Modbus 区域, Item2: 基址偏移, Item3: 最大点数, Item4: 是否十六进制索引
        /// </summary>
        private static readonly Dictionary<string, Tuple<ModbusArea, ushort, int, bool>> AddressRules = new Dictionary<string, Tuple<ModbusArea, ushort, int, bool>>(StringComparer.OrdinalIgnoreCase)
        {
            // 线圈 (Coil)
            { "Y",  Tuple.Create(ModbusArea.Coil,            (ushort)0,     1024, true)  }, // 输出点，基址 0
            { "M",  Tuple.Create(ModbusArea.Coil,            (ushort)8192,  7680, false) }, // 中间继电器，基址 8192
            { "SM", Tuple.Create(ModbusArea.Coil,            (ushort)20480, 2048, false) }, // 特殊继电器，基址 20480
            { "L",  Tuple.Create(ModbusArea.Coil,            (ushort)22528, 7680, false) }, // 锁存继电器，基址 22528
            { "B",  Tuple.Create(ModbusArea.Coil,            (ushort)30720, 256,  false) }, // 辅助继电器，基址 30720
            // 离散量输入 (DiscreteInput)
            { "X",  Tuple.Create(ModbusArea.DiscreteInput,   (ushort)0,     1024, true)  }, // 输入点，基址 0
            // 保持寄存器 (HoldingRegister)
            { "D",  Tuple.Create(ModbusArea.HoldingRegister, (ushort)0,     8000, false) }, // 数据寄存器，基址 0
            { "SD", Tuple.Create(ModbusArea.HoldingRegister, (ushort)20480, 10000,false) }, // 特殊数据寄存器，基址 20480
            { "W",  Tuple.Create(ModbusArea.HoldingRegister, (ushort)30720, 512,  true)  }, // 链接寄存器，基址 30720
            { "SW", Tuple.Create(ModbusArea.HoldingRegister, (ushort)40960, 512,  true)  }, // 特殊链接寄存器，基址 40960
            { "TN", Tuple.Create(ModbusArea.HoldingRegister, (ushort)53248, 512,  false) }, // 定时器当前值，基址 53248
        };

        private static readonly Regex AddressPattern = new Regex(@"^(?<prefix>[A-Z]{1,2})(?<index>[0-9A-F]+)$", RegexOptions.Compiled | RegexOptions.IgnoreCase);

        public string Key
        {
            get { return "mitsubishi-modbus-tcp"; }
        }

        public string DisplayName
        {
            get { return "Mitsubishi Modbus TCP"; }
        }

        public string DefaultAddress
        {
            get { return "D100"; }
        }

        public string ExampleAddresses
        {
            get { return "D100, SD0, W10, TN50, M0, SM100, L30, B0, X10, Y20"; }
        }

        /// <summary>
        /// 将三菱 PLC 的地址字符串解析为 <see cref="ModbusAddress"/> 对象。
        /// </summary>
        /// <param name="address">
        /// 要解析的地址字符串，格式为 "A123" 或 "AB123"，其中前缀为 1-2 个字母
        /// （Y/M/SM/L/B/X/D/SD/W/SW/TN），后跟索引号。
        /// X、Y、W、SW 使用十六进制索引，其余使用十进制。
        /// 例如："D100"、"X10"、"SM50"、"TN0"。
        /// </param>
        /// <returns>
        /// 解析后的 <see cref="ModbusAddress"/> 对象，包含 <see cref="ModbusArea"/> 区域
        /// 和计算得到的 Modbus 绝对地址（基址偏移 + 索引）。
        /// </returns>
        /// <exception cref="IndustrialAddressParseException">
        /// 地址为 null/空、格式错误、前缀不支持、索引无效或超出范围时抛出。
        /// </exception>
        public ModbusAddress ParseAddress(string address)
        {
            if (string.IsNullOrWhiteSpace(address))
            {
                throw new IndustrialAddressParseException("Address is required.");
            }

            var normalized = address.Trim().ToUpperInvariant();
            var match = AddressPattern.Match(normalized);
            if (!match.Success)
            {
                throw new IndustrialAddressParseException(string.Format("Unsupported Mitsubishi Modbus variable: {0}", address));
            }

            var prefix = match.Groups["prefix"].Value;
            Tuple<ModbusArea, ushort, int, bool> rule;
            if (!AddressRules.TryGetValue(prefix, out rule))
            {
                throw new IndustrialAddressParseException(string.Format("Unsupported Mitsubishi Modbus variable type: {0}", prefix));
            }

            int index;
            try
            {
                index = Convert.ToInt32(match.Groups["index"].Value, rule.Item4 ? 16 : 10);
            }
            catch (Exception ex)
            {
                throw new IndustrialAddressParseException("Invalid Mitsubishi Modbus variable index.", ex);
            }

            if (index < 0 || index >= rule.Item3)
            {
                throw new IndustrialAddressParseException("Mitsubishi Modbus variable index out of range.");
            }

            return new ModbusAddress(rule.Item1, (ushort)(rule.Item2 + index));
        }

        public ushort[] NormalizeRegistersForRead(DataType dataType, ushort[] registers)
        {
            return registers;
        }

        public ushort[] NormalizeRegistersForWrite(DataType dataType, ushort[] registers)
        {
            return registers;
        }
    }
}
