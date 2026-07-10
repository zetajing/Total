# IndustrialCommSdk

统一工业通讯 SDK，当前目标运行时为 `net472`。  
当前工程包含 SDK 类库、日志组件，以及一个可直接调试的 WPF 示例程序。

## 当前能力

- `Modbus TCP / Modbus RTU（RS-485 / RS-232）`
- `TCP Socket` 传输桥接骨架
- `Siemens S7` 基础客户端骨架
- `Mitsubishi MC 3E` 基础客户端骨架
- 内置轮询订阅调度
- 批量读写入口与协议级批量扩展点
- Modbus 连续地址合并批量读取
- Modbus / S7 真异步读写与连接
- JSON 设备配置加载
- JSON / CSV 点位表加载
- 连接诊断测试
- 统一读写请求/返回模型
- 可选 SQL Server 历史数据存储（后台有界队列、批量写入、失败重试）
- P0/P1 可靠性优化：S7 生命周期、MC 地址元数据、批量超时、健康状态分类、按设备合并轮询

## P0/P1 可靠性更新

PR #1 已合并到 `master`，对应合并提交为 `1afa2eb44394361548f8fd3d313b7c939bed89ca`。这轮优化重点不是继续堆协议，而是先把 SDK 的稳定性边界打牢：

- `IndustrialClientBase`：统一单点/批量超时处理，校验请求 `DeviceId`，区分地址/数据错误与连接/传输故障，避免全 Bad 批量读取被误记为健康成功。
- `SiemensS7Client`：参考成熟 S7.NetPlus 使用方式，连接前释放旧实例，连接失败后清理临时连接，读写失败后保护底层连接状态，并加强 DB/bit 地址校验。
- `MitsubishiMcClient`：将设备代码、地址进制、位/字属性集中到元数据表，修正 `ZR` 地址进制规则，并补充 Host、Port、连接状态和地址范围校验。
- `PollingScheduler`：同一设备只保留一个后台 Worker，到期订阅合并去重读取，采用固定节拍减少漂移，并隔离订阅回调异常。

详细记录见 [docs/p0-p1-reliability-update.md](docs/p0-p1-reliability-update.md)。后续增加 Omron、OPC UA、EtherNet/IP 等协议前，应优先保持这些稳定性边界不被破坏。

## 解决方案结构

- `IndustrialCommSdk`
  SDK 主类库。

- `IndustrialCommDemo`
  WPF 示例程序，已经包含在 [Total.sln]中，可直接作为启动项目调试。

## 快速开始

```powershell
dotnet restore
dotnet build .\IndustrialCommSdk\IndustrialCommSdk.csproj
dotnet build .\Total.sln
```

## 3 分钟跑通

推荐先从 `Modbus TCP` 开始验证：

1. 打开 [Total.sln]
2. 启动 [IndustrialCommDemo]
3. 在 `Modbus TCP` 页填写：
   - Host: `127.0.0.1`
   - Port: `502`
   - Slave ID: `1`
   - Address: `D100`
4. 点击“连接”
5. 点击“读取”验证是否能拿到值

如果只是想看 SDK 调用方式，可以直接参考下面的最小代码示例。

### Modbus RTU / RS-485 实机验证

1. 在 Modbus 页将连接类型切换为 `Modbus RTU（串口）`
2. 选择串口号，并按设备手册设置波特率、数据位、校验位、停止位和从站 ID
3. 普通仪表、变频器、温控器等选择 `通用 Modbus` 地址类型
4. 输入 `HR0`、`IR0`、`40001` 等地址后连接并读取
5. 在日志窗口核对 `Modbus RTU TX` 和 `Modbus RTU RX` 原始十六进制帧

注意：RS-485 是物理接口，不代表设备一定使用 Modbus RTU。设备协议、站号、功能码和寄存器表必须以设备手册为准。

## 最小代码示例

### 读取 / 写入 / 批量

```csharp
using System.Collections.Generic;
using IndustrialCommSdk;

var client = IndustrialClientFactory.ModbusTcp("127.0.0.1");

await client.ConnectAsync();

int value = await client.ReadAsync<int>("D100");
await client.WriteAsync("D100", 123);

var values = await client.ReadManyAsync<int>("D100", "D101", "D102");

await client.WriteManyAsync(new Dictionary<string, int>
{
    ["D100"] = 100,
    ["D101"] = 101,
});

await client.DisconnectAsync();
```

自动连接、断开并释放客户端：

```csharp
await IndustrialClientFactory.ModbusTcp("127.0.0.1").UseAsync(async plc =>
{
    int value = await plc.ReadAsync<int>("D100");
    await plc.WriteAsync("D100", value + 1);
});
```

### 混合类型批量

同一批点位如果类型不同，用 `Tag` 描述地址和类型：

```csharp
using IndustrialCommSdk;

var speed = Tag.Int16("D100");
var running = Tag.Bool("M10");

var values = await client.ReadManyAsync(speed, running);
short s = values.Get(speed);
bool r = values.Get(running);

await client.WriteManyAsync(
    speed.WithValue((short)123),
    running.WithValue(true));
```

### 配置化部署

设备配置可以放到 JSON 文件，例如 `devices.json`：

```json
{
  "devices": [
    {
      "name": "plc1",
      "protocol": "modbus-tcp",
      "host": "192.168.1.10",
      "port": 502,
      "slaveId": 1,
      "deviceProfile": "inovance-easyplc",
      "pointsFile": "points/plc1.json",
      "enabled": true,
      "pollingIntervalMilliseconds": 1000,
      "reconnectDelayMilliseconds": 3000
    },
    {
      "name": "s7plc",
      "protocol": "siemens-s7",
      "host": "192.168.1.20",
      "cpuType": "S71200",
      "rack": 0,
      "slot": 1,
      "pointsFile": "points/s7plc.json"
    }
  ]
}
```

代码里直接按设备名创建：

```csharp
var plc = IndustrialClientFactory.FromConfig("devices.json", "plc1");
```

每台设备绑定自己的点位表。例如 `points/plc1.json`：

```json
{
  "tags": [
    { "name": "Speed", "address": "D100", "type": "Int16" },
    { "name": "Running", "address": "M10", "type": "Bool" },
    { "name": "Title", "address": "D200", "type": "String", "length": 12 }
  ]
}
```

或者用 CSV：

```csv
Name,Address,Type,Length
Speed,D100,Int16,1
Running,M10,Bool,1
Title,D200,String,12
```

加载后直接批量读取：

```csharp
var tags = TagTable.LoadForDevice("devices.json", "plc1");
var values = await plc.ReadManyAsync(tags.Tags);
```

需要进一步简化时，可直接打开 JSON 配置化设备，按点位名称读、写和批量读写：

```csharp
using (var device = IndustrialDeployment.Open("Config/devices.json", "plc1"))
{
    await device.ConnectAsync();

    short speed = await device.ReadAsync<short>("Speed");
    await device.WriteAsync("Run", true);
    var values = await device.ReadManyAsync();

    await device.WriteManyAsync(new Dictionary<string, object>
    {
        ["Speed"] = (short)120,
        ["Run"] = false,
    });

    await device.DisconnectAsync();
}
```

`enabled` 控制设备是否由 `IndustrialDeviceHost` 自动启动；`pollingIntervalMilliseconds` 启用按点位表批量轮询；`reconnectDelayMilliseconds` 指定断线后的重连检查周期。省略 `enabled` 时默认为启用，省略轮询周期时仅管理连接、不自动轮询。

新增设备时，复制一段设备配置并新建对应的点位 JSON；软件启动后会按 `pointsFile` 自动加载。

部署前可在不连接 PLC 的情况下校验全部设备配置、协议参数和点位表：

```csharp
using System.IO;
using IndustrialCommSdk;

var configPath = "Config/devices.json";
var config = IndustrialSdkConfig.Load(configPath);
var result = config.Validate(Path.GetDirectoryName(Path.GetFullPath(configPath)));

if (!result.IsValid)
{
    throw new InvalidOperationException(string.Join(Environment.NewLine, result.Errors));
}
```

Demo 的“JSON 配置”页会自动列出 `devices.json` 中的设备。修改 JSON 后依次点击“保存配置”和“校验配置”；校验通过后可直接测试连接或批量读取。

需要让多台设备随程序启动并持续运行时，使用 `IndustrialDeviceHost`：

```csharp
using (var host = IndustrialDeviceHost.Load("Config/devices.json"))
{
    host.ValuesReceived += (sender, args) =>
    {
        // args.DeviceName、args.Tags 和 args.Values 按索引对应。
    };

    await host.StartAsync();
    short speed = await host.Get("plc1").ReadAsync<short>("Speed");

    // 在应用关闭时停止轮询、断开设备并释放客户端。
    await host.StopAsync();
}
```

上线前可以先做连接诊断：

```csharp
var report = await plc.TestAsync();
Console.WriteLine(report);
```

### Modbus RTU 写法

```csharp
using IndustrialCommSdk;

var client = IndustrialClientFactory.ModbusRtu("COM3");

await client.ConnectAsync();

// HR0 是零基保持寄存器 0；40001 是同一地址的一基引用写法。
var value = await client.ReadAsync<ushort>("HR0");

await client.DisconnectAsync();
```

### 高级请求模型写法

如果需要完整控制 `DeviceId`、`DataType`、`Length`、`Timeout`，仍然可以使用底层请求模型：

```csharp
using System.Threading;
using IndustrialCommSdk;
using IndustrialCommSdk.Abstractions;
using IndustrialCommSdk.Protocols.Modbus;

var client = IndustrialClientFactory.CreateModbusTcp(new ModbusTcpClientOptions
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

### 订阅示例

当前项目里的订阅是“轮询订阅”：

- 不是 PLC 主动推送
- 而是 SDK 按固定时间间隔持续读取
- 同一设备共用一个后台 Worker，到期订阅会合并重复点位后统一读取
- 轮询采用固定节拍调度，避免“读取耗时 + Interval”导致周期持续漂移
- 订阅回调异常会被日志记录，不会终止设备轮询循环

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

## 原始 TCP Socket 收发

`TcpTransportClient` 同时支持定长协议和服务端主动推送：

```csharp
using IndustrialCommSdk.Transport;

using (var socket = new TcpTransportClient(new TcpTransportOptions
{
    Host = "192.168.1.20",
    Port = 9000,
    AutoReconnect = true,
}))
{
    await socket.ConnectAsync(cancellationToken);
    await socket.SendAsync(requestBytes, cancellationToken);

    // 已知响应长度时使用，超时覆盖整个报文的接收过程。
    byte[] response = await socket.ReceiveExactAsync(32, cancellationToken);

    // 长度未知或服务端主动推送时使用；一次返回最多 4096 字节。
    byte[] pushed = await socket.ReceiveAsync(4096, cancellationToken);
}
```

TCP 是字节流，`ReceiveAsync` 的一次返回不等于一个完整业务报文。JSON、行文本或自定义二进制协议仍应按各自的长度字段或分隔符处理拆包、粘包。

服务端接收后如果需要异步回复或写库，应订阅可等待的异步事件，避免使用 `async void`：

```csharp
var server = new TcpTransportServer(IPAddress.Any, 9000);
server.DataReceivedAsync += (sender, e) =>
    e.Session.SendAsync(e.Payload, CancellationToken.None);
await server.StartAsync(cancellationToken);
```

## SQL Server 历史数据（可选）

数据库能力是旁路功能，不参与 PLC 实时控制。SDK 使用后台有界队列把通信回调与 SQL Server 写入隔离：

- 数据库变慢或暂时断开时，不阻塞设备读取和轮询
- 自动创建 `dbo.IndustrialDataHistory` 历史表
- 按批次事务写入，短暂失败自动重试
- 默认关闭；不配置数据库时不影响原有 SDK 用法

Demo 的“数据库”页已经预填本机 Windows 身份验证连接字符串：

```text
Server=localhost;Database=UpperComputerDb;Integrated Security=True;Encrypt=True;TrustServerCertificate=True;
```

点击“测试并启用”后，Modbus / S7 / MC 的手动读取结果以及 Modbus 轮询结果会写入历史表。

Demo 数据库页还提供：实时流水、按设备和地址聚合的最新值、组合筛选与分页、质量统计、分批 CSV 导出、带数量确认的条件删除，以及 7/30/90/180 天保留期清理。SDK 的 `IIndustrialHistoryManagementStore` 提供分页、统计、筛选候选和最新值查询；`BufferedIndustrialDataRecorder.GetSnapshot()` 可读取队列、写入、丢弃和失败指标。

SDK 独立使用示例：

```csharp
using IndustrialCommSdk.Storage;

var store = new SqlServerIndustrialDataStore(new SqlServerDataStoreOptions
{
    ConnectionString = "Server=localhost;Database=UpperComputerDb;Integrated Security=True;Encrypt=True;TrustServerCertificate=True;",
    TableName = "dbo.IndustrialDataHistory"
});

using (var recorder = new BufferedIndustrialDataRecorder(store))
{
    await recorder.StartAsync(CancellationToken.None);

    var value = await client.ReadAsync(
        new ReadRequest(client.DeviceId, "D100", DataType.Int16),
        CancellationToken.None);

    recorder.TryRecord(client.Kind, client.DeviceId, new[] { value });
    await recorder.StopAsync(CancellationToken.None); // 停止时排空队列
}
```

查询最近记录：

```sql
SELECT TOP (100) *
FROM dbo.IndustrialDataHistory
ORDER BY [Timestamp] DESC;
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

## FA MES TCP 双向对接

SDK 提供 `IndustrialCommSdk.Mes.MesTcpClient`，兼容《设备与 MES 通讯文档（FA）》及 H101_ST0 参考程序的无分隔符长连接协议：

1. 设备主动连接 MES，默认端口为 `9312`。
2. 每次连接或重连成功后自动发送 `START ... STOP` 上线信息。
3. 同一连接接收 `FACHECK`，设备完成后发送 `FATRACK`，随后接收 `FANUM`。
4. 接收器按完整 JSON 对象边界处理 TCP 拆包和粘包；发送内容不追加 CRLF。

```csharp
using IndustrialCommSdk.Mes;

var mes = new MesTcpClient(new MesClientOptions
{
    Host = "127.0.0.1", // 联调时填写实际 MES 地址
    Port = 9312,
    DeviceNo = "001",
    DeviceName = "设备001",
    DeviceIp = "192.168.1.10",
    DeviceMac = "00-11-22-33-44-55",
});

mes.FaCheckReceived += (sender, e) =>
    Console.WriteLine($"{e.Message.Message.SerialNo}: {e.Message.Message.Result}");
mes.FaNumReceived += (sender, e) =>
    Console.WriteLine($"过站结果: {e.Message.Message.Result}");

await mes.ConnectAsync(CancellationToken.None);
await mes.SendTrackAsync(new FaTrackMessage
{
    Message = new FaTrackBody
    {
        Process = "FAFAL0290M0",
        SerialNo = "SN001",
        Number = "4",
        Parameters = new Dictionary<string, string> { ["test"] = "1" },
    },
}, CancellationToken.None);
```

Demo 的“MES”页可保存非敏感连接参数、查看连接/重连状态、手工发送上线信息和 `FATRACK`，并以颜色展示 `FACHECK`、`FANUM` 的 OK/NG 结果。生产地址不会自动连接；确认设备编号、网卡 IP/MAC 和工序编码后再联调。当前协议没有请求 ID，不应并行发送多个需要按顺序匹配 `FANUM` 的报工请求。

## 快捷 API 一览

- 创建
  - `IndustrialClientFactory.ModbusTcp`
  - `IndustrialClientFactory.ModbusRtu`
  - `IndustrialClientFactory.SiemensS7`
  - `IndustrialClientFactory.MitsubishiMc`
  - `IndustrialClientFactory.CreateModbusTcp`
  - `IndustrialClientFactory.FromConfig`

- 点位表
  - `TagTable.Load`
  - `TagTable.LoadForDevice`
  - `TagTable.LoadJson`
  - `TagTable.LoadCsv`

- 部署校验
  - `IndustrialSdkConfig.Validate`
  - `IndustrialDeployment.Open`
  - `IndustrialDeviceHost.Load`

- 读取 / 批量读取
  - `ReadAsync<T>`
  - `ReadManyAsync<T>(addresses...)`
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

- 写入 / 批量写入
  - `WriteAsync(address, string)`
  - `WriteAsync(address, byte[])`
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
  - `WriteManyAsync(dictionary)`

- 混合类型批量
  - `Tag.Int16 / Tag.Float / Tag.Bool / ...`
  - `ReadAsync(Tag)`
  - `ReadManyAsync(Tag...)`
  - `WriteAsync(Tag, value)`
  - `WriteManyAsync(tag.WithValue(...))`

- 不抛异常读取
  - `TryReadAsync<T>`

- 生命周期
  - `UseAsync`

- 诊断
  - `TestAsync`

## 调试 Demo

### Visual Studio

1. 打开 [Total.sln]
2. 在解决方案资源管理器中右键 `IndustrialCommDemo`
3. 选择“设为启动项目”
4. 直接按 `F5` 调试

### 命令行构建

```powershell
dotnet build .\IndustrialCommDemo\IndustrialCommDemo.csproj
```

构建成功后可执行文件位于：

- [IndustrialCommDemo.exe]

## Modbus TCP / RTU 现状

当前 Modbus 已内置三个设备 profile：

- 通用 `Modbus`，适用于普通 RTU/TCP 设备
- 汇川 `EasyPLC`
- 三菱 `Modbus TCP`

### 通用 Modbus 地址格式

通用 profile 不套用任何 PLC 品牌地址映射，支持以下两类写法：

- 零基协议地址：`C0`、`DI0`、`IR0`、`HR0`
- 一基引用地址：`00001`、`10001`、`30001`、`40001`

对应关系：

| 地址 | 区域 | 协议偏移 | 常用功能码 |
| --- | --- | ---: | --- |
| `C0` / `00001` | Coil | 0 | `0x01`、`0x05`、`0x0F` |
| `DI0` / `10001` | Discrete Input | 0 | `0x02` |
| `IR0` / `30001` | Input Register | 0 | `0x04` |
| `HR0` / `40001` | Holding Register | 0 | `0x03`、`0x06`、`0x10` |

设备手册若直接给出“协议地址 100”，应使用 `HR100`；若写的是引用地址 `40101`，其协议偏移同样为 100。

### RTU 串口与原始帧日志

Demo 与 SDK 日志按小时滚动并使用独立目录：`<数据目录>/Logs/Demo` 和 `<数据目录>/Logs/SDK`，默认保留 14 天。数据目录可在 Demo 的“存储设置”页修改，禁止设置到 C 盘；界面状态位于 `<数据目录>/State`，缓存统一预留在 `<数据目录>/Cache`。

- 默认串口格式为规范推荐的 `9600-8-E-1`，也可以按设备手册配置 `8-N-1`、`8-N-2` 等格式。
- RTU 操作由客户端公共锁串行执行，避免半双工总线同时发送多个请求。
- 默认读写超时均为 `3000 ms`，失败重试 `2` 次，参数均可配置。
- CRC16、RTU 帧封装和响应校验由 NModbus 处理。
- Demo 在 RTU 模式下会直接显示实际 TX/RX 的完整 Modbus RTU ADU，并标记 CRC 是否正确；RS-485 仅是物理层，界面展示的是在线路上传输的 Modbus RTU 标准报文。
- RTU 页面同时提供类似 Modbus 调试助手的原始十六进制发送框，可自动追加 CRC16，并支持标准功能码 `01/02/03/04/05/06/0F/10` 的响应组帧、异常响应和站号 `0` 广播。
- 每次成功写入串口后记录完整请求帧，例如：

```text
Modbus RTU TX | 01 03 00 00 00 01 84 0A
```

- 收到完整响应后记录响应帧；超时前只收到部分数据时标记为 `partial`：

```text
Modbus RTU RX | 01 03 02 00 7B F8 67
Modbus RTU RX (partial) | 01 03 02
```

日志包含从站地址、功能码、数据和 CRC，可直接与设备手册、串口抓包工具进行逐字节比较。

### 最近一轮优化

- `IndustrialClientBase` 的 `ReadManyAsync / WriteManyAsync` 已调整为整批操作只获取一次锁，避免逐条请求反复加锁/解锁。
- 基类新增 `ReadManyCoreAsync / WriteManyCoreAsync` 虚方法，协议子类可以覆写实现自己的批量优化逻辑。
- `ModbusTcpClient` 已覆写 `ReadManyCoreAsync`，会按 `Area` 分组、按地址排序，并把连续地址或间隙不超过 `16` 个寄存器的请求合并为一次读取。
- 合并读取完成后，会再按偏移量拆分结果，映射回各自的 `DataValue`，适合订阅和批量轮询场景。
- `ModbusTcpClient` 已改用 nmodbus 原生 async API，如 `ReadHoldingRegistersAsync / WriteMultipleRegistersAsync` 等，不再依赖 `Task.Run` 伪异步。
- `ModbusTcpClient` 的连接超时通过 `Task.WhenAny` 配合 `CancellationToken` 控制。
- `SiemensS7Client` 也已切换到 `S7netplus 0.20.0` 提供的异步 API：`OpenAsync / ReadAsync / WriteAsync`。
- PR #1 进一步完善了 S7 连接生命周期、MC 3E 地址元数据、批量超时、健康状态分类与按设备合并轮询。完整说明见 [docs/p0-p1-reliability-update.md](docs/p0-p1-reliability-update.md)。

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
- S7 重复连接、连接失败、通信失败后的资源释放更可靠
- MC 3E 地址解析规则更集中，后续扩展设备类型时不需要散落修改多个判断
- 健康状态不再被普通地址错误、数据转换错误误判成连接故障

### 批量读日志

当前 Modbus 公共客户端在批量读取时会输出两类日志：

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

- [InovanceEasyPlcModbusProfile.cs]
- [MitsubishiModbusTcpProfile.cs]
- [GenericModbusProfile.cs]

统一接口在：

- [ModbusDeviceProfile.cs]

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

这样可以把“地址映射”和“字序差异”都隔离在 profile 内部，不会污染 Modbus TCP/RTU 主流程。

## 当前关键文件

- [IndustrialClientBase.cs]
- [ModbusTcpClient.cs]
- [ModbusRtuClient.cs]
- [ModbusRtuTracingStreamResource.cs]
- [GenericModbusProfile.cs]
- [ModbusAddressing.cs]
- [InovanceEasyPlcModbusProfile.cs]
- [SiemensS7Client.cs]
- [MitsubishiMcClient.cs]
- [PollingScheduler.cs]
- [MainWindow.xaml.cs]

## 验证

当前构建验证命令：

- `dotnet build .\Total.sln --no-restore`
- `dotnet build .\IndustrialCommSdk\IndustrialCommSdk.csproj --no-restore`
- `dotnet build .\IndustrialCommDemo\IndustrialCommDemo.csproj -p:BuildProjectReferences=false`
