# IndustrialCommSdk 零基础入门指南

## 1. 先选择引用方式

- 只连接一种 PLC：引用对应协议程序集，直接创建客户端。
- 配置驱动、多设备运行：引用聚合程序集 `IndustrialCommSdk`，使用 `IndustrialSdk`。
- 只做 MES JSON：引用 `IndustrialCommSdk.Mes.Http`。
- 只做 TCP/Socket：引用 `IndustrialCommSdk.Transport`。

所有项目当前目标框架都是 `net472`。

## 2. 第一个 Modbus TCP 程序

项目引用 `IndustrialCommSdk.Protocols.Modbus`，然后创建客户端：

```csharp
using IndustrialCommSdk;
using IndustrialCommSdk.Protocols.Modbus;

var options = new ModbusTcpClientOptions
{
    DeviceId = "plc1",
    Host = "192.168.1.10",
    Port = 502,
    SlaveId = 1,
    DeviceProfile = ModbusDeviceProfiles.InovanceEasyPlc,
    ConnectTimeoutMilliseconds = 3000,
    OperationTimeoutMilliseconds = 5000
};

using (var client = new ModbusTcpClient(options))
{
    await client.UseAsync(async connected =>
    {
        short speed = await connected.ReadInt16Async("D100");
        await connected.WriteAsync("D101", speed);
    });
}
```

`UseAsync` 会连接、执行委托并断开。需要长连接时可手动调用 `ConnectAsync` 和 `DisconnectAsync`。

## 3. 常见强类型读取

```csharp
bool running = await client.ReadBoolAsync("M10");
short int16 = await client.ReadInt16Async("D100");
int int32 = await client.ReadInt32Async("D200");
float real = await client.ReadFloatAsync("D300");
string title = await client.ReadStringAsync("D400", 12);
byte[] raw = await client.ReadByteArrayAsync("D500", 16);
```

地址格式由协议和设备 Profile 决定。数值异常时优先检查地址、数据类型、字节序和字序。

## 4. 其他协议

```csharp
var rtu = new ModbusRtuClient(new ModbusRtuClientOptions
{
    DeviceId = "rtu1",
    PortName = "COM3",
    BaudRate = 9600,
    SlaveId = 1
});

var s7 = new SiemensS7Client(new SiemensS7ClientOptions
{
    DeviceId = "s7-1",
    Host = "192.168.1.20",
    CpuType = S7.Net.CpuType.S71200,
    Rack = 0,
    Slot = 1
});

var mc = new MitsubishiMcClient(new MitsubishiMcClientOptions
{
    DeviceId = "mc-1",
    Host = "192.168.1.30",
    Port = 5000
});
```

OPC UA、MQTT 和 Redis 使用相同模式：创建对应 Options，再构造对应 Client。

## 5. 配置驱动运行

```csharp
var sdk = IndustrialSdk.CreateDefault();
var config = sdk.LoadConfiguration("Config/devices.json");
var validation = config.Validate("Config", sdk.Protocols);

if (!validation.IsValid)
{
    foreach (var error in validation.Errors)
        Console.WriteLine(error);
    return;
}

using (var host = sdk.CreateDeviceHost(config, "Config"))
{
    host.ValuesReceived += (sender, e) =>
    {
        foreach (var value in e.Values)
            Console.WriteLine(value.Address + " = " + value.Value);
    };

    await host.StartAsync();
    Console.ReadLine();
    await host.StopAsync();
}
```

配置中公共字段放在设备对象顶层，轮询、重连和操作超时放在 `runtime`，Host、端口、串口、CPU 等协议字段放在 `settings`。切换协议时必须换成该协议的 Settings 结构。

## 6. 排错顺序

### 连接超时

1. 确认电脑和设备 IP 在可路由网段。
2. 确认端口、串口参数、站号、Rack/Slot。
3. 检查防火墙、交换机和 PLC 服务是否启用。
4. 先用 WPF 或 WinForms Demo 做最小连接测试。

### 地址错误

- Modbus 地址会受设备 Profile 影响。
- S7 常用 `DB1.DBX0.0`、`DB1.DBW2`、`M0.0`。
- MC 地址常用 `D100`、`M10`。
- OPC UA 使用 NodeId；MQTT 使用 Topic；Redis 使用 Key。

### 能读不能写

检查 PLC 运行模式、写保护、用户权限、地址区域以及业务层写入限制。SDK 不会替代现场安全联锁。

## 7. 重要升级说明

当前版本不包含 `SimpleClient`、`IndustrialClientFactory` 或旧 JSON 自动迁移。旧代码应改为：

- 单协议：Options + 具体 Client 构造函数。
- 配置驱动：`IndustrialSdk.CreateDefault()` + 新 `runtime/settings` JSON。
