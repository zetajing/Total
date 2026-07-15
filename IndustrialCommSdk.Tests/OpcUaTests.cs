using System;
using IndustrialCommSdk;
using IndustrialCommSdk.Abstractions;
using IndustrialCommSdk.Runtime.Configuration;
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
            using (var client = new OpcUaClient(new OpcUaClientOptions
            {
                DeviceId = "ua-plc", EndpointUrl = "opc.tcp://127.0.0.1:4840",
            }))
            {
                Assert.AreEqual(ProtocolKind.OpcUa, client.Kind);
                Assert.AreEqual("ua-plc", client.DeviceId);
                Assert.IsTrue(client.Capabilities.SupportsOptimizedBatchRead);
                Assert.IsTrue(client.Capabilities.SupportsString);
            }
        }

        [Test]
        public void Configuration_SupportsEndpointAndCredentials()
        {
            var sdk = IndustrialSdk.CreateDefault();
            var config = sdk.ParseConfiguration("{\"devices\":[{\"name\":\"ua-plc\",\"protocol\":\"opc-ua\",\"pointsFile\":\"points.json\",\"runtime\":{\"pollingIntervalMilliseconds\":1000,\"reconnectDelayMilliseconds\":3000,\"operationTimeoutMilliseconds\":5000},\"settings\":{\"endpointUrl\":\"opc.tcp://localhost:4840\",\"username\":\"operator\",\"password\":\"secret\"}}]}");
            using (var client = sdk.CreateClient(config.FindDevice("ua-plc")))
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
