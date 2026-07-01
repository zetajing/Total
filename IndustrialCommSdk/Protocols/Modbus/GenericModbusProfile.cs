using System;
using System.Text.RegularExpressions;
using IndustrialCommSdk.Abstractions;
using IndustrialCommSdk.Exceptions;

namespace IndustrialCommSdk.Protocols.Modbus
{
    /// <summary>
    /// 通用 Modbus 地址配置，不包含任何 PLC 品牌地址映射和多寄存器字序交换。
    /// </summary>
    public sealed class GenericModbusProfile : IModbusDeviceProfile
    {
        private static readonly Regex ExplicitAddressPattern = new Regex(
            @"^(C|COIL|DI|IR|HR)(\d+)$", RegexOptions.Compiled | RegexOptions.IgnoreCase);
        private static readonly Regex ReferenceAddressPattern = new Regex(
            @"^([0134])(\d{4,5})$", RegexOptions.Compiled);

        public string Key => "generic-modbus";
        public string DisplayName => "Generic Modbus";
        public string DefaultAddress => "HR0";
        public string ExampleAddresses => "HR0, IR0, C0, DI0, 40001, 30001, 00001, 10001";

        /// <summary>
        /// 支持零基地址 HR0/IR0/C0/DI0，以及一基引用地址 4xxxx/3xxxx/0xxxx/1xxxx。
        /// </summary>
        public ModbusAddress ParseAddress(string address)
        {
            if (string.IsNullOrWhiteSpace(address))
                throw new IndustrialAddressParseException("Address is required.");

            var normalized = address.Trim().ToUpperInvariant();
            var explicitMatch = ExplicitAddressPattern.Match(normalized);
            if (explicitMatch.Success)
            {
                var area = ParseExplicitArea(explicitMatch.Groups[1].Value);
                return new ModbusAddress(area, ParseZeroBasedOffset(explicitMatch.Groups[2].Value, address));
            }

            var referenceMatch = ReferenceAddressPattern.Match(normalized);
            if (referenceMatch.Success)
            {
                var area = ParseReferenceArea(referenceMatch.Groups[1].Value[0]);
                int reference;
                if (!int.TryParse(referenceMatch.Groups[2].Value, out reference) || reference < 1 || reference > 65536)
                    throw new IndustrialAddressParseException("Modbus reference address must be between 1 and 65536.");
                return new ModbusAddress(area, (ushort)(reference - 1));
            }

            throw new IndustrialAddressParseException(string.Format(
                "Unsupported generic Modbus address: {0}. Use HR0/IR0/C0/DI0 or 40001/30001/00001/10001 format.", address));
        }

        public ushort[] NormalizeRegistersForRead(DataType dataType, ushort[] registers) => registers;
        public ushort[] NormalizeRegistersForWrite(DataType dataType, ushort[] registers) => registers;

        private static ushort ParseZeroBasedOffset(string value, string originalAddress)
        {
            ushort result;
            if (!ushort.TryParse(value, out result))
                throw new IndustrialAddressParseException("Generic Modbus address is outside the 0-65535 range: " + originalAddress);
            return result;
        }

        private static ModbusArea ParseExplicitArea(string prefix)
        {
            switch (prefix)
            {
                case "C":
                case "COIL": return ModbusArea.Coil;
                case "DI": return ModbusArea.DiscreteInput;
                case "IR": return ModbusArea.InputRegister;
                default: return ModbusArea.HoldingRegister;
            }
        }

        private static ModbusArea ParseReferenceArea(char prefix)
        {
            switch (prefix)
            {
                case '0': return ModbusArea.Coil;
                case '1': return ModbusArea.DiscreteInput;
                case '3': return ModbusArea.InputRegister;
                default: return ModbusArea.HoldingRegister;
            }
        }
    }
}
