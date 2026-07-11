using System;
using IndustrialCommSdk;
using IndustrialCommSdk.Abstractions;
using IndustrialCommSdk.Protocols.Common;
using NUnit.Framework;

namespace IndustrialCommSdk.Tests
{
    [TestFixture]
    public sealed class MqttRedisTests
    {
        [Test]
        public void Factories_CreateExpectedProtocolClients()
        {
            using (var mqtt = IndustrialClientFactory.Mqtt("127.0.0.1", deviceId: "mqtt-device"))
            using (var redis = IndustrialClientFactory.Redis("127.0.0.1", deviceId: "redis-device"))
            {
                Assert.AreEqual(ProtocolKind.Mqtt, mqtt.Kind);
                Assert.AreEqual(ProtocolKind.Redis, redis.Kind);
                Assert.IsTrue(redis.Capabilities.SupportsOptimizedBatchRead);
                Assert.IsTrue(mqtt.Capabilities.SupportsByteArray);
            }
        }

        [TestCase("mqtt", ProtocolKind.Mqtt)]
        [TestCase("redis", ProtocolKind.Redis)]
        public void FromConfig_SupportsNewProtocols(string protocol, ProtocolKind expected)
        {
            var json = string.Format("{{\"devices\":[{{\"name\":\"service\",\"protocol\":\"{0}\",\"host\":\"localhost\",\"pointsFile\":\"points.json\"}}]}}", protocol);
            var config = IndustrialSdkConfig.FromJson(json);
            using (var client = IndustrialClientFactory.FromConfig(config, "service")) Assert.AreEqual(expected, client.Kind);
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
            Assert.Throws<ArgumentOutOfRangeException>(() => IndustrialClientFactory.Mqtt("localhost", qos: 3));
        }
    }
}
