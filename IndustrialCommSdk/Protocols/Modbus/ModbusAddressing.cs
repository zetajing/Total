using System;
using IndustrialCommSdk.Abstractions;
using IndustrialCommSdk.Exceptions;

namespace IndustrialCommSdk.Protocols.Modbus
{
    public enum ModbusArea
    {
        Coil = 1,
        DiscreteInput = 2,
        InputRegister = 3,
        HoldingRegister = 4,
    }

    public sealed class ModbusAddress
    {
        public ModbusAddress(ModbusArea area, ushort zeroBasedAddress)
        {
            Area = area;
            ZeroBasedAddress = zeroBasedAddress;
        }

        public ModbusArea Area { get; private set; }
        public ushort ZeroBasedAddress { get; private set; }
        public bool IsBitArea { get { return Area == ModbusArea.Coil || Area == ModbusArea.DiscreteInput; } }
        public bool IsRegisterArea { get { return Area == ModbusArea.HoldingRegister || Area == ModbusArea.InputRegister; } }
    }

    public sealed class ModbusAddressParser : IAddressParser
    {
        private readonly IModbusDeviceProfile _deviceProfile;

        public ModbusAddressParser(IModbusDeviceProfile deviceProfile = null)
        {
            _deviceProfile = deviceProfile ?? ModbusDeviceProfiles.InovanceEasyPlc;
        }

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
