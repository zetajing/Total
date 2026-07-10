# 项目上下文

## 项目目标

`Total` 是一个面向工业现场的 .NET 通讯 SDK 与 WPF Demo。当前重点是通过外置 JSON 配置完成快速部署：部署人员只需修改 `Config` 目录中的设备与点位文件，即可读取、写入和批量读取 PLC 数据。

## 当前分支与提交

- 默认分支：`master`
- 当前代码状态：以 `master` 最新提交为准，使用 `git log --oneline -1` 查看。
- 每次修改代码或文档后都需要提交并推送到 Git。

## 解决方案结构

| 目录/项目 | 职责 |
| --- | --- |
| `IndustrialCommSdk` | 核心 SDK：统一客户端、协议实现、点位表、配置、轮询、诊断、存储和 MES。 |
| `IndustrialCommSdk.Tests` | SDK 单元和回环通讯测试。当前应通过 138 项测试。 |
| `IndustrialCommDemo` | WPF 演示程序，包含协议调试、JSON 部署、MES、数据库、网卡和存储设置页面。 |
| `LogHelper` | Demo 使用的日志组件。 |
| `Refresh Logs` | 旧式 WPF 工具项目，不属于 SDK/Demo 主链路。 |

## 支持的协议

- Modbus TCP / RTU
- Siemens S7
- Mitsubishi MC 3E
- Socket TCP 调试
- FA MES TCP

统一抽象位于 `IndustrialCommSdk/Abstractions/IIndustrialClient`。协议客户端应继续遵循该接口，不在业务层直接依赖某个 PLC 驱动库。

## JSON 快速部署

Demo 配置模板位于：

- `IndustrialCommDemo/Config/devices.json`
- `IndustrialCommDemo/Config/points/plc1.json`
- `IndustrialCommDemo/Config/points/s7plc.json`
- `IndustrialCommDemo/Config/modbus-profiles.json`

构建时，`IndustrialCommDemo.csproj` 会将整个 `Config/**/*.json` 复制到 Demo 输出目录。运行时应修改输出目录下的 `Config` 文件，不把设备信息内置到 DLL。

`devices.json` 的每台设备通过 `pointsFile` 关联点位表：

```json
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
}
```

新增设备时：复制一段设备配置，创建对应的点位 JSON，然后在 Demo 的“JSON 配置”页点击“重新加载”和“校验配置”。

### Modbus 品牌 JSON 配置

`IndustrialCommDemo/Config/modbus-profiles.json` 通过 `ModbusProfileDefinition` 数据模型驱动 `JsonModbusProfile : IModbusDeviceProfile`。汇川 EasyPLC 和三菱 Modbus TCP 的品牌映射已用 JSON 等价描述，`TryLoadDefaultConfig()` 自动从该文件加载。

新增品牌只需在 `modbus-profiles.json` 的 `profiles` 数组加一条记录：

- `key`：唯一标识，例如 `"my-brand"`
- `addressPattern`：地址正则，组 1 为前缀、组 2 为索引
- `mappings`：前缀映射表（prefix → area / base / max / radix）
- `lowWordFirst`：多字类型是否需要低字在前交换

`GenericModbusProfile` 保留为硬编码（双格式地址逻辑）。

## 简化 SDK 用法

配置校验不连接 PLC：

```csharp
var configPath = "Config/devices.json";
var config = IndustrialSdkConfig.Load(configPath);
var result = config.Validate(Path.GetDirectoryName(Path.GetFullPath(configPath)));
```

配置化读写：

```csharp
using (var device = IndustrialDeployment.Open("Config/devices.json", "plc1"))
{
    await device.ConnectAsync();
    short speed = await device.ReadAsync<short>("Speed");
    await device.WriteAsync("Run", true);
    var values = await device.ReadManyAsync();
    await device.DisconnectAsync();
}
```

`IndustrialDeployment.Open` 一次性加载设备配置、点位表并创建协议客户端。`IndustrialConfiguredClient` 按 JSON 点位名提供单读、单写、批量读和批量写入口。

多设备后台运行使用 `IndustrialDeviceHost.Load("Config/devices.json")`：仅加载 `enabled` 不为 false 的设备，管理连接、按 `pollingIntervalMilliseconds` 批量轮询、断线重连、状态事件和数据事件。

## 验证命令

SDK 测试：

```powershell
dotnet test IndustrialCommSdk.Tests\IndustrialCommSdk.Tests.csproj --no-restore
```

Demo 构建：

```powershell
dotnet build IndustrialCommDemo\IndustrialCommDemo.csproj --no-restore
```

预期结果：SDK 140 项测试通过，Demo 构建 0 警告、0 错误。

## 已知限制

`dotnet build Total.sln --no-restore` 目前会在 `Refresh Logs` 项目失败。该项目是旧式 .NET Framework WPF 工程，当前 .NET SDK 无法创建其 x86 `GenerateResource` 任务宿主。

SDK 与 `IndustrialCommDemo` 单独构建正常。解决整个解决方案构建问题时，优先考虑迁移 `Refresh Logs` 为 SDK 风格项目，或使用完整 Visual Studio MSBuild 构建该旧项目。

## 已完成：DeviceHost 基础运行时

已完成的能力：

1. 从 `devices.json` 自动加载所有启用设备。
2. 管理连接、固定周期重连、健康状态和运行日志。
3. 按设备的点位表执行周期批量读取并上报事件。
4. 提供按“设备名 + 点位名”的读写入口和状态事件。

下一优先级：增加 `readGroup`、`writable`、单位等少量点位运行元数据；再根据真实现场需求扩展协议模块。

Modbus 品牌差异通过 `IModbusDeviceProfile` 隔离，JSON 数据驱动，新增品牌不改 C# 代码、不重新编译。非 Modbus 协议扩展采用插件式：只有真实现场需要时才增加 Omron、Allen-Bradley、OPC UA 等模块，并保持 `IIndustrialClient` 抽象不变。
