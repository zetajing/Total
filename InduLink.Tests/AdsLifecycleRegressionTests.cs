using System;
using System.Reflection;
using InduLink.Protocols.Ads;
using NUnit.Framework;
using TwinCAT;
using SdkAdsClient = InduLink.Protocols.Ads.AdsClient;

namespace InduLink.Tests
{
    [TestFixture]
    public sealed class AdsLifecycleRegressionTests
    {
        [Test]
        public void DisposingRetiredClient_DoesNotInvalidateReplacement()
        {
            using var sdk = new SdkAdsClient(new AdsClientOptions { DeviceId = "ads-dispose-generation" });
            using var retired = new TwinCAT.Ads.AdsClient();
            using var current = new TwinCAT.Ads.AdsClient();
            Field("_adsClient").SetValue(sdk, current);
            Field("_transportLost").SetValue(sdk, 0);
            typeof(SdkAdsClient).GetMethod("DetachAndDisposeClient", BindingFlags.Instance | BindingFlags.NonPublic)
                .Invoke(sdk, new object[] { retired });
            Assert.AreSame(current, Field("_adsClient").GetValue(sdk));
            Assert.AreEqual(0, Field("_transportLost").GetValue(sdk));
        }

        [Test]
        public void LateDisconnectFromOldClient_DoesNotInvalidateReplacement()
        {
            using var sdk = new SdkAdsClient(new AdsClientOptions { DeviceId = "ads-generation" });
            using var oldClient = new TwinCAT.Ads.AdsClient();
            using var current = new TwinCAT.Ads.AdsClient();
            Field("_adsClient").SetValue(sdk, current);
            Field("_transportLost").SetValue(sdk, 0);
            RaiseState(sdk, oldClient, ConnectionState.Disconnected);
            Assert.AreEqual(0, Field("_transportLost").GetValue(sdk));
        }

        [Test]
        public void TransportConnectedEvent_DoesNotDeclareLogicalRecovery()
        {
            using var sdk = new SdkAdsClient(new AdsClientOptions { DeviceId = "ads-recovery" });
            using var current = new TwinCAT.Ads.AdsClient();
            Field("_adsClient").SetValue(sdk, current);
            RaiseState(sdk, current, ConnectionState.Disconnected);
            RaiseState(sdk, current, ConnectionState.Connected);
            Assert.AreEqual(1, Field("_transportLost").GetValue(sdk),
                "Transport reconnection alone does not restore variable handles or subscriptions.");
        }

        private static FieldInfo Field(string name) => typeof(SdkAdsClient).GetField(name, BindingFlags.Instance | BindingFlags.NonPublic);

        private static void RaiseState(SdkAdsClient sdk, object sender, ConnectionState state)
        {
            var args = new ConnectionStateChangedEventArgs(default, state, ConnectionState.Connected);
            typeof(SdkAdsClient).GetMethod("OnConnectionStateChanged", BindingFlags.Instance | BindingFlags.NonPublic)
                .Invoke(sdk, new[] { sender, args });
        }
    }
}
