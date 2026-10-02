using System;
using System.Collections.Generic;
using System.Data.Common;
using System.Data.SqlClient;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using InduLink.Abstractions;
using InduLink.Storage;
using InduLink.Storage.MySql;
using MySqlConnector;
using NUnit.Framework;

namespace InduLink.Tests
{
    [TestFixture, Category("DatabaseIntegration")]
    public sealed class DatabaseServerIntegrationTests
    {
        [TestCase("mysql")]
        [TestCase("sqlserver")]
        public async Task HistoryRoundTripsTimestampOffsetAndRawBytes(string provider)
        {
            await using var fixture = await DatabaseFixture.CreateAsync(provider);
            var record = Record();
            await fixture.Store.WriteAsync(new[] { record }, CancellationToken.None);
            var rows = await fixture.Store.QueryAsync(new HistoryQueryFilter { DeviceId = record.DeviceId }, CancellationToken.None);
            Assert.That(rows.Count, Is.EqualTo(1));
            Assert.That(rows[0].Timestamp, Is.EqualTo(record.Timestamp));
            Assert.That(rows[0].Timestamp.Offset, Is.EqualTo(record.Timestamp.Offset));
            Assert.That(rows[0].RawData, Is.EqualTo(record.RawData));
            Assert.That(rows[0].ValueText, Is.EqualTo(record.ValueText));
            var page = await fixture.Store.QueryPageAsync(new HistoryPageRequest { Filter = new HistoryQueryFilter { DeviceId = record.DeviceId }, PageSize = 50 }, CancellationToken.None);
            Assert.That(page.Records.Count, Is.EqualTo(1));
        }

        [TestCase("mysql")]
        [TestCase("sqlserver")]
        public async Task BatchFailureRollsBackPreviouslyInsertedRows(string provider)
        {
            await using var fixture = await DatabaseFixture.CreateAsync(provider);
            Assert.CatchAsync<Exception>(() => fixture.Store.WriteAsync(new[] { Record(), null }, CancellationToken.None));
            Assert.That(await fixture.Store.QueryAsync(new HistoryQueryFilter(), CancellationToken.None), Is.Empty);
        }

        [TestCase("mysql")]
        [TestCase("sqlserver")]
        public async Task CancellationAfterFirstInsertRollsBackAndStoreRemainsUsable(string provider)
        {
            await using var fixture = await DatabaseFixture.CreateAsync(provider);
            using var cancellation = new CancellationTokenSource();
            Assert.CatchAsync<OperationCanceledException>(() => fixture.Store.WriteAsync(new CancelAfterFirstRecord(cancellation), cancellation.Token));
            Assert.That(await fixture.Store.QueryAsync(new HistoryQueryFilter(), CancellationToken.None), Is.Empty);
            await fixture.Store.WriteAsync(new[] { Record() }, CancellationToken.None);
            Assert.That((await fixture.Store.QueryAsync(new HistoryQueryFilter(), CancellationToken.None)).Count, Is.EqualTo(1));
        }

        private static InduLinkDataRecord Record() => new InduLinkDataRecord
        {
            Protocol = ProtocolKind.SiemensS7, DeviceId = "integration-plc", Address = "DB1.DBD8", DataType = DataType.Float,
            ValueText = "12.5", RawData = new byte[] { 0x41, 0x48, 0, 0 }, Quality = QualityStatus.Good,
            Timestamp = new DateTimeOffset(2026, 1, 2, 3, 4, 5, TimeSpan.FromHours(8)).AddTicks(1234560),
        };

        private sealed class CancelAfterFirstRecord : IReadOnlyCollection<InduLinkDataRecord>
        {
            private readonly CancellationTokenSource _cancellation;
            public CancelAfterFirstRecord(CancellationTokenSource cancellation) { _cancellation = cancellation; }
            public int Count => 2;
            public IEnumerator<InduLinkDataRecord> GetEnumerator() { yield return Record(); _cancellation.Cancel(); yield return Record(); }
            System.Collections.IEnumerator System.Collections.IEnumerable.GetEnumerator() => GetEnumerator();
        }

        private sealed class DatabaseFixture : IAsyncDisposable
        {
            private readonly string _provider;
            private readonly string _connectionString;
            private readonly string _table = "InduLinkIntegration_" + Guid.NewGuid().ToString("N");
            public IInduLinkHistoryStore Store { get; private set; }
            private DatabaseFixture(string provider, string connectionString) { _provider = provider; _connectionString = connectionString; }
            public static async Task<DatabaseFixture> CreateAsync(string provider)
            {
                var variable = provider == "mysql" ? "INDULINK_TEST_MYSQL" : "INDULINK_TEST_SQLSERVER";
                var connectionString = Environment.GetEnvironmentVariable(variable);
                if (string.IsNullOrWhiteSpace(connectionString)) Assert.Ignore("Set " + variable + " to use a disposable integration database.");
                var fixture = new DatabaseFixture(provider, connectionString);
                fixture.Store = provider == "mysql"
                    ? (IInduLinkHistoryStore)new MySqlInduLinkDataStore(new MySqlDataStoreOptions { ConnectionString = connectionString, TableName = fixture._table })
                    : new SqlServerInduLinkDataStore(new SqlServerDataStoreOptions { ConnectionString = connectionString, TableName = "dbo." + fixture._table });
                try { await fixture.Store.InitializeAsync(CancellationToken.None); return fixture; }
                catch { await fixture.DisposeAsync(); throw; }
            }
            public async ValueTask DisposeAsync()
            {
                try
                {
                    using DbConnection connection = _provider == "mysql" ? new MySqlConnection(_connectionString) : new SqlConnection(_connectionString);
                    await connection.OpenAsync();
                    using var command = connection.CreateCommand();
                    command.CommandText = _provider == "mysql" ? "DROP TABLE IF EXISTS `" + _table + "`;" : "DROP TABLE IF EXISTS [dbo].[" + _table + "];";
                    await command.ExecuteNonQueryAsync();
                }
                finally { Store?.Dispose(); }
            }
        }
    }
}
