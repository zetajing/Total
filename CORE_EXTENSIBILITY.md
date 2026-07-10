# SDK 核心可扩展平台设计

本文档记录 `IndustrialCommSdk` 从“多个协议客户端集合”演进为“可扩展工业通讯平台”的当前状态。当前改造保持非破坏式：不修改现有 `IIndustrialClient` 方法签名，不影响现有 Demo 和业务调用；但平台模型已经接入核心实现、三大 PLC 协议、轮询调度和 Demo 页面。

## 当前新增平台组件

```text
IndustrialCommSdk/Abstractions/ProtocolCapabilities.cs
IndustrialCommSdk/Abstractions/IndustrialAddress.cs
IndustrialCommSdk/Abstractions/BatchOptions.cs
IndustrialCommSdk/Abstractions/BatchSplitPlan.cs
IndustrialCommSdk/Abstractions/PlatformInterfaces.cs
IndustrialCommSdk/IndustrialClientPlatformExtensions.cs
IndustrialCommSdk/Diagnostics/BatchPlanDiagnostics.cs
IndustrialCommDemo/Helpers/CapabilityDisplayHelper.cs
```

## 1. 协议能力模型

核心入口：

```csharp
var capabilities = client.GetCapabilities();
```

`ProtocolCapabilities` 描述协议是否支持批量、优化批量、位地址、字符串、ByteArray、原始传输、连接诊断、原生异步、最大批量数量、最大地址跨度、PDU 限制、推荐轮询周期和默认超时。

当前默认能力覆盖：

- Modbus TCP
- Modbus RTU
- Siemens S7
- Mitsubishi MC 3E
- TCP Socket

`IndustrialClientBase` 已实现 `IProtocolCapabilityProvider`，所有继承基类的协议客户端默认具备能力描述。第三方协议客户端可以实现 `IProtocolCapabilityProvider` 覆盖默认能力。

## 2. 统一地址抽象

核心接口：

```csharp
public interface IIndustrialAddress
{
    string Original { get; }
    string Normalized { get; }
    string Area { get; }
    int Offset { get; }
    int? Bit { get; }
    bool IsBitAddress { get; }
}
```

当前已接入：

- `ModbusAddress : IIndustrialAddress`
- `S7Address : IIndustrialAddress`
- `McAddress : IIndustrialAddress`
- `ModbusAddressParser : IAddressParser<ModbusAddress>`
- `S7AddressParser : IAddressParser<S7Address>`
- `McAddressParser : IAddressParser<McAddress>`

旧接口 `IAddressParser.Parse(string) -> object` 仍保留，协议内部逐步优先使用 `ParseTyped`，减少 object cast。

## 3. 批量选项、拆分计划和诊断

核心类型：

```csharp
BatchReadOptions
BatchWriteOptions
BatchSplitPlan
BatchRequestGroup
IBatchOperationPlanner
BatchPlanDiagnostics
```

用途：

- 把协议私有的地址合并和拆批规则抽象成协议无关模型。
- 让 `PollingScheduler` 可以自动消费协议 planner。
- 让日志和 Demo 统一展示 `OriginalRequestCount / PlannedRequestCount / SavedRequestCount`。
- 让第三方协议只需实现 `IBatchOperationPlanner` 即可接入统一轮询拆批。

`BatchPlanDiagnostics` 统一格式化：

- `BATCH_PLAN summary`：逻辑批次的总览。
- `BATCH_PLAN group`：计划中的每个物理请求组。
- `BATCH_PLAN executed_group`：协议执行阶段真正发出的物理批次。

字段包括 Source、Device、Protocol、Operation、OriginalRequests、PlannedRequests、SavedRequests、Sequence、Area、Start、End、Length、DataType、Requests、Elapsed 和 Addresses。

## 4. 三大 PLC 协议接入 batch planner

### Modbus

`ModbusClientBase : IBatchOperationPlanner`

- `PlanRead(...)` 使用现有 Modbus 区域分组和连续地址合并规则生成 `BatchSplitPlan`。
- `ReadManyCoreAsync` 先构建计划摘要，再复用原有成熟读取执行逻辑。
- `PlanWrite(...)` 当前保守为单点组，暂不自动合并写操作。

### Siemens S7

`SiemensS7Client : IBatchOperationPlanner`

`PlanRead(...)` 按以下边界生成读取批次：

- Area：DB / Memory / Input / Output
- DB number
- DataType
- ByteOffset / BitOffset
- `MaxReadItems`
- `MaxAddressSpan`

连续或同跨度内的同区点位会进入同一计划组。`PlanWrite(...)` 保守为单点组。

### Mitsubishi MC

`MitsubishiMcClient : IBatchOperationPlanner`

`PlanRead(...)` 按以下边界生成读取批次：

- DeviceType：D / W / R / ZR / M / X / Y 等
- 位设备 / 字设备属性
- DataType
- Device index
- `MaxReadItems`
- `MaxAddressSpan`

连续或同跨度内的同设备区点位会进入同一计划组。`PlanWrite(...)` 保守为单点组。

## 5. PollingScheduler 接入能力和 planner

`PollingScheduler.SubscribeAsync` 现在会：

1. 读取 `client.GetCapabilities()`。
2. 拒绝 `SupportsSubscriptions == false` 的协议，例如原始 TCP Socket。
3. 拒绝低于 `RecommendedMinPollingInterval` 的订阅周期。
4. Worker 内保存协议能力，用于轮询拆批和日志增强。

每轮轮询执行现在会：

1. 按设备合并到期订阅的重复点位。
2. 如果客户端实现 `IBatchOperationPlanner`，优先调用 `PlanRead(...)` 生成 `BatchSplitPlan`。
3. 按 `BatchSplitPlan.Groups` 分批调用 `ReadManyAsync`。
4. 如果客户端没有 planner，则按 `ProtocolCapabilities.MaxReadItems` 做保守拆分。
5. 每个批次独立容错；某个批次失败时，该批次点位返回 `QualityStatus.Bad`，不会阻断其他批次。
6. 批次结果按请求 key 合并，再按每个订阅的原始点位顺序上报。

## 6. Demo 接入协议能力展示

Demo 已经在协议页面展示能力信息：

- `ModbusTab` 根据 TCP / RTU 连接方式显示默认能力，连接后显示实际 client 能力。
- `SiemensS7Tab` 通过共享 `ProtocolTabViewModel.CapabilityText` 展示 S7 能力。
- `MitsubishiMcTab` 通过共享 `ProtocolTabViewModel.CapabilityText` 展示 MC 能力。
- `CapabilityDisplayHelper` 统一格式化 DisplayName、能力标签、最大读写数量、最大地址跨度、PDU 限制、推荐轮询周期和默认超时。

当前 Demo 仍只做展示，还没有根据能力自动隐藏或禁用输入控件。

## 7. 测试覆盖

`PlatformModelTests` 已覆盖：

- 协议能力默认值
- 通用 `IndustrialAddress`
- Modbus / S7 / MC parser 输出的 `IIndustrialAddress` 形状
- Modbus `PlanRead` 连续地址合并计划
- Modbus 分离地址范围保持独立计划组
- S7 同 DB 连续地址计划
- MC 同设备区连续地址计划
- 批量计划统计
- 能力 provider override 与 fallback

移除测试项目之前，轮询调度曾覆盖以下场景：

- 协议不支持订阅时拒绝
- 订阅周期低于推荐最小值时拒绝
- DeviceId 不匹配
- 同设备不同客户端实例拒绝
- 重复点位合并读取
- 无 planner 时按 `MaxReadItems` 拆分轮询批次
- 有 planner 时优先使用 `BatchSplitPlan` 拆分轮询批次

## 当前边界

- `IIndustrialClient` 签名保持不变。
- 现有 `ReadManyAsync / WriteManyAsync` 仍可继续使用。
- Modbus / S7 / MC 都已建立 `BatchSplitPlan`，轮询调度器已可使用 planner 拆分读取批次。
- S7 / MC 的 planner 当前只负责拆批计划，底层读取执行仍复用现有逐项 `ReadManyAsync` 路径；真正的协议级多点合并读取可以后续单独做。
- Demo 已展示 `ProtocolCapabilities`，但还未按能力自动禁用控件或调整输入项。
- `BatchPlanDiagnostics` 已覆盖计划组和执行组日志格式；后续还需要逐步替换各处手写日志为统一 formatter。
- NuGet 拆包暂缓，先稳定 Abstractions/Core 边界。

## 建议下一步

1. 把 Modbus、PollingScheduler 和后续 S7/MC 优化读取的手写 batch 日志替换成 `BatchPlanDiagnostics`。
2. Demo 根据 `ProtocolCapabilities` 动态启用/禁用数据类型和轮询输入。
3. 在 planner 边界上为 S7 / MC 实现真正协议级合并读取。
4. 抽出能力矩阵和 batch plan 快照，为诊断包和生产排障准备。
