using IndustrialCommSdk;
using IndustrialCommSdk.Configuration;
using IndustrialCommSdk.Protocols.Modbus;
using NUnit.Framework;

namespace IndustrialCommSdk.Tests
{
    [TestFixture]
    public sealed class ConfigurationTests
    {
        [Test]
        public void OperationTimeout_RoundTripsThroughJson()
        {
            var sdk = IndustrialSdk.CreateDefault();
            var config = sdk.ParseConfiguration("{\"devices\":[{\"name\":\"plc\",\"protocol\":\"modbus-tcp\",\"pointsFile\":\"points.json\",\"enabled\":true,\"runtime\":{\"pollingIntervalMilliseconds\":1000,\"reconnectDelayMilliseconds\":3000,\"operationTimeoutMilliseconds\":1234},\"settings\":{\"host\":\"127.0.0.1\"}}]}");
            Assert.AreEqual(1234, config.Devices[0].Runtime.OperationTimeoutMilliseconds);
            StringAssert.Contains("operationTimeoutMilliseconds", sdk.SerializeConfiguration(config));
        }

        [Test]
        public void ClientOptions_DefaultToFiveSecondsAndRejectInvalidValue()
        {
            Assert.AreEqual(5000, new ModbusTcpClientOptions().OperationTimeoutMilliseconds);
            Assert.Throws<System.ArgumentOutOfRangeException>(() => new ModbusTcpClient(new ModbusTcpClientOptions
            {
                DeviceId = "plc",
                Host = "127.0.0.1",
                OperationTimeoutMilliseconds = 0,
                DeviceProfile = ModbusDeviceProfiles.Generic,
            }));
        }

        [Test]
        public void DirectClient_UsesSelectedModbusProfile()
        {
            using (var client = new ModbusTcpClient(new ModbusTcpClientOptions
            {
                DeviceId = "plc",
                Host = "127.0.0.1",
                DeviceProfile = ModbusDeviceProfiles.MitsubishiModbusTcp,
            }))
            {
                Assert.AreEqual("mitsubishi-modbus-tcp", client.Profile.Key);
                Assert.AreEqual(ModbusArea.HoldingRegister, client.Profile.ParseAddress("D100").Area);
            }
        }

        [Test]
        public void GetRequired_RejectsUnknownProfile()
        {
            Assert.Throws<System.Collections.Generic.KeyNotFoundException>(() => ModbusDeviceProfiles.GetRequired("unknown-device"));
        }
    }
}
