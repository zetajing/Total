using System.Collections.Generic;
using IndustrialCommSdk.Abstractions;

namespace IndustrialCommSdk.Protocols.Modbus
{
    public interface IModbusDeviceProfile
    {
        string Key { get; }
        string DisplayName { get; }
        string DefaultAddress { get; }
        string ExampleAddresses { get; }

        ModbusAddress ParseAddress(string address);
        ushort[] NormalizeRegistersForRead(DataType dataType, ushort[] registers);
        ushort[] NormalizeRegistersForWrite(DataType dataType, ushort[] registers);
    }

    public static class ModbusDeviceProfiles
    {
        public static InovanceEasyPlcModbusProfile InovanceEasyPlc { get; } = new InovanceEasyPlcModbusProfile();

        public static IReadOnlyList<IModbusDeviceProfile> All { get; } = new IModbusDeviceProfile[]
        {
            InovanceEasyPlc,
        };
    }
}
