using System;
using System.Threading;
using IndustrialCommSdk.Storage;
using IndustrialCommSdk.Storage.MySql;
using NUnit.Framework;

namespace IndustrialCommSdk.Tests
{
    [TestFixture]
    public sealed class MySqlStorageTests
    {
        private const string ConnectionString =
            "Server=127.0.0.1;Port=3306;Database=industrial_test;User ID=test;Password=test;DateTimeKind=Utc;";

        [Test]
        public void Providers_ImplementTheSameHistoryContractWithoutConnecting()
        {
            using (var sqlServer = new SqlServerIndustrialDataStore(new SqlServerDataStoreOptions
            {
                ConnectionString = "Server=localhost;Database=test;Integrated Security=True;",
            }))
            using (var mySql = CreateStore())
            {
                Assert.That(sqlServer, Is.InstanceOf<IIndustrialHistoryStore>());
                Assert.That(mySql, Is.InstanceOf<IIndustrialHistoryStore>());
            }
        }

        [TestCase(null)]
        [TestCase("")]
        [TestCase("   ")]
        public void Options_RejectEmptyConnectionString(string connectionString)
        {
            var options = new MySqlDataStoreOptions { ConnectionString = connectionString };
            Assert.Throws<InvalidOperationException>(() => new MySqlIndustrialDataStore(options));
        }

        [TestCase("history;DROP_TABLE")]
        [TestCase("database.history.extra")]
        [TestCase("database-name.history")]
        [TestCase("database.`history`")]
        [TestCase("1database.history")]
        public void Options_RejectUnsafeTableNames(string tableName)
        {
            var options = new MySqlDataStoreOptions
            {
                ConnectionString = ConnectionString,
                TableName = tableName,
            };
            Assert.Throws<InvalidOperationException>(() => new MySqlIndustrialDataStore(options));
        }

        [TestCase("IndustrialDataHistory")]
        [TestCase("industrial.IndustrialDataHistory")]
        [TestCase("_database._history_01")]
        public void Options_AcceptSafeTableNames(string tableName)
        {
            using (var store = new MySqlIndustrialDataStore(new MySqlDataStoreOptions
            {
                ConnectionString = ConnectionString,
                TableName = tableName,
            }))
            {
                Assert.That(store, Is.Not.Null);
            }
        }

        [Test]
        public void LocalValidation_HappensBeforeAnyDatabaseConnection()
        {
            using (var store = CreateStore())
            {
                Assert.Throws<ArgumentOutOfRangeException>(() =>
                    store.ReadAfterAsync(-1, 100, CancellationToken.None));
                Assert.ThrowsAsync<ArgumentOutOfRangeException>(() =>
                    store.QueryAsync(new HistoryQueryFilter { MaxRows = 0 }, CancellationToken.None));
                Assert.ThrowsAsync<ArgumentException>(() =>
                    store.QueryAsync(new HistoryQueryFilter
                    {
                        FromTime = DateTimeOffset.UtcNow,
                        ToTime = DateTimeOffset.UtcNow.AddMinutes(-1),
                    }, CancellationToken.None));
                Assert.ThrowsAsync<InvalidOperationException>(() =>
                    store.DeleteAsync(new HistoryQueryFilter(), CancellationToken.None));
            }
        }

        [Test]
        public void Initialize_HonorsPreCancelledTokenWithoutConnecting()
        {
            using (var store = CreateStore())
            using (var source = new CancellationTokenSource())
            {
                source.Cancel();
                Assert.ThrowsAsync<OperationCanceledException>(() => store.InitializeAsync(source.Token));
            }
        }

        private static MySqlIndustrialDataStore CreateStore()
        {
            return new MySqlIndustrialDataStore(new MySqlDataStoreOptions
            {
                ConnectionString = ConnectionString,
                TableName = "industrial.IndustrialDataHistory",
            });
        }
    }
}
