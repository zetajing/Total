using System;
using System.Collections.Generic;
using IndustrialCommSdk.Abstractions;
using IndustrialCommSdk.Exceptions;

namespace IndustrialCommSdk.Runtime
{
    /// <summary>描述一个包含地址、数据类型和长度的工业点位。</summary>
    public class IndustrialTag
    {
        /// <summary>创建一个非泛型点位定义。</summary>
        public IndustrialTag(string address, DataType dataType, ushort length = 1, string name = null)
        {
            if (string.IsNullOrWhiteSpace(address)) throw new ArgumentException("Address cannot be null or empty.", nameof(address));
            if (length == 0) throw new ArgumentOutOfRangeException(nameof(length), "Length must be greater than zero.");

            Address = address;
            DataType = dataType;
            Length = length;
            Name = name;
        }

        /// <summary>获取协议地址。</summary>
        public string Address { get; private set; }
        /// <summary>获取点位数据类型。</summary>
        public DataType DataType { get; private set; }
        /// <summary>获取读取长度；字符串和字节数组使用该值指定长度。</summary>
        public ushort Length { get; private set; }
        /// <summary>获取可选的业务名称。</summary>
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

    /// <summary>描述带有 CLR 返回类型的工业点位。</summary>
    public sealed class IndustrialTag<T> : IndustrialTag
    {
        /// <summary>创建一个强类型点位定义。</summary>
        public IndustrialTag(string address, DataType dataType, ushort length = 1, string name = null)
            : base(address, dataType, length, name)
        {
        }

        /// <summary>将点位和值组合成可用于批量写入的请求。</summary>
        public IndustrialWrite WithValue(T value)
        {
            return new IndustrialWrite(this, value);
        }
    }

    /// <summary>表示一个点位及其待写入值。</summary>
    public sealed class IndustrialWrite
    {
        /// <summary>创建单个写入项。</summary>
        public IndustrialWrite(IndustrialTag tag, object value)
        {
            Tag = tag ?? throw new ArgumentNullException(nameof(tag));
            Value = value ?? throw new ArgumentNullException(nameof(value));
        }

        /// <summary>获取目标点位。</summary>
        public IndustrialTag Tag { get; private set; }
        /// <summary>获取待写入值。</summary>
        public object Value { get; private set; }

        internal WriteRequest ToWriteRequest(string deviceId)
        {
            return Tag.ToWriteRequest(deviceId, Value);
        }
    }

    /// <summary>表示简化 API 的强类型成功或失败结果。</summary>
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

        /// <summary>获取操作是否成功。</summary>
        public bool IsSuccess { get; private set; }
        /// <summary>获取成功时转换后的值。</summary>
        public T Value { get; private set; }
        /// <summary>获取 SDK 返回的原始数据值及质量信息。</summary>
        public DataValue DataValue { get; private set; }
        /// <summary>获取失败原因。</summary>
        public string ErrorMessage { get; private set; }
        /// <summary>获取导致失败的原始异常。</summary>
        public Exception Exception { get; private set; }

        /// <summary>创建成功结果。</summary>
        public static IndustrialResult<T> Success(T value, DataValue dataValue)
        {
            return new IndustrialResult<T>(true, value, dataValue, null, null);
        }

        /// <summary>创建失败结果。</summary>
        public static IndustrialResult<T> Failure(string errorMessage, DataValue dataValue = null, Exception exception = null)
        {
            return new IndustrialResult<T>(false, default(T), dataValue, errorMessage, exception);
        }
    }

    /// <summary>保存一次批量读取的点位定义和对应结果。</summary>
    public sealed class IndustrialTagReadResult
    {
        private readonly Dictionary<IndustrialTag, int> _tagIndexes;
        private readonly Dictionary<string, int> _addressIndexes;

        /// <summary>创建批量读取结果；点位与值必须按索引一一对应。</summary>
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

        /// <summary>获取本次读取的点位。</summary>
        public IReadOnlyList<IndustrialTag> Tags { get; private set; }
        /// <summary>获取与 Tags 索引对应的数据值。</summary>
        public IReadOnlyList<DataValue> Values { get; private set; }

        /// <summary>按强类型点位取得并转换值。</summary>
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

        /// <summary>按地址取得并转换值。</summary>
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

    /// <summary>提供常用数据类型的强类型点位快捷构造方法。</summary>
    public static class Tag
    {
        /// <summary>创建布尔点位。</summary>
        public static IndustrialTag<bool> Bool(string address, string name = null)
        {
            return new IndustrialTag<bool>(address, DataType.Bool, 1, name);
        }

        /// <summary>创建 16 位有符号整数点位。</summary>
        public static IndustrialTag<short> Int16(string address, string name = null)
        {
            return new IndustrialTag<short>(address, DataType.Int16, 1, name);
        }

        /// <summary>创建 16 位无符号整数点位。</summary>
        public static IndustrialTag<ushort> UInt16(string address, string name = null)
        {
            return new IndustrialTag<ushort>(address, DataType.UInt16, 1, name);
        }

        /// <summary>创建 32 位有符号整数点位。</summary>
        public static IndustrialTag<int> Int32(string address, string name = null)
        {
            return new IndustrialTag<int>(address, DataType.Int32, 1, name);
        }

        /// <summary>创建 32 位无符号整数点位。</summary>
        public static IndustrialTag<uint> UInt32(string address, string name = null)
        {
            return new IndustrialTag<uint>(address, DataType.UInt32, 1, name);
        }

        /// <summary>创建单精度浮点点位。</summary>
        public static IndustrialTag<float> Float(string address, string name = null)
        {
            return new IndustrialTag<float>(address, DataType.Float, 1, name);
        }

        /// <summary>创建双精度浮点点位。</summary>
        public static IndustrialTag<double> Double(string address, string name = null)
        {
            return new IndustrialTag<double>(address, DataType.Double, 1, name);
        }

        /// <summary>创建字节点位。</summary>
        public static IndustrialTag<byte> Byte(string address, string name = null)
        {
            return new IndustrialTag<byte>(address, DataType.Byte, 1, name);
        }

        /// <summary>创建字符点位。</summary>
        public static IndustrialTag<char> Char(string address, string name = null)
        {
            return new IndustrialTag<char>(address, DataType.Char, 1, name);
        }

        /// <summary>创建指定长度的字符串点位。</summary>
        public static IndustrialTag<string> String(string address, ushort length, string name = null)
        {
            return new IndustrialTag<string>(address, DataType.String, length, name);
        }

        /// <summary>创建指定长度的字节数组点位。</summary>
        public static IndustrialTag<byte[]> Bytes(string address, ushort length, string name = null)
        {
            return new IndustrialTag<byte[]>(address, DataType.ByteArray, length, name);
        }
    }
}
