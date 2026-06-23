using System.Text;
using IndustrialCommSdk.Abstractions;
using IndustrialCommSdk.Exceptions;
using IndustrialCommSdk.Protocols.Common;
using NUnit.Framework;

namespace IndustrialCommSdk.Tests
{
    [TestFixture]
    public class RegisterValueCodecTests
    {
        [Test]
        public void GetRequiredRegisterLength_Should_Calculate_String_Register_Count()
        {
            var length = RegisterValueCodec.GetRequiredRegisterLength(DataType.String, "ABC");

            Assert.That(length, Is.EqualTo(2));
        }

        [Test]
        public void EncodeBytes_Should_Pad_String_To_Register_Length()
        {
            var request = new WriteRequest("modbus-1", "D100", DataType.String, "ABC", 2);

            var bytes = RegisterValueCodec.EncodeBytes(request);

            Assert.That(bytes, Is.EqualTo(new byte[] { 65, 66, 67, 0 }));
        }

        [Test]
        public void EncodeBytes_Should_Reject_String_That_Exceeds_Register_Length()
        {
            var request = new WriteRequest("modbus-1", "D100", DataType.String, "ABCDE", 2);

            Assert.Throws<IndustrialDataConversionException>(() => RegisterValueCodec.EncodeBytes(request));
        }

        [Test]
        public void ToDataValue_Should_Decode_Padded_String()
        {
            var request = new ReadRequest("modbus-1", "D100", DataType.String, 2);
            var registers = RegisterValueCodec.GetRegistersFromBytes(Encoding.ASCII.GetBytes("ABC\0"));

            var value = RegisterValueCodec.ToDataValue(request, registers);

            Assert.That(value.Value, Is.EqualTo("ABC"));
        }
    }
}
