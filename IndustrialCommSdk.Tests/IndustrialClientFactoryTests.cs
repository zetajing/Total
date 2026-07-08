using IndustrialCommSdk.Abstractions;
using IndustrialCommSdk.Protocols.Mc;
using IndustrialCommSdk.Protocols.Modbus;
using IndustrialCommSdk.Protocols.S7;
using NUnit.Framework;

namespace IndustrialCommSdk.Tests
{
    [TestFixture]
    public sealed class IndustrialClientFactoryTests
    {
        [Test]
        public void CreateModbus_Should_Return_ModbusTcp_Client()
        {
            using (var client = IndustrialClientFactory.CreateModbus(new ModbusTcpClientOptions
            {
                DeviceId = "modbus-tcp-test",
                Host = "127.0.0.1",
            }))
            {
                Assert.That(client, Is.InstanceOf<ModbusTcpClient>());
                Assert.That(client.Kind, Is.EqualTo(ProtocolKind.ModbusTcp));
            }
        }

        [Test]
        public void ModbusTcp_Should_Create_Client_From_Minimal_Arguments()
        {
            using (var client = IndustrialClientFactory.ModbusTcp("127.0.0.1"))
            {
                Assert.That(client, Is.InstanceOf<ModbusTcpClient>());
                Assert.That(client.Kind, Is.EqualTo(ProtocolKind.ModbusTcp));
                Assert.That(client.DeviceId, Is.EqualTo("modbus-tcp-127.0.0.1-502-1"));
            }
        }

        [Test]
        public void CreateModbusTcp_Should_Return_ModbusTcp_Client()
        {
            using (var client = IndustrialClientFactory.CreateModbusTcp("127.0.0.1"))
            {
                Assert.That(client, Is.InstanceOf<ModbusTcpClient>());
                Assert.That(client.Kind, Is.EqualTo(ProtocolKind.ModbusTcp));
            }
        }

        [Test]
        public void ModbusTcp_Should_Validate_Host()
        {
            Assert.Throws<System.ArgumentException>(() => IndustrialClientFactory.ModbusTcp(""));
        }

        [Test]
        public void ModbusTcp_Should_Validate_Port()
        {
            Assert.Throws<System.ArgumentOutOfRangeException>(() => IndustrialClientFactory.ModbusTcp("127.0.0.1", 70000));
        }

        [Test]
        public void FromConfig_Should_Create_ModbusTcp_Client()
        {
            var config = IndustrialSdkConfig.FromJson(@"
{
  ""devices"": [
    {
      ""name"": ""plc1"",
      ""protocol"": ""modbus-tcp"",
      ""host"": ""192.168.1.10"",
      ""port"": 1502,
      ""slaveId"": 2,
      ""deviceProfile"": ""generic""
    }
  ]
}");

            using (var client = IndustrialClientFactory.FromConfig(config, "plc1"))
            {
                Assert.That(client, Is.InstanceOf<ModbusTcpClient>());
                Assert.That(client.DeviceId, Is.EqualTo("plc1"));
                Assert.That(client.Kind, Is.EqualTo(ProtocolKind.ModbusTcp));
            }
        }

        [Test]
        public void FromConfig_Should_Throw_When_Device_Missing()
        {
            var config = IndustrialSdkConfig.FromJson(@"{ ""devices"": [] }");

            Assert.Throws<System.Collections.Generic.KeyNotFoundException>(() => IndustrialClientFactory.FromConfig(config, "missing"));
        }

        [Test]
        public void CreateModbusRtu_Should_Return_ModbusRtu_Client()
        {
            using (var client = IndustrialClientFactory.CreateModbusRtu(new ModbusRtuClientOptions
            {
                DeviceId = "modbus-rtu-test",
                PortName = "COM1",
            }))
            {
                Assert.That(client, Is.InstanceOf<ModbusRtuClient>());
                Assert.That(client.Kind, Is.EqualTo(ProtocolKind.ModbusRtu));
            }
        }

        [Test]
        public void ModbusRtu_Should_Create_Client_From_Minimal_Arguments()
        {
            using (var client = IndustrialClientFactory.ModbusRtu("COM3"))
            {
                Assert.That(client, Is.InstanceOf<ModbusRtuClient>());
                Assert.That(client.Kind, Is.EqualTo(ProtocolKind.ModbusRtu));
                Assert.That(client.DeviceId, Is.EqualTo("modbus-rtu-COM3-9600-1"));
            }
        }

        [Test]
        public void CreateSiemensS7_Should_Return_SiemensS7_Client()
        {
            using (var client = IndustrialClientFactory.CreateSiemensS7(new SiemensS7ClientOptions
            {
                DeviceId = "s7-test",
                Host = "127.0.0.1",
            }))
            {
                Assert.That(client, Is.InstanceOf<SiemensS7Client>());
                Assert.That(client.Kind, Is.EqualTo(ProtocolKind.SiemensS7));
            }
        }

        [Test]
        public void SiemensS7_Should_Return_Concrete_Client_From_Minimal_Arguments()
        {
            using (var client = IndustrialClientFactory.SiemensS7("192.168.0.10"))
            {
                Assert.That(client, Is.InstanceOf<SiemensS7Client>());
                Assert.That(client.Kind, Is.EqualTo(ProtocolKind.SiemensS7));
                Assert.That(client.DeviceId, Is.EqualTo("siemens-s7-192.168.0.10-0-1"));
            }
        }

        [Test]
        public void CreateMitsubishiMc_Should_Return_MitsubishiMc_Client()
        {
            using (var client = IndustrialClientFactory.CreateMitsubishiMc(new MitsubishiMcClientOptions
            {
                DeviceId = "mc-test",
                Host = "127.0.0.1",
            }))
            {
                Assert.That(client, Is.InstanceOf<MitsubishiMcClient>());
                Assert.That(client.Kind, Is.EqualTo(ProtocolKind.MitsubishiMc));
            }
        }

        [Test]
        public void MitsubishiMc_Should_Create_Client_From_Minimal_Arguments()
        {
            using (var client = IndustrialClientFactory.MitsubishiMc("192.168.0.20"))
            {
                Assert.That(client, Is.InstanceOf<MitsubishiMcClient>());
                Assert.That(client.Kind, Is.EqualTo(ProtocolKind.MitsubishiMc));
                Assert.That(client.DeviceId, Is.EqualTo("mitsubishi-mc-192.168.0.20-5000"));
            }
        }
    }
}
