using System;
using System.Collections.Generic;
using IndustrialCommSdk.Abstractions;
using IndustrialCommSdk.Exceptions;

namespace IndustrialCommSdk
{
    public class IndustrialTag
    {
        public IndustrialTag(string address, DataType dataType, ushort length = 1, string name = null)
        {
            if (string.IsNullOrWhiteSpace(address)) throw new ArgumentException("Address cannot be null or empty.", nameof(address));
            if (length == 0) throw new ArgumentOutOfRangeException(nameof(length), "Length must be greater than zero.");

            Address = address;
            DataType = dataType;
            Length = length;
            Name = name;
        }

        public string Address { get; private set; }
        public DataType DataType { get; private set; }
        public ushort Length { get; private set; }
        public string Name { get; private set; }

        internal ReadRequest ToReadRequest(string deviceId)
        {
            return new ReadRequest(deviceId, Address, DataType, Length);
        }

        internal WriteRequest ToWriteRequest(string deviceId, object value)
        {
            return new WriteRequest(deviceId, Address, DataType, value, Length);
        }
    }

    public sealed class IndustrialTag<T> : IndustrialTag
    {
        public IndustrialTag(string address, DataType dataType, ushort length = 1, string name = null)
            : base(address, dataType, length, name)
        {
        }

        public IndustrialWrite WithValue(T value)
        {
            return new IndustrialWrite(this, value);
        }
    }

    public sealed class IndustrialWrite
    {
        public IndustrialWrite(IndustrialTag tag, object value)
        {
            Tag = tag ?? throw new ArgumentNullException(nameof(tag));
            Value = value ?? throw new ArgumentNullException(nameof(value));
        }

        public IndustrialTag Tag { get; private set; }
        public object Value { get; private set; }

        internal WriteRequest ToWriteRequest(string deviceId)
        {
            return Tag.ToWriteRequest(deviceId, Value);
        }
    }

    public sealed class IndustrialResult<T>
    {
        private IndustrialResult(bool isSuccess, T value, DataValue dataValue, string errorMessage, Exception exception)
        {
            IsSuccess = isSuccess;
            Value = value;
            DataValue = dataValue;
            ErrorMessage = errorMessage;
            Exception = exception;
        }

        public bool IsSuccess { get; private set; }
        public T Value { get; private set; }
        public DataValue DataValue { get; private set; }
        public string ErrorMessage { get; private set; }
        public Exception Exception { get; private set; }

        public static IndustrialResult<T> Success(T value, DataValue dataValue)
        {
            return new IndustrialResult<T>(true, value, dataValue, null, null);
        }

        public static IndustrialResult<T> Failure(string errorMessage, DataValue dataValue = null, Exception exception = null)
        {
            return new IndustrialResult<T>(false, default(T), dataValue, errorMessage, exception);
        }
    }

    public sealed class IndustrialTagReadResult
    {
        private readonly Dictionary<IndustrialTag, int> _tagIndexes;
        private readonly Dictionary<string, int> _addressIndexes;

        public IndustrialTagReadResult(IReadOnlyList<IndustrialTag> tags, IReadOnlyList<DataValue> values)
        {
            Tags = tags ?? throw new ArgumentNullException(nameof(tags));
            Values = values ?? throw new ArgumentNullException(nameof(values));

            if (tags.Count != values.Count)
            {
                throw new ArgumentException("Tag count must match value count.", nameof(values));
            }

            _tagIndexes = new Dictionary<IndustrialTag, int>();
            _addressIndexes = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);

            for (var i = 0; i < tags.Count; i++)
            {
                _tagIndexes[tags[i]] = i;
                if (!_addressIndexes.ContainsKey(tags[i].Address))
                {
                    _addressIndexes.Add(tags[i].Address, i);
                }
            }
        }

        public IReadOnlyList<IndustrialTag> Tags { get; private set; }
        public IReadOnlyList<DataValue> Values { get; private set; }

        public T Get<T>(IndustrialTag<T> tag)
        {
            if (tag == null) throw new ArgumentNullException(nameof(tag));

            int index;
            if (!_tagIndexes.TryGetValue(tag, out index))
            {
                if (!_addressIndexes.TryGetValue(tag.Address, out index))
                {
                    throw new KeyNotFoundException(string.Format("Tag address {0} was not found in the read result.", tag.Address));
                }
            }

            return ConvertValue<T>(Values[index], tag.Address, tag.DataType);
        }

        public T Get<T>(string address)
        {
            if (string.IsNullOrWhiteSpace(address)) throw new ArgumentException("Address cannot be null or empty.", nameof(address));

            int index;
            if (!_addressIndexes.TryGetValue(address, out index))
            {
                throw new KeyNotFoundException(string.Format("Address {0} was not found in the read result.", address));
            }

            return ConvertValue<T>(Values[index], address, Values[index].DataType);
        }

        private static T ConvertValue<T>(DataValue dataValue, string address, DataType dataType)
        {
            if (dataValue == null)
            {
                throw new IndustrialProtocolException(string.Format("Read result is null for address {0}.", address));
            }

            if (dataValue.Quality != QualityStatus.Good)
            {
                throw new IndustrialProtocolException(
                    string.IsNullOrWhiteSpace(dataValue.ErrorMessage)
                        ? string.Format("Read failed for address {0}.", address)
                        : dataValue.ErrorMessage);
            }

            if (dataValue.Value == null)
            {
                throw new IndustrialDataConversionException(string.Format("Read value is null for address {0}.", address));
            }

            if (dataValue.Value is T typed)
            {
                return typed;
            }

            try
            {
                return (T)Convert.ChangeType(dataValue.Value, typeof(T));
            }
            catch (Exception ex)
            {
                throw new IndustrialDataConversionException(
                    string.Format("Cannot convert address {0} value from {1} to {2} for data type {3}.",
                        address,
                        dataValue.Value.GetType().Name,
                        typeof(T).Name,
                        dataType),
                    ex);
            }
        }
    }

    public static class Tag
    {
        public static IndustrialTag<bool> Bool(string address, string name = null)
        {
            return new IndustrialTag<bool>(address, DataType.Bool, 1, name);
        }

        public static IndustrialTag<short> Int16(string address, string name = null)
        {
            return new IndustrialTag<short>(address, DataType.Int16, 1, name);
        }

        public static IndustrialTag<ushort> UInt16(string address, string name = null)
        {
            return new IndustrialTag<ushort>(address, DataType.UInt16, 1, name);
        }

        public static IndustrialTag<int> Int32(string address, string name = null)
        {
            return new IndustrialTag<int>(address, DataType.Int32, 1, name);
        }

        public static IndustrialTag<uint> UInt32(string address, string name = null)
        {
            return new IndustrialTag<uint>(address, DataType.UInt32, 1, name);
        }

        public static IndustrialTag<float> Float(string address, string name = null)
        {
            return new IndustrialTag<float>(address, DataType.Float, 1, name);
        }

        public static IndustrialTag<double> Double(string address, string name = null)
        {
            return new IndustrialTag<double>(address, DataType.Double, 1, name);
        }

        public static IndustrialTag<byte> Byte(string address, string name = null)
        {
            return new IndustrialTag<byte>(address, DataType.Byte, 1, name);
        }

        public static IndustrialTag<char> Char(string address, string name = null)
        {
            return new IndustrialTag<char>(address, DataType.Char, 1, name);
        }

        public static IndustrialTag<string> String(string address, ushort length, string name = null)
        {
            return new IndustrialTag<string>(address, DataType.String, length, name);
        }

        public static IndustrialTag<byte[]> Bytes(string address, ushort length, string name = null)
        {
            return new IndustrialTag<byte[]>(address, DataType.ByteArray, length, name);
        }
    }
}
