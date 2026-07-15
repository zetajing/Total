# 项目上下文

## 项目目标

`Total` 是面向工业现场的 .NET Framework 4.7.2 通信 SDK、WPF 上位机 Demo 和 WinForms 最小验证程序。协议、运行时、MES 与存储能力属于 SDK；应用层只负责配置、编排和界面，不复制协议实现。

## 维护规则

- 本文件是仓库内的持续记忆，记录当前事实，不保留已经被代码推翻的设计。
- 每轮结构或公开 API 调整后，同步更新模块边界、验证结果、已知限制和后续事项。
- `README.md` 面向 SDK 使用者；本文件面向后续开发和维护。
- 默认分支为 `master`。提交号和远端状态以 Git 实际输出为准，不在本文件固化易过期的 HEAD。

## 2026-07-14 SDK 程序集模块化重构

这是一次有意的破坏性升级，仍只目标 `net472`。

### 当前程序集

| 程序集 | 责任 |
|---|---|
| `IndustrialCommSdk.Abstractions` | 客户端契约、请求/返回模型、枚举、能力、日志、诊断、异常 |
| `IndustrialCommSdk.Runtime` | 客户端基类、轮询、DeviceHost、配置、协议注册表、TagTable、快捷扩展 |
| `IndustrialCommSdk.Transport` | TCP、分帧、服务端、会话和 Socket |
| `IndustrialCommSdk.Protocols.Common` | 寄存器和文本编解码 |
| `IndustrialCommSdk.Protocols.Modbus` | Modbus TCP/RTU 与 NModbus4 |
| `IndustrialCommSdk.Protocols.S7` | Siemens S7 与 S7netplus |
| `IndustrialCommSdk.Protocols.Mc` | Mitsubishi MC 3E |
| `IndustrialCommSdk.Protocols.OpcUa` | OPC UA 与 OPC Foundation |
| `IndustrialCommSdk.Protocols.Mqtt` | MQTT 与 MQTTnet |
| `IndustrialCommSdk.Protocols.Redis` | Redis 与 StackExchange.Redis |
| `IndustrialCommSdk.Mes.Http` | 开放式 MES HTTP JSON 发送和接收 |
| `IndustrialCommSdk.Storage` | SQL Server 历史、缓冲记录器、CSV |
| `IndustrialCommSdk` | 引用全部模块的聚合入口和默认注册表 |

原 `IndustrialCommSdk.Core` 已迁空并从解决方案删除。协议项目不互相引用，也不引用聚合入口；第三方包只属于对应协议项目。

### 公开 API

新增 Runtime 配置扩展点：

- `IProtocolSettings`
- `IIndustrialProtocolProvider`
- `IndustrialProtocolProvider<TSettings>`
- `IndustrialProtocolRegistry`
- `IndustrialConfigurationSerializer`

聚合入口使用实例 API：

```csharp
var sdk = IndustrialSdk.CreateDefault(logger);
var config = sdk.LoadConfiguration("Config/devices.json");
using (var host = sdk.CreateDeviceHost(config, "Config"))
{
    await host.StartAsync();
}
```

`IndustrialClientFactory`、`SimpleClient` 和 `IndustrialDeployment` 已删除，不提供类型转发、旧 API 包装或旧 JSON 自动迁移。只引用单协议模块时，使用对应 Options 和 Client 构造函数。

### 协议键与配置

canonical 协议键固定为：

- `modbus-tcp`
- `modbus-rtu`
- `siemens-s7`
- `mitsubishi-mc`
- `opc-ua`
- `mqtt`
- `redis`

注册表拒绝重复键、非 canonical 键和 Settings 类型不匹配。配置由 Newtonsoft.Json 13.0.3 解析，设备公共字段、`runtime` 和强类型 `settings` 分离。未知协议、空 settings、错误类型和 Provider 字段校验错误在解析或离线校验阶段返回。

### Demo

- WPF 配置页保留公共字段和完整 `devices.json` 高级编辑器。
- Host、端口、串口、CPU 等协议字段统一改为独立 Settings JSON 编辑器。
- 切换协议会加载 Provider 默认 Settings；格式化、应用、保存均经过对应 Provider 解析/校验。
- WPF 运行中心通过 `IndustrialSdk` 创建 `IndustrialDeviceHost`。
- WinForms 不再依赖聚合程序集，直接引用 Runtime、Transport、Modbus、S7、MC 和 MES 模块。
- 配置样例已迁移为 `runtime/settings` 新结构。

### 内部拆分

所有 SDK 手写 C# 文件控制在 500 行以内。已经完成：

- `IndustrialClientQuickExtensions`：连接/读取与写入/转换 partial 文件。
- `PollingScheduler`：Scheduler、SubscriptionRegistry、DeviceWorker。
- `IndustrialClientBase`：请求执行与诊断跟踪。
- `DatabaseStorage`：模型/接口、SQL Schema/写入、历史查询、内部 SQL 帮助器、缓冲记录器。
- `SiemensS7Client`：地址解析和客户端实现分离。
- `IndustrialDeviceHost` 当前 500 行以内。

## 2026-07-15 公开命名空间与协议示例收敛

- `IndustrialCommSdk.Runtime` 的 RootNamespace 和全部公开类型统一到 `IndustrialCommSdk.Runtime` 及其子命名空间。
- 快捷扩展、Tag、TagTable、DeviceHost 和 `IndustrialClientBase` 位于 `IndustrialCommSdk.Runtime`。
- 配置 API 位于 `IndustrialCommSdk.Runtime.Configuration`，轮询位于 `IndustrialCommSdk.Runtime.Polling`，批计划诊断位于 `IndustrialCommSdk.Runtime.Diagnostics`。
- `IndustrialCommSdk.Storage` 的 RootNamespace 和全部公开类型统一到 `IndustrialCommSdk.Storage`；`LogDisplayHelper` 不再留在历史 `IndustrialCommSdk.Diagnostics` 命名空间。
- 不提供旧命名空间包装或类型转发。Demo、WinForms、协议模块、聚合入口和测试已经同步迁移。
- `docs/protocols/` 为七个 canonical 协议分别提供一页可编译的 Options + Client 最小示例，并由根 README 和协议索引统一导航。
- 架构测试会枚举 Runtime 与 Storage 的导出类型，阻止公开命名空间重新漂移到程序集边界之外。

## MES HTTP JSON 边界

MES 保持开放 JSON，不内置 FACHECK、FATRACK、FANUM 或其他业务类型。

- `MesHttpClient.SendJsonAsync(endpoint, json, token)` 只接受 BaseUrl 下安全相对端点。
- 请求根节点必须是 JSON 对象；正文验证后原样 UTF-8 POST，不重新序列化。
- `Content-Type` 和 `Accept` 为 `application/json`。
- 2xx/3xx/4xx 原样返回；默认不重试，只有显式配置时才对 5xx、网络错误和超时有界重试。
- `MesJsonReceiver` 接收任意相对路径的 POST JSON 对象，支持正文上限、处理超时、Authorization 检查和自定义 JSON 响应。
- WPF 与 WinForms MES 页面均只使用 JSON 配置和 JSON 正文。

## 测试状态

模块化重构后测试由 55 项增加到 81 项，当前 Release 测试全部通过。新增覆盖：

- 七种协议 Settings 序列化往返与默认注册完整性。
- 未知协议、旧别名、重复注册、错误 Settings 类型和必填字段。
- 协议程序集不引用聚合入口或其他协议程序集。
- PollingScheduler 的 DeviceId/客户端冲突、重复点位合并、回调异常隔离、取消和 Dispose。
- DeviceHost 禁用设备和无效点位文件。
- Storage 缓冲写入重试、计数与优雅停止。

验收结果：干净 Release 全解决方案构建成功，0 错误；仅 OPC UA 现有 API 产生 6 个 obsolete 警告。Release 测试 81/81 通过，七页协议示例由临时 `net472` 项目编译通过，`git diff --check` 通过，WPF Release 启动冒烟检查通过。

## 已知限制

- 只支持 `net472`，尚未多目标到 .NET 8。
- 尚未制作和发布独立 NuGet 包。
- 密码仍由应用配置提供，未内置凭据加密。
- S7/MC 尚未实现真正合并为单报文的批量读取优化。
- OPC UA 当前依赖仍使用部分已标记 obsolete 的 API；行为未在本次模块化中调整。
- SDK 不替代 PLC 急停、硬件联锁和现场安全回路。

## 后续建议

1. 为各程序集补独立 NuGet 元数据和版本策略。
2. 增加包级 API 兼容性检测和依赖图 CI。
3. 在单独版本中迁移 OPC UA 异步 API。
4. 评估 `net472;net8.0` 多目标，不与协议行为优化混在同一变更中。
