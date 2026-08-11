# 工程记录

本文把原先分散在“可靠性更新、协议审查、核心扩展设计、后续修改意见”中的内容收敛为一份维护记录。这里区分当前设计、验证边界和后续建议；历史提交号不固化在文档中，当前分支和代码状态以 Git 为准。

## 状态快照（0.x 内部迭代，2026-08-11）

### 已完成

- `[0.x]` Modbus TCP/RTU、S7、MC、OPC UA、MQTT、Redis 客户端和统一 `IIndustrialClient` 入口。
- `[0.x]` 协议能力模型、轮询拆批、配置模板与 JSON Schema 校验。
- `[0.x]` S7/MC 连续区协议级批量读取：同一存储区一次读取连续字节/字或位设备，再按请求分别解码；批量写入仍保持逐项执行。
- `[0.x]` 基础连接生命周期、超时、重连、健康状态、诊断快照和 Demo 验证页面。

### 进行中

- `[下一次 0.x]` GitHub Actions、SDK-only 构建、Release 测试门禁和 NuGet 版本元数据。
- `[下一次 0.x]` S7 仿真 Server、MC 黄金报文、OPC UA 证书和数据库容器集成测试。
- `[下一次 0.x]` 统一设备协议密钥引用，逐步从明文 `Password` 迁移到 DPAPI `ISecretStore`。

### 待办

- `[后续 0.x]` 暴露 Mitsubishi MC 复杂拓扑路由参数。
- `[后续 0.x]` 完善 S7/MC 跨边界、超时、断线恢复和真实设备验收。
- `[1.0 前]` 冻结公共 API、配置模型、错误模型和点位语义，再决定多目标框架与正式包拆分。

## 当前已完成

### 模块化和公开 API

- SDK 已拆为 Abstractions、Runtime、Transport、协议、MES、Web、FTP、Storage 和 MySQL 提供程序等程序集。
- 公开命名空间已收敛到程序集边界，不提供旧命名空间包装或类型转发。
- `IndustrialSdk` 提供默认注册表、配置解析、离线校验和 `IndustrialDeviceHost` 创建；单协议仍可直接使用具体 Options + Client。
- `devices.json` 将公共字段、`runtime` 和强类型 `settings` 分开，canonical 协议键拒绝旧别名和重复注册。

### 协议和 Demo

- Modbus TCP/RTU、S7、MC、OPC UA、MQTT、Redis 均有直接客户端入口和最小示例。
- Modbus TCP Options 会在构造阶段校验设备 ID、主机、端口、站号、连接超时和设备 Profile。
- S7 加固连接生命周期、失效连接清理、DB/DBX/M/I/Q 地址校验和明确 bit 地址要求。
- MC 地址解析使用集中元数据，`ZR` 按十进制，`X/Y/W` 等协议语义地址按十六进制；后续扩设备类型应继续扩展元数据表。
- 原始 TCP 支持固定长度、分隔符、2/4 字节大端长度头，以及半包/粘包缓存。
- WinForms 最小程序隔离验证 Modbus TCP、Modbus RTU、S7、MC、原始 TCP 和开放式 MES HTTP JSON；WPF Demo 负责配置驱动运行和综合展示。
- MES 保持开放 JSON，不内置 FACHECK、FATRACK、FANUM 等业务流程；5xx 重试有界，响应及时释放，并支持注入 `HttpMessageHandler` / 外部 `HttpClient`。

### 能力模型和批量计划

- `ProtocolCapabilities` 统一描述批量、位地址、类型、PDU、超时和推荐轮询能力。
- `IIndustrialAddress` 及 Modbus/S7/MC 强类型地址解析已接入平台模型。
- `BatchReadOptions`、`BatchWriteOptions`、`BatchSplitPlan`、`IBatchOperationPlanner` 和 `BatchPlanDiagnostics` 已建立。
- Modbus、S7、MC 已能生成读取拆批计划；`PollingScheduler` 会优先使用 planner，批次独立容错并按订阅顺序上报。
- S7/MC 已在 `ReadManyCoreAsync` 中执行协议级连续区合并读取；S7 读取共享字节区，MC 读取连续字/位设备，再按原始请求分别解码。

## 可靠性设计

### 客户端和连接

- 单点和批量操作使用一致的请求级/默认超时策略；批量请求校验 DeviceId，空批量直接完成。
- 地址解析和数据转换错误不当作连接故障；连接、超时、Socket、IO 等传输异常才影响健康状态。
- 读失败倾向于返回 `DataValue.Bad`，写失败抛异常；业务层必须检查 `DataValue.Quality`，不能只判断是否抛异常。
- 每个客户端串行化核心操作，避免同一 TCP/串口连接上的请求响应错位。
- TCP 使用连接代际隔离旧连接错误；断线、重连、分帧失败会清理对应残帧，排队接收取消不会破坏活动接收的半帧。

### 轮询和 Host

- 同一设备/客户端使用一个 Worker 合并重复点位，轮询按固定计划推进，减少“读取耗时 + 间隔”造成的漂移。
- 回调异常被隔离，不会杀死轮询循环；Worker 替换、停止和新订阅并发时会重新绑定，避免订阅挂在正在退出的 Worker 上。
- `IndustrialDeviceHost` 启动失败会回滚已启动设备；停止失败保留可重试状态；构造中途失败会释放已经创建的客户端。
- SDK 主动周期读取不是 PLC 主动推送；需要推送时使用网络服务或设备原生能力。

### 存储和 MES Receiver

- `BufferedIndustrialDataRecorder` 串行化 Start/Stop/Dispose，停止后不可重启；生产者与停止并发时只返回拒绝，不抛出队列竞态异常。
- 缓冲队列有界，数据库故障不会阻塞 PLC 实时通信；接受的数据先排空，未接受的数据记录丢弃计数。
- MES Receiver 在锁外有界等待在途请求；处理器内 Stop 不自等待；同步阻塞处理器可以返回 504；超时处理器继续受跟踪并占用容量；过载快速返回 429。

## 审查结论和现场风险

涉及帧格式、超时和重试语义的调整，应配合模拟器、回环测试或实机回归，不应只凭静态审查改变默认行为。

| 项目 | 当前结论 | 现场注意 |
| --- | --- | --- |
| Modbus TCP/RTU | 已有 Profile、串口参数和基本读写路径 | TCP 无认证；RTU 还要检查总线、电气和串口占用 |
| Siemens S7 | 地址和连接生命周期已加固 | 需按 CPU 设置 Rack/Slot、PUT/GET 和 DB 优化访问；TCP 102 应隔离 |
| Mitsubishi MC | 支持二进制 3E 直连 | 网络号、PC 号、目标 I/O、目标站号仍有固定默认边界，复杂路由需另行设计 |
| OPC UA | 基于成熟 OPC Foundation 库 | 生产使用安全端点和证书信任；NamespaceArray 变化需现场核对 |
| MQTT | 客户端及网关支持 TLS、ACL、重连和 Topic 约束 | 控制 Topic 慎用 Retain；账号、Key 和 Broker 端口要按网络边界保护 |
| MySQL/SQL Server | 共同历史存储契约和离线参数校验 | 真实建表、时区、事务、取消和权限必须做集成测试 |
| FTP/FTPS | 客户端默认显式 FTPS、被动模式和安全路径 | 明文 FTP 需显式双开关；SFTP 不属于 v1 |

## 验证边界

建议每次改动至少分开记录三类结果：

1. **编译**：Release 全解决方案或受影响项目是否构建成功。
2. **离线测试**：协议解析、配置校验、生命周期、分帧、批量计划和存储契约测试是否通过。
3. **现场/外部集成**：真实 PLC、串口总线、OPC UA Server、MQTT Broker、数据库和 FTP 服务是否验证。

编译成功不代表现场通信成功；不连接外部数据库的测试也不代表 MySQL/SQL Server 已完成验收。密码、连接字符串和现场地址不得写入测试输出或诊断包。

## 后续路线

### P0：质量门禁

- 增加 GitHub Actions，至少覆盖 restore、SDK、Demo、全解决方案 build/test，并上传构建结果。
- 明确 SemVer、CHANGELOG、NuGet 元数据、包级兼容性和协议兼容矩阵。
- 让文档、示例和配置 schema 在 API 变更时成为同一检查项。

### P1：核心平台

- 继续将能力矩阵和 batch plan 快照用于诊断。
- 用 `BatchPlanDiagnostics` 替换协议和轮询中的手写批量日志。
- 根据 `ProtocolCapabilities` 动态启用/禁用 Demo 输入项。
- S7/MC 协议级合并读取已完成；后续补充跨边界、超时、断线恢复和仿真/现场验收，不改变现有单点语义。

### P2：测试和可观测性

- Modbus 增加本地 loopback server；S7 引入 Snap7/仿真 Server；MC 增加帧和响应黄金样本。
- 增加可选真实数据库集成测试，覆盖建表、事务回滚、时区往返和取消。
- 评估 `IndustrialClientMetrics`、`PollingMetrics`、`TransportMetrics`、`StorageMetrics`，记录成功率、超时、延迟、连续失败、轮询周期和批量节省数。
- 统一事件 ID，并提供脱敏诊断包；诊断包必须移除 Host、账号、连接字符串、密码和 API Key。

### P3：协议扩展

先稳定现有 Modbus/S7/MC，再按现场需求选择 Omron FINS、Keyence KV、Allen-Bradley EtherNet/IP 或 MQTT/Sparkplug B。扩展协议应继续映射到 `IIndustrialClient`、`Tag`、`DataValue`、`QualityStatus` 和 `IndustrialDeviceHost`，不要一开始暴露过重的厂商对象模型。
