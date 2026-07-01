using System;
using System.IO.Ports;
using System.Threading;
using IndustrialCommSdk.Abstractions;
using IndustrialCommSdk.Protocols.Modbus;
using NUnit.Framework;

namespace IndustrialCommSdk.Tests
{
    /// <summary>
    ///     对 <see cref="ModbusRtuClient" /> 和 <see cref="ModbusRtuClientOptions" /> 的单元测试。
    ///     验证 RTU 客户端的参数校验、Options 默认值和配置正确性。
    ///     这些测试不依赖真实串口，重点确保调用方在明显错误的输入下能收到清晰异常。
    /// </summary>
    [TestFixture]
    public class ModbusRtuClientTests
    {
        /// <summary>
        ///     验证 <see cref="ModbusRtuClientOptions" /> 的默认值是否符合预期。
        ///     默认波特率 9600、数据位 8、无校验、1 停止位、从站 ID 1。
        /// </summary>
        [Test]
        public void Options_Should_Have_Correct_Defaults()
        {
            var options = new ModbusRtuClientOptions();

            Assert.That(options.BaudRate, Is.EqualTo(9600));
            Assert.That(options.DataBits, Is.EqualTo(8));
            Assert.That(options.Parity, Is.EqualTo(Parity.Even));
            Assert.That(options.StopBits, Is.EqualTo(StopBits.One));
            Assert.That(options.ReadTimeout, Is.EqualTo(3000));
            Assert.That(options.WriteTimeout, Is.EqualTo(3000));
            Assert.That(options.Retries, Is.EqualTo(2));
            Assert.That(options.WaitToRetryMilliseconds, Is.EqualTo(100));
            Assert.That(options.SlaveId, Is.EqualTo(1));
            Assert.That(options.DeviceProfile, Is.Not.Null);
        }

        /// <summary>
        ///     验证当 <see cref="ModbusRtuClientOptions"/> 为 null 时，
        ///     构造函数应抛出 <see cref="ArgumentNullException" />。
        /// </summary>
        [Test]
        public void Constructor_Should_Reject_Null_Options()
        {
            Assert.Throws<ArgumentNullException>(() => new ModbusRtuClient(null));
        }

        [TestCase(null)]
        [TestCase("")]
        [TestCase("   ")]
        public void Constructor_Should_Reject_Missing_Port_Name(string portName)
        {
            Assert.Throws<ArgumentException>(() => new ModbusRtuClient(new ModbusRtuClientOptions
            {
                DeviceId = "rtu-test",
                PortName = portName,
            }));
        }

        [TestCase(0)]
        [TestCase(248)]
        public void Constructor_Should_Reject_Invalid_Slave_Id(byte slaveId)
        {
            Assert.Throws<ArgumentOutOfRangeException>(() => new ModbusRtuClient(new ModbusRtuClientOptions
            {
                DeviceId = "rtu-test",
                PortName = "COM1",
                SlaveId = slaveId,
            }));
        }

        [TestCase(5)]
        [TestCase(7)]
        [TestCase(9)]
        public void Constructor_Should_Reject_Non_Eight_Data_Bits(int dataBits)
        {
            Assert.Throws<ArgumentOutOfRangeException>(() => new ModbusRtuClient(new ModbusRtuClientOptions
            {
                DeviceId = "rtu-test",
                PortName = "COM1",
                DataBits = dataBits,
            }));
        }

        /// <summary>
        ///     验证 RTU 客户端创建后默认未连接。
        /// </summary>
        [Test]
        public void Client_Should_Not_Be_Connected_Initially()
        {
            var client = CreateClient("COM99");

            Assert.That(client.IsConnected, Is.False);
        }

        /// <summary>
        ///     验证连接不存在的串口时应抛出 <see cref="IndustrialCommSdk.Exceptions.IndustrialConnectionException" />。
        /// </summary>
        [Test]
        public void ConnectAsync_Should_Throw_When_Port_Does_Not_Exist()
        {
            var client = CreateClient("COM_NONEXIST_9999");

            Assert.ThrowsAsync<IndustrialCommSdk.Exceptions.IndustrialConnectionException>(
                async () => await client.ConnectAsync(CancellationToken.None));
        }

        /// <summary>
        ///     验证工厂方法能正确创建 RTU 客户端实例。
        /// </summary>
        [Test]
        public void Factory_Should_Create_Rtu_Client()
        {
            var options = new ModbusRtuClientOptions
            {
                DeviceId = "rtu-test",
                PortName = "COM1",
            };

            var client = IndustrialClientFactory.CreateModbusRtu(options);

            Assert.That(client, Is.Not.Null);
            Assert.That(client, Is.InstanceOf<ModbusRtuClient>());
            Assert.That(client.DeviceId, Is.EqualTo("rtu-test"));
        }

        /// <summary>
        ///     验证 <see cref="ModbusRtuClientOptions" /> 的自定义值能正确传递。
        /// </summary>
        [Test]
        public void Options_Should_Accept_Custom_Values()
        {
            var options = new ModbusRtuClientOptions
            {
                DeviceId = "custom-rtu",
                PortName = "COM5",
                BaudRate = 115200,
                DataBits = 8,
                Parity = Parity.Even,
                StopBits = StopBits.Two,
                SlaveId = 3,
            };

            Assert.That(options.PortName, Is.EqualTo("COM5"));
            Assert.That(options.BaudRate, Is.EqualTo(115200));
            Assert.That(options.DataBits, Is.EqualTo(8));
            Assert.That(options.Parity, Is.EqualTo(Parity.Even));
            Assert.That(options.StopBits, Is.EqualTo(StopBits.Two));
            Assert.That(options.SlaveId, Is.EqualTo(3));
        }

        /// <summary>
        ///     验证 RTU 客户端的 Kind 属性应为 <see cref="ProtocolKind.ModbusRtu" />。
        /// </summary>
        [Test]
        public void Client_Should_Report_ModbusRtu_Kind()
        {
            var client = CreateClient("COM99");

            Assert.That(client.Kind, Is.EqualTo(ProtocolKind.ModbusRtu));
        }

        [TestCase("HR0", ModbusArea.HoldingRegister, 0)]
        [TestCase("IR10", ModbusArea.InputRegister, 10)]
        [TestCase("C5", ModbusArea.Coil, 5)]
        [TestCase("DI7", ModbusArea.DiscreteInput, 7)]
        [TestCase("40001", ModbusArea.HoldingRegister, 0)]
        [TestCase("30001", ModbusArea.InputRegister, 0)]
        [TestCase("00001", ModbusArea.Coil, 0)]
        [TestCase("10001", ModbusArea.DiscreteInput, 0)]
        public void Generic_Profile_Should_Parse_Common_Address_Formats(
            string address, ModbusArea expectedArea, int expectedOffset)
        {
            var parsed = ModbusDeviceProfiles.Generic.ParseAddress(address);

            Assert.That(parsed.Area, Is.EqualTo(expectedArea));
            Assert.That(parsed.ZeroBasedAddress, Is.EqualTo(expectedOffset));
        }

        private static ModbusRtuClient CreateClient(string portName)
        {
            return new ModbusRtuClient(new ModbusRtuClientOptions
            {
                DeviceId = "rtu-test",
                PortName = portName,
            });
        }
    }
}
