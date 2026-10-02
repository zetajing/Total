# 数据语义、停止预算与集成回归

## 地址比较

`ProtocolAddressComparer.ForProtocol(...)` 为 Modbus TCP/RTU、S7 保留不区分大小写的 PLC 地址规则，为 MQTT、Redis、OPC UA、ADS 使用区分大小写的规则。设备业务名称与点位业务名称仍不区分大小写。

配置校验、`InduLinkSdk.Open`、DeviceHost 和快捷批量读取会自动选择协议规则。直接使用 `TagTable` 或 `InduLinkTagReadResult` 时，默认保留地址大小写；PLC 使用者可显式传入比较器：

```csharp
var tags = TagTable.Load("points.json",
    ProtocolAddressComparer.ForProtocol(ProtocolKind.SiemensS7));
```

同一比较规则下的重复地址仍被拒绝。`Line/Speed` 与 `line/speed` 在区分大小写的协议中是两个独立点位。

## 网关写入结果

HTTP、WebSocket 和 MQTT 使用的 `TagGatewayWriteResult` 新增字符串序列化的 `Status`：

| Status | 含义 |
| --- | --- |
| `Succeeded` | 该项写入已确认成功 |
| `Failed` | 该项校验失败或底层报告明确失败 |
| `Uncertain` | 写入可能已执行，禁止据此自动重放 |
| `NotAttempted` | 同设备的前一项失败或不确定，该项未发往设备 |

保留现有 `Succeeded` 和 `ErrorMessage` 字段。网关逐项执行同一设备的写入，保留已确认的前序成功；某项失败后停止该设备剩余写入，其他设备仍可处理。写入期间取消会保留已有结果，将在途不确定项和剩余未执行项分别标记；请求在开始校验前取消仍抛出取消异常。该流程不是跨点位事务，不自动回滚已完成写入。隐藏原始地址时仍保留 `Uncertain` 状态及重试提示。

## 主机停止预算

```csharp
await using var host = sdk.CreateDeviceHost(config, "Config",
    new InduLinkDeviceHostOptions { ShutdownTimeout = TimeSpan.FromSeconds(5) });
```

默认预算为 10 秒，覆盖主机生命周期锁和本次所有设备停止等待。调用方取消抛出 `OperationCanceledException`，预算耗尽抛出 `TimeoutException`。取消/超时终止等待，已开始的停止任务继续受跟踪，后续 `StopAsync` 可等待同一任务或重试失败的清理。

`DisposeAsync` 在预算内返回；未完成操作的连接和锁延后到后台清理完成后释放。底层永不退出时，资源也不会被并发强行释放。应用应让自己的驱动响应取消；不能把超时返回当作底层 I/O 已停止。

## 回归入口

`InduLink.Tests` 目标为 `net8.0`，不引用 WPF Demo；`InduLink.Demo.Tests` 目标为 `net8.0-windows`，承载配置页面、窗口生命周期和 Demo JSON 用例。

```powershell
dotnet build Total.sln -c Release
dotnet test InduLink.Tests/InduLink.Tests.csproj -c Release
dotnet test InduLink.Demo.Tests/InduLink.Demo.Tests.csproj -c Release
```

OPC UA 回归自动启动进程内的 OPC Foundation 参考服务器，使用临时 PKI 验证默认拒绝未受信服务器、显式信任、实际读取，以及服务器重启后重装原生订阅。客户端安全连接会检查或创建应用证书；可用 `CertificateStoreDirectory` 隔离部署的 `own/trusted/issuers/rejected` 目录，默认 PKI 路径保持原有位置。正式环境仍需管理证书分发、私钥权限和信任审批。

MQTT 重连回归覆盖代理重启及首次订阅恢复被拒绝的情况。只有连接和订阅恢复均完成才结束重试；底层仍连接但订阅恢复失败时，会重建会话继续恢复。失败断言保留客户端诊断日志。

Snap7 回归使用现有 x86 sidecar，覆盖完整 S7 握手、Bool/INT/REAL 读写和服务重启后的重新连接。监听固定为本机 TCP 102，端口已占用时测试失败，避免误连已有服务。

```powershell
dotnet publish InduLinkDemo.Snap7Server/InduLinkDemo.Snap7Server.csproj -c Release -r win-x86 --self-contained true -o artifacts/snap7
$env:INDULINK_TEST_SNAP7_SERVER = Join-Path $PWD 'artifacts/snap7/InduLinkDemo.Snap7Server.exe'
dotnet test InduLink.Tests/InduLink.Tests.csproj -c Release --filter 'TestCategory=Snap7Integration'
```

数据库回归由 `INDULINK_TEST_MYSQL` 和 `INDULINK_TEST_SQLSERVER` 提供连接字符串，验证实际建表、时间与时区/原始字节往返、第二条失败后的整批回滚、首条已写入后的取消回滚，以及取消后的再次写入。未配置对应服务时明确跳过。测试创建并删除 `InduLinkIntegration_<GUID>` 表，应连接专门的可丢弃测试数据库，不连接生产数据库；连接字符串不写入日志。

```powershell
# 在当前进程配置上述两个环境变量后运行：
dotnet test InduLink.Tests/InduLink.Tests.csproj -c Release --filter 'TestCategory=DatabaseIntegration'
```

GitHub Actions 在 `master`、`codex/**` 推送、目标为 `master` 的 PR 和手动运行时执行三组作业：独立 SDK 构建/回归、Windows 应用和 Snap7、Linux 上临时 MySQL 8.0/SQL Server 2022 容器。数据库作业配置连接字符串后不会以缺少服务为由跳过。此轮协议回归排除 MC 命名用例；项目构建仍包含现有依赖。
