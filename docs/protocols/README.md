# 协议模块最小示例

本目录按 canonical 协议键提供可直接复制的最小示例。示例基于当前的具体 `Options + Client` API，不依赖已删除的静态工厂或 `SimpleClient`。

| canonical key | 页面 | 直接引用的协议程序集 |
| --- | --- | --- |
| `modbus-tcp` | [Modbus TCP](modbus-tcp.md) | `IndustrialCommSdk.Protocols.Modbus` |
| `modbus-rtu` | [Modbus RTU](modbus-rtu.md) | `IndustrialCommSdk.Protocols.Modbus` |
| `siemens-s7` | [Siemens S7](siemens-s7.md) | `IndustrialCommSdk.Protocols.S7` |
| `mitsubishi-mc` | [Mitsubishi MC](mitsubishi-mc.md) | `IndustrialCommSdk.Protocols.Mc` |
| `opc-ua` | [OPC UA](opc-ua.md) | `IndustrialCommSdk.Protocols.OpcUa` |
| `mqtt` | [MQTT](mqtt.md) | `IndustrialCommSdk.Protocols.Mqtt` |
| `redis` | [Redis](redis.md) | `IndustrialCommSdk.Protocols.Redis` |

## 通用规则

- 当前 SDK 目标框架是 `net472`。
- 项目直接引用对应协议项目即可，项目依赖会自动带入 `IndustrialCommSdk.Runtime`、`IndustrialCommSdk.Abstractions` 及该协议自己的第三方包。
- 示例中的 `IndustrialCommSdk.Runtime` 命名空间提供 `UseAsync`、强类型读取和写入扩展。
- `UseAsync` 会依次完成连接、业务操作、断开和释放；回调结束后不要再复用该客户端实例。需要长连接时，应自行持有客户端并显式调用 `ConnectAsync`、`DisconnectAsync` 和 `Dispose`。
- 示例地址、端口和账号都是占位值。执行写入前必须替换成测试设备上的安全地址，并确认数据类型、长度和字节序。
- 配置驱动场景应使用各模块的 Provider；本目录专门展示单模块直接构造客户端的方式。

