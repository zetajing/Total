using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using InduLink.Abstractions;
using Newtonsoft.Json.Linq;

namespace InduLink.Runtime
{
    /// <summary>控制工业点位向 HTTP、MQTT 和 WebSocket 等远程入口公开时的安全策略。</summary>
    public sealed class IndustrialTagGatewayOptions
    {
        /// <summary>是否允许远程写入。点位自身还必须标记为 Writable。</summary>
        public bool EnableRemoteWrites { get; set; }

        /// <summary>是否允许绕过点位名称直接读取协议地址；默认关闭。</summary>
        public bool AllowRawAddressReads { get; set; }

        /// <summary>是否在对外元数据和结果中公开底层协议地址；默认关闭。</summary>
        public bool ExposeRawAddresses { get; set; }
    }

    /// <summary>为外部通信入口提供仅按配置点位访问的统一设备网关。</summary>
    public interface IIndustrialTagGateway
    {
        IndustrialTagGatewayOptions Options { get; }
        IReadOnlyList<TagGatewayDevice> Devices { get; }
        IReadOnlyList<TagGatewayTag> GetTags(string deviceName);
        Task<IReadOnlyList<TagGatewayValue>> ReadAsync(IReadOnlyCollection<TagGatewayReadItem> items, CancellationToken cancellationToken = default);
        Task<IReadOnlyList<TagGatewayWriteResult>> WriteAsync(IReadOnlyCollection<TagGatewayWriteItem> items, CancellationToken cancellationToken = default);
        Task<TagGatewayValue> ReadAddressAsync(TagGatewayRawReadItem item, CancellationToken cancellationToken = default);
        event EventHandler<TagGatewayValuesChangedEventArgs> ValuesChanged;
        event EventHandler<TagGatewayDeviceStateChangedEventArgs> DeviceStateChanged;
    }

    /// <summary>基于 IndustrialDeviceHost 的默认点位网关实现。</summary>
    public sealed class IndustrialTagGateway : IIndustrialTagGateway, IDisposable
    {
        private readonly IndustrialDeviceHost _host;
        private int _disposed;

        public IndustrialTagGateway(IndustrialDeviceHost host, IndustrialTagGatewayOptions options = null)
        {
            _host = host ?? throw new ArgumentNullException(nameof(host));
            Options = options ?? new IndustrialTagGatewayOptions();
            _host.ValuesReceived += HostOnValuesReceived;
            _host.DeviceStateChanged += HostOnDeviceStateChanged;
        }

        public IndustrialTagGatewayOptions Options { get; }

        public IReadOnlyList<TagGatewayDevice> Devices
        {
            get
            {
                ThrowIfDisposed();
                return _host.Devices.Values
                    .OrderBy(device => device.Device.DeviceName, StringComparer.OrdinalIgnoreCase)
                    .Select(CreateDeviceSnapshot)
                    .ToList()
                    .AsReadOnly();
            }
        }

        public event EventHandler<TagGatewayValuesChangedEventArgs> ValuesChanged;
        public event EventHandler<TagGatewayDeviceStateChangedEventArgs> DeviceStateChanged;

        public IReadOnlyList<TagGatewayTag> GetTags(string deviceName)
        {
            ThrowIfDisposed();
            var device = _host.Get(Require(deviceName, nameof(deviceName)));
            return device.Device.Tags.Tags
                .Where(tag => !string.IsNullOrWhiteSpace(tag.Name))
                .Select(tag => new TagGatewayTag(
                    tag.Name,
                    tag.DataType,
                    tag.Length,
                    tag.Writable,
                    Options.ExposeRawAddresses ? tag.Address : null))
                .ToList()
                .AsReadOnly();
        }

        public async Task<IReadOnlyList<TagGatewayValue>> ReadAsync(
            IReadOnlyCollection<TagGatewayReadItem> items,
            CancellationToken cancellationToken = default)
        {
            ThrowIfDisposed();
            if (items == null) throw new ArgumentNullException(nameof(items));

            var requested = items.ToList();
            var results = new TagGatewayValue[requested.Count];
            var groups = new Dictionary<string, List<ReadWorkItem>>(StringComparer.OrdinalIgnoreCase);

            for (var index = 0; index < requested.Count; index++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var item = requested[index];
                if (item == null)
                {
                    results[index] = TagGatewayValue.Failure(null, null, null, "Read item cannot be null.");
                    continue;
                }

                try
                {
                    var device = _host.Get(Require(item.DeviceName, nameof(item.DeviceName)));
                    var tag = device.Device.Tags.Get(Require(item.TagName, nameof(item.TagName)));
                    if (string.IsNullOrWhiteSpace(tag.Name)) throw new KeyNotFoundException("Only named tags can be read through the gateway.");

                    List<ReadWorkItem> group;
                    if (!groups.TryGetValue(device.Device.DeviceName, out group))
                    {
                        group = new List<ReadWorkItem>();
                        groups.Add(device.Device.DeviceName, group);
                    }
                    group.Add(new ReadWorkItem(index, device, tag));
                }
                catch (Exception ex)
                {
                    results[index] = TagGatewayValue.Failure(item.DeviceName, item.TagName, null, ex.Message);
                }
            }

            foreach (var group in groups.Values)
            {
                cancellationToken.ThrowIfCancellationRequested();
                try
                {
                    var requests = group.Select(work => new ReadRequest(
                        work.Device.Device.Client.DeviceId,
                        work.Tag.Address,
                        work.Tag.DataType,
                        work.Tag.Length)).ToList();
                    var batch = await group[0].Device.Device.Client.ReadManyAsync(requests, cancellationToken).ConfigureAwait(false);
                    for (var i = 0; i < group.Count; i++)
                    {
                        var work = group[i];
                        results[work.Index] = i < batch.Values.Count
                            ? CreateValue(work.Device.Device.DeviceName, work.Tag, batch.Values[i])
                            : TagGatewayValue.Failure(work.Device.Device.DeviceName, work.Tag.Name, work.Tag.DataType, "The device returned fewer values than requested.");
                    }
                }
                catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
                {
                    throw;
                }
                catch (Exception ex)
                {
                    var error = Options.ExposeRawAddresses ? ex.Message : "Device read failed.";
                    foreach (var work in group)
                    {
                        results[work.Index] = TagGatewayValue.Failure(work.Device.Device.DeviceName, work.Tag.Name, work.Tag.DataType, error);
                    }
                }
            }

            return Array.AsReadOnly(results);
        }

        public async Task<IReadOnlyList<TagGatewayWriteResult>> WriteAsync(
            IReadOnlyCollection<TagGatewayWriteItem> items,
            CancellationToken cancellationToken = default)
        {
            ThrowIfDisposed();
            if (items == null) throw new ArgumentNullException(nameof(items));

            var requested = items.ToList();
            var results = new TagGatewayWriteResult[requested.Count];
            var groups = new Dictionary<string, List<WriteWorkItem>>(StringComparer.OrdinalIgnoreCase);

            for (var index = 0; index < requested.Count; index++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var item = requested[index];
                if (item == null)
                {
                    results[index] = TagGatewayWriteResult.Failure(null, null, "Write item cannot be null.");
                    continue;
                }

                try
                {
                    if (!Options.EnableRemoteWrites) throw new UnauthorizedAccessException("Remote writes are disabled.");
                    var device = _host.Get(Require(item.DeviceName, nameof(item.DeviceName)));
                    var tag = device.Device.Tags.Get(Require(item.TagName, nameof(item.TagName)));
                    if (string.IsNullOrWhiteSpace(tag.Name)) throw new KeyNotFoundException("Only named tags can be written through the gateway.");
                    if (!tag.Writable) throw new UnauthorizedAccessException("The requested tag is not writable.");

                    var converted = ConvertValue(item.Value, tag.DataType);
                    List<WriteWorkItem> group;
                    if (!groups.TryGetValue(device.Device.DeviceName, out group))
                    {
                        group = new List<WriteWorkItem>();
                        groups.Add(device.Device.DeviceName, group);
                    }
                    group.Add(new WriteWorkItem(index, device, tag, converted));
                }
                catch (Exception ex)
                {
                    results[index] = TagGatewayWriteResult.Failure(item.DeviceName, item.TagName, ex.Message);
                }
            }

            foreach (var group in groups.Values)
            {
                cancellationToken.ThrowIfCancellationRequested();
                try
                {
                    var requests = group.Select(work => new WriteRequest(
                        work.Device.Device.Client.DeviceId,
                        work.Tag.Address,
                        work.Tag.DataType,
                        work.Value,
                        work.Tag.Length)).ToList();
                    await group[0].Device.Device.Client.WriteManyAsync(requests, cancellationToken).ConfigureAwait(false);
                    foreach (var work in group)
                    {
                        results[work.Index] = TagGatewayWriteResult.Success(work.Device.Device.DeviceName, work.Tag.Name);
                    }
                }
                catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
                {
                    throw;
                }
                catch (Exception ex)
                {
                    var error = Options.ExposeRawAddresses ? ex.Message : "Device write failed.";
                    foreach (var work in group)
                    {
                        results[work.Index] = TagGatewayWriteResult.Failure(work.Device.Device.DeviceName, work.Tag.Name, error);
                    }
                }
            }

            return Array.AsReadOnly(results);
        }

        public async Task<TagGatewayValue> ReadAddressAsync(TagGatewayRawReadItem item, CancellationToken cancellationToken = default)
        {
            ThrowIfDisposed();
            if (item == null) throw new ArgumentNullException(nameof(item));
            if (!Options.AllowRawAddressReads) throw new UnauthorizedAccessException("Raw address reads are disabled.");

            var device = _host.Get(Require(item.DeviceName, nameof(item.DeviceName)));
            var request = new ReadRequest(
                device.Device.Client.DeviceId,
                Require(item.Address, nameof(item.Address)),
                item.DataType,
                item.Length);
            var value = await device.Device.Client.ReadAsync(request, cancellationToken).ConfigureAwait(false);
            return new TagGatewayValue(
                device.Device.DeviceName,
                null,
                value.DataType,
                value.Value,
                value.Quality,
                value.Timestamp,
                value.ErrorMessage,
                Options.ExposeRawAddresses ? value.Address : null);
        }

        public void Dispose()
        {
            if (Interlocked.Exchange(ref _disposed, 1) != 0) return;
            _host.ValuesReceived -= HostOnValuesReceived;
            _host.DeviceStateChanged -= HostOnDeviceStateChanged;
        }

        private void HostOnValuesReceived(object sender, IndustrialDeviceValuesEventArgs args)
        {
            var values = new List<TagGatewayValue>();
            var count = Math.Min(args.Tags.Count, args.Values.Count);
            for (var i = 0; i < count; i++)
            {
                if (string.IsNullOrWhiteSpace(args.Tags[i].Name)) continue;
                values.Add(CreateValue(args.DeviceName, args.Tags[i], args.Values[i]));
            }

            if (values.Count > 0)
            {
                ValuesChanged?.Invoke(this, new TagGatewayValuesChangedEventArgs(values.AsReadOnly(), args.Timestamp));
            }
        }

        private void HostOnDeviceStateChanged(object sender, IndustrialDeviceStateChangedEventArgs args)
        {
            var error = args.ErrorMessage ?? args.Health.LastError;
            if (!Options.ExposeRawAddresses && !string.IsNullOrWhiteSpace(error))
                error = "Device communication error.";
            DeviceStateChanged?.Invoke(this, new TagGatewayDeviceStateChangedEventArgs(new TagGatewayDevice(
                args.DeviceName,
                args.Health.Status,
                args.Health.LastSuccessUtc,
                args.Health.ConsecutiveFailures,
                error)));
        }

        private TagGatewayValue CreateValue(string deviceName, IndustrialTag tag, DataValue value)
        {
            return new TagGatewayValue(
                deviceName,
                tag.Name,
                value.DataType,
                value.Value,
                value.Quality,
                value.Timestamp,
                Options.ExposeRawAddresses || string.IsNullOrWhiteSpace(value.ErrorMessage)
                    ? value.ErrorMessage
                    : "Device read returned an error.",
                Options.ExposeRawAddresses ? value.Address : null);
        }

        private TagGatewayDevice CreateDeviceSnapshot(IndustrialHostedDevice device)
        {
            var health = device.Health;
            var error = device.LastError ?? health.LastError;
            if (!Options.ExposeRawAddresses && !string.IsNullOrWhiteSpace(error))
                error = "Device communication error.";
            return new TagGatewayDevice(
                device.Device.DeviceName,
                health.Status,
                health.LastSuccessUtc,
                health.ConsecutiveFailures,
                error);
        }

        private static object ConvertValue(object value, DataType dataType)
        {
            if (value is JValue jsonValue) value = jsonValue.Value;
            if (value == null) throw new ArgumentNullException(nameof(value), "Write value cannot be null.");

            switch (dataType)
            {
                case DataType.Bool:
                    if (value is string boolText)
                    {
                        if (string.Equals(boolText, "1", StringComparison.Ordinal)) return true;
                        if (string.Equals(boolText, "0", StringComparison.Ordinal)) return false;
                    }
                    return Convert.ToBoolean(value, CultureInfo.InvariantCulture);
                case DataType.SByte: return Convert.ToSByte(value, CultureInfo.InvariantCulture);
                case DataType.Int16: return Convert.ToInt16(value, CultureInfo.InvariantCulture);
                case DataType.UInt16: return Convert.ToUInt16(value, CultureInfo.InvariantCulture);
                case DataType.Int32: return Convert.ToInt32(value, CultureInfo.InvariantCulture);
                case DataType.UInt32: return Convert.ToUInt32(value, CultureInfo.InvariantCulture);
                case DataType.Int64: return Convert.ToInt64(value, CultureInfo.InvariantCulture);
                case DataType.UInt64: return Convert.ToUInt64(value, CultureInfo.InvariantCulture);
                case DataType.Float: return Convert.ToSingle(value, CultureInfo.InvariantCulture);
                case DataType.Double: return Convert.ToDouble(value, CultureInfo.InvariantCulture);
                case DataType.Byte: return Convert.ToByte(value, CultureInfo.InvariantCulture);
                case DataType.Char:
                    var text = Convert.ToString(value, CultureInfo.InvariantCulture);
                    if (string.IsNullOrEmpty(text) || text.Length != 1) throw new FormatException("Char values must contain exactly one character.");
                    return text[0];
                case DataType.String:
                case DataType.WString: return Convert.ToString(value, CultureInfo.InvariantCulture);
                case DataType.Time:
                case DataType.TimeOfDay:
                case DataType.LTime:
                    if (value is TimeSpan) return value;
                    if (value is string timeText) return TimeSpan.Parse(timeText, CultureInfo.InvariantCulture);
                    return TimeSpan.FromMilliseconds(Convert.ToDouble(value, CultureInfo.InvariantCulture));
                case DataType.Date:
                case DataType.DateTime:
                    if (value is DateTimeOffset || value is DateTime) return value;
                    return DateTimeOffset.Parse(Convert.ToString(value, CultureInfo.InvariantCulture), CultureInfo.InvariantCulture);
                case DataType.ByteArray:
                    if (value is byte[] bytes) return bytes;
                    if (value is JArray jsonArray) return jsonArray.ToObject<byte[]>();
                    if (value is string base64) return Convert.FromBase64String(base64);
                    if (value is IEnumerable<byte> byteSequence) return byteSequence.ToArray();
                    throw new FormatException("ByteArray values must be a byte array or Base64 string.");
                default: throw new ArgumentOutOfRangeException(nameof(dataType), dataType, "Unsupported data type.");
            }
        }

        private static string Require(string value, string parameterName)
        {
            if (string.IsNullOrWhiteSpace(value)) throw new ArgumentException("Value cannot be null or empty.", parameterName);
            return value.Trim();
        }

        private void ThrowIfDisposed()
        {
            if (Volatile.Read(ref _disposed) != 0) throw new ObjectDisposedException(nameof(IndustrialTagGateway));
        }

        private class ReadWorkItem
        {
            public ReadWorkItem(int index, IndustrialHostedDevice device, IndustrialTag tag)
            {
                Index = index;
                Device = device;
                Tag = tag;
            }

            public int Index { get; }
            public IndustrialHostedDevice Device { get; }
            public IndustrialTag Tag { get; }
        }

        private sealed class WriteWorkItem : ReadWorkItem
        {
            public WriteWorkItem(int index, IndustrialHostedDevice device, IndustrialTag tag, object value)
                : base(index, device, tag)
            {
                Value = value;
            }

            public object Value { get; }
        }
    }

    public sealed class TagGatewayDevice
    {
        public TagGatewayDevice(string name, ConnectionStatus status, DateTimeOffset? lastSuccessUtc, int consecutiveFailures, string lastError)
        {
            Name = name;
            Status = status;
            LastSuccessUtc = lastSuccessUtc;
            ConsecutiveFailures = consecutiveFailures;
            LastError = lastError;
        }

        public string Name { get; }
        public ConnectionStatus Status { get; }
        public DateTimeOffset? LastSuccessUtc { get; }
        public int ConsecutiveFailures { get; }
        public string LastError { get; }
    }

    public sealed class TagGatewayTag
    {
        public TagGatewayTag(string name, DataType dataType, ushort length, bool writable, string address)
        {
            Name = name;
            DataType = dataType;
            Length = length;
            Writable = writable;
            Address = address;
        }

        public string Name { get; }
        public DataType DataType { get; }
        public ushort Length { get; }
        public bool Writable { get; }
        public string Address { get; }
    }

    public sealed class TagGatewayReadItem
    {
        public TagGatewayReadItem(string deviceName, string tagName)
        {
            DeviceName = deviceName;
            TagName = tagName;
        }

        public string DeviceName { get; }
        public string TagName { get; }
    }

    public sealed class TagGatewayWriteItem
    {
        public TagGatewayWriteItem(string deviceName, string tagName, object value)
        {
            DeviceName = deviceName;
            TagName = tagName;
            Value = value;
        }

        public string DeviceName { get; }
        public string TagName { get; }
        public object Value { get; }
    }

    public sealed class TagGatewayRawReadItem
    {
        public TagGatewayRawReadItem(string deviceName, string address, DataType dataType, ushort length = 1)
        {
            if (length == 0) throw new ArgumentOutOfRangeException(nameof(length));
            DeviceName = deviceName;
            Address = address;
            DataType = dataType;
            Length = length;
        }

        public string DeviceName { get; }
        public string Address { get; }
        public DataType DataType { get; }
        public ushort Length { get; }
    }

    public sealed class TagGatewayValue
    {
        public TagGatewayValue(
            string deviceName,
            string tagName,
            DataType? dataType,
            object value,
            QualityStatus quality,
            DateTimeOffset timestamp,
            string errorMessage,
            string address)
        {
            DeviceName = deviceName;
            TagName = tagName;
            DataType = dataType;
            Value = value;
            Quality = quality;
            Timestamp = timestamp;
            ErrorMessage = errorMessage;
            Address = address;
        }

        public string DeviceName { get; }
        public string TagName { get; }
        public DataType? DataType { get; }
        public object Value { get; }
        public QualityStatus Quality { get; }
        public DateTimeOffset Timestamp { get; }
        public string ErrorMessage { get; }
        public string Address { get; }

        public static TagGatewayValue Failure(string deviceName, string tagName, DataType? dataType, string errorMessage)
        {
            return new TagGatewayValue(deviceName, tagName, dataType, null, QualityStatus.Bad, DateTimeOffset.UtcNow, errorMessage, null);
        }
    }

    public sealed class TagGatewayWriteResult
    {
        private TagGatewayWriteResult(string deviceName, string tagName, bool succeeded, DateTimeOffset timestamp, string errorMessage)
        {
            DeviceName = deviceName;
            TagName = tagName;
            Succeeded = succeeded;
            Timestamp = timestamp;
            ErrorMessage = errorMessage;
        }

        public string DeviceName { get; }
        public string TagName { get; }
        public bool Succeeded { get; }
        public DateTimeOffset Timestamp { get; }
        public string ErrorMessage { get; }

        public static TagGatewayWriteResult Success(string deviceName, string tagName)
        {
            return new TagGatewayWriteResult(deviceName, tagName, true, DateTimeOffset.UtcNow, null);
        }

        public static TagGatewayWriteResult Failure(string deviceName, string tagName, string errorMessage)
        {
            return new TagGatewayWriteResult(deviceName, tagName, false, DateTimeOffset.UtcNow, errorMessage);
        }
    }

    public sealed class TagGatewayValuesChangedEventArgs : EventArgs
    {
        public TagGatewayValuesChangedEventArgs(IReadOnlyList<TagGatewayValue> values, DateTimeOffset timestamp)
        {
            Values = values ?? throw new ArgumentNullException(nameof(values));
            Timestamp = timestamp;
        }

        public IReadOnlyList<TagGatewayValue> Values { get; }
        public DateTimeOffset Timestamp { get; }
    }

    public sealed class TagGatewayDeviceStateChangedEventArgs : EventArgs
    {
        public TagGatewayDeviceStateChangedEventArgs(TagGatewayDevice device)
        {
            Device = device ?? throw new ArgumentNullException(nameof(device));
        }

        public TagGatewayDevice Device { get; }
    }
}
