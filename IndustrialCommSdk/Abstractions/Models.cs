using System;
using System.Collections.Generic;

namespace IndustrialCommSdk.Abstractions
{
    /// <summary>描述一次设备读取操作。</summary>
    public sealed class ReadRequest
    {
        /// <summary>初始化读取请求。</summary>
        /// <param name="deviceId">目标设备标识。</param>
        /// <param name="address">协议地址，例如 Modbus 的保持寄存器地址。</param>
        /// <param name="dataType">期望解码成的数据类型。</param>
        /// <param name="length">字符串或字节数组等变长数据的读取长度。</param>
        /// <param name="timeout">本次操作的可选超时时间；为空时使用客户端默认值。</param>
        public ReadRequest(string deviceId, string address, DataType dataType, ushort length = 1, TimeSpan? timeout = null)
        {
            if (string.IsNullOrWhiteSpace(deviceId)) throw new ArgumentNullException(nameof(deviceId));
            if (string.IsNullOrWhiteSpace(address)) throw new ArgumentNullException(nameof(address));

            DeviceId = deviceId;
            Address = address;
            DataType = dataType;
            Length = length;
            Timeout = timeout;
        }

        /// <summary>目标设备标识。</summary>
        public string DeviceId { get; private set; }
        /// <summary>协议地址，例如 Modbus 的保持寄存器地址。</summary>
        public string Address { get; private set; }
        /// <summary>期望解码成的数据类型。</summary>
        public DataType DataType { get; private set; }
        /// <summary>字符串或字节数组等变长数据的读取长度。</summary>
        public ushort Length { get; private set; }
        /// <summary>本次操作的可选超时时间；为空时使用客户端默认值。</summary>
        public TimeSpan? Timeout { get; private set; }
    }

    /// <summary>描述一次设备写入操作。</summary>
    public sealed class WriteRequest
    {
        /// <summary>初始化写入请求。</summary>
        /// <param name="deviceId">目标设备标识。</param>
        /// <param name="address">目标协议地址。</param>
        /// <param name="dataType">写入值的数据类型。</param>
        /// <param name="value">待写入的值。</param>
        /// <param name="length">字符串或字节数组等变长数据的写入长度。</param>
        /// <param name="timeout">本次操作的可选超时时间；为空时使用客户端默认值。</param>
        public WriteRequest(string deviceId, string address, DataType dataType, object value, ushort length = 1, TimeSpan? timeout = null)
        {
            if (string.IsNullOrWhiteSpace(deviceId)) throw new ArgumentNullException(nameof(deviceId));
            if (string.IsNullOrWhiteSpace(address)) throw new ArgumentNullException(nameof(address));
            if (value == null) throw new ArgumentNullException(nameof(value));

            DeviceId = deviceId;
            Address = address;
            DataType = dataType;
            Value = value;
            Length = length;
            Timeout = timeout;
        }

        /// <summary>目标设备标识。</summary>
        public string DeviceId { get; private set; }
        /// <summary>目标协议地址。</summary>
        public string Address { get; private set; }
        /// <summary>写入值的数据类型。</summary>
        public DataType DataType { get; private set; }
        /// <summary>待写入的值。</summary>
        public object Value { get; private set; }
        /// <summary>字符串或字节数组等变长数据的写入长度。</summary>
        public ushort Length { get; private set; }
        /// <summary>本次操作的可选超时时间；为空时使用客户端默认值。</summary>
        public TimeSpan? Timeout { get; private set; }
    }

    /// <summary>描述一组需要周期轮询的读取项。</summary>
    public sealed class SubscriptionRequest
    {
        /// <summary>初始化订阅请求。</summary>
        /// <param name="subscriptionKey">调用方提供的订阅业务键。</param>
        /// <param name="deviceId">被轮询的设备标识。</param>
        /// <param name="items">每轮需要读取的项目。</param>
        /// <param name="interval">两轮读取之间的时间间隔。</param>
        /// <param name="reportOnChangeOnly">是否仅在读取结果变化时上报事件。</param>
        public SubscriptionRequest(string subscriptionKey, string deviceId, IReadOnlyCollection<ReadRequest> items, TimeSpan interval, bool reportOnChangeOnly)
        {
            if (string.IsNullOrWhiteSpace(subscriptionKey)) throw new ArgumentNullException(nameof(subscriptionKey));
            if (string.IsNullOrWhiteSpace(deviceId)) throw new ArgumentNullException(nameof(deviceId));
            if (items == null) throw new ArgumentNullException(nameof(items));
            if (interval <= TimeSpan.Zero) throw new ArgumentOutOfRangeException(nameof(interval));

            SubscriptionKey = subscriptionKey;
            DeviceId = deviceId;
            Items = items;
            Interval = interval;
            ReportOnChangeOnly = reportOnChangeOnly;
        }

        /// <summary>调用方提供的订阅业务键。</summary>
        public string SubscriptionKey { get; private set; }
        /// <summary>被轮询的设备标识。</summary>
        public string DeviceId { get; private set; }
        /// <summary>每轮需要读取的项目。</summary>
        public IReadOnlyCollection<ReadRequest> Items { get; private set; }
        /// <summary>两轮读取之间的时间间隔。</summary>
        public TimeSpan Interval { get; private set; }
        /// <summary>是否仅在读取结果变化时上报事件。</summary>
        public bool ReportOnChangeOnly { get; private set; }
    }

    /// <summary>一次读取的结果，同时保留解码值、原始数据和质量信息。</summary>
    public sealed class DataValue
    {
        /// <summary>初始化读取结果。</summary>
        public DataValue(string address, DataType dataType, object value, byte[] rawData, QualityStatus quality, DateTimeOffset timestamp, string errorMessage)
        {
            Address = address;
            DataType = dataType;
            Value = value;
            RawData = rawData;
            Quality = quality;
            Timestamp = timestamp;
            ErrorMessage = errorMessage;
        }

        /// <summary>本结果对应的协议地址。</summary>
        public string Address { get; private set; }
        /// <summary>解码后的数据类型。</summary>
        public DataType DataType { get; private set; }
        /// <summary>解码值；读取失败时可能为空。</summary>
        public object Value { get; private set; }
        /// <summary>设备返回的原始字节；读取失败时可能为空。</summary>
        public byte[] RawData { get; private set; }
        /// <summary>当前值的质量状态。</summary>
        public QualityStatus Quality { get; private set; }
        /// <summary>结果生成时间。</summary>
        public DateTimeOffset Timestamp { get; private set; }
        /// <summary>读取失败时的错误信息。</summary>
        public string ErrorMessage { get; private set; }
    }

    /// <summary>一次批量读取返回的有序结果集合。</summary>
    public sealed class BatchReadResult
    {
        /// <summary>初始化批量读取结果。</summary>
        /// <param name="values">有序的读取结果列表。</param>
        public BatchReadResult(IReadOnlyList<DataValue> values)
        {
            Values = values ?? throw new ArgumentNullException(nameof(values));
        }

        /// <summary>获取有序的读取结果列表。</summary>
        public IReadOnlyList<DataValue> Values { get; private set; }
    }

    /// <summary>轮询订阅产生的一次数据通知事件参数。</summary>
    public sealed class SubscriptionEvent : EventArgs
    {
        /// <summary>初始化订阅事件参数。</summary>
        /// <param name="subscriptionId">订阅标识。</param>
        /// <param name="values">本次轮询读取的数据值列表。</param>
        /// <param name="timestamp">事件生成时间。</param>
        public SubscriptionEvent(string subscriptionId, IReadOnlyList<DataValue> values, DateTimeOffset timestamp)
        {
            SubscriptionId = subscriptionId;
            Values = values ?? throw new ArgumentNullException(nameof(values));
            Timestamp = timestamp;
        }

        /// <summary>获取订阅标识。</summary>
        public string SubscriptionId { get; private set; }
        /// <summary>获取本次轮询读取的数据值列表。</summary>
        public IReadOnlyList<DataValue> Values { get; private set; }
        /// <summary>获取事件生成时间。</summary>
        public DateTimeOffset Timestamp { get; private set; }
    }

    /// <summary>客户端连接与近期操作状态的只读快照。</summary>
    public sealed class HealthSnapshot
    {
        /// <summary>初始化健康快照。</summary>
        /// <param name="status">当前连接状态。</param>
        /// <param name="lastSuccessUtc">最近一次成功操作的 UTC 时间。</param>
        /// <param name="consecutiveFailures">连续失败次数。</param>
        /// <param name="lastError">最近一次错误信息。</param>
        public HealthSnapshot(ConnectionStatus status, DateTimeOffset? lastSuccessUtc, int consecutiveFailures, string lastError)
        {
            Status = status;
            LastSuccessUtc = lastSuccessUtc;
            ConsecutiveFailures = consecutiveFailures;
            LastError = lastError;
        }

        /// <summary>获取当前连接状态。</summary>
        public ConnectionStatus Status { get; private set; }
        /// <summary>获取最近一次成功操作的 UTC 时间。</summary>
        public DateTimeOffset? LastSuccessUtc { get; private set; }
        /// <summary>获取连续失败次数。</summary>
        public int ConsecutiveFailures { get; private set; }
        /// <summary>获取最近一次错误信息。</summary>
        public string LastError { get; private set; }
    }
}
