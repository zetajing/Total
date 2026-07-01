using IndustrialCommSdk.Protocols.Modbus;
using NUnit.Framework;

namespace IndustrialCommSdk.Tests
{
    [TestFixture]
    public class ModbusRtuFrameCodecTests
    {
        [Test]
        public void StandardReadRequest_Should_HaveValidCrc()
        {
            var frame = new byte[] { 0x01, 0x03, 0x00, 0x00, 0x00, 0x01, 0x84, 0x0A };
            Assert.That(ModbusRtuFrameCodec.HasValidCrc(frame), Is.True);
            Assert.That(ModbusRtuFrameCodec.ComputeCrc(frame, 0, 6), Is.EqualTo(0x0A84));
        }

        [Test]
        public void CorruptedResponse_Should_FailCrcValidation()
        {
            var frame = new byte[] { 0x01, 0x03, 0x02, 0x00, 0x7B, 0xF8, 0x66 };
            Assert.That(ModbusRtuFrameCodec.HasValidCrc(frame), Is.False);
        }

        [Test]
        public void ParseAndAppendCrc_Should_CreateStandardRequest()
        {
            var payload = ModbusRtuFrameCodec.ParseHex("01 03 00 00 00 01");
            var frame = ModbusRtuFrameCodec.AppendCrc(payload);
            Assert.That(frame, Is.EqualTo(new byte[] { 0x01, 0x03, 0x00, 0x00, 0x00, 0x01, 0x84, 0x0A }));
        }
    }
}
