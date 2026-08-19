using IndustrialCommSdk.Exceptions;
using IndustrialCommSdk.Protocols.S7;
using NUnit.Framework;

namespace IndustrialCommSdk.Tests
{
    [TestFixture]
    public sealed class S7WriteValueEncoderTests
    {
        [Test]
        public void Options_DefaultToNoAutomaticWriteReplay()
        {
            Assert.IsFalse(new SiemensS7ClientOptions().AutoReconnectWrites);
        }

        [Test]
        public void EncodeString_PadsToConfiguredLength()
        {
            var encoded = S7WriteValueEncoder.EncodeString("AB", 4);

            CollectionAssert.AreEqual(new byte[] { 65, 66, 0, 0 }, encoded);
        }

        [Test]
        public void EncodeString_RejectsPayloadLongerThanConfiguredLength()
        {
            Assert.Throws<IndustrialDataConversionException>(
                () => S7WriteValueEncoder.EncodeString("ABC", 2));
        }

        [Test]
        public void EncodeByteArray_PadsToConfiguredLength()
        {
            var encoded = S7WriteValueEncoder.EncodeByteArray(new byte[] { 1, 2 }, 4);

            CollectionAssert.AreEqual(new byte[] { 1, 2, 0, 0 }, encoded);
        }

        [Test]
        public void EncodeByteArray_RejectsZeroLength()
        {
            Assert.Throws<IndustrialDataConversionException>(
                () => S7WriteValueEncoder.EncodeByteArray(new byte[0], 0));
        }
    }
}
