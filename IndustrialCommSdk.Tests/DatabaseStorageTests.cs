using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using IndustrialCommSdk.Abstractions;
using IndustrialCommSdk.Storage;
using NUnit.Framework;

namespace IndustrialCommSdk.Tests
{
    /// <summary>
    /// 数据库存储功能的单元测试。
    /// 除真实 SQL Server 冒烟验证外，普通单元测试不应依赖开发电脑上是否安装数据库，
    /// 因此后台记录器测试使用内存中的 CapturingDataStore 替代 SQL Server。
    /// </summary>
    [TestFixture]
    public sealed class DatabaseStorageTests
    {
        /// <summary>
        /// 验证数值使用固定区域格式保存，并确认原始字节数组是副本而不是共享引用。
        /// </summary>
        [Test]
        public void DataRecord_UsesInvariantValueAndCopiesRawBytes()
        {
            var raw = new byte[] { 0x12, 0x34 };
            var value = new DataValue(
                "D100",
                DataType.Double,
                12.5d,
                raw,
                QualityStatus.Good,
                new DateTimeOffset(2026, 6, 30, 14, 0, 0, TimeSpan.FromHours(8)),
                null);

            var record = IndustrialDataRecord.FromDataValue(ProtocolKind.ModbusTcp, "device-1", value);
            // 修改输入数组。如果实现正确复制了数据，record.RawData 不会随之变化。
            raw[0] = 0;

            Assert.That(record.Protocol, Is.EqualTo(ProtocolKind.ModbusTcp));
            Assert.That(record.DeviceId, Is.EqualTo("device-1"));
            Assert.That(record.Address, Is.EqualTo("D100"));
            Assert.That(record.ValueText, Is.EqualTo("12.5"));
            Assert.That(record.RawData, Is.EqualTo(new byte[] { 0x12, 0x34 }));
        }

        /// <summary>
        /// 验证危险表名会在执行 SQL 之前被拒绝，防止通过表名注入额外 SQL 语句。
        /// </summary>
        [Test]
        public void SqlServerStore_RejectsUnsafeTableName()
        {
            var options = new SqlServerDataStoreOptions
            {
                ConnectionString = "Server=localhost;Database=UpperComputerDb;Integrated Security=True;",
                TableName = "dbo.History;DROP TABLE Users",
            };

            Assert.Throws<InvalidOperationException>(() => new SqlServerIndustrialDataStore(options));
        }

        [Test]
        public void SqlServerStore_RejectsInvalidHistoryQueryArgumentsBeforeConnecting()
        {
            var store = new SqlServerIndustrialDataStore(new SqlServerDataStoreOptions
            {
                ConnectionString = "Server=localhost;Database=UpperComputerDb;Integrated Security=True;",
                TableName = "dbo.IndustrialDataHistory",
            });

            Assert.ThrowsAsync<ArgumentOutOfRangeException>(async () =>
                await store.ReadLatestAsync(0, CancellationToken.None));
            Assert.Throws<ArgumentOutOfRangeException>(() =>
                store.ReadAfterAsync(-1, 100, CancellationToken.None));
        }

        /// <summary>
        /// 验证停止记录器时会排空队列：即使刚入队就停止，该条记录仍应到达存储实现。
        /// </summary>
        [Test]
        public async Task BufferedRecorder_DrainsQueuedValuesWhenStopped()
        {
            var store = new CapturingDataStore();
            using (var recorder = new BufferedIndustrialDataRecorder(
                store,
                new BufferedDataRecorderOptions { BatchSize = 10, QueueCapacity = 10, RetryCount = 0 }))
            {
                await recorder.StartAsync(CancellationToken.None);
                var value = new DataValue(
                    "DB1.DBD0",
                    DataType.Int32,
                    42,
                    new byte[0],
                    QualityStatus.Good,
                    DateTimeOffset.UtcNow,
                    null);

                Assert.That(recorder.TryRecord(ProtocolKind.SiemensS7, "s7-1", new[] { value }), Is.True);
                await recorder.StopAsync(CancellationToken.None);
            }

            Assert.That(store.Initialized, Is.True);
            Assert.That(store.Records.Single().DeviceId, Is.EqualTo("s7-1"));
            Assert.That(store.Records.Single().ValueText, Is.EqualTo("42"));
        }

        /// <summary>
        /// 仅用于测试的内存存储。它实现与 SQL Server 相同的接口，但把结果放入 List，
        /// 从而让测试快速、可重复，并且不需要外部数据库环境。
        /// </summary>
        private sealed class CapturingDataStore : IIndustrialDataStore
        {
            public bool Initialized { get; private set; }
            public List<IndustrialDataRecord> Records { get; } = new List<IndustrialDataRecord>();

            public Task InitializeAsync(CancellationToken cancellationToken)
            {
                Initialized = true;
                return Task.CompletedTask;
            }

            public Task WriteAsync(IReadOnlyCollection<IndustrialDataRecord> records, CancellationToken cancellationToken)
            {
                Records.AddRange(records);
                return Task.CompletedTask;
            }

            public void Dispose()
            {
            }
        }
    }
}
