using System.Threading.Tasks;
using InduLink.Abstractions;
using InduLink.Protocols.Ads;
using InduLink.Protocols.Mc;
using InduLink.Protocols.Modbus;
using InduLink.Protocols.S7;
using NUnit.Framework;

namespace InduLink.Tests
{
    [TestFixture]
    public sealed class ClientCreationTests
    {
        [Test]
        public async Task QuickFactories_CreateExpectedClientsAndDeviceIds()
        {
            await using var s7 = SiemensS7Client.Create("192.168.1.10");
            await using var modbusTcp = ModbusTcpClient.Create("192.168.1.20", slaveId: 2);
            await using var modbusRtu = ModbusRtuClient.Create("COM3", slaveId: 3);
            await using var mc = MitsubishiMcClient.Create("192.168.1.30");
            await using var ads = AdsClient.Create();

            Assert.Multiple(() =>
            {
                Assert.AreEqual("s7:192.168.1.10", s7.DeviceId);
                Assert.AreEqual(ProtocolKind.SiemensS7, s7.Kind);
                Assert.AreEqual("modbus-tcp:192.168.1.20:502:2", modbusTcp.DeviceId);
                Assert.AreEqual(ProtocolKind.ModbusTcp, modbusTcp.Kind);
                Assert.AreEqual("modbus-rtu:COM3:3", modbusRtu.DeviceId);
                Assert.AreEqual(ProtocolKind.ModbusRtu, modbusRtu.Kind);
                Assert.AreEqual("mc:192.168.1.30:5000", mc.DeviceId);
                Assert.AreEqual(ProtocolKind.MitsubishiMc, mc.Kind);
                Assert.AreEqual("ads:local:851", ads.DeviceId);
                Assert.AreEqual(ProtocolKind.TwinCatAds, ads.Kind);
            });
        }

        [Test]
        public async Task QuickFactories_RespectExplicitDeviceIds()
        {
            await using var s7 = SiemensS7Client.Create(
                " 192.168.1.10 ",
                deviceId: " line-1-plc ");

            Assert.AreEqual("line-1-plc", s7.DeviceId);
        }
    }
}
