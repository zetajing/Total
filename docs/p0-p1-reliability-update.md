# P0/P1 可靠性优化说明

本文件记录 `PR #1 Optimize industrial SDK reliability for P0/P1` 合并后的关键设计变化，便于后续继续扩展协议或排查现场问题。

## 合并信息

- 合并分支：`optimize-p0-p1` → `master`
- 合并提交：`1afa2eb44394361548f8fd3d313b7c939bed89ca`
- 影响范围：
  - `IndustrialCommSdk/Protocols/Mc/MitsubishiMcClient.cs`
  - `IndustrialCommSdk/Protocols/S7/SiemensS7Client.cs`
  - `IndustrialCommSdk/Internal/IndustrialClientBase.cs`
  - `IndustrialCommSdk/Polling/PollingScheduler.cs`
  - `IndustrialCommSdk.Tests/PollingSchedulerTests.cs`

## P0：必须先做稳的部分

### Mitsubishi MC

三菱 MC 地址解析已从散落判断改为集中元数据表，统一维护：

- 设备代码
- 地址进制
- 位设备 / 字设备分类
- 地址范围校验

重点修正：`ZR` 不再按十六进制解析。`X / Y / W` 等地址仍按协议语义使用十六进制。后续新增 `B / F / V / SM / SB / SW / TS / TC / CS / CC` 等设备类型时，应优先扩展元数据表，而不是继续写 `type == ...` 条件判断。

### Siemens S7

S7 客户端生命周期已按成熟 S7.NetPlus 使用方式做加固：

- 重复连接前先关闭旧 `Plc`。
- 连接失败时释放临时 `Plc`，避免半开连接残留。
- 读写遇到传输/连接类异常时关闭失效连接，让下次连接或重连有干净状态。
- 加强 DB、DBX、M/I/Q 地址校验。
- Bool 写入必须使用明确 bit 地址，例如 `DB1.DBX0.0` 或 `M10.2`。
- 保留 `ReadDbClassAsync` / `WriteDbClassAsync` 类映射 DB 能力。

后续继续增强 S7 时，优先参考 S7.NetPlus/Snap7 的连接生命周期和测试方式，不要只在现有代码上局部补丁。

### IndustrialClientBase

统一客户端基类现在负责更严格的请求和健康状态管理：

- 单点和批量操作使用一致的超时策略。
- 批量读写会校验请求 `DeviceId`。
- 空批量请求直接返回空结果或完成任务。
- 全 Bad 的批量读取不会把健康状态重置为成功。
- 地址解析错误、数据转换错误不等同于连接故障。
- 连接、超时、Socket、IO 等传输类异常才影响连接健康状态。

读取失败仍倾向于返回 `DataValue.Bad`，写入失败仍抛异常。业务层必须检查 `DataValue.Quality`，不能只判断调用是否抛异常。

## P1：稳定性与性能改进

### PollingScheduler

轮询调度已从“每个订阅一个后台任务”改为“同一设备/同一客户端一个后台 Worker”：

- 多个订阅共享同一个设备 Worker。
- 同一轮到期订阅会合并重复 `ReadRequest`。
- 轮询节拍按固定计划推进，减少 `读取耗时 + Interval` 带来的累计漂移。
- 订阅回调异常被捕获并记录，不会杀死轮询循环。
- 同一 `DeviceId` 不允许绑定不同客户端实例。
- Worker 停止与新订阅并发时，会移除旧 Worker 并重新绑定，避免订阅挂到正在退出的 Worker。

注意：轮询仍然是 SDK 主动周期读取，不是 PLC 主动推送。

### 测试覆盖

`PollingSchedulerTests` 已覆盖：

- 基础订阅事件触发
- ByteArray 内容变化检测
- Subscription DeviceId 与 Client DeviceId 不匹配时拒绝
- 同一 DeviceId 不同 Client 实例混用时拒绝
- 重复点位在同一设备 Worker 内合并读取

## 当前设计边界

1. 每个客户端内部仍串行化核心操作，避免同一 TCP/串口连接上的请求响应错位。
2. 轮询调度负责跨订阅合并重复点位，但协议级连续地址合并仍由具体协议实现。
3. 当前 Modbus TCP 已有连续地址合并批量读取；S7/MC 后续可以继续扩展协议级批量优化。
4. 健康状态区分传输故障与地址/数据错误，但仍需要现场日志配合判断实际 PLC 状态。
5. README 里“验证”部分如果写了本地通过，需要以真实本地/CI 结果为准更新。

## 后续建议

优先级从高到低：

1. 在 Windows + VS/MSBuild 环境跑完整 `dotnet test` 和 Demo 构建。
2. 给 S7/MC 增加更多协议级单元测试和模拟响应测试。
3. 为 S7/MC 实现协议级批量读写优化。
4. 增加 `readGroup`、`writable`、单位、量程、报警上下限等点位运行元数据。
5. 根据真实现场需求再扩 Omron FINS、EtherNet/IP、OPC UA、MQTT 等模块。
