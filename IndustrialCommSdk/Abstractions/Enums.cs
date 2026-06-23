namespace IndustrialCommSdk.Abstractions
{
    public enum ProtocolKind
    {
        ModbusTcp = 1,
        TcpSocket = 2,
        SiemensS7 = 3,
        MitsubishiMc = 4,
    }

    public enum DataType
    {
        Bool = 1,
        Int16 = 2,
        UInt16 = 3,
        Int32 = 4,
        UInt32 = 5,
        Float = 6,
        Double = 7,
        String = 8,
        ByteArray = 9,
    }

    public enum QualityStatus
    {
        Unknown = 0,
        Good = 1,
        Bad = 2,
        Stale = 3,
    }

    public enum ConnectionStatus
    {
        Disconnected = 0,
        Connecting = 1,
        Connected = 2,
        Faulted = 3,
    }
}
