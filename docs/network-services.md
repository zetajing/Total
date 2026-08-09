# 网络服务扩展

本页说明 Demo 和 SDK 中新增的 MQTT Broker、HTTP/HTTPS、WebAPI、WebSocket 与
FTP/FTPS 客户端。现有 `IndustrialCommSdk.Mes.Http` 和 Demo 的 MES 页面不变；MES
继续使用原来的 `8080/8081` 配置，新工业 Web 网关默认使用 `8088`，两者可以同时运行。

## 安全默认值

- MQTT Broker 默认监听 `127.0.0.1:1883`，Web 网关默认监听
  `http://127.0.0.1:8088/`，并且都不会自动启动。
- WebAPI 和 Tag WebSocket 必须使用 `X-Industrial-Api-Key`。
- 非本机 MQTT 监听必须启用 TLS；非本机 Web 监听必须使用 HTTPS/WSS、API Key 和
  非空 Origin 白名单。
- 对外访问只接受 `devices.json` 所引用点表中的命名 Tag。原始地址读取默认关闭，
  不提供原始地址写入接口。
- 远程写入必须同时满足 `enableRemoteWrites=true` 和点表中目标 Tag 的
  `writable=true`。旧点表缺少 `writable` 时按 `false` 处理。
- MQTT、WebAPI 和 FTP 的密码或 API Key 不写入普通 JSON。Demo 使用 Windows DPAPI
  CurrentUser 保护密钥，只在 `network-services.json` 中保存密钥名称。

## 配置与密钥

Demo 配置文件为输出目录下的 `Config/network-services.json`：

```json
{
  "version": 1,
  "mqttBroker": {
    "autoStart": false,
    "bindAddress": "127.0.0.1",
    "port": 1883,
    "useTls": false,
    "tlsPort": 8883,
    "username": "industrial",
    "passwordSecretName": "mqtt.broker.password"
  },
  "webGateway": {
    "autoStart": false,
    "listenPrefix": "http://127.0.0.1:8088/",
    "apiKeySecretName": "web.api-key",
    "enableRemoteWrites": false,
    "allowRawAddressReads": false,
    "exposeRawAddresses": false,
    "allowedOrigins": []
  },
  "ftp": {
    "host": "127.0.0.1",
    "port": 21,
    "username": "industrial",
    "passwordSecretName": "ftp.password",
    "useTls": true,
    "allowInsecureFtp": false,
    "passiveMode": true,
    "remoteRoot": "/"
  }
}
```

DPAPI 文件存放在应用状态目录的 `network-secrets` 子目录中，只能由同一 Windows
用户上下文解密。备份或迁移到另一用户/机器后，应在界面中重新录入密钥。日志和请求
审计不会记录密码、API Key、认证头或请求正文。

点表可按 JSON 或 CSV 设置远程写权限：

```json
{
  "name": "SetPoint",
  "address": "DB1.DBW2",
  "dataType": "int16",
  "length": 1,
  "writable": true
}
```

CSV 使用 `writable` 列，值可写为 `true` 或 `false`。

## MQTT 客户端与 Broker

MQTT 客户端支持连接超时、KeepAlive、遗嘱、持久/清理会话、TLS 证书校验、通配
Topic 消息事件，以及带指数退避和订阅恢复的自动重连。内嵌 Broker 默认禁用匿名连接，
并支持账号密码、发布/订阅 ACL、客户端会话和服务端发布。

工业 Tag 网关固定使用以下 Topic：

| Topic | 用途 |
|---|---|
| `industrial/v1/devices/{device}/tags/{tag}` | 保留的 Tag 快照和变化值 |
| `industrial/v1/devices/{device}/state` | 保留的设备状态 |
| `industrial/v1/requests/{clientId}/read` | 批量读取命令 |
| `industrial/v1/requests/{clientId}/write` | 批量写入命令 |
| `industrial/v1/responses/{clientId}/{correlationId}` | 逐请求响应 |
| `industrial/v1/gateway/heartbeat` | 网关心跳 |

设备名、Tag 名、客户端 ID 和关联 ID 在作为 Topic 段时会进行 URL 编码。客户端只能
发布到与自己 MQTT ClientId 相同的 request Topic，也只能订阅自己的 response Topic、
设备快照和心跳。

读取命令示例：

```json
{
  "correlationId": "read-001",
  "items": [
    { "device": "s7plc", "tag": "Temperature" },
    { "device": "s7plc", "tag": "Running" }
  ]
}
```

写入命令在 item 中增加 `value`。网关仍会逐项执行全局写开关和 `writable` 检查，
MQTT 认证成功并不代表拥有任意 PLC 写权限。

## HTTP/HTTPS 与 WebAPI

`IndustrialCommSdk.Web.Http.IHttpApiClient` 支持 GET、POST、PUT、PATCH、DELETE 及
其他 `HttpMethod`，可发送 JSON、文本或二进制，支持自定义 Header、取消、超时和响应
大小上限。

工业 Web 网关提供：

| 方法和路径 | 功能 |
|---|---|
| `GET /api/v1/health` | 网关健康状态 |
| `GET /api/v1/devices` | 已配置设备 |
| `GET /api/v1/devices/{device}/tags` | 指定设备的命名 Tag |
| `POST /api/v1/read` | 批量读取 Tag |
| `POST /api/v1/write` | 批量写入可写 Tag |
| `POST /api/v1/read-address` | 原始地址读取，默认禁用 |
| `GET /ws/v1/tags` | Tag WebSocket 升级入口 |

HTTP 请求必须携带：

```text
X-Industrial-Api-Key: <DPAPI 中配置的 API Key>
Content-Type: application/json
```

Tag 读取正文：

```json
{
  "correlationId": "http-read-001",
  "items": [
    { "device": "s7plc", "tag": "Temperature" }
  ]
}
```

响应逐项包含设备、Tag、数据类型、值、质量、UTC 时间和错误信息。网关区分无效请求、
未认证、无权限、请求过大、并发过多和内部错误，对应 400、401、403、413、429 和 500。

## WebSocket

独立 WebSocket 客户端和服务端支持文本、二进制、消息分片重组、串行发送、消息大小
限制、关闭握手和客户端自动重连。Tag WebSocket 建立后可发送：

```json
{
  "type": "subscribe",
  "correlationId": "sub-001",
  "items": [
    { "device": "s7plc", "tag": "Temperature" }
  ]
}
```

服务器先返回 `snapshot`，随后只在值、质量或状态改变时发送 `change`。还支持
`unsubscribe` 和 `ping`，服务器会定期发送应用层 `heartbeat`。单会话订阅数量、消息
大小、会话数量和慢客户端积压均有上限。

浏览器原生 WebSocket API 不能设置自定义 `X-Industrial-Api-Key` Header，因此当前
Tag WebSocket 入口面向 WPF、服务程序等原生客户端。v1 不提供把 API Key 放入 URL 的
浏览器票据方案，避免密钥进入浏览历史和代理日志。

## HTTP.sys 的远程 HTTPS 配置

SDK 和 Demo 不会自动执行管理员命令。要监听非本机前缀，管理员需显式配置 URL ACL
和证书绑定，例如：

```powershell
netsh http add urlacl url=https://+:8088/ user="DOMAIN\\IndustrialUser"
netsh http add sslcert ipport=0.0.0.0:8088 certhash=<证书指纹> appid="{11111111-2222-3333-4444-555555555555}"
```

实际用户、地址、端口、证书和 AppId 必须按部署环境替换。还需配置防火墙，并在
`allowedOrigins` 中明确列出允许的 Web Origin。不要使用 `*` 作为生产白名单。

## FTP/FTPS 客户端

`IndustrialCommSdk.FileTransfer.Ftp.IFtpFileClient` 基于 FluentFTP，支持：

- 连接、断开、健康检查和能力探测；
- 目录浏览和创建目录；
- 上传、下载、删除和重命名；
- 断点续传、进度、取消，以及服务器支持时的校验和；
- 默认“临时文件上传、校验、远端改名”的原子上传流程。

客户端默认使用显式 FTPS 和被动模式。明文 FTP 必须同时设置 `UseTls=false` 和
`AllowInsecureFtp=true`。`RemoteRoot` 是客户端可见的远端根目录，路径规范化会拒绝
原始或多重 URL 编码的 `..` 越界。

本项目不内嵌 FTP Server，也不远程管理 IIS FTP 或 FileZilla Server；WPF 页面只连接
用户部署的标准 FTP/FTPS 服务。SFTP 属于 SSH 文件传输，不是 FTP over TLS，v1 不支持。

真实 FTP/FTPS 集成测试默认跳过。设置 `INDUSTRIAL_FTP_TEST_HOST` 后可按需配置
`INDUSTRIAL_FTP_TEST_PORT`、`INDUSTRIAL_FTP_TEST_USERNAME`、
`INDUSTRIAL_FTP_TEST_PASSWORD`、`INDUSTRIAL_FTP_TEST_SECURITY`、
`INDUSTRIAL_FTP_TEST_ROOT` 和 `INDUSTRIAL_FTP_TEST_CERT_THUMBPRINT` 再运行测试。

## 版本一边界

本阶段不包含嵌入式 FTP Server、SFTP、Swagger、静态网站托管或 Windows Service。
所有 SDK 服务接口均保持独立，可在后续版本承载到单独的 Windows Service，而无需改变
现有 MES HTTP JSON 行为。
