# `modbus-rtu` 最小示例

## 所需程序集

- 直接引用：`IndustrialCommSdk.Protocols.Modbus`
- 示例使用：`IndustrialCommSdk.Runtime` 的快捷扩展
- 自动传递：`IndustrialCommSdk.Abstractions`、`IndustrialCommSdk.Protocols.Common`、`NModbus4`

## 连接、读取和写入

```csharp
using System;
using System.IO.Ports;
using System.Threading.Tasks;
using IndustrialCommSdk.Protocols.Modbus;
using IndustrialCommSdk.Runtime;

public static class ModbusRtuExample
{
    public static async Task RunAsync()
    {
        var client = new ModbusRtuClient(new ModbusRtuClientOptions
        {
            DeviceId = "plc-modbus-rtu-1",
            PortName = "COM3",
            BaudRate = 9600,
            DataBits = 8,
            Parity = Parity.Even,
            StopBits = StopBits.One,
            SlaveId = 1,
            ReadTimeout = 3000,
            WriteTimeout = 3000,
            OperationTimeoutMilliseconds = 5000,
            Retries = 2,
            WaitToRetryMilliseconds = 100,
            DeviceProfile = ModbusDeviceProfiles.Generic,
        });

        await client.UseAsync(async connected =>
        {
            ushort current = await connected.ReadUInt16Async("HR0");
            Console.WriteLine("HR0 = " + current);

            // 仅在确认 C0 可安全写入后执行。
            await connected.WriteAsync("C0", true);
        });
    }
}
```

## 地址和串口参数

通用地址与 Modbus TCP 相同：`HR0`、`IR0`、`C0`、`DI0`，也可以使用 `40001`、`30001`、`00001`、`10001`。`IR` 和 `DI` 只读。

当前直接客户端要求 8 个数据位，站号范围为 1–247。波特率、校验位和停止位必须与总线上所有设备一致；常见组合是 `9600-8-E-1` 或 `9600-8-N-2`，但应以设备手册为准。

## 安全与现场提示

- 同一串口不能同时被多个程序占用。
- RS-485 应检查 A/B 极性、终端电阻、偏置电阻、接地和总线拓扑；超时不一定是软件故障。
- 写入前确认从站号，避免操作到同一总线上的其他设备。
- 串口链路通常没有认证能力，应限制物理接入和串口服务器的网络访问。

