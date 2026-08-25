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
    }
}
