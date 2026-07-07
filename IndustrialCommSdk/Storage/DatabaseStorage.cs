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
    /// <summary>
    /// 数据库历史记录中的一条设备采集值。
    /// <para>
    /// 通信层返回的是 <see cref="DataValue"/>，其中的 <c>Value</c> 是 <see cref="object"/>：
    /// 它可能是整数、浮点数、字符串或字节数组。数据库表不适合为每一种 PLC 数据类型都建一列，
    /// 因此这里把显示值统一保存到 <see cref="ValueText"/>，同时保留 <see cref="RawData"/>，
    /// 方便以后排查字节序、数据类型转换等问题。
    /// </para>
    /// </summary>
    public sealed class IndustrialDataRecord
    {
        /// <summary>数据库自增主键。新记录写入前为 0，从历史表查询时由数据库赋值。</summary>
        public long Id { get; set; }
        /// <summary>通信协议，例如 Modbus TCP、Siemens S7 或 Mitsubishi MC。</summary>
        public ProtocolKind Protocol { get; set; }
        /// <summary>设备标识。它来自创建通信客户端时配置的 DeviceId，用于区分多台设备。</summary>
        public string DeviceId { get; set; }
        /// <summary>协议地址，例如 Modbus 的 D100 或 S7 的 DB1.DBD0。</summary>
        public string Address { get; set; }
        /// <summary>SDK 对该地址进行解码时使用的数据类型。</summary>
        public DataType DataType { get; set; }
        /// <summary>
        /// 使用不受区域设置影响的格式序列化后的值。
        /// 例如浮点数始终使用小数点，避免不同 Windows 区域设置产生不同文本格式。
        /// </summary>
        public string ValueText { get; set; }
        /// <summary>设备返回的原始字节。保存副本，防止调用方以后修改原数组而改变待写入数据。</summary>
        public byte[] RawData { get; set; }
        /// <summary>数据质量。Good 表示读取成功，Bad/Stale 等状态也可以入库供故障追溯。</summary>
        public QualityStatus Quality { get; set; }
        /// <summary>采集时间。使用 DateTimeOffset，同时保留时间和时区偏移。</summary>
        public DateTimeOffset Timestamp { get; set; }
        /// <summary>读取失败时的错误信息；成功记录通常为空。</summary>
        public string ErrorMessage { get; set; }

        /// <summary>
        /// 把 SDK 的读取结果转换为不依赖具体 PLC 类型的数据库记录。
        /// 这个转换在数据进入后台队列之前完成，后台写库线程只需要处理统一模型。
        /// </summary>
        public static IndustrialDataRecord FromDataValue(ProtocolKind protocol, string deviceId, DataValue value)
        {
            if (string.IsNullOrWhiteSpace(deviceId)) throw new ArgumentNullException(nameof(deviceId));
            if (value == null) throw new ArgumentNullException(nameof(value));

            return new IndustrialDataRecord
            {
                Protocol = protocol,
                DeviceId = deviceId,
                Address = value.Address,
                DataType = value.DataType,
                ValueText = FormatValue(value.Value),
                // RawData 是可变数组，因此必须复制；否则调用方改动原数组会污染历史数据。
                RawData = value.RawData == null ? null : (byte[])value.RawData.Clone(),
                Quality = value.Quality,
                Timestamp = value.Timestamp,
                ErrorMessage = value.ErrorMessage,
            };
        }

        private static string FormatValue(object value)
        {
            if (value == null) return null;

            // 字节数组直接转成 12-34-AB 形式，比 System.Byte[] 更适合人工查看。
            var bytes = value as byte[];
            if (bytes != null) return BitConverter.ToString(bytes);

            // 数值实现了 IFormattable，指定 InvariantCulture 可避免逗号/小数点受系统语言影响。
            var formattable = value as IFormattable;
            return formattable != null
                ? formattable.ToString(null, CultureInfo.InvariantCulture)
                : Convert.ToString(value, CultureInfo.InvariantCulture);
        }
    }


    /// <summary>
    /// 历史数据查询过滤条件。
    /// 所有字段均为可选，null 表示不限制该条件，多个条件之间为 AND 关系。
    /// </summary>
    public sealed class HistoryQueryFilter
    {
        /// <summary>按设备标识过滤（精确匹配）。</summary>
        public string DeviceId { get; set; }
        /// <summary>按协议地址过滤（精确匹配，例如 "D100" 或 "DB1.DBD0"）。</summary>
        public string Address { get; set; }
        public HistoryAddressMatchMode AddressMatchMode { get; set; } = HistoryAddressMatchMode.Exact;
        /// <summary>按协议类型过滤。</summary>
        public ProtocolKind? Protocol { get; set; }
        public DataType? DataType { get; set; }
        /// <summary>只查询此时间之后的记录（含边界）。</summary>
        public DateTimeOffset? FromTime { get; set; }
        /// <summary>只查询此时间之前的记录（含边界）。</summary>
        public DateTimeOffset? ToTime { get; set; }
        /// <summary>只查询指定质量状态的记录。</summary>
        public QualityStatus? Quality { get; set; }
        /// <summary>最大返回行数，默认 1000。</summary>
        public int MaxRows { get; set; } = 1000;
    }

    public enum HistoryAddressMatchMode { Exact, Contains }

    public sealed class HistoryPageRequest
    {
        public HistoryQueryFilter Filter { get; set; } = new HistoryQueryFilter();
        public int PageNumber { get; set; } = 1;
        public int PageSize { get; set; } = 100;
    }

    public sealed class HistoryPageResult
    {
        public IReadOnlyList<IndustrialDataRecord> Records { get; set; }
        public long TotalCount { get; set; }
        public int PageNumber { get; set; }
        public int PageSize { get; set; }
    }

    public sealed class HistorySummary
    {
        public long TotalCount { get; set; }
        public long GoodCount { get; set; }
        public long BadCount { get; set; }
        public long StaleCount { get; set; }
        public long UnknownCount { get; set; }
        public DateTimeOffset? EarliestTimestamp { get; set; }
        public DateTimeOffset? LatestTimestamp { get; set; }
    }

    public sealed class HistoryFilterOptions
    {
        public IReadOnlyList<string> DeviceIds { get; set; }
        public IReadOnlyList<string> Addresses { get; set; }
    }

    public interface IIndustrialHistoryManagementStore
    {
        Task<HistoryPageResult> QueryPageAsync(HistoryPageRequest request, CancellationToken cancellationToken);
        Task<HistorySummary> GetSummaryAsync(HistoryQueryFilter filter, CancellationToken cancellationToken);
        Task<HistoryFilterOptions> GetFilterOptionsAsync(string deviceId, int maxItems, CancellationToken cancellationToken);
        Task<IReadOnlyList<IndustrialDataRecord>> GetLatestValuesAsync(HistoryQueryFilter filter, int maxRows, CancellationToken cancellationToken);
    }

    /// <summary>
    /// 工业采集数据存储接口。
    /// <para>
    /// 通过接口隔离后，通信模块只认识“保存历史数据”这一能力，并不知道底层是 SQL Server、
    /// SQLite 还是测试用内存集合。最重要的是：存储实现不得参与 PLC 实时控制，数据库故障不能
    /// 阻止设备通信、急停或联锁逻辑继续运行。
    /// </para>
    /// </summary>
    public interface IIndustrialDataStore : IDisposable
    {
        /// <summary>
        /// 检查数据库连接并创建存储所需的数据表。
        /// 该方法可以重复调用；表已经存在时不会重复创建。
        /// </summary>
        Task InitializeAsync(CancellationToken cancellationToken);

        /// <summary>
        /// 在一个数据库事务中批量写入历史记录。
        /// 批量写入可以减少频繁打开事务带来的开销。
        /// </summary>
        Task WriteAsync(IReadOnlyCollection<IndustrialDataRecord> records, CancellationToken cancellationToken);

        /// <summary>
        /// 按条件查询历史记录，结果按时间降序排列。
        /// </summary>
        Task<IReadOnlyList<IndustrialDataRecord>> QueryAsync(HistoryQueryFilter filter, CancellationToken cancellationToken);

        /// <summary>
        /// 按条件删除历史记录，返回实际删除的行数。
        /// </summary>
        Task<int> DeleteAsync(HistoryQueryFilter filter, CancellationToken cancellationToken);
    }

    /// <summary>
    /// SQL Server 历史数据存储配置。
    /// 使用独立配置类可以让调用方在不修改存储实现的情况下切换服务器、数据库或表名。
    /// </summary>
    public sealed class SqlServerDataStoreOptions
    {
        /// <summary>
        /// SQL Server 连接字符串。
        /// 推荐在 Windows 上位机中使用 Integrated Security=True，避免把用户名和密码写进配置文件。
        /// </summary>
        public string ConnectionString { get; set; }

        /// <summary>
        /// 历史表名，格式为 schema.table，例如 dbo.IndustrialDataHistory。
        /// 表名会经过严格校验后才拼接到建表 SQL 中。
        /// </summary>
        public string TableName { get; set; } = "dbo.IndustrialDataHistory";

        /// <summary>SQL 命令超时秒数。设置超时可以避免数据库异常时无限等待。</summary>
        public int CommandTimeoutSeconds { get; set; } = 15;

        internal SqlServerIndustrialDataStore.SqlTableIdentifier ValidateAndGetTable()
        {
            // 尽早验证配置，让错误在启动数据库记录功能时暴露，而不是等到第一次采集后才出现。
            if (string.IsNullOrWhiteSpace(ConnectionString))
            {
                throw new InvalidOperationException("SQL Server 连接字符串不能为空。");
            }

            if (CommandTimeoutSeconds <= 0)
            {
                throw new InvalidOperationException("SQL 命令超时必须大于 0 秒。");
            }

            return SqlServerIndustrialDataStore.SqlTableIdentifier.Parse(TableName);
        }
    }

    /// <summary>
    /// 使用 ADO.NET 将工业采集值写入 SQL Server。
    /// <para>
    /// 项目目标框架是 .NET Framework 4.7.2，因此这里直接使用框架内置的
    /// <see cref="SqlConnection"/> 和 <see cref="SqlCommand"/>。这样依赖更少，部署到工业电脑时
    /// 也不需要额外携带 Entity Framework 运行组件。
    /// </para>
    /// </summary>
    public sealed class SqlServerIndustrialDataStore : IIndustrialDataStore, IIndustrialHistoryManagementStore
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
                        // 任意一条失败就回滚本批次，然后把异常交给后台记录器决定是否重试。
                        transaction.Rollback();
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
[Id], [Protocol], [DeviceId], [Address], [DataType], [ValueText], [Quality], [Timestamp], [ErrorMessage]
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
[Id], [Protocol], [DeviceId], [Address], [DataType], [ValueText], [Quality], [Timestamp], [ErrorMessage]
FROM {0}
WHERE [Id] > @AfterId
ORDER BY [Id] ASC;",
                    _table.QuotedName),
                afterId,
                maxRows,
                cancellationToken);
        }

        /// <inheritdoc />
        public async Task<IReadOnlyList<IndustrialDataRecord>> QueryAsync(HistoryQueryFilter filter, CancellationToken cancellationToken)
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
[Id], [Protocol], [DeviceId], [Address], [DataType], [ValueText], [Quality], [Timestamp], [ErrorMessage]
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
            var countSql = string.Format(CultureInfo.InvariantCulture, "SELECT COUNT_BIG(1) FROM {0} {1};", _table.QuotedName, where);
            var pageSql = string.Format(CultureInfo.InvariantCulture,
                @"SELECT [Id], [Protocol], [DeviceId], [Address], [DataType], [ValueText], [Quality], [Timestamp], [ErrorMessage]
FROM {0} {1}
ORDER BY [Timestamp] DESC, [Id] DESC
OFFSET @Offset ROWS FETCH NEXT @PageSize ROWS ONLY;", _table.QuotedName, where);

            using (var connection = new SqlConnection(_options.ConnectionString))
            {
                await connection.OpenAsync(cancellationToken).ConfigureAwait(false);
                long total;
                using (var countCommand = new SqlCommand(countSql, connection))
                {
                    countCommand.CommandTimeout = _options.CommandTimeoutSeconds;
                    AddFilterParameters(countCommand, filter);
                    total = Convert.ToInt64(await countCommand.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false), CultureInfo.InvariantCulture);
                }

                var records = new List<IndustrialDataRecord>(request.PageSize);
                using (var command = new SqlCommand(pageSql, connection))
                {
                    command.CommandTimeout = _options.CommandTimeoutSeconds;
                    AddFilterParameters(command, filter);
                    command.Parameters.Add("@Offset", SqlDbType.Int).Value = checked((request.PageNumber - 1) * request.PageSize);
                    command.Parameters.Add("@PageSize", SqlDbType.Int).Value = request.PageSize;
                    using (var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false))
                        while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false)) records.Add(ReadRecord(reader));
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
                @"WITH Latest AS (SELECT [Id],[Protocol],[DeviceId],[Address],[DataType],[ValueText],[Quality],[Timestamp],[ErrorMessage],
ROW_NUMBER() OVER(PARTITION BY [DeviceId],[Address] ORDER BY [Timestamp] DESC,[Id] DESC) AS rn FROM {0} {1})
SELECT TOP (@MaxRows) [Id],[Protocol],[DeviceId],[Address],[DataType],[ValueText],[Quality],[Timestamp],[ErrorMessage]
FROM Latest WHERE rn=1 ORDER BY [Timestamp] DESC,[Id] DESC;", _table.QuotedName, BuildWhereClause(filter));
            var records = new List<IndustrialDataRecord>();
            using (var connection = new SqlConnection(_options.ConnectionString)) using (var command = new SqlCommand(sql, connection))
            { command.CommandTimeout = _options.CommandTimeoutSeconds; command.Parameters.Add("@MaxRows", SqlDbType.Int).Value = maxRows; AddFilterParameters(command, filter); await connection.OpenAsync(cancellationToken).ConfigureAwait(false); using (var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false)) while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false)) records.Add(ReadRecord(reader)); }
            return records;
        }

        private static void ValidateFilter(HistoryQueryFilter filter)
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

    /// <summary>
    /// 后台批量写库配置。
    /// 这些参数控制“写入效率”和“内存占用”之间的平衡，调用方通常使用默认值即可。
    /// </summary>
    public sealed class BufferedDataRecorderOptions
    {
        /// <summary>
        /// 单次数据库事务期望写入的最大记录数。
        /// 数值过小会增加事务次数，数值过大会让单次事务持续更久。
        /// </summary>
        public int BatchSize { get; set; } = 100;
        /// <summary>
        /// 内存中最多排队的读取批次数。
        /// 使用上限可以防止数据库长期不可用时队列无限增长，最终耗尽上位机内存。
        /// </summary>
        public int QueueCapacity { get; set; } = 1000;
        /// <summary>单批失败后的重试次数。重试仍失败时记录错误并放弃该批数据。</summary>
        public int RetryCount { get; set; } = 2;
    }

    public sealed class BufferedRecorderSnapshot
    {
        public bool IsRunning { get; set; }
        public int QueuedBatchCount { get; set; }
        public long AcceptedRecordCount { get; set; }
        public long WrittenRecordCount { get; set; }
        public long DroppedRecordCount { get; set; }
        public long WriteFailureCount { get; set; }
        public DateTimeOffset? LastSuccessfulWrite { get; set; }
        public string LastError { get; set; }
    }

    /// <summary>
    /// 把通信线程产生的读取结果放入有界队列，再由单独后台任务批量写库。
    /// <para>完整数据流如下：</para>
    /// <para>PLC 读取回调 → <see cref="TryRecord"/> 快速入队 → 后台任务合并批次 → SQL Server。</para>
    /// <para>
    /// 这种结构叫“生产者/消费者”：通信线程是生产者，数据库线程是消费者。
    /// 数据库变慢或短暂断线时不会阻塞设备通信；队列满时会丢弃最新批次并记录警告，
    /// 以保证实时通信优先于历史数据完整性。
    /// </para>
    /// </summary>
    public sealed class BufferedIndustrialDataRecorder : IDisposable
    {
        private readonly IIndustrialDataStore _store;
        private readonly BufferedDataRecorderOptions _options;
        private readonly IIndustrialLogger _logger;
        // BlockingCollection 同时提供线程安全队列、容量限制和“完成添加”通知，
        // 很适合 .NET Framework 4.7.2 下实现简单可靠的生产者/消费者模型。
        private readonly BlockingCollection<IReadOnlyCollection<IndustrialDataRecord>> _queue;

        // 该取消源只属于后台工作任务。正常停止使用 CompleteAdding 排空队列，
        // Dispose 才会取消仍在等待的任务。
        private readonly CancellationTokenSource _stopSource = new CancellationTokenSource();
        private Task _worker;
        private readonly object _stopGate = new object();
        private Task _stopTask;

        // 使用整数配合 Interlocked/Volatile 代替普通 bool，确保多线程能看到一致的启动状态。
        private int _started;
        private long _acceptedRecordCount;
        private long _writtenRecordCount;
        private long _droppedRecordCount;
        private long _writeFailureCount;
        private long _lastSuccessfulWriteUtcTicks;
        private string _lastError;
        private int _disposed;

        /// <summary>
        /// 创建后台记录器，但此时还没有连接数据库或启动后台任务。
        /// 调用方必须继续调用 <see cref="StartAsync"/>。
        /// </summary>
        public BufferedIndustrialDataRecorder(
            IIndustrialDataStore store,
            BufferedDataRecorderOptions options = null,
            IIndustrialLogger logger = null)
        {
            _store = store ?? throw new ArgumentNullException(nameof(store));
            _options = options ?? new BufferedDataRecorderOptions();

            // 配置错误属于编程错误，应在启动前立即报告，而不是让后台线程静默失败。
            if (_options.BatchSize <= 0) throw new ArgumentOutOfRangeException(nameof(options), "BatchSize 必须大于 0。");
            if (_options.QueueCapacity <= 0) throw new ArgumentOutOfRangeException(nameof(options), "QueueCapacity 必须大于 0。");
            if (_options.RetryCount < 0) throw new ArgumentOutOfRangeException(nameof(options), "RetryCount 不能小于 0。");
            _logger = logger ?? NullIndustrialLogger.Instance;
            _queue = new BlockingCollection<IReadOnlyCollection<IndustrialDataRecord>>(_options.QueueCapacity);
        }

        /// <summary>
        /// 检查数据库、创建表并启动后台写入任务。
        /// 只有初始化成功后才启动消费者，避免把数据放入一个永远无法写出的队列。
        /// </summary>
        public async Task StartAsync(CancellationToken cancellationToken)
        {
            if (Volatile.Read(ref _disposed) != 0) throw new ObjectDisposedException(nameof(BufferedIndustrialDataRecorder));
            // CompareExchange 保证并发调用 StartAsync 时只有第一个调用真正执行启动逻辑。
            if (Interlocked.CompareExchange(ref _started, 1, 0) != 0) return;
            try
            {
                await _store.InitializeAsync(cancellationToken).ConfigureAwait(false);
                _logger.Info(string.Format(CultureInfo.InvariantCulture, "DATABASE recorder started | QueueCapacity={0} | BatchSize={1} | Retries={2}", _options.QueueCapacity, _options.BatchSize, _options.RetryCount));

                // Task.Run 把持续运行的队列消费者放到线程池，不占用 WPF UI 线程。
                _worker = Task.Run(() => ProcessQueueAsync(_stopSource.Token));
            }
            catch
            {
                // 初始化失败后恢复未启动状态，调用方可以修正连接字符串并创建新记录器重试。
                Interlocked.Exchange(ref _started, 0);
                throw;
            }
        }

        /// <summary>
        /// 非阻塞地把一批读取结果加入写库队列。
        /// <para>
        /// “非阻塞”是这里最重要的约束：该方法可能直接运行在 PLC 轮询回调中，
        /// 因此不能等待数据库，也不能在队列满时停住通信线程。
        /// </para>
        /// </summary>
        public bool TryRecord(ProtocolKind protocol, string deviceId, IReadOnlyCollection<DataValue> values)
        {
            if (Volatile.Read(ref _disposed) != 0 || Volatile.Read(ref _started) == 0 || values == null || values.Count == 0 || _queue.IsAddingCompleted)
            {
                return false;
            }

            // 入队前转换并复制可变数据，确保后台线程读取的是调用时刻的稳定快照。
            var records = values.Select(value => IndustrialDataRecord.FromDataValue(protocol, deviceId, value)).ToArray();
            if (_queue.TryAdd(records))
            {
                Interlocked.Add(ref _acceptedRecordCount, records.Length);
                _logger.Trace(string.Format(CultureInfo.InvariantCulture, "DATABASE batch queued | Records={0} | QueuedBatches={1}", records.Length, _queue.Count));
                return true;
            }

            // TryAdd 不等待空间。队列满说明数据库速度已明显落后，优先保护通信和内存。
            _logger.Warn("数据库写入队列已满，本批采集数据已丢弃。");
            Interlocked.Add(ref _droppedRecordCount, records.Length);
            return false;
        }

        public BufferedRecorderSnapshot GetSnapshot()
        {
            var ticks = Interlocked.Read(ref _lastSuccessfulWriteUtcTicks);
            return new BufferedRecorderSnapshot {
                IsRunning = Volatile.Read(ref _started) != 0,
                QueuedBatchCount = _queue.Count,
                AcceptedRecordCount = Interlocked.Read(ref _acceptedRecordCount), WrittenRecordCount = Interlocked.Read(ref _writtenRecordCount),
                DroppedRecordCount = Interlocked.Read(ref _droppedRecordCount), WriteFailureCount = Interlocked.Read(ref _writeFailureCount),
                LastSuccessfulWrite = ticks == 0 ? (DateTimeOffset?)null : new DateTimeOffset(ticks, TimeSpan.Zero), LastError = Volatile.Read(ref _lastError) };
        }

        /// <summary>
        /// 停止接收新数据，并等待队列中的记录写完。
        /// 这叫“优雅停止”：应用退出时先禁止入队，再把已经采集的数据排空，减少停机丢数。
        /// </summary>
        public async Task StopAsync(CancellationToken cancellationToken)
        {
            Task stopTask;
            lock (_stopGate)
            {
                if (_stopTask == null)
                {
                    if (Interlocked.Exchange(ref _started, 0) == 0)
                    {
                        return;
                    }

                    // 取消调用方的等待不会取消实际排空过程；后续 StopAsync 或 Dispose
                    // 仍可取得同一个任务并继续等待，避免队列处于“已停止但无人收尾”的状态。
                    _queue.CompleteAdding();
                    _stopTask = CompleteStopAsync();
                }
                stopTask = _stopTask;
            }

            var completed = await Task.WhenAny(stopTask, Task.Delay(Timeout.Infinite, cancellationToken)).ConfigureAwait(false);
            if (completed != stopTask) cancellationToken.ThrowIfCancellationRequested();
            await stopTask.ConfigureAwait(false);
        }

        /// <inheritdoc />
        public void Dispose()
        {
            if (Interlocked.Exchange(ref _disposed, 1) != 0)
            {
                return;
            }

            // 即使先前 StopAsync 的“等待”被取消，实际排空任务仍保存在 _stopTask 中，
            // Dispose 必须重新等待它，不能直接释放队列和存储对象。
            try { StopAsync(CancellationToken.None).GetAwaiter().GetResult(); }
            catch (Exception ex) { _logger.Error("停止数据库记录器失败。", ex); }

            // 释放顺序：通知任务取消 → 释放队列/令牌源 → 释放具体数据库存储。
            _stopSource.Cancel();
            _queue.Dispose();
            _stopSource.Dispose();
            _store.Dispose();
            Interlocked.Exchange(ref _started, 0);
        }

        private async Task CompleteStopAsync()
        {
            if (_worker != null)
            {
                await _worker.ConfigureAwait(false);
            }
            _logger.Info(string.Format(CultureInfo.InvariantCulture, "DATABASE recorder stopped | Accepted={0} | Written={1} | Dropped={2} | Failures={3}", Interlocked.Read(ref _acceptedRecordCount), Interlocked.Read(ref _writtenRecordCount), Interlocked.Read(ref _droppedRecordCount), Interlocked.Read(ref _writeFailureCount)));
        }

        private async Task ProcessQueueAsync(CancellationToken cancellationToken)
        {
            // 复用 List 缓冲区，避免每一轮消费者循环都创建新的可增长集合。
            var batch = new List<IndustrialDataRecord>(_options.BatchSize);
            while (!_queue.IsCompleted)
            {
                IReadOnlyCollection<IndustrialDataRecord> first;
                try
                {
                    // 没有数据时 Take 会等待，不会让后台线程空转并持续占用 CPU。
                    first = _queue.Take(cancellationToken);
                }
                catch (InvalidOperationException)
                {
                    // CompleteAdding 且队列已空时，Take 会抛出该异常，表示可以正常结束消费者。
                    break;
                }

                batch.Clear();
                batch.AddRange(first);
                IReadOnlyCollection<IndustrialDataRecord> next;

                // 将队列中已经到达的小批次尽量合并，减少数据库事务和网络往返次数。
                while (batch.Count < _options.BatchSize && _queue.TryTake(out next))
                {
                    batch.AddRange(next);
                }

                await WriteWithRetryAsync(batch.ToArray(), cancellationToken).ConfigureAwait(false);
            }
        }

        private async Task WriteWithRetryAsync(IReadOnlyCollection<IndustrialDataRecord> records, CancellationToken cancellationToken)
        {
            for (var attempt = 0; ; attempt++)
            {
                try
                {
                    var stopwatch = Stopwatch.StartNew();
                    await _store.WriteAsync(records, cancellationToken).ConfigureAwait(false);
                    Interlocked.Add(ref _writtenRecordCount, records.Count);
                    Interlocked.Exchange(ref _lastSuccessfulWriteUtcTicks, DateTimeOffset.UtcNow.UtcTicks);
                    Volatile.Write(ref _lastError, null);
                    _logger.Trace(string.Format(CultureInfo.InvariantCulture, "DATABASE batch written | Records={0} | Attempt={1} | Elapsed={2}ms", records.Count, attempt + 1, stopwatch.ElapsedMilliseconds));
                    return;
                }
                catch (Exception ex) when (!(ex is OperationCanceledException) && attempt < _options.RetryCount)
                {
                    Interlocked.Increment(ref _writeFailureCount);
                    Volatile.Write(ref _lastError, ex.Message);
                    _logger.Warn(string.Format(CultureInfo.InvariantCulture, "数据库写入失败，准备第 {0} 次重试：{1}", attempt + 1, ex.Message));

                    // 重试间隔逐次增加，避免数据库刚恢复时大量客户端同时立即重试形成冲击。
                    await Task.Delay(TimeSpan.FromMilliseconds(250 * (attempt + 1)), cancellationToken).ConfigureAwait(false);
                }
                catch (Exception ex) when (!(ex is OperationCanceledException))
                {
                    // 达到重试上限后放弃本批。这里不能让异常结束消费者，否则后续数据库恢复也无法继续记录。
                    _logger.Error("数据库写入失败，本批数据已放弃。", ex);
                    Interlocked.Increment(ref _writeFailureCount);
                    Interlocked.Add(ref _droppedRecordCount, records.Count);
                    Volatile.Write(ref _lastError, ex.Message);
                    return;
                }
            }
        }
    }
}
