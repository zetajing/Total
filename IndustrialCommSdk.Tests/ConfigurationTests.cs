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
    }
}
