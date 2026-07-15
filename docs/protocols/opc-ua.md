# `opc-ua` 最小示例

## 所需程序集

- 直接引用：`IndustrialCommSdk.Protocols.OpcUa`
- 示例使用：`IndustrialCommSdk.Runtime` 的快捷扩展
- 自动传递：`IndustrialCommSdk.Abstractions`、`OPCFoundation.NetStandard.Opc.Ua`

## 连接、读取和写入

```csharp
using System;
using System.Threading.Tasks;
using IndustrialCommSdk.Protocols.OpcUa;
using IndustrialCommSdk.Runtime;

public static class OpcUaExample
{
    public static async Task RunAsync()
    {
        var client = new OpcUaClient(new OpcUaClientOptions
        {
            DeviceId = "opcua-server-1",
            EndpointUrl = "opc.tcp://192.168.1.40:4840",
            Username = "operator",
            Password = "replace-from-secret-store",
            UseSecurity = false, // 仅限隔离的本地测试；生产环境应使用安全端点。
            AutoAcceptUntrustedCertificates = false,
            ConnectTimeoutMilliseconds = 10000,
            OperationTimeoutMilliseconds = 5000,
            SessionTimeoutMilliseconds = 60000,
        });

        await client.UseAsync(async connected =>
        {
            float temperature = await connected.ReadFloatAsync(
                "ns=2;s=Machine/Temperature");
            Console.WriteLine("Temperature = " + temperature);

            // 节点必须可写，且服务端类型必须为 Float。
            await connected.WriteAsync("ns=2;s=Machine/SetPoint", 42.0f);
        });
    }
}
```

## NodeId 和类型

地址必须是 OPC UA 标准 NodeId 字符串，例如：

- 字符串标识：`ns=2;s=Machine/Temperature`
- 数字标识：`ns=2;i=1001`
- GUID 或 ByteString 标识也由 OPC Foundation 的 `NodeId.Parse` 处理

命名空间索引 `ns=2` 可能随服务端配置变化；稳定集成时应核对服务端 NamespaceArray。写入时 SDK 会按所选 `DataType` 创建 CLR 值，必须与节点的实际 Built-in Type 一致，否则服务端通常返回 `Bad_TypeMismatch`。

## 安全提示

示例中的 `UseSecurity = false` 只适合隔离测试环境。生产环境应选择签名或签名加密端点，将客户端证书加入服务端信任列表，并保持 `AutoAcceptUntrustedCertificates = false`。账号密码不要写入源码或 `devices.json`，应从安全配置源注入，同时限制账号可浏览和可写的节点范围。

