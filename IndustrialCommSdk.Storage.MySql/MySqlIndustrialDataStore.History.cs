using System;
using System.Collections.Generic;
using System.Data;
using System.Globalization;
using System.Threading;
using System.Threading.Tasks;
using IndustrialCommSdk.Storage;
using MySqlConnector;

namespace IndustrialCommSdk.Storage.MySql
{
    public sealed partial class MySqlIndustrialDataStore
    {
        /// <inheritdoc />
        public async Task<IReadOnlyList<IndustrialDataRecord>> QueryAsync(
            HistoryQueryFilter filter,
            CancellationToken cancellationToken)
        {
            if (filter == null) throw new ArgumentNullException(nameof(filter));
            ValidateFilter(filter);
            if (filter.MaxRows <= 0 || filter.MaxRows > 10000)
                throw new ArgumentOutOfRangeException(nameof(filter), "MaxRows 必须在 1 到 10000 之间。");

            var sql = string.Format(
                CultureInfo.InvariantCulture,
                "SELECT {0} FROM {1} {2} ORDER BY `TimestampUtc` DESC, `Id` DESC LIMIT @MaxRows;",
                ColumnProjection,
                _table.QuotedName,
                BuildWhereClause(filter));

            return await ReadFilteredAsync(sql, filter, filter.MaxRows, command =>
                command.Parameters.Add("@MaxRows", MySqlDbType.Int32).Value = filter.MaxRows,
                cancellationToken).ConfigureAwait(false);
        }

        /// <inheritdoc />
        public async Task<int> DeleteAsync(HistoryQueryFilter filter, CancellationToken cancellationToken)
        {
            if (filter == null) throw new ArgumentNullException(nameof(filter));
            ValidateFilter(filter);
            var where = BuildWhereClause(filter);
            if (string.IsNullOrEmpty(where))
                throw new InvalidOperationException("删除操作必须至少指定一个过滤条件，防止误删全部数据。");

            var sql = string.Format(
                CultureInfo.InvariantCulture,
                "DELETE FROM {0} {1};",
                _table.QuotedName,
                where);
            using (var connection = new MySqlConnection(_options.ConnectionString))
            using (var command = new MySqlCommand(sql, connection))
            {
                command.CommandTimeout = _options.CommandTimeoutSeconds;
                AddFilterParameters(command, filter);
                await connection.OpenAsync(cancellationToken).ConfigureAwait(false);
                return await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
            }
        }

        /// <inheritdoc />
        public async Task<HistoryPageResult> QueryPageAsync(
            HistoryPageRequest request,
            CancellationToken cancellationToken)
        {
            if (request == null) throw new ArgumentNullException(nameof(request));
            var filter = request.Filter ?? new HistoryQueryFilter();
            ValidateFilter(filter);
            if (request.PageNumber <= 0) throw new ArgumentOutOfRangeException(nameof(request.PageNumber));
            if (request.PageSize != 50 && request.PageSize != 100 &&
                request.PageSize != 200 && request.PageSize != 1000)
                throw new ArgumentOutOfRangeException(nameof(request.PageSize));

            var where = BuildWhereClause(filter);
            var countSql = string.Format(
                CultureInfo.InvariantCulture,
                "SELECT COUNT(*) FROM {0} {1};",
                _table.QuotedName,
                where);
            var pageSql = string.Format(
                CultureInfo.InvariantCulture,
                "SELECT {0} FROM {1} {2} ORDER BY `TimestampUtc` DESC, `Id` DESC LIMIT @PageSize OFFSET @Offset;",
                ColumnProjection,
                _table.QuotedName,
                where);

            using (var connection = new MySqlConnection(_options.ConnectionString))
            {
                await connection.OpenAsync(cancellationToken).ConfigureAwait(false);
                using (var transaction = await connection.BeginTransactionAsync(
                    IsolationLevel.RepeatableRead,
                    true,
                    cancellationToken).ConfigureAwait(false))
                {
                    long total;
                    using (var countCommand = new MySqlCommand(countSql, connection, transaction))
                    {
                        countCommand.CommandTimeout = _options.CommandTimeoutSeconds;
                        AddFilterParameters(countCommand, filter);
                        total = Convert.ToInt64(
                            await countCommand.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false),
                            CultureInfo.InvariantCulture);
                    }

                    var records = new List<IndustrialDataRecord>(request.PageSize);
                    using (var command = new MySqlCommand(pageSql, connection, transaction))
                    {
                        command.CommandTimeout = _options.CommandTimeoutSeconds;
                        AddFilterParameters(command, filter);
                        command.Parameters.Add("@PageSize", MySqlDbType.Int32).Value = request.PageSize;
                        command.Parameters.Add("@Offset", MySqlDbType.Int64).Value =
                            checked((long)(request.PageNumber - 1) * request.PageSize);
                        using (var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false))
                        {
                            while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
                                records.Add(ReadRecord(reader));
                        }
                    }

                    await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
                    return new HistoryPageResult
                    {
                        Records = records,
                        TotalCount = total,
                        PageNumber = request.PageNumber,
                        PageSize = request.PageSize,
                    };
                }
            }
        }

        /// <inheritdoc />
        public async Task<HistorySummary> GetSummaryAsync(
            HistoryQueryFilter filter,
            CancellationToken cancellationToken)
        {
            filter = filter ?? new HistoryQueryFilter();
            ValidateFilter(filter);
            var where = BuildWhereClause(filter);
            var sql = string.Format(
                CultureInfo.InvariantCulture,
                @"WITH `Filtered` AS
(
    SELECT `Id`, `Quality`, `TimestampUtc`, `TimestampOffsetMinutes`
    FROM {0} {1}
)
SELECT COUNT(*),
COALESCE(SUM(CASE WHEN `Quality`='Good' THEN 1 ELSE 0 END), 0),
COALESCE(SUM(CASE WHEN `Quality`='Bad' THEN 1 ELSE 0 END), 0),
COALESCE(SUM(CASE WHEN `Quality`='Stale' THEN 1 ELSE 0 END), 0),
COALESCE(SUM(CASE WHEN `Quality`='Unknown' THEN 1 ELSE 0 END), 0),
(SELECT `TimestampUtc` FROM `Filtered` ORDER BY `TimestampUtc` ASC, `Id` ASC LIMIT 1),
(SELECT `TimestampOffsetMinutes` FROM `Filtered` ORDER BY `TimestampUtc` ASC, `Id` ASC LIMIT 1),
(SELECT `TimestampUtc` FROM `Filtered` ORDER BY `TimestampUtc` DESC, `Id` DESC LIMIT 1),
(SELECT `TimestampOffsetMinutes` FROM `Filtered` ORDER BY `TimestampUtc` DESC, `Id` DESC LIMIT 1)
FROM `Filtered`;",
                _table.QuotedName,
                where);

            using (var connection = new MySqlConnection(_options.ConnectionString))
            using (var command = new MySqlCommand(sql, connection))
            {
                command.CommandTimeout = _options.CommandTimeoutSeconds;
                AddFilterParameters(command, filter);
                await connection.OpenAsync(cancellationToken).ConfigureAwait(false);
                using (var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false))
                {
                    await reader.ReadAsync(cancellationToken).ConfigureAwait(false);
                    var result = new HistorySummary
                    {
                        TotalCount = reader.GetInt64(0),
                        GoodCount = Convert.ToInt64(reader.GetValue(1), CultureInfo.InvariantCulture),
                        BadCount = Convert.ToInt64(reader.GetValue(2), CultureInfo.InvariantCulture),
                        StaleCount = Convert.ToInt64(reader.GetValue(3), CultureInfo.InvariantCulture),
                        UnknownCount = Convert.ToInt64(reader.GetValue(4), CultureInfo.InvariantCulture),
                    };

                    if (!reader.IsDBNull(5)) result.EarliestTimestamp = ReadTimestamp(reader, 5, 6);
                    if (!reader.IsDBNull(7)) result.LatestTimestamp = ReadTimestamp(reader, 7, 8);
                    return result;
                }
            }
        }

        /// <inheritdoc />
        public async Task<HistoryFilterOptions> GetFilterOptionsAsync(
            string deviceId,
            int maxItems,
            CancellationToken cancellationToken)
        {
            if (maxItems <= 0 || maxItems > 1000) throw new ArgumentOutOfRangeException(nameof(maxItems));
            var devices = new List<string>();
            var addresses = new List<string>();
            using (var connection = new MySqlConnection(_options.ConnectionString))
            {
                await connection.OpenAsync(cancellationToken).ConfigureAwait(false);
                var deviceSql = string.Format(
                    CultureInfo.InvariantCulture,
                    "SELECT DISTINCT `DeviceId` FROM {0} ORDER BY `DeviceId` LIMIT @Max;",
                    _table.QuotedName);
                using (var command = new MySqlCommand(deviceSql, connection))
                {
                    command.CommandTimeout = _options.CommandTimeoutSeconds;
                    command.Parameters.Add("@Max", MySqlDbType.Int32).Value = maxItems;
                    using (var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false))
                    {
                        while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
                            devices.Add(reader.GetString(0));
                    }
                }

                var addressSql = string.Format(
                    CultureInfo.InvariantCulture,
                    "SELECT DISTINCT `Address` FROM {0} {1} ORDER BY `Address` LIMIT @Max;",
                    _table.QuotedName,
                    string.IsNullOrWhiteSpace(deviceId) ? string.Empty : "WHERE `DeviceId` = @DeviceId");
                using (var command = new MySqlCommand(addressSql, connection))
                {
                    command.CommandTimeout = _options.CommandTimeoutSeconds;
                    command.Parameters.Add("@Max", MySqlDbType.Int32).Value = maxItems;
                    if (!string.IsNullOrWhiteSpace(deviceId))
                        command.Parameters.Add("@DeviceId", MySqlDbType.VarChar, 128).Value = deviceId;
                    using (var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false))
                    {
                        while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
                            addresses.Add(reader.GetString(0));
                    }
                }
            }
            return new HistoryFilterOptions { DeviceIds = devices, Addresses = addresses };
        }

        /// <inheritdoc />
        public Task<IReadOnlyList<IndustrialDataRecord>> GetLatestValuesAsync(
            HistoryQueryFilter filter,
            int maxRows,
            CancellationToken cancellationToken)
        {
            filter = filter ?? new HistoryQueryFilter();
            ValidateFilter(filter);
            if (maxRows <= 0 || maxRows > 1000) throw new ArgumentOutOfRangeException(nameof(maxRows));
            var sql = string.Format(
                CultureInfo.InvariantCulture,
                @"WITH `Ranked` AS
(
    SELECT `Id`, ROW_NUMBER() OVER
        (PARTITION BY `DeviceId`, `Address` ORDER BY `TimestampUtc` DESC, `Id` DESC) AS `rn`
    FROM {0} {1}
)
SELECT {2}
FROM `Ranked` AS `r`
INNER JOIN {0} AS `h` ON `h`.`Id` = `r`.`Id`
WHERE `r`.`rn` = 1
ORDER BY `h`.`TimestampUtc` DESC, `h`.`Id` DESC LIMIT @MaxRows;",
                _table.QuotedName,
                BuildWhereClause(filter),
                QualifiedColumnProjection);
            return ReadFilteredAsync(sql, filter, maxRows, command =>
                command.Parameters.Add("@MaxRows", MySqlDbType.Int32).Value = maxRows,
                cancellationToken);
        }

        private async Task<IReadOnlyList<IndustrialDataRecord>> ReadFilteredAsync(
            string sql,
            HistoryQueryFilter filter,
            int capacity,
            Action<MySqlCommand> configure,
            CancellationToken cancellationToken)
        {
            var records = new List<IndustrialDataRecord>(capacity);
            using (var connection = new MySqlConnection(_options.ConnectionString))
            using (var command = new MySqlCommand(sql, connection))
            {
                command.CommandTimeout = _options.CommandTimeoutSeconds;
                AddFilterParameters(command, filter);
                configure?.Invoke(command);
                await connection.OpenAsync(cancellationToken).ConfigureAwait(false);
                using (var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false))
                {
                    while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
                        records.Add(ReadRecord(reader));
                }
            }
            return records;
        }

    }
}
