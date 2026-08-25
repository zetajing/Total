using System;

namespace IndustrialCommSdk.Protocols.Ads
{
    /// <summary>配置 TwinCAT ADS 目标端点。</summary>
    public sealed class AdsSettings : Runtime.Configuration.IProtocolSettings
    {
        /// <summary>目标 AMS Net ID；留空时连接本机路由器。</summary>
        public string AmsNetId { get; set; }

        /// <summary>目标 ADS 端口；TwinCAT PLC Runtime 1 通常为 851。</summary>
        public int Port { get; set; } = 851;

        /// <summary>建立连接时使用的 ADS 超时。</summary>
        public int ConnectTimeoutMilliseconds { get; set; } = 10000;
    }

    /// <summary>直接构造 <see cref="AdsClient"/> 时使用的选项。</summary>
    public sealed class AdsClientOptions
    {
        public string DeviceId { get; set; }
        public string AmsNetId { get; set; }
        public int Port { get; set; } = 851;
        public int ConnectTimeoutMilliseconds { get; set; } = 10000;
        public int OperationTimeoutMilliseconds { get; set; } = 5000;
        public bool SynchronizeNotifications { get; set; }
    }

    /// <summary>ADS 变量地址。ADS 地址就是 PLC 符号名，例如 MAIN.bool1。</summary>
    public sealed class AdsAddress : Abstractions.IIndustrialAddress
    {
        internal AdsAddress(string address)
        {
            if (string.IsNullOrWhiteSpace(address)) throw new ArgumentException("ADS variable name is required.", nameof(address));
            Original = address;
            Normalized = address.Trim();
        }

        public string Original { get; private set; }
        public string Normalized { get; private set; }
        public string Area { get { return "ADS"; } }
        public int Offset { get { return 0; } }
        public int? Bit { get { return null; } }
        public bool IsBitAddress { get { return false; } }

        public override string ToString() { return Normalized; }
    }

    /// <summary>将 ADS 符号地址规范化并校验为空白输入。</summary>
    public sealed class AdsAddressParser : Abstractions.IAddressParser, Abstractions.IAddressParser<AdsAddress>
    {
        public object Parse(string address) { return ParseTyped(address); }

        AdsAddress Abstractions.IAddressParser<AdsAddress>.Parse(string address) { return ParseTyped(address); }

        public AdsAddress ParseTyped(string address)
        {
            if (string.IsNullOrWhiteSpace(address))
                throw new Exceptions.IndustrialAddressParseException("ADS variable name cannot be empty.");
            return new AdsAddress(address);
        }
    }

    /// <summary>ADS 任意类型通知事件。</summary>
    public sealed class AdsValueNotificationEventArgs : EventArgs
    {
        public AdsValueNotificationEventArgs(string subscriptionId, string variableName, object value, DateTimeOffset timestamp)
        {
            SubscriptionId = subscriptionId;
            VariableName = variableName;
            Value = value;
            Timestamp = timestamp;
        }

        public string SubscriptionId { get; private set; }
        public string VariableName { get; private set; }
        public object Value { get; private set; }
        public DateTimeOffset Timestamp { get; private set; }
    }
}
