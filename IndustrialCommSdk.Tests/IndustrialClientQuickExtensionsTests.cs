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
        ///     验证 <c>ReadAsync&lt;T&gt;</c> 能根据泛型类型自动推断读取数据类型。
        /// </summary>
        [Test]
        public async Task ReadAsync_Generic_Should_Infer_DataType()
        {
            var client = new FakeIndustrialClient
            {
                ReadResult = new DataValue("D100", DataType.Int32, 456, null, QualityStatus.Good, DateTimeOffset.UtcNow, null)
            };

            var value = await client.ReadAsync<int>("D100");

            Assert.That(value, Is.EqualTo(456));
            Assert.That(client.LastReadRequest.DataType, Is.EqualTo(DataType.Int32));
            Assert.That(client.LastReadRequest.Length, Is.EqualTo(1));
        }

        /// <summary>
        ///     验证 <c>ReadAsync&lt;T&gt;</c> 的长度重载适用于字符串、字节数组等变长读取。
        /// </summary>
        [Test]
        public async Task ReadAsync_Generic_With_Length_Should_Use_Length()
        {
            var client = new FakeIndustrialClient
            {
                ReadResult = new DataValue("D200", DataType.String, "ABC", null, QualityStatus.Good, DateTimeOffset.UtcNow, null)
            };

            var value = await client.ReadAsync<string>("D200", 8);

            Assert.That(value, Is.EqualTo("ABC"));
            Assert.That(client.LastReadRequest.DataType, Is.EqualTo(DataType.String));
            Assert.That(client.LastReadRequest.Length, Is.EqualTo(8));
        }

        [Test]
        public async Task ReadAsync_Tag_Should_Use_Tag_Metadata()
        {
            var tag = Tag.Float("D200", "temperature");
            var client = new FakeIndustrialClient
            {
                ReadResult = new DataValue("D200", DataType.Float, 12.5f, null, QualityStatus.Good, DateTimeOffset.UtcNow, null)
            };

            var value = await client.ReadAsync(tag);

            Assert.That(value, Is.EqualTo(12.5f));
            Assert.That(client.LastReadRequest.Address, Is.EqualTo("D200"));
            Assert.That(client.LastReadRequest.DataType, Is.EqualTo(DataType.Float));
            Assert.That(client.LastReadRequest.Length, Is.EqualTo(1));
        }

        [Test]
        public async Task TryReadAsync_Should_Return_Failure_Without_Throwing()
        {
            var client = new FakeIndustrialClient
            {
                ReadResult = new DataValue("D100", DataType.Int16, null, null, QualityStatus.Bad, DateTimeOffset.UtcNow, "read failed")
            };

            var result = await client.TryReadAsync<short>("D100");

            Assert.That(result.IsSuccess, Is.False);
            Assert.That(result.ErrorMessage, Is.EqualTo("read failed"));
            Assert.That(result.DataValue, Is.SameAs(client.ReadResult));
        }

        [Test]
        public async Task ReadManyAsync_Tags_Should_Build_Requests_And_Return_Tag_Result()
        {
            var speed = Tag.Int16("D100", "speed");
            var temperature = Tag.Float("D200", "temperature");
            var client = new FakeIndustrialClient
            {
                ReadManyResult = new BatchReadResult(new[]
                {
                    new DataValue("D100", DataType.Int16, (short)123, null, QualityStatus.Good, DateTimeOffset.UtcNow, null),
                    new DataValue("D200", DataType.Float, 45.5f, null, QualityStatus.Good, DateTimeOffset.UtcNow, null),
                })
            };

            var result = await client.ReadManyAsync(speed, temperature);

            Assert.That(client.LastReadManyRequests.Count, Is.EqualTo(2));
            Assert.That(client.LastReadManyRequests[0].Address, Is.EqualTo("D100"));
            Assert.That(client.LastReadManyRequests[0].DataType, Is.EqualTo(DataType.Int16));
            Assert.That(client.LastReadManyRequests[1].Address, Is.EqualTo("D200"));
            Assert.That(client.LastReadManyRequests[1].DataType, Is.EqualTo(DataType.Float));
            Assert.That(result.Get(speed), Is.EqualTo(123));
            Assert.That(result.Get(temperature), Is.EqualTo(45.5f));
        }

        [Test]
        public async Task ReadManyAsync_Generic_Should_Read_Same_Type_Addresses()
        {
            var client = new FakeIndustrialClient
            {
                ReadManyResult = new BatchReadResult(new[]
                {
                    new DataValue("D100", DataType.Int32, 100, null, QualityStatus.Good, DateTimeOffset.UtcNow, null),
                    new DataValue("D101", DataType.Int32, 101, null, QualityStatus.Good, DateTimeOffset.UtcNow, null),
                })
            };

            var result = await client.ReadManyAsync<int>("D100", "D101");

            Assert.That(client.LastReadManyRequests.Count, Is.EqualTo(2));
            Assert.That(client.LastReadManyRequests[0].DataType, Is.EqualTo(DataType.Int32));
            Assert.That(client.LastReadManyRequests[1].DataType, Is.EqualTo(DataType.Int32));
            Assert.That(result["D100"], Is.EqualTo(100));
            Assert.That(result["D101"], Is.EqualTo(101));
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
        ///     验证 <c>WriteAsync(string)</c> 能自动用字符串长度构造写入请求。
        /// </summary>
        [Test]
        public async Task WriteAsync_String_Should_Infer_Length()
        {
            var client = new FakeIndustrialClient();

            await client.WriteAsync("D300", "ABC");

            Assert.That(client.LastWriteRequest.DataType, Is.EqualTo(DataType.String));
            Assert.That(client.LastWriteRequest.Value, Is.EqualTo("ABC"));
            Assert.That(client.LastWriteRequest.Length, Is.EqualTo(3));
        }

        /// <summary>
        ///     验证 <c>WriteAsync(byte[])</c> 能自动用数组长度构造写入请求。
        /// </summary>
        [Test]
        public async Task WriteAsync_ByteArray_Should_Infer_Length()
        {
            var client = new FakeIndustrialClient();
            var bytes = new byte[] { 1, 2, 3, 4 };

            await client.WriteAsync("D400", bytes);

            Assert.That(client.LastWriteRequest.DataType, Is.EqualTo(DataType.ByteArray));
            Assert.That(client.LastWriteRequest.Value, Is.SameAs(bytes));
            Assert.That(client.LastWriteRequest.Length, Is.EqualTo(4));
        }

        [Test]
        public async Task WriteAsync_Tag_Should_Use_Tag_Metadata()
        {
            var tag = Tag.UInt16("HR0");
            var client = new FakeIndustrialClient();

            await client.WriteAsync(tag, (ushort)10);

            Assert.That(client.LastWriteRequest.Address, Is.EqualTo("HR0"));
            Assert.That(client.LastWriteRequest.DataType, Is.EqualTo(DataType.UInt16));
            Assert.That(client.LastWriteRequest.Value, Is.EqualTo(10));
        }

        [Test]
        public async Task WriteManyAsync_Should_Build_Write_Requests_From_Tag_Values()
        {
            var speed = Tag.Int16("D100");
            var enabled = Tag.Bool("M10");
            var client = new FakeIndustrialClient();

            await client.WriteManyAsync(speed.WithValue(123), enabled.WithValue(true));

            Assert.That(client.LastWriteManyRequests.Count, Is.EqualTo(2));
            Assert.That(client.LastWriteManyRequests[0].Address, Is.EqualTo("D100"));
            Assert.That(client.LastWriteManyRequests[0].DataType, Is.EqualTo(DataType.Int16));
            Assert.That(client.LastWriteManyRequests[0].Value, Is.EqualTo(123));
            Assert.That(client.LastWriteManyRequests[1].Address, Is.EqualTo("M10"));
            Assert.That(client.LastWriteManyRequests[1].DataType, Is.EqualTo(DataType.Bool));
            Assert.That(client.LastWriteManyRequests[1].Value, Is.EqualTo(true));
        }

        [Test]
        public async Task WriteManyAsync_Generic_Should_Write_Same_Type_Addresses()
        {
            var client = new FakeIndustrialClient();
            var values = new Dictionary<string, int>
            {
                ["D100"] = 100,
                ["D101"] = 101,
            };

            await client.WriteManyAsync(values);

            Assert.That(client.LastWriteManyRequests.Count, Is.EqualTo(2));
            Assert.That(client.LastWriteManyRequests[0].Address, Is.EqualTo("D100"));
            Assert.That(client.LastWriteManyRequests[0].DataType, Is.EqualTo(DataType.Int32));
            Assert.That(client.LastWriteManyRequests[0].Value, Is.EqualTo(100));
            Assert.That(client.LastWriteManyRequests[1].Address, Is.EqualTo("D101"));
            Assert.That(client.LastWriteManyRequests[1].DataType, Is.EqualTo(DataType.Int32));
            Assert.That(client.LastWriteManyRequests[1].Value, Is.EqualTo(101));
        }

        [Test]
        public async Task UseAsync_Should_Connect_Disconnect_And_Dispose()
        {
            var client = new FakeIndustrialClient();
            var called = false;

            await client.UseAsync(async c =>
            {
                called = true;
                Assert.That(c.IsConnected, Is.True);
                await Task.CompletedTask;
            });

            Assert.That(called, Is.True);
            Assert.That(client.ConnectCount, Is.EqualTo(1));
            Assert.That(client.DisconnectCount, Is.EqualTo(1));
            Assert.That(client.DisposeCount, Is.EqualTo(1));
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
            public bool IsConnected { get; private set; } = true;

            public int ConnectCount { get; private set; }

            public int DisconnectCount { get; private set; }

            public int DisposeCount { get; private set; }

            /// <summary>记录最后一次 <see cref="ReadAsync" /> 调用的请求参数。</summary>
            public ReadRequest LastReadRequest { get; private set; }

            /// <summary>记录最后一次 <see cref="WriteAsync" /> 调用的请求参数。</summary>
            public WriteRequest LastWriteRequest { get; private set; }

            public List<ReadRequest> LastReadManyRequests { get; private set; }

            public List<WriteRequest> LastWriteManyRequests { get; private set; }

            /// <summary>预设的读取结果，由各测试方法在调用前设置。</summary>
            public DataValue ReadResult { get; set; }

            public BatchReadResult ReadManyResult { get; set; }

            /// <summary>模拟连接操作，直接返回已完成任务。</summary>
            public Task ConnectAsync(CancellationToken cancellationToken)
            {
                ConnectCount++;
                IsConnected = true;
                return Task.CompletedTask;
            }

            /// <summary>模拟断开操作，直接返回已完成任务。</summary>
            public Task DisconnectAsync(CancellationToken cancellationToken)
            {
                DisconnectCount++;
                IsConnected = false;
                return Task.CompletedTask;
            }

            /// <summary>
            ///     模拟读取操作，记录请求并返回预设的 <see cref="ReadResult" />。
            /// </summary>
            public Task<DataValue> ReadAsync(ReadRequest request, CancellationToken cancellationToken)
            {
                LastReadRequest = request;
                return Task.FromResult(ReadResult);
            }

            /// <summary>模拟批量读取操作。</summary>
            public Task<BatchReadResult> ReadManyAsync(IReadOnlyCollection<ReadRequest> requests, CancellationToken cancellationToken)
            {
                LastReadManyRequests = new List<ReadRequest>(requests);
                return Task.FromResult(ReadManyResult);
            }

            /// <summary>
            ///     模拟写入操作，记录请求并返回已完成任务。
            /// </summary>
            public Task WriteAsync(WriteRequest request, CancellationToken cancellationToken)
            {
                LastWriteRequest = request;
                return Task.CompletedTask;
            }

            /// <summary>模拟批量写入操作。</summary>
            public Task WriteManyAsync(IReadOnlyCollection<WriteRequest> requests, CancellationToken cancellationToken)
            {
                LastWriteManyRequests = new List<WriteRequest>(requests);
                return Task.CompletedTask;
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
                DisposeCount++;
            }
        }
    }
}
