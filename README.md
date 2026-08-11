# IndustrialCommSdk

面向工业现场的 .NET Framework 4.7.2 通信 SDK，统一封装 PLC、工业协议、MES、网络服务和历史存储。SDK 将公共契约、运行时、传输、协议、存储拆成独立程序集；应用可以只引用需要的模块，也可以引用 `IndustrialCommSdk` 聚合入口。

## 能做什么

| 能力 | 程序集 |
| --- | --- |
| 公共契约、数据值、诊断、异常 | `IndustrialCommSdk.Abstractions` |
| 配置、DeviceHost、轮询、TagTable、快捷扩展 | `IndustrialCommSdk.Runtime` |
| TCP、分帧、会话、Socket | `IndustrialCommSdk.Transport` |
| Modbus TCP/RTU | `IndustrialCommSdk.Protocols.Modbus` |
| Siemens S7 | `IndustrialCommSdk.Protocols.S7` |
| Mitsubishi MC 3E | `IndustrialCommSdk.Protocols.Mc` |
| OPC UA | `IndustrialCommSdk.Protocols.OpcUa` |
| MQTT | `IndustrialCommSdk.Protocols.Mqtt` |
| Redis Key/Value | `IndustrialCommSdk.Protocols.Redis` |
| 开放式 MES HTTP JSON | `IndustrialCommSdk.Mes.Http` |
| HTTP/HTTPS、WebAPI、WebSocket | `IndustrialCommSdk.Web` |
| FTP/FTPS | `IndustrialCommSdk.FileTransfer.Ftp` |
| SQL Server 历史存储、缓冲记录、CSV | `IndustrialCommSdk.Storage` |
| MySQL 8.0+ 历史存储 | `IndustrialCommSdk.Storage.MySql` |

协议程序集之间互不引用，也不引用聚合程序集；第三方驱动只由所属模块携带。Redis 是独立的缓存/键值协议，不属于 SQL Server/MySQL 历史存储。

## 快速开始

环境要求：Windows、Visual Studio 2022 或 .NET SDK，以及 .NET Framework 4.7.2 Developer Pack。

```powershell
dotnet restore Total.sln
dotnet build Total.sln -c Release
dotnet test IndustrialCommSdk.Tests/IndustrialCommSdk.Tests.csproj -c Release
```

只连接一种协议时，直接引用对应模块并构造具体 Client：

```csharp
using IndustrialCommSdk.Protocols.Modbus;
using IndustrialCommSdk.Runtime;

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

配置驱动的多设备程序使用聚合入口：

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

协议键固定为 `modbus-tcp`、`modbus-rtu`、`siemens-s7`、`mitsubishi-mc`、`opc-ua`、`mqtt` 和 `redis`。设备公共字段、运行参数和协议 `settings` 分离，完整配置约定见 [架构与配置](docs/architecture.md#配置驱动运行)。

## 文档导航

从 [文档总览](docs/README.md) 选择入口：

- [入门、选择引用方式和排错](docs/architecture.md#使用路线)
- [协议最小示例与现场注意事项](docs/protocols.md)
- [SQL Server/MySQL 历史存储](docs/storage.md)
- [MES、Web、MQTT Broker、WebSocket、FTP/FTPS](docs/integrations.md)
- [可靠性、审查结论、验证边界和路线图](docs/engineering-notes.md)

## 示例程序

- `IndustrialCommDemo`：WPF 完整 Demo，包含协议页、配置页、运行中心、MES、数据库和网络服务页面。
- `IndustrialCommMinimal.WinForms`：直接引用所需模块的最小验证程序，不依赖聚合程序集。

## 重要边界

- SDK 不替代 PLC 急停、硬件联锁和现场安全回路。
- 写入前由业务层完成权限、范围、点位和设备状态校验。
- 数据库故障不应阻塞 PLC 实时通信；缓冲队列有界，满载时会记录丢弃计数。
- 密码和 API Key 不应提交到源码或普通配置；生产环境接入受保护的凭据来源。
- 当前只目标 `net472`，尚未提供独立 NuGet 发布和多目标框架。
- 本地构建/测试通过不等于真实 PLC、数据库、Broker 或 FTP 现场验收通过；现场集成需单独安排。
