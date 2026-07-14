using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;
using IndustrialCommSdk.Abstractions;
using IndustrialCommSdk.Exceptions;

namespace IndustrialCommSdk.Protocols.Modbus
{
    /// <summary>
    /// 基于 JSON <see cref="ModbusProfileDefinition"/> 数据驱动的 Modbus 设备配置文件。
    /// 允许在 <c>modbus-profiles.json</c> 中定义品牌地址映射规则，实现零代码新增品牌。
    /// </summary>
    /// <remarks>
    /// <para>
    /// 通过 <see cref="ModbusDeviceProfiles.LoadJsonProfiles"/> 加载 JSON 配置文件后，
    /// 可通过 <see cref="ModbusTcpClientOptions.DeviceProfile"/> 交给 Modbus TCP 客户端加载。
    /// </para>
    /// <para>
    /// 地址解析流程：
    /// <list type="number">
    ///   <item>使用 <see cref="ModbusProfileDefinition.AddressPattern"/> 正则提取 prefix 和 index。</item>
    ///   <item>在 mappings 表中查找 prefix 对应的解析规则。</item>
    ///   <item>根据 radix（decimal/hex/octal）解析索引号。</item>
    ///   <item>校验索引不超过 max 范围。</item>
    ///   <item>返回 <c>base + index</c> 的 Modbus 绝对地址。</item>
    /// </list>
    /// </para>
    /// </remarks>
    public sealed class JsonModbusProfile : IModbusDeviceProfile
    {
        private readonly ModbusProfileDefinition _definition;
        private readonly Regex _pattern;
        private readonly Dictionary<string, MappingRule> _mappings;

        private struct MappingRule
        {
            public ModbusArea Area;
            public ushort Base;
            public int Max;
            public bool IsHex;
            public bool IsOctal;
        }

        /// <summary>
        /// 从 <see cref="ModbusProfileDefinition"/> 创建 JsonModbusProfile 实例。
        /// </summary>
        /// <param name="definition">JSON 配置中定义的设备配置文件数据。</param>
        /// <exception cref="ArgumentNullException"><paramref name="definition"/> 为 null。</exception>
        public JsonModbusProfile(ModbusProfileDefinition definition)
        {
            _definition = definition ?? throw new ArgumentNullException(nameof(definition));

            // 编译地址正则
            if (string.IsNullOrWhiteSpace(definition.AddressPattern))
                throw new ArgumentException("addressPattern is required.");
            _pattern = new Regex(definition.AddressPattern, RegexOptions.Compiled | RegexOptions.IgnoreCase);

            // 构建映射表
            _mappings = new Dictionary<string, MappingRule>(StringComparer.OrdinalIgnoreCase);
            if (definition.Mappings != null)
            {
                foreach (var mapping in definition.Mappings)
                {
                    if (string.IsNullOrWhiteSpace(mapping.Prefix))
                        continue;

                    var area = ParseArea(mapping.Area);
                    bool isHex = string.Equals(mapping.Radix, "hex", StringComparison.OrdinalIgnoreCase);
                    bool isOctal = string.Equals(mapping.Radix, "octal", StringComparison.OrdinalIgnoreCase);

                    _mappings[mapping.Prefix] = new MappingRule
                    {
                        Area = area,
                        Base = mapping.Base,
                        Max = mapping.Max,
                        IsHex = isHex,
                        IsOctal = isOctal,
                    };
                }
            }
        }

        /// <summary>获取设备配置文件的唯一标识键。</summary>
        public string Key => _definition.Key ?? string.Empty;

        /// <summary>获取设备配置文件的显示名称。</summary>
        public string DisplayName => _definition.DisplayName ?? string.Empty;

        /// <summary>获取设备默认的示例地址。</summary>
        public string DefaultAddress => _definition.DefaultAddress ?? string.Empty;

        /// <summary>获取设备支持的示例地址列表（逗号分隔）。</summary>
        public string ExampleAddresses => _definition.ExampleAddresses ?? string.Empty;

        /// <summary>
        /// 将地址字符串解析为 <see cref="ModbusAddress"/> 对象。
        /// </summary>
        /// <param name="address">设备特定的地址字符串，例如 "D100"、"SM50"。</param>
        /// <returns>解析后的 <see cref="ModbusAddress"/> 对象。</returns>
        /// <exception cref="IndustrialAddressParseException">地址格式无效或前缀不受支持时抛出。</exception>
        public ModbusAddress ParseAddress(string address)
        {
            if (string.IsNullOrWhiteSpace(address))
                throw new IndustrialAddressParseException("Address is required.");

            var match = _pattern.Match(address.Trim().ToUpperInvariant());
            if (!match.Success)
                throw new IndustrialAddressParseException(string.Format(
                    "Unsupported {0} variable: {1}", DisplayName, address));

            var prefix = match.Groups[1].Value;
            MappingRule rule;
            if (!_mappings.TryGetValue(prefix, out rule))
                throw new IndustrialAddressParseException(string.Format(
                    "Unsupported {0} variable type: {1}", DisplayName, prefix));

            int index;
            try
            {
                if (rule.IsOctal)
                    index = Convert.ToInt32(match.Groups[2].Value, 8);
                else if (rule.IsHex)
                    index = Convert.ToInt32(match.Groups[2].Value, 16);
                else
                    index = int.Parse(match.Groups[2].Value);
            }
            catch (Exception ex)
            {
                throw new IndustrialAddressParseException(
                    string.Format("Invalid {0} variable index.", DisplayName), ex);
            }

            if (index < 0 || index >= rule.Max)
                throw new IndustrialAddressParseException(
                    string.Format("{0} variable index out of range.", DisplayName));

            return new ModbusAddress(rule.Area, (ushort)(rule.Base + index));
        }

        /// <summary>
        /// 将从设备读取的寄存器数据规范化为当前平台所需的字节序。
        /// </summary>
        public ushort[] NormalizeRegistersForRead(DataType dataType, ushort[] registers)
        {
            return _definition.LowWordFirst && RequiresSwap(dataType)
                ? ReverseRegisters(registers)
                : registers;
        }

        /// <summary>
        /// 将写入设备的寄存器数据从当前平台字节序规范化为设备所需的字节序。
        /// </summary>
        public ushort[] NormalizeRegistersForWrite(DataType dataType, ushort[] registers)
        {
            return _definition.LowWordFirst && RequiresSwap(dataType)
                ? ReverseRegisters(registers)
                : registers;
        }

        private static ModbusArea ParseArea(string area)
        {
            if (string.IsNullOrWhiteSpace(area))
                return ModbusArea.HoldingRegister;

            switch (area.Trim().ToLowerInvariant())
            {
                case "coil":            return ModbusArea.Coil;
                case "discreteinput":   return ModbusArea.DiscreteInput;
                case "inputregister":   return ModbusArea.InputRegister;
                case "holdingregister": return ModbusArea.HoldingRegister;
                default:                return ModbusArea.HoldingRegister;
            }
        }

        private static bool RequiresSwap(DataType dataType)
        {
            switch (dataType)
            {
                case DataType.Int32:
                case DataType.UInt32:
                case DataType.Float:
                case DataType.Double:
                    return true;
                default:
                    return false;
            }
        }

        private static ushort[] ReverseRegisters(ushort[] registers)
        {
            if (registers == null || registers.Length <= 1)
                return registers;

            var buffer = new ushort[registers.Length];
            Buffer.BlockCopy(registers, 0, buffer, 0, registers.Length * sizeof(ushort));
            Array.Reverse(buffer);
            return buffer;
        }

        /// <summary>
        /// 返回当前配置文件的摘要字符串，便于调试和日志。
        /// </summary>
        public override string ToString()
        {
            return string.Format("{0} ({1})", DisplayName, Key);
        }
    }
}
