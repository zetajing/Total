using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Data;
using System.Data.SqlClient;
using System.Globalization;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using IndustrialCommSdk.Abstractions;
using IndustrialCommSdk.Diagnostics;

namespace IndustrialCommSdk.Storage
{
    public sealed partial class SqlServerIndustrialDataStore
    {        public async Task<IReadOnlyList<IndustrialDataRecord>> QueryAsync(HistoryQueryFilter filter, CancellationToken cancellationToken)
        {
            if (filter == null) throw new ArgumentNullException(nameof(filter));
            ValidateFilter(filter);
            if (filter.MaxRows <= 0 || filter.MaxRows > 10000)
            {
                throw new ArgumentOutOfRangeException(nameof(filter), "MaxRows 必须在 1 到 10000 之间。");
            }

            // 动态构建 WHERE 子句，每个条件使用参数化查询防止 SQL 注入。
            var conditions = new List<string>();
            if (!string.IsNullOrWhiteSpace(filter.DeviceId)) conditions.Add("[DeviceId] = @DeviceId");
            if (!string.IsNullOrWhiteSpace(filter.Address)) conditions.Add(filter.AddressMatchMode == HistoryAddressMatchMode.Contains ? "[Address] LIKE @Address ESCAPE '\\'" : "[Address] = @Address");
            if (filter.Protocol.HasValue) conditions.Add("[Protocol] = @Protocol");
            if (filter.DataType.HasValue) conditions.Add("[DataType] = @DataType");
            if (filter.FromTime.HasValue) conditions.Add("[Timestamp] >= @FromTime");
            if (filter.ToTime.HasValue) conditions.Add("[Timestamp] <= @ToTime");
            if (filter.Quality.HasValue) conditions.Add("[Quality] = @Quality");

            var whereClause = conditions.Count > 0 ? "WHERE " + string.Join(" AND ", conditions) : string.Empty;

            var sql = string.Format(
                CultureInfo.InvariantCulture,
                @"SELECT TOP (@MaxRows)
[Id], [Protocol], [DeviceId], [Address], [DataType], [ValueText], [RawData], [Quality], [Timestamp], [ErrorMessage]
FROM {0}
{1}
ORDER BY [Timestamp] DESC;",
                _table.QuotedName,
                whereClause);

            var records = new List<IndustrialDataRecord>(filter.MaxRows);
            using (var connection = new SqlConnection(_options.ConnectionString))
            using (var command = new SqlCommand(sql, connection))
            {
                command.CommandTimeout = _options.CommandTimeoutSeconds;
                command.Parameters.Add("@MaxRows", SqlDbType.Int).Value = filter.MaxRows;
                if (!string.IsNullOrWhiteSpace(filter.DeviceId)) command.Parameters.Add("@DeviceId", SqlDbType.NVarChar, 128).Value = filter.DeviceId;
                if (!string.IsNullOrWhiteSpace(filter.Address)) command.Parameters.Add("@Address", SqlDbType.NVarChar, 260).Value = FormatAddressParameter(filter);
                if (filter.Protocol.HasValue) command.Parameters.Add("@Protocol", SqlDbType.NVarChar, 32).Value = filter.Protocol.Value.ToString();
                if (filter.DataType.HasValue) command.Parameters.Add("@DataType", SqlDbType.NVarChar, 32).Value = filter.DataType.Value.ToString();
                if (filter.FromTime.HasValue) command.Parameters.Add("@FromTime", SqlDbType.DateTimeOffset).Value = filter.FromTime.Value;
                if (filter.ToTime.HasValue) command.Parameters.Add("@ToTime", SqlDbType.DateTimeOffset).Value = filter.ToTime.Value;
                if (filter.Quality.HasValue) command.Parameters.Add("@Quality", SqlDbType.NVarChar, 32).Value = filter.Quality.Value.ToString();

                await connection.OpenAsync(cancellationToken).ConfigureAwait(false);
                using (var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false))
                {
                    while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
                    {
                        records.Add(ReadRecord(reader));
                    }
                }
            }

            return records;
        }

        /// <inheritdoc />
        public async Task<int> DeleteAsync(HistoryQueryFilter filter, CancellationToken cancellationToken)
        {
            if (filter == null) throw new ArgumentNullException(nameof(filter));
            ValidateFilter(filter);

            var conditions = new List<string>();
            if (!string.IsNullOrWhiteSpace(filter.DeviceId)) conditions.Add("[DeviceId] = @DeviceId");
            if (!string.IsNullOrWhiteSpace(filter.Address)) conditions.Add(filter.AddressMatchMode == HistoryAddressMatchMode.Contains ? "[Address] LIKE @Address ESCAPE '\\'" : "[Address] = @Address");
            if (filter.Protocol.HasValue) conditions.Add("[Protocol] = @Protocol");
            if (filter.DataType.HasValue) conditions.Add("[DataType] = @DataType");
            if (filter.FromTime.HasValue) conditions.Add("[Timestamp] >= @FromTime");
            if (filter.ToTime.HasValue) conditions.Add("[Timestamp] <= @ToTime");
            if (filter.Quality.HasValue) conditions.Add("[Quality] = @Quality");

            if (conditions.Count == 0)
            {
                throw new InvalidOperationException("删除操作必须至少指定一个过滤条件，防止误删全部数据。");
            }

            var sql = string.Format(
                CultureInfo.InvariantCulture,
                "DELETE FROM {0} WHERE {1};",
                _table.QuotedName,
                string.Join(" AND ", conditions));

            using (var connection = new SqlConnection(_options.ConnectionString))
            using (var command = new SqlCommand(sql, connection))
            {
                command.CommandTimeout = _options.CommandTimeoutSeconds;
                if (!string.IsNullOrWhiteSpace(filter.DeviceId)) command.Parameters.Add("@DeviceId", SqlDbType.NVarChar, 128).Value = filter.DeviceId;
                if (!string.IsNullOrWhiteSpace(filter.Address)) command.Parameters.Add("@Address", SqlDbType.NVarChar, 260).Value = FormatAddressParameter(filter);
                if (filter.Protocol.HasValue) command.Parameters.Add("@Protocol", SqlDbType.NVarChar, 32).Value = filter.Protocol.Value.ToString();
                if (filter.DataType.HasValue) command.Parameters.Add("@DataType", SqlDbType.NVarChar, 32).Value = filter.DataType.Value.ToString();
                if (filter.FromTime.HasValue) command.Parameters.Add("@FromTime", SqlDbType.DateTimeOffset).Value = filter.FromTime.Value;
                if (filter.ToTime.HasValue) command.Parameters.Add("@ToTime", SqlDbType.DateTimeOffset).Value = filter.ToTime.Value;
                if (filter.Quality.HasValue) command.Parameters.Add("@Quality", SqlDbType.NVarChar, 32).Value = filter.Quality.Value.ToString();

                await connection.OpenAsync(cancellationToken).ConfigureAwait(false);
                return await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
            }
        }

        public async Task<HistoryPageResult> QueryPageAsync(HistoryPageRequest request, CancellationToken cancellationToken)
        {
            if (request == null) throw new ArgumentNullException(nameof(request));
            var filter = request.Filter ?? new HistoryQueryFilter();
            ValidateFilter(filter);
            if (request.PageNumber <= 0) throw new ArgumentOutOfRangeException(nameof(request.PageNumber));
            if (request.PageSize != 50 && request.PageSize != 100 && request.PageSize != 200 && request.PageSize != 1000)
                throw new ArgumentOutOfRangeException(nameof(request.PageSize));

            var where = BuildWhereClause(filter);
            var pageSql = string.Format(CultureInfo.InvariantCulture,
                @"WITH [Filtered] AS
(
    SELECT [Id], ROW_NUMBER() OVER (ORDER BY [Timestamp] DESC, [Id] DESC) AS [RowNumber]
    FROM {0} WITH (HOLDLOCK) {1}
),
[PageRows] AS
(
    SELECT [Id], [RowNumber] FROM [Filtered]
    WHERE [RowNumber] > @Offset AND [RowNumber] <= (@Offset + @PageSize)
),
[Total] AS
(
    SELECT COUNT_BIG(1) AS [TotalCount] FROM [Filtered]
)
SELECT h.[Id], h.[Protocol], h.[DeviceId], h.[Address], h.[DataType], h.[ValueText], h.[RawData],
       h.[Quality], h.[Timestamp], h.[ErrorMessage], t.[TotalCount]
FROM [Total] AS t
LEFT JOIN [PageRows] AS p ON 1 = 1
LEFT JOIN {0} AS h ON h.[Id] = p.[Id]
ORDER BY p.[RowNumber];", _table.QuotedName, where);

            using (var connection = new SqlConnection(_options.ConnectionString))
            using (var command = new SqlCommand(pageSql, connection))
            {
                command.CommandTimeout = _options.CommandTimeoutSeconds;
                AddFilterParameters(command, filter);
                command.Parameters.Add("@Offset", SqlDbType.BigInt).Value =
                    checked((long)(request.PageNumber - 1) * request.PageSize);
                command.Parameters.Add("@PageSize", SqlDbType.Int).Value = request.PageSize;
                await connection.OpenAsync(cancellationToken).ConfigureAwait(false);
                var records = new List<IndustrialDataRecord>(request.PageSize);
                long total = 0;
                using (var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false))
                {
                    while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
                    {
                        total = reader.GetInt64(10);
                        if (!reader.IsDBNull(0)) records.Add(ReadRecord(reader));
                    }
                }
                return new HistoryPageResult { Records = records, TotalCount = total, PageNumber = request.PageNumber, PageSize = request.PageSize };
            }
        }

        public async Task<HistorySummary> GetSummaryAsync(HistoryQueryFilter filter, CancellationToken cancellationToken)
        {
            filter = filter ?? new HistoryQueryFilter();
            ValidateFilter(filter);
            var sql = string.Format(CultureInfo.InvariantCulture,
                @"SELECT COUNT_BIG(1),
SUM(CASE WHEN [Quality]='Good' THEN CAST(1 AS BIGINT) ELSE 0 END), SUM(CASE WHEN [Quality]='Bad' THEN CAST(1 AS BIGINT) ELSE 0 END),
SUM(CASE WHEN [Quality]='Stale' THEN CAST(1 AS BIGINT) ELSE 0 END), SUM(CASE WHEN [Quality]='Unknown' THEN CAST(1 AS BIGINT) ELSE 0 END),
MIN([Timestamp]), MAX([Timestamp]) FROM {0} {1};", _table.QuotedName, BuildWhereClause(filter));
            using (var connection = new SqlConnection(_options.ConnectionString))
            using (var command = new SqlCommand(sql, connection))
            {
                command.CommandTimeout = _options.CommandTimeoutSeconds; AddFilterParameters(command, filter);
                await connection.OpenAsync(cancellationToken).ConfigureAwait(false);
                using (var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false))
                {
                    await reader.ReadAsync(cancellationToken).ConfigureAwait(false);
                    return new HistorySummary {
                        TotalCount = reader.GetInt64(0), GoodCount = reader.IsDBNull(1) ? 0 : Convert.ToInt64(reader.GetValue(1)),
                        BadCount = reader.IsDBNull(2) ? 0 : Convert.ToInt64(reader.GetValue(2)), StaleCount = reader.IsDBNull(3) ? 0 : Convert.ToInt64(reader.GetValue(3)),
                        UnknownCount = reader.IsDBNull(4) ? 0 : Convert.ToInt64(reader.GetValue(4)),
                        EarliestTimestamp = reader.IsDBNull(5) ? (DateTimeOffset?)null : reader.GetDateTimeOffset(5),
                        LatestTimestamp = reader.IsDBNull(6) ? (DateTimeOffset?)null : reader.GetDateTimeOffset(6) };
                }
            }
        }

        public async Task<HistoryFilterOptions> GetFilterOptionsAsync(string deviceId, int maxItems, CancellationToken cancellationToken)
        {
            if (maxItems <= 0 || maxItems > 1000) throw new ArgumentOutOfRangeException(nameof(maxItems));
            var devices = new List<string>(); var addresses = new List<string>();
            using (var connection = new SqlConnection(_options.ConnectionString))
            {
                await connection.OpenAsync(cancellationToken).ConfigureAwait(false);
                using (var command = new SqlCommand(string.Format("SELECT DISTINCT TOP (@Max) [DeviceId] FROM {0} ORDER BY [DeviceId];", _table.QuotedName), connection))
                { command.CommandTimeout = _options.CommandTimeoutSeconds; command.Parameters.Add("@Max", SqlDbType.Int).Value = maxItems; using (var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false)) while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false)) devices.Add(reader.GetString(0)); }
                var addressSql = string.Format("SELECT DISTINCT TOP (@Max) [Address] FROM {0} {1} ORDER BY [Address];", _table.QuotedName, string.IsNullOrWhiteSpace(deviceId) ? "" : "WHERE [DeviceId]=@DeviceId");
                using (var command = new SqlCommand(addressSql, connection))
                { command.CommandTimeout = _options.CommandTimeoutSeconds; command.Parameters.Add("@Max", SqlDbType.Int).Value = maxItems; if (!string.IsNullOrWhiteSpace(deviceId)) command.Parameters.Add("@DeviceId", SqlDbType.NVarChar, 128).Value = deviceId; using (var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false)) while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false)) addresses.Add(reader.GetString(0)); }
            }
            return new HistoryFilterOptions { DeviceIds = devices, Addresses = addresses };
        }

        public async Task<IReadOnlyList<IndustrialDataRecord>> GetLatestValuesAsync(HistoryQueryFilter filter, int maxRows, CancellationToken cancellationToken)
        {
            filter = filter ?? new HistoryQueryFilter(); ValidateFilter(filter);
            if (maxRows <= 0 || maxRows > 1000) throw new ArgumentOutOfRangeException(nameof(maxRows));
            var sql = string.Format(CultureInfo.InvariantCulture,
                @"WITH [Ranked] AS
(
    SELECT [Id], ROW_NUMBER() OVER
        (PARTITION BY [DeviceId], [Address] ORDER BY [Timestamp] DESC, [Id] DESC) AS [rn]
    FROM {0} {1}
)
SELECT TOP (@MaxRows) h.[Id], h.[Protocol], h.[DeviceId], h.[Address], h.[DataType], h.[ValueText],
       h.[RawData], h.[Quality], h.[Timestamp], h.[ErrorMessage]
FROM [Ranked] AS r
INNER JOIN {0} AS h ON h.[Id] = r.[Id]
WHERE r.[rn] = 1
ORDER BY h.[Timestamp] DESC, h.[Id] DESC;", _table.QuotedName, BuildWhereClause(filter));
            var records = new List<IndustrialDataRecord>();
            using (var connection = new SqlConnection(_options.ConnectionString)) using (var command = new SqlCommand(sql, connection))
            { command.CommandTimeout = _options.CommandTimeoutSeconds; command.Parameters.Add("@MaxRows", SqlDbType.Int).Value = maxRows; AddFilterParameters(command, filter); await connection.OpenAsync(cancellationToken).ConfigureAwait(false); using (var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false)) while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false)) records.Add(ReadRecord(reader)); }
            return records;
        }
    }
}
