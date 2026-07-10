# SDK 核心可扩展平台设计

本文档记录 `IndustrialCommSdk` 从“多个协议客户端集合”演进为“可扩展工业通讯平台”的第一步。当前改造保持非破坏式：不修改现有 `IIndustrialClient` 方法签名，不影响现有 Demo 和业务调用；但已经把平台模型接入核心实现、轮询调度和 Demo 界面。

## 本轮新增能力

### 1. 协议能力模型

新增：

```text
IndustrialCommSdk/Abstractions/ProtocolCapabilities.cs
IndustrialCommSdk/IndustrialClientPlatformExtensions.cs
IndustrialCommDemo/Helpers/CapabilityDisplayHelper.cs
```

核心入口：

```csharp
var capabilities = client.GetCapabilities();
```

用途：

- Demo 可以显示协议能力、批量限制、PDU 限制、推荐轮询周期和默认超时。
- DeviceHost / PollingScheduler 可以根据 `RecommendedMinPollingInterval` 给出轮询周期建议。
- 后续文档可以自动生成协议能力矩阵。
- 第三方协议客户端可实现 `IProtocolCapabilityProvider` 覆盖默认能力。

当前默认能力覆盖：

- Modbus TCP
- Modbus RTU
- Siemens S7
- Mitsubishi MC 3E
- TCP Socket

### 2. 统一地址抽象

新增：

```text
IndustrialCommSdk/Abstractions/IndustrialAddress.cs
IndustrialCommSdk/Abstractions/PlatformInterfaces.cs
```

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

用途：

- 保留对外 string 地址入口，降低使用门槛。
- 协议内部逐步迁移到强类型地址，减少反复 parse 和 object cast。
- 点位表校验、批量合并、文档生成、错误提示可以共享统一地址形状。

当前已接入：

- `ModbusAddress : IIndustrialAddress`
- `S7Address : IIndustrialAddress`
- `McAddress : IIndustrialAddress`
- `ModbusAddressParser : IAddressParser<ModbusAddress>`
- `S7AddressParser : IAddressParser<S7Address>`
- `McAddressParser : IAddressParser<McAddress>`

### 3. 批量选项和拆分计划

新增：

```text
IndustrialCommSdk/Abstractions/BatchOptions.cs
IndustrialCommSdk/Abstractions/BatchSplitPlan.cs
```

核心类型：

```csharp
BatchReadOptions
BatchWriteOptions
BatchSplitPlan
BatchRequestGroup
IBatchOperationPlanner
```

用途：

- 把协议私有的地址合并和拆批规则抽象成协议无关模型。
- 轮询调度可以根据 capabilities / options 自动拆分大批量点位。
- 日志和 Demo 可以统一展示 `OriginalRequestCount / PlannedRequestCount / SavedRequestCount`。
- 第三方协议只要实现 `IBatchOperationPlanner`，就可以被 `PollingScheduler` 自动消费。

当前已接入：

- `ModbusClientBase : IBatchOperationPlanner`
- `SiemensS7Client : IBatchOperationPlanner`
- `MitsubishiMcClient : IBatchOperationPlanner`
- `PollingScheduler` 已优先使用 `IBatchOperationPlanner` 生成轮询读取批次
- 没有 planner 的协议会按 `ProtocolCapabilities.MaxReadItems` 做保守拆分

## 已经直接重构接入的部分

### 1. `IndustrialClientBase` 接入能力模型

`IndustrialClientBase` 已实现 `IProtocolCapabilityProvider`，所有继承基类的协议客户端天然具备：

```csharp
client.GetCapabilities();
```

默认能力来自：

```csharp
ProtocolCapabilities.ForProtocol(client.Kind)
```

协议客户端后续如果因为配置不同导致能力不同，可以 override：

```csharp
public override ProtocolCapabilities Capabilities { get { ... } }
```

### 2. Modbus / S7 / MC 地址接入统一地址模型

`ModbusAddress`、`S7Address` 和 `McAddress` 已开始提供统一的：

- `Original`
- `Normalized`
- `Area`
- `Offset`
- `Bit`
- `IsBitAddress`

这样后续批量规划、点位表校验、诊断日志、文档生成可以不再依赖协议私有字段。

### 3. Parser 接入强类型接口

保留旧接口：

```csharp
IAddressParser.Parse(string) -> object
```

新增强类型接口：

```csharp
IAddressParser<ModbusAddress>.Parse(string)
IAddressParser<S7Address>.Parse(string)
IAddressParser<McAddress>.Parse(string)
```

协议内部已开始使用 `ParseTyped`，减少强制转换和 object 返回值扩散。

### 4. 三大 PLC 协议接入 `BatchSplitPlan`

#### Modbus

`ModbusClientBase.PlanRead(...)` 使用现有 Modbus 区域分组和连续地址合并规则生成 `BatchSplitPlan`。`ReadManyCoreAsync` 也会先构建并记录计划摘要，再复用原有读取执行逻辑。

#### Siemens S7

`SiemensS7Client.PlanRead(...)` 会按：

- Area：DB / Memory / Input / Output
- DB number
- DataType
- ByteOffset / BitOffset
- `MaxReadItems`
- `MaxAddressSpan`

生成读取批次。连续或同跨度内的同区点位会进入同一计划组。写入规划当前保守为“单逻辑写入 = 单物理写入组”。

#### Mitsubishi MC

`MitsubishiMcClient.PlanRead(...)` 会按：

- DeviceType：D / W / R / ZR / M / X / Y 等
- 位设备 / 字设备属性
- DataType
- Device index
- `MaxReadItems`
- `MaxAddressSpan`

生成读取批次。写入规划同样保守为单点组，避免误合并写操作。

计划中会记录：

- `OriginalRequestCount`
- `PlannedRequestCount`
- `SavedRequestCount`
- 每个 `BatchRequestGroup` 的 Area、StartOffset、EndOffset、DataType 和原始请求列表

### 5. PollingScheduler 接入协议能力和批量计划

`PollingScheduler.SubscribeAsync` 现在会：

1. 读取 `client.GetCapabilities()`。
2. 拒绝 `SupportsSubscriptions == false` 的协议，例如原始 TCP Socket。
3. 拒绝低于 `RecommendedMinPollingInterval` 的订阅周期。
4. Worker 内保存协议能力，用于轮询拆批和日志增强。

每轮轮询执行现在会：

1. 先按设备合并到期订阅的重复点位。
2. 如果客户端实现 `IBatchOperationPlanner`，优先调用 `PlanRead(...)` 生成 `BatchSplitPlan`。
3. 按 `BatchSplitPlan.Groups` 分批调用 `ReadManyAsync`。
4. 如果客户端没有 planner，则按 `ProtocolCapabilities.MaxReadItems` 做保守拆分。
5. 每个批次独立容错；某个批次失败时，该批次的点位返回 `QualityStatus.Bad`，不会阻断其他批次。
6. 批次结果按请求 key 合并，再按每个订阅的原始点位顺序上报。

### 6. Demo 接入协议能力展示

Demo 现在已经在协议页面展示能力信息：

- `ModbusTab` 根据 TCP / RTU 连接方式显示默认能力，连接后显示实际 client 能力。
- `SiemensS7Tab` 通过共享 `ProtocolTabViewModel.CapabilityText` 展示 S7 能力。
- `MitsubishiMcTab` 通过共享 `ProtocolTabViewModel.CapabilityText` 展示 MC 能力。
- `CapabilityDisplayHelper` 统一格式化 DisplayName、能力标签、最大读写数量、最大地址跨度、PDU 限制、推荐轮询周期和默认超时。

当前 Demo 仍只做“展示能力”，还没有根据能力自动隐藏或禁用输入控件。

### 7. 测试覆盖

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

`PollingSchedulerTests` 已覆盖：

- 协议不支持订阅时拒绝
- 订阅周期低于推荐最小值时拒绝
- DeviceId 不匹配
- 同设备不同客户端实例拒绝
- 重复点位合并读取
- 无 planner 时按 `MaxReadItems` 拆分轮询批次
- 有 planner 时优先使用 `BatchSplitPlan` 拆分轮询批次

## 当前边界

本轮已经开始直接接入平台模型，但仍然避免一次性大改：

- `IIndustrialClient` 签名保持不变。
- 现有 `ReadManyAsync / WriteManyAsync` 仍可继续使用。
- Modbus / S7 / MC 都已建立 `BatchSplitPlan`，轮询调度器已可使用 planner 拆分读取批次。
- S7 / MC 的 planner 当前只负责拆批计划，底层读取执行仍复用现有逐项 `ReadManyAsync` 路径；真正的协议级多点合并读取可以后续单独做。
- Demo 已展示 `ProtocolCapabilities`，但还未按能力自动禁用控件或调整输入项。
- NuGet 拆包暂缓，先稳定 Abstractions/Core 边界。

## 建议下一步 PR

### PR 1：统一日志和诊断输出

目标：

- 将 `BatchSplitPlan` 摘要输出成统一结构化日志事件。
- Demo 后续可以展示 Original / Planned / Saved 请求数。
- 为诊断包输出批量计划、能力矩阵和慢请求数据打基础。

### PR 2：Demo 根据协议能力动态调整 UI

目标：

- 根据 `SupportsBitAddress / SupportsString / SupportsByteArray` 动态启用或禁用数据类型。
- 对低于推荐轮询周期的输入即时显示警告。
- 在批量读取前预览 `MaxReadItems / MaxAddressSpan / MaxPduBytes` 风险。

### PR 3：S7 / MC 协议级优化读取

目标：

- 在 planner 已有边界上，为 S7 / MC 逐步实现真正的协议级合并读取。
- 保留结果顺序映射，避免优化影响调用方结果顺序。
- 明确位地址、字地址、DB 区、设备区的合并边界。

## 不建议现在做的事

- 不建议马上拆 NuGet 包。先稳定 Abstractions/Core 边界。
- 不建议马上加大量新协议。先让现有 Modbus / S7 / MC 全部接入平台模型。
- 不建议马上改 `IIndustrialClient` 签名。等 1.0 API 冻结前再统一考虑破坏性变更。
