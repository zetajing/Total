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
                var snapshot = recorder.GetSnapshot();
                Assert.That(snapshot.AcceptedRecordCount, Is.EqualTo(1));
                Assert.That(snapshot.WrittenRecordCount, Is.EqualTo(1));
                Assert.That(snapshot.DroppedRecordCount, Is.EqualTo(0));
            }

            Assert.That(store.Initialized, Is.True);
            Assert.That(store.Records.Single().DeviceId, Is.EqualTo("s7-1"));
            Assert.That(store.Records.Single().ValueText, Is.EqualTo("42"));
        }

        [Test]
        public async Task BufferedRecorder_Can_Resume_Waiting_After_Stop_Wait_Is_Canceled()
        {
            var store = new BlockingDataStore();
            using (var recorder = new BufferedIndustrialDataRecorder(
                store,
                new BufferedDataRecorderOptions { BatchSize = 10, QueueCapacity = 10, RetryCount = 0 }))
            {
                await recorder.StartAsync(CancellationToken.None);
                var value = new DataValue("D0", DataType.UInt16, (ushort)1, new byte[] { 0, 1 }, QualityStatus.Good, DateTimeOffset.UtcNow, null);
                Assert.That(recorder.TryRecord(ProtocolKind.ModbusTcp, "device", new[] { value }), Is.True);
                await store.WriteStarted.Task;

                using (var cancellation = new CancellationTokenSource())
                {
                    cancellation.Cancel();
                    Assert.ThrowsAsync<OperationCanceledException>(async () =>
                        await recorder.StopAsync(cancellation.Token));
                }

                store.AllowWrite.TrySetResult(true);
                await recorder.StopAsync(CancellationToken.None);
                Assert.That(recorder.GetSnapshot().WrittenRecordCount, Is.EqualTo(1));
            }
        }

        /// <summary>
        /// 验证按设备标识过滤查询能正确返回匹配记录。
        /// </summary>
        [Test]
        public async Task QueryAsync_FiltersByDeviceId()
        {
            var store = new CapturingDataStore();
            await store.WriteAsync(new[]
            {
                CreateRecord("device-1", "D100", DateTimeOffset.UtcNow),
                CreateRecord("device-2", "D200", DateTimeOffset.UtcNow),
                CreateRecord("device-1", "D300", DateTimeOffset.UtcNow),
            }, CancellationToken.None);

            var results = await store.QueryAsync(
                new HistoryQueryFilter { DeviceId = "device-1" },
                CancellationToken.None);

            Assert.That(results.Count, Is.EqualTo(2));
            Assert.That(results.All(r => r.DeviceId == "device-1"), Is.True);
        }

        /// <summary>
        /// 验证按时间范围过滤查询。
        /// </summary>
        [Test]
        public async Task QueryAsync_FiltersByTimeRange()
        {
            var now = DateTimeOffset.UtcNow;
            var store = new CapturingDataStore();
            await store.WriteAsync(new[]
            {
                CreateRecord("d1", "D100", now.AddHours(-2)),
                CreateRecord("d1", "D200", now),
                CreateRecord("d1", "D300", now.AddHours(2)),
            }, CancellationToken.None);

            var results = await store.QueryAsync(
                new HistoryQueryFilter { FromTime = now.AddMinutes(-30), ToTime = now.AddHours(1) },
                CancellationToken.None);

            Assert.That(results.Count, Is.EqualTo(1));
            Assert.That(results[0].Address, Is.EqualTo("D200"));
        }

        [Test]
        public async Task QueryAsync_SupportsAddressContainsAndDataType()
        {
            var store = new CapturingDataStore();
            var first = CreateRecord("d1", "DB1.DBD0", DateTimeOffset.UtcNow); first.DataType = DataType.Int32;
            var second = CreateRecord("d1", "D100", DateTimeOffset.UtcNow);
            await store.WriteAsync(new[] { first, second }, CancellationToken.None);
            var result = await store.QueryAsync(new HistoryQueryFilter { Address = "DBD", AddressMatchMode = HistoryAddressMatchMode.Contains, DataType = DataType.Int32 }, CancellationToken.None);
            Assert.That(result.Select(x => x.Address), Is.EqualTo(new[] { "DB1.DBD0" }));
        }

        [Test]
        public void ManagementQuery_RejectsInvalidTimeRangeBeforeConnecting()
        {
            var store = new SqlServerIndustrialDataStore(new SqlServerDataStoreOptions { ConnectionString = "Server=localhost;Database=x;Integrated Security=True;" });
            Assert.ThrowsAsync<ArgumentException>(async () => await store.QueryPageAsync(new HistoryPageRequest {
                Filter = new HistoryQueryFilter { FromTime = DateTimeOffset.UtcNow, ToTime = DateTimeOffset.UtcNow.AddDays(-1) }
            }, CancellationToken.None));
        }

        /// <summary>
        /// 验证删除操作只删除匹配的记录。
        /// </summary>
        [Test]
        public async Task DeleteAsync_RemovesMatchingRecords()
        {
            var store = new CapturingDataStore();
            await store.WriteAsync(new[]
            {
                CreateRecord("device-1", "D100", DateTimeOffset.UtcNow),
                CreateRecord("device-2", "D200", DateTimeOffset.UtcNow),
                CreateRecord("device-1", "D300", DateTimeOffset.UtcNow),
            }, CancellationToken.None);

            var removed = await store.DeleteAsync(
                new HistoryQueryFilter { DeviceId = "device-1" },
                CancellationToken.None);

            Assert.That(removed, Is.EqualTo(2));
            Assert.That(store.Records.Count, Is.EqualTo(1));
            Assert.That(store.Records[0].DeviceId, Is.EqualTo("device-2"));
        }

        /// <summary>
        /// 验证 CSV 导出包含正确的表头和数据行。
        /// </summary>
        [Test]
        public void CsvExporter_ProducesCorrectHeaderAndData()
        {
            var records = new[]
            {
                CreateRecord("device-1", "D100", new DateTimeOffset(2026, 6, 30, 14, 0, 0, TimeSpan.Zero)),
            };

            var bytes = CsvHistoryExporter.ExportToBytes(records);
            var text = System.Text.Encoding.UTF8.GetString(bytes);

            // 验证 BOM 存在
            Assert.That(bytes[0], Is.EqualTo(0xEF));
            Assert.That(bytes[1], Is.EqualTo(0xBB));
            Assert.That(bytes[2], Is.EqualTo(0xBF));

            var lines = text.Split(new[] { "\r\n", "\n" }, StringSplitOptions.RemoveEmptyEntries);
            Assert.That(lines.Length, Is.EqualTo(2)); // 表头 + 1 行数据
            Assert.That(lines[0], Does.StartWith("Id,Protocol,DeviceId,Address,DataType"));
            Assert.That(lines[1], Does.Contain("device-1"));
            Assert.That(lines[1], Does.Contain("D100"));
        }

        /// <summary>
        /// 验证 CSV 导出正确处理含逗号/引号的字段。
        /// </summary>
        [Test]
        public void CsvExporter_EscapesSpecialCharacters()
        {
            var records = new[]
            {
                CreateRecord("dev,ice", "D\"100", new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero)),
            };

            var bytes = CsvHistoryExporter.ExportToBytes(records);
            var text = System.Text.Encoding.UTF8.GetString(bytes);
            var lines = text.Split(new[] { "\r\n", "\n" }, StringSplitOptions.RemoveEmptyEntries);

            // 含逗号的字段应被引号包裹
            Assert.That(lines[1], Does.Contain("\"dev,ice\""));
            // 含引号的字段中双引号应加倍
            Assert.That(lines[1], Does.Contain("\"D\"\"100\""));
        }

        [Test]
        public async Task CsvExporter_AppendsBatchesWithSingleHeader()
        {
            using (var stream = new System.IO.MemoryStream())
            {
                await CsvHistoryExporter.WriteBatchAsync(new[] { CreateRecord("d1", "D1", DateTimeOffset.UtcNow) }, stream, true, CancellationToken.None);
                await CsvHistoryExporter.WriteBatchAsync(new[] { CreateRecord("d2", "D2", DateTimeOffset.UtcNow) }, stream, false, CancellationToken.None);
                var text = System.Text.Encoding.UTF8.GetString(stream.ToArray());
                Assert.That(text.Split(new[] { "Id,Protocol" }, StringSplitOptions.None).Length - 1, Is.EqualTo(1));
                Assert.That(text, Does.Contain("d1")); Assert.That(text, Does.Contain("d2"));
            }
        }

        private static IndustrialDataRecord CreateRecord(string deviceId, string address, DateTimeOffset timestamp)
        {
            return new IndustrialDataRecord
            {
                Protocol = ProtocolKind.ModbusTcp,
                DeviceId = deviceId,
                Address = address,
                DataType = DataType.Int16,
                ValueText = "100",
                Quality = QualityStatus.Good,
                Timestamp = timestamp,
            };
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

            public Task<IReadOnlyList<IndustrialDataRecord>> QueryAsync(HistoryQueryFilter filter, CancellationToken cancellationToken)
            {
                IEnumerable<IndustrialDataRecord> query = Records;
                if (!string.IsNullOrWhiteSpace(filter.DeviceId))
                    query = query.Where(r => r.DeviceId == filter.DeviceId);
                if (!string.IsNullOrWhiteSpace(filter.Address))
                    query = filter.AddressMatchMode == HistoryAddressMatchMode.Contains ? query.Where(r => r.Address.Contains(filter.Address)) : query.Where(r => r.Address == filter.Address);
                if (filter.Protocol.HasValue)
                    query = query.Where(r => r.Protocol == filter.Protocol.Value);
                if (filter.DataType.HasValue)
                    query = query.Where(r => r.DataType == filter.DataType.Value);
                if (filter.FromTime.HasValue)
                    query = query.Where(r => r.Timestamp >= filter.FromTime.Value);
                if (filter.ToTime.HasValue)
                    query = query.Where(r => r.Timestamp <= filter.ToTime.Value);
                if (filter.Quality.HasValue)
                    query = query.Where(r => r.Quality == filter.Quality.Value);

                var result = query
                    .OrderByDescending(r => r.Timestamp)
                    .Take(filter.MaxRows)
                    .ToList();

                return Task.FromResult<IReadOnlyList<IndustrialDataRecord>>(result);
            }

            public Task<int> DeleteAsync(HistoryQueryFilter filter, CancellationToken cancellationToken)
            {
                int removed;
                if (!string.IsNullOrWhiteSpace(filter.DeviceId))
                    removed = Records.RemoveAll(r => r.DeviceId == filter.DeviceId);
                else if (!string.IsNullOrWhiteSpace(filter.Address))
                    removed = Records.RemoveAll(r => r.Address == filter.Address);
                else
                    throw new InvalidOperationException("删除操作必须至少指定一个过滤条件。");

                return Task.FromResult(removed);
            }

            public void Dispose()
            {
            }
        }

        private sealed class BlockingDataStore : IIndustrialDataStore
        {
            public TaskCompletionSource<bool> WriteStarted { get; } = new TaskCompletionSource<bool>();
            public TaskCompletionSource<bool> AllowWrite { get; } = new TaskCompletionSource<bool>();
            public Task InitializeAsync(CancellationToken cancellationToken) { return Task.CompletedTask; }
            public async Task WriteAsync(IReadOnlyCollection<IndustrialDataRecord> records, CancellationToken cancellationToken)
            {
                WriteStarted.TrySetResult(true);
                await AllowWrite.Task.ConfigureAwait(false);
            }
            public Task<IReadOnlyList<IndustrialDataRecord>> QueryAsync(HistoryQueryFilter filter, CancellationToken cancellationToken)
            {
                return Task.FromResult<IReadOnlyList<IndustrialDataRecord>>(new IndustrialDataRecord[0]);
            }
            public Task<int> DeleteAsync(HistoryQueryFilter filter, CancellationToken cancellationToken) { return Task.FromResult(0); }
            public void Dispose() { }
        }
    }
}
