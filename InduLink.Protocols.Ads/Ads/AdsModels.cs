using System;
using System.Collections.Generic;
using System.Linq;
using TwinCAT.Ads;

namespace InduLink.Protocols.Ads
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

        /// <summary>是否启用官方 ADS SumCommand 批量访问。</summary>
        public bool EnableSumCommands { get; set; } = true;

        /// <summary>单个批量命令包含的最大项目数。</summary>
        public int MaxBatchItems { get; set; } = 500;

        /// <summary>单个批量命令估算的最大数据量。</summary>
        public int MaxBatchPayloadBytes { get; set; } = 61440;

        /// <summary>连接后是否读取一次目标设备状态。</summary>
        public bool ValidateTargetStateOnConnect { get; set; } = true;

        /// <summary>是否使用兼容模式同步投递通知回调。</summary>
        public bool SynchronizeNotifications { get; set; }
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
        public bool EnableSumCommands { get; set; } = true;
        public int MaxBatchItems { get; set; } = 500;
        public int MaxBatchPayloadBytes { get; set; } = 61440;
        public bool ValidateTargetStateOnConnect { get; set; } = true;
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

    /// <summary>ADS 设备状态快照。</summary>
    public sealed class AdsDeviceStateSnapshot
    {
        public AdsDeviceStateSnapshot(string adsState, short deviceState, DateTimeOffset timestamp)
        {
            AdsState = adsState;
            DeviceState = deviceState;
            Timestamp = timestamp;
        }

        public string AdsState { get; private set; }
        public short DeviceState { get; private set; }
        public DateTimeOffset Timestamp { get; private set; }
    }

    /// <summary>ADS 批量写入中的单项错误。</summary>
    public sealed class AdsBatchWriteError
    {
        public AdsBatchWriteError(string address, AdsErrorCode errorCode, string message)
        {
            Address = address;
            ErrorCode = errorCode;
            Message = message;
        }

        public string Address { get; private set; }
        public AdsErrorCode ErrorCode { get; private set; }
        public string Message { get; private set; }

        public override string ToString()
        {
            return Address + ": " + Message;
        }
    }

    /// <summary>ADS SumCommand 批量写入失败异常，包含设备返回的逐项错误。</summary>
    public sealed class AdsBatchWriteException : Exceptions.InduLinkunicationException
    {
        public AdsBatchWriteException(IReadOnlyList<AdsBatchWriteError> errors)
            : base(CreateMessage(errors))
        {
            Errors = errors ?? throw new ArgumentNullException(nameof(errors));
        }

        public IReadOnlyList<AdsBatchWriteError> Errors { get; private set; }

        private static string CreateMessage(IReadOnlyList<AdsBatchWriteError> errors)
        {
            if (errors == null || errors.Count == 0) return "ADS batch write failed.";
            return "ADS batch write failed: " + string.Join("; ", errors.Select(error => error.ToString()));
        }
    }
}
