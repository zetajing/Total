# SDK 核心可扩展平台设计

本文档记录 `IndustrialCommSdk` 从“多个协议客户端集合”演进为“可扩展工业通讯平台”的第一步。当前变更尽量保持非破坏式：不修改现有 `IIndustrialClient` 方法签名，不影响现有 Demo 和业务调用。

## 本轮新增能力

### 1. 协议能力模型

新增：

```text
IndustrialCommSdk/Abstractions/ProtocolCapabilities.cs
IndustrialCommSdk/IndustrialClientPlatformExtensions.cs
```

核心入口：

```csharp
var capabilities = client.GetCapabilities();
```

用途：

- Demo 可以根据能力决定是否显示批量、位地址、字符串、ByteArray、原始 Socket 等功能。
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

建议后续每个协议内部逐步对齐：

- `ModbusAddress : IIndustrialAddress`
- `S7Address : IIndustrialAddress`
- `McAddress : IIndustrialAddress`

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

- 把当前 Modbus 的连续地址合并思想抽象成协议无关模型。
- 后续 S7、MC 可以复用相同的批量规划概念。
- PollingScheduler 可以根据 capabilities / options 自动拆分大批量点位。
- 日志可以统一输出 `OriginalRequestCount / PlannedRequestCount / SavedRequestCount`。

## 当前边界

本轮只建立平台模型，不强行重写已有协议实现：

- `IIndustrialClient` 签名保持不变。
- 现有 `ReadManyAsync / WriteManyAsync` 仍可继续使用。
- Modbus 现有连续地址合并暂时保留在协议实现内部。
- S7 / MC 之后再逐步接入 `BatchSplitPlan`。
- Demo 暂时还未根据 `ProtocolCapabilities` 动态调整 UI。

## 建议下一步 PR

### PR 1：Demo 接入协议能力

目标：

- 在 Modbus / S7 / MC 页面显示当前协议能力。
- 根据 `SupportsBitAddress / SupportsString / SupportsByteArray` 调整输入提示。
- 对过高频轮询显示警告。

### PR 2：PollingScheduler 接入能力限制

目标：

- 根据 `RecommendedMinPollingInterval` 校验订阅周期。
- 根据 `MaxReadItems / MaxAddressSpan / MaxPduBytes` 为未来拆分批量做准备。
- 轮询日志输出计划摘要。

### PR 3：Modbus 批量计划化

目标：

- 把现有 Modbus 连续地址合并结果映射到 `BatchSplitPlan`。
- 日志统一输出 `OriginalRequests / PlannedRequests / SavedRequests`。
- 为 S7 / MC 复用批量规划打基础。

### PR 4：S7 / MC 地址对象实现统一接口

目标：

- 让 `S7Address` 和 `McAddress` 实现 `IIndustrialAddress`。
- 新增 `IAddressParser<TAddress>` 适配。
- 保留旧 `IAddressParser`，避免破坏已有代码。

## 不建议现在做的事

- 不建议马上拆 NuGet 包。先稳定 Abstractions/Core 边界。
- 不建议马上加大量新协议。先让现有 Modbus / S7 / MC 全部接入平台模型。
- 不建议马上改 `IIndustrialClient` 签名。等 1.0 API 冻结前再统一考虑破坏性变更。
