# IndustrialCommSdk

统一工业通讯 SDK，当前目标运行时为 `net472`。  
当前工程包含 SDK 类库、测试项目，以及一个可直接调试的 WPF 示例程序。

## 当前能力

- `Modbus TCP`
- `TCP Socket` 传输桥接骨架
- `Siemens S7` 基础客户端骨架
- `Mitsubishi MC 3E` 基础客户端骨架
- 内置轮询订阅调度
- 批量读写入口与协议级批量扩展点
- Modbus 连续地址合并批量读取
- Modbus / S7 真异步读写与连接
- 统一读写请求/返回模型

## 解决方案结构

- `IndustrialCommSdk`
  SDK 主类库。

- `IndustrialCommSdk.Tests`
  NUnit 测试项目，用来验证地址解析、数据编解码、轮询调度等逻辑。

- `IndustrialCommDemo`
  WPF 示例程序，已经包含在 [Total.sln](C:/Users/75881/Documents/Total/Total.sln) 中，可直接作为启动项目调试。

## 快速开始

```powershell
dotnet restore
dotnet build Total.sln
dotnet test Total.sln
```

## 3 分钟跑通

推荐先从 `Modbus TCP` 开始验证：

1. 打开 [Total.sln](C:/Users/75881/Documents/Total/Total.sln)
2. 启动 [IndustrialCommDemo](C:/Users/75881/Documents/Total/IndustrialCommDemo/IndustrialCommDemo.csproj)
3. 在 `Modbus TCP` 页填写：
   - Host: `127.0.0.1`
   - Port: `502`
   - Slave ID: `1`
   - Address: `D100`
4. 点击“连接”
5. 点击“读取”验证是否能拿到值

如果只是想看 SDK 调用方式，可以直接参考下面的最小代码示例。

## 最小代码示例

### 基础请求模型写法

```csharp
using System.Threading;
using IndustrialCommSdk;
using IndustrialCommSdk.Abstractions;
using IndustrialCommSdk.Protocols.Modbus;

var client = IndustrialClientFactory.CreateModbus(new ModbusTcpClientOptions
{
    DeviceId = "modbus-1",
    Host = "127.0.0.1",
    Port = 502,
    SlaveId = 1
});

await client.ConnectAsync(CancellationToken.None);

var result = await client.ReadAsync(
    new ReadRequest("modbus-1", "D100", DataType.Int16, 1),
    CancellationToken.None);

await client.WriteAsync(
    new WriteRequest("modbus-1", "D100", DataType.Int16, (short)123, 1),
    CancellationToken.None);

await client.DisconnectAsync(CancellationToken.None);
```

### 快捷 API 写法

新增的快捷扩展方法适合“先跑通、少写样板代码”的场景：

```csharp
using IndustrialCommSdk;
using IndustrialCommSdk.Protocols.Modbus;

var client = IndustrialClientFactory.CreateModbus(new ModbusTcpClientOptions
{
    DeviceId = "modbus-1",
    Host = "127.0.0.1"
});

await client.ConnectAsync();

short d100 = await client.ReadInt16Async("D100");
float d200 = await client.ReadFloatAsync("D200");
bool m10 = await client.ReadBoolAsync("M10");

await client.WriteAsync("M10", true);
await client.WriteAsync("D100", (short)123);
await client.WriteStringAsync("D300", "ABC", 2);

await client.DisconnectAsync();
```

### 通用快捷 API

如果你想保留通用性，但又不想手动拼 `ReadRequest / WriteRequest`，可以用：

```csharp
int value = await client.ReadValueAsync<int>("D100", DataType.Int32);
await client.WriteValueAsync("D100", DataType.Int32, 456);
```

### 订阅示例

当前项目里的订阅是“轮询订阅”：

- 不是 PLC 主动推送
- 而是 SDK 按固定时间间隔持续读取

单地址订阅示例：

```csharp
using System;
using IndustrialCommSdk.Abstractions;

var subscriptionId = await client.SubscribeAsync(
    new SubscriptionRequest(
        "modbus-sub-1",
        client.DeviceId,
        new[]
        {
            new ReadRequest(client.DeviceId, "D100", DataType.Int16, 1)
        },
        TimeSpan.FromSeconds(1),
        false),
    (sender, e) =>
    {
        foreach (var item in e.Values)
        {
            Console.WriteLine($"{item.Address} = {item.Value} | Quality={item.Quality}");
        }
    },
    default);

await client.UnsubscribeAsync(subscriptionId, default);
```

多地址订阅示例：

```csharp
var subscriptionId = await client.SubscribeAsync(
    new SubscriptionRequest(
        "modbus-sub-2",
        client.DeviceId,
        new[]
        {
            new ReadRequest(client.DeviceId, "D100", DataType.Int16, 1),
            new ReadRequest(client.DeviceId, "D102", DataType.Int16, 1),
            new ReadRequest(client.DeviceId, "D104", DataType.Int16, 1),
        },
        TimeSpan.FromMilliseconds(500),
        false),
    (sender, e) =>
    {
        foreach (var item in e.Values)
        {
            Console.WriteLine($"{item.Address} = {item.Value} | Quality={item.Quality}");
        }
    },
    default);
```

在 Demo 中，`Modbus TCP` 页的地址框也支持一次填写多个地址，分隔方式可以是：

- 逗号：`D100, D102, D104`
- 分号：`D100; D102; D104`
- 换行

输入多个地址后：

- 点击“读取”会自动走批量读取
- 点击“订阅”会自动走批量轮询订阅
- 点击“写入”时支持两种模式：
  - 只填 1 个值：同一个值广播到多个地址
  - 填写与地址数相同的多个值：按顺序逐项写入

多个连续地址在订阅和批量读取时会自动走地址合并优化，减少网络往返次数。

注意：

- 多地址模式下，位地址和寄存器地址不要混用
- 位地址会自动锁定为 `Bool`
- 写入值支持逗号、分号、换行分隔

## 快捷 API 一览

- 读取
  - `ReadBoolAsync`
  - `ReadInt16Async`
  - `ReadUInt16Async`
  - `ReadInt32Async`
  - `ReadUInt32Async`
  - `ReadFloatAsync`
  - `ReadDoubleAsync`
  - `ReadStringAsync`
  - `ReadByteArrayAsync`
  - `ReadValueAsync<T>`

- 写入
  - `WriteAsync(address, bool)`
  - `WriteAsync(address, short)`
  - `WriteAsync(address, ushort)`
  - `WriteAsync(address, int)`
  - `WriteAsync(address, uint)`
  - `WriteAsync(address, float)`
  - `WriteAsync(address, double)`
  - `WriteStringAsync`
  - `WriteByteArrayAsync`
  - `WriteValueAsync`

## 调试 Demo

### Visual Studio

1. 打开 [Total.sln](C:/Users/75881/Documents/Total/Total.sln)
2. 在解决方案资源管理器中右键 `IndustrialCommDemo`
3. 选择“设为启动项目”
4. 直接按 `F5` 调试

### 命令行构建

```powershell
dotnet build .\IndustrialCommDemo\IndustrialCommDemo.csproj
```

构建成功后可执行文件位于：

- [IndustrialCommDemo.exe](C:/Users/75881/Documents/Total/IndustrialCommDemo/bin/Debug/net472/IndustrialCommDemo.exe)

## Modbus TCP 现状

当前 `Modbus TCP` 已内置两个设备 profile：

- 汇川 `EasyPLC`
- 三菱 `Modbus TCP`

### 最近一轮优化

- `IndustrialClientBase` 的 `ReadManyAsync / WriteManyAsync` 已调整为整批操作只获取一次锁，避免逐条请求反复加锁/解锁。
- 基类新增 `ReadManyCoreAsync / WriteManyCoreAsync` 虚方法，协议子类可以覆写实现自己的批量优化逻辑。
- `ModbusTcpClient` 已覆写 `ReadManyCoreAsync`，会按 `Area` 分组、按地址排序，并把连续地址或间隙不超过 `16` 个寄存器的请求合并为一次读取。
- 合并读取完成后，会再按偏移量拆分结果，映射回各自的 `DataValue`，适合订阅和批量轮询场景。
- `ModbusTcpClient` 已改用 nmodbus 原生 async API，如 `ReadHoldingRegistersAsync / WriteMultipleRegistersAsync` 等，不再依赖 `Task.Run` 伪异步。
- `ModbusTcpClient` 的连接超时通过 `Task.WhenAny` 配合 `CancellationToken` 控制。
- `SiemensS7Client` 也已切换到 `S7netplus 0.20.0` 提供的异步 API：`OpenAsync / ReadAsync / WriteAsync`。

### S7 DB 块增强

现在 `SiemensS7Client` 额外支持两类更贴近现场使用习惯的能力：

- 直接按“PLC DB 布局对应的 C# 类”整块读写
- 在 Demo 的 `S7` 页里粘贴 `DINT %DB200.DBD6`、`LREAL P#DB200.DBX20.0` 这类文本后，自动选择对应数据类型

类映射 DB 示例：

```csharp
public sealed class Db200Model
{
    public byte ByteValue { get; set; }
    public int DintValue { get; set; }
    public double LrealValue { get; set; }
}

var client = IndustrialClientFactory.CreateSiemensS7(new SiemensS7ClientOptions
{
    DeviceId = "s7-1",
    Host = "192.168.0.10",
    Rack = 0,
    Slot = 1
});

await client.ConnectAsync(CancellationToken.None);

var s7Client = (IndustrialCommSdk.Protocols.S7.SiemensS7Client)client;
var db200 = await s7Client.ReadDbClassAsync<Db200Model>(200);

db200.ByteValue = 12;
await s7Client.WriteDbClassAsync(db200, 200);
```

### 这轮优化的直接收益

- 批量轮询时锁竞争更少
- 连续 Modbus 地址的网络往返次数显著下降
- UI 线程和高频订阅场景下阻塞更少
- 后续其他协议可以沿用同一套批量扩展模式

### 批量读日志

当前 `ModbusTcpClient` 在批量读取时会输出两类日志：

- 每个合并批次的明细
  - 原始请求数
  - 合并后的单次调用区段
  - 覆盖地址列表
  - 单批耗时

- 整轮批量读取汇总
  - `OriginalRequests`
  - `MergedCalls`
  - `SavedCalls`
  - 总耗时

这部分日志适合直接在 Demo 日志窗口中观察批量合并效果。

### 已支持的汇川地址语义

- 位地址：`X / Y / M / S / B`
- 寄存器地址：`D / R`

### 已支持的三菱 Modbus 地址语义

- 位地址：`X / Y / M / L`
- 寄存器地址：`D / R / W`

说明：

- 三菱 profile 中，`X / Y / W` 按十六进制编号解析，例如 `X10`、`Y20`、`W1A`
- 当前三菱 profile 的 32 位寄存器顺序按“标准不交换”处理

### 当前数据类型规则

- `X / Y / M / S / B` 固定按 `Bool`
- `D / R` 支持 `Int16 / UInt16 / Int32 / UInt32 / Float / Double / String / ByteArray`

### 32 位规则

汇川 `EasyPLC` 当前按“低字在前”处理 `Int32 / UInt32 / Float / Double`。

## 汇川 Profile 抽象

为了后续扩展其他品牌，当前已经把品牌规则收敛为独立类：

- [InovanceEasyPlcModbusProfile.cs](C:/Users/75881/Documents/Total/IndustrialCommSdk/Protocols/Modbus/InovanceEasyPlcModbusProfile.cs)
- [MitsubishiModbusTcpProfile.cs](C:/Users/75881/Documents/Total/IndustrialCommSdk/Protocols/Modbus/MitsubishiModbusTcpProfile.cs)

统一接口在：

- [ModbusDeviceProfile.cs](C:/Users/75881/Documents/Total/IndustrialCommSdk/Protocols/Modbus/ModbusDeviceProfile.cs)

这些 profile 当前负责：

- 各品牌地址解析
- 各品牌寄存器顺序处理
- 默认示例地址和默认调试地址

## 后续增加其他品牌的方式

新增一个品牌时，建议直接仿照汇川实现一个新的 `IModbusDeviceProfile`：

1. 新建一个 `XXXModbusProfile` 类
2. 实现 `ParseAddress`
3. 实现 `NormalizeRegistersForRead`
4. 实现 `NormalizeRegistersForWrite`
5. 注册到 `ModbusDeviceProfiles`

这样可以把“地址映射”和“字序差异”都隔离在品牌类内部，不会污染 `ModbusTcpClient` 主流程。

## 当前关键文件

- [IndustrialClientBase.cs](C:/Users/75881/Documents/Total/IndustrialCommSdk/Internal/IndustrialClientBase.cs)
- [ModbusTcpClient.cs](C:/Users/75881/Documents/Total/IndustrialCommSdk/Protocols/Modbus/ModbusTcpClient.cs)
- [ModbusAddressing.cs](C:/Users/75881/Documents/Total/IndustrialCommSdk/Protocols/Modbus/ModbusAddressing.cs)
- [InovanceEasyPlcModbusProfile.cs](C:/Users/75881/Documents/Total/IndustrialCommSdk/Protocols/Modbus/InovanceEasyPlcModbusProfile.cs)
- [SiemensS7Client.cs](C:/Users/75881/Documents/Total/IndustrialCommSdk/Protocols/S7/SiemensS7Client.cs)
- [MainWindow.xaml.cs](C:/Users/75881/Documents/Total/IndustrialCommDemo/MainWindow.xaml.cs)

## 验证

当前本地验证通过：

- `dotnet test Total.sln`
- `dotnet test .\IndustrialCommSdk.Tests\IndustrialCommSdk.Tests.csproj`
- `dotnet build .\IndustrialCommDemo\IndustrialCommDemo.csproj -p:BuildProjectReferences=false`

最近一次测试结果：

- `19 / 19` 通过
- `0` 失败
