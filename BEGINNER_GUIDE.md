# IndustrialCommSdk 零基础入门指南

这份指南面向第一次接触本 SDK 的开发者。先不用理解所有工业协议，只要知道设备的协议、IP 地址和要操作的数据地址，就可以开始。

## 1. 先理解三个词

### 客户端

客户端就是你的程序与 PLC 或其他设备之间的“通信工具”。不同协议使用不同客户端，但连接、读取和写入的方式基本一致。

### 协议

协议是程序与设备约定的通信规则。必须选择设备实际启用的协议，不能随意选择。

| 设备或场景 | 常用入口 |
| --- | --- |
| 支持 Modbus TCP 的 PLC 或仪表 | `SimpleClient.ModbusTcp(...)` |
| 通过串口连接的 Modbus 设备 | `SimpleClient.ModbusRtu(...)` |
| Siemens S7-1200/1500 | `SimpleClient.S7(...)` |
| Mitsubishi PLC 的 MC 3E 协议 | `SimpleClient.Mc(...)` |

如果不知道设备使用什么协议，请先查看设备说明书或询问 PLC 工程师。

### 地址

地址表示设备中数据存放的位置，例如：

| 协议 | 地址示例 | 大致含义 |
| --- | --- | --- |
| 通用 Modbus | `HR0` | 第 0 个保持寄存器 |
| 汇川等厂商映射 | `D100` | D 数据区第 100 个位置 |
| Siemens S7 | `DB1.DBW0` | DB1 数据块中从 0 开始的一个字 |
| Mitsubishi MC | `D100` | D 数据区第 100 个位置 |

地址格式由设备协议和 PLC 程序决定。示例地址不一定能直接用于你的设备。

## 2. 第一个程序：读取一个 Modbus 数据

下面假设：

- PLC 的 IP 是 `192.168.1.10`
- PLC 已启用 Modbus TCP，端口是默认的 `502`
- 站号是默认的 `1`
- 要读取的地址是 `HR0`
- 该地址保存一个 16 位有符号整数，即 C# 的 `short`

```csharp
using System;
using System.Threading.Tasks;
using IndustrialCommSdk;

class Program
{
    static async Task Main()
    {
        await SimpleClient.ModbusTcp("192.168.1.10").UseAsync(async client =>
        {
            short value = await client.ReadAsync<short>("HR0");
            Console.WriteLine($"HR0 当前值：{value}");
        });
    }
}
```

`UseAsync` 会自动完成连接、断开和释放。即使读取时发生异常，它也会尝试清理连接，因此新项目优先使用这种写法。

## 3. 写入一个数据

```csharp
await SimpleClient.ModbusTcp("192.168.1.10").UseAsync(async client =>
{
    await client.WriteAsync("HR0", (short)100);
});
```

写入会真实改变设备数据。第一次测试前，应向设备负责人确认地址可写，并避免操作启动、停止、速度和安全联锁等关键点位。

## 4. 读取值时该选什么类型

`ReadAsync<T>` 中的 `T` 是希望得到的 C# 类型。它必须与 PLC 中的数据定义一致。

| PLC 数据含义 | C# 类型 | 示例 |
| --- | --- | --- |
| 开关量 | `bool` | `ReadAsync<bool>("M10")` |
| 16 位有符号整数 | `short` | `ReadAsync<short>("HR0")` |
| 16 位无符号整数 | `ushort` | `ReadAsync<ushort>("HR0")` |
| 32 位有符号整数 | `int` | `ReadAsync<int>("D100")` |
| 单精度浮点数 | `float` | `ReadAsync<float>("DB1.DBD0")` |
| 双精度浮点数 | `double` | `ReadAsync<double>("DB1.DBD0")` |

如果读取成功但数值明显不对，优先检查地址、数据类型、字节序和设备 Profile，而不是直接修改结果。

## 5. 不同协议的最小示例

### Modbus TCP

```csharp
await SimpleClient.ModbusTcp(
    "192.168.1.10",
    port: 502,
    slaveId: 1).UseAsync(async client =>
{
    short value = await client.ReadAsync<short>("HR0");
});
```

### Modbus RTU

```csharp
await SimpleClient.ModbusRtu(
    "COM3",
    baudRate: 9600,
    slaveId: 1).UseAsync(async client =>
{
    short value = await client.ReadAsync<short>("HR0");
});
```

串口参数除了波特率外，还包括数据位、校验位和停止位。`SimpleClient` 默认使用 8 数据位、偶校验、1 停止位；设备设置不同时，应使用底层工厂配置。

### Siemens S7

```csharp
await SimpleClient.S7(
    "192.168.1.20",
    rack: 0,
    slot: 1).UseAsync(async client =>
{
    short value = await client.ReadAsync<short>("DB1.DBW0");
});
```

### Mitsubishi MC

```csharp
await SimpleClient.Mc(
    "192.168.1.30",
    port: 5000).UseAsync(async client =>
{
    short value = await client.ReadAsync<short>("D100");
});
```

## 6. 手动管理连接

需要连续执行多次操作时，也可以显式管理连接：

```csharp
using (var client = SimpleClient.ModbusTcp("192.168.1.10"))
{
    await client.ConnectAsync();

    short first = await client.ReadAsync<short>("HR0");
    short second = await client.ReadAsync<short>("HR1");

    await client.DisconnectAsync();
}
```

一般业务代码仍推荐 `UseAsync`，它更不容易遗漏资源清理。

## 7. 常见问题排查

### 连接超时

依次确认：

1. 电脑和设备是否在可互通的网络中
2. IP 地址和端口是否正确
3. PLC 是否已启用相应协议服务
4. Windows 防火墙、交换机或安全软件是否拦截端口
5. Modbus 站号、S7 rack/slot 是否与设备一致

### 地址错误

不要凭示例猜测地址。以 PLC 点表、程序或设备手册为准，并确认当前客户端采用的地址格式。

### 数值不正确

常见原因包括：

- 将 16 位数据当成 32 位数据读取
- 将整数当成浮点数读取
- 厂商地址映射不匹配
- 字节序或字序与设备不同

### 读取正常但写入失败

确认地址是否允许写入、设备是否处于可写状态，以及 PLC 程序是否立即覆盖了写入值。

## 8. 接下来学什么

完成单点读写后，再按这个顺序学习：

1. README 中的“地址与厂商 Profile”
2. 批量读写
3. JSON 点位表和配置驱动运行
4. 自动轮询与断线重连
5. 诊断、日志和历史数据

底层的 `ReadRequest`、`WriteRequest`、`CancellationToken` 和协议 Options 适合有定制需求时再学习，第一次使用不必全部掌握。
