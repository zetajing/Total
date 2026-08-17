# Snap7Server 本地虚拟 PLC

`IndustrialCommDemo.Snap7Server` 是随解决方案构建的 x86 .NET Framework 4.7.2 控制台进程。它使用 Snap7Server 的原生通信服务注册 DB1 内存，让 `IndustrialCommDemo` 或其他 S7 客户端可以通过 TCP 102 读取和写入数据。

它是通信处理器模拟器，不执行 TIA Portal 的梯形图、SCL 或 MC7 程序。它适合验证 S7 地址、字节序、REAL/Bool 解码、批量读取和写入链路。

## 在 S7 Demo 中使用

1. 构建 `IndustrialCommDemo` 或整个 `Total.sln`。
2. 打开 Demo 的“调试与维护 → Siemens S7”。
3. 在“虚拟 PLC（Snap7Server）”区域点击“启动虚拟 PLC”。
4. S7 连接使用主机 `127.0.0.1`、机架 `0`、槽位 `1`。
5. 读取 `DB1.DBX0.0`（Bool）或 `DB1.DBD8`（Float），长度填 `1`。

默认内存值：

| 地址 | 默认值 |
| --- | --- |
| `DB1.DBX0.0` | `TRUE` |
| `DB1.DBD8` | `10.0`（S7 REAL，大端序） |

启动区域可以修改 REAL 和 Bool 的初始值，然后重新启动服务。S7.NetPlus 客户端使用固定的 S7 默认端口 102，因此页面将端口锁定为 102；如果端口被其他程序占用，请先停止占用者。

## 单独启动

生成后可以直接运行：

```powershell
IndustrialCommDemo.Snap7Server\bin\Release\net472\IndustrialCommDemo.Snap7Server.exe `
  --address 0.0.0.0 --port 102 --float 12.5 --bool false
```

进程参数：

- `--address`：监听地址，默认 `0.0.0.0`。
- `--port`：监听端口，默认 `102`。
- `--db-size`：DB1 字节数，默认 `256`。
- `--float`：写入 `DB1.DBD8` 的 REAL 初值，默认 `10.0`。
- `--bool`：写入 `DB1.DBX0.0` 的 Bool 初值，默认 `true`。

原生 `snap7.dll` 来自 `Snap7Server.Net` NuGet 包，并随 x86 服务端输出复制。主 WPF 进程不直接加载这个 x86 DLL，避免 AnyCPU/x64 进程出现“试图加载格式不正确的程序”。
