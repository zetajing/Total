using System;
using System.Collections.Generic;
using System.Runtime.Serialization;

namespace IndustrialCommSdk.Mes
{
    public enum MesConnectionState
    {
        Disconnected,
        Connecting,
        Connected,
        Reconnecting,
        Stopped,
    }

    public sealed class MesClientOptions
    {
        public string Host { get; set; }
        public int Port { get; set; } = 9312;
        public string DeviceNo { get; set; }
        public string DeviceName { get; set; }
        public string DeviceIp { get; set; }
        public string DeviceMac { get; set; }
        public int ConnectTimeoutMilliseconds { get; set; } = 3000;
        public int SendTimeoutMilliseconds { get; set; } = 3000;
        public int InitialReconnectDelayMilliseconds { get; set; } = 500;
        public int MaximumReconnectDelayMilliseconds { get; set; } = 10000;
        public int MaximumMessageCharacters { get; set; } = 1024 * 1024;
        public bool AutoReconnect { get; set; } = true;
    }

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

    [DataContract]
    public sealed class FaCheckMessage : MesMessage
    {
        public FaCheckMessage() { Type = "FACHECK"; }
        [DataMember(Name = "message", Order = 7)]
        public FaCheckBody Message { get; set; }
    }

    [DataContract]
    public sealed class FaCheckBody
    {
        [DataMember(Name = "process", Order = 1)] public string Process { get; set; }
        [DataMember(Name = "serialNo", Order = 2)] public string SerialNo { get; set; }
        [DataMember(Name = "result", Order = 3)] public string Result { get; set; }
        [DataMember(Name = "msg", Order = 4)] public string Description { get; set; }
    }

    [DataContract]
    public sealed class FaTrackMessage : MesMessage
    {
        public FaTrackMessage() { Type = "FATRACK"; }
        [DataMember(Name = "message", Order = 7)]
        public FaTrackBody Message { get; set; }
    }

    [DataContract]
    public sealed class FaTrackBody
    {
        [DataMember(Name = "process", Order = 1)] public string Process { get; set; }
        [DataMember(Name = "serialNo", Order = 2)] public string SerialNo { get; set; }
        [DataMember(Name = "num", Order = 3)] public string Number { get; set; }
        [DataMember(Name = "param", Order = 4)] public Dictionary<string, string> Parameters { get; set; } = new Dictionary<string, string>();
    }

    [DataContract]
    public sealed class FaNumMessage : MesMessage
    {
        public FaNumMessage() { Type = "FANUM"; }
        [DataMember(Name = "message", Order = 7)]
        public FaNumBody Message { get; set; }
    }

    [DataContract]
    public sealed class FaNumBody
    {
        [DataMember(Name = "result", Order = 1)] public string Result { get; set; }
    }

    public sealed class MesConnectionStateChangedEventArgs : EventArgs
    {
        public MesConnectionStateChangedEventArgs(MesConnectionState state, string errorMessage)
        { State = state; ErrorMessage = errorMessage; }
        public MesConnectionState State { get; private set; }
        public string ErrorMessage { get; private set; }
    }

    public sealed class MesMessageEventArgs<T> : EventArgs
    {
        public MesMessageEventArgs(T message, string rawMessage)
        { Message = message; RawMessage = rawMessage; }
        public T Message { get; private set; }
        public string RawMessage { get; private set; }
    }

    public sealed class MesRawMessageEventArgs : EventArgs
    {
        public MesRawMessageEventArgs(string message, bool sent)
        { Message = message; Sent = sent; }
        public string Message { get; private set; }
        public bool Sent { get; private set; }
    }

    public sealed class MesProtocolErrorEventArgs : EventArgs
    {
        public MesProtocolErrorEventArgs(string rawMessage, string errorMessage, Exception exception)
        { RawMessage = rawMessage; ErrorMessage = errorMessage; Exception = exception; }
        public string RawMessage { get; private set; }
        public string ErrorMessage { get; private set; }
        public Exception Exception { get; private set; }
    }
}
