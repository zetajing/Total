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
    public sealed partial class SqlServerIndustrialDataStore : IIndustrialHistoryStore
    {
        private readonly SqlServerDataStoreOptions _options;
        private readonly SqlTableIdentifier _table;

        /// <summary>
        /// 创建 SQL Server 存储，并立即校验连接字符串以外的本地配置。
        /// 构造函数不会访问数据库，真正连接发生在 <see cref="InitializeAsync"/>。
        /// </summary>
        public SqlServerIndustrialDataStore(SqlServerDataStoreOptions options)
        {
            _options = options ?? throw new ArgumentNullException(nameof(options));
            _table = options.ValidateAndGetTable();
        }

        /// <inheritdoc />
        public async Task InitializeAsync(CancellationToken cancellationToken)
        {
            // 表结构同时保存“可读文本”和“原始字节”：前者便于报表展示，后者便于协议故障排查。
            // OBJECT_ID 判断使这段初始化 SQL 具备幂等性，多次启动应用不会重复建表。
            var sql = string.Format(
                CultureInfo.InvariantCulture,
                @"IF OBJECT_ID(@ObjectName, N'U') IS NULL
BEGIN
    CREATE TABLE {0}
    (
        [Id] BIGINT IDENTITY(1,1) NOT NULL CONSTRAINT {1} PRIMARY KEY,
        [Protocol] NVARCHAR(32) NOT NULL,
        [DeviceId] NVARCHAR(128) NOT NULL,
        [Address] NVARCHAR(128) NOT NULL,
        [DataType] NVARCHAR(32) NOT NULL,
        [ValueText] NVARCHAR(MAX) NULL,
        [RawData] VARBINARY(MAX) NULL,
        [Quality] NVARCHAR(32) NOT NULL,
        [Timestamp] DATETIMEOFFSET(7) NOT NULL,
        [ErrorMessage] NVARCHAR(2048) NULL
    );
    CREATE INDEX {2} ON {0} ([Timestamp] DESC);
    CREATE INDEX {3} ON {0} ([DeviceId], [Address], [Timestamp] DESC);
END;",
                _table.QuotedName,
                _table.QuoteGeneratedName("PK"),
                _table.QuoteGeneratedName("IX_Timestamp"),
                _table.QuoteGeneratedName("IX_Device_Address_Timestamp"));

            using (var connection = new SqlConnection(_options.ConnectionString))
            using (var command = new SqlCommand(sql, connection))
            {
                command.CommandTimeout = _options.CommandTimeoutSeconds;

                // 对象名判断也使用参数，只有经过校验并加方括号的表名才会进入 DDL 文本。
                command.Parameters.Add("@ObjectName", SqlDbType.NVarChar, 257).Value = _table.UnquotedName;

                // OpenAsync/ExecuteNonQueryAsync 避免数据库连接过程占用调用线程。
                // ConfigureAwait(false) 表示 SDK 不要求回到 WPF UI 线程继续执行。
                await connection.OpenAsync(cancellationToken).ConfigureAwait(false);
                await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
            }
        }

        /// <inheritdoc />
        public async Task WriteAsync(IReadOnlyCollection<IndustrialDataRecord> records, CancellationToken cancellationToken)
        {
            if (records == null) throw new ArgumentNullException(nameof(records));
            if (records.Count == 0) return;

            // 只有经过校验并引用的表名需要拼接；每一条设备数据都使用 SQL 参数传入。
            // 绝不能直接把 DeviceId、Address 或 ValueText 拼进 SQL，否则会产生 SQL 注入风险。
            var sql = string.Format(
                CultureInfo.InvariantCulture,
                @"INSERT INTO {0}
([Protocol], [DeviceId], [Address], [DataType], [ValueText], [RawData], [Quality], [Timestamp], [ErrorMessage])
VALUES
(@Protocol, @DeviceId, @Address, @DataType, @ValueText, @RawData, @Quality, @Timestamp, @ErrorMessage);",
                _table.QuotedName);

            using (var connection = new SqlConnection(_options.ConnectionString))
            {
                await connection.OpenAsync(cancellationToken).ConfigureAwait(false);

                // 一个批次使用一个事务：要么整批成功，要么整批回滚，避免只写入一半。
                using (var transaction = connection.BeginTransaction())
                using (var command = CreateInsertCommand(connection, transaction, sql))
                {
                    try
                    {
                        foreach (var record in records)
                        {
                            cancellationToken.ThrowIfCancellationRequested();

                            // 复用同一个 SqlCommand 和参数对象，只替换 Value，减少循环内对象分配。
                            SetParameterValues(command, record);
                            await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
                        }

                        transaction.Commit();
                    }
                    catch
                    {
                        // 保留原始写入/网络异常；连接已损坏时 Rollback 也可能失败。
                        try { transaction.Rollback(); } catch { }
                        throw;
                    }
                }
            }
        }

        /// <summary>读取历史表中最新的若干条记录，结果按 Id 从大到小排列。</summary>
        public Task<IReadOnlyList<IndustrialDataRecord>> ReadLatestAsync(int maxRows, CancellationToken cancellationToken)
        {
            return ReadAsync(
                string.Format(
                    CultureInfo.InvariantCulture,
                    @"SELECT TOP (@MaxRows)
[Id], [Protocol], [DeviceId], [Address], [DataType], [ValueText], [RawData], [Quality], [Timestamp], [ErrorMessage]
FROM {0}
ORDER BY [Id] DESC;",
                    _table.QuotedName),
                null,
                maxRows,
                cancellationToken);
        }

        /// <summary>
        /// 读取指定 Id 之后的新记录，结果按 Id 从小到大排列，便于调用方连续推进增量游标。
        /// </summary>
        public Task<IReadOnlyList<IndustrialDataRecord>> ReadAfterAsync(long afterId, int maxRows, CancellationToken cancellationToken)
        {
            if (afterId < 0) throw new ArgumentOutOfRangeException(nameof(afterId));

            return ReadAsync(
                string.Format(
                    CultureInfo.InvariantCulture,
                    @"SELECT TOP (@MaxRows)
[Id], [Protocol], [DeviceId], [Address], [DataType], [ValueText], [RawData], [Quality], [Timestamp], [ErrorMessage]
FROM {0}
WHERE [Id] > @AfterId
ORDER BY [Id] ASC;",
                    _table.QuotedName),
                afterId,
                maxRows,
                cancellationToken);
        }

        /// <inheritdoc />
    }
}
