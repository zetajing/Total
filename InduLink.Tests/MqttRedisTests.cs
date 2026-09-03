using System;
using System.Threading;
using System.Threading.Tasks;
using InduLink;
using InduLink.Abstractions;
using InduLink.Runtime.Configuration;
using InduLink.Protocols.Common;
using InduLink.Protocols.Mqtt;
using InduLink.Protocols.Redis;
using NUnit.Framework;
using StackExchange.Redis;

namespace InduLink.Tests
{
    [TestFixture]
    public sealed class MqttRedisTests
    {
        [Test]
        public void Factories_CreateExpectedProtocolClients()
        {
            using (var mqtt = new MqttClient(new MqttClientOptions { DeviceId = "mqtt-device", Host = "127.0.0.1" }))
            using (var redis = new RedisClient(new RedisClientOptions { DeviceId = "redis-device", Host = "127.0.0.1" }))
            {
                Assert.IsInstanceOf<IKeyValueClient>(mqtt);
                Assert.IsInstanceOf<IKeyValueClient>(redis);
                Assert.AreEqual(ProtocolKind.Mqtt, mqtt.Kind);
                Assert.AreEqual(ProtocolKind.Redis, redis.Kind);
                Assert.IsTrue(redis.Capabilities.SupportsOptimizedBatchRead);
                Assert.IsFalse(mqtt.Capabilities.SupportsOptimizedBatchWrite);
                Assert.IsTrue(mqtt.Capabilities.SupportsByteArray);
            }
        }

        [TestCase("mqtt", ProtocolKind.Mqtt)]
        [TestCase("redis", ProtocolKind.Redis)]
        public void Configuration_SupportsNewProtocols(string protocol, ProtocolKind expected)
        {
            var sdk = IndustrialSdk.CreateDefault();
            var json = string.Format("{{\"devices\":[{{\"name\":\"service\",\"protocol\":\"{0}\",\"pointsFile\":\"points.json\",\"runtime\":{{\"pollingIntervalMilliseconds\":1000,\"reconnectDelayMilliseconds\":3000,\"operationTimeoutMilliseconds\":5000}},\"settings\":{{\"host\":\"localhost\"}}}}]}}", protocol);
            var config = sdk.ParseConfiguration(json);
            using (var client = sdk.CreateClient(config.FindDevice("service"))) Assert.AreEqual(expected, client.Kind);
        }

        [Test]
        public void TextCodec_RoundTripsSupportedValues()
        {
            Assert.AreEqual(123.5f, TextValueCodec.Decode(DataType.Float, TextValueCodec.Encode(DataType.Float, 123.5f)));
            Assert.AreEqual(true, TextValueCodec.Decode(DataType.Bool, TextValueCodec.Encode(DataType.Bool, true)));
            CollectionAssert.AreEqual(new byte[] { 1, 2, 3 }, (byte[])TextValueCodec.Decode(DataType.ByteArray, new byte[] { 1, 2, 3 }));
        }

        [Test]
        public void InvalidMqttQos_IsRejected()
        {
            var sdk = IndustrialSdk.CreateDefault();
            var device = new IndustrialDeviceConfig
            {
                Name = "mqtt", Protocol = "mqtt", PointsFile = "points.json",
                Settings = new MqttSettings { Host = "localhost", QualityOfService = 3 },
            };
            Assert.Throws<ArgumentException>(() => sdk.CreateClient(device));
        }

        [Test]
        public async Task RedisConnectionProvider_CoalescesConnectionCreationByKey()
        {
            using (var provider = new RedisConnectionProvider())
            {
                var calls = 0;
                Func<Task<ConnectionMultiplexer>> factory = async () =>
                {
                    Interlocked.Increment(ref calls);
                    await Task.Delay(25).ConfigureAwait(false);
                    return null;
                };

                await Task.WhenAll(
                    provider.GetOrCreateAsync("redis:test", factory, CancellationToken.None),
                    provider.GetOrCreateAsync("redis:test", factory, CancellationToken.None));

                Assert.AreEqual(1, calls);
            }
        }
    }
}
