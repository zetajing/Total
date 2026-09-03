# 历史存储

SDK 提供 SQL Server 和 MySQL 两种 `IIndustrialHistoryStore` 实现。二者保存相同的 `IndustrialDataRecord`，支持初始化、批量写入、条件查询、分页、最新值、增量读取、删除和保留期清理。采集代码只依赖接口，数据库类型只在应用创建入口决定。

Redis 不属于关系型历史库提供程序，仍是独立的 `InduLink.Protocols.Redis`，用于 Key/Value 通信与缓存集成。

## 选择提供程序

| 项目 | SQL Server | MySQL |
| --- | --- | --- |
| 程序集 | `InduLink.Storage.dll` | `InduLink.Storage.MySql.dll` |
| 命名空间 | `InduLink.Storage` | `InduLink.Storage.MySql` |
| 驱动 | `System.Data.SqlClient` | `MySqlConnector 2.6.1` |
| 服务端 | SQL Server | MySQL 8.0+ |
| 表名 | `schema.table` | `table` 或 `database.table` |
| 时间 | `DATETIMEOFFSET(7)` | UTC 时间 + 偏移分钟两列 |

业务层保持数据库无关：

```csharp
using InduLink.Storage;

public sealed class HistoryService
{
    private readonly IIndustrialHistoryStore _store;

    public HistoryService(IIndustrialHistoryStore store)
    {
        _store = store;
    }

    // QueryAsync、QueryPageAsync、GetLatestValuesAsync 等调用不关心数据库类型。
}
```

SQL Server 和 MySQL 都实现 `IIndustrialHistoryStore`；`IIndustrialDataStore` 是缓冲记录器所需的基础契约。只引用 `InduLink.Storage` 不会携带 MySQL 驱动；完整聚合入口会包含内置 MySQL 提供程序。

## 生命周期和缓冲写入

`InitializeAsync` 检查连接并幂等创建历史表。直接使用存储时，创建者负责初始化和 `Dispose`；使用 `BufferedIndustrialDataRecorder` 时，`StartAsync` 自动初始化。

缓冲记录器的所有权和顺序：

1. `TryRecord` 只做非阻塞入队，不在 PLC 回调线程等待数据库。
2. `StopAsync` 停止接收新数据并排空已接受队列。
3. 记录器拥有传入的存储实例，`Dispose` 会同时释放它；停止后不要继续使用该实例。
4. 队列有界、写入可重试；数据库不可用时优先保护设备通信，并记录丢弃计数。

分页查询保证 `TotalCount` 与当前页来自同一一致读取；后台持续写入时不会把两个时点的结果拼在一个 `HistoryPageResult` 中。

## SQL Server

目标框架为 `net8.0`，使用 `System.Data.SqlClient` NuGet 包，不需要 Entity Framework。`TableName` 必须是 `schema.table`，例如 `dbo.IndustrialDataHistory`；数据库和 schema 必须已经存在，`InitializeAsync` 只创建历史表及索引。

Windows 上位机优先使用 Windows 身份验证：

```csharp
var options = new SqlServerDataStoreOptions
{
    ConnectionString =
        "Server=localhost;Database=UpperComputerDb;Integrated Security=True;" +
        "Encrypt=True;TrustServerCertificate=True;",
    TableName = "dbo.IndustrialDataHistory",
    CommandTimeoutSeconds = 15,
};
```

`TrustServerCertificate=True` 适合本机联调；生产环境应部署可信证书并验证服务器身份。

最小写入和查询示例：

```csharp
using (var recorder = new BufferedIndustrialDataRecorder(
    new SqlServerIndustrialDataStore(options),
    new BufferedDataRecorderOptions
    {
        BatchSize = 100,
        QueueCapacity = 1000,
        RetryCount = 2,
    }))
{
    await recorder.StartAsync(CancellationToken.None);

    var accepted = recorder.TryRecord(
        ProtocolKind.ModbusTcp,
        "plc-1",
        new[]
        {
            new DataValue(
                "D100",
                DataType.Int16,
                42,
                new byte[] { 0, 42 },
                QualityStatus.Good,
                DateTimeOffset.Now,
                null),
        });

    if (!accepted)
        throw new InvalidOperationException("记录未进入缓冲队列。");

    await recorder.StopAsync(CancellationToken.None);
}

using (var queryStore = new SqlServerIndustrialDataStore(options))
{
    var rows = await queryStore.QueryAsync(
        new HistoryQueryFilter { DeviceId = "plc-1", MaxRows = 10 },
        CancellationToken.None);

    foreach (var row in rows)
        Console.WriteLine("{0:o} {1}={2}", row.Timestamp, row.Address, row.ValueText);
}
```

不使用缓冲记录器时，首次写入前直接调用 `await store.InitializeAsync(CancellationToken.None)`，并由存储创建者负责释放。

## MySQL

MySQL 要求 8.0+，因为最新值查询使用 `ROW_NUMBER()` 窗口函数。提供程序程序集为 `InduLink.Storage.MySql.dll`，驱动为 `MySqlConnector 2.6.1`。`TableName` 支持 `IndustrialDataHistory` 或 `UpperComputerDb.IndustrialDataHistory`，标识符只能包含字母、数字和下划线，数据库必须已经存在。

```csharp
var options = new MySqlDataStoreOptions
{
    ConnectionString =
        "Server=127.0.0.1;Port=3306;Database=UpperComputerDb;" +
        "User ID=industrial_app;Password=<PASSWORD>;" +
        "SslMode=Preferred;DateTimeKind=Utc;",
    TableName = "IndustrialDataHistory",
    CommandTimeoutSeconds = 15,
};
```

保留 `DateTimeKind=Utc`，使驱动按 UTC 读取 `DATETIME`。生产环境根据服务器证书使用 `SslMode=Required` 或更严格的 TLS 校验。真实密码不要提交 Git、写入普通日志或直接放在 Demo 明文状态文件中。

MySQL 没有与 SQL Server `DATETIMEOFFSET` 完全对应的列类型，提供程序使用：

- `TimestampUtc DATETIME(6)`：筛选和排序的 UTC 时刻；
- `TimestampOffsetMinutes SMALLINT`：原始时区偏移分钟数。

读取时使用两列重建 `DateTimeOffset`。`DATETIME(6)` 保留微秒精度，不保留 .NET tick 的最后一位 100 纳秒精度。

写入生命周期与 SQL Server 相同：

```csharp
using (var recorder = new BufferedIndustrialDataRecorder(
    new MySqlIndustrialDataStore(options),
    new BufferedDataRecorderOptions
    {
        BatchSize = 100,
        QueueCapacity = 1000,
        RetryCount = 2,
    }))
{
    await recorder.StartAsync(CancellationToken.None);

    recorder.TryRecord(
        ProtocolKind.ModbusTcp,
        "plc-1",
        new[]
        {
            new DataValue(
                "D100",
                DataType.Int16,
                42,
                new byte[] { 0, 42 },
                QualityStatus.Good,
                DateTimeOffset.Now,
                null),
        });

    await recorder.StopAsync(CancellationToken.None);
}

using (var queryStore = new MySqlIndustrialDataStore(options))
{
    var rows = await queryStore.QueryAsync(
        new HistoryQueryFilter { DeviceId = "plc-1", MaxRows = 10 },
        CancellationToken.None);
}
```

## 现场边界

历史库是旁路记录，不是实时控制链路；数据库连接失败不应阻塞 PLC 读写。SQL Server/MySQL 的真实服务器建表、事务回滚、时区往返和取消行为需要在目标环境单独做集成验证，默认离线测试不能替代现场验收。
