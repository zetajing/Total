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
            if (string.IsNullOrWhiteSpace(deviceId)) throw new ArgumentException("Device ID cannot be null or empty.", nameof(deviceId));
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

        /// <summary>
        /// 将任意值对象转换为适合显示或存储的字符串。
        /// 这是 SDK 提供的统一格式化入口，供外部调用（例如 Demo 的 FormatHelper）。
        /// </summary>
        public static string FormatValueStatic(object value)
        {
            return FormatValue(value);
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
    /// MySQL 还是测试用内存集合。最重要的是：存储实现不得参与 PLC 实时控制，数据库故障不能
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
    /// Demo 和历史管理页所需的完整存储能力。
    /// SQL Server 与 MySQL 提供程序实现同一契约，调用方无需分支查询逻辑。
    /// </summary>
    public interface IIndustrialHistoryStore : IIndustrialDataStore, IIndustrialHistoryManagementStore
    {
        /// <summary>读取最新的若干条历史记录，按 Id 降序。</summary>
        Task<IReadOnlyList<IndustrialDataRecord>> ReadLatestAsync(int maxRows, CancellationToken cancellationToken);

        /// <summary>读取指定 Id 之后的记录，按 Id 升序，用于增量刷新。</summary>
        Task<IReadOnlyList<IndustrialDataRecord>> ReadAfterAsync(long afterId, int maxRows, CancellationToken cancellationToken);
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
}
