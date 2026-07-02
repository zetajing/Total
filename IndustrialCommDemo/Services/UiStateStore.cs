using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.Serialization;
using System.Runtime.Serialization.Json;
using System.Text;
using LogHelper;

namespace IndustrialCommDemo.Services
{
    /// <summary>
    /// UI 状态存储类，负责将 <see cref="DemoUiState"/> 序列化为 JSON 文件并持久化到本地磁盘，
    /// 以及从磁盘加载反序列化为对象。使用 <see cref="DataContractJsonSerializer"/> 进行序列化。
    /// 数据文件存储在 StoragePathProvider.StateRoot 目录下的 "ui-state.json"。
    /// </summary>
    internal sealed class UiStateStore
    {
        /// <summary>
        /// <see cref="DataContractJsonSerializer"/> 静态实例，用于序列化和反序列化 <see cref="DemoUiState"/>。
        /// </summary>
        private static readonly DataContractJsonSerializer Serializer = new DataContractJsonSerializer(typeof(DemoUiState));

        /// <summary>
        /// 持久化文件的完整路径。
        /// </summary>
        private readonly string _filePath;

        /// <summary>
        /// 初始化 <see cref="UiStateStore"/> 类的新实例。
        /// 通过 StoragePathProvider.StateRoot 获取状态目录并设置状态文件的完整路径。
        /// </summary>
        public UiStateStore()
        {
            var baseDirectory = StoragePathProvider.StateRoot;
            Directory.CreateDirectory(baseDirectory);
            _filePath = Path.Combine(baseDirectory, "ui-state.json");
        }

        /// <summary>
        /// 从磁盘加载 UI 状态。如果文件不存在或反序列化失败，则返回一个经过标准化处理的空状态实例。
        /// </summary>
        /// <returns>加载并标准化后的 <see cref="DemoUiState"/> 实例；加载失败时返回默认状态。</returns>
        public DemoUiState Load()
        {
            try
            {
                if (!File.Exists(_filePath))
                {
                    return Normalize(new DemoUiState());
                }

                using (var stream = File.OpenRead(_filePath))
                {
                    return Normalize((DemoUiState)Serializer.ReadObject(stream) ?? new DemoUiState());
                }
            }
            catch
            {
                // 任何加载异常（如文件损坏）均返回默认状态
                return Normalize(new DemoUiState());
            }
        }

        /// <summary>
        /// 将指定的 UI 状态保存到磁盘。保存前会对状态进行标准化处理。
        /// 如果 <paramref name="state"/> 为 null，则直接返回不执行任何操作。
        /// </summary>
        /// <param name="state">要保存的 <see cref="DemoUiState"/> 实例。不能为 null，否则忽略保存操作。</param>
        public void Save(DemoUiState state)
        {
            if (state == null)
            {
                return;
            }

            state = Normalize(state);
            // 数据目录可能在程序运行期间被人工删除。构造函数创建过目录并不代表
            // 保存时目录仍然存在，因此每次落盘前都要重新确保 State 目录可用。
            Directory.CreateDirectory(Path.GetDirectoryName(_filePath));
            using (var stream = File.Create(_filePath))
            {
                Serializer.WriteObject(stream, state);
            }
        }

        /// <summary>
        /// 对 <see cref="DemoUiState"/> 实例进行标准化处理，确保其所有子属性（Modbus、Socket、S7、Mc）
        /// 及子属性中的 RecentAddresses 集合均不为 null。防止因反序列化部分数据导致 NullReferenceException。
        /// </summary>
        /// <param name="state">要标准化处理的 <see cref="DemoUiState"/> 实例。</param>
        /// <returns>标准化处理后的同一 <see cref="DemoUiState"/> 实例。</returns>
        private static DemoUiState Normalize(DemoUiState state)
        {
            state.Modbus = state.Modbus ?? new ModbusUiState();
            state.Socket = state.Socket ?? new SocketUiState();
            state.S7 = state.S7 ?? new ProtocolUiState();
            state.Mc = state.Mc ?? new ProtocolUiState();
            // 旧版本 ui-state.json 中没有 Database 字段。
            // 这里补默认对象，使升级后的 Demo 可以直接读取旧配置而不会出现空引用。
            state.Database = state.Database ?? new DatabaseUiState();
            state.Mes = state.Mes ?? new MesUiState();

            state.Modbus.RecentAddresses = state.Modbus.RecentAddresses ?? new List<string>();
            state.S7.RecentAddresses = state.S7.RecentAddresses ?? new List<string>();
            state.Mc.RecentAddresses = state.Mc.RecentAddresses ?? new List<string>();
            return state;
        }
    }

    /// <summary>
    /// 演示应用程序的完整 UI 状态数据契约，包含 Modbus、Socket（调试）、S7 和 MC 协议的 UI 状态子对象。
    /// 使用 <see cref="DataContractAttribute"/> 标记，支持 JSON 序列化。
    /// </summary>
    [DataContract]
    internal sealed class DemoUiState
    {
        /// <summary>
        /// 获取或设置 Modbus 协议的 UI 状态。
        /// </summary>
        [DataMember(Order = 1)]
        public ModbusUiState Modbus { get; set; } = new ModbusUiState();

        /// <summary>
        /// 获取或设置 Socket 调试的 UI 状态。
        /// </summary>
        [DataMember(Order = 2)]
        public SocketUiState Socket { get; set; } = new SocketUiState();

        /// <summary>
        /// 获取或设置 S7（西门子）协议的 UI 状态。
        /// </summary>
        [DataMember(Order = 3)]
        public ProtocolUiState S7 { get; set; } = new ProtocolUiState();

        /// <summary>
        /// 获取或设置 MC（三菱）协议的 UI 状态。
        /// </summary>
        [DataMember(Order = 4)]
        public ProtocolUiState Mc { get; set; } = new ProtocolUiState();

        /// <summary>获取或设置可选的 SQL Server 历史存储配置。</summary>
        [DataMember(Order = 5)]
        public DatabaseUiState Database { get; set; } = new DatabaseUiState();

        [DataMember(Order = 6)]
        public MesUiState Mes { get; set; } = new MesUiState();
    }

    /// <summary>MES 联调页保存的非敏感连接和报工输入。</summary>
    [DataContract]
    internal sealed class MesUiState
    {
        [DataMember(Order = 1)] public string Host { get; set; }
        [DataMember(Order = 2)] public string Port { get; set; }
        [DataMember(Order = 3)] public string DeviceNo { get; set; }
        [DataMember(Order = 4)] public string DeviceName { get; set; }
        [DataMember(Order = 5)] public string DeviceIp { get; set; }
        [DataMember(Order = 6)] public string DeviceMac { get; set; }
        [DataMember(Order = 7)] public string Process { get; set; }
        [DataMember(Order = 8)] public string SerialNo { get; set; }
        [DataMember(Order = 9)] public string Number { get; set; }
        [DataMember(Order = 10)] public string Parameters { get; set; }
    }

    /// <summary>
    /// SQL Server 历史存储页面的非敏感配置。
    /// <para>
    /// 这里只保存连接字符串和表名，不保存“当前已连接”这种运行时状态。
    /// Demo 默认使用 Windows 身份验证；如果改成 SQL 用户名/密码，密码也会成为连接字符串的一部分，
    /// 因此生产环境应改用受保护的配置系统，而不是直接写入本地 JSON。
    /// </para>
    /// </summary>
    [DataContract]
    internal sealed class DatabaseUiState
    {
        /// <summary>
        /// 获取或设置连接字符串。Demo 默认使用 Windows 身份验证；不要在此处保存 SQL 登录密码。
        /// </summary>
        [DataMember(Order = 1)]
        public string ConnectionString { get; set; }

        /// <summary>获取或设置历史表名。</summary>
        [DataMember(Order = 2)]
        public string TableName { get; set; }
        [DataMember(Order = 3)] public string QueryDeviceId { get; set; }
        [DataMember(Order = 4)] public string QueryAddress { get; set; }
        [DataMember(Order = 5)] public bool AddressContains { get; set; }
        [DataMember(Order = 6)] public string FromTime { get; set; }
        [DataMember(Order = 7)] public string ToTime { get; set; }
        [DataMember(Order = 8)] public int PageSize { get; set; } = 100;
        [DataMember(Order = 9)] public int RetentionDays { get; set; } = 30;
        [DataMember(Order = 10)] public string Protocol { get; set; }
        [DataMember(Order = 11)] public string DataType { get; set; }
        [DataMember(Order = 12)] public string Quality { get; set; }
    }

    /// <summary>
    /// Modbus 协议的 UI 状态数据契约，包含设备 ID、主机、端口、从站 ID、地址、长度、写值、
    /// 轮询间隔及最近使用地址列表等属性。
    /// </summary>
    [DataContract]
    internal sealed class ModbusUiState
    {
        /// <summary>
        /// 获取或设置设备标识。
        /// </summary>
        [DataMember(Order = 1)]
        public string DeviceId { get; set; }

        /// <summary>
        /// 获取或设置 Modbus TCP 主机地址。
        /// </summary>
        [DataMember(Order = 2)]
        public string Host { get; set; }

        /// <summary>
        /// 获取或设置 Modbus TCP 端口号。
        /// </summary>
        [DataMember(Order = 3)]
        public string Port { get; set; }

        /// <summary>
        /// 获取或设置从站 ID（Slave ID）。
        /// </summary>
        [DataMember(Order = 4)]
        public string SlaveId { get; set; }

        /// <summary>
        /// 获取或设置要读取的寄存器起始地址。
        /// </summary>
        [DataMember(Order = 5)]
        public string Address { get; set; }

        /// <summary>
        /// 获取或设置要读取的寄存器长度（数量）。
        /// </summary>
        [DataMember(Order = 6)]
        public string Length { get; set; }

        /// <summary>
        /// 获取或设置要写入的寄存器值。
        /// </summary>
        [DataMember(Order = 7)]
        public string WriteValue { get; set; }

        /// <summary>
        /// 获取或设置轮询间隔时间（毫秒）。
        /// </summary>
        [DataMember(Order = 8)]
        public string PollInterval { get; set; }

        /// <summary>
        /// 获取或设置当前选中的 Modbus 设备模型键。
        /// </summary>
        [DataMember(Order = 9)]
        public string ModelKey { get; set; }

        /// <summary>
        /// 获取或设置最近使用的地址列表。
        /// </summary>
        [DataMember(Order = 10)]
        public List<string> RecentAddresses { get; set; } = new List<string>();

        /// <summary>
        /// 获取或设置连接类型（"Tcp" 或 "Rtu"）。
        /// </summary>
        [DataMember(Order = 11)]
        public string ConnectionType { get; set; }

        /// <summary>
        /// 获取或设置 RTU 串口名称（如 COM1）。
        /// </summary>
        [DataMember(Order = 12)]
        public string PortName { get; set; }

        /// <summary>
        /// 获取或设置 RTU 波特率。
        /// </summary>
        [DataMember(Order = 13)]
        public string BaudRate { get; set; }

        /// <summary>
        /// 获取或设置 RTU 数据位。
        /// </summary>
        [DataMember(Order = 14)]
        public string DataBits { get; set; }

        /// <summary>
        /// 获取或设置 RTU 校验位名称（None/Odd/Even）。
        /// </summary>
        [DataMember(Order = 15)]
        public string Parity { get; set; }

        /// <summary>
        /// 获取或设置 RTU 停止位名称（One/OnePointFive/Two）。
        /// </summary>
        [DataMember(Order = 16)]
        public string StopBits { get; set; }
    }

    /// <summary>
    /// Socket 调试工具的 UI 状态数据契约，包含服务端和客户端的 IP、端口配置，
    /// 回声模式开关以及服务端/客户端消息内容。
    /// </summary>
    [DataContract]
    internal sealed class SocketUiState
    {
        /// <summary>
        /// 获取或设置 Socket 服务端的监听 IP 地址。
        /// </summary>
        [DataMember(Order = 1)]
        public string ServerIp { get; set; }

        /// <summary>
        /// 获取或设置 Socket 服务端的监听端口号。
        /// </summary>
        [DataMember(Order = 2)]
        public string ServerPort { get; set; }

        /// <summary>
        /// 获取或设置 Socket 客户端的目标主机地址。
        /// </summary>
        [DataMember(Order = 3)]
        public string ClientHost { get; set; }

        /// <summary>
        /// 获取或设置 Socket 客户端的目标端口号。
        /// </summary>
        [DataMember(Order = 4)]
        public string ClientPort { get; set; }

        /// <summary>
        /// 获取或设置是否启用了回声（Echo）模式。默认值为 true。
        /// </summary>
        [DataMember(Order = 5)]
        public bool EchoEnabled { get; set; } = true;

        /// <summary>
        /// 获取或设置服务端发送的消息内容。
        /// </summary>
        [DataMember(Order = 6)]
        public string ServerMessage { get; set; }

        /// <summary>
        /// 获取或设置客户端发送的消息内容。
        /// </summary>
        [DataMember(Order = 7)]
        public string ClientMessage { get; set; }
    }

    /// <summary>
    /// 通用协议（S7 / MC）的 UI 状态数据契约，包含设备 ID、主机、端口或机架号、
    /// 插槽或长度、地址、长度、写值及最近使用地址列表等属性。
    /// </summary>
    [DataContract]
    internal sealed class ProtocolUiState
    {
        /// <summary>
        /// 获取或设置设备标识。
        /// </summary>
        [DataMember(Order = 1)]
        public string DeviceId { get; set; }

        /// <summary>
        /// 获取或设置协议目标的主机地址。
        /// </summary>
        [DataMember(Order = 2)]
        public string Host { get; set; }

        /// <summary>
        /// 获取或设置端口号（对于 S7 协议也可能用作机架号 Rack）。
        /// </summary>
        [DataMember(Order = 3)]
        public string PortOrRack { get; set; }

        /// <summary>
        /// 获取或设置插槽号（对于 S7 协议）或长度（根据具体上下文）。
        /// </summary>
        [DataMember(Order = 4)]
        public string SlotOrLength { get; set; }

        /// <summary>
        /// 获取或设置要读取的寄存器/数据起始地址。
        /// </summary>
        [DataMember(Order = 5)]
        public string Address { get; set; }

        /// <summary>
        /// 获取或设置要读取的数据长度（数量）。
        /// </summary>
        [DataMember(Order = 6)]
        public string Length { get; set; }

        /// <summary>
        /// 获取或设置要写入的数据值。
        /// </summary>
        [DataMember(Order = 7)]
        public string WriteValue { get; set; }

        /// <summary>
        /// 获取或设置最近使用的地址列表。
        /// </summary>
        [DataMember(Order = 8)]
        public List<string> RecentAddresses { get; set; } = new List<string>();
    }
}
