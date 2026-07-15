# `redis` 最小示例

## 所需程序集

- 直接引用：`IndustrialCommSdk.Protocols.Redis`
- 示例使用：`IndustrialCommSdk.Runtime` 的快捷扩展
- 自动传递：`IndustrialCommSdk.Abstractions`、`IndustrialCommSdk.Protocols.Common`、`StackExchange.Redis`

## 连接、读取和写入

```csharp
using System;
using System.Threading.Tasks;
using IndustrialCommSdk.Protocols.Redis;
using IndustrialCommSdk.Runtime;

public static class RedisExample
{
    public static async Task RunAsync()
    {
        var client = new RedisClient(new RedisClientOptions
        {
            DeviceId = "redis-runtime-1",
            Host = "192.168.1.60",
            Port = 6379,
            Username = "industrial-app",
            Password = "replace-from-secret-store",
            Database = 0,
            Ssl = false, // 仅限隔离的本地测试。
            ConnectTimeoutMilliseconds = 5000,
            OperationTimeoutMilliseconds = 5000,
        });

        await client.UseAsync(async connected =>
        {
            await connected.WriteAsync("factory:line1:mode", "auto");
            string mode = await connected.ReadAsync<string>("factory:line1:mode");
            Console.WriteLine("Mode = " + mode);
        });
    }
}
```

## Key 和值

工业地址直接映射为 Redis Key。当前模块使用 Redis String 的 `GET`/`SET`，字符串和数值以 UTF-8 文本编码，字节数组按原始字节保存；它不是 Redis Hash、List 或 Stream 的通用封装。

Redis 模块与 SQL Server/MySQL 历史存储彻底独立。它不实现 `IIndustrialHistoryStore`，不提供关系型分页、汇总或保留期清理；需要历史追溯时，应选用 `IndustrialCommSdk.Storage` 或 `IndustrialCommSdk.Storage.MySql`。

读取不存在的 Key 会返回 Bad 质量，强类型快捷读取随后抛出协议异常。Key 应包含业务命名空间，例如 `factory:line1:mode`，避免不同设备相互覆盖。批量读写会使用 Redis 的批量 String API。

## 安全提示

不要把 Redis 6379 端口暴露到互联网。生产环境应使用 TLS、ACL 最小权限、网络白名单和独立账号，密码从安全配置源注入。Redis 数据可能被其他应用同时修改；对控制命令或需要原子性的状态转换，应在业务层增加版本、锁或事务语义，而不是依赖一次普通 `SET`。
