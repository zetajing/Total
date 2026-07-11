using IndustrialCommSdk;
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
            var config = IndustrialSdkConfig.FromJson("{\"devices\":[{\"name\":\"plc\",\"protocol\":\"modbus-tcp\",\"host\":\"127.0.0.1\",\"pointsFile\":\"points.json\",\"operationTimeoutMilliseconds\":1234}]}");
            Assert.AreEqual(1234, config.Devices[0].OperationTimeoutMilliseconds);
            StringAssert.Contains("operationTimeoutMilliseconds", config.ToJson());
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
        public void SimpleClient_UsesSelectedModbusProfile()
        {
            using (var client = SimpleClient.ModbusTcp("127.0.0.1", deviceProfile: ModbusDeviceProfiles.MitsubishiModbusTcp))
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
