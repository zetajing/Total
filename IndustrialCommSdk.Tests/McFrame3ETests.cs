using IndustrialCommSdk.Protocols.Mc;
using IndustrialCommSdk.Exceptions;
using NUnit.Framework;

namespace IndustrialCommSdk.Tests
{
    [TestFixture]
    public sealed class McFrame3ETests
    {
        [Test]
        public void BuildReadWordsRequest_MatchesQna3EBinaryLayout()
        {
            var frame = McFrame3E.BuildReadWordsRequest(new McAddress(McDeviceType.D, 100, false), 2);

            Assert.That(frame, Is.EqualTo(new byte[]
            {
                0x50, 0x00, 0x00, 0xFF, 0xFF, 0x03, 0x00, 0x0C, 0x00,
                0x0A, 0x00,
                0x01, 0x04, 0x00, 0x00,
                0x64, 0x00, 0x00, 0xA8,
                0x02, 0x00,
            }));
        }

        [Test]
        public void BuildWriteBitsRequest_UsesMcNibbleEncoding()
        {
            var frame = McFrame3E.BuildWriteBitsRequest(
                new McAddress(McDeviceType.M, 10, true),
                new[] { true, false, true, true, false });

            Assert.That(frame, Is.EqualTo(new byte[]
            {
                0x50, 0x00, 0x00, 0xFF, 0xFF, 0x03, 0x00, 0x0F, 0x00,
                0x0A, 0x00,
                0x01, 0x14, 0x01, 0x00,
                0x0A, 0x00, 0x00, 0x90,
                0x05, 0x00,
                0x10, 0x11, 0x00,
            }));
        }

        [Test]
        public void UnpackBits_DecodesMcNibbleEncoding()
        {
            var values = McFrame3E.UnpackBits(new byte[] { 0x10, 0x11, 0x00 }, 5);

            Assert.That(values, Is.EqualTo(new[] { true, false, true, true, false }));
        }

        [Test]
        public void ResponseParsing_RejectsInvalidSubheaderAndOddWordPayload()
        {
            Assert.Throws<IndustrialProtocolException>(() =>
                McFrame3E.GetRemainingResponseLength(new byte[]
                {
                    0x50, 0x00, 0x00, 0xFF, 0xFF, 0x03, 0x00, 0x02, 0x00, 0x00, 0x00,
                }));
            Assert.Throws<IndustrialProtocolException>(() => McFrame3E.ToRegisters(new byte[] { 0x01 }));
        }

        [TestCase("ZR1A", McDeviceType.ZR, 0x1A, false)]
        [TestCase("SS10", McDeviceType.SS, 10, true)]
        [TestCase("SN10", McDeviceType.SN, 10, false)]
        public void AddressParser_UsesCorrectRadixAndDeviceKind(string text, McDeviceType type, int index, bool isBit)
        {
            var address = (McAddress)new McAddressParser().Parse(text);

            Assert.That(address.DeviceType, Is.EqualTo(type));
            Assert.That(address.Index, Is.EqualTo(index));
            Assert.That(address.IsBitDevice, Is.EqualTo(isBit));
        }
    }
}
