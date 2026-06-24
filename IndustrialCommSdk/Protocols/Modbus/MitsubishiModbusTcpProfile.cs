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
    /// 为了与现场常见的三菱地址书写习惯保持一致，本配置文件优先支持
    /// D/R/W（字寄存器）以及 M/X/Y/L（位设备）这组最常用前缀。
    /// 其中 X、Y、W 的编号按三菱常见约定使用十六进制解析，其余前缀使用十进制解析。
    /// </remarks>
    public sealed class MitsubishiModbusTcpProfile : IModbusDeviceProfile
    {
        private static readonly Dictionary<string, Tuple<ModbusArea, bool>> AddressRules = new Dictionary<string, Tuple<ModbusArea, bool>>(StringComparer.OrdinalIgnoreCase)
        {
            { "D", Tuple.Create(ModbusArea.HoldingRegister, false) },
            { "R", Tuple.Create(ModbusArea.HoldingRegister, false) },
            { "W", Tuple.Create(ModbusArea.HoldingRegister, true) },
            { "M", Tuple.Create(ModbusArea.Coil, false) },
            { "X", Tuple.Create(ModbusArea.DiscreteInput, true) },
            { "Y", Tuple.Create(ModbusArea.Coil, true) },
            { "L", Tuple.Create(ModbusArea.Coil, false) },
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
            get { return "D100, R200, W10, M0, X10, Y20, L30"; }
        }

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
            Tuple<ModbusArea, bool> rule;
            if (!AddressRules.TryGetValue(prefix, out rule))
            {
                throw new IndustrialAddressParseException(string.Format("Unsupported Mitsubishi Modbus variable type: {0}", prefix));
            }

            int index;
            try
            {
                index = Convert.ToInt32(match.Groups["index"].Value, rule.Item2 ? 16 : 10);
            }
            catch (Exception ex)
            {
                throw new IndustrialAddressParseException("Invalid Mitsubishi Modbus variable index.", ex);
            }

            if (index < 0 || index > ushort.MaxValue)
            {
                throw new IndustrialAddressParseException("Mitsubishi Modbus variable index out of range.");
            }

            return new ModbusAddress(rule.Item1, (ushort)index);
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
