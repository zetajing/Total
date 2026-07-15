# SQL Server 历史存储

## 程序集和命名空间

- 目标框架：`.NET Framework 4.7.2`
- 程序集：`IndustrialCommSdk.Abstractions.dll`、`IndustrialCommSdk.Storage.dll`
- 命名空间：`IndustrialCommSdk.Abstractions`、`IndustrialCommSdk.Storage`
- 驱动：框架自带的 `System.Data.SqlClient`，不需要 Entity Framework

在仓库根目录旁创建示例项目时，可直接引用两个项目：

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
  </ItemGroup>
</Project>
```

## Options

`SqlServerDataStoreOptions.TableName` 必须是 `schema.table`，例如 `dbo.IndustrialDataHistory`。连接字符串中的数据库和指定的 schema 必须已经存在；`InitializeAsync` 只负责创建历史表及索引。

Windows 上位机优先使用 Windows 身份验证，避免在配置文件中保存 SQL 登录密码：

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

`TrustServerCertificate=True` 适合本机联调。生产环境应部署可信证书并验证服务器身份。

## 最小可运行示例

下面的控制台程序展示 Options、缓冲记录、查询和释放。请先创建 `UpperComputerDb` 数据库，并确保当前 Windows 用户有建表和读写权限。

```csharp
using System;
using System.Threading;
using System.Threading.Tasks;
using IndustrialCommSdk.Abstractions;
using IndustrialCommSdk.Storage;

internal static class Program
{
    private static void Main()
    {
        RunAsync().GetAwaiter().GetResult();
    }

    private static async Task RunAsync()
    {
        var options = new SqlServerDataStoreOptions
        {
            ConnectionString =
                "Server=localhost;Database=UpperComputerDb;Integrated Security=True;" +
                "Encrypt=True;TrustServerCertificate=True;",
            TableName = "dbo.IndustrialDataHistory",
            CommandTimeoutSeconds = 15,
        };

        using (var recorder = new BufferedIndustrialDataRecorder(
            new SqlServerIndustrialDataStore(options),
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
        using (var queryStore = new SqlServerIndustrialDataStore(options))
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
