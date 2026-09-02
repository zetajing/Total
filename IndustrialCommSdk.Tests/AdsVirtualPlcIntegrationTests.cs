using System;
using System.Collections.Generic;
using System.Globalization;
using System.Threading;
using System.Threading.Tasks;
using IndustrialCommSdk.Abstractions;
using IndustrialCommSdk.Protocols.Ads;
using IndustrialCommSdk.Runtime;
using NUnit.Framework;

namespace IndustrialCommSdk.Tests
{
    [TestFixture]
    public sealed class AdsVirtualPlcIntegrationTests
    {
        [Test, Explicit("Requires a running virtual TwinCAT PLC and ADS router.")]
        public async Task ScreenshotVariables_ReadExpectedIdleValues()
        {
            var configuration = AdsIntegrationConfiguration.FromEnvironment();
            using (var client = CreateClient(configuration))
            {
                await client.ConnectAsync(CancellationToken.None);
                try
                {
                    Assert.IsFalse(await client.ReadBoolAsync("MAIN.xStart"));
                    Assert.IsFalse(await client.ReadBoolAsync("MAIN.xStop"));
                    Assert.IsFalse(await client.ReadBoolAsync("MAIN.xMotorRun"));
                    Assert.IsFalse(await client.ReadBoolAsync("MAIN.xError"));
                    Assert.AreEqual(0, await client.ReadInt32Async("MAIN.nCount"));
                    Assert.AreEqual(100, await client.ReadInt32Async("MAIN.nTarget"));
                    Assert.That(await client.ReadFloatAsync("MAIN.rSpeed"), Is.EqualTo(25f).Within(0.0001f));
                    Assert.That(await client.ReadDoubleAsync("MAIN.lrTemperature"), Is.EqualTo(23.5d).Within(0.0001d));
                    StringAssert.AreEqualIgnoringCase("Idle", (await client.ReadStringAsync(
                        "MAIN.sStatus", configuration.StatusLength)).TrimEnd('\0'));
                    Assert.AreEqual(TimeSpan.FromMilliseconds(500), await client.ReadAnyAsync<TimeSpan>("MAIN.tDelay"));
                }
                finally
                {
                    await client.DisconnectAsync(CancellationToken.None);
                }
            }
        }

        [Test, Explicit("Requires a running virtual TwinCAT PLC and ADS router.")]
        public async Task SafeWriteVariables_AreRestoredAfterVerification()
        {
            var configuration = AdsIntegrationConfiguration.FromEnvironment();
            using (var client = CreateClient(configuration))
            {
                await client.ConnectAsync(CancellationToken.None);
                try
                {
                    var originalTarget = await client.ReadInt32Async("MAIN.nTarget");
                    var originalSpeed = await client.ReadFloatAsync("MAIN.rSpeed");
                    var originalDelay = await client.ReadAnyAsync<TimeSpan>("MAIN.tDelay");
                    try
                    {
                        await client.WriteAsync("MAIN.nTarget", originalTarget + 1);
                        await client.WriteAsync("MAIN.rSpeed", originalSpeed + 1f);
                        await client.WriteAnyAsync("MAIN.tDelay", originalDelay + TimeSpan.FromMilliseconds(100));

                        Assert.AreEqual(originalTarget + 1, await client.ReadInt32Async("MAIN.nTarget"));
                        Assert.That(await client.ReadFloatAsync("MAIN.rSpeed"), Is.EqualTo(originalSpeed + 1f).Within(0.0001f));
                        Assert.AreEqual(originalDelay + TimeSpan.FromMilliseconds(100), await client.ReadAnyAsync<TimeSpan>("MAIN.tDelay"));
                    }
                    finally
                    {
                        await client.WriteAsync("MAIN.nTarget", originalTarget);
                        await client.WriteAsync("MAIN.rSpeed", originalSpeed);
                        await client.WriteAnyAsync("MAIN.tDelay", originalDelay);
                    }
                }
                finally
                {
                    await client.DisconnectAsync(CancellationToken.None);
                }
            }
        }

        [Test, Explicit("Requires a running virtual TwinCAT PLC and ADS router.")]
        public async Task TargetChange_ProducesOneOnChangeNotification()
        {
            var configuration = AdsIntegrationConfiguration.FromEnvironment();
            using (var client = CreateClient(configuration))
            {
                await client.ConnectAsync(CancellationToken.None);
                try
                {
                    var originalTarget = await client.ReadInt32Async("MAIN.nTarget");
                    var notificationCount = 0;
                    var changed = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
                    var subscription = new SubscriptionRequest(
                        "ads-target-change",
                        client.DeviceId,
                        new[] { new ReadRequest(client.DeviceId, "MAIN.nTarget", DataType.Int32) },
                        TimeSpan.FromMilliseconds(50),
                        reportOnChangeOnly: true);

                    EventHandler<SubscriptionEvent> handler = (sender, args) =>
                    {
                        if (args.Values.Count > 0 && Equals(args.Values[0].Value, originalTarget + 1))
                        {
                            if (Interlocked.Increment(ref notificationCount) == 1)
                                changed.TrySetResult(true);
                        }
                    };

                    var subscriptionId = await client.SubscribeAsync(subscription, handler, CancellationToken.None);
                    try
                    {
                        await Task.Delay(200);
                        Interlocked.Exchange(ref notificationCount, 0);
                        await client.WriteAsync("MAIN.nTarget", originalTarget + 1);
                        Assert.IsTrue(await WaitAsync(changed.Task, TimeSpan.FromSeconds(5)));
                        await Task.Delay(500);
                        Assert.AreEqual(1, Volatile.Read(ref notificationCount));
                    }
                    finally
                    {
                        await client.UnsubscribeAsync(subscriptionId, CancellationToken.None);
                        await client.WriteAsync("MAIN.nTarget", originalTarget);
                    }
                }
                finally
                {
                    await client.DisconnectAsync(CancellationToken.None);
                }
            }
        }

        private static AdsClient CreateClient(AdsIntegrationConfiguration configuration)
        {
            return new AdsClient(new AdsClientOptions
            {
                DeviceId = "ads-virtual-plc-integration",
                AmsNetId = configuration.TargetAmsNetId,
                Port = configuration.Port,
                ConnectTimeoutMilliseconds = 10000,
                OperationTimeoutMilliseconds = 5000,
                EnableSumCommands = true,
                ValidateTargetStateOnConnect = true,
            });
        }

        private static async Task<bool> WaitAsync(Task<bool> task, TimeSpan timeout)
        {
            using (var cancellation = new CancellationTokenSource(timeout))
            {
                try
                {
                    await task.WaitAsync(cancellation.Token);
                    return true;
                }
                catch (OperationCanceledException)
                {
                    return false;
                }
            }
        }

        private sealed class AdsIntegrationConfiguration
        {
            public string TargetAmsNetId { get; private set; }
            public string TargetIp { get; private set; }
            public string LocalAmsNetId { get; private set; }
            public string RouterMode { get; private set; }
            public int Port { get; private set; }
            public ushort StatusLength { get; private set; }

            public static AdsIntegrationConfiguration FromEnvironment()
            {
                var targetAmsNetId = Environment.GetEnvironmentVariable("ADS_VIRTUAL_PLC_TARGET_AMS_NET_ID");
                if (string.IsNullOrWhiteSpace(targetAmsNetId))
                {
                    Assert.Ignore("Set ADS_VIRTUAL_PLC_TARGET_AMS_NET_ID before running the explicit ADS integration tests.");
                }

                var portText = Environment.GetEnvironmentVariable("ADS_VIRTUAL_PLC_PORT");
                var statusLengthText = Environment.GetEnvironmentVariable("ADS_VIRTUAL_PLC_STATUS_LENGTH");
                var configuration = new AdsIntegrationConfiguration
                {
                    TargetAmsNetId = targetAmsNetId,
                    TargetIp = Environment.GetEnvironmentVariable("ADS_VIRTUAL_PLC_TARGET_IP"),
                    LocalAmsNetId = Environment.GetEnvironmentVariable("ADS_VIRTUAL_PLC_LOCAL_AMS_NET_ID"),
                    RouterMode = Environment.GetEnvironmentVariable("ADS_VIRTUAL_PLC_ROUTER_MODE") ?? "system",
                    Port = ParsePositiveInt(portText, 851),
                    StatusLength = (ushort)ParsePositiveInt(statusLengthText, 80),
                };
                TestContext.Progress.WriteLine(
                    "ADS integration setup: RouterMode={0}, TargetIp={1}, LocalAmsNetId={2}, TargetAmsNetId={3}",
                    configuration.RouterMode,
                    configuration.TargetIp ?? "(not set)",
                    configuration.LocalAmsNetId ?? "(not set)",
                    configuration.TargetAmsNetId);
                return configuration;
            }

            private static int ParsePositiveInt(string value, int fallback)
            {
                return int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsed) && parsed > 0
                    ? parsed
                    : fallback;
            }
        }
    }
}
