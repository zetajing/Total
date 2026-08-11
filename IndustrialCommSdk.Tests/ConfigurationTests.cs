using System;
using System.IO;
using IndustrialCommSdk;
using IndustrialCommSdk.Runtime.Configuration;
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
        public void GenericProfile_ParsesStandardModbusAreas()
        {
            var holding = ModbusDeviceProfiles.Generic.ParseAddress("40001");
            var input = ModbusDeviceProfiles.Generic.ParseAddress("IR3");
            var coil = ModbusDeviceProfiles.Generic.ParseAddress("00005");
            var discreteInput = ModbusDeviceProfiles.Generic.ParseAddress("DI7");

            Assert.AreEqual(ModbusArea.HoldingRegister, holding.Area);
            Assert.AreEqual((ushort)0, holding.ZeroBasedAddress);
            Assert.AreEqual(ModbusArea.InputRegister, input.Area);
            Assert.AreEqual((ushort)3, input.ZeroBasedAddress);
            Assert.AreEqual(ModbusArea.Coil, coil.Area);
            Assert.AreEqual((ushort)4, coil.ZeroBasedAddress);
            Assert.AreEqual(ModbusArea.DiscreteInput, discreteInput.Area);
            Assert.AreEqual((ushort)7, discreteInput.ZeroBasedAddress);
        }

        [Test]
        public void JsonProfile_CanBeLoadedAndSelected()
        {
            var key = "test-custom-profile-" + Guid.NewGuid().ToString("N");
            var path = Path.Combine(TestContext.CurrentContext.TestDirectory, key + ".json");
            File.WriteAllText(path, "{\"profiles\":[{\"key\":\"" + key + "\",\"displayName\":\"测试自定义 PLC\",\"defaultAddress\":\"D0\",\"exampleAddresses\":\"D0, M0\",\"addressPattern\":\"^([A-Z]+)(\\\\d+)$\",\"lowWordFirst\":false,\"mappings\":[{\"prefix\":\"D\",\"area\":\"HoldingRegister\",\"base\":100,\"max\":10,\"radix\":\"decimal\"},{\"prefix\":\"M\",\"area\":\"Coil\",\"base\":20,\"max\":10,\"radix\":\"decimal\"}]}]}");
            try
            {
                ModbusDeviceProfiles.LoadJsonProfiles(path);
                var profile = ModbusDeviceProfiles.GetRequired(key);

                Assert.AreEqual("测试自定义 PLC", profile.DisplayName);
                Assert.AreEqual(ModbusArea.HoldingRegister, profile.ParseAddress("D3").Area);
                Assert.AreEqual((ushort)103, profile.ParseAddress("D3").ZeroBasedAddress);
                Assert.AreEqual(ModbusArea.Coil, profile.ParseAddress("M2").Area);
                Assert.AreEqual((ushort)22, profile.ParseAddress("M2").ZeroBasedAddress);
            }
            finally
            {
                if (File.Exists(path)) File.Delete(path);
            }
        }

        [Test]
        public void GetRequired_RejectsUnknownProfile()
        {
            Assert.Throws<System.Collections.Generic.KeyNotFoundException>(() => ModbusDeviceProfiles.GetRequired("unknown-device"));
        }
    }
}
