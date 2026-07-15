# `mitsubishi-mc` 最小示例

## 所需程序集

- 直接引用：`IndustrialCommSdk.Protocols.Mc`
- 示例使用：`IndustrialCommSdk.Runtime` 的快捷扩展
- 自动传递：`IndustrialCommSdk.Abstractions`、`IndustrialCommSdk.Transport`、`IndustrialCommSdk.Protocols.Common`

## 连接、读取和写入

```csharp
using System;
using System.Threading.Tasks;
using IndustrialCommSdk.Protocols.Mc;
using IndustrialCommSdk.Runtime;

public static class MitsubishiMcExample
{
    public static async Task RunAsync()
    {
        var client = new MitsubishiMcClient(new MitsubishiMcClientOptions
        {
            DeviceId = "plc-mc-1",
            Host = "192.168.1.30",
            Port = 5000,
            SendTimeoutMilliseconds = 3000,
            ReceiveTimeoutMilliseconds = 5000,
            OperationTimeoutMilliseconds = 5000,
        });

        await client.UseAsync(async connected =>
        {
            ushort current = await connected.ReadUInt16Async("D100");
            Console.WriteLine("D100 = " + current);

            // 仅在确认 D101 可安全写入后执行。
            await connected.WriteAsync("D101", (ushort)42);
        });
    }
}
```

## 地址和 PLC 设置

当前客户端使用 MC 协议二进制 3E 帧，支持的字设备包括 `D`、`W`、`R`、`SD`、`Z`、`ZR`、`TN`、`SN`、`CN`，位设备包括 `M`、`X`、`Y`、`L`、`SS`。

`W`、`X`、`Y` 的设备号按十六进制解析，例如 `X1A`；`D100`、`M100`、`ZR100` 等按十进制解析。位设备应搭配 `bool`，字设备则应搭配实际占用字数的数据类型。

PLC 侧需要启用与示例一致的 MC/SLMP 二进制 3E TCP 监听端口。当前实现固定使用网络号 `0x00`、PC 号 `0xFF`、目标 I/O `0x03FF` 和目标站号 `0x00`；需要经过多层网络或指定其他站号的场景暂不适用。

## 安全提示

MC TCP 通常不提供传输加密。应限制监听端口的来源地址、隔离控制网络，并在写入前核对 PLC 设备区和运行状态。不要直接把工程软件中显示的十六进制地址按十进制填写。

