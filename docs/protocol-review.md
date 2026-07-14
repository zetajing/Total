# 协议实现审查

## 本轮已处理

- Modbus TCP：完整选项构造现在会立即校验设备 ID、主机、端口、站号、连接超时和设备配置文件，与 Modbus RTU、S7、MC 的失败时机保持一致。
- 新增 WinForms 最小系统，分别隔离验证 Modbus TCP、Modbus RTU、Siemens S7、Mitsubishi MC、原始 TCP 和开放式 MES HTTP JSON。
- 四种 PLC 客户端统一使用 5000 ms 默认操作超时，请求级 Timeout 优先；S7 连接增加独立超时。
- 原始 TCP 增加固定长度、分隔符、2/4 字节大端长度头分帧及半包/粘包缓存。
- 新增结构化诊断快照；Modbus RTU 额外统计串口打开、响应超时和帧错误。
- `ModbusTcpClientOptions.DeviceProfile` 支持显式设备 Profile；WinForms 可选择通用、汇川、三菱及 JSON 注册映射并联动示例地址。
- MES HTTP 修复 5xx 响应可能无限重试的问题，所有响应均会及时释放；新增自定义 `HttpMessageHandler` 和外部 `HttpClient` 注入入口，便于代理、证书、连接池与自动化测试。

## 后续优化建议

| 优先级 | 协议 | 建议 | 原因 |
| --- | --- | --- | --- |
| P2 | Mitsubishi MC | 将网络号、PLC 号、目标模块 I/O 和监视定时器暴露为选项 | 当前固定 3E 帧路由参数适合简单直连，复杂网络拓扑需要配置能力。 |

涉及帧格式、超时和重试语义的调整应配合模拟器或实机回归，不在本轮仅凭静态审查改变默认行为。
