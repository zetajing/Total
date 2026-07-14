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
    {        private static void ValidateFilter(HistoryQueryFilter filter)
        {
            if (filter.FromTime.HasValue && filter.ToTime.HasValue && filter.FromTime.Value > filter.ToTime.Value)
                throw new ArgumentException("开始时间不能晚于结束时间。", nameof(filter));
        }

        private static string BuildWhereClause(HistoryQueryFilter filter)
        {
            var conditions = new List<string>();
            if (!string.IsNullOrWhiteSpace(filter.DeviceId)) conditions.Add("[DeviceId]=@DeviceId");
            if (!string.IsNullOrWhiteSpace(filter.Address)) conditions.Add(filter.AddressMatchMode == HistoryAddressMatchMode.Contains ? "[Address] LIKE @Address ESCAPE '\\'" : "[Address]=@Address");
            if (filter.Protocol.HasValue) conditions.Add("[Protocol]=@Protocol");
            if (filter.DataType.HasValue) conditions.Add("[DataType]=@DataType");
            if (filter.FromTime.HasValue) conditions.Add("[Timestamp]>=@FromTime");
            if (filter.ToTime.HasValue) conditions.Add("[Timestamp]<=@ToTime");
            if (filter.Quality.HasValue) conditions.Add("[Quality]=@Quality");
            return conditions.Count == 0 ? string.Empty : "WHERE " + string.Join(" AND ", conditions);
        }

        private static void AddFilterParameters(SqlCommand command, HistoryQueryFilter filter)
        {
            if (!string.IsNullOrWhiteSpace(filter.DeviceId)) command.Parameters.Add("@DeviceId", SqlDbType.NVarChar, 128).Value = filter.DeviceId;
            if (!string.IsNullOrWhiteSpace(filter.Address)) command.Parameters.Add("@Address", SqlDbType.NVarChar, 260).Value = FormatAddressParameter(filter);
            if (filter.Protocol.HasValue) command.Parameters.Add("@Protocol", SqlDbType.NVarChar, 32).Value = filter.Protocol.Value.ToString();
            if (filter.DataType.HasValue) command.Parameters.Add("@DataType", SqlDbType.NVarChar, 32).Value = filter.DataType.Value.ToString();
            if (filter.FromTime.HasValue) command.Parameters.Add("@FromTime", SqlDbType.DateTimeOffset).Value = filter.FromTime.Value;
            if (filter.ToTime.HasValue) command.Parameters.Add("@ToTime", SqlDbType.DateTimeOffset).Value = filter.ToTime.Value;
            if (filter.Quality.HasValue) command.Parameters.Add("@Quality", SqlDbType.NVarChar, 32).Value = filter.Quality.Value.ToString();
        }

        private static string FormatAddressParameter(HistoryQueryFilter filter)
        {
            if (filter.AddressMatchMode != HistoryAddressMatchMode.Contains) return filter.Address;
            return "%" + filter.Address.Replace("\\", "\\\\").Replace("%", "\\%").Replace("_", "\\_").Replace("[", "\\[") + "%";
        }

        private static IndustrialDataRecord ReadRecord(SqlDataReader reader)
        {
            ProtocolKind protocol; DataType dataType; QualityStatus quality;
            Enum.TryParse(reader.GetString(1), true, out protocol); Enum.TryParse(reader.GetString(4), true, out dataType); Enum.TryParse(reader.GetString(6), true, out quality);
            return new IndustrialDataRecord { Id = reader.GetInt64(0), Protocol = protocol, DeviceId = reader.GetString(2), Address = reader.GetString(3), DataType = dataType,
                ValueText = reader.IsDBNull(5) ? null : reader.GetString(5), Quality = quality, Timestamp = reader.GetDateTimeOffset(7), ErrorMessage = reader.IsDBNull(8) ? null : reader.GetString(8) };
        }

        /// <inheritdoc />
        public void Dispose()
        {
        }

        private async Task<IReadOnlyList<IndustrialDataRecord>> ReadAsync(
            string sql,
            long? afterId,
            int maxRows,
            CancellationToken cancellationToken)
        {
            if (maxRows <= 0 || maxRows > 1000)
            {
                throw new ArgumentOutOfRangeException(nameof(maxRows), "单次查询行数必须在 1 到 1000 之间。");
            }

            var records = new List<IndustrialDataRecord>(maxRows);
            using (var connection = new SqlConnection(_options.ConnectionString))
            using (var command = new SqlCommand(sql, connection))
            {
                command.CommandTimeout = _options.CommandTimeoutSeconds;
                command.Parameters.Add("@MaxRows", SqlDbType.Int).Value = maxRows;
                if (afterId.HasValue)
                {
                    command.Parameters.Add("@AfterId", SqlDbType.BigInt).Value = afterId.Value;
                }

                await connection.OpenAsync(cancellationToken).ConfigureAwait(false);
                using (var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false))
                {
                    while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
                    {
                        ProtocolKind protocol;
                        DataType dataType;
                        QualityStatus quality;
                        Enum.TryParse(reader.GetString(1), true, out protocol);
                        Enum.TryParse(reader.GetString(4), true, out dataType);
                        Enum.TryParse(reader.GetString(6), true, out quality);

                        records.Add(new IndustrialDataRecord
                        {
                            Id = reader.GetInt64(0),
                            Protocol = protocol,
                            DeviceId = reader.GetString(2),
                            Address = reader.GetString(3),
                            DataType = dataType,
                            ValueText = reader.IsDBNull(5) ? null : reader.GetString(5),
                            Quality = quality,
                            Timestamp = reader.GetDateTimeOffset(7),
                            ErrorMessage = reader.IsDBNull(8) ? null : reader.GetString(8),
                        });
                    }
                }
            }

            return records;
        }

        private SqlCommand CreateInsertCommand(SqlConnection connection, SqlTransaction transaction, string sql)
        {
            var command = new SqlCommand(sql, connection, transaction)
            {
                CommandTimeout = _options.CommandTimeoutSeconds,
            };
            // 显式声明 SqlDbType 和长度，避免 AddWithValue 根据运行时值推断出不稳定的数据库类型。
            command.Parameters.Add("@Protocol", SqlDbType.NVarChar, 32);
            command.Parameters.Add("@DeviceId", SqlDbType.NVarChar, 128);
            command.Parameters.Add("@Address", SqlDbType.NVarChar, 128);
            command.Parameters.Add("@DataType", SqlDbType.NVarChar, 32);
            command.Parameters.Add("@ValueText", SqlDbType.NVarChar, -1);
            command.Parameters.Add("@RawData", SqlDbType.VarBinary, -1);
            command.Parameters.Add("@Quality", SqlDbType.NVarChar, 32);
            command.Parameters.Add("@Timestamp", SqlDbType.DateTimeOffset);
            command.Parameters.Add("@ErrorMessage", SqlDbType.NVarChar, 2048);
            return command;
        }

        private static void SetParameterValues(SqlCommand command, IndustrialDataRecord record)
        {
            if (record == null) throw new ArgumentNullException(nameof(record));
            command.Parameters["@Protocol"].Value = record.Protocol.ToString();
            command.Parameters["@DeviceId"].Value = record.DeviceId ?? string.Empty;
            command.Parameters["@Address"].Value = record.Address ?? string.Empty;
            command.Parameters["@DataType"].Value = record.DataType.ToString();
            // ADO.NET 使用 DBNull.Value 表示 SQL NULL，不能直接传入 C# 的 null。
            command.Parameters["@ValueText"].Value = (object)record.ValueText ?? DBNull.Value;
            command.Parameters["@RawData"].Value = (object)record.RawData ?? DBNull.Value;
            command.Parameters["@Quality"].Value = record.Quality.ToString();
            command.Parameters["@Timestamp"].Value = record.Timestamp;
            command.Parameters["@ErrorMessage"].Value = (object)record.ErrorMessage ?? DBNull.Value;
        }

        internal sealed class SqlTableIdentifier
        {
            private SqlTableIdentifier(string schema, string table)
            {
                Schema = schema;
                Table = table;
            }

            public string Schema { get; private set; }
            public string Table { get; private set; }
            public string UnquotedName { get { return Schema + "." + Table; } }
            public string QuotedName { get { return Quote(Schema) + "." + Quote(Table); } }

            public string QuoteGeneratedName(string prefix)
            {
                var value = prefix + "_" + Table;
                return Quote(value.Length <= 128 ? value : value.Substring(0, 128));
            }

            public static SqlTableIdentifier Parse(string value)
            {
                // SQL 参数只能替代“值”，不能替代表名。因此表名必须在拼接前限制为安全字符。
                // 这里要求恰好是 schema.table 两段，拒绝分号、空格、方括号等可构造 SQL 的字符。
                var parts = (value ?? string.Empty).Split('.');
                if (parts.Length != 2 || !IsValidPart(parts[0]) || !IsValidPart(parts[1]))
                {
                    throw new InvalidOperationException("历史表名必须使用 schema.table 格式，且只能包含字母、数字和下划线。");
                }

                return new SqlTableIdentifier(parts[0], parts[1]);
            }

            private static bool IsValidPart(string value)
            {
                if (string.IsNullOrWhiteSpace(value) || value.Length > 128 || (!char.IsLetter(value[0]) && value[0] != '_'))
                {
                    return false;
                }

                return value.All(ch => char.IsLetterOrDigit(ch) || ch == '_');
            }

            private static string Quote(string value)
            {
                // SQL Server 使用 [name] 引用对象名，避免普通关键字与对象名冲突。
                return "[" + value.Replace("]", "]]" ) + "]";
            }
        }
    }
}

