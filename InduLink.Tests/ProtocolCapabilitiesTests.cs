using InduLink.Abstractions;
using NUnit.Framework;

namespace InduLink.Tests
{
    [TestFixture]
    public sealed class ProtocolCapabilitiesTests
    {
        [TestCase(ProtocolKind.ModbusTcp)]
        [TestCase(ProtocolKind.ModbusRtu)]
        [TestCase(ProtocolKind.SiemensS7)]
        [TestCase(ProtocolKind.OpcUa)]
        [TestCase(ProtocolKind.Redis)]
        public void ProtocolsWithBatchReadImplementationsAdvertiseOptimizedReads(ProtocolKind kind)
        {
            Assert.IsTrue(ProtocolCapabilities.ForProtocol(kind).SupportsOptimizedBatchRead);
        }

        [TestCase(ProtocolKind.ModbusTcp)]
        [TestCase(ProtocolKind.ModbusRtu)]
        [TestCase(ProtocolKind.SiemensS7)]
        [TestCase(ProtocolKind.Mqtt)]
        public void ProtocolsWithoutBatchWriteImplementationsDoNotAdvertiseOptimizedWrites(ProtocolKind kind)
        {
            Assert.IsFalse(ProtocolCapabilities.ForProtocol(kind).SupportsOptimizedBatchWrite);
        }

        [Test]
        public void OpcUaAndRedisKeepTheirOptimizedBatchWriteCapabilities()
        {
            Assert.IsTrue(ProtocolCapabilities.ForProtocol(ProtocolKind.OpcUa).SupportsOptimizedBatchWrite);
            Assert.IsTrue(ProtocolCapabilities.ForProtocol(ProtocolKind.Redis).SupportsOptimizedBatchWrite);
        }

        [Test]
        public void PlcAndDataServicesAdvertiseDifferentCapabilitySets()
        {
            var mc = ProtocolCapabilities.ForProtocol(ProtocolKind.MitsubishiMc);
            Assert.IsTrue(mc.SupportsRegisterRead);
            Assert.IsTrue(mc.SupportsRegisterWrite);
            Assert.IsTrue(mc.SupportsEventSubscription);
            Assert.IsFalse(mc.SupportsKeyValue);

            var mqtt = ProtocolCapabilities.ForProtocol(ProtocolKind.Mqtt);
            Assert.IsFalse(mqtt.SupportsRegisterRead);
            Assert.IsFalse(mqtt.SupportsRegisterWrite);
            Assert.IsTrue(mqtt.SupportsKeyValue);
            Assert.IsFalse(mqtt.SupportsEventSubscription);

            var redis = ProtocolCapabilities.ForProtocol(ProtocolKind.Redis);
            Assert.IsFalse(redis.SupportsRegisterRead);
            Assert.IsFalse(redis.SupportsRegisterWrite);
            Assert.IsTrue(redis.SupportsKeyValue);
        }
    }
}
