using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using IndustrialCommSdk.Abstractions;
using IndustrialCommSdk.Exceptions;
using NUnit.Framework;

namespace IndustrialCommSdk.Tests
{
    /// <summary>
    ///     对 <see cref="IndustrialClientQuickExtensions" /> 快速扩展方法的单元测试。
    ///     验证通过 <see cref="IIndustrialClient" /> 接口调用强类型读写扩展方法时，
    ///     是否能正确构造请求、执行类型转换以及在数据质量异常或类型不兼容时抛出预期异常。
    ///     使用 <see cref="FakeIndustrialClient" /> 模拟底层客户端行为。
    /// </summary>
    [TestFixture]
    public class IndustrialClientQuickExtensionsTests
    {
        /// <summary>
        ///     验证 <c>ReadInt16Async</c> 扩展方法能正确构建 <see cref="ReadRequest" /> 并返回类型正确的值。
        ///     预期：返回值为 123，请求中的设备 ID、地址和数据类型均与调用参数一致。
        /// </summary>
        [Test]
        public async Task ReadInt16Async_Should_Build_Request_And_Return_Typed_Value()
        {
            var client = new FakeIndustrialClient
            {
                ReadResult = new DataValue("D100", DataType.Int16, (short)123, null, QualityStatus.Good, DateTimeOffset.UtcNow, null)
            };

            var value = await client.ReadInt16Async("D100");

            Assert.That(value, Is.EqualTo(123));
            Assert.That(client.LastReadRequest, Is.Not.Null);
            Assert.That(client.LastReadRequest.DeviceId, Is.EqualTo("fake-1"));
            Assert.That(client.LastReadRequest.Address, Is.EqualTo("D100"));
            Assert.That(client.LastReadRequest.DataType, Is.EqualTo(DataType.Int16));
        }

        /// <summary>
        ///     验证当读取到的数据质量状态为 <see cref="QualityStatus.Bad" /> 时，
        ///     扩展方法应抛出 <see cref="IndustrialProtocolException" /> 并包含服务端返回的错误消息。
        /// </summary>
        [Test]
        public void ReadValueAsync_Should_Throw_When_Quality_Is_Bad()
        {
            var client = new FakeIndustrialClient
            {
                ReadResult = new DataValue("D100", DataType.Int16, null, null, QualityStatus.Bad, DateTimeOffset.UtcNow, "boom")
            };

            var ex = Assert.ThrowsAsync<IndustrialProtocolException>(async () => await client.ReadInt16Async("D100"));

            Assert.That(ex.Message, Is.EqualTo("boom"));
        }

        /// <summary>
        ///     验证当读取到的数据值无法转换为目标 CLR 类型时，
        ///     扩展方法应抛出 <see cref="IndustrialDataConversionException" />。
        ///     本测试中返回值为字符串 "abc"，无法转换为 <see cref="short" />。
        /// </summary>
        [Test]
        public void ReadValueAsync_Should_Throw_When_Type_Cannot_Be_Converted()
        {
            var client = new FakeIndustrialClient
            {
                ReadResult = new DataValue("D100", DataType.Int16, "abc", null, QualityStatus.Good, DateTimeOffset.UtcNow, null)
            };

            Assert.ThrowsAsync<IndustrialDataConversionException>(async () => await client.ReadInt16Async("D100"));
        }

        /// <summary>
        ///     验证 <c>WriteAsync(bool)</c> 扩展方法能正确构建 <see cref="WriteRequest" />。
        ///     预期：请求中的设备 ID、地址、数据类型（Bool）和值（true）均与调用参数一致。
        /// </summary>
        [Test]
        public async Task WriteAsync_Bool_Should_Build_Write_Request()
        {
            var client = new FakeIndustrialClient();

            await client.WriteAsync("M10", true);

            Assert.That(client.LastWriteRequest, Is.Not.Null);
            Assert.That(client.LastWriteRequest.DeviceId, Is.EqualTo("fake-1"));
            Assert.That(client.LastWriteRequest.Address, Is.EqualTo("M10"));
            Assert.That(client.LastWriteRequest.DataType, Is.EqualTo(DataType.Bool));
            Assert.That(client.LastWriteRequest.Value, Is.EqualTo(true));
        }

        /// <summary>
        ///     验证 <c>WriteStringAsync</c> 扩展方法能正确识别用户指定的字符串长度参数。
        ///     预期：请求中的 Length 属性等于传入的显式长度值，数据类型为 <see cref="DataType.String" />。
        /// </summary>
        [Test]
        public async Task WriteStringAsync_Should_Use_Provided_Length()
        {
            var client = new FakeIndustrialClient();

            await client.WriteStringAsync("D200", "ABC", 2);

            Assert.That(client.LastWriteRequest.Length, Is.EqualTo(2));
            Assert.That(client.LastWriteRequest.DataType, Is.EqualTo(DataType.String));
        }

        /// <summary>
        ///     用于快速扩展方法测试的模拟工业客户端。
        ///     实现 <see cref="IIndustrialClient" /> 接口，固定返回预设的 <see cref="ReadResult" />，
        ///     并记录最后一次读写请求以便断言验证。
        /// </summary>
        private sealed class FakeIndustrialClient : IIndustrialClient
        {
            /// <summary>固定设备 ID，始终返回 "fake-1"。</summary>
            public string DeviceId { get; } = "fake-1";

            /// <summary>固定协议类型，始终返回 <see cref="ProtocolKind.ModbusTcp" />。</summary>
            public ProtocolKind Kind { get; } = ProtocolKind.ModbusTcp;

            /// <summary>固定连接状态，始终返回 <c>true</c>。</summary>
            public bool IsConnected { get; } = true;

            /// <summary>记录最后一次 <see cref="ReadAsync" /> 调用的请求参数。</summary>
            public ReadRequest LastReadRequest { get; private set; }

            /// <summary>记录最后一次 <see cref="WriteAsync" /> 调用的请求参数。</summary>
            public WriteRequest LastWriteRequest { get; private set; }

            /// <summary>预设的读取结果，由各测试方法在调用前设置。</summary>
            public DataValue ReadResult { get; set; }

            /// <summary>模拟连接操作，直接返回已完成任务。</summary>
            public Task ConnectAsync(CancellationToken cancellationToken) => Task.CompletedTask;

            /// <summary>模拟断开操作，直接返回已完成任务。</summary>
            public Task DisconnectAsync(CancellationToken cancellationToken) => Task.CompletedTask;

            /// <summary>
            ///     模拟读取操作，记录请求并返回预设的 <see cref="ReadResult" />。
            /// </summary>
            public Task<DataValue> ReadAsync(ReadRequest request, CancellationToken cancellationToken)
            {
                LastReadRequest = request;
                return Task.FromResult(ReadResult);
            }

            /// <summary>模拟批量读取操作（未实现）。</summary>
            public Task<BatchReadResult> ReadManyAsync(IReadOnlyCollection<ReadRequest> requests, CancellationToken cancellationToken)
            {
                throw new NotImplementedException();
            }

            /// <summary>
            ///     模拟写入操作，记录请求并返回已完成任务。
            /// </summary>
            public Task WriteAsync(WriteRequest request, CancellationToken cancellationToken)
            {
                LastWriteRequest = request;
                return Task.CompletedTask;
            }

            /// <summary>模拟批量写入操作（未实现）。</summary>
            public Task WriteManyAsync(IReadOnlyCollection<WriteRequest> requests, CancellationToken cancellationToken)
            {
                throw new NotImplementedException();
            }

            /// <summary>模拟订阅操作（未实现）。</summary>
            public Task<string> SubscribeAsync(SubscriptionRequest request, EventHandler<SubscriptionEvent> handler, CancellationToken cancellationToken)
            {
                throw new NotImplementedException();
            }

            /// <summary>模拟取消订阅操作（未实现）。</summary>
            public Task UnsubscribeAsync(string subscriptionId, CancellationToken cancellationToken)
            {
                throw new NotImplementedException();
            }

            /// <summary>返回模拟的健康快照，连接状态为 <see cref="ConnectionStatus.Connected" />。</summary>
            public HealthSnapshot GetHealth()
            {
                return new HealthSnapshot(ConnectionStatus.Connected, DateTimeOffset.UtcNow, 0, null);
            }

            /// <summary>释放资源（无操作）。</summary>
            public void Dispose()
            {
            }
        }
    }
}
