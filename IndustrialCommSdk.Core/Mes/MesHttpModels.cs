using System.Runtime.Serialization;

namespace IndustrialCommSdk.Mes
{
    // ── 请求模型 ──

    /// <summary>HTTP 设备上线请求。</summary>
    [DataContract]
    public sealed class MesOnlineRequest
    {
        [DataMember(Name = "deviceNo", Order = 1)]
        public string DeviceNo { get; set; }

        [DataMember(Name = "deviceName", Order = 2)]
        public string DeviceName { get; set; }

        [DataMember(Name = "deviceIp", Order = 3)]
        public string DeviceIp { get; set; }

        [DataMember(Name = "deviceMac", Order = 4)]
        public string DeviceMac { get; set; }
    }

    /// <summary>HTTP 工序检查 FACHECK 请求。</summary>
    [DataContract]
    public sealed class MesFaCheckRequest
    {
        [DataMember(Name = "deviceNo", Order = 1)]
        public string DeviceNo { get; set; }

        [DataMember(Name = "deviceName", Order = 2)]
        public string DeviceName { get; set; }

        [DataMember(Name = "deviceIp", Order = 3)]
        public string DeviceIp { get; set; }

        [DataMember(Name = "deviceMac", Order = 4)]
        public string DeviceMac { get; set; }

        [DataMember(Name = "process", Order = 5)]
        public string Process { get; set; }

        [DataMember(Name = "serialNo", Order = 6)]
        public string SerialNo { get; set; }

        [DataMember(Name = "result", Order = 7)]
        public string Result { get; set; }

        [DataMember(Name = "msg", Order = 8)]
        public string Description { get; set; }
    }

    /// <summary>HTTP 生产追踪 FATRACK 请求。</summary>
    [DataContract]
    public sealed class MesFaTrackRequest
    {
        [DataMember(Name = "deviceNo", Order = 1)]
        public string DeviceNo { get; set; }

        [DataMember(Name = "deviceName", Order = 2)]
        public string DeviceName { get; set; }

        [DataMember(Name = "deviceIp", Order = 3)]
        public string DeviceIp { get; set; }

        [DataMember(Name = "deviceMac", Order = 4)]
        public string DeviceMac { get; set; }

        [DataMember(Name = "process", Order = 5)]
        public string Process { get; set; }

        [DataMember(Name = "serialNo", Order = 6)]
        public string SerialNo { get; set; }

        [DataMember(Name = "num", Order = 7)]
        public string Number { get; set; }

        [DataMember(Name = "param", Order = 8)]
        public System.Collections.Generic.Dictionary<string, string> Parameters { get; set; }
    }

    // ── 响应模型 ──

    /// <summary>通用 HTTP 响应基类。</summary>
    [DataContract]
    public class MesApiResponse
    {
        [DataMember(Name = "code", Order = 1)]
        public string Code { get; set; }

        [DataMember(Name = "message", Order = 2)]
        public string Message { get; set; }

        /// <summary>指示服务端是否返回成功（code 为 "0" 或 "ok" 等，忽略大小写）。</summary>
        public bool IsSuccess =>
            string.IsNullOrWhiteSpace(Code) ||
            string.Equals(Code, "0", System.StringComparison.OrdinalIgnoreCase) ||
            string.Equals(Code, "ok", System.StringComparison.OrdinalIgnoreCase) ||
            string.Equals(Code, "success", System.StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>设备上线响应。</summary>
    [DataContract]
    public sealed class MesOnlineResponse : MesApiResponse
    {
    }

    /// <summary>FACHECK 响应。</summary>
    [DataContract]
    public sealed class MesFaCheckResponse : MesApiResponse
    {
        [DataMember(Name = "result", Order = 3)]
        public string Result { get; set; }

        [DataMember(Name = "serialNo", Order = 4)]
        public string SerialNo { get; set; }

        [DataMember(Name = "process", Order = 5)]
        public string Process { get; set; }

        [DataMember(Name = "msg", Order = 6)]
        public string Description { get; set; }
    }

    /// <summary>FATRACK 响应。</summary>
    [DataContract]
    public sealed class MesFaTrackResponse : MesApiResponse
    {
    }
}
