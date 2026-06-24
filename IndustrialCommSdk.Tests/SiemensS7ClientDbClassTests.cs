using System;
using System.Threading.Tasks;
using IndustrialCommSdk.Protocols.S7;
using NUnit.Framework;

namespace IndustrialCommSdk.Tests
{
    /// <summary>
    ///     针对 <see cref="SiemensS7Client" /> 的 DB 类映射扩展 API 做参数级校验测试。
    ///     这些测试不依赖真实 PLC，重点确保调用方在明显错误的输入下能收到清晰异常。
    /// </summary>
    [TestFixture]
    public class SiemensS7ClientDbClassTests
    {
        [Test]
        public void ReadDbClassAsync_Generic_Should_Reject_Invalid_Db_Number()
        {
            var client = CreateClient();

            Assert.ThrowsAsync<ArgumentOutOfRangeException>(
                async () => await client.ReadDbClassAsync<TestDbModel>(0));
        }

        [Test]
        public void ReadDbClassAsync_WithFactory_Should_Reject_Null_Factory()
        {
            var client = CreateClient();

            Assert.ThrowsAsync<ArgumentNullException>(
                async () => await client.ReadDbClassAsync<TestDbModel>(null, 9));
        }

        [Test]
        public void ReadDbClassAsync_Instance_Should_Reject_Null_Instance()
        {
            var client = CreateClient();

            Assert.ThrowsAsync<ArgumentNullException>(
                async () => await client.ReadDbClassAsync(null, 9));
        }

        [Test]
        public void WriteDbClassAsync_Should_Reject_Null_Value()
        {
            var client = CreateClient();

            Assert.ThrowsAsync<ArgumentNullException>(
                async () => await client.WriteDbClassAsync(null, 9));
        }

        [Test]
        public void WriteDbClassAsync_Should_Reject_Negative_Start_Byte()
        {
            var client = CreateClient();

            Assert.ThrowsAsync<ArgumentOutOfRangeException>(
                async () => await client.WriteDbClassAsync(new TestDbModel(), 9, -1));
        }

        private static SiemensS7Client CreateClient()
        {
            return new SiemensS7Client(
                new SiemensS7ClientOptions
                {
                    DeviceId = "s7-test",
                    Host = "127.0.0.1",
                    Rack = 0,
                    Slot = 1
                });
        }

        private sealed class TestDbModel
        {
            public bool BOOL_1 { get; set; }
            public ushort WORD_1 { get; set; }
        }
    }
}
