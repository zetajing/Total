# 协议参考

本页集中收录所有 canonical 协议键的直接客户端示例。示例使用当前的 `Options + Client` API，不依赖已删除的静态工厂或 `SimpleClient`。

| canonical key | 客户端 | 直接引用的程序集 |
| --- | --- | --- |
| `modbus-tcp` | `ModbusTcpClient` | `IndustrialCommSdk.Protocols.Modbus` |
| `modbus-rtu` | `ModbusRtuClient` | `IndustrialCommSdk.Protocols.Modbus` |
| `siemens-s7` | `SiemensS7Client` | `IndustrialCommSdk.Protocols.S7` |
| `mitsubishi-mc` | `MitsubishiMcClient` | `IndustrialCommSdk.Protocols.Mc` |
| `opc-ua` | `OpcUaClient` | `IndustrialCommSdk.Protocols.OpcUa` |
| `mqtt` | `MqttClient` | `IndustrialCommSdk.Protocols.Mqtt` |
| `redis` | `RedisClient` | `IndustrialCommSdk.Protocols.Redis` |

## 通用规则

- 当前 SDK 目标框架是 `net472`。
- 项目直接引用对应协议项目即可，传递依赖会带入 `IndustrialCommSdk.Runtime`、`IndustrialCommSdk.Abstractions` 及协议自己的第三方包。
- `IndustrialCommSdk.Runtime` 提供 `UseAsync`、强类型读取和写入扩展。
- `UseAsync` 依次完成连接、业务操作、断开和释放；需要长连接时自行调用 `ConnectAsync`、`DisconnectAsync` 和 `Dispose`。
- 示例地址、端口和账号都是占位值。写入前必须替换成测试设备上的安全地址，并确认数据类型、长度和字节序。
- 配置驱动场景使用各模块的 Provider；本页专门展示单模块直接构造客户端。

## Modbus TCP

所需程序集：`IndustrialCommSdk.Protocols.Modbus`，快捷扩展来自 `IndustrialCommSdk.Runtime`。

```csharp
using System;
using System.Threading.Tasks;
using IndustrialCommSdk.Protocols.Modbus;
using IndustrialCommSdk.Runtime;

public static class ModbusTcpExample
{
    public static async Task RunAsync()
    {
        var client = new ModbusTcpClient(new ModbusTcpClientOptions
        {
            DeviceId = "plc-modbus-tcp-1",
            Host = "192.168.1.10",
            Port = 502,
            SlaveId = 1,
            DeviceProfile = ModbusDeviceProfiles.Generic,
            ConnectTimeoutMilliseconds = 3000,
            OperationTimeoutMilliseconds = 5000,
        });

        await client.UseAsync(async connected =>
        {
            ushort current = await connected.ReadUInt16Async("HR0");
            Console.WriteLine("HR0 = " + current);

            // 仅在确认 HR1 可安全写入后执行。
            await connected.WriteAsync("HR1", (ushort)42);
        });
    }
}
```

`ModbusDeviceProfiles.Generic` 支持零基地址 `HR0`、`IR0`、`C0`、`DI0`，也支持一基引用地址 `40001`、`30001`、`00001`、`10001`。`HR` 是保持寄存器，`IR` 是输入寄存器，`C` 是线圈，`DI` 是离散输入；`IR` 和 `DI` 只能读取。通用 Profile 中 `40001` 等价于 `HR0`，不要把设备手册的显示地址直接当成偏移。

多寄存器值的字序取决于设备。通用 Profile 不交换字序；汇川 EasyPLC 或三菱 Modbus 映射应选择对应的 `InovanceEasyPlc` 或 `MitsubishiModbusTcp` Profile，并使用品牌地址。

Modbus TCP 本身没有认证和加密。生产环境应使用工业防火墙、VLAN 或白名单限制 502 端口，并禁止从办公网或互联网直接访问 PLC。`SlaveId` 当前有效范围为 1–247；任何写入都应先在停机或仿真环境验证。

## Modbus RTU

```csharp
using System;
using System.IO.Ports;
using System.Threading.Tasks;
using IndustrialCommSdk.Protocols.Modbus;
using IndustrialCommSdk.Runtime;

public static class ModbusRtuExample
{
    public static async Task RunAsync()
    {
        var client = new ModbusRtuClient(new ModbusRtuClientOptions
        {
            DeviceId = "plc-modbus-rtu-1",
            PortName = "COM3",
            BaudRate = 9600,
            DataBits = 8,
            Parity = Parity.Even,
            StopBits = StopBits.One,
            SlaveId = 1,
            ReadTimeout = 3000,
            WriteTimeout = 3000,
            OperationTimeoutMilliseconds = 5000,
            Retries = 2,
            WaitToRetryMilliseconds = 100,
            DeviceProfile = ModbusDeviceProfiles.Generic,
        });

        await client.UseAsync(async connected =>
        {
            ushort current = await connected.ReadUInt16Async("HR0");
            Console.WriteLine("HR0 = " + current);
            await connected.WriteAsync("C0", true);
        });
    }
}
```

地址写法与 Modbus TCP 相同。当前直接客户端要求 8 个数据位，站号范围为 1–247；波特率、校验位和停止位必须与总线所有设备一致。常见组合是 `9600-8-E-1` 或 `9600-8-N-2`，但应以设备手册为准。

同一串口不能同时被多个程序占用。RS-485 超时还应检查 A/B 极性、终端电阻、偏置电阻、接地和总线拓扑；串口链路通常没有认证能力，应限制物理接入和串口服务器的网络访问。

## Siemens S7

```csharp
using System;
using System.Threading.Tasks;
using IndustrialCommSdk.Protocols.S7;
using IndustrialCommSdk.Runtime;
using S7.Net;

public static class SiemensS7Example
{
    public static async Task RunAsync()
    {
        var client = new SiemensS7Client(new SiemensS7ClientOptions
        {
            DeviceId = "plc-s7-1",
            Host = "192.168.1.20",
            CpuType = CpuType.S71200,
            Rack = 0,
            Slot = 1,
            AutoReconnect = true,
            ConnectTimeoutMilliseconds = 5000,
            OperationTimeoutMilliseconds = 5000,
        });

        await client.UseAsync(async connected =>
        {
            ushort current = await connected.ReadUInt16Async("DB1.DBW0");
            Console.WriteLine("DB1.DBW0 = " + current);
            await connected.WriteAsync("DB1.DBW2", (ushort)42);
        });
    }
}
```

常用地址包括 `DB1.DBX0.0`、`DB1.DBB0`、`DB1.DBW0`、`DB1.DBD2`、`DB1.DBL4`，以及 `MX0.0`、`MW0`、`IX0.0`、`QX0.0`。位地址必须带 `0–7` 的 bit 索引；`DBW` 应搭配 16 位类型，32 位值通常使用 `DBD`，双精度值使用 `DBL`，字符串和字节数组需要显式长度。

S7-1200/1500 使用绝对 DB 地址时，通常需要在 TIA Portal 中关闭对应 DB 的优化块访问，并允许所需的 PUT/GET 访问。Rack/Slot 必须按实际 CPU 设置。生产环境应限制 TCP 102 的来源和 DB 写权限；`AutoReconnect` 会在通信失败后重试，业务侧写入必须考虑幂等性。

## Mitsubishi MC 3E

```csharp
using System;
using System.Threading.Tasks;
using IndustrialCommSdk.Protocols.Mc;
using IndustrialCommSdk.Runtime;

public static class MitsubishiMcExample
{
    public static async Task RunAsync()
    {
        var client = new MitsubishiMcClient(new MitsubishiMcClientOptions
        {
            DeviceId = "plc-mc-1",
            Host = "192.168.1.30",
            Port = 5000,
            SendTimeoutMilliseconds = 3000,
            ReceiveTimeoutMilliseconds = 5000,
            OperationTimeoutMilliseconds = 5000,
        });

        await client.UseAsync(async connected =>
        {
            ushort current = await connected.ReadUInt16Async("D100");
            Console.WriteLine("D100 = " + current);
            await connected.WriteAsync("D101", (ushort)42);
        });
    }
}
```

当前客户端使用 MC 协议二进制 3E 帧。字设备包括 `D`、`W`、`R`、`SD`、`Z`、`ZR`、`TN`、`SN`、`CN`，位设备包括 `M`、`X`、`Y`、`L`、`SS`、`TS`、`CS` 等。`W`、`X`、`Y`、`ZR` 的设备号按十六进制解析；`D100`、`M100` 按十进制解析。位设备搭配 `bool`，字设备搭配实际占用字数的数据类型。

PLC 侧需要启用一致的 MC/SLMP 二进制 3E TCP 监听端口。当前实现固定使用网络号 `0x00`、PC 号 `0xFF`、目标 I/O `0x03FF` 和目标站号 `0x00`，复杂多层网络拓扑暂不适用。MC TCP 通常没有传输加密，应限制来源地址、隔离控制网络，并核对 PLC 设备区和运行状态。

## OPC UA

```csharp
using System;
using System.Threading.Tasks;
using IndustrialCommSdk.Protocols.OpcUa;
using IndustrialCommSdk.Runtime;

public static class OpcUaExample
{
    public static async Task RunAsync()
    {
        var client = new OpcUaClient(new OpcUaClientOptions
        {
            DeviceId = "opcua-server-1",
            EndpointUrl = "opc.tcp://192.168.1.40:4840",
            Username = "operator",
            Password = "replace-from-secret-store",
            UseSecurity = false, // 仅限隔离的本地测试。
            AutoAcceptUntrustedCertificates = false,
            ConnectTimeoutMilliseconds = 10000,
            OperationTimeoutMilliseconds = 5000,
            SessionTimeoutMilliseconds = 60000,
        });

        await client.UseAsync(async connected =>
        {
            float temperature = await connected.ReadFloatAsync(
                "ns=2;s=Machine/Temperature");
            Console.WriteLine("Temperature = " + temperature);
            await connected.WriteAsync("ns=2;s=Machine/SetPoint", 42.0f);
        });
    }
}
```

地址必须是 OPC UA NodeId，例如 `ns=2;s=Machine/Temperature`、`ns=2;i=1001`；GUID 和 ByteString 也由底层解析。`ns=2` 可能随服务端 NamespaceArray 变化，稳定集成时应核对实际命名空间。写入值的 CLR 类型必须匹配节点 Built-in Type，否则通常返回 `Bad_TypeMismatch`。

`UseSecurity = false` 只适合隔离测试。生产环境选择签名或签名加密端点，将客户端证书加入服务端信任列表，保持 `AutoAcceptUntrustedCertificates = false`，并从安全配置源注入账号密码。

## MQTT

```csharp
using System;
using System.Threading;
using System.Threading.Tasks;
using IndustrialCommSdk.Protocols.Mqtt;
using IndustrialCommSdk.Runtime;

public static class MqttExample
{
    public static async Task RunAsync()
    {
        var client = new MqttClient(new MqttClientOptions
        {
            DeviceId = "mqtt-line-1",
            Host = "192.168.1.50",
            Port = 1883,
            ClientId = "line-1-sdk-client",
            Username = "device-user",
            Password = "replace-from-secret-store",
            UseTls = false, // 仅限隔离的本地测试。
            QualityOfService = 1,
            Retain = false,
            ConnectTimeoutMilliseconds = 5000,
            OperationTimeoutMilliseconds = 5000,
        });

        using (var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10)))
        {
            await client.UseAsync(async connected =>
            {
                await connected.WriteAsync(
                    "factory/line1/command", "start", timeout.Token);
                string status = await connected.ReadStringAsync(
                    "factory/line1/status", 256, timeout.Token);
                Console.WriteLine("Status = " + status);
            }, timeout.Token);
        }
    }
}
```

工业地址直接映射为 MQTT Topic。写入发布 UTF-8 文本，读取把收到的文本按请求数据类型转换；`QualityOfService` 只能是 0、1 或 2。当前读取缓存和等待器按完整 Topic 精确匹配，应使用确定 Topic，不要把 `+` 或 `#` 当作单值读取地址；没有缓存且没有新消息时，会等待到超时或取消。

生产环境启用 TLS、Broker 认证和按 Topic 划分的 ACL，并使用唯一 `ClientId`。谨慎启用 `Retain`：控制 Topic 通常应保持 `Retain = false`，避免旧控制命令在重连后再次执行。

## Redis

```csharp
using System;
using System.Threading.Tasks;
using IndustrialCommSdk.Protocols.Redis;
using IndustrialCommSdk.Runtime;

public static class RedisExample
{
    public static async Task RunAsync()
    {
        var client = new RedisClient(new RedisClientOptions
        {
            DeviceId = "redis-runtime-1",
            Host = "192.168.1.60",
            Port = 6379,
            Username = "industrial-app",
            Password = "replace-from-secret-store",
            Database = 0,
            Ssl = false, // 仅限隔离的本地测试。
            ConnectTimeoutMilliseconds = 5000,
            OperationTimeoutMilliseconds = 5000,
        });

        await client.UseAsync(async connected =>
        {
            await connected.WriteAsync("factory:line1:mode", "auto");
            string mode = await connected.ReadAsync<string>("factory:line1:mode");
            Console.WriteLine("Mode = " + mode);
        });
    }
}
```

当前模块使用 Redis String 的 `GET`/`SET`，字符串和数值以 UTF-8 文本编码，字节数组按原始字节保存；它不是 Redis Hash、List 或 Stream 的通用封装。不存在的 Key 返回 Bad 质量，强类型快捷读取随后抛出协议异常；Key 应包含业务命名空间，批量读写会使用 Redis 批量 String API。

Redis 与 SQL Server/MySQL 历史存储彻底独立，不实现 `IIndustrialHistoryStore`，也不提供关系型分页、汇总或保留期清理。生产环境不要暴露 6379，使用 TLS、ACL、网络白名单和独立账号；需要原子状态转换时在业务层增加版本、锁或事务语义。
