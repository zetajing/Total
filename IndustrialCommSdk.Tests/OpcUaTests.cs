using System;
using IndustrialCommSdk;
using IndustrialCommSdk.Abstractions;
using IndustrialCommSdk.Protocols.OpcUa;
using NUnit.Framework;

namespace IndustrialCommSdk.Tests
{
    [TestFixture]
    public sealed class OpcUaTests
    {
        [Test]
        public void Factory_CreatesOpcUaClientWithExpectedCapabilities()
        {
            using (var client = IndustrialClientFactory.OpcUa("opc.tcp://127.0.0.1:4840", "ua-plc"))
            {
                Assert.AreEqual(ProtocolKind.OpcUa, client.Kind);
                Assert.AreEqual("ua-plc", client.DeviceId);
                Assert.IsTrue(client.Capabilities.SupportsOptimizedBatchRead);
                Assert.IsTrue(client.Capabilities.SupportsString);
            }
        }

        [Test]
        public void FromConfig_SupportsEndpointAndCredentials()
        {
            var config = IndustrialSdkConfig.FromJson("{\"devices\":[{\"name\":\"ua-plc\",\"protocol\":\"opc-ua\",\"endpointUrl\":\"opc.tcp://localhost:4840\",\"username\":\"operator\",\"password\":\"secret\",\"pointsFile\":\"points.json\"}]}");
            using (var client = IndustrialClientFactory.FromConfig(config, "ua-plc"))
                Assert.AreEqual(ProtocolKind.OpcUa, client.Kind);
        }

        [TestCase("ns=2;s=Machine/Temperature")]
        [TestCase("ns=2;i=1001")]
        [TestCase("i=2258")]
        public void ParseNodeId_AcceptsStandardNodeIds(string address)
        {
            Assert.AreEqual(address, OpcUaClient.ParseNodeId(address).ToString());
        }

        [Test]
        public void Options_RejectInvalidEndpoint()
        {
            Assert.Throws<ArgumentException>(() => new OpcUaClient(new OpcUaClientOptions
            {
                DeviceId = "ua-plc", EndpointUrl = "http://localhost:4840"
            }));
        }
    }
}
