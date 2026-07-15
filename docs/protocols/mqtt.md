# `mqtt` 最小示例

## 所需程序集

- 直接引用：`IndustrialCommSdk.Protocols.Mqtt`
- 示例使用：`IndustrialCommSdk.Runtime` 的快捷扩展
- 自动传递：`IndustrialCommSdk.Abstractions`、`IndustrialCommSdk.Protocols.Common`、`MQTTnet`

## 连接、发布和读取

```csharp
using System;
using System.Threading;
using System.Threading.Tasks;
using IndustrialCommSdk.Protocols.Mqtt;
using IndustrialCommSdk.Runtime;

public static class MqttExample
{
    public static async Task RunAsync()
    {
        var client = new MqttClient(new MqttClientOptions
        {
            DeviceId = "mqtt-line-1",
            Host = "192.168.1.50",
            Port = 1883,
            ClientId = "line-1-sdk-client",
            Username = "device-user",
            Password = "replace-from-secret-store",
            UseTls = false, // 仅限隔离的本地测试。
            QualityOfService = 1,
            Retain = false,
            ConnectTimeoutMilliseconds = 5000,
            OperationTimeoutMilliseconds = 5000,
        });

        using (var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10)))
        {
            await client.UseAsync(async connected =>
            {
                // WriteAsync 将值发布到 Topic。
                await connected.WriteAsync(
                    "factory/line1/command", "start", timeout.Token);

                // ReadStringAsync 返回缓存的最新消息；没有缓存时会订阅并等待下一条消息。
                string status = await connected.ReadStringAsync(
                    "factory/line1/status", 256, timeout.Token);
                Console.WriteLine("Status = " + status);
            }, timeout.Token);
        }
    }
}
```

## Topic 和载荷

工业地址直接映射为 MQTT Topic。写入会发布 UTF-8 文本载荷；读取会把收到的 UTF-8 文本按请求的数据类型转换。`QualityOfService` 只能是 0、1 或 2。

当前读取缓存和等待器按完整 Topic 精确匹配，因此示例应使用 `factory/line1/status` 这样的确定 Topic，不要把 `+` 或 `#` 通配订阅当作单值读取地址。没有缓存且没有新消息时，读取会一直等待到请求超时或取消。

## 安全提示

生产环境应启用 TLS、Broker 认证和按 Topic 划分的 ACL，并使用唯一 `ClientId`。账号密码不要写入源码。谨慎启用 `Retain`：保留的控制命令可能在设备重连后再次被新订阅者收到；控制 Topic 通常应保持 `Retain = false`，状态 Topic 是否保留则按业务设计决定。
