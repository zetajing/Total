using System;
using IndustrialCommSdk;
using IndustrialCommSdk.Abstractions;
using IndustrialCommSdk.Protocols.Ads;
using NUnit.Framework;

namespace IndustrialCommSdk.Tests
{
    [TestFixture]
    public sealed class AdsProtocolTests
    {
        [Test]
        public void DefaultSdkRegistersAdsProvider()
        {
            var sdk = IndustrialSdk.CreateDefault();
            var provider = sdk.Protocols.Get("ads");

            Assert.AreEqual("ads", provider.Protocol);
            Assert.AreEqual(typeof(AdsSettings), provider.SettingsType);
        }

        [Test]
        public void AdsAddressParserTrimsSymbolName()
        {
            var address = new AdsAddressParser().ParseTyped("  MAIN.bool1  ");

            Assert.AreEqual("MAIN.bool1", address.Normalized);
            Assert.AreEqual("ADS", address.Area);
            Assert.IsFalse(address.IsBitAddress);
        }

        [Test]
        public void AdsClientAdvertisesNativeSubscriptions()
        {
            using (var client = new AdsClient(new AdsClientOptions
            {
                DeviceId = "ads-test",
                AmsNetId = "192.168.1.90.1.1",
            }))
            {
                Assert.AreEqual(ProtocolKind.TwinCatAds, client.Kind);
                Assert.IsTrue(client.Capabilities.SupportsNativeSubscriptions);
                Assert.IsTrue(client.Capabilities.SupportsString);
                Assert.IsTrue(client.Capabilities.SupportsByteArray);
            }
        }

        [Test]
        public void InvalidAmsNetIdIsRejectedBeforeConnect()
        {
            Assert.Throws<ArgumentException>(() => new AdsClient(new AdsClientOptions
            {
                DeviceId = "ads-test",
                AmsNetId = "192.168.1.90",
            }));
        }

        [Test]
        public void AdsOptionsUseSafeBatchAndStateDefaults()
        {
            var options = new AdsClientOptions();

            Assert.IsTrue(options.EnableSumCommands);
            Assert.AreEqual(500, options.MaxBatchItems);
            Assert.AreEqual(61440, options.MaxBatchPayloadBytes);
            Assert.IsTrue(options.ValidateTargetStateOnConnect);
            Assert.IsFalse(options.SynchronizeNotifications);
        }

        [Test]
        public void AdsOfficialPrimitiveTypesKeepStableExistingValues()
        {
            Assert.AreEqual(1, (int)DataType.Bool);
            Assert.AreEqual(4, (int)DataType.Int32);
            Assert.AreEqual(6, (int)DataType.Float);
            Assert.AreEqual(7, (int)DataType.Double);
            Assert.AreEqual(8, (int)DataType.String);
            Assert.AreEqual(13, (int)DataType.SByte);
            Assert.AreEqual(14, (int)DataType.Int64);
            Assert.AreEqual(15, (int)DataType.UInt64);
            Assert.AreEqual(16, (int)DataType.Time);
            Assert.AreEqual(17, (int)DataType.WString);
        }
    }
}
