using System;
using InduLink.Abstractions;

namespace InduLinkDemo.Views
{
    /// <summary>虚拟服务发出的客户端预填参数；只传递配置，不执行连接或写入。</summary>
    internal sealed class SimulatorClientRequest : EventArgs
    {
        public ProtocolKind Protocol { get; init; }
        public string Host { get; init; }
        public int Port { get; init; }
        public byte SlaveId { get; init; }
        public string Endpoint { get; init; }
        public bool UseSecurity { get; init; }
        public string Address { get; init; }
        public DataType DataType { get; init; }
        public ushort Length { get; init; }
        public string ValueText { get; init; }
    }
}
