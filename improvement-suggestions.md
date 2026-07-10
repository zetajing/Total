# 后续修改意见：参考优秀工业通讯项目

本文档基于 `Total / IndustrialCommSdk` 当前 `master` 状态，以及几个成熟工业通讯项目的公开设计经验整理。目标不是盲目照抄，而是把项目从“能用的 SDK + Demo”继续推进到“稳定、可扩展、可发布、可交付”的工程形态。

## 参考项目

### S7.NetPlus

可借鉴点：

- 聚焦一个协议，把 Siemens S7 的连接、地址、读写、类型映射打磨清楚。
- 支持 S7-200 / S7-300 / S7-400 / S7-1200 / S7-1500。
- 测试层使用 Snap7 Server 做协议行为验证。
- README 简洁，但清楚说明支持框架、安装、构建、测试和 NuGet 使用方式。

对本项目启发：

- `SiemensS7Client` 不要只停留在“能读写”，应继续补齐 S7 地址模型、类型映射、DB 映射、批量读写、连接诊断和模拟测试。
- S7 的测试不要只做 parser 单测，后续应引入 Snap7 / 仿真 Server / 可选真实 PLC 集成测试。

### NModbus

可借鉴点：

- 项目定位明确：C# Modbus 协议实现。
- 把 Serial ASCII、Serial RTU、TCP、UDP 等 transport 分开维护。
- 更强调接口抽象，便于扩展 Slave、Network、自定义 Function Code。
- 有 `Samples`、`UnitTests`、`IntegrationTests` 等目录，学习成本低。

对本项目启发：

- 当前 `Modbus TCP / RTU` 已经可用，但 transport、profile、protocol 仍可以进一步拆清楚。
- 自定义功能码、品牌 profile、Modbus Server/Slave 模拟器可以后续独立成扩展模块。
- 示例代码要从 README 中拆出一部分放进 `samples/`，避免 README 越写越长。

### OPC UA .NET Standard

可借鉴点：

- 工程化成熟：文档、示例、测试、覆盖率、迁移指南、安全策略、NuGet 包拆分都非常完整。
- 模块划分清楚：Core / Client / Server / PubSub / GDS / Complex Types / Device Integration 等能力分层。
- 提供 Reference Server / Reference Client / Docker / Samples，让用户能快速验证。
- 明确版本线：新版本功能开发、旧版本安全和关键 bug 修复分开维护。

对本项目启发：

- `IndustrialCommSdk` 后续应逐步拆包：Core、Protocols、Storage、Demo、Samples。
- 需要建立“版本说明 / 迁移指南 / 安全说明 / 兼容性矩阵”。
- 如果以后做 OPC UA Client，不建议自己手写协议栈，应直接基于成熟库封装成 `IIndustrialClient`。

### libplctag

可借鉴点：

- API 非常克制，强调跨语言可包装性。
- 用简单、稳定的 tag 访问模型覆盖 Allen-Bradley 和 Modbus 场景。
- 示例程序覆盖面广，方便用户直接复制验证。

对本项目启发：

- `Tag` / `TagTable` 是正确方向，应继续强化为 SDK 的第一入口。
- 对上层业务用户来说，应该尽量隐藏协议细节，只暴露“设备 + 点位 + 值 + 质量 + 时间戳”。
- 后续如果加 Allen-Bradley / EtherNet/IP，不应一开始就暴露复杂 CIP 对象模型，先做稳定 tag 访问模型。

## 当前项目已经做对的地方

1. 有统一抽象：`IIndustrialClient`、`ReadRequest`、`WriteRequest`、`DataValue`、`BatchReadResult` 已经能把 Modbus、S7、MC 等协议统一起来。
2. 有配置化部署：`devices.json` + `points/*.json` 已经很适合工业现场快速配置。
3. 有 Demo：WPF Demo 对调试 PLC、串口、MES、数据库非常重要，适合现场工程师使用。
4. 有 P0/P1 基础可靠性优化：连接生命周期、批量超时、健康状态、轮询合并已经比原来稳很多。
5. 有数据库旁路写入：历史数据记录与实时通讯隔离，这个方向正确。

## 主要短板

1. 项目边界还不够清晰：SDK、Demo、MES、数据库、配置、协议都在一个仓库里，但还没有形成明确的包边界。
2. 测试层还不够工业化：大部分是单测，缺少协议仿真测试、回环测试、真实设备可选测试、压力测试。
3. 文档结构逐渐变长：README 已经承担了太多内容，后续需要 Docs 目录分章节维护。
4. 版本发布体系不足：缺少 SemVer、CHANGELOG、NuGet 打包说明、兼容性矩阵。
5. 地址和类型模型仍偏弱：不同协议的地址语义、批量限制、最大 PDU、字节序、位/字边界还需要统一描述。
6. 运行时观测能力不足：日志已有，但缺少指标、健康事件、统计快照、慢请求记录和轮询性能报表。

## 修改建议

## P0：先补质量门禁和工程安全线

这些建议优先级最高，不建议继续大规模加协议前跳过。

### 1. 建立 CI

新增 GitHub Actions：

- restore
- build `IndustrialCommSdk`
- test `IndustrialCommSdk.Tests`
- build `IndustrialCommDemo`，必要时跳过旧式 `Refresh Logs`
- 上传测试结果

建议工作流：

```text
.github/workflows/dotnet-ci.yml
```

原因：

- 现在已经合并了较大 P0/P1 改动，后续必须让每个 PR 自动验证。
- 当前环境曾无法本地 clone / test，CI 是最稳的兜底。

### 2. 增加 CHANGELOG 和版本策略

新增：

```text
CHANGELOG.md
docs/versioning.md
```

建议版本策略：

- `0.x`：内部快速迭代，允许破坏性调整。
- `1.0`：确定 `IIndustrialClient`、`Tag`、配置模型、错误模型后再发布。
- 每个协议模块单独记录兼容性。

### 3. 把 README 拆成文档入口

README 保留：

- 项目是什么
- 快速开始
- 支持协议
- 最小代码
- 文档索引

详细内容迁移到：

```text
docs/getting-started.md
docs/configuration.md
docs/modbus.md
docs/siemens-s7.md
docs/mitsubishi-mc.md
docs/polling.md
docs/storage.md
docs/mes.md
docs/troubleshooting.md
```

### 4. 增加真实构建限制说明

当前 `Total.sln` 可能被旧项目 `Refresh Logs` 拖累，建议明确拆分：

```text
Total.sln                // 保留全部项目
IndustrialCommSdk.sln    // 只包含 SDK + Tests + Demo
```

或者至少增加：

```text
build-sdk.ps1
build-demo.ps1
test-sdk.ps1
```

## P1：把 SDK 核心做成可扩展平台

### 5. 拆分包边界

建议目标结构：

```text
src/IndustrialCommSdk.Abstractions
src/IndustrialCommSdk.Core
src/IndustrialCommSdk.Protocols.Modbus
src/IndustrialCommSdk.Protocols.SiemensS7
src/IndustrialCommSdk.Protocols.MitsubishiMc
src/IndustrialCommSdk.Storage.SqlServer
src/IndustrialCommSdk.Mes
samples/
tools/
docs/
```

短期不一定真的拆 NuGet，但先按命名空间和目录拆清楚。

### 6. 建立协议能力描述模型

新增类似：

```csharp
public sealed class ProtocolCapabilities
{
    public bool SupportsBatchRead { get; set; }
    public bool SupportsBatchWrite { get; set; }
    public bool SupportsBitAddress { get; set; }
    public int MaxReadItems { get; set; }
    public int MaxWriteItems { get; set; }
    public int MaxPduBytes { get; set; }
    public TimeSpan RecommendedMinPollingInterval { get; set; }
}
```

用途：

- Demo 根据能力决定 UI 是否允许批量、字符串、位写入。
- 轮询调度器根据能力自动拆分批量。
- 文档可以自动生成协议能力矩阵。

### 7. 统一地址模型

当前地址主要还是 string。建议逐步演进：

```csharp
public interface IIndustrialAddress
{
    string Original { get; }
    string Normalized { get; }
    string Area { get; }
    int Offset { get; }
    int? Bit { get; }
}
```

每个协议内部保留强类型地址：

- `ModbusAddress`
- `S7Address`
- `McAddress`

对外仍允许 string，但内部不要反复 parse。

### 8. 加强批量读写策略

建议新增：

```csharp
BatchReadOptions
BatchWriteOptions
BatchSplitPlan
```

能力：

- per-request timeout
- total timeout
- max items
- max span
- max pdu bytes
- continue on error
- preserve order
- split by area / type / contiguous range

当前 Modbus 已有连续地址合并，下一步应把这个思想抽象出来，给 S7、MC 复用。

### 9. 自动重连状态机统一化

目前 DeviceHost 有重连，S7 也做了局部失败清理。建议统一成：

```text
Disconnected -> Connecting -> Connected -> Degraded -> Reconnecting -> Faulted
```

并明确：

- 哪些异常触发重连
- 读失败是否自动重试
- 写失败是否允许重试
- 重连期间读写请求怎么处理
- 轮询失败多少次后降级

## P2：提高现场可观测性和可维护性

### 10. 增加运行时指标

建议增加：

```csharp
IndustrialClientMetrics
PollingMetrics
TransportMetrics
StorageMetrics
```

至少包含：

- total reads / writes
- success / failure count
- timeout count
- average latency
- p95 latency
- consecutive failures
- last success timestamp
- last failure reason
- polling cycle duration
- merged request count
- saved request count

Demo 可以显示这些指标，现场排查非常有用。

### 11. 统一日志事件 ID

当前日志是字符串为主。建议增加事件 ID：

```text
COMM_CONNECT_START
COMM_CONNECT_OK
COMM_CONNECT_FAIL
COMM_READ_START
COMM_READ_OK
COMM_READ_BAD
COMM_WRITE_FAIL
POLL_CYCLE_START
POLL_CYCLE_SUMMARY
STORAGE_WRITE_FAIL
```

这样后续无论写文件、控制台、数据库、OpenTelemetry，都可以统一筛选。

### 12. 增加诊断报告导出

建议 Demo 提供“一键导出诊断包”：

```text
diagnostics.zip
  config-sanitized.json
  point-table-summary.json
  sdk-log-last-30min.txt
  demo-log-last-30min.txt
  health-snapshot.json
  metrics-snapshot.json
```

注意：导出前脱敏 Host、账号、连接字符串等敏感信息。

## P3：协议扩展建议

不建议现在立刻堆很多协议。顺序建议：

1. 先把现有 Modbus / S7 / MC 打磨到稳定。
2. 再做 OPC UA Client，因为它更适合上层系统集成。
3. 然后根据现场需求选择：
   - Omron FINS
   - Keyence KV
   - Allen-Bradley EtherNet/IP
   - MQTT / Sparkplug B

### OPC UA Client 建议

不要自己实现 OPC UA 协议栈，直接封装成熟库，并映射到：

```text
IIndustrialClient
Tag
DataValue
QualityStatus
IndustrialDeviceHost
```

OPC UA 的 `StatusCode`、SourceTimestamp、ServerTimestamp 应保留到扩展字段，不要简单丢弃。

### Allen-Bradley / EtherNet/IP 建议

参考 libplctag 的思路，先实现简单 tag 访问：

```csharp
ReadAsync<int>("Program:MainProgram.Speed")
WriteAsync("RunCommand", true)
```

不要一开始就暴露完整 CIP 对象模型，否则 SDK 会快速复杂化。

## 建议路线图

### 第一阶段：工程化兜底

- 添加 GitHub Actions CI。
- 增加 CHANGELOG。
- 拆 README 到 docs。
- 增加 SDK-only sln 或 build script。

### 第二阶段：核心模型稳定

- 增加 `ProtocolCapabilities`。
- 增加 `BatchReadOptions / BatchWriteOptions`。
- 抽象批量拆分计划。
- 强化 `Tag` / `TagTable` 为主要用户入口。

### 第三阶段：测试工业化

- Modbus 加本地 loopback server 测试。
- S7 加 Snap7 Server / 仿真测试。
- MC 加 frame parser / response parser 黄金样本测试。
- DeviceHost 加重连、降级、恢复测试。
- Polling 加压力和漂移测试。

### 第四阶段：产品化

- NuGet 打包。
- Samples 目录。
- Demo 诊断包。
- 版本兼容矩阵。
- 安全文档。

### 第五阶段：扩协议

- OPC UA Client。
- Omron FINS / Keyence KV / Allen-Bradley 按现场优先级推进。
- MQTT / Sparkplug B 用于上层系统集成。

## 最推荐下一步

下一步不要先加新协议，建议先做这 4 个 PR：

1. `ci: add sdk build and test workflow`
2. `docs: split README into docs index`
3. `core: add protocol capability model`
4. `core: introduce batch operation options and split plan`

这 4 个做完后，项目就从“代码能跑”提升到“可持续演进”。后面继续加协议、加 Demo 功能、打包 NuGet，风险会小很多。
