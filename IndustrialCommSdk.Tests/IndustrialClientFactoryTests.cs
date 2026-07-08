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
    }
}
