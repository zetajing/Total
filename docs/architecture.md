# 架构与配置

本文是 SDK 使用者和维护者共用的当前架构说明：先说明如何选择入口，再说明程序集边界、配置模型、轮询和可扩展平台。

## 使用路线

- 只连接一种 PLC：引用对应协议程序集，直接创建 `Options + Client`。
- 配置驱动、多设备运行：引用 `IndustrialCommSdk`，使用 `IndustrialSdk`、`IndustrialDeviceHost`。
- 只做 MES JSON：引用 `IndustrialCommSdk.Mes.Http`。
- 只做 TCP/Socket：引用 `IndustrialCommSdk.Transport`。
- 需要历史数据：业务层依赖 `IIndustrialHistoryStore`，数据库选择只放在创建入口。

所有项目当前目标框架都是 `net472`。快捷扩展、Tag 和轮询位于 `IndustrialCommSdk.Runtime`；配置位于 `IndustrialCommSdk.Runtime.Configuration`；存储公共类型位于 `IndustrialCommSdk.Storage`。

## 程序集边界

| 程序集 | 责任 |
| --- | --- |
| `IndustrialCommSdk.Abstractions` | 客户端契约、请求/返回模型、枚举、能力、日志、诊断和异常 |
| `IndustrialCommSdk.Runtime` | 客户端基类、轮询、DeviceHost、配置、协议注册表、TagTable、快捷扩展 |
| `IndustrialCommSdk.Transport` | TCP 客户端/服务端、会话、分帧、原始 Socket |
| `IndustrialCommSdk.Protocols.Common` | 寄存器、文本编解码等协议共享实现 |
| `IndustrialCommSdk.Protocols.*` | 各协议的 Options、地址解析、连接和读写实现 |
| `IndustrialCommSdk.Mes.Http` | 开放式 MES HTTP JSON 发送和接收 |
| `IndustrialCommSdk.Web` | HTTP/HTTPS、工业 WebAPI、WebSocket 客户端/服务端 |
| `IndustrialCommSdk.FileTransfer.Ftp` | FTP/FTPS 文件客户端 |
| `IndustrialCommSdk.Storage` | 历史存储契约、SQL Server、缓冲记录器和 CSV |
| `IndustrialCommSdk.Storage.MySql` | MySQL 8.0+ 历史存储提供程序 |
| `IndustrialCommSdk` | 引用全部内置模块并提供默认注册表 |

这是一次有意的破坏性模块化升级。旧 `SimpleClient`、`IndustrialClientFactory`、`IndustrialDeployment` 和旧配置兼容层已删除，不提供类型转发或旧 JSON 自动迁移。

## 配置驱动运行

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

`devices.json` 的公共字段、运行参数和协议参数严格分离：

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

配置注册表只接受 canonical 小写协议键：`modbus-tcp`、`modbus-rtu`、`siemens-s7`、`mitsubishi-mc`、`opc-ua`、`mqtt`、`redis`。未知协议、旧别名、空 `settings`、类型不匹配和 Provider 字段错误在解析或离线校验阶段暴露。

点位文件保持独立：

```json
{
  "tags": [
    { "name": "Speed", "address": "D100", "type": "Int16" },
    { "name": "Running", "address": "M10", "type": "Bool" }
  ]
}
```

## 轮询和生命周期

`PollingScheduler` 将同一设备/同一客户端的订阅放入一个 Worker：

1. 合并同一轮到期订阅的重复点位。
2. 协议实现 `IBatchOperationPlanner` 时，先生成 `BatchSplitPlan`。
3. 按计划组调用 `ReadManyAsync`；没有 planner 时按能力中的 `MaxReadItems` 保守拆分。
4. 批次独立容错，失败批次返回 `QualityStatus.Bad`，不阻断其他批次。
5. 按订阅原始点位顺序上报，并隔离回调异常。

订阅会拒绝不支持订阅的协议、低于推荐最小周期的请求、DeviceId 不匹配和同一 DeviceId 绑定不同客户端实例。最后一个订阅移除后 Worker 退出；`Dispose` 会等待活动和退役 Worker。

客户端操作超时或外部取消后，如果协议核心任务仍未真正退出，它会继续独占连接，防止下一请求与旧请求并发复用同一 PLC 连接。自定义协议若完全忽略取消，后续请求和同步释放可能等待底层 I/O 结束，这是连接安全边界。

## 可扩展平台

### 能力模型

```csharp
var capabilities = client.GetCapabilities();
```

`ProtocolCapabilities` 描述批量读写、优化批量、位地址、字符串、ByteArray、原始传输、连接诊断、最大批量数量、最大地址跨度、PDU 限制、推荐轮询周期和默认超时。`IndustrialClientBase` 提供默认能力，第三方客户端可实现 `IProtocolCapabilityProvider` 覆盖它。

### 地址和批量模型

平台类型包括 `IIndustrialAddress`、`ModbusAddress`、`S7Address`、`McAddress`、`BatchReadOptions`、`BatchWriteOptions`、`BatchSplitPlan`、`IBatchOperationPlanner` 和 `BatchPlanDiagnostics`。对外仍可使用字符串地址，协议内部优先使用强类型解析。

Modbus、S7、MC 已建立读取拆批计划。S7/MC 的 `ReadManyCoreAsync` 已将同一存储区内的连续点位合并为协议级读取：S7 一次读取共享字节区，MC 一次读取连续字/位设备，再按原始请求分别解码。planner 负责物理边界，客户端负责实际报文和结果映射。

### 自定义协议

```csharp
var registry = new IndustrialProtocolRegistry()
    .Register(new MyProtocolProvider());

var sdk = new IndustrialSdk(registry, logger);
```

自定义 `Settings` 实现 `IProtocolSettings`，Provider 继承 `IndustrialProtocolProvider<TSettings>`。注册表要求 canonical 小写键，拒绝重复键，并在创建客户端前检查 Settings 类型。

## 当前限制和验证边界

- 当前只目标 `net472`，尚未多目标到 .NET 8，也尚未发布独立 NuGet 包。
- 密码仍由应用配置提供，生产部署应接入 Windows 凭据管理器、DPAPI 或其他受保护来源。
- S7/MC 已完成连续区单报文批量读取；跨边界、断线恢复和真实设备行为仍需仿真/现场验收。
- OPC UA 客户端已使用底层库异步 API，并默认拒绝未受信证书；证书签发、信任链和真实 Server 互操作仍需集成验收。
- SQL Server/MySQL 真实服务器集成测试不在默认离线测试内。
- SDK 不替代急停、联锁和现场安全回路。

更详细的可靠性和验证记录见 [工程记录](engineering-notes.md)；数据库边界见 [历史存储](storage.md)。
