using IndustrialCommSdk.Abstractions;
using IndustrialCommSdk.Protocols.Common;
using IndustrialCommSdk.Protocols.Modbus;
using NUnit.Framework;

namespace IndustrialCommSdk.Tests
{
    /// <summary>
    ///     对 Modbus 设备地址映射配置（<see cref="ModbusDeviceProfiles" />）的单元测试。
    ///     验证汇川（Inovance）等品牌 PLC 的寄存器高低字节序归一化逻辑是否正确，
    ///     包括读方向（低字在前 → 标准序）和写方向（标准序 → 低字在前）的数据转换。
    /// </summary>
    [TestFixture]
    public class ModbusDeviceProfileTests
    {
        /// <summary>
        ///     验证汇川 EasyPLC 配置在读方向上能将低字优先的原始寄存器数据还原为正确的 <see cref="Int32" /> 值。
        ///     原始寄存器为 [0x5678, 0x1234]（低字在前），归一化后应得到 0x12345678。
        /// </summary>
        [Test]
        public void InovanceProfile_Should_Reorder_Int32_LowWordFirst_Registers()
        {
            var rawRegisters = new ushort[] { 0x5678, 0x1234 };
            var normalized = ModbusDeviceProfiles.InovanceEasyPlc.NormalizeRegistersForRead(DataType.Int32, rawRegisters);
            var request = new ReadRequest("modbus-1", "D100", DataType.Int32, 2);

            var value = RegisterValueCodec.ToDataValue(request, normalized);

            Assert.That((int)value.Value, Is.EqualTo(0x12345678));
        }

        /// <summary>
        ///     验证汇川 EasyPLC 配置在写方向上能将标准 <see cref="Int32" /> 值 0x12345678
        ///     转换为低字优先的寄存器数组 [0x5678, 0x1234]。
        /// </summary>
        [Test]
        public void InovanceProfile_Should_Output_Int32_As_LowWordFirst_Registers()
        {
            var request = new WriteRequest("modbus-1", "D100", DataType.Int32, 0x12345678, 2);

            var registers = ModbusDeviceProfiles.InovanceEasyPlc.NormalizeRegistersForWrite(DataType.Int32, RegisterValueCodec.EncodeRegisters(request));

            Assert.That(registers, Is.EqualTo(new ushort[] { 0x5678, 0x1234 }));
        }

        /// <summary>
        ///     验证汇川 EasyPLC 配置在读方向上能将低字优先的 <see cref="float" /> 原始寄存器
        ///     归一化为正确的 IEEE 754 单精度浮点值。
        ///     原始寄存器为 [0x0000, 0x3F80]（低字在前），归一化后应得到 1.0f。
        /// </summary>
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
