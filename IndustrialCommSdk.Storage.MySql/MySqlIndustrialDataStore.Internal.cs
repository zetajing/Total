using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using IndustrialCommSdk.Abstractions;
using IndustrialCommSdk.Storage;
using MySqlConnector;

namespace IndustrialCommSdk.Storage.MySql
{
    public sealed partial class MySqlIndustrialDataStore
    {
        private const string ColumnProjection =
            "`Id`, `Protocol`, `DeviceId`, `Address`, `DataType`, `ValueText`, `RawData`, `Quality`, `TimestampUtc`, `TimestampOffsetMinutes`, `ErrorMessage`";
        private const string QualifiedColumnProjection =
            "`h`.`Id`, `h`.`Protocol`, `h`.`DeviceId`, `h`.`Address`, `h`.`DataType`, `h`.`ValueText`, `h`.`RawData`, `h`.`Quality`, `h`.`TimestampUtc`, `h`.`TimestampOffsetMinutes`, `h`.`ErrorMessage`";

        private static void ValidateFilter(HistoryQueryFilter filter)
        {
            if (filter.FromTime.HasValue && filter.ToTime.HasValue && filter.FromTime.Value > filter.ToTime.Value)
            {
                throw new ArgumentException("开始时间不能晚于结束时间。", nameof(filter));
            }
        }

        private static string BuildWhereClause(HistoryQueryFilter filter)
        {
            var conditions = new List<string>();
            if (!string.IsNullOrWhiteSpace(filter.DeviceId)) conditions.Add("`DeviceId` = @DeviceId");
            if (!string.IsNullOrWhiteSpace(filter.Address))
            {
                conditions.Add(filter.AddressMatchMode == HistoryAddressMatchMode.Contains
                    ? "`Address` LIKE @Address ESCAPE '!'"
                    : "`Address` = @Address");
            }
            if (filter.Protocol.HasValue) conditions.Add("`Protocol` = @Protocol");
            if (filter.DataType.HasValue) conditions.Add("`DataType` = @DataType");
            if (filter.FromTime.HasValue) conditions.Add("`TimestampUtc` >= @FromTime");
            if (filter.ToTime.HasValue) conditions.Add("`TimestampUtc` <= @ToTime");
            if (filter.Quality.HasValue) conditions.Add("`Quality` = @Quality");
            return conditions.Count == 0 ? string.Empty : "WHERE " + string.Join(" AND ", conditions);
        }

        private static void AddFilterParameters(MySqlCommand command, HistoryQueryFilter filter)
        {
            if (!string.IsNullOrWhiteSpace(filter.DeviceId))
                command.Parameters.Add("@DeviceId", MySqlDbType.VarChar, 128).Value = filter.DeviceId;
            if (!string.IsNullOrWhiteSpace(filter.Address))
                command.Parameters.Add("@Address", MySqlDbType.VarChar, 260).Value = FormatAddressParameter(filter);
            if (filter.Protocol.HasValue)
                command.Parameters.Add("@Protocol", MySqlDbType.VarChar, 32).Value = filter.Protocol.Value.ToString();
            if (filter.DataType.HasValue)
                command.Parameters.Add("@DataType", MySqlDbType.VarChar, 32).Value = filter.DataType.Value.ToString();
            if (filter.FromTime.HasValue)
                command.Parameters.Add("@FromTime", MySqlDbType.DateTime).Value = ToDatabaseUtc(filter.FromTime.Value);
            if (filter.ToTime.HasValue)
                command.Parameters.Add("@ToTime", MySqlDbType.DateTime).Value = ToDatabaseUtc(filter.ToTime.Value);
            if (filter.Quality.HasValue)
                command.Parameters.Add("@Quality", MySqlDbType.VarChar, 32).Value = filter.Quality.Value.ToString();
        }

        private static string FormatAddressParameter(HistoryQueryFilter filter)
        {
            if (filter.AddressMatchMode != HistoryAddressMatchMode.Contains) return filter.Address;
            return "%" + filter.Address.Replace("!", "!!").Replace("%", "!%").Replace("_", "!_") + "%";
        }

        private static IndustrialDataRecord ReadRecord(MySqlDataReader reader)
        {
            ProtocolKind protocol;
            DataType dataType;
            QualityStatus quality;
            Enum.TryParse(reader.GetString(1), true, out protocol);
            Enum.TryParse(reader.GetString(4), true, out dataType);
            Enum.TryParse(reader.GetString(7), true, out quality);

            return new IndustrialDataRecord
            {
                Id = reader.GetInt64(0),
                Protocol = protocol,
                DeviceId = reader.GetString(2),
                Address = reader.GetString(3),
                DataType = dataType,
                ValueText = reader.IsDBNull(5) ? null : reader.GetString(5),
                RawData = reader.IsDBNull(6) ? null : (byte[])reader.GetValue(6),
                Quality = quality,
                Timestamp = ReadTimestamp(reader, 8, 9),
                ErrorMessage = reader.IsDBNull(10) ? null : reader.GetString(10),
            };
        }

        private static DateTimeOffset ReadTimestamp(MySqlDataReader reader, int utcOrdinal, int offsetOrdinal)
        {
            var utc = DateTime.SpecifyKind(reader.GetDateTime(utcOrdinal), DateTimeKind.Utc);
            var offset = TimeSpan.FromMinutes(reader.GetInt16(offsetOrdinal));
            return new DateTimeOffset(utc).ToOffset(offset);
        }

        private static DateTime ToDatabaseUtc(DateTimeOffset value)
        {
            // DATETIME 不携带 Kind；按 UTC 墙钟值写入，避免连接字符串的 DateTimeKind 设置转换数值。
            return DateTime.SpecifyKind(value.UtcDateTime, DateTimeKind.Unspecified);
        }

        private async Task<IReadOnlyList<IndustrialDataRecord>> ReadAsync(
            string sql,
            long? afterId,
            int maxRows,
            CancellationToken cancellationToken)
        {
            if (maxRows <= 0 || maxRows > 1000)
                throw new ArgumentOutOfRangeException(nameof(maxRows), "单次查询行数必须在 1 到 1000 之间。");

            var records = new List<IndustrialDataRecord>(maxRows);
            using (var connection = new MySqlConnection(_options.ConnectionString))
            using (var command = new MySqlCommand(sql, connection))
            {
                command.CommandTimeout = _options.CommandTimeoutSeconds;
                command.Parameters.Add("@MaxRows", MySqlDbType.Int32).Value = maxRows;
                if (afterId.HasValue)
                    command.Parameters.Add("@AfterId", MySqlDbType.Int64).Value = afterId.Value;

                await connection.OpenAsync(cancellationToken).ConfigureAwait(false);
                using (var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false))
                {
                    while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
                        records.Add(ReadRecord(reader));
                }
            }
            return records;
        }

        private MySqlCommand CreateInsertCommand(MySqlConnection connection, MySqlTransaction transaction, string sql)
        {
            var command = new MySqlCommand(sql, connection, transaction)
            {
                CommandTimeout = _options.CommandTimeoutSeconds,
            };
            command.Parameters.Add("@Protocol", MySqlDbType.VarChar, 32);
            command.Parameters.Add("@DeviceId", MySqlDbType.VarChar, 128);
            command.Parameters.Add("@Address", MySqlDbType.VarChar, 128);
            command.Parameters.Add("@DataType", MySqlDbType.VarChar, 32);
            command.Parameters.Add("@ValueText", MySqlDbType.LongText);
            command.Parameters.Add("@RawData", MySqlDbType.LongBlob);
            command.Parameters.Add("@Quality", MySqlDbType.VarChar, 32);
            command.Parameters.Add("@TimestampUtc", MySqlDbType.DateTime);
            command.Parameters.Add("@TimestampOffsetMinutes", MySqlDbType.Int16);
            command.Parameters.Add("@ErrorMessage", MySqlDbType.VarChar, 2048);
            return command;
        }

        private static void SetParameterValues(MySqlCommand command, IndustrialDataRecord record)
        {
            if (record == null) throw new ArgumentNullException(nameof(record));
            command.Parameters["@Protocol"].Value = record.Protocol.ToString();
            command.Parameters["@DeviceId"].Value = record.DeviceId ?? string.Empty;
            command.Parameters["@Address"].Value = record.Address ?? string.Empty;
            command.Parameters["@DataType"].Value = record.DataType.ToString();
            command.Parameters["@ValueText"].Value = (object)record.ValueText ?? DBNull.Value;
            command.Parameters["@RawData"].Value = (object)record.RawData ?? DBNull.Value;
            command.Parameters["@Quality"].Value = record.Quality.ToString();
            command.Parameters["@TimestampUtc"].Value = ToDatabaseUtc(record.Timestamp);
            command.Parameters["@TimestampOffsetMinutes"].Value = checked((short)record.Timestamp.Offset.TotalMinutes);
            command.Parameters["@ErrorMessage"].Value = (object)record.ErrorMessage ?? DBNull.Value;
        }

        internal sealed class MySqlTableIdentifier
        {
            private MySqlTableIdentifier(string database, string table)
            {
                Database = database;
                Table = table;
            }

            public string Database { get; private set; }
            public string Table { get; private set; }
            public string QuotedName => string.IsNullOrEmpty(Database)
                ? Quote(Table)
                : Quote(Database) + "." + Quote(Table);

            public string QuoteGeneratedName(string prefix)
            {
                var value = prefix + "_" + Table;
                return Quote(value.Length <= 64 ? value : value.Substring(0, 64));
            }

            public static MySqlTableIdentifier Parse(string value)
            {
                var parts = (value ?? string.Empty).Split('.');
                if (parts.Length == 1 && IsValidPart(parts[0]))
                    return new MySqlTableIdentifier(null, parts[0]);
                if (parts.Length == 2 && IsValidPart(parts[0]) && IsValidPart(parts[1]))
                    return new MySqlTableIdentifier(parts[0], parts[1]);
                throw new InvalidOperationException(
                    "MySQL 历史表名必须使用 table 或 database.table 格式，且只能包含字母、数字和下划线。");
            }

            private static bool IsValidPart(string value)
            {
                return !string.IsNullOrWhiteSpace(value)
                    && value.Length <= 64
                    && (char.IsLetter(value[0]) || value[0] == '_')
                    && value.All(ch => char.IsLetterOrDigit(ch) || ch == '_');
            }

            private static string Quote(string value)
            {
                return "`" + value.Replace("`", "``") + "`";
            }
        }
    }
}
