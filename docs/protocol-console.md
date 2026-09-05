# 其他协议控制台实战

本页配合[协议参考](protocols.md)，提供 Modbus TCP/RTU、S7、MC、OPC UA、MQTT、Redis 的完整控制台入口。[ADS 控制台实战](protocols.md#ads-控制台实战)保留在协议参考中。

## 使用方法与验证边界

快速定位：[Modbus TCP](#modbus-tcp) · [Modbus RTU](#modbus-rtu) · [Siemens S7](#siemens-s7) · [Mitsubishi MC 3E](#mitsubishi-mc-3e) · [OPC UA](#opc-ua) · [MQTT](#mqtt) · [Redis](#redis)。

每节从仓库根目录创建独立 .NET 8 控制台，再将该节完整 C# 代码保存为 Program.cs。项目引用会带入当前 SDK 的传递依赖；仓库外使用时调整项目路径。所有地址、端口和类型布局均为示例，需先按设备点表配置或替换。

默认只读，添加 `--write-test` 才会执行该节写入。PLC 示例使用独立测试点并尝试恢复原值；这不是事务，恢复可能因断线失败，也不能和 PLC 周期写入并发使用。MQTT 发布不可撤销，Redis 测试 Key 会保留，详见各节。

每个示例包含单点强类型读取和多点批量读取：强类型入口在坏质量时抛异常；批量结果要逐项检查 `Quality`，不能只判断请求是否抛异常。同一示例是顺序执行，前面读取失败会跳至 finally，不会继续后续读写。

这些示例只验证编译和 API 可用性，不连接真实设备。字符串、数组、时间类型的支持以具体协议为准；ADS 的 ReadArrayAsync、ReadLTimeAsync 等不能直接套用到其他客户端。批量读多个点也不保证获得 PLC 同一扫描周期的原子快照。

## Modbus TCP

### 项目与设备准备

设备开启 Modbus TCP，确认 IP、监听端口和站号。这里采用 Generic Profile：HR0 对应一基引用地址 40001。先查寄存器手册，再替换示例地址与类型；TCP 连接成功并不代表站号或寄存器映射正确。

```powershell
dotnet new console -n ModbusTcpConsole -o samples/ModbusTcpConsole --framework net8.0
dotnet add samples/ModbusTcpConsole/ModbusTcpConsole.csproj reference InduLink.Protocols.Modbus/InduLink.Protocols.Modbus.csproj
```

### 类型与地址对照

| 类型 / C# | 地址示例 | 读取方法与含义 |
| --- | --- | --- |
| BOOL / bool | C0、DI0 | ReadBoolAsync；DI 只读 |
| INT / short | HR0 | ReadInt16Async，占 1 个寄存器 |
| UINT / ushort | IR0、HR0 | ReadUInt16Async；IR 只读 |
| DINT / int、UDINT / uint | HR2 | ReadInt32Async / ReadUInt32Async，占 2 个寄存器 |
| REAL / float | HR4 | ReadFloatAsync，占 2 个寄存器 |
| Double / double | HR6 | ReadDoubleAsync，占 4 个寄存器 |

寄存器只提供 16 位数据，32/64 位类型和字序必须由设备点表约定。这里 HR2 与 HR4 是两个独立的双寄存器值，不是数组索引。连续 ushort 点位可用下方 ReadManyAsync 逐点描述；不要把 ADS ReadArrayAsync 套用到 Modbus。

### 完整 Program.cs

```csharp
using System;
using System.Linq;
using System.Threading;
using InduLink.Abstractions;
using InduLink.Protocols.Modbus;
using InduLink.Runtime;

if (args.Length == 0)
    throw new ArgumentException("请提供连接目标，例如：192.168.1.10");
bool enableWriteTest = args.Contains("--write-test");
using var client = new ModbusTcpClient(new ModbusTcpClientOptions
{
    DeviceId = "console-modbustcp",
    Host = args[0], Port = 502, SlaveId = 1,
    DeviceProfile = ModbusDeviceProfiles.Generic,
    ConnectTimeoutMilliseconds = 3000, OperationTimeoutMilliseconds = 5000
});

try
{
    await client.ConnectAsync();
    Console.WriteLine($"线圈={await client.ReadBoolAsync("C0")}");
    Console.WriteLine($"输入位={await client.ReadBoolAsync("DI0")}");
    Console.WriteLine($"INT={await client.ReadInt16Async("HR0")}");
    Console.WriteLine($"UINT={await client.ReadUInt16Async("IR0")}");
    Console.WriteLine($"DINT={await client.ReadInt32Async("HR2")}");
    Console.WriteLine($"REAL={await client.ReadFloatAsync("HR4")}");

    var requests = new[]
    {
        new ReadRequest(client.DeviceId, "HR0", DataType.UInt16, 1),
        new ReadRequest(client.DeviceId, "HR1", DataType.UInt16, 1)
    };
    var batch = await client.ReadManyAsync(requests, CancellationToken.None);
    foreach (var value in batch.Values)
        Console.WriteLine($"{value.Address}: Quality={value.Quality}, Value={value.Value}");

    if (enableWriteTest)
    {
        var original = await client.ReadUInt16Async("HR10");
        try
        {
            await client.WriteAsync("HR10", (ushort)42);
            var actual = await client.ReadUInt16Async("HR10");
            if (!actual.Equals((ushort)42))
                throw new InvalidOperationException("写入读回不一致，请检查设备周期逻辑或其他写入方。");
            Console.WriteLine("写入及读回验证通过。");
        }
        finally
        {
            await client.WriteAsync("HR10", original);
        }
    }
}
finally
{
    if (client.IsConnected)
        await client.DisconnectAsync();
}
```

### 运行与排错

```powershell
dotnet run --project samples/ModbusTcpConsole/ModbusTcpConsole.csproj -c Release -- 192.168.1.10
# 确认测试写入目标后才执行：
dotnet run --project samples/ModbusTcpConsole/ModbusTcpConsole.csproj -c Release -- 192.168.1.10 --write-test
```

| 现象 | 检查方向 |
| --- | --- |
| Illegal Data Address / Bad 质量 | 核对零基偏移、一基引用地址、寄存器区和连续读取范围 |
| 整数正常，Float 异常 | 检查 PLC 实际类型、双寄存器顺序及 DeviceProfile |
| 连接超时或无响应 | 核对端口、站号、设备连接数量限制 |

## Modbus RTU

### 项目与设备准备

确认 COM 端口、站号、波特率、数据位、校验位、停止位。示例为 9600-8-E-1；使用前替换成设备参数，当前客户端要求 8 个数据位。先关闭占用同一串口的调试工具。

```powershell
dotnet new console -n ModbusRtuConsole -o samples/ModbusRtuConsole --framework net8.0
dotnet add samples/ModbusRtuConsole/ModbusRtuConsole.csproj reference InduLink.Protocols.Modbus/InduLink.Protocols.Modbus.csproj
```

### 类型与地址对照

| 类型 / C# | 地址示例 | 读取方法与含义 |
| --- | --- | --- |
| BOOL / bool | C0、DI0 | ReadBoolAsync；DI 只读 |
| INT / short、UINT / ushort | HR0 | ReadInt16Async / ReadUInt16Async |
| DINT / int、REAL / float | HR2 | ReadInt32Async / ReadFloatAsync，按设备声明二选一 |
| 输入寄存器 / ushort | IR0 | ReadUInt16Async，只读 |

地址、寄存器占用与 Profile 规则和 Modbus TCP 一致；总线的串行传输速度决定轮询间隔，增大并发不能让同一串口同时完成多条请求。不要使用广播站号做这个需要响应的读写示例。

### 完整 Program.cs

```csharp
using System;
using System.Linq;
using System.Threading;
using InduLink.Abstractions;
using InduLink.Protocols.Modbus;
using InduLink.Runtime;
using System.IO.Ports;

if (args.Length == 0)
    throw new ArgumentException("请提供连接目标，例如：COM3");
bool enableWriteTest = args.Contains("--write-test");
using var client = new ModbusRtuClient(new ModbusRtuClientOptions
{
    DeviceId = "console-modbusrtu",
    PortName = args[0], BaudRate = 9600, DataBits = 8,
    Parity = Parity.Even, StopBits = StopBits.One, SlaveId = 1,
    ReadTimeout = 3000, WriteTimeout = 3000,
    OperationTimeoutMilliseconds = 5000, Retries = 2,
    WaitToRetryMilliseconds = 100, DeviceProfile = ModbusDeviceProfiles.Generic
});

try
{
    await client.ConnectAsync();
    Console.WriteLine($"线圈={await client.ReadBoolAsync("C0")}");
    Console.WriteLine($"输入={await client.ReadBoolAsync("DI0")}");
    Console.WriteLine($"保持寄存器={await client.ReadUInt16Async("HR0")}");
    Console.WriteLine($"输入寄存器={await client.ReadUInt16Async("IR0")}");

    var requests = new[]
    {
        new ReadRequest(client.DeviceId, "HR0", DataType.UInt16, 1),
        new ReadRequest(client.DeviceId, "HR1", DataType.UInt16, 1)
    };
    var batch = await client.ReadManyAsync(requests, CancellationToken.None);
    foreach (var value in batch.Values)
        Console.WriteLine($"{value.Address}: Quality={value.Quality}, Value={value.Value}");

    if (enableWriteTest)
    {
        var original = await client.ReadUInt16Async("HR10");
        try
        {
            await client.WriteAsync("HR10", (ushort)42);
            var actual = await client.ReadUInt16Async("HR10");
            if (!actual.Equals((ushort)42))
                throw new InvalidOperationException("写入读回不一致，请检查设备周期逻辑或其他写入方。");
            Console.WriteLine("写入及读回验证通过。");
        }
        finally
        {
            await client.WriteAsync("HR10", original);
        }
    }
}
finally
{
    if (client.IsConnected)
        await client.DisconnectAsync();
}
```

### 运行与排错

```powershell
dotnet run --project samples/ModbusRtuConsole/ModbusRtuConsole.csproj -c Release -- COM3
# 确认测试写入目标后才执行：
dotnet run --project samples/ModbusRtuConsole/ModbusRtuConsole.csproj -c Release -- COM3 --write-test
```

| 现象 | 检查方向 |
| --- | --- |
| 无响应 | 检查站号、A/B 极性、波特率和校验参数 |
| 串口打不开 | 检查端口名称、驱动、权限和程序占用 |
| 偶发校验/超时 | 检查终端电阻、接地、干扰及总线负载 |

## Siemens S7

### 项目与设备准备

核对 CPU、Rack/Slot、TCP 102 和 DB 绝对访问设置；S7-1200/1500 检查 DB 优化访问及 PUT/GET。示例是独立的点表布局，不假定它与 Snap7 Demo 的 DB1 一致。

```powershell
dotnet new console -n S7Console -o samples/S7Console --framework net8.0
dotnet add samples/S7Console/S7Console.csproj reference InduLink.Protocols.S7/InduLink.Protocols.S7.csproj
```

### 类型与地址对照

| 类型 / C# | 地址示例 | 读取方法与含义 |
| --- | --- | --- |
| BOOL / bool | DB1.DBX0.0 | ReadBoolAsync，明确 bit 索引 |
| INT / short、WORD / ushort | DB1.DBW2 | ReadInt16Async / ReadUInt16Async |
| DINT / int | DB1.DBD4 | ReadInt32Async |
| REAL / float | DB1.DBD8 | ReadFloatAsync |
| 原生 STRING[20] / string | DB1.DBX12.0 | ReadDbStringAsync(address, 20)，占 22 字节 |
| 原始字节串 / byte[] | DB1.DBB12 | ReadByteArrayAsync(address, length)，不会解析 STRING 头 |

DBD 只表示四字节起点，不能据此判断 DINT 还是 REAL。上表 STRING 从字节 12 起含两字节头和 20 字节容量，因此下一个 INT 放在字节 34。DB1.DBX12.0 和 DB1.DBB12 均可表示该原生字符串起点；不要把长度加上头部后传给 ReadDbStringAsync。ReadStringAsync 则读取原始 ASCII 字节串。

### 完整 Program.cs

```csharp
using System;
using System.Linq;
using System.Threading;
using InduLink.Abstractions;
using InduLink.Protocols.S7;
using InduLink.Runtime;
using CpuType = S7.Net.CpuType;

if (args.Length == 0)
    throw new ArgumentException("请提供连接目标，例如：192.168.1.20");
bool enableWriteTest = args.Contains("--write-test");
using var client = new SiemensS7Client(new SiemensS7ClientOptions
{
    DeviceId = "console-s7",
    Host = args[0], CpuType = CpuType.S71200, Rack = 0, Slot = 1,
    AutoReconnect = true, ConnectTimeoutMilliseconds = 5000,
    OperationTimeoutMilliseconds = 5000
});

try
{
    await client.ConnectAsync();
    Console.WriteLine($"BOOL={await client.ReadBoolAsync("DB1.DBX0.0")}");
    Console.WriteLine($"INT={await client.ReadInt16Async("DB1.DBW2")}");
    Console.WriteLine($"DINT={await client.ReadInt32Async("DB1.DBD4")}");
    Console.WriteLine($"REAL={await client.ReadFloatAsync("DB1.DBD8")}");
    // 仅当 DB1 字节 12 处声明了 STRING[20]，并预留 22 字节时使用。
    Console.WriteLine($"STRING={await client.ReadDbStringAsync("DB1.DBX12.0", 20)}");

    var requests = new[]
    {
        new ReadRequest(client.DeviceId, "DB1.DBW2", DataType.Int16, 1),
        new ReadRequest(client.DeviceId, "DB1.DBW34", DataType.Int16, 1)
    };
    var batch = await client.ReadManyAsync(requests, CancellationToken.None);
    foreach (var value in batch.Values)
        Console.WriteLine($"{value.Address}: Quality={value.Quality}, Value={value.Value}");

    if (enableWriteTest)
    {
        var original = await client.ReadInt16Async("DB1.DBW34");
        try
        {
            await client.WriteAsync("DB1.DBW34", (short)42);
            var actual = await client.ReadInt16Async("DB1.DBW34");
            if (!actual.Equals((short)42))
                throw new InvalidOperationException("写入读回不一致，请检查设备周期逻辑或其他写入方。");
            Console.WriteLine("写入及读回验证通过。");
        }
        finally
        {
            await client.WriteAsync("DB1.DBW34", original);
        }
    }
}
finally
{
    if (client.IsConnected)
        await client.DisconnectAsync();
}
```

### 运行与排错

```powershell
dotnet run --project samples/S7Console/S7Console.csproj -c Release -- 192.168.1.20
# 确认测试写入目标后才执行：
dotnet run --project samples/S7Console/S7Console.csproj -c Release -- 192.168.1.20 --write-test
```

| 现象 | 检查方向 |
| --- | --- |
| 可连接但 DB 读失败 | 检查 DB 是否存在、优化访问、PUT/GET 和偏移长度 |
| Float 显示极小值 | 核对该地址实际声明是否是 DINT，而不是先改解码器 |
| 字符串异常 | 检查是否指向头部、声明容量及原生/原始字符串 API |

## Mitsubishi MC 3E

### 项目与设备准备

PLC 需配置 MC/SLMP 二进制 3E TCP 服务，并确认监听端口；示例 5000 是占位配置，不适用于所有 PLC。当前固定路由字段及不支持复杂拓扑的边界见协议参考。

```powershell
dotnet new console -n McConsole -o samples/McConsole --framework net8.0
dotnet add samples/McConsole/McConsole.csproj reference InduLink.Protocols.Mc/InduLink.Protocols.Mc.csproj
```

### 类型与地址对照

| 类型 / C# | 地址示例 | 读取方法与含义 |
| --- | --- | --- |
| 位 / bool | M100、X10、Y10 | ReadBoolAsync |
| 字 / short、ushort | D100、W10 | ReadInt16Async / ReadUInt16Async |
| 双字 / int、uint | D102 | ReadInt32Async / ReadUInt32Async，占 2 字 |
| REAL / float | D104 | ReadFloatAsync，占 2 字 |
| Double / double | D106 | ReadDoubleAsync，占 4 字 |

D100、M100 按十进制；W、X、Y、ZR 设备号按十六进制，例如 X10 的设备号为十进制 16。读取 DINT/REAL 占相邻两个字，要避免与下一点重叠。此处只补使用文档，不扩展 MC 路由或协议实现。

### 完整 Program.cs

```csharp
using System;
using System.Linq;
using System.Threading;
using InduLink.Abstractions;
using InduLink.Protocols.Mc;
using InduLink.Runtime;

if (args.Length == 0)
    throw new ArgumentException("请提供连接目标，例如：192.168.1.30");
bool enableWriteTest = args.Contains("--write-test");
using var client = new MitsubishiMcClient(new MitsubishiMcClientOptions
{
    DeviceId = "console-mc",
    Host = args[0], Port = 5000, SendTimeoutMilliseconds = 3000,
    ReceiveTimeoutMilliseconds = 5000, OperationTimeoutMilliseconds = 5000
});

try
{
    await client.ConnectAsync();
    Console.WriteLine($"M={await client.ReadBoolAsync("M100")}");
    Console.WriteLine($"INT={await client.ReadInt16Async("D100")}");
    Console.WriteLine($"UINT={await client.ReadUInt16Async("D101")}");
    Console.WriteLine($"DINT={await client.ReadInt32Async("D102")}");
    Console.WriteLine($"REAL={await client.ReadFloatAsync("D104")}");

    var requests = new[]
    {
        new ReadRequest(client.DeviceId, "D100", DataType.Int16, 1),
        new ReadRequest(client.DeviceId, "D101", DataType.Int16, 1)
    };
    var batch = await client.ReadManyAsync(requests, CancellationToken.None);
    foreach (var value in batch.Values)
        Console.WriteLine($"{value.Address}: Quality={value.Quality}, Value={value.Value}");

    if (enableWriteTest)
    {
        var original = await client.ReadInt16Async("D110");
        try
        {
            await client.WriteAsync("D110", (short)42);
            var actual = await client.ReadInt16Async("D110");
            if (!actual.Equals((short)42))
                throw new InvalidOperationException("写入读回不一致，请检查设备周期逻辑或其他写入方。");
            Console.WriteLine("写入及读回验证通过。");
        }
        finally
        {
            await client.WriteAsync("D110", original);
        }
    }
}
finally
{
    if (client.IsConnected)
        await client.DisconnectAsync();
}
```

### 运行与排错

```powershell
dotnet run --project samples/McConsole/McConsole.csproj -c Release -- 192.168.1.30
# 确认测试写入目标后才执行：
dotnet run --project samples/McConsole/McConsole.csproj -c Release -- 192.168.1.30 --write-test
```

| 现象 | 检查方向 |
| --- | --- |
| TCP 通但读取失败 | 核对二进制/ASCII、3E 帧、PLC 开放设置和设备区 |
| X/Y/W 点位偏移 | 核对十六进制设备号，不能按十进制解释 |
| 双字/浮点异常 | 核对实际占用字数、PLC 声明和地址重叠 |

## OPC UA

### 项目与设备准备

使用服务端提供的完整 opc.tcp 端点和 NodeId，确认节点可读。示例启用安全端点且拒绝自动信任未知证书，运行前需建立双方证书信任；匿名端点可不设置用户名，认证端点通过环境变量提供账号密码。

```powershell
dotnet new console -n OpcUaConsole -o samples/OpcUaConsole --framework net8.0
dotnet add samples/OpcUaConsole/OpcUaConsole.csproj reference InduLink.Protocols.OpcUa/InduLink.Protocols.OpcUa.csproj
```

### 类型与地址对照

| 类型 / C# | 地址示例 | 读取方法与含义 |
| --- | --- | --- |
| Boolean / bool | ns=2;s=Demo/Running | ReadBoolAsync |
| Int16 / short、UInt16 / ushort | 服务端实际 NodeId | ReadInt16Async / ReadUInt16Async |
| Int32 / int、UInt32 / uint | ns=2;s=Demo/Count | ReadInt32Async / ReadUInt32Async，按节点类型选择 |
| Float / float、Double / double | ns=2;s=Demo/Temperature | ReadFloatAsync / ReadDoubleAsync，按节点类型选择 |
| String / string | ns=2;s=Demo/Status | ReadStringAsync |
| ByteString / byte[] | 服务端实际 NodeId | ReadByteArrayAsync |

NodeId 是节点标识，不是 PLC 字节地址。ns 索引可能随 NamespaceArray 改变，需重新核对。写入 float 用 42.0f，double 用 42.0d；同一个数值不同 CLR 类型也可能被服务端拒绝。ReadManyAsync 表示多个节点，不等价于读取一个数组节点；本示例不宣称通用数组或结构体支持。

### 完整 Program.cs

```csharp
using System;
using System.Linq;
using System.Threading;
using InduLink.Abstractions;
using InduLink.Protocols.OpcUa;
using InduLink.Runtime;

if (args.Length == 0)
    throw new ArgumentException("请提供连接目标，例如：opc.tcp://192.168.1.40:4840");
bool enableWriteTest = args.Contains("--write-test");
using var client = new OpcUaClient(new OpcUaClientOptions
{
    DeviceId = "console-opcua",
    EndpointUrl = args[0],
    Username = Environment.GetEnvironmentVariable("OPCUA_USERNAME"),
    Password = Environment.GetEnvironmentVariable("OPCUA_PASSWORD"),
    UseSecurity = true, AutoAcceptUntrustedCertificates = false,
    ConnectTimeoutMilliseconds = 10000, OperationTimeoutMilliseconds = 5000,
    SessionTimeoutMilliseconds = 60000
});

try
{
    await client.ConnectAsync();
    Console.WriteLine($"运行={await client.ReadBoolAsync("ns=2;s=Demo/Running")}");
    Console.WriteLine($"计数={await client.ReadInt32Async("ns=2;s=Demo/Count")}");
    Console.WriteLine($"温度={await client.ReadFloatAsync("ns=2;s=Demo/Temperature")}");
    Console.WriteLine($"状态={await client.ReadStringAsync("ns=2;s=Demo/Status", 80)}");

    var requests = new[]
    {
        new ReadRequest(client.DeviceId, "ns=2;s=Demo/Temperature", DataType.Float, 1),
        new ReadRequest(client.DeviceId, "ns=2;s=Demo/SetPoint", DataType.Float, 1)
    };
    var batch = await client.ReadManyAsync(requests, CancellationToken.None);
    foreach (var value in batch.Values)
        Console.WriteLine($"{value.Address}: Quality={value.Quality}, Value={value.Value}");

    if (enableWriteTest)
    {
        var original = await client.ReadFloatAsync("ns=2;s=Demo/SetPoint");
        try
        {
            await client.WriteAsync("ns=2;s=Demo/SetPoint", 42.0f);
            var actual = await client.ReadFloatAsync("ns=2;s=Demo/SetPoint");
            if (!actual.Equals(42.0f))
                throw new InvalidOperationException("写入读回不一致，请检查设备周期逻辑或其他写入方。");
            Console.WriteLine("写入及读回验证通过。");
        }
        finally
        {
            await client.WriteAsync("ns=2;s=Demo/SetPoint", original);
        }
    }
}
finally
{
    if (client.IsConnected)
        await client.DisconnectAsync();
}
```

### 运行与排错

```powershell
dotnet run --project samples/OpcUaConsole/OpcUaConsole.csproj -c Release -- opc.tcp://192.168.1.40:4840
# 确认测试写入目标后才执行：
dotnet run --project samples/OpcUaConsole/OpcUaConsole.csproj -c Release -- opc.tcp://192.168.1.40:4840 --write-test
```

| 现象 | 检查方向 |
| --- | --- |
| BadCertificateUntrusted | 在对应信任库建立证书信任，核对证书名称和有效期 |
| BadNodeIdUnknown | 核对完整 NodeId 和 NamespaceArray，勿用显示名称替代 |
| BadTypeMismatch / BadNotWritable | 核对 Built-in Type、AccessLevel 和用户权限 |

## MQTT

### 项目与设备准备

示例连接隔离测试 Broker 的 1883 明文端口；生产启用 UseTls 并使用 Broker 的 TLS 端口。准备另一个发布端持续向 demo/temperature、demo/status、demo/setpoint 发布对应文本，或提前配置合适的保留状态消息。控制台不会替你产生这些输入。

```powershell
dotnet new console -n MqttConsole -o samples/MqttConsole --framework net8.0
dotnet add samples/MqttConsole/MqttConsole.csproj reference InduLink.Protocols.Mqtt/InduLink.Protocols.Mqtt.csproj
```

### 类型与地址对照

| 类型 / C# | 地址示例 | 读取方法与含义 |
| --- | --- | --- |
| 数字文本 / float | demo/temperature | 消息例如 23.5，ReadFloatAsync |
| 整数文本 / int | demo/count | 消息例如 42，ReadInt32Async |
| 文本 / string | demo/status | 消息例如 running，ReadStringAsync |
| 原始 payload / byte[] | demo/raw | ReadByteArrayAsync，不解码为文本 |

Topic 精确区分大小写，单值读取不接受用 +/# 代替实际 Topic。没有缓存时读取等待消息到达，已有缓存则可能立即返回旧值，不等于每次向设备请求新值。String 的 length 不是 MQTT 固定字段长度；JSON 需先 ReadStringAsync，再由业务层解析。消息发布无法通过 finally 撤回，测试 Topic 默认 Retain=false。

### 完整 Program.cs

```csharp
using System;
using System.Linq;
using System.Threading;
using InduLink.Abstractions;
using InduLink.Protocols.Mqtt;
using InduLink.Runtime;

if (args.Length == 0)
    throw new ArgumentException("请提供连接目标，例如：127.0.0.1");
bool enableWriteTest = args.Contains("--write-test");
using var client = new MqttClient(new MqttClientOptions
{
    DeviceId = "console-mqtt",
    Host = args[0], Port = 1883,
    ClientId = "indulink-console-" + Guid.NewGuid().ToString("N"),
    Username = Environment.GetEnvironmentVariable("MQTT_USERNAME"),
    Password = Environment.GetEnvironmentVariable("MQTT_PASSWORD"),
    UseTls = false, QualityOfService = 1, Retain = false,
    ConnectTimeoutMilliseconds = 5000, OperationTimeoutMilliseconds = 5000
});

try
{
    await client.ConnectAsync();
    Console.WriteLine($"温度={await client.ReadFloatAsync("demo/temperature")}");
    Console.WriteLine($"状态={await client.ReadStringAsync("demo/status", 80)}");

    var requests = new[]
    {
        new ReadRequest(client.DeviceId, "demo/temperature", DataType.Float, 1),
        new ReadRequest(client.DeviceId, "demo/setpoint", DataType.Float, 1)
    };
    var batch = await client.ReadManyAsync(requests, CancellationToken.None);
    foreach (var value in batch.Values)
        Console.WriteLine($"{value.Address}: Quality={value.Quality}, Value={value.Value}");

    if (enableWriteTest)
    {
        // 只发布演示消息，不向真实设备命令 Topic 发送数据。
        await client.WriteAsync("demo/indulink/test", "hello");
        Console.WriteLine("演示消息已发布；发布成功不代表订阅端业务已执行。");
    }
}
finally
{
    if (client.IsConnected)
        await client.DisconnectAsync();
}
```

### 运行与排错

```powershell
dotnet run --project samples/MqttConsole/MqttConsole.csproj -c Release -- 127.0.0.1
# 确认测试写入目标后才执行：
dotnet run --project samples/MqttConsole/MqttConsole.csproj -c Release -- 127.0.0.1 --write-test
```

| 现象 | 检查方向 |
| --- | --- |
| 读取超时 | 检查发布端是否在读取订阅后发消息、Topic 大小写及 ACL |
| 数值解析失败 | 检查 payload 是数字文本还是 JSON/二进制 |
| 连接反复被踢 | 检查是否复用 ClientId 以及 Broker 连接策略 |

## Redis

### 项目与设备准备

示例使用隔离本地 Redis 的 6379 明文端口、数据库 0。认证从环境变量读取；生产按服务器设置 Ssl 和端口。提前由管理工具在数据库 0 创建 String 类型的 demo:count=42、demo:target=100、demo:status=ready。

```powershell
dotnet new console -n RedisConsole -o samples/RedisConsole --framework net8.0
dotnet add samples/RedisConsole/RedisConsole.csproj reference InduLink.Protocols.Redis/InduLink.Protocols.Redis.csproj
```

### 类型与地址对照

| 类型 / C# | 地址示例 | 读取方法与含义 |
| --- | --- | --- |
| 整数文本 / int | demo:count | ReadInt32Async |
| 浮点文本 / float、double | demo:temperature | ReadFloatAsync / ReadDoubleAsync |
| 文本 / string | demo:status | ReadStringAsync |
| 原始 bytes / byte[] | demo:raw | ReadByteArrayAsync |

Key 区分大小写，值按文本/原始字节转换；当前模块不是 Hash/List/Stream 的封装。读取不存在的 Key 返回 Bad 质量，强类型入口会抛异常。普通 WriteAsync 使用 SET，不保留既有 TTL；因此测试用唯一 Key，不用“恢复旧字符串”冒充恢复原有过期策略。ReadManyAsync 可一次描述多个独立 Key。

### 完整 Program.cs

```csharp
using System;
using System.Linq;
using System.Threading;
using InduLink.Abstractions;
using InduLink.Protocols.Redis;
using InduLink.Runtime;

if (args.Length == 0)
    throw new ArgumentException("请提供连接目标，例如：127.0.0.1");
bool enableWriteTest = args.Contains("--write-test");
using var client = new RedisClient(new RedisClientOptions
{
    DeviceId = "console-redis",
    Host = args[0], Port = 6379, Database = 0,
    Username = Environment.GetEnvironmentVariable("REDIS_USERNAME"),
    Password = Environment.GetEnvironmentVariable("REDIS_PASSWORD"),
    Ssl = false, ConnectTimeoutMilliseconds = 5000,
    OperationTimeoutMilliseconds = 5000
});

try
{
    await client.ConnectAsync();
    Console.WriteLine($"计数={await client.ReadInt32Async("demo:count")}");
    Console.WriteLine($"状态={await client.ReadStringAsync("demo:status", 80)}");

    var requests = new[]
    {
        new ReadRequest(client.DeviceId, "demo:count", DataType.Int32, 1),
        new ReadRequest(client.DeviceId, "demo:target", DataType.Int32, 1)
    };
    var batch = await client.ReadManyAsync(requests, CancellationToken.None);
    foreach (var value in batch.Values)
        Console.WriteLine($"{value.Address}: Quality={value.Quality}, Value={value.Value}");

    if (enableWriteTest)
    {
        // 唯一测试 Key，避免覆盖已有业务值及其 TTL。
        string key = "demo:indulink:test:" + Guid.NewGuid().ToString("N");
        await client.WriteAsync(key, "hello");
        Console.WriteLine($"测试 Key={key}, Value={await client.ReadStringAsync(key, 80)}");
        Console.WriteLine("测试 Key 会保留，请通过 Redis 管理工具清理。");
    }
}
finally
{
    if (client.IsConnected)
        await client.DisconnectAsync();
}
```

### 运行与排错

```powershell
dotnet run --project samples/RedisConsole/RedisConsole.csproj -c Release -- 127.0.0.1
# 确认测试写入目标后才执行：
dotnet run --project samples/RedisConsole/RedisConsole.csproj -c Release -- 127.0.0.1 --write-test
```

| 现象 | 检查方向 |
| --- | --- |
| Key 不存在 / Bad 质量 | 核对数据库编号、Key 大小写和预置数据 |
| WRONGTYPE | Key 必须为 Redis String，而不是 Hash/List |
| NOAUTH / NOPERM | 检查 ACL 用户、密码和 Key 命名空间权限 |
