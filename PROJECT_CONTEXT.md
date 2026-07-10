# 项目上下文

## 项目目标

`Total` 是一个面向工业现场的 .NET 通讯 SDK 与 WPF Demo。当前重点是通过外置 JSON 配置完成快速部署：部署人员只需修改 `Config` 目录中的设备与点位文件，即可读取、写入、批量读取、轮询订阅和保存历史数据。

## 当前分支与提交

- 默认分支：`master`
- 当前代码状态：已合并 `PR #1 Optimize industrial SDK reliability for P0/P1`。
- P0/P1 合并提交：`1afa2eb44394361548f8fd3d313b7c939bed89ca`
- 当前正在推进：`core-extensible-platform`，目标是把 SDK 核心做成可扩展平台。
- 每次修改代码或文档后都需要提交并推送到 Git。

## 解决方案结构

| 目录/项目 | 职责 |
| --- | --- |
| `IndustrialCommSdk` | 核心 SDK：统一客户端、协议实现、点位表、配置、轮询、诊断、存储和 MES。 |
| `IndustrialCommSdk.Tests` | SDK 单元和回环通讯测试。测试数量会随功能继续增加，提交前优先运行该项目。 |
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

## 核心可扩展平台现状

`core-extensible-platform` 最初采用非破坏式建模，现在已经推进到“直接接入现有核心实现”：不修改 `IIndustrialClient` 现有方法签名，但核心基类、Modbus/S7/MC 地址模型、Modbus 批量规划和轮询调度已经开始使用平台模型。

新增文件：

- `IndustrialCommSdk/Abstractions/ProtocolCapabilities.cs`
- `IndustrialCommSdk/Abstractions/IndustrialAddress.cs`
- `IndustrialCommSdk/Abstractions/BatchOptions.cs`
- `IndustrialCommSdk/Abstractions/BatchSplitPlan.cs`
- `IndustrialCommSdk/Abstractions/PlatformInterfaces.cs`
- `IndustrialCommSdk/IndustrialClientPlatformExtensions.cs`
- `IndustrialCommSdk.Tests/PlatformModelTests.cs`
- `CORE_EXTENSIBILITY.md`

新增/接入能力：

1. `ProtocolCapabilities`
   - 描述协议是否支持批量、位地址、字符串、ByteArray、原始传输、原生异步、推荐轮询周期、最大批量数量、最大地址跨度和 PDU 限制。
   - 通过 `client.GetCapabilities()` 读取。
   - 第三方协议客户端可实现 `IProtocolCapabilityProvider` 覆盖默认能力。

2. `IndustrialClientBase : IProtocolCapabilityProvider`
   - 所有继承基类的协议客户端默认具备协议能力。
   - 默认返回 `ProtocolCapabilities.ForProtocol(Kind)`。
   - 具体协议可 override `Capabilities` 来描述运行时差异。

3. `IIndustrialAddress` / `IndustrialAddress`
   - 提供统一地址形状：Original、Normalized、Area、Offset、Bit、IsBitAddress。
   - `ModbusAddress`、`S7Address` 和 `McAddress` 已实现该接口。

4. 强类型地址 Parser
   - `ModbusAddressParser` 已实现 `IAddressParser<ModbusAddress>`，并保留旧 `IAddressParser`。
   - `S7AddressParser` 已实现 `IAddressParser<S7Address>`，并保留旧 `IAddressParser`。
   - `McAddressParser` 已实现 `IAddressParser<McAddress>`，并保留旧 `IAddressParser`。
   - 协议内部已开始使用 `ParseTyped`，减少 object cast。

5. `BatchReadOptions` / `BatchWriteOptions` / `BatchSplitPlan`
   - 为批量读写的超时、拆分、合并、顺序保持、错误继续策略提供协议无关模型。
   - `ModbusClientBase` 已实现 `IBatchOperationPlanner`。
   - Modbus 现有连续地址合并已映射为 `BatchSplitPlan`。
   - `PlanRead` 使用现有按 Area 分组、地址排序、小间隔合并的规则生成计划。
   - `PlanWrite` 当前保守地按单个写入生成物理写入组，暂不自动合并写操作。

6. `PollingScheduler` 接入能力模型和批量计划
   - `SubscribeAsync` 会读取 `client.GetCapabilities()`。
   - 拒绝不支持订阅的协议，例如原始 TCP Socket。
   - 拒绝低于 `RecommendedMinPollingInterval` 的订阅周期。
   - Worker 内保存协议能力，并优先使用 `IBatchOperationPlanner.PlanRead(...)` 拆分轮询读取批次。
   - 没有 planner 的协议会按 `MaxReadItems` 做保守拆分。
   - 每个轮询批次独立容错，失败批次返回 `QualityStatus.Bad`，不会阻断其他批次。

7. 测试更新
   - `PlatformModelTests` 覆盖能力模型、统一地址、Modbus/S7/MC parser 的平台地址形状、Modbus 读计划、批量计划和能力 provider fallback。
   - `PollingSchedulerTests` 已覆盖协议不支持订阅、低于推荐轮询周期、DeviceId 不匹配、同设备不同客户端拒绝、重复点位合并读取、无 planner 拆分、有 planner 计划拆分。

## P0/P1 可靠性优化现状

本轮 P0/P1 已合并到 `master`，核心目标是先把工业通讯 SDK 的连接生命周期、批量超时、健康状态和轮询调度做稳，再继续扩协议。

### P0 已完成

1. `MitsubishiMcClient`
   - MC 地址解析改成集中元数据表，统一维护设备代码、地址进制、位/字设备分类。
   - 修正 `ZR` 地址不应按十六进制解析的问题。
   - 增加 Host、Port、DeviceId、连接状态和地址范围校验。

2. `SiemensS7Client`
   - 参考成熟 S7.NetPlus 使用方式优化生命周期。
   - 重复连接前先关闭旧 `Plc`。
   - 连接失败时释放临时 `Plc`，避免残留半开连接。
   - 读写失败遇到传输/连接类异常时关闭失效连接。
   - 加强 DB、DBX、M/I/Q 位地址校验；Bool 写入必须使用明确 bit 地址。
   - 保留 `ReadDbClassAsync` / `WriteDbClassAsync` 类映射能力。

3. `IndustrialClientBase`
   - 单点和批量操作统一超时语义。
   - 批量操作校验请求 `DeviceId`。
   - 全 Bad 批量结果不再把健康状态重置为成功。
   - 地址错误、数据转换错误与连接/传输故障分离，不再把所有异常都记成连接 Faulted。
   - 连接、超时、Socket、IO 等传输类故障才影响连接健康状态。

### P1 已完成

1. `PollingScheduler`
   - 同一设备 / 同一客户端只保留一个后台 Worker。
   - 多个订阅到期时合并重复点位读取。
   - 轮询改为固定节拍，减少“读取耗时 + Interval”造成的累计漂移。
   - 订阅回调异常被隔离，不会杀死轮询循环。
   - 同一 `DeviceId` 不允许绑定不同客户端实例，避免轮询挂到错误连接。
   - Worker 停止与新订阅并发时会移除旧 Worker 并重新绑定。

2. `PollingSchedulerTests`
   - 已覆盖基础订阅事件、ByteArray 变化检测、DeviceId 不匹配、同设备不同客户端拒绝、重复点位合并读取。

## 当前设计边界

- 轮询订阅仍是“SDK 主动周期读取”，不是 PLC 主动推送。
- `IIndustrialClient` 的操作仍按客户端串行化执行，避免同一 TCP/串口连接上的请求响应错位。
- 轮询调度已按设备合并，并已接入协议能力校验和批量计划拆分。
- Modbus 连续地址合并已映射到 `BatchSplitPlan`，并且轮询调度器可以使用该计划分批读取；S7 / MC 还未实现批量计划器。
- `ReadAsync` 通信失败默认返回 `DataValue.Bad`；写入失败仍抛异常。调用方需要按 `Quality` 判断读取结果。
- Demo 还未根据 `ProtocolCapabilities` 动态调整 UI。
- 环境里无法保证所有变更都经过本地 `dotnet test`，后续每次功能修改必须优先补齐本地或 CI 验证。

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

优先验证：

```powershell
dotnet test .\IndustrialCommSdk.Tests\IndustrialCommSdk.Tests.csproj
dotnet build .\IndustrialCommDemo\IndustrialCommDemo.csproj -p:BuildProjectReferences=false
```

## 已知限制

`dotnet build Total.sln --no-restore` 目前会在 `Refresh Logs` 项目失败。该项目是旧式 .NET Framework WPF 工程，当前 .NET SDK 无法创建其 x86 `GenerateResource` 任务宿主。

SDK 与 `IndustrialCommDemo` 单独构建正常。解决整个解决方案构建问题时，优先考虑迁移 `Refresh Logs` 为 SDK 风格项目，或使用完整 Visual Studio MSBuild 构建该旧项目。

## 已完成：DeviceHost 基础运行时

已完成的能力：

1. 从 `devices.json` 自动加载所有启用设备。
2. 管理连接、固定周期重连、健康状态和运行日志。
3. 按设备的点位表执行周期批量读取并上报事件。
4. 提供按“设备名 + 点位名”的读写入口和状态事件。

下一优先级：接入 Demo 的协议能力显示，再继续实现 S7 / MC 的批量计划器。

Modbus 品牌差异通过 `IModbusDeviceProfile` 隔离，JSON 数据驱动，新增品牌不改 C# 代码、不重新编译。非 Modbus 协议扩展采用插件式：只有真实现场需要时才增加 Omron、Allen-Bradley、OPC UA 等模块，并保持 `IIndustrialClient` 抽象不变。
