using IndustrialCommSdk.Abstractions;
using IndustrialCommSdk.Protocols.Common;
using IndustrialCommSdk.Protocols.Modbus;
using NUnit.Framework;

namespace IndustrialCommSdk.Tests
{
    [TestFixture]
    public class ModbusDeviceProfileTests
    {
        [Test]
        public void InovanceProfile_Should_Reorder_Int32_LowWordFirst_Registers()
        {
            var rawRegisters = new ushort[] { 0x5678, 0x1234 };
            var normalized = ModbusDeviceProfiles.InovanceEasyPlc.NormalizeRegistersForRead(DataType.Int32, rawRegisters);
            var request = new ReadRequest("modbus-1", "D100", DataType.Int32, 2);

            var value = RegisterValueCodec.ToDataValue(request, normalized);

            Assert.That((int)value.Value, Is.EqualTo(0x12345678));
        }

        [Test]
        public void InovanceProfile_Should_Output_Int32_As_LowWordFirst_Registers()
        {
            var request = new WriteRequest("modbus-1", "D100", DataType.Int32, 0x12345678, 2);

            var registers = ModbusDeviceProfiles.InovanceEasyPlc.NormalizeRegistersForWrite(DataType.Int32, RegisterValueCodec.EncodeRegisters(request));

            Assert.That(registers, Is.EqualTo(new ushort[] { 0x5678, 0x1234 }));
        }

        [Test]
        public void InovanceProfile_Should_Reorder_Float_LowWordFirst_Registers()
        {
            var rawRegisters = new ushort[] { 0x0000, 0x3F80 };
            var normalized = ModbusDeviceProfiles.InovanceEasyPlc.NormalizeRegistersForRead(DataType.Float, rawRegisters);
            var request = new ReadRequest("modbus-1", "D100", DataType.Float, 2);

            var value = RegisterValueCodec.ToDataValue(request, normalized);

            Assert.That((float)value.Value, Is.EqualTo(1.0f).Within(0.0001f));
        }
    }
}
