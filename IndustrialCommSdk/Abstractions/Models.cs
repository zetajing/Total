using System;
using System.Collections.Generic;

namespace IndustrialCommSdk.Abstractions
{
    public sealed class ReadRequest
    {
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

        public string DeviceId { get; private set; }
        public string Address { get; private set; }
        public DataType DataType { get; private set; }
        public ushort Length { get; private set; }
        public TimeSpan? Timeout { get; private set; }
    }

    public sealed class WriteRequest
    {
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

        public string DeviceId { get; private set; }
        public string Address { get; private set; }
        public DataType DataType { get; private set; }
        public object Value { get; private set; }
        public ushort Length { get; private set; }
        public TimeSpan? Timeout { get; private set; }
    }

    public sealed class SubscriptionRequest
    {
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

        public string SubscriptionKey { get; private set; }
        public string DeviceId { get; private set; }
        public IReadOnlyCollection<ReadRequest> Items { get; private set; }
        public TimeSpan Interval { get; private set; }
        public bool ReportOnChangeOnly { get; private set; }
    }

    public sealed class DataValue
    {
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

        public string Address { get; private set; }
        public DataType DataType { get; private set; }
        public object Value { get; private set; }
        public byte[] RawData { get; private set; }
        public QualityStatus Quality { get; private set; }
        public DateTimeOffset Timestamp { get; private set; }
        public string ErrorMessage { get; private set; }
    }

    public sealed class BatchReadResult
    {
        public BatchReadResult(IReadOnlyList<DataValue> values)
        {
            Values = values ?? throw new ArgumentNullException(nameof(values));
        }

        public IReadOnlyList<DataValue> Values { get; private set; }
    }

    public sealed class SubscriptionEvent : EventArgs
    {
        public SubscriptionEvent(string subscriptionId, IReadOnlyList<DataValue> values, DateTimeOffset timestamp)
        {
            SubscriptionId = subscriptionId;
            Values = values ?? throw new ArgumentNullException(nameof(values));
            Timestamp = timestamp;
        }

        public string SubscriptionId { get; private set; }
        public IReadOnlyList<DataValue> Values { get; private set; }
        public DateTimeOffset Timestamp { get; private set; }
    }

    public sealed class HealthSnapshot
    {
        public HealthSnapshot(ConnectionStatus status, DateTimeOffset? lastSuccessUtc, int consecutiveFailures, string lastError)
        {
            Status = status;
            LastSuccessUtc = lastSuccessUtc;
            ConsecutiveFailures = consecutiveFailures;
            LastError = lastError;
        }

        public ConnectionStatus Status { get; private set; }
        public DateTimeOffset? LastSuccessUtc { get; private set; }
        public int ConsecutiveFailures { get; private set; }
        public string LastError { get; private set; }
    }
}
