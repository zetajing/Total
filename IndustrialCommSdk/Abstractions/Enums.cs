namespace IndustrialCommSdk.Abstractions
{
    /// <summary>SDK 支持的工业通信协议类型。</summary>
    public enum ProtocolKind
    {
        /// <summary>Modbus TCP 协议。</summary>
        ModbusTcp = 1,
        /// <summary>TCP Socket 桥接协议。</summary>
        TcpSocket = 2,
        /// <summary>西门子 S7 协议。</summary>
        SiemensS7 = 3,
        /// <summary>三菱 MC 3E 协议。</summary>
        MitsubishiMc = 4,
    }

    /// <summary>读写请求中使用的数据类型。</summary>
    public enum DataType
    {
        /// <summary>布尔值（位）。</summary>
        Bool = 1,
        /// <summary>16 位有符号整数。</summary>
        Int16 = 2,
        /// <summary>16 位无符号整数。</summary>
        UInt16 = 3,
        /// <summary>32 位有符号整数。</summary>
        Int32 = 4,
        /// <summary>32 位无符号整数。</summary>
        UInt32 = 5,
        /// <summary>单精度浮点数。</summary>
        Float = 6,
        /// <summary>双精度浮点数。</summary>
        Double = 7,
        /// <summary>8 位无符号字节。</summary>
        Byte = 10,
        /// <summary>单个 ASCII 字符。</summary>
        Char = 11,
        /// <summary>ASCII 字符串。</summary>
        String = 8,
        /// <summary>原始字节数组。</summary>
        ByteArray = 9,
    }

    /// <summary>读取值的质量状态，用于区分有效值、错误值和过期值。</summary>
    public enum QualityStatus
    {
        /// <summary>未知质量。</summary>
        Unknown = 0,
        /// <summary>数据质量良好。</summary>
        Good = 1,
        /// <summary>读取失败，数据不可用。</summary>
        Bad = 2,
        /// <summary>数据已过期。</summary>
        Stale = 3,
    }

    /// <summary>工业客户端当前的连接状态。</summary>
    public enum ConnectionStatus
    {
        /// <summary>未连接。</summary>
        Disconnected = 0,
        /// <summary>正在连接中。</summary>
        Connecting = 1,
        /// <summary>已连接。</summary>
        Connected = 2,
        /// <summary>连接故障。</summary>
        Faulted = 3,
    }
}
