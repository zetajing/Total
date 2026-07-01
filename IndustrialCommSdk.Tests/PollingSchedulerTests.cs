using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using IndustrialCommSdk.Abstractions;
using IndustrialCommSdk.Polling;
using NUnit.Framework;

namespace IndustrialCommSdk.Tests
{
    /// <summary>
    ///     对 <see cref="PollingScheduler" /> 轮询调度器的单元测试。
    ///     验证通过轮询方式实现的订阅功能能否在指定周期内触发事件并返回正确的数据值。
    ///     使用 <see cref="FakeIndustrialClient" /> 模拟设备端返回固定数据。
    /// </summary>
    [TestFixture]
    public class PollingSchedulerTests
    {
        /// <summary>
        ///     验证 <see cref="PollingScheduler" /> 在订阅后能按时触发 <see cref="SubscriptionEvent" />。
        ///     订阅周期为 50 毫秒，模拟客户端每次读取返回固定值 42；
        ///     测试等待事件发生，若 1 秒内未触发则视为失败。
        ///     预期：事件被触发，返回的数据值列表中包含一个值为 42 的数据点。
        /// </summary>
        [Test]
        public async Task PollingScheduler_Should_Raise_Subscription_Event()
        {
            var scheduler = new PollingScheduler();
            var client = new FakeIndustrialClient();
            var request = new SubscriptionRequest("sub-1", "dev-1", new[]
            {
                new ReadRequest("dev-1", "addr-1", DataType.UInt16)
            }, TimeSpan.FromMilliseconds(50), false);

            var tcs = new TaskCompletionSource<SubscriptionEvent>();
            await scheduler.SubscribeAsync(client, request, (sender, args) => tcs.TrySetResult(args), CancellationToken.None);

            var completed = await Task.WhenAny(tcs.Task, Task.Delay(1000));
            await scheduler.UnsubscribeAsync("sub-1", CancellationToken.None);

            Assert.That(completed, Is.EqualTo(tcs.Task));
            Assert.That(tcs.Task.Result.Values.Count, Is.EqualTo(1));
            Assert.That(tcs.Task.Result.Values[0].Value, Is.EqualTo(42));
        }

        [Test]
        public async Task PollingScheduler_Should_Detect_ByteArray_Content_Changes()
        {
            using (var scheduler = new PollingScheduler())
            {
                var readCount = 0;
                var client = new FakeIndustrialClient(() =>
                {
                    readCount++;
                    return readCount < 3 ? new byte[] { 0x01 } : new byte[] { 0x02 };
                });
                var request = new SubscriptionRequest("bytes", "dev-1", new[]
                {
                    new ReadRequest("dev-1", "addr-1", DataType.ByteArray)
                }, TimeSpan.FromMilliseconds(20), true);

                var reports = 0;
                var changed = new TaskCompletionSource<byte[]>();
                await scheduler.SubscribeAsync(client, request, (sender, args) =>
                {
                    reports++;
                    var bytes = (byte[])args.Values[0].Value;
                    if (bytes[0] == 0x02) changed.TrySetResult(bytes);
                }, CancellationToken.None);

                var completed = await Task.WhenAny(changed.Task, Task.Delay(1000));
                await scheduler.UnsubscribeAsync("bytes", CancellationToken.None);

                Assert.That(completed, Is.EqualTo(changed.Task));
                Assert.That(reports, Is.EqualTo(2));
            }
        }

        /// <summary>
        ///     用于轮询调度器测试的模拟工业客户端。
        ///     实现 <see cref="IIndustrialClient" /> 接口，<see cref="ReadAsync" /> 方法固定返回值为 42 的 <see cref="DataValue" />。
        /// </summary>
        private sealed class FakeIndustrialClient : IIndustrialClient
        {
            private readonly Func<object> _valueFactory;

            public FakeIndustrialClient(Func<object> valueFactory = null)
            {
                _valueFactory = valueFactory ?? (() => 42);
            }

            /// <summary>固定设备 ID，始终返回 "fake"。</summary>
            public string DeviceId { get { return "fake"; } }

            /// <summary>固定协议类型，始终返回 <see cref="ProtocolKind.TcpSocket" />。</summary>
            public ProtocolKind Kind { get { return ProtocolKind.TcpSocket; } }

            /// <summary>固定连接状态，始终返回 <c>true</c>。</summary>
            public bool IsConnected { get { return true; } }

            /// <summary>模拟连接操作，直接返回已完成任务。</summary>
            public Task ConnectAsync(CancellationToken cancellationToken) { return Task.CompletedTask; }

            /// <summary>模拟断开操作，直接返回已完成任务。</summary>
            public Task DisconnectAsync(CancellationToken cancellationToken) { return Task.CompletedTask; }

            /// <summary>释放资源（无操作）。</summary>
            public void Dispose() { }

            /// <summary>返回模拟的健康快照，连接状态为 <see cref="ConnectionStatus.Connected" />。</summary>
            public HealthSnapshot GetHealth() { return new HealthSnapshot(ConnectionStatus.Connected, DateTimeOffset.UtcNow, 0, null); }

            /// <summary>
            ///     模拟读取操作，返回固定值 42 的 <see cref="DataValue" />。
            /// </summary>
            public Task<DataValue> ReadAsync(ReadRequest request, CancellationToken cancellationToken)
            {
                return Task.FromResult(new DataValue(request.Address, request.DataType, _valueFactory(), new byte[] { 0x00, 0x2A }, QualityStatus.Good, DateTimeOffset.UtcNow, null));
            }

            /// <summary>
            ///     模拟批量读取操作，逐项调用 <see cref="ReadAsync" /> 并聚合结果。
            /// </summary>
            public async Task<BatchReadResult> ReadManyAsync(IReadOnlyCollection<ReadRequest> requests, CancellationToken cancellationToken)
            {
                var values = new List<DataValue>();
                foreach (var request in requests)
                {
                    values.Add(await ReadAsync(request, cancellationToken));
                }
                return new BatchReadResult(values);
            }

            /// <summary>模拟订阅操作（不支持，抛出 <see cref="NotSupportedException" />）。</summary>
            public Task<string> SubscribeAsync(SubscriptionRequest request, EventHandler<SubscriptionEvent> handler, CancellationToken cancellationToken)
            {
                throw new NotSupportedException();
            }

            /// <summary>模拟取消订阅操作（不支持，抛出 <see cref="NotSupportedException" />）。</summary>
            public Task UnsubscribeAsync(string subscriptionId, CancellationToken cancellationToken)
            {
                throw new NotSupportedException();
            }

            /// <summary>模拟写入操作，直接返回已完成任务。</summary>
            public Task WriteAsync(WriteRequest request, CancellationToken cancellationToken) { return Task.CompletedTask; }

            /// <summary>模拟批量写入操作，直接返回已完成任务。</summary>
            public Task WriteManyAsync(IReadOnlyCollection<WriteRequest> requests, CancellationToken cancellationToken) { return Task.CompletedTask; }
        }
    }
}
