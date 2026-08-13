using System;
using System.Text;
using IndustrialCommSdk.Exceptions;
using IndustrialCommSdk.Protocols.S7;
using NUnit.Framework;

namespace IndustrialCommSdk.Tests
{
    [TestFixture]
    public sealed class SiemensS7StringTests
    {
        [Test]
        public void Decode_UsesCurrentLengthInsteadOfTrailingBuffer()
        {
            var bytes = new byte[62];
            bytes[0] = 60;
            bytes[1] = 3;
            Buffer.BlockCopy(Encoding.ASCII.GetBytes("ABC"), 0, bytes, 2, 3);
            bytes[5] = 0xFF;

            Assert.AreEqual("ABC", S7StringCodec.Decode(bytes, 60));
        }

        [Test]
        public void Decode_RejectsInvalidCurrentLength()
        {
            var bytes = new byte[62];
            bytes[0] = 60;
            bytes[1] = 61;

            Assert.Throws<IndustrialDataConversionException>(() => S7StringCodec.Decode(bytes, 60));
        }

        [TestCase(0)]
        [TestCase(255)]
        public void ValidateReservedLength_RejectsValuesOutsideS7Range(int reservedLength)
        {
            Assert.Throws<ArgumentOutOfRangeException>(
                () => S7StringCodec.ValidateReservedLength(reservedLength));
        }
    }
}
