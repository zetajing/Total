using IndustrialCommSdk.Protocols.Mc;
using IndustrialCommSdk.Protocols.Modbus;
using IndustrialCommSdk.Protocols.S7;
using NUnit.Framework;

namespace IndustrialCommSdk.Tests
{
    [TestFixture]
    public class ProtocolParserTests
    {
        [Test]
        public void ModbusParser_Should_Reject_Standard_Address()
        {
            var parser = new ModbusAddressParser();
            Assert.Throws<IndustrialCommSdk.Exceptions.IndustrialAddressParseException>(() => parser.Parse("40001"));
        }

        [Test]
        public void ModbusParser_Should_Parse_Inovance_Address()
        {
            var parser = new ModbusAddressParser();
            var result = (ModbusAddress)parser.Parse("X17");

            Assert.That(result.Area, Is.EqualTo(ModbusArea.DiscreteInput));
            Assert.That(result.ZeroBasedAddress, Is.EqualTo(0xF80F));
        }

        [Test]
        public void ModbusParser_Should_Use_Custom_Profile()
        {
            var parser = new ModbusAddressParser(new TestModbusProfile());
            var result = (ModbusAddress)parser.Parse("Z15");

            Assert.That(result.Area, Is.EqualTo(ModbusArea.HoldingRegister));
            Assert.That(result.ZeroBasedAddress, Is.EqualTo(115));
        }

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
        public void McParser_Should_Parse_Hex_Bit_Address()
        {
            var parser = new McAddressParser();
            var result = (McAddress)parser.Parse("X1F");

            Assert.That(result.DeviceType, Is.EqualTo(McDeviceType.X));
            Assert.That(result.Index, Is.EqualTo(0x1F));
            Assert.That(result.IsBitDevice, Is.True);
        }

        private sealed class TestModbusProfile : IModbusDeviceProfile
        {
            public string Key { get { return "test"; } }
            public string DisplayName { get { return "Test"; } }
            public string DefaultAddress { get { return "Z1"; } }
            public string ExampleAddresses { get { return "Z1"; } }

            public ModbusAddress ParseAddress(string address)
            {
                return new ModbusAddress(ModbusArea.HoldingRegister, (ushort)(100 + int.Parse(address.Substring(1))));
            }

            public ushort[] NormalizeRegistersForRead(IndustrialCommSdk.Abstractions.DataType dataType, ushort[] registers)
            {
                return registers;
            }

            public ushort[] NormalizeRegistersForWrite(IndustrialCommSdk.Abstractions.DataType dataType, ushort[] registers)
            {
                return registers;
            }
        }
    }
}
