# 本地虚拟协议服务

Demo 的 **调试与维护 → 虚拟 PLC** 提供三个页签：已有的 Siemens S7（Snap7 Server）、Modbus TCP 和 OPC UA。Modbus/OPC UA 通过 `InduLink.Simulation` 类库实现真实协议服务，其他进程的客户端也可以连接。

## 从 Demo 使用

1. 选择 Modbus TCP 或 OPC UA 页签。默认设置只接受本机连接。
2. 在点位表中设置地址、类型、初值、变化方式和步长，或点击“导入配置”。
3. 点击“启动服务”，再到对应客户端页面连接。
4. 选中点位，在“选中点位的新值”输入数值并应用；客户端写入后的值也会显示在“当前值”中。
5. 点击“停止”释放端口；再次启动会恢复配置中的初值。关闭 Demo 时会清理全部虚拟服务。

运行期间点位结构与服务设置锁定；调整地址/类型需要先停止服务。配置导出保存设置和初值，不保存当前运行值。已有 S7 的使用方式见 [Snap7 Server 文档](snap7-server.md)。

## Modbus TCP

默认监听 `127.0.0.1:1502`，站号 `1`。客户端设备类型选择 **Generic Modbus**（配置键 `generic-modbus`），无需套用 PLC 品牌地址映射。

| 区域 | 零基地址示例 | 类型与权限 |
| --- | --- | --- |
| 线圈 | `C0` | Bool，客户端可读写 |
| 离散输入 | `DI0` | Bool，客户端只读 |
| 保持寄存器 | `HR0` | 数值，客户端可读写 |
| 输入寄存器 | `IR0` | 数值，客户端只读 |

也支持 `00001/10001/40001/30001` 等一基引用地址，分别对应上述零号位置。表中的地址不能在同一存储区域内重叠；例如 `HR0` 的 Int32 占用两个寄存器，下一点必须从 `HR2` 开始。

支持 Int16、UInt16、Int32、UInt32、Float、Double。每个寄存器使用大端字节序，多寄存器使用高字在前；Float 占两个寄存器，Double 占四个。输入区域可通过模拟器界面修改，协议侧维持只读。未配置的内存位置初始为零，完整内存区域为 0–65535；重启重置已配置点位。

示例点位：`C0=true`、`DI0=false`（周期切换）、`HR0=100`（Int16 递增）、`HR2=12.5`（Float）、`IR0=42`（UInt16）。

## OPC UA

默认 Endpoint 为 `opc.tcp://localhost:4840/InduLinkSimulator`。`localhost` 实际绑定 `127.0.0.1`；如需选择其他监听接口，填写该接口的数字 IP 地址。这个处理避免协议栈把 DNS 主机名解释为全接口监听，相关实现可见 [当前引用版本的 OPC Foundation TCP Listener 源码](https://github.com/OPCFoundation/UA-.NETStandard/blob/2a7346e7d74f42b8c4f04223eb020e33d2c7edb4/Stack/Opc.Ua.Core/Stack/Tcp/TcpTransportListener.cs#L544)。

节点位于 Objects 下，命名空间 URI 为 `urn:indulink:simulator:points`，索引为 `2`。配置可填写完整 `ns=2;s=Demo/Temperature`，或填写 `Demo/Temperature` 后通过 `GetNodeId(...)` 获取客户端地址；字符串标识区分大小写。

节点支持 Bool、Int16、UInt16、Int32、UInt32、Float、Double、String，可浏览、读写、原生订阅。默认节点为：

| NodeId | 类型 | 初值 |
| --- | --- | --- |
| `ns=2;s=Demo/Running` | Bool | true |
| `ns=2;s=Demo/Counter` | Int32 | 0，每周期递增 1 |
| `ns=2;s=Demo/Temperature` | Float | 23.5 |
| `ns=2;s=Demo/Status` | String | Ready |

默认使用无加密、匿名连接，客户端“安全连接”也应关闭。勾选“加密连接”时只提供 Basic256Sha256 / SignAndEncrypt，客户端和服务端都需要显式信任对方应用证书：

- 服务端 PKI：`%LocalAppData%/InduLink/simulation/opcua/pki`；客户端默认 PKI：`%LocalAppData%/InduLink/pki`。界面的“证书目录”可为不同实例选择独立目录。
- 将服务端 `own/certs` 的 `.der` 公共证书复制到客户端 `trusted/certs`。
- 将客户端 `own/certs` 的 `.der` 公共证书复制到服务端 `trusted/certs`。
- 初次安全连接会创建客户端证书；完成复制后重启虚拟服务并重新连接客户端。不复制 `own/private` 中的私钥。
- 类库调用可通过 `certificateStoreDirectory` 隔离不同实例的 PKI。

## 点位变化

- **固定值：**保留初值、界面设值或客户端写入后的值。
- **递增：**每个周期将当前数值加上步长，支持负数步长；整数点位要求整数步长。
- **周期切换：**每个周期取反，仅用于 Bool。

默认周期为 1000 ms，最小为 20 ms。初值和界面写入数值使用小数点（例如 `12.5`），Bool 使用 `true/false`。超出类型范围或产生非有限浮点数时保留最后有效值，并在状态栏给出错误；周期变化仍可能覆盖用户刚写入的值，持续手动测试可改用“固定值”。

## 配置文件

“导入/导出配置”使用独立 JSON 文件，避免和生产设备配置混用。下面是一份可导入 Modbus 页面的最小示例：

```json
{
  "Protocol": "modbus-tcp",
  "Address": "127.0.0.1",
  "Port": 1502,
  "SlaveId": 1,
  "IntervalMilliseconds": 1000,
  "Points": [
    { "Address": "C0", "DataType": "Bool", "InitialValue": "true", "Behavior": "Fixed", "Step": 1 },
    { "Address": "HR0", "DataType": "Int32", "InitialValue": "0", "Behavior": "Increment", "Step": 1 }
  ]
}
```

OPC UA 配置使用 `"Protocol": "opcua"`、`"Endpoint": "opc.tcp://localhost:4840/InduLinkSimulator"` 和 `"UseSecurity": false`。不匹配当前页面的协议配置会被拒绝。

## 在自己的程序中运行

引用 `InduLink.Simulation/InduLink.Simulation.csproj`，通过异步生命周期使用服务。省略地址和端口时采用上述默认值：

```csharp
using InduLink.Abstractions;
using InduLink.Simulation;

await using var modbus = new ModbusTcpSimulator(new[]
{
    new SimulationPoint("C0", DataType.Bool, "true"),
    new SimulationPoint("HR0", DataType.Float, "12.5"),
});
await modbus.StartAsync();
modbus.SetValue("HR0", "31.25");

await using var opcua = new OpcUaSimulator(new[]
{
    new SimulationPoint("Demo/Counter", DataType.Int32, "0", SimulationBehavior.Increment),
});
await opcua.StartAsync();
Console.WriteLine(opcua.GetNodeId("Demo/Counter")); // ns=2;s=Demo/Counter
Console.ReadLine();
```

`ReadValues()` 返回点位当前快照；`StopAsync()` 允许再次启动，`DisposeAsync()` 释放服务后禁止重新启动。端口冲突、非法初值、重叠地址或非法变化方式不会被报告为启动成功。

## 自动化回归

```powershell
dotnet test InduLink.Tests/InduLink.Tests.csproj -c Release --filter 'TestCategory=SimulatorIntegration'
dotnet test InduLink.Demo.Tests/InduLink.Demo.Tests.csproj -c Release --filter 'FullyQualifiedName~VirtualProtocolUiTests'
```

协议回归覆盖四类 Modbus 区域与多寄存器类型、客户端写入、周期变化、重启/端口释放、OPC UA 浏览/大小写/写入/订阅、双向证书信任、端口冲突与取消后的再次启动。WPF 回归通过实际界面控件启动服务、应用新值并使用 SDK 客户端读回，再验证停止和窗口清理路径。两组均纳入已有 SDK/Windows CI 作业。

这些服务用于通信与数据模拟；点位变化由定时器生成，不执行 PLC 用户程序。本轮新增范围为 Modbus TCP 和 OPC UA，MC 保持跳过。
