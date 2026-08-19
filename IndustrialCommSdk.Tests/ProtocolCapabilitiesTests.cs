using IndustrialCommSdk.Abstractions;
using NUnit.Framework;

namespace IndustrialCommSdk.Tests
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
    }
}
