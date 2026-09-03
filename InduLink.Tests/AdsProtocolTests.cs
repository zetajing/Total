using System;
using InduLink;
using InduLink.Abstractions;
using InduLink.Protocols.Ads;
using NUnit.Framework;
using TwinCAT.PlcOpen;

namespace InduLink.Tests
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

        [Test]
        public void AdsPlcOpenTypesAreMappedToOfficialBeckhoffTypes()
        {
            Assert.AreEqual(18, (int)DataType.Date);
            Assert.AreEqual(19, (int)DataType.DateTime);
            Assert.AreEqual(20, (int)DataType.TimeOfDay);
            Assert.AreEqual(21, (int)DataType.LTime);

            Assert.AreEqual(typeof(DATE), AdsTypeCodec.GetClrType(DataType.Date));
            Assert.AreEqual(typeof(DT), AdsTypeCodec.GetClrType(DataType.DateTime));
            Assert.AreEqual(typeof(TOD), AdsTypeCodec.GetClrType(DataType.TimeOfDay));
            Assert.AreEqual(typeof(LTIME), AdsTypeCodec.GetClrType(DataType.LTime));
        }

        [Test]
        public void AdsPlcOpenValuesConvertToSdkFriendlyValues()
        {
            var localDate = new DateTime(2026, 9, 3, 0, 0, 0, DateTimeKind.Unspecified);
            var localDateTime = new DateTime(2026, 9, 3, 12, 34, 56, DateTimeKind.Unspecified);
            var date = new DateTimeOffset(localDate, TimeZoneInfo.Local.GetUtcOffset(localDate));
            var dateTime = new DateTimeOffset(localDateTime, TimeZoneInfo.Local.GetUtcOffset(localDateTime));
            var plcDate = (DATE)AdsTypeCodec.ConvertForWrite(DataType.Date, date);
            var plcDateTime = (DT)AdsTypeCodec.ConvertForWrite(DataType.DateTime, dateTime);
            var plcTimeOfDay = (TOD)AdsTypeCodec.ConvertForWrite(DataType.TimeOfDay, TimeSpan.FromHours(12));
            var plcLongTime = (LTIME)AdsTypeCodec.ConvertForWrite(DataType.LTime, TimeSpan.FromMilliseconds(500));

            Assert.AreEqual(date, AdsTypeCodec.ConvertForRead(DataType.Date, plcDate));
            Assert.AreEqual(dateTime, AdsTypeCodec.ConvertForRead(DataType.DateTime, plcDateTime));
            Assert.AreEqual(TimeSpan.FromHours(12), AdsTypeCodec.ConvertForRead(DataType.TimeOfDay, plcTimeOfDay));
            Assert.AreEqual(TimeSpan.FromMilliseconds(500), AdsTypeCodec.ConvertForRead(DataType.LTime, plcLongTime));
        }
    }
}
