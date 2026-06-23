using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;
using IndustrialCommSdk.Abstractions;
using IndustrialCommSdk.Exceptions;

namespace IndustrialCommSdk.Protocols.Modbus
{
    public sealed class InovanceEasyPlcModbusProfile : IModbusDeviceProfile
    {
        private static readonly Dictionary<char, Tuple<ushort, ModbusArea, int, bool>> AddressMap = new Dictionary<char, Tuple<ushort, ModbusArea, int, bool>>
        {
            { 'D', Tuple.Create((ushort)0x0000, ModbusArea.HoldingRegister, 8000, false) },
            { 'R', Tuple.Create((ushort)0x3000, ModbusArea.HoldingRegister, 32768, false) },
            { 'M', Tuple.Create((ushort)0x0000, ModbusArea.Coil, 8000, false) },
            { 'B', Tuple.Create((ushort)0x3000, ModbusArea.Coil, 32768, false) },
            { 'S', Tuple.Create((ushort)0xE000, ModbusArea.Coil, 4096, false) },
            { 'X', Tuple.Create((ushort)0xF800, ModbusArea.DiscreteInput, 1024, true) },
            { 'Y', Tuple.Create((ushort)0xFC00, ModbusArea.Coil, 1024, true) },
        };

        public string Key
        {
            get { return "inovance-easyplc"; }
        }

        public string DisplayName
        {
            get { return "Inovance EasyPLC"; }
        }

        public string DefaultAddress
        {
            get { return "D100"; }
        }

        public string ExampleAddresses
        {
            get { return "D100, R200, M0, S10, B5, X17, Y20"; }
        }

        public ModbusAddress ParseAddress(string address)
        {
            if (string.IsNullOrWhiteSpace(address))
            {
                throw new IndustrialAddressParseException("Address is required.");
            }

            var match = Regex.Match(address.Trim().ToUpperInvariant(), @"^([A-Z])(\d+)$");
            if (!match.Success)
            {
                throw new IndustrialAddressParseException(string.Format("Unsupported Inovance PLC variable: {0}", address));
            }

            var type = match.Groups[1].Value[0];
            Tuple<ushort, ModbusArea, int, bool> rule;
            if (!AddressMap.TryGetValue(type, out rule))
            {
                throw new IndustrialAddressParseException(string.Format("Unsupported Inovance PLC variable type: {0}", type));
            }

            int index;
            try
            {
                index = rule.Item4 ? Convert.ToInt32(match.Groups[2].Value, 8) : int.Parse(match.Groups[2].Value);
            }
            catch (Exception ex)
            {
                throw new IndustrialAddressParseException("Invalid Inovance PLC variable index.", ex);
            }

            if (index < 0 || index >= rule.Item3)
            {
                throw new IndustrialAddressParseException("Inovance PLC variable index out of range.");
            }

            return new ModbusAddress(rule.Item2, (ushort)(rule.Item1 + index));
        }

        public ushort[] NormalizeRegistersForRead(DataType dataType, ushort[] registers)
        {
            return RequiresLowWordFirst(dataType) ? ReverseRegisters(registers) : registers;
        }

        public ushort[] NormalizeRegistersForWrite(DataType dataType, ushort[] registers)
        {
            return RequiresLowWordFirst(dataType) ? ReverseRegisters(registers) : registers;
        }

        private static bool RequiresLowWordFirst(DataType dataType)
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
            {
                return registers;
            }

            var buffer = new ushort[registers.Length];
            Buffer.BlockCopy(registers, 0, buffer, 0, registers.Length * sizeof(ushort));
            Array.Reverse(buffer);
            return buffer;
        }
    }
}
