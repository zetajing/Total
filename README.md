# IndustrialCommSdk

## OPC UA

SDK 支持 OPC UA 客户端读写及批量读写。地址使用标准 NodeId 文本，例如
`ns=2;s=Machine/Temperature`、`ns=2;i=1001`。

```csharp
using (var client = IndustrialClientFactory.OpcUa(
    "opc.tcp://127.0.0.1:4840", deviceId: "ua-plc"))
{
    await client.ConnectAsync(CancellationToken.None);
    var value = await client.ReadAsync(new ReadRequest(
        "ua-plc", "ns=2;s=Machine/Temperature", DataType.Float),
        CancellationToken.None);
}
```

`devices.json` 也可配置匿名或用户名认证：

```json
{
  "name": "ua-plc",
  "protocol": "opc-ua",
  "endpointUrl": "opc.tcp://127.0.0.1:4840",
  "username": "operator",
  "password": "secret",
  "useSecurity": false,
  "pointsFile": "points/opcua.json"
}
```

面向 .NET Framework 4.7.2 的统一工业通信 SDK，提供一致的设备连接、异步读写、批量操作、轮询、诊断、配置和历史数据接口。

仓库同时包含 Windows 11 风格的 WPF 运行中心和轻量 WinForms 协议验证工具，可用于开发调试、现场联调和 SDK 回归。

## 功能概览

| 能力 | 当前支持 |
| --- | --- |
| PLC 协议 | Modbus TCP、Modbus RTU、Siemens S7、Mitsubishi MC 3E |
| 上层通信 | 原始 TCP、带分帧 TCP、MES TCP、MES HTTP |
| 数据操作 | 强类型单点读写、混合类型批量读写、点位名读写 |
| 运行能力 | 统一设备生命周期、自动重连、合并轮询、质量状态 |
| 配置 | JSON 设备配置、JSON/CSV 点位表、离线校验 |
| 诊断 | 超时与失败分类、健康状态、结构化诊断快照、滚动日志 |
| 存储 | SQL Server 历史数据、后台批量写入、查询与 CSV 导出 |
| UI | WPF 运行中心、WinForms 最小协议验证程序 |

SDK 默认操作超时为 5000 ms。单个请求指定的超时优先于客户端默认值。

## 解决方案结构

```text
Total.sln
├─ IndustrialCommSdk/              SDK 主类库
│  ├─ Abstractions/                公共接口、模型和能力描述
│  ├─ Protocols/                   Modbus、S7、MC 协议实现
│  ├─ Transport/                   TCP 传输与消息分帧
│  ├─ Polling/                     合并轮询调度
│  ├─ Diagnostics/                 日志与诊断快照
│  ├─ Mes/                         MES TCP / HTTP
│  └─ Storage/                     历史数据与 CSV
├─ IndustrialCommDemo/             WPF 运行中心与调试工具
├─ IndustrialCommMinimal.WinForms/ 最小协议验证程序
├─ IndustrialCommSdk.Tests/        NUnit 回归测试
└─ docs/                            设计与可靠性说明
```

## 环境要求

- Windows 10/11
- .NET Framework 4.7.2 Developer Pack
- Visual Studio 2022，或可构建 `net472` 的 .NET SDK/MSBuild
- 仅在使用历史数据功能时需要 SQL Server

## 构建与测试

```powershell
dotnet restore .\Total.sln
dotnet build .\Total.sln --configuration Release
dotnet test .\IndustrialCommSdk.Tests\IndustrialCommSdk.Tests.csproj --configuration Release
```

当前回归集包含 20 项测试，覆盖配置、TCP 分帧、连接超时、诊断、批量计划以及 MES HTTP 重试与资源生命周期。

## 快速开始

### 创建客户端

常见场景使用 `SimpleClient`：

```csharp
using IndustrialCommSdk;

var modbusTcp = SimpleClient.ModbusTcp("192.168.1.10", port: 502, slaveId: 1);
var modbusRtu = SimpleClient.ModbusRtu("COM3", baudRate: 9600, slaveId: 1);
var siemens = SimpleClient.S7("192.168.1.20", rack: 0, slot: 1);
var mitsubishi = SimpleClient.Mc("192.168.1.30", port: 5000);
```

需要厂商地址映射、CPU 型号、连接超时或底层选项时，使用 `IndustrialClientFactory` 或对应的 Options 类型。

### 连接、读写与释放

`UseAsync` 会在操作结束后自动断开并释放客户端：

```csharp
using IndustrialCommSdk;

await SimpleClient.ModbusTcp("192.168.1.10").UseAsync(async client =>
{
    int count = await client.ReadAsync<int>("D100");
    await client.WriteAsync("D100", count + 1);
});
```

也可以显式管理生命周期：

```csharp
using (var client = SimpleClient.S7("192.168.1.20"))
{
    await client.ConnectAsync();
    float temperature = await client.ReadAsync<float>("DB1.DBD0");
    await client.DisconnectAsync();
}
```

### 批量读写

```csharp
using System.Collections.Generic;
using IndustrialCommSdk;

var values = await client.ReadManyAsync<int>("D100", "D101", "D102");

await client.WriteManyAsync(new Dictionary<string, int>
{
    ["D100"] = 100,
    ["D101"] = 101,
});
```

混合类型使用强类型 Tag：

```csharp
var speed = Tag.Int16("D100");
var running = Tag.Bool("M10");

var result = await client.ReadManyAsync(speed, running);
short currentSpeed = result.Get(speed);
bool isRunning = result.Get(running);
```

## 地址与厂商 Profile

| 协议 | 常见地址示例 |
| --- | --- |
| 通用 Modbus | `HR0`、`IR0`、`40001`、`00001` |
| 汇川 EasyPLC | `D100`、`M10`、`X0`、`Y0` |
| 三菱 Modbus Profile | `D100`、`M10`、`X10`、`Y20`、`W1A` |
| Siemens S7 | `DB1.DBX0.0`、`DB1.DBW2`、`DB1.DBD4` |
| Mitsubishi MC | `D100`、`M10`、`X10`、`ZR100` |

Modbus TCP 默认使用通用映射。设备采用厂商地址或特殊字节序时，应显式选择 Profile：

```csharp
using IndustrialCommSdk.Protocols.Modbus;

var client = SimpleClient.ModbusTcp(
    "192.168.1.10",
    deviceProfile: ModbusDeviceProfiles.InovanceEasyPlc);
```

## 配置驱动运行

WPF 运行中心从 `IndustrialCommDemo/Config/devices.json` 加载设备，并通过各设备的 `pointsFile` 加载点位表。

最小设备配置：

```json
{
  "devices": [
    {
      "name": "plc1",
      "protocol": "modbus-tcp",
      "host": "192.168.1.10",
      "port": 502,
      "slaveId": 1,
      "deviceProfile": "generic",
      "pointsFile": "points/plc1.json",
      "pollingIntervalMilliseconds": 1000,
      "reconnectDelayMilliseconds": 3000,
      "enabled": true
    }
  ]
}
```

加载并离线校验：

```csharp
var config = IndustrialSdkConfig.Load(@"Config\devices.json");
var validation = config.Validate(@"Config");

if (!validation.IsValid)
{
    foreach (var error in validation.Errors)
        Console.WriteLine(error);
}
```

## MES HTTP

MES HTTP 支持上线、`FACHECK` 和 `FATRACK`，仅对 5xx、网络异常和超时执行有上限的重试；4xx 会直接返回业务错误。

```csharp
using IndustrialCommSdk.Mes;

using (var mes = new MesHttpClient(new MesHttpClientOptions
{
    BaseUrl = "http://mes-server:8080/api",
    DeviceNo = "DEVICE-001",
    DeviceName = "装配设备 1",
    DeviceIp = "192.168.1.10",
    DeviceMac = "00-11-22-33-44-55",
    TimeoutMilliseconds = 5000,
    MaxRetries = 2,
}))
{
    var response = await mes.SendOnlineAsync(CancellationToken.None);
}
```

需要代理、证书、统一认证或模拟测试时，可以注入处理器：

```csharp
var handler = new HttpClientHandler
{
    Proxy = proxy,
    UseProxy = true,
};

using (var mes = new MesHttpClient(options, handler, disposeHandler: true))
{
    await mes.SendOnlineAsync(CancellationToken.None);
}
```

也可以传入外部管理的 `HttpClient`。此时 SDK 不会修改或释放该实例：

```csharp
using (var mes = new MesHttpClient(options, sharedHttpClient, logger: null))
{
    await mes.SendOnlineAsync(CancellationToken.None);
}
```

## Demo

### WPF 运行中心

将 `IndustrialCommDemo` 设为启动项目。主要页面包括：

- 运行中心：统一启动、停止和查看设备及点位状态
- 设备配置：表单与 JSON 配置维护
- 历史数据：实时流水、筛选、统计、导出和清理
- 调试与维护：各协议直连、Socket、MES、网卡和存储设置
- 运行日志：应用日志与 SDK 日志分离展示

### WinForms 最小验证程序

`IndustrialCommMinimal.WinForms` 将每种协议隔离到独立页签，只保留连接、读写或收发所需的最小控件，适合现场快速排除 Demo 业务逻辑的影响。

## 可靠性约定

- 所有网络、串口和协议操作都应使用取消令牌或明确超时。
- `DeviceId` 不匹配、地址解析失败和数据转换失败不会被误判为设备掉线。
- 连接或传输故障会影响健康状态；单个坏点不会无条件拖垮整批结果。
- 同一设备的轮询订阅由单一 Worker 合并执行，降低重复读取和节拍漂移。
- 原始 TCP 必须根据上层协议选择固定长度、分隔符或长度头分帧，不能假设一次接收就是一条完整消息。

生产环境仍应通过真实 PLC、串口链路和 MES 服务执行实机验收，自动化测试不能替代现场协议确认。

## 文档

- [P0/P1 可靠性优化说明](docs/p0-p1-reliability-update.md)
- [协议实现审查](docs/protocol-review.md)
- [核心扩展设计](CORE_EXTENSIBILITY.md)
- [项目状态与待办](PROJECT_CONTEXT.md)
- [改进建议](improvement-suggestions.md)

## 当前边界

- 目标框架为 `net472`，尚未发布 NuGet 包。
- S7 与 MC 已具备批量计划能力，但连续点位仍未全部实现协议级合并读取。
- 写批量计划保持保守单点分组，需结合实机限制后再开放合并。
- OPC UA、EtherNet/IP、Omron 等协议仅在真实项目需要时扩展。

## License

当前仓库未提供独立许可证文件。如需分发、商用或二次发布，请先确认代码所有者授权。
