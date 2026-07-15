using System;
using IndustrialCommSdk;
using IndustrialCommSdk.Abstractions;
using IndustrialCommSdk.Runtime.Configuration;
using IndustrialCommSdk.Protocols.Common;
using IndustrialCommSdk.Protocols.Mqtt;
using IndustrialCommSdk.Protocols.Redis;
using NUnit.Framework;

namespace IndustrialCommSdk.Tests
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
                Assert.AreEqual(ProtocolKind.Mqtt, mqtt.Kind);
                Assert.AreEqual(ProtocolKind.Redis, redis.Kind);
                Assert.IsTrue(redis.Capabilities.SupportsOptimizedBatchRead);
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
    }
}
