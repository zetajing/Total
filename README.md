# InduLink

面向工业现场的 .NET 8 通信 SDK，统一封装 PLC、工业协议、MES、网络服务和历史存储。SDK 将公共契约、运行时、传输、协议、存储拆成独立程序集；应用可以只引用需要的模块，也可以引用 `InduLink` 聚合入口。

## 能做什么

| 能力 | 程序集 |
| --- | --- |
| 公共契约、数据值、诊断、异常 | `InduLink.Abstractions` |
| 配置、DeviceHost、轮询、TagTable、快捷扩展 | `InduLink.Runtime` |
| TCP、分帧、会话、Socket | `InduLink.Transport` |
| Modbus TCP/RTU | `InduLink.Protocols.Modbus` |
| Siemens S7 | `InduLink.Protocols.S7` |
| Mitsubishi MC 3E | `InduLink.Protocols.Mc` |
| TwinCAT ADS | `InduLink.Protocols.Ads` |
| OPC UA | `InduLink.Protocols.OpcUa` |
| MQTT | `InduLink.Protocols.Mqtt` |
| Redis Key/Value | `InduLink.Protocols.Redis` |
| 开放式 MES HTTP JSON | `InduLink.Mes.Http` |
| HTTP/HTTPS、WebAPI、WebSocket | `InduLink.Web` |
| FTP/FTPS | `InduLink.FileTransfer.Ftp` |
| SQL Server 历史存储、缓冲记录、CSV | `InduLink.Storage` |
| MySQL 8.0+ 历史存储 | `InduLink.Storage.MySql` |

协议程序集之间互不引用，也不引用聚合程序集；第三方驱动只由所属模块携带。Redis 是独立的缓存/键值协议，不属于 SQL Server/MySQL 历史存储。

## 快速开始

环境要求：Windows、Visual Studio 2022 或 .NET 8 SDK。WPF/WinForms 项目使用 `net8.0-windows`，Snap7Server 使用 x86 目标。

```powershell
dotnet restore Total.sln
dotnet build Total.sln -c Release
dotnet test InduLink.Tests/InduLink.Tests.csproj -c Release
```

只连接一种协议时，直接引用对应模块并构造具体 Client：

```csharp
using InduLink.Protocols.Modbus;
using InduLink.Runtime;

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

协议键固定为 `modbus-tcp`、`modbus-rtu`、`siemens-s7`、`mitsubishi-mc`、`ads`、`opc-ua`、`mqtt` 和 `redis`。设备公共字段、运行参数和协议 `settings` 分离，完整配置约定见 [架构与配置](docs/architecture.md#配置驱动运行)。

## 文档导航

从 [文档总览](docs/README.md) 选择入口：

- [入门、选择引用方式和排错](docs/architecture.md#使用路线)
- [协议最小示例与现场注意事项](docs/protocols.md)
- [其他协议完整控制台示例、类型对照与排错](docs/protocol-console.md)
- [SQL Server/MySQL 历史存储](docs/storage.md)
- [MES、Web、MQTT Broker、WebSocket、FTP/FTPS](docs/integrations.md)
- [可靠性、审查结论、验证边界和路线图](docs/engineering-notes.md)

## 示例程序

- `InduLinkDemo`：WPF 完整 Demo，包含协议页、配置页、运行中心、MES、数据库和网络服务页面。
- `InduLinkDemo.Snap7Server`：x86 本地 Snap7 通信模拟器，用于在没有实体 PLC 时验证 S7 Demo 的 DB1 多个 Bool/INT/DINT/REAL 点位读写。
- `InduLinkMinimal.WinForms`：直接引用所需模块的最小验证程序，不依赖聚合程序集。

## 重要边界

- SDK 不替代 PLC 急停、硬件联锁和现场安全回路。
- 写入前由业务层完成权限、范围、点位和设备状态校验。
- 数据库故障不应阻塞 PLC 实时通信；缓冲队列有界，满载时会记录丢弃计数。
- 密码和 API Key 不应提交到源码或普通配置；生产环境接入受保护的凭据来源。
- 当前目标为 .NET 8；WPF/WinForms 应用使用 Windows 专用目标框架，Snap7Server 保持 x86 sidecar。
- Runtime 中的 `DpapiSecretStore` 仅支持 Windows，非 Windows 环境在构造时明确报错；其他平台应提供自己的 `ISecretStore` 实现，详见 [平台和密钥存储](docs/engineering-notes.md#平台和密钥存储)。
- 本地构建/测试通过不等于真实 PLC、数据库、Broker 或 FTP 现场验收通过；现场集成需单独安排。
