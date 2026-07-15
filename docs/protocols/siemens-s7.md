# `siemens-s7` 最小示例

## 所需程序集

- 直接引用：`IndustrialCommSdk.Protocols.S7`
- 示例使用：`IndustrialCommSdk.Runtime` 的快捷扩展
- 自动传递：`IndustrialCommSdk.Abstractions`、`S7netplus`

## 连接、读取和写入

```csharp
using System;
using System.Threading.Tasks;
using IndustrialCommSdk.Protocols.S7;
using IndustrialCommSdk.Runtime;
using S7.Net;

public static class SiemensS7Example
{
    public static async Task RunAsync()
    {
        var client = new SiemensS7Client(new SiemensS7ClientOptions
        {
            DeviceId = "plc-s7-1",
            Host = "192.168.1.20",
            CpuType = CpuType.S71200,
            Rack = 0,
            Slot = 1,
            AutoReconnect = true,
            ConnectTimeoutMilliseconds = 5000,
            OperationTimeoutMilliseconds = 5000,
        });

        await client.UseAsync(async connected =>
        {
            ushort current = await connected.ReadUInt16Async("DB1.DBW0");
            Console.WriteLine("DB1.DBW0 = " + current);

            // 仅在确认 DB1.DBW2 可安全写入后执行。
            await connected.WriteAsync("DB1.DBW2", (ushort)42);
        });
    }
}
```

## 地址和 PLC 设置

常用地址示例：

- DB 区：`DB1.DBX0.0`、`DB1.DBB0`、`DB1.DBW0`、`DB1.DBD2`、`DB1.DBL4`。
- M/I/Q 区：`MX0.0`、`MW0`、`IX0.0`、`QX0.0`。

位地址必须带 `0–7` 的 bit 索引。示例中的 `DBW` 地址应搭配 16 位类型；32 位值通常使用 `DBD`，双精度值使用 `DBL`。字符串和字节数组读取还需要显式长度。

S7-1200/1500 使用绝对 DB 地址时，通常需要在 TIA Portal 中关闭对应 DB 的优化块访问，并允许所需的 PUT/GET 访问。不同 CPU 的 Rack/Slot 不同，必须按实际硬件设置。

## 安全提示

不要为了连接方便而开放所有 PLC 访问权限。生产环境应限制 TCP 102 的来源，使用隔离控制网，并只开放确实需要的 DB 和写权限。`AutoReconnect` 会在通信失败后重试一次，因此业务侧写入必须考虑幂等性。

