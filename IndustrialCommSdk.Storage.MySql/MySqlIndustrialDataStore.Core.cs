using System;
using System.Collections.Generic;
using System.Globalization;
using System.Threading;
using System.Threading.Tasks;
using IndustrialCommSdk.Storage;
using MySqlConnector;

namespace IndustrialCommSdk.Storage.MySql
{
    /// <summary>使用 MySqlConnector 将工业采集历史保存到 MySQL 8.0+。</summary>
    public sealed partial class MySqlIndustrialDataStore : IIndustrialHistoryStore
    {
        private readonly MySqlDataStoreOptions _options;
        private readonly MySqlTableIdentifier _table;

        /// <summary>创建存储实例。构造函数只校验本地配置，不访问数据库。</summary>
        public MySqlIndustrialDataStore(MySqlDataStoreOptions options)
        {
            _options = options ?? throw new ArgumentNullException(nameof(options));
            _table = options.ValidateAndGetTable();
        }

        /// <inheritdoc />
        public async Task InitializeAsync(CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var sql = string.Format(
                CultureInfo.InvariantCulture,
                @"CREATE TABLE IF NOT EXISTS {0}
(
    `Id` BIGINT NOT NULL AUTO_INCREMENT,
    `Protocol` VARCHAR(32) NOT NULL,
    `DeviceId` VARCHAR(128) NOT NULL,
    `Address` VARCHAR(128) NOT NULL,
    `DataType` VARCHAR(32) NOT NULL,
    `ValueText` LONGTEXT NULL,
    `RawData` LONGBLOB NULL,
    `Quality` VARCHAR(32) NOT NULL,
    `TimestampUtc` DATETIME(6) NOT NULL,
    `TimestampOffsetMinutes` SMALLINT NOT NULL,
    `ErrorMessage` VARCHAR(2048) NULL,
    PRIMARY KEY (`Id`),
    KEY {1} (`TimestampUtc` DESC),
    KEY {2} (`DeviceId`, `Address`, `TimestampUtc` DESC)
) ENGINE=InnoDB DEFAULT CHARACTER SET=utf8mb4 COLLATE=utf8mb4_unicode_ci;",
                _table.QuotedName,
                _table.QuoteGeneratedName("IX_Timestamp"),
                _table.QuoteGeneratedName("IX_Device_Address_Timestamp"));

            using (var connection = new MySqlConnection(_options.ConnectionString))
            using (var command = new MySqlCommand(sql, connection))
            {
                command.CommandTimeout = _options.CommandTimeoutSeconds;
                await connection.OpenAsync(cancellationToken).ConfigureAwait(false);
                await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
            }
        }

        /// <inheritdoc />
        public async Task WriteAsync(IReadOnlyCollection<IndustrialDataRecord> records, CancellationToken cancellationToken)
        {
            if (records == null) throw new ArgumentNullException(nameof(records));
            if (records.Count == 0) return;
            cancellationToken.ThrowIfCancellationRequested();

            var sql = string.Format(
                CultureInfo.InvariantCulture,
                @"INSERT INTO {0}
(`Protocol`, `DeviceId`, `Address`, `DataType`, `ValueText`, `RawData`, `Quality`, `TimestampUtc`, `TimestampOffsetMinutes`, `ErrorMessage`)
VALUES
(@Protocol, @DeviceId, @Address, @DataType, @ValueText, @RawData, @Quality, @TimestampUtc, @TimestampOffsetMinutes, @ErrorMessage);",
                _table.QuotedName);

            using (var connection = new MySqlConnection(_options.ConnectionString))
            {
                await connection.OpenAsync(cancellationToken).ConfigureAwait(false);
                using (var transaction = await connection.BeginTransactionAsync(cancellationToken).ConfigureAwait(false))
                using (var command = CreateInsertCommand(connection, transaction, sql))
                {
                    try
                    {
                        foreach (var record in records)
                        {
                            cancellationToken.ThrowIfCancellationRequested();
                            SetParameterValues(command, record);
                            await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
                        }

                        cancellationToken.ThrowIfCancellationRequested();
                        await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
                    }
                    catch
                    {
                        try
                        {
                            await transaction.RollbackAsync(CancellationToken.None).ConfigureAwait(false);
                        }
                        catch
                        {
                            // 保留原始写入或取消异常。
                        }
                        throw;
                    }
                }
            }
        }

        /// <summary>读取 Id 最大的若干条记录，结果按 Id 降序。</summary>
        public Task<IReadOnlyList<IndustrialDataRecord>> ReadLatestAsync(int maxRows, CancellationToken cancellationToken)
        {
            return ReadAsync(
                string.Format(
                    CultureInfo.InvariantCulture,
                    "SELECT {0} FROM {1} ORDER BY `Id` DESC LIMIT @MaxRows;",
                    ColumnProjection,
                    _table.QuotedName),
                null,
                maxRows,
                cancellationToken);
        }

        /// <summary>读取指定 Id 之后的记录，结果按 Id 升序。</summary>
        public Task<IReadOnlyList<IndustrialDataRecord>> ReadAfterAsync(long afterId, int maxRows, CancellationToken cancellationToken)
        {
            if (afterId < 0) throw new ArgumentOutOfRangeException(nameof(afterId));
            return ReadAsync(
                string.Format(
                    CultureInfo.InvariantCulture,
                    "SELECT {0} FROM {1} WHERE `Id` > @AfterId ORDER BY `Id` ASC LIMIT @MaxRows;",
                    ColumnProjection,
                    _table.QuotedName),
                afterId,
                maxRows,
                cancellationToken);
        }

        /// <inheritdoc />
        public void Dispose()
        {
            // 每次操作都使用独立连接，实例本身不持有需要释放的数据库资源。
        }
    }
}
