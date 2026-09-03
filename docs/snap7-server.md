# Snap7Server 本地虚拟 PLC

`InduLinkDemo.Snap7Server` 是随解决方案构建的 x86 .NET 8 控制台进程。它使用 Snap7Server 的原生通信服务注册 DB1 内存，让 `InduLinkDemo` 或其他 S7 客户端可以通过 TCP 102 读取和写入数据。

它是通信处理器模拟器，不执行 TIA Portal 的梯形图、SCL 或 MC7 程序。它适合验证 S7 地址、字节序、REAL/Bool 解码、批量读取和写入链路。

## 在 S7 Demo 中使用

1. 构建 `InduLinkDemo` 或整个 `Total.sln`。
2. 打开 Demo 的“调试与维护 → Siemens S7”。
3. 在“虚拟 PLC（Snap7Server）”区域点击“启动虚拟 PLC”。
4. S7 连接使用主机 `127.0.0.1`、机架 `0`、槽位 `1`。
5. 读取 `DB1.DBX0.0`（Bool）或 `DB1.DBD8`（Float），长度填 `1`。

默认内存值：

| 地址 | 默认值 |
| --- | --- |
| `DB1.DBX0.0` | `TRUE` |
| `DB1.DBW2` | `1500`（额外 INT） |
| `DB1.DBD4` | `123456`（额外 DINT） |
| `DB1.DBD8` | `10.0`（S7 REAL，大端序） |
| `DB1.DBD12` | `20.0`（额外 REAL） |
| `DB1.DBD16` | `30.0`（额外 REAL） |
| `DB1.DBX0.1` | `FALSE`（额外 Bool） |
| `DB1.DBX0.2` | `TRUE`（额外 Bool） |

启动区域可以修改快捷 REAL、Bool 和“额外点位”列表，然后重新启动服务。额外点位每行一个，支持以下格式：

```text
REAL DB1.DBD12=20.0
BOOL DB1.DBX0.1=false
INT DB1.DBW2=1500
DINT DB1.DBD4=123456
DB1.DBD16=30.0
```

类型可以写在地址前面或后面，也可以根据 `DBW`/`DBD`/`DBX` 地址省略；当前额外点位支持 `BOOL`、`INT`、`DINT` 和 `REAL`，仅注册 DB1。S7.NetPlus 客户端使用固定的 S7 默认端口 102，因此页面将端口锁定为 102；如果端口被其他程序占用，请先停止占用者。

## 单独启动

生成后可以直接运行：

```powershell
InduLinkDemo.Snap7Server\bin\Release\net8.0\InduLinkDemo.Snap7Server.exe `
  --address 0.0.0.0 --port 102 --float 12.5 --bool false `
  --point "REAL DB1.DBD12=20.0" `
  --point "BOOL DB1.DBX0.1=true"
```

进程参数：

- `--address`：监听地址，默认 `0.0.0.0`。
- `--port`：监听端口，默认 `102`。
- `--db-size`：DB1 字节数，默认 `256`。
- `--float`：写入 `DB1.DBD8` 的 REAL 初值，默认 `10.0`。
- `--bool`：写入 `DB1.DBX0.0` 的 Bool 初值，默认 `true`。
- `--point`：追加一个 `BOOL`、`INT`、`DINT` 或 `REAL` 点位，参数可重复；例如 `--point "REAL DB1.DBD12=20.0"`。

原生 `snap7.dll` 来自 `Snap7Server.Net` NuGet 包，并随 x86 服务端输出复制。主 WPF 进程不直接加载这个 x86 DLL，避免 AnyCPU/x64 进程出现“试图加载格式不正确的程序”。

发布 `InduLinkDemo` 时，使用 `win-x64` 自包含发布会将 x86 Snap7Server 及其运行时放到发布目录的 `snap7server` 子目录；目标电脑不需要安装 .NET Runtime。主程序和 Snap7Server 的运行库分目录保存，避免 x64 与 x86 文件冲突。
