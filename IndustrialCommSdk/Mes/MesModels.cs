using System;
using System.Collections.Generic;
using System.Runtime.Serialization;

namespace IndustrialCommSdk.Mes
{
    /// <summary>MES 客户端连接生命周期状态。</summary>
    public enum MesConnectionState
    {
        Disconnected,
        Connecting,
        Connected,
        Reconnecting,
        Stopped,
    }

    /// <summary>MES TCP 客户端连接、设备身份和重连选项。</summary>
    public sealed class MesClientOptions
    {
        /// <summary>MES 服务地址。</summary>
        public string Host { get; set; }
        /// <summary>MES 服务端口。</summary>
        public int Port { get; set; } = 9312;
        /// <summary>设备编号。</summary>
        public string DeviceNo { get; set; }
        /// <summary>设备名称。</summary>
        public string DeviceName { get; set; }
        /// <summary>设备 IP 地址。</summary>
        public string DeviceIp { get; set; }
        /// <summary>设备 MAC 地址。</summary>
        public string DeviceMac { get; set; }
        /// <summary>连接超时，单位为毫秒。</summary>
        public int ConnectTimeoutMilliseconds { get; set; } = 3000;
        /// <summary>发送超时，单位为毫秒。</summary>
        public int SendTimeoutMilliseconds { get; set; } = 3000;
        /// <summary>首次重连等待时间，单位为毫秒。</summary>
        public int InitialReconnectDelayMilliseconds { get; set; } = 500;
        /// <summary>重连退避的最大等待时间，单位为毫秒。</summary>
        public int MaximumReconnectDelayMilliseconds { get; set; } = 10000;
        /// <summary>单条 JSON 消息允许的最大字符数。</summary>
        public int MaximumMessageCharacters { get; set; } = 1024 * 1024;
        /// <summary>连接意外中断后是否自动重连。</summary>
        public bool AutoReconnect { get; set; } = true;
    }

    /// <summary>MES JSON 消息共有的设备身份和时间字段。</summary>
    [DataContract]
    public abstract class MesMessage
    {
        [DataMember(Name = "type", Order = 1)]
        public string Type { get; set; }
        [DataMember(Name = "deviceNo", Order = 2)]
        public string DeviceNo { get; set; }
        [DataMember(Name = "deviceName", Order = 3)]
        public string DeviceName { get; set; }
        [DataMember(Name = "deviceIp", Order = 4)]
        public string DeviceIp { get; set; }
        [DataMember(Name = "deviceMac", Order = 5)]
        public string DeviceMac { get; set; }
        [DataMember(Name = "deviceTime", Order = 6)]
        public string DeviceTime { get; set; }
    }

    /// <summary>MES 工序检查 FACHECK 消息。</summary>
    [DataContract]
    public sealed class FaCheckMessage : MesMessage
    {
        public FaCheckMessage() { Type = "FACHECK"; }
        [DataMember(Name = "message", Order = 7)]
        public FaCheckBody Message { get; set; }
    }

    /// <summary>FACHECK 消息正文。</summary>
    [DataContract]
    public sealed class FaCheckBody
    {
        [DataMember(Name = "process", Order = 1)] public string Process { get; set; }
        [DataMember(Name = "serialNo", Order = 2)] public string SerialNo { get; set; }
        [DataMember(Name = "result", Order = 3)] public string Result { get; set; }
        [DataMember(Name = "msg", Order = 4)] public string Description { get; set; }
    }

    /// <summary>MES 生产追踪 FATRACK 消息。</summary>
    [DataContract]
    public sealed class FaTrackMessage : MesMessage
    {
        public FaTrackMessage() { Type = "FATRACK"; }
        [DataMember(Name = "message", Order = 7)]
        public FaTrackBody Message { get; set; }
    }

    /// <summary>FATRACK 消息正文。</summary>
    [DataContract]
    public sealed class FaTrackBody
    {
        [DataMember(Name = "process", Order = 1)] public string Process { get; set; }
        [DataMember(Name = "serialNo", Order = 2)] public string SerialNo { get; set; }
        [DataMember(Name = "num", Order = 3)] public string Number { get; set; }
        [DataMember(Name = "param", Order = 4)] public Dictionary<string, string> Parameters { get; set; } = new Dictionary<string, string>();
    }

    /// <summary>MES 数量反馈 FANUM 消息。</summary>
    [DataContract]
    public sealed class FaNumMessage : MesMessage
    {
        public FaNumMessage() { Type = "FANUM"; }
        [DataMember(Name = "message", Order = 7)]
        public FaNumBody Message { get; set; }
    }

    /// <summary>FANUM 消息正文。</summary>
    [DataContract]
    public sealed class FaNumBody
    {
        [DataMember(Name = "result", Order = 1)] public string Result { get; set; }
    }

    /// <summary>提供 MES 连接状态变化数据。</summary>
    public sealed class MesConnectionStateChangedEventArgs : EventArgs
    {
        public MesConnectionStateChangedEventArgs(MesConnectionState state, string errorMessage)
        { State = state; ErrorMessage = errorMessage; }
        public MesConnectionState State { get; private set; }
        public string ErrorMessage { get; private set; }
    }

    /// <summary>提供已解析 MES 消息及原始报文。</summary>
    public sealed class MesMessageEventArgs<T> : EventArgs
    {
        public MesMessageEventArgs(T message, string rawMessage)
        { Message = message; RawMessage = rawMessage; }
        public T Message { get; private set; }
        public string RawMessage { get; private set; }
    }

    /// <summary>提供 MES 原始收发报文。</summary>
    public sealed class MesRawMessageEventArgs : EventArgs
    {
        public MesRawMessageEventArgs(string message, bool sent)
        { Message = message; Sent = sent; }
        public string Message { get; private set; }
        public bool Sent { get; private set; }
    }

    /// <summary>提供 MES 协议解析错误详情。</summary>
    public sealed class MesProtocolErrorEventArgs : EventArgs
    {
        public MesProtocolErrorEventArgs(string rawMessage, string errorMessage, Exception exception)
        { RawMessage = rawMessage; ErrorMessage = errorMessage; Exception = exception; }
        public string RawMessage { get; private set; }
        public string ErrorMessage { get; private set; }
        public Exception Exception { get; private set; }
    }
}
