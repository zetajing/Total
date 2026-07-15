# `modbus-tcp` 最小示例

## 所需程序集

- 直接引用：`IndustrialCommSdk.Protocols.Modbus`
- 示例使用：`IndustrialCommSdk.Runtime` 的快捷扩展
- 自动传递：`IndustrialCommSdk.Abstractions`、`IndustrialCommSdk.Protocols.Common`、`NModbus4`

## 连接、读取和写入

```csharp
using System;
using System.Threading.Tasks;
using IndustrialCommSdk.Protocols.Modbus;
using IndustrialCommSdk.Runtime;

public static class ModbusTcpExample
{
    public static async Task RunAsync()
    {
        var client = new ModbusTcpClient(new ModbusTcpClientOptions
        {
            DeviceId = "plc-modbus-tcp-1",
            Host = "192.168.1.10",
            Port = 502,
            SlaveId = 1,
            DeviceProfile = ModbusDeviceProfiles.Generic,
            ConnectTimeoutMilliseconds = 3000,
            OperationTimeoutMilliseconds = 5000,
        });

        await client.UseAsync(async connected =>
        {
            ushort current = await connected.ReadUInt16Async("HR0");
            Console.WriteLine("HR0 = " + current);

            // 仅在确认 HR1 可安全写入后执行。
            await connected.WriteAsync("HR1", (ushort)42);
        });
    }
}
```

## 地址和类型

`ModbusDeviceProfiles.Generic` 支持两套写法：

- 零基地址：`HR0`、`IR0`、`C0`、`DI0`。
- 一基引用地址：`40001`、`30001`、`00001`、`10001`。

`HR` 是保持寄存器，`IR` 是输入寄存器，`C` 是线圈，`DI` 是离散输入。`IR` 和 `DI` 只能读取。不要把设备手册中的 `40001` 不经确认直接当成偏移 `40001`；在通用 Profile 中它等价于 `HR0`。

多寄存器值的字序取决于设备。通用 Profile 不交换字序；汇川 EasyPLC 或三菱 Modbus 映射应分别选择 `ModbusDeviceProfiles.InovanceEasyPlc` 或 `ModbusDeviceProfiles.MitsubishiModbusTcp`，并使用对应品牌地址。

## 安全提示

Modbus TCP 本身没有认证和加密。生产环境应通过工业防火墙、VLAN 或白名单限制 502 端口，并禁止从办公网或互联网直接访问 PLC。`SlaveId` 当前有效范围为 1–247。任何写入都应先在停机或仿真环境验证。

