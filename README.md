# IndustrialCommSdk

面向 .NET Framework 4.7.2 的工业通信 SDK。项目把公共契约、运行时、传输、协议、MES 和存储拆成独立程序集；应用可以只引用所需模块，也可以引用 `IndustrialCommSdk` 聚合入口获得全部内置协议。

## 模块

| 程序集 | 职责 |
|---|---|
| `IndustrialCommSdk.Abstractions` | 客户端接口、请求/响应模型、枚举、能力、日志、诊断和异常 |
| `IndustrialCommSdk.Runtime` | 客户端基类、轮询、DeviceHost、配置、协议注册表、TagTable 和快捷扩展 |
| `IndustrialCommSdk.Transport` | TCP 客户端/服务端、会话、分帧和原始 Socket |
| `IndustrialCommSdk.Protocols.Common` | 寄存器和文本编解码等协议共享实现 |
| `IndustrialCommSdk.Protocols.Modbus` | Modbus TCP/RTU（NModbus4） |
| `IndustrialCommSdk.Protocols.S7` | Siemens S7（S7netplus） |
| `IndustrialCommSdk.Protocols.Mc` | Mitsubishi MC 3E |
| `IndustrialCommSdk.Protocols.OpcUa` | OPC UA（OPC Foundation） |
| `IndustrialCommSdk.Protocols.Mqtt` | MQTT（MQTTnet） |
| `IndustrialCommSdk.Protocols.Redis` | Redis（StackExchange.Redis） |
| `IndustrialCommSdk.Mes.Http` | 开放式 MES HTTP JSON 发送和接收 |
| `IndustrialCommSdk.Storage` | SQL Server 历史、缓冲记录器和 CSV |
| `IndustrialCommSdk` | 引用全部内置模块并提供默认注册表 |

协议模块互不引用，也不引用聚合程序集。第三方驱动包只存在于对应协议项目中。

常用命名空间与程序集保持一致：

```csharp
using IndustrialCommSdk;                       // 聚合入口 IndustrialSdk
using IndustrialCommSdk.Runtime;               // 快捷扩展、TagTable、DeviceHost
using IndustrialCommSdk.Runtime.Configuration; // 注册表、Provider、强类型配置
using IndustrialCommSdk.Storage;               // 历史存储、缓冲记录器、日志显示帮助
```

命名空间迁移不提供兼容包装：快捷扩展、Tag 和 TagTable 从 `IndustrialCommSdk` 移到
`IndustrialCommSdk.Runtime`；配置从 `IndustrialCommSdk.Configuration` 移到
`IndustrialCommSdk.Runtime.Configuration`；轮询从 `IndustrialCommSdk.Polling` 移到
`IndustrialCommSdk.Runtime.Polling`；自定义协议基类 `IndustrialClientBase` 直接位于
`IndustrialCommSdk.Runtime`；`LogDisplayHelper` 位于 `IndustrialCommSdk.Storage`。

## 构建与测试

需要 Windows、Visual Studio 2022 或 .NET SDK，以及 .NET Framework 4.7.2 Developer Pack。

```powershell
dotnet restore Total.sln
dotnet build Total.sln -c Release
dotnet test IndustrialCommSdk.Tests/IndustrialCommSdk.Tests.csproj -c Release
```

## 直接创建单协议客户端

只使用一个协议时，不需要聚合入口。引用对应协议模块及其传递依赖，然后直接构造客户端：

```csharp
using IndustrialCommSdk;
using IndustrialCommSdk.Runtime;
using IndustrialCommSdk.Protocols.Modbus;

using (var client = new ModbusTcpClient(new ModbusTcpClientOptions
{
    DeviceId = "plc1",
    Host = "192.168.1.10",
    Port = 502,
    SlaveId = 1,
    DeviceProfile = ModbusDeviceProfiles.InovanceEasyPlc,
    ConnectTimeoutMilliseconds = 3000,
    OperationTimeoutMilliseconds = 5000
}))
{
    await client.UseAsync(async connected =>
    {
        var speed = await connected.ReadInt16Async("D100");
        await connected.WriteAsync("D101", (short)(speed + 1));
    });
}
```

其他协议分别使用 `ModbusRtuClient`、`SiemensS7Client`、`MitsubishiMcClient`、`OpcUaClient`、`MqttClient` 和 `RedisClient` 及其 Options。

### 每个协议的最小示例

- [示例索引](docs/protocols/README.md)
- [Modbus TCP](docs/protocols/modbus-tcp.md)
- [Modbus RTU](docs/protocols/modbus-rtu.md)
- [Siemens S7](docs/protocols/siemens-s7.md)
- [Mitsubishi MC](docs/protocols/mitsubishi-mc.md)
- [OPC UA](docs/protocols/opc-ua.md)
- [MQTT](docs/protocols/mqtt.md)
- [Redis](docs/protocols/redis.md)

## 聚合入口与配置

完整应用使用实例化入口：

```csharp
var sdk = IndustrialSdk.CreateDefault(logger);
var config = sdk.LoadConfiguration("Config/devices.json");

var validation = config.Validate(
    Path.GetDirectoryName(Path.GetFullPath("Config/devices.json")),
    sdk.Protocols,
    logger);

if (!validation.IsValid)
    throw new InvalidOperationException(string.Join(Environment.NewLine, validation.Errors));

using (var host = sdk.CreateDeviceHost(config, "Config"))
{
    await host.StartAsync();
    var value = await host.Get("plc1").ReadAsync("Speed");
    await host.StopAsync();
}
```

`IndustrialSdk` 提供：

- `CreateDefault(logger)`
- `LoadConfiguration` / `ParseConfiguration`
- `SerializeConfiguration` / `SaveConfiguration`
- `CreateClient`
- `CreateDeviceHost`

这是破坏性模块化版本。旧 `IndustrialClientFactory`、`SimpleClient`、`IndustrialDeployment` 和旧配置兼容层已删除。

### devices.json

公共字段、运行参数和协议参数严格分离：

```json
{
  "devices": [
    {
      "name": "plc1",
      "protocol": "modbus-tcp",
      "deviceId": "plc1",
      "pointsFile": "points/plc1.json",
      "enabled": true,
      "runtime": {
        "pollingIntervalMilliseconds": 1000,
        "reconnectDelayMilliseconds": 3000,
        "operationTimeoutMilliseconds": 5000,
        "reportOnChangeOnly": false
      },
      "settings": {
        "host": "127.0.0.1",
        "port": 502,
        "slaveId": 1,
        "deviceProfile": "inovance-easyplc",
        "connectTimeoutMilliseconds": 3000
      }
    }
  ]
}
```

固定协议键只有：

- `modbus-tcp`
- `modbus-rtu`
- `siemens-s7`
- `mitsubishi-mc`
- `opc-ua`
- `mqtt`
- `redis`

不接受旧别名。Newtonsoft.Json 13.0.3 根据注册表把 `settings` 反序列化为协议强类型；未知协议、空 settings、类型不匹配和字段错误会在解析或离线校验阶段暴露。

## 自定义协议

自定义 Settings 实现 `IProtocolSettings`，Provider 继承 `IndustrialProtocolProvider<TSettings>`：

```csharp
var registry = new IndustrialProtocolRegistry()
    .Register(new MyProtocolProvider());

var sdk = new IndustrialSdk(registry, logger);
```

注册表要求 canonical 小写键，拒绝重复键，并在创建客户端前检查 Settings 类型。

## 点位表与轮询

点位文件仍使用独立 JSON：

```json
{
  "tags": [
    { "name": "Speed", "address": "D100", "type": "Int16" },
    { "name": "Running", "address": "M10", "type": "Bool" }
  ]
}
```

`PollingScheduler` 按 DeviceId 合并订阅和重复读取，隔离回调异常，并对失败批次生成 Bad 质量结果。一个 DeviceId 同时只允许绑定一个客户端实例。

## MES HTTP JSON

MES 是开放式 HTTP JSON，不内置 FACHECK、FATRACK、FANUM 等业务流程。

发送：

```csharp
using (var mes = new MesHttpClient(new MesHttpClientOptions
{
    BaseUrl = "http://127.0.0.1:8080/api/",
    TimeoutMilliseconds = 5000,
    MaxRetries = 0
}))
{
    var response = await mes.SendJsonAsync(
        "events/upload",
        "{\"deviceNo\":\"001\",\"value\":12}",
        CancellationToken.None);
}
```

SDK 验证根节点为 JSON 对象，但 POST 正文保持调用方输入的空格和字段顺序。默认不重试，避免非幂等报工重复；服务端支持幂等时可显式启用有界重试。

`MesJsonReceiver` 可通过本地 `HttpListener` 接收任意相对路径的 POST JSON 对象，支持正文上限、处理超时、Authorization 完整值检查和自定义 JSON 响应。

## Demo

- `IndustrialCommDemo`：WPF 完整 Demo。配置页包含公共字段、独立 Settings JSON 编辑器和完整 devices.json 高级编辑器；运行中心使用 `IndustrialSdk` 与 `IndustrialDeviceHost`。
- `IndustrialCommMinimal.WinForms`：直接引用所需模块的最小验证程序，不依赖聚合程序集。
- MES 页面只使用开放 JSON 编辑器，不包含旧 TCP MES 或固定业务操作。

## 可靠性边界

- SDK 不替代 PLC 急停、联锁和安全回路。
- 写入前应由业务层完成权限、范围和设备状态校验。
- 数据库故障不会阻塞 PLC 实时通信；缓冲队列有界，满载时记录丢弃计数。
- 密码当前仍是明文配置；生产部署应由应用层接入安全凭据来源。
- 当前只目标 `net472`，尚未提供 NuGet 发布和多目标框架。
