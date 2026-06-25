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
    [TestFixture]
    public sealed class DatabaseStorageTests
    {
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
            raw[0] = 0;

            Assert.That(record.Protocol, Is.EqualTo(ProtocolKind.ModbusTcp));
            Assert.That(record.DeviceId, Is.EqualTo("device-1"));
            Assert.That(record.Address, Is.EqualTo("D100"));
            Assert.That(record.ValueText, Is.EqualTo("12.5"));
            Assert.That(record.RawData, Is.EqualTo(new byte[] { 0x12, 0x34 }));
        }

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
