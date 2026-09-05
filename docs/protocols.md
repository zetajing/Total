# 协议参考

本页集中收录所有 canonical 协议键的直接客户端示例。示例使用当前的 `Options + Client` API，不依赖已删除的静态工厂或 `SimpleClient`。

从新建控制台开始接入时，可直接使用 [Modbus TCP/RTU、S7、MC、OPC UA、MQTT、Redis 完整实战](protocol-console.md)；[ADS 完整实战](#ads-控制台实战)见本页对应章节。完整实战默认只读，写入需要显式开启。

| canonical key | 客户端 | 直接引用的程序集 |
| --- | --- | --- |
| `modbus-tcp` | `ModbusTcpClient` | `InduLink.Protocols.Modbus` |
| `modbus-rtu` | `ModbusRtuClient` | `InduLink.Protocols.Modbus` |
| `siemens-s7` | `SiemensS7Client` | `InduLink.Protocols.S7` |
| `mitsubishi-mc` | `MitsubishiMcClient` | `InduLink.Protocols.Mc` |
| `ads` | `AdsClient` | `InduLink.Protocols.Ads` |
| `opc-ua` | `OpcUaClient` | `InduLink.Protocols.OpcUa` |
| `mqtt` | `MqttClient` | `InduLink.Protocols.Mqtt` |
| `redis` | `RedisClient` | `InduLink.Protocols.Redis` |

## 通用规则

- 当前 SDK 目标框架是 `net8.0`；WPF/WinForms 应用使用 Windows 专用目标框架。
- 项目直接引用对应协议项目即可，传递依赖会带入 `InduLink.Runtime`、`InduLink.Abstractions` 及协议自己的第三方包。
- `InduLink.Runtime` 提供 `UseAsync`、强类型读取和写入扩展。
- `UseAsync` 依次完成连接、业务操作、断开和释放；需要长连接时自行调用 `ConnectAsync`、`DisconnectAsync` 和 `Dispose`。
- 示例地址、端口和账号都是占位值。写入前必须替换成测试设备上的安全地址，并确认数据类型、长度和字节序。
- 配置驱动场景使用各模块的 Provider；本页专门展示单模块直接构造客户端。

## Modbus TCP

所需程序集：`InduLink.Protocols.Modbus`，快捷扩展来自 `InduLink.Runtime`。

```csharp
using System;
using System.Threading.Tasks;
using InduLink.Protocols.Modbus;
using InduLink.Runtime;

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
using InduLink.Protocols.Modbus;
using InduLink.Runtime;

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
using InduLink.Protocols.S7;
using InduLink.Runtime;
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

点位表读取西门子原生字符串时，可使用 `"type": "STRING[50]"`。其中地址指向 STRING 的两个字节头部，长度 `50` 是声明的最大字符数；现有 `"type": "String"` 仍表示原始 ASCII 字节串，不会改变其语义。

S7-1200/1500 使用绝对 DB 地址时，通常需要在 TIA Portal 中关闭对应 DB 的优化块访问，并允许所需的 PUT/GET 访问。Rack/Slot 必须按实际 CPU 设置。生产环境应限制 TCP 102 的来源和 DB 写权限；`AutoReconnect` 会在通信失败后重试，业务侧写入必须考虑幂等性。

## Mitsubishi MC 3E

```csharp
using System;
using System.Threading.Tasks;
using InduLink.Protocols.Mc;
using InduLink.Runtime;

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
using InduLink.Protocols.OpcUa;
using InduLink.Runtime;

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

## TwinCAT ADS

```csharp
using System;
using System.Threading.Tasks;
using InduLink.Protocols.Ads;
using InduLink.Runtime;

public static class AdsExample
{
    public static async Task RunAsync()
    {
        var client = new AdsClient(new AdsClientOptions
        {
            DeviceId = "twincat-plc-1",
            AmsNetId = "192.168.1.90.1.1",
            Port = 851,
            ConnectTimeoutMilliseconds = 10000,
            OperationTimeoutMilliseconds = 5000,
        });

        await client.UseAsync(async connected =>
        {
            bool start = await connected.ReadBoolAsync("MAIN.xStart");
            int target = await connected.ReadInt32Async("MAIN.nTarget");
            float speed = await connected.ReadFloatAsync("MAIN.rSpeed");
            double temperature = await connected.ReadDoubleAsync("MAIN.lrTemperature");
            string status = await connected.ReadStringAsync("MAIN.sStatus", 80);
            TimeSpan delay = await connected.ReadAnyAsync<TimeSpan>("MAIN.tDelay");

            Console.WriteLine($"Start={start}, Target={target}, Speed={speed}, Temperature={temperature}, Status={status}, Delay={delay}");

            // 仅在确认 nTarget 可安全写入后执行；测试结束后恢复原值。
            await connected.WriteAsync("MAIN.nTarget", target);

            // 官方 ADS 结构体通过 ReadAnyAsync/WriteAnyAsync 访问。
            // var value = await connected.ReadAnyAsync<ComplexStruct>("MAIN.ComplexStruct1");
        });
    }
}
```

ADS 地址直接使用 PLC 符号名，例如截图中的 `MAIN.xStart`、`MAIN.nTarget`、`MAIN.rSpeed`、`MAIN.lrTemperature`、`MAIN.sStatus` 和 `MAIN.tDelay`；字符串长度通过请求的 `length` 指定，`TIME` 映射为 `TimeSpan`。`AdsClient` 的订阅使用 ADS 原生 OnChange/Cyclic 通知，不需要 SDK 轮询；批量读写使用官方 SumCommand 并自动按项目数和报文大小分包。结构体需按 TwinCAT 内存布局声明 `[StructLayout(LayoutKind.Sequential, Pack = 1)]`，再使用 `ReadAnyAsync<T>`/`WriteAnyAsync`。核心项目直接依赖 `Beckhoff.TwinCAT.Ads 7.0.317`，不再携带旧版本地 DLL。

不安装 TwinCAT 的电脑可选部署 `InduLink.AdsRouter.Host.exe` 和 `InduLink.Protocols.Ads.Router.dll`。编辑宿主输出目录中的 `appsettings.json`：设置本机 `AmsRouter:Name`、本机 `AmsRouter:NetId` 和虚拟 PLC 的 `RemoteConnections`；远程 PLC 还必须添加指向本机 AMS Net ID 的返回路由。默认端口是 TCP `48898`，PLC Runtime 1 的 ADS 端口通常是 `851`。独立 Router 不提供 ADS Secure 和系统服务端口 `10000`，只能部署在受防火墙保护的可信工业网络，并且不能与系统 TwinCAT Router 同时占用 `48898`。

虚拟 PLC 的显式集成测试位于 `InduLink.Tests/AdsVirtualPlcIntegrationTests.cs`。运行前设置 `ADS_VIRTUAL_PLC_TARGET_AMS_NET_ID`，可选设置 `ADS_VIRTUAL_PLC_TARGET_IP`、`ADS_VIRTUAL_PLC_LOCAL_AMS_NET_ID`、`ADS_VIRTUAL_PLC_ROUTER_MODE`、`ADS_VIRTUAL_PLC_PORT` 和 `ADS_VIRTUAL_PLC_STATUS_LENGTH`，再执行测试过滤器 `AdsVirtualPlcIntegrationTests`。测试只写入 `MAIN.nTarget`、`MAIN.rSpeed`、`MAIN.tDelay`，并在 `finally` 中恢复原值，不写入电机状态或故障变量。

### ADS 控制台实战

以下示例参考 ConsoleApp1 的 `IN1.*` 读写流程，并按当前 SDK API 整理。`IN1` 是该示例的符号前缀，不是 SDK 固定名称；实际项目可能使用 `MAIN` 或 GVL 名称，必须按 PLC 导出的完整符号替换。

#### 创建项目与连接准备

在本仓库根目录执行：

```powershell
dotnet new console -n AdsConsole -o samples/AdsConsole --framework net8.0
dotnet add samples/AdsConsole/AdsConsole.csproj reference InduLink.Protocols.Ads/InduLink.Protocols.Ads.csproj
dotnet build samples/AdsConsole/AdsConsole.csproj -c Release
```

生成的项目会通过项目引用获得 Runtime、Abstractions 和 Beckhoff ADS 包，不需要复制 DLL，也不需要引用聚合项目 `InduLink`。如果控制台在仓库外，`dotnet add ... reference ...` 的两个路径都应改成真实路径；不要直接复制另一台机器的相对 `ProjectReference`。

- `DeviceId` 是 SDK 内的设备标识；`AmsNetId` 是目标 PLC 的六段 AMS Net ID，不是四段 IP 地址，也不应仅根据 IP 猜测。
- `Port = 851` 表示目标 ADS Runtime 端口，与 Router 的 TCP 48898 端口不同。
- 先配置系统 TwinCAT Router 或上文的独立 Router，再确认目标的返回路由。`AdsClientOptions` 本身不创建远程路由。
- 从只读设备状态开始，再读取一个已存在的符号；示例所有变量必须存在，缺少任何一个都会使顺序读取在该处结束。

#### PLC 类型与读取方法

下表地址沿用参考项目，类型以实际 PLC 声明为准。位宽相同不代表数值语义相同，例如 `DWORD` 用 `uint` 读取，`REAL` 用 `float` 读取。

| PLC 类型 | C# 类型 | 示例调用 |
| --- | --- | --- |
| BOOL | `bool` | `ReadBoolAsync("IN1.xStop")` |
| SINT | `sbyte` | `ReadSByteAsync("IN1.sintValue")` |
| USINT / BYTE | `byte` | `ReadAsync<byte>("IN1.usintValue")` / `ReadAsync<byte>("IN1.byteValue")` |
| INT | `short` | `ReadInt16Async("IN1.intValue")` |
| UINT / WORD | `ushort` | `ReadUInt16Async("IN1.uintValue")` / `ReadUInt16Async("IN1.wordValue")` |
| DINT | `int` | `ReadInt32Async("IN1.nCount")` |
| UDINT / DWORD | `uint` | `ReadUInt32Async("IN1.udintValue")` / `ReadUInt32Async("IN1.dwordValue")` |
| LINT | `long` | `ReadInt64Async("IN1.lintValue")` |
| ULINT / LWORD | `ulong` | `ReadUInt64Async("IN1.ulintValue")` / `ReadUInt64Async("IN1.lwordValue")` |
| REAL / LREAL | `float` / `double` | `ReadFloatAsync("IN1.rSpeed")` / `ReadDoubleAsync("IN1.lrTemperature")` |
| SDK `DataType.Char` 对应的单字符符号 | `char` | `ReadValueAsync<char>("IN1.charValue", DataType.Char)` |
| STRING(80) | `string` | `ReadStringAsync("IN1.sStatus", 80)` |
| WSTRING(80) | `string` | `ReadWStringAsync("IN1.wsStatus", 80)` |
| TIME | `TimeSpan` | `ReadTimeAsync("IN1.tDelay")` |
| LTIME | `TimeSpan` | `ReadLTimeAsync("IN1.lTimeValue")` |
| TOD / TIME_OF_DAY | `TimeSpan` | `ReadTimeOfDayAsync("IN1.todValue")` |
| DATE | `DateTimeOffset` | `ReadDateAsync("IN1.dateValue")` |
| DT / DATE_AND_TIME | `DateTimeOffset` | `ReadDateTimeAsync("IN1.dateTimeValue")` |
| ARRAY[0..4] OF DINT | `int[]` | `ReadArrayAsync<int>("IN1.aValues", 5)` |

参考代码中的 CHAR 读取位置为空；上表提供 SDK 显式类型调用方式，但需要先核对 `charValue` 的真实 PLC 声明，不能把 `STRING(1)`、BYTE 或 WCHAR 自动当成该类型。

字符串长度传 PLC 声明容量，例如 `STRING(80)` / `WSTRING(80)` 传 `80`，不是当前文字长度，也不手动加结束符。当前 ADS 实现分别使用 `Encoding.Default` 和 `Encoding.Unicode`；中文示例优先使用匹配的 WSTRING 符号，STRING 的编码需与 PLC 工程核对。TIME、TOD、LTIME 都返回 `TimeSpan`，写入时应使用各自的方法，避免将 LTIME 或 TOD 按 TIME 写入。DATE/DT 本身不携带业务时区，不应把返回的 `DateTimeOffset` 当成 PLC 已声明了时区。

#### 可复制的 Program.cs

替换新项目的 `Program.cs`。默认只读；只有带 `--write-test` 才会写入 `IN1.aValues`，并尝试恢复原数组。数组应是独立测试变量，避免 PLC 程序同时改写它。

```csharp
using System;
using System.Linq;
using InduLink.Abstractions;
using InduLink.Protocols.Ads;
using InduLink.Runtime;

string targetAmsNetId = Environment.GetEnvironmentVariable("ADS_TARGET_AMS_NET_ID")
    ?? throw new InvalidOperationException("请设置 ADS_TARGET_AMS_NET_ID 为目标 PLC 的 AMS Net ID。");
bool enableWriteTest = args.Contains("--write-test");

using var client = new AdsClient(new AdsClientOptions
{
    DeviceId = "console-plc",
    AmsNetId = targetAmsNetId,
    Port = 851,
    ConnectTimeoutMilliseconds = 10000,
    OperationTimeoutMilliseconds = 5000
});

try
{
    await client.ConnectAsync();
    var state = await client.ReadDeviceStateAsync();
    Console.WriteLine($"PLC 状态：{state.AdsState}");

    Console.WriteLine($"BOOL={await client.ReadBoolAsync("IN1.xStop")}");
    Console.WriteLine($"DINT={await client.ReadInt32Async("IN1.nCount")}");
    Console.WriteLine($"Target={await client.ReadInt32Async("IN1.nTarget")}");
    Console.WriteLine($"REAL={await client.ReadFloatAsync("IN1.rSpeed")}");
    Console.WriteLine($"LREAL={await client.ReadDoubleAsync("IN1.lrTemperature")}");
    Console.WriteLine($"STRING={await client.ReadStringAsync("IN1.sStatus", 80)}");
    Console.WriteLine($"WSTRING={await client.ReadWStringAsync("IN1.wsStatus", 80)}");
    Console.WriteLine($"SINT={await client.ReadSByteAsync("IN1.sintValue")}");
    Console.WriteLine($"USINT={await client.ReadAsync<byte>("IN1.usintValue")}");
    Console.WriteLine($"BYTE={await client.ReadAsync<byte>("IN1.byteValue")}");
    Console.WriteLine($"INT={await client.ReadInt16Async("IN1.intValue")}");
    Console.WriteLine($"UINT={await client.ReadUInt16Async("IN1.uintValue")}");
    Console.WriteLine($"WORD={await client.ReadUInt16Async("IN1.wordValue")}");
    Console.WriteLine($"DINT={await client.ReadInt32Async("IN1.dintValue")}");
    Console.WriteLine($"UDINT={await client.ReadUInt32Async("IN1.udintValue")}");
    Console.WriteLine($"DWORD={await client.ReadUInt32Async("IN1.dwordValue")}");
    Console.WriteLine($"LINT={await client.ReadInt64Async("IN1.lintValue")}");
    Console.WriteLine($"ULINT={await client.ReadUInt64Async("IN1.ulintValue")}");
    Console.WriteLine($"LWORD={await client.ReadUInt64Async("IN1.lwordValue")}");
    Console.WriteLine($"TIME={await client.ReadTimeAsync("IN1.tDelay")}");
    Console.WriteLine($"LTIME={await client.ReadLTimeAsync("IN1.lTimeValue")}");
    Console.WriteLine($"TOD={await client.ReadTimeOfDayAsync("IN1.todValue")}");
    Console.WriteLine($"DATE={await client.ReadDateAsync("IN1.dateValue")}");
    Console.WriteLine($"DT={await client.ReadDateTimeAsync("IN1.dateTimeValue")}");

    int[] original = await client.ReadArrayAsync<int>("IN1.aValues", 5);
    Console.WriteLine($"数组：{string.Join(", ", original)}");
    if (enableWriteTest)
    {
        try
        {
            int[] expected = { 10, 20, 30, 40, 50 };
            await client.WriteArrayAsync("IN1.aValues", expected);
            int[] actual = await client.ReadArrayAsync<int>("IN1.aValues", 5);
            if (!expected.SequenceEqual(actual))
                throw new InvalidOperationException("数组读回不一致，请检查 PLC 是否同时写入该变量。");
            Console.WriteLine("数组写入及读回验证通过。");
        }
        finally
        {
            await client.WriteArrayAsync("IN1.aValues", original);
        }
    }
}
finally
{
    if (client.IsConnected)
        await client.DisconnectAsync();
}
```

数组 `length = 5` 表示元素数量，不是字节数或最后一个索引。`WriteArrayAsync` 的数组类型和长度需匹配 PLC 声明。恢复写入在断线时仍可能失败；`finally` 是恢复尝试，不是事务回滚。

运行前将下面的示例 AMS Net ID 替换为实际目标：

```powershell
$env:ADS_TARGET_AMS_NET_ID = "192.168.1.90.1.1"
dotnet run --project samples/AdsConsole/AdsConsole.csproj -c Release
# 确认数组是可写测试变量后，单独执行：
dotnet run --project samples/AdsConsole/AdsConsole.csproj -c Release -- --write-test
```

其他类型的写入可按下列方式放进业务代码或显式测试开关内。数值后缀/转换用于确定 CLR 类型；字符串和时间类型使用专用入口：

```csharp
await client.WriteAsync("IN1.sintValue", (sbyte)-10);
await client.WriteAsync("IN1.lintValue", 100L);
await client.WriteAsync("IN1.ulintValue", 200UL);
await client.WriteValueAsync("IN1.charValue", DataType.Char, 'B');
await client.WriteWStringAsync("IN1.wsStatus", "运行中", 80);
await client.WriteAsync("IN1.tDelay", TimeSpan.FromMilliseconds(800));
await client.WriteLTimeAsync("IN1.lTimeValue", TimeSpan.FromMilliseconds(1200));
await client.WriteTimeOfDayAsync("IN1.todValue", new TimeSpan(14, 30, 0));
await client.WriteDateAsync("IN1.dateValue", DateTimeOffset.UtcNow);
await client.WriteDateTimeAsync("IN1.dateTimeValue", DateTimeOffset.UtcNow);
```

#### 常见问题与验证范围

| 现象 | 检查顺序 |
| --- | --- |
| 项目引用找不到 | 打开 csproj，确认 ProjectReference 相对于该 csproj 的目录确实存在 |
| 连接或读状态失败 | 核对目标 AMS Net ID、Runtime 端口、Router 是否运行、双向路由及防火墙 |
| 状态可读但符号不存在 | 使用 PLC 实际导出的完整符号路径；`IN1.*` 与 `MAIN.*` 不能混用 |
| 数值异常或类型/长度错误 | 对照 PLC 声明检查整数位宽、REAL/LREAL、字符串容量、数组维度 |
| 数组写入后读回不同 | 检查 PLC 周期逻辑是否覆盖数组，以及是否有其他客户端在写入 |
| 读取失败时程序直接结束 | 顺序 await 遇异常即退出，并执行 finally；强类型读取会检查质量并抛异常，需要逐点容错时自行分组捕获 |

本节示例按当前 SDK 源码核对并进行编译验证；不表示已连接读者的 PLC 或验证其符号声明。仓库现有 ADS 显式集成测试使用 `MAIN.*`，不能直接用来验收这里的 `IN1.*` 点表。参考项目中的本机地址与目录不作为部署默认值。

## MQTT

```csharp
using System;
using System.Threading;
using System.Threading.Tasks;
using InduLink.Protocols.Mqtt;
using InduLink.Runtime;

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
using InduLink.Protocols.Redis;
using InduLink.Runtime;

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
