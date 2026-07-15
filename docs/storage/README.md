# 历史存储

SDK 提供 SQL Server 和 MySQL 两种历史存储实现。二者保存相同的 `IndustrialDataRecord`，并支持初始化、批量写入、条件查询、分页、最新值、删除和保留期清理。

Redis 不属于这两种关系型历史库提供程序。它保持在独立的 `IndustrialCommSdk.Protocols.Redis` 中，用于 Key/Value 通信与缓存集成，不实现 `IIndustrialHistoryStore`。

| 项目 | SQL Server | MySQL |
| --- | --- | --- |
| 提供程序程序集 | `IndustrialCommSdk.Storage.dll` | `IndustrialCommSdk.Storage.MySql.dll` |
| 提供程序命名空间 | `IndustrialCommSdk.Storage` | `IndustrialCommSdk.Storage.MySql` |
| 数据访问驱动 | .NET Framework 自带的 `System.Data.SqlClient` | `MySqlConnector 2.6.1` |
| 服务端版本 | SQL Server | MySQL 8.0+ |
| 表名格式 | `schema.table` | `table` 或 `database.table` |
| 时间存储 | `DATETIMEOFFSET(7)` | UTC 时间与偏移量分别存储 |
| 最小示例 | [SQL Server](sql-server.md) | [MySQL](mysql.md) |

## 业务层只依赖存储接口

数据库类型应只在应用的创建入口决定。采集、查询和历史管理代码不应判断 SQL Server 或 MySQL：

- `IIndustrialDataStore` 提供初始化、写入、查询和删除能力，也是 `BufferedIndustrialDataRecorder` 的依赖。
- `IIndustrialHistoryStore` 在上述能力之外，还提供分页、汇总、筛选项、最新值和按 Id 增量读取，适合历史管理页面。
- `SqlServerIndustrialDataStore` 和 `MySqlIndustrialDataStore` 均实现 `IIndustrialHistoryStore`。

```csharp
using IndustrialCommSdk.Storage;

public sealed class HistoryService
{
    private readonly IIndustrialHistoryStore _store;

    public HistoryService(IIndustrialHistoryStore store)
    {
        _store = store;
    }

    // 这里可以调用 QueryAsync、QueryPageAsync、GetLatestValuesAsync 等方法，
    // 无需知道实际数据库类型。
}
```

## 生命周期和所有权

`InitializeAsync` 会检查连接并以幂等方式创建历史表。直接使用存储时，由调用方调用它并负责 `Dispose`。使用缓冲记录器时，`BufferedIndustrialDataRecorder.StartAsync` 会自动调用 `InitializeAsync`，无需重复初始化。

分页查询会保证 `TotalCount` 与当前页来自同一一致读取；后台持续写入时，不会把两个时点的结果拼在一个 `HistoryPageResult` 中。

缓冲记录器接管传入的 `IIndustrialDataStore`：

1. `TryRecord` 只做非阻塞入队，不在 PLC 回调线程等待数据库。
2. `StopAsync` 停止接收新数据并排空已接受的队列。
3. `BufferedIndustrialDataRecorder.Dispose` 会同时释放底层存储；此后不要继续使用该存储实例。

数据库不可用时，有界队列和重试机制优先保护设备通信。数据库历史记录不能替代 PLC 急停、联锁或其他实时安全逻辑。
