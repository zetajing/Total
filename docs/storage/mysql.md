# MySQL 历史存储

## 程序集、版本和命名空间

- 目标框架：`.NET Framework 4.7.2`
- 服务端：MySQL 8.0+
- 公共存储程序集：`IndustrialCommSdk.Storage.dll`
- MySQL 提供程序程序集：`IndustrialCommSdk.Storage.MySql.dll`
- 公共命名空间：`IndustrialCommSdk.Storage`
- 提供程序命名空间：`IndustrialCommSdk.Storage.MySql`
- ADO.NET 驱动：`MySqlConnector 2.6.1`

MySQL 8.0+ 是明确的运行要求，因为最新值查询使用了 `ROW_NUMBER()` 窗口函数。

在仓库根目录旁创建示例项目时，可直接引用三个项目。`IndustrialCommSdk.Storage.MySql` 已传递引用 `MySqlConnector 2.6.1`，示例项目不需要再次添加同一个包：

```xml
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <OutputType>Exe</OutputType>
    <TargetFramework>net472</TargetFramework>
    <LangVersion>latest</LangVersion>
  </PropertyGroup>
  <ItemGroup>
    <ProjectReference Include="..\IndustrialCommSdk.Abstractions\IndustrialCommSdk.Abstractions.csproj" />
    <ProjectReference Include="..\IndustrialCommSdk.Storage\IndustrialCommSdk.Storage.csproj" />
    <ProjectReference Include="..\IndustrialCommSdk.Storage.MySql\IndustrialCommSdk.Storage.MySql.csproj" />
  </ItemGroup>
</Project>
```

## Options

`MySqlDataStoreOptions.TableName` 支持以下两种安全格式：

- `IndustrialDataHistory`：使用连接字符串选定的数据库。
- `UpperComputerDb.IndustrialDataHistory`：显式指定 `database.table`。

数据库必须已经存在；`InitializeAsync` 只创建历史表及索引。标识符只能包含字母、数字和下划线。

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

保留 `DateTimeKind=Utc`，使驱动读取 `DATETIME` 时明确按 UTC 处理。生产环境还应根据服务器证书配置使用 `SslMode=Required` 或更严格的 TLS 校验。

连接字符串通常包含 MySQL 用户名和密码。不要把真实密码提交到 Git，也不要把完整连接字符串写入普通日志。Demo 的 UI 状态文件是明文 JSON；生产应用应从 Windows 凭据管理器、环境注入或其他受保护的凭据来源读取密码。

## 时间存储

MySQL 没有与 SQL Server `DATETIMEOFFSET` 完全对应的列类型。提供程序使用两列保存一个 `DateTimeOffset`：

- `TimestampUtc DATETIME(6)`：用于筛选和排序的 UTC 时刻。
- `TimestampOffsetMinutes SMALLINT`：原始时区偏移分钟数。

读取时会用这两列重建 `IndustrialDataRecord.Timestamp`。`DATETIME(6)` 的精度是微秒，不保留 .NET tick 的最后一位 100 纳秒精度。

## 最小可运行示例

下面的控制台程序展示 Options、缓冲记录、查询和释放。请先创建 `UpperComputerDb` 数据库和具有建表、读写权限的 `industrial_app` 用户，并把 `<PASSWORD>` 替换为实际密码。

```csharp
using System;
using System.Threading;
using System.Threading.Tasks;
using IndustrialCommSdk.Abstractions;
using IndustrialCommSdk.Storage;
using IndustrialCommSdk.Storage.MySql;

internal static class Program
{
    private static void Main()
    {
        RunAsync().GetAwaiter().GetResult();
    }

    private static async Task RunAsync()
    {
        var options = new MySqlDataStoreOptions
        {
            ConnectionString =
                "Server=127.0.0.1;Port=3306;Database=UpperComputerDb;" +
                "User ID=industrial_app;Password=<PASSWORD>;" +
                "SslMode=Preferred;DateTimeKind=Utc;",
            TableName = "IndustrialDataHistory",
            CommandTimeoutSeconds = 15,
        };

        using (var recorder = new BufferedIndustrialDataRecorder(
            new MySqlIndustrialDataStore(options),
            new BufferedDataRecorderOptions
            {
                BatchSize = 100,
                QueueCapacity = 1000,
                RetryCount = 2,
            }))
        {
            // StartAsync 内部初始化写入存储；建表操作可以重复执行。
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
            {
                throw new InvalidOperationException("记录未进入缓冲队列。");
            }

            // 排空已经接受的数据。
            await recorder.StopAsync(CancellationToken.None);
        }

        // 记录器拥有它的写入存储；历史查询使用独立实例。
        using (var queryStore = new MySqlIndustrialDataStore(options))
        {
            var rows = await queryStore.QueryAsync(
                new HistoryQueryFilter
                {
                    DeviceId = "plc-1",
                    MaxRows = 10,
                },
                CancellationToken.None);

            foreach (var row in rows)
            {
                Console.WriteLine("{0:o} {1}={2}", row.Timestamp, row.Address, row.ValueText);
            }
        }
    }
}
```

如果不使用 `BufferedIndustrialDataRecorder`，请在首次写入前直接调用：

```csharp
await store.InitializeAsync(CancellationToken.None);
```

此时存储实例的创建者必须自行调用 `Dispose`。
