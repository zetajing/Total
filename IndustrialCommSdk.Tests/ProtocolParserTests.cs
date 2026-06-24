using IndustrialCommSdk.Protocols.Mc;
using IndustrialCommSdk.Protocols.Modbus;
using IndustrialCommSdk.Protocols.S7;
using NUnit.Framework;

namespace IndustrialCommSdk.Tests
{
    /// <summary>
    ///     对 Modbus、S7 和 MC（三菱）协议的地址解析器（<see cref="ModbusAddressParser" />、
    ///     <see cref="S7AddressParser" />、<see cref="McAddressParser" />）的单元测试。
    ///     验证各协议地址解析器能否正确解析标准地址格式，以及在非法输入时能否抛出预期的异常。
    ///     同时测试 Modbus 地址解析器在使用自定义设备配置（<see cref="IModbusDeviceProfile" />）时的行为。
    /// </summary>
    [TestFixture]
    public class ProtocolParserTests
    {
        /// <summary>
        ///     验证 <see cref="ModbusAddressParser" /> 在收到传统 Modbus 标准地址格式 "40001" 时
        ///     能正确识别为不支持的类型并抛出 <see cref="IndustrialCommSdk.Exceptions.IndustrialAddressParseException" />。
        ///     该测试确保解析器对 PLC 开发者常用的非标准地址格式予以拒绝。
        /// </summary>
        [Test]
        public void ModbusParser_Should_Reject_Standard_Address()
        {
            var parser = new ModbusAddressParser();
            Assert.Throws<IndustrialCommSdk.Exceptions.IndustrialAddressParseException>(() => parser.Parse("40001"));
        }

        /// <summary>
        ///     验证 <see cref="ModbusAddressParser" /> 能正确解析汇川/Inovance 风格的地址 "X17"。
        ///     预期：区域为 <see cref="ModbusArea.DiscreteInput" />，零基地址为 0xF80F（即 63487）。
        /// </summary>
        [Test]
        public void ModbusParser_Should_Parse_Inovance_Address()
        {
            var parser = new ModbusAddressParser();
            var result = (ModbusAddress)parser.Parse("X17");

            Assert.That(result.Area, Is.EqualTo(ModbusArea.DiscreteInput));
            Assert.That(result.ZeroBasedAddress, Is.EqualTo(0xF80F));
        }

        /// <summary>
        ///     验证 <see cref="ModbusAddressParser" /> 在注入自定义 <see cref="IModbusDeviceProfile" /> 后，
        ///     能够按照自定义配置解析非标准地址 "Z15"。
        ///     预期：区域为 <see cref="ModbusArea.HoldingRegister" />，零基地址为 115。
        /// </summary>
        [Test]
        public void ModbusParser_Should_Use_Custom_Profile()
        {
            var parser = new ModbusAddressParser(new TestModbusProfile());
            var result = (ModbusAddress)parser.Parse("Z15");

            Assert.That(result.Area, Is.EqualTo(ModbusArea.HoldingRegister));
            Assert.That(result.ZeroBasedAddress, Is.EqualTo(115));
        }

        /// <summary>
        ///     验证 <see cref="S7AddressParser" /> 能正确解析西门子 S7 协议的 DB 位地址格式 "DB1.DBX0.1"。
        ///     预期：区域为 <see cref="S7Area.Db" />，DB 编号为 1，字节偏移为 0，位偏移为 1。
        /// </summary>
        [Test]
        public void S7Parser_Should_Parse_Db_Bit_Address()
        {
            var parser = new S7AddressParser();
            var result = (S7Address)parser.Parse("DB1.DBX0.1");

            Assert.That(result.Area, Is.EqualTo(S7Area.Db));
            Assert.That(result.DbNumber, Is.EqualTo(1));
            Assert.That(result.ByteOffset, Is.EqualTo(0));
            Assert.That(result.BitOffset, Is.EqualTo(1));
        }

        [Test]
        public void S7Parser_Should_Accept_Bit_Zero_Address()
        {
            var parser = new S7AddressParser();
            var result = (S7Address)parser.Parse("DB1.DBX0.0");

            Assert.That(result.DbNumber, Is.EqualTo(1));
            Assert.That(result.ByteOffset, Is.EqualTo(0));
            Assert.That(result.BitOffset, Is.EqualTo(0));
        }

        [Test]
        public void S7Parser_Should_Reject_Bit_Address_Outside_Byte_Range()
        {
            var parser = new S7AddressParser();

            Assert.Throws<IndustrialCommSdk.Exceptions.IndustrialAddressParseException>(
                () => parser.Parse("DB1.DBX0.8"));
        }

        /// <summary>
        ///     验证 <see cref="S7AddressParser" /> 能兼容 TIA 常见的地址前缀格式，
        ///     例如 "%DB200.DBD6" 或 "P#DB200.DBX20.0"。
        /// </summary>
        [Test]
        public void S7Parser_Should_Normalize_Tia_Style_Prefixes()
        {
            var parser = new S7AddressParser();

            var dwordAddress = (S7Address)parser.Parse("%DB200.DBD6");
            var pointerAddress = (S7Address)parser.Parse("P#DB200.DBX20.0");

            Assert.That(dwordAddress.Normalized, Is.EqualTo("DB200.DBD6"));
            Assert.That(dwordAddress.DbNumber, Is.EqualTo(200));
            Assert.That(dwordAddress.ByteOffset, Is.EqualTo(6));

            Assert.That(pointerAddress.Normalized, Is.EqualTo("DB200.DBX20.0"));
            Assert.That(pointerAddress.DbNumber, Is.EqualTo(200));
            Assert.That(pointerAddress.ByteOffset, Is.EqualTo(20));
            Assert.That(pointerAddress.BitOffset, Is.EqualTo(0));
        }

        /// <summary>
        ///     验证 <see cref="McAddressParser" /> 能正确解析三菱 MC 协议的十六进制位地址 "X1F"。
        ///     预期：设备类型为 <see cref="McDeviceType.X" />，索引值为 0x1F，且被识别为位设备。
        /// </summary>
        [Test]
        public void McParser_Should_Parse_Hex_Bit_Address()
        {
            var parser = new McAddressParser();
            var result = (McAddress)parser.Parse("X1F");

            Assert.That(result.DeviceType, Is.EqualTo(McDeviceType.X));
            Assert.That(result.Index, Is.EqualTo(0x1F));
            Assert.That(result.IsBitDevice, Is.True);
        }

        /// <summary>
        ///     用于自定义设备配置测试的模拟 Modbus 设备配置文件。
        ///     实现对 "Z{number}" 格式地址的解析，将数字部分加上 100 作为 <see cref="ModbusArea.HoldingRegister" /> 区域的零基地址。
        /// </summary>
        private sealed class TestModbusProfile : IModbusDeviceProfile
        {
            /// <summary>配置唯一标识，返回 "test"。</summary>
            public string Key { get { return "test"; } }

            /// <summary>配置显示名称，返回 "Test"。</summary>
            public string DisplayName { get { return "Test"; } }

            /// <summary>默认地址，返回 "Z1"。</summary>
            public string DefaultAddress { get { return "Z1"; } }

            /// <summary>示例地址，返回 "Z1"。</summary>
            public string ExampleAddresses { get { return "Z1"; } }

            /// <summary>
            ///     解析 "Z{number}" 格式的自定义地址。
            ///     将数字部分转换为整数后加 100，作为 <see cref="ModbusArea.HoldingRegister" /> 的零基地址。
            /// </summary>
            /// <param name="address">待解析的地址字符串（例如 "Z15"）。</param>
            /// <returns>解析后的 <see cref="ModbusAddress" /> 对象。</returns>
            public ModbusAddress ParseAddress(string address)
            {
                return new ModbusAddress(ModbusArea.HoldingRegister, (ushort)(100 + int.Parse(address.Substring(1))));
            }

            /// <summary>读方向寄存器归一化（透传，不执行任何转换）。</summary>
            public ushort[] NormalizeRegistersForRead(IndustrialCommSdk.Abstractions.DataType dataType, ushort[] registers)
            {
                return registers;
            }

            /// <summary>写方向寄存器归一化（透传，不执行任何转换）。</summary>
            public ushort[] NormalizeRegistersForWrite(IndustrialCommSdk.Abstractions.DataType dataType, ushort[] registers)
            {
                return registers;
            }
        }
    }
}
