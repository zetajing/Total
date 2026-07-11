# 项目上下文

## 项目目标

`Total` 是一个面向工业现场的 .NET 通讯 SDK 与 WPF 上位机应用。当前方向是让 `IndustrialCommDemo` 成为可以实际运行的软件，但连接、读写、轮询、重连、配置和存储能力必须继续由 `IndustrialCommSdk` 提供，应用层不复制协议实现。

## 上下文维护规则

- 本文件是项目在仓库内的持续记忆和下一次工作的首要入口。
- 每轮代码或结构调整后，必须同步更新：当前状态、已完成内容、验证结果、已知限制和未完成任务。
- 尚未完成、尚未验证或仅有设计结论的事项，也必须写入“未完成任务”，不能只留在聊天记录里。
- `README.md` 面向使用者；本文件面向后续开发和接手项目的人，允许记录实现边界与技术债务。

## 当前分支与提交

- 默认分支：`master`
- 当前远端提交：`d6c3f37 07100`（Demo 产品化、运行中心与配置表单）。
- P0/P1 合并提交：`1afa2eb44394361548f8fd3d313b7c939bed89ca`
- 核心扩展平台已通过 PR #2 合并到 `master`，合并提交为 `efd7b47`。
- “Demo 产品化与结构收敛”已提交并推送；当前工作区只有无用文件清理尚未提交。
- 每次修改代码或文档后都需要提交并推送到 Git。

## 当前工作区：Demo 产品化与结构收敛

本轮目标不是继续堆协议页面，而是形成清晰的两层结构：

```text
IndustrialCommDemo（上位机应用、交互、展示）
    -> IndustrialApplicationRuntime（应用运行边界）
        -> IndustrialCommSdk（连接、协议、轮询、重连、配置、存储）
```

本轮已完成并包含在提交 `d6c3f37`：

1. 新增 `IndustrialCommDemo/Services/IndustrialApplicationRuntime.cs`
   - 统一持有 SDK 的 `IndustrialDeviceHost`。
   - 提供加载、启动、停止和重新加载配置的应用级生命周期。
   - 转换设备状态和实时点位事件供 UI 使用。
   - 隔离多个事件订阅者异常，单个 UI 或数据库处理器失败不会阻断其他处理器。

2. 新增“运行中心”
   - 文件：`IndustrialCommDemo/Views/DeviceRuntimeTab.xaml(.cs)`。
   - 从 `Config/devices.json` 加载全部启用设备。
   - 显示设备名称、协议、端点、连接状态、最近错误和实时点位。
   - 运行中心采集结果会进入已有数据库记录链路。

3. 重组主导航
   - 一级页面：运行中心、设备配置、历史数据、调试与维护。
   - Modbus、S7、MC、Socket、MES、网卡和存储页面归入“调试与维护”。
   - 应用可见名称改为“工业设备运行中心”，应用日志标识由 `DEMO` 改为 `APP`。

4. 产品化设备配置
   - 常用参数使用表单编辑：设备名、协议、IP、端口、串口、从站号、点位文件、轮询周期、重连周期和启用状态。
   - 支持新增、删除设备。
   - 点位使用可增删的表格编辑名称、地址、类型和长度。
   - 设备 JSON 和点位 JSON 继续作为高级入口保留。
   - 新设备点位文件不存在时允许先在界面创建，保存时生成文件。

5. SDK 配置写入能力
   - `IndustrialSdkConfig.ToJson/Save` 统一保存设备配置。
   - `TagTable.ToJson/SaveJson` 统一保存点位表。
   - 序列化会缩进并省略未使用的空字段，Demo 不自行拼 JSON。

6. 无用文件清理（当前尚未提交）
   - 删除空占位文件 `IndustrialCommDemo/MainWindow.Layout.cs` 和 `MainWindow.Network.cs`。
   - 删除不参与产品构建和运行的 `.reasonix` 工具元数据、历史截图与 `reasonix.toml`。
   - 删除旧验证输出目录 `artifacts` 和空的 `.agents/.codex` 目录。
   - `.vs` 属于已忽略的 Visual Studio 缓存；已清理未占用部分，剩余索引数据库正被 IDE 占用，关闭 Visual Studio 后可直接删除。
   - 保留仓库内暂未被 Demo 调用但属于 SDK 对外 API 的 Quick API、SocketBridge 和诊断入口，不能按内部引用数误删。

本轮验证：

- `dotnet build Total.sln -c Release --no-restore`：成功，0 警告，0 错误。
- `IndustrialSdkConfig` JSON 往返：2 台设备，0 个 `null` 字段。
- `TagTable` JSON 往返：5 个点位，0 个 `null` 字段。

## 解决方案结构

| 目录/项目 | 职责 |
| --- | --- |
| `IndustrialCommSdk` | 核心 SDK：统一客户端、协议实现、点位表、配置、轮询、诊断、日志、数据目录、存储和 MES。 |
| `IndustrialCommDemo` | WPF 上位机应用：运行中心负责设备状态和实时点位，其他页面提供协议调试、JSON 配置、MES、数据库、网卡和存储设置。 |
| `IndustrialCommMinimal.WinForms` | WinForms 协议最小系统：独立验证 Modbus TCP/RTU、S7、MC、原始 TCP 和 MES TCP/HTTP。 |

SDK 内部按职责划分：`Abstractions` 放公共契约，`Protocols` 放协议实现，`Transport` 放传输层，`Polling` 放调度，`Diagnostics` 放诊断与日志，`Storage` 放数据库、导出和数据目录策略，`Mes` 放 MES 通讯。Demo 只引用 `IndustrialCommSdk`，不再维护独立的 `LogHelper` 程序集。

稳定性基线：内置 PLC 默认操作超时 5000 ms，请求级超时优先；TCP 传输提供可选业务分帧；客户端通过可选诊断接口公开结构化累计快照，不修改 `IIndustrialClient`。

## 支持的协议

- Modbus TCP / RTU
- Siemens S7
- Mitsubishi MC 3E
- Socket TCP 调试
- FA MES TCP（长连接 + HTTP API 双模式）

统一抽象位于 `IndustrialCommSdk/Abstractions/IIndustrialClient`。协议客户端应继续遵循该接口，不在业务层直接依赖某个 PLC 驱动库。

## 核心可扩展平台现状

`core-extensible-platform` 最初采用非破坏式建模，现在已经推进到“直接接入现有核心实现”：不修改 `IIndustrialClient` 现有方法签名，但核心基类、Modbus/S7/MC 地址模型、三大 PLC 协议批量规划、轮询调度、统一批量诊断和 Demo 能力展示已经开始使用平台模型。

新增文件：

- `IndustrialCommSdk/Abstractions/ProtocolCapabilities.cs`
- `IndustrialCommSdk/Abstractions/IndustrialAddress.cs`
- `IndustrialCommSdk/Abstractions/BatchOptions.cs`
- `IndustrialCommSdk/Abstractions/BatchSplitPlan.cs`
- `IndustrialCommSdk/Abstractions/PlatformInterfaces.cs`
- `IndustrialCommSdk/IndustrialClientPlatformExtensions.cs`
- `IndustrialCommSdk/Diagnostics/BatchPlanDiagnostics.cs`
- `IndustrialCommDemo/Helpers/CapabilityDisplayHelper.cs`
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
   - `SiemensS7Client` 已实现 `IBatchOperationPlanner`。
   - `MitsubishiMcClient` 已实现 `IBatchOperationPlanner`。
   - Modbus 现有连续地址合并已映射为 `BatchSplitPlan`。
   - S7 planner 按 Area / DB / DataType / ByteOffset / BitOffset / MaxReadItems / MaxAddressSpan 生成读计划。
   - MC planner 按 DeviceType / 位字属性 / DataType / DeviceIndex / MaxReadItems / MaxAddressSpan 生成读计划。
   - 三个协议的 `PlanWrite` 当前都保持保守单点组，暂不自动合并写操作。

6. `BatchPlanDiagnostics`
   - 统一格式化 `BATCH_PLAN summary`、`BATCH_PLAN group` 和 `BATCH_PLAN executed_group`。
   - 后续 Modbus、PollingScheduler、S7/MC 优化读取和 Demo 诊断面板都应复用这套字段。
   - 关键字段包括 Source、Device、Protocol、Operation、OriginalRequests、PlannedRequests、SavedRequests、Area、Start、End、Length、Requests、Elapsed 和 Addresses。

7. `PollingScheduler` 接入能力模型和批量计划
   - `SubscribeAsync` 会读取 `client.GetCapabilities()`。
   - 拒绝不支持订阅的协议，例如原始 TCP Socket。
   - 拒绝低于 `RecommendedMinPollingInterval` 的订阅周期。
   - Worker 内保存协议能力。
   - 每轮会合并重复点位，优先用 `IBatchOperationPlanner.PlanRead(...)` 拆批，没有 planner 时按 `MaxReadItems` 保守拆批。
   - 批次独立容错，失败批次返回 `QualityStatus.Bad`，不阻断其他批次。

8. Demo 能力展示
   - 新增 `CapabilityDisplayHelper` 统一格式化协议能力。
   - `ModbusTab` 会根据 TCP / RTU 连接方式显示默认能力，连接后显示实际 client 能力。
   - `SiemensS7Tab` 和 `MitsubishiMcTab` 通过 `ProtocolTabViewModel.CapabilityText` 显示协议能力。
   - 当前只是展示能力，还未根据能力自动隐藏或禁用 UI 控件。

9. 测试更新
   - `PlatformModelTests` 覆盖能力模型、统一地址、Modbus/S7/MC parser 的平台地址形状、Modbus/S7/MC 读计划、批量计划和能力 provider fallback。
   - 移除测试项目之前，曾覆盖协议不支持订阅、低于推荐轮询周期、DeviceId 不匹配、同设备不同客户端拒绝、重复点位合并读取、无 planner 拆批和 planner 拆批。

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

2. 轮询调度历史回归场景
   - 移除测试项目之前，曾覆盖基础订阅事件、ByteArray 变化检测、DeviceId 不匹配、同设备不同客户端拒绝、重复点位合并读取。

## 当前设计边界

- 轮询订阅仍是“SDK 主动周期读取”，不是 PLC 主动推送。
- `IIndustrialClient` 的操作仍按客户端串行化执行，避免同一 TCP/串口连接上的请求响应错位。
- 轮询调度已按设备合并、协议能力校验，并可用 `IBatchOperationPlanner` / `MaxReadItems` 拆分轮询批次。
- Modbus / S7 / MC 都已映射到 `BatchSplitPlan`；S7 / MC 当前只是计划化拆批，底层仍复用现有逐项读取路径，真正协议级合并读取后续单独做。
- `BatchPlanDiagnostics` 已建立统一日志格式，但 Modbus / PollingScheduler 的旧手写 batch 日志仍需逐步替换。
- Demo 已显示 `ProtocolCapabilities`，但还未根据能力动态禁用控件或预警输入。
- `ReadAsync` 通信失败默认返回 `DataValue.Bad`；写入失败仍抛异常。调用方需要按 `Quality` 判断读取结果。
- 当前仓库不再包含独立测试项目；修改核心协议后至少应完成 SDK、Demo 和整个解决方案的构建验证。

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

新增设备时：在“设备配置”页点击“新增设备”，填写常用连接参数，在点位表格中添加点位，然后保存并校验。熟悉配置格式的维护人员也可以使用高级 JSON 入口。

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

Demo 的“设备配置”页提供设备常用参数表单、点位表格和高级 JSON 三种入口。`IndustrialSdkConfig.ToJson/Save` 与 `TagTable.ToJson/SaveJson` 负责统一序列化，Demo 不自行拼接配置 JSON。

## 验证命令

解决方案构建：

```powershell
dotnet build Total.sln --no-restore
```

Demo 构建：

```powershell
dotnet build IndustrialCommDemo\IndustrialCommDemo.csproj --no-restore
```

SDK 构建：

```powershell
dotnet build .\IndustrialCommSdk\IndustrialCommSdk.csproj --no-restore
```

## 已知限制

当前仓库不包含自动化测试项目，构建成功不能替代协议实机验证。

## 已完成：DeviceHost 基础运行时

已完成的能力：

1. 从 `devices.json` 自动加载所有启用设备。
2. 管理连接、固定周期重连、健康状态和运行日志。
3. 按设备的点位表执行周期批量读取并上报事件。
4. 提供按“设备名 + 点位名”的读写入口和状态事件。

Modbus 品牌差异通过 `IModbusDeviceProfile` 隔离，JSON 数据驱动，新增品牌不改 C# 代码、不重新编译。非 Modbus 协议扩展采用插件式：只有真实现场需要时才增加 Omron、Allen-Bradley、OPC UA 等模块，并保持 `IIndustrialClient` 抽象不变。

## 未完成任务

以下任务按当前优先级排序。完成后应移动到对应“已完成”章节，并记录验证结果。

### P0：先完成本轮产品化闭环

- [ ] 对新的主导航、运行中心、设备表单和点位表格执行一次人工界面冒烟验证；当前只完成编译和序列化往返验证。
- [ ] 保存设备配置后，明确提示运行中心配置已变化，并提供安全的“保存并重新加载运行中心”；运行中不能静默重载导致设备突然断开。
- [ ] 增加配置未保存状态，切换设备、重新加载或关闭软件前提示，避免表格编辑丢失。
- [ ] 根据协议动态显示表单字段：TCP 显示 IP/端口，RTU 显示串口参数，S7 显示 CPU/Rack/Slot，避免现场人员看到无关输入项。
- [ ] 为新增设备提供可选的点位模板，不只创建空白点位行。
- [ ] 本次无用文件清理完成后提交并推送，随后更新本节的提交号和远端状态。

### P1：让运行中心成为真正的操作页面

- [ ] 增加设备总数、在线数、故障数、Bad 点位数等概览卡片。
- [ ] 增加设备筛选、点位搜索、质量筛选和最后更新时间提示。
- [ ] 为可写点位增加受控写入入口和确认提示，写入仍必须调用 SDK 的点位名 API。
- [ ] 增加报警/异常列表，区分连接故障、轮询失败、Bad 质量和数据库记录失败。
- [ ] 明确应用启动策略：手动启动、启动后自动运行，或由设置项控制；当前为手动启动。
- [ ] 数据库记录开关与运行中心状态需要更直观地联动和展示。

### P1：SDK 与质量保障

- [x] 已恢复轻量 `IndustrialCommSdk.Tests` 回归工程；当前覆盖超时与诊断、TCP 分帧、配置、批量计划以及 MES HTTP 重试，共 20 项测试。
- [ ] 为 `IndustrialSdkConfig.ToJson/Save` 和 `TagTable.ToJson/SaveJson` 补自动化回归验证；当前只有 PowerShell 运行时往返检查。
- [ ] 替换 Modbus / PollingScheduler 剩余手写 batch 日志，统一使用 `BatchPlanDiagnostics`。
- [ ] 根据 `ProtocolCapabilities` 动态禁用调试页面不支持的操作和输入项。
- [ ] S7 / MC 当前只有批量计划，仍需实现真正的协议级连续合并读取。
- [ ] 写批量计划目前仍是保守单点组，需结合真实设备限制决定是否合并。

### 2026-07-11 SDK 持续优化

- 修复 MES HTTP 在 5xx 响应下重试计数不递增、可能无限循环的可靠性缺陷。
- `HttpResponseMessage` 现在按请求及时释放，避免长时间运行时连接与资源泄漏。
- 新增自定义 `HttpMessageHandler`（可指定所有权）及外部 `HttpClient` 注入构造方式；外部客户端不会被 SDK 修改或释放。
- SDK 超时改为请求级关联取消，不依赖修改共享 `HttpClient.Timeout`。
- 新增 4 项 MES HTTP 自动化测试，验证 5xx 有界重试、重试恢复、4xx 不重试、处理器所有权及外部客户端生命周期；完整测试为 20/20 通过，Release 全解决方案构建 0 警告、0 错误。

### P2：交付与维护

- [ ] 增加发布目录生成方式和最小部署说明，输出应包含 EXE、SDK 依赖和可编辑 `Config` 目录。
- [ ] 评估是否把项目名 `IndustrialCommDemo` 改为正式产品名；在功能稳定前不做大范围命名迁移。
- [x] 已重写 README，从约 900 行压缩为面向使用者的项目概览、构建测试、快速开始、配置、MES HTTP、Demo 和文档入口；历史可靠性与协议审查细节由 `docs` 承载。
- [ ] 只有真实项目需要时才扩展 Omron、Allen-Bradley、OPC UA 等协议，当前不提前堆模块。
