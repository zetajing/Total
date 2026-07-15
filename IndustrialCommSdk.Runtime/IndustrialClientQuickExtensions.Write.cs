using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using IndustrialCommSdk.Abstractions;
using IndustrialCommSdk.Exceptions;

namespace IndustrialCommSdk.Runtime
{
    /// <summary>工业客户端的写入与值转换快捷扩展。</summary>
    public static partial class IndustrialClientQuickExtensions
    {
        /// <summary>
        /// 向指定地址异步写入一个 16 位有符号整数。
        /// </summary>
        /// <param name="client">工业客户端实例。</param>
        /// <param name="address">要写入的地址字符串。</param>
        /// <param name="value">要写入的 <see cref="short"/> 值。</param>
        /// <param name="cancellationToken">用于取消异步操作的取消令牌。</param>
        /// <returns>表示异步写入操作的任务。</returns>
        /// <exception cref="ArgumentNullException">当 <paramref name="client"/> 为 null 时抛出。</exception>
        /// <exception cref="IndustrialProtocolException">写入操作失败时抛出。</exception>
        public static Task WriteAsync(this IIndustrialClient client, string address, short value, CancellationToken cancellationToken = default)
        {
            return WriteValueAsync(client, address, DataType.Int16, value, 1, cancellationToken);
        }

        /// <summary>
        /// 向指定地址异步写入一个 16 位无符号整数。
        /// </summary>
        /// <param name="client">工业客户端实例。</param>
        /// <param name="address">要写入的地址字符串。</param>
        /// <param name="value">要写入的 <see cref="ushort"/> 值。</param>
        /// <param name="cancellationToken">用于取消异步操作的取消令牌。</param>
        /// <returns>表示异步写入操作的任务。</returns>
        /// <exception cref="ArgumentNullException">当 <paramref name="client"/> 为 null 时抛出。</exception>
        /// <exception cref="IndustrialProtocolException">写入操作失败时抛出。</exception>
        public static Task WriteAsync(this IIndustrialClient client, string address, ushort value, CancellationToken cancellationToken = default)
        {
            return WriteValueAsync(client, address, DataType.UInt16, value, 1, cancellationToken);
        }

        /// <summary>
        /// 向指定地址异步写入一个 32 位有符号整数。
        /// </summary>
        /// <param name="client">工业客户端实例。</param>
        /// <param name="address">要写入的地址字符串。</param>
        /// <param name="value">要写入的 <see cref="int"/> 值。</param>
        /// <param name="cancellationToken">用于取消异步操作的取消令牌。</param>
        /// <returns>表示异步写入操作的任务。</returns>
        /// <exception cref="ArgumentNullException">当 <paramref name="client"/> 为 null 时抛出。</exception>
        /// <exception cref="IndustrialProtocolException">写入操作失败时抛出。</exception>
        public static Task WriteAsync(this IIndustrialClient client, string address, int value, CancellationToken cancellationToken = default)
        {
            return WriteValueAsync(client, address, DataType.Int32, value, 1, cancellationToken);
        }

        /// <summary>
        /// 向指定地址异步写入一个 32 位无符号整数。
        /// </summary>
        /// <param name="client">工业客户端实例。</param>
        /// <param name="address">要写入的地址字符串。</param>
        /// <param name="value">要写入的 <see cref="uint"/> 值。</param>
        /// <param name="cancellationToken">用于取消异步操作的取消令牌。</param>
        /// <returns>表示异步写入操作的任务。</returns>
        /// <exception cref="ArgumentNullException">当 <paramref name="client"/> 为 null 时抛出。</exception>
        /// <exception cref="IndustrialProtocolException">写入操作失败时抛出。</exception>
        public static Task WriteAsync(this IIndustrialClient client, string address, uint value, CancellationToken cancellationToken = default)
        {
            return WriteValueAsync(client, address, DataType.UInt32, value, 1, cancellationToken);
        }

        /// <summary>
        /// 向指定地址异步写入一个单精度浮点数。
        /// </summary>
        /// <param name="client">工业客户端实例。</param>
        /// <param name="address">要写入的地址字符串。</param>
        /// <param name="value">要写入的 <see cref="float"/> 值。</param>
        /// <param name="cancellationToken">用于取消异步操作的取消令牌。</param>
        /// <returns>表示异步写入操作的任务。</returns>
        /// <exception cref="ArgumentNullException">当 <paramref name="client"/> 为 null 时抛出。</exception>
        /// <exception cref="IndustrialProtocolException">写入操作失败时抛出。</exception>
        public static Task WriteAsync(this IIndustrialClient client, string address, float value, CancellationToken cancellationToken = default)
        {
            return WriteValueAsync(client, address, DataType.Float, value, 1, cancellationToken);
        }

        /// <summary>
        /// 向指定地址异步写入一个双精度浮点数。
        /// </summary>
        /// <param name="client">工业客户端实例。</param>
        /// <param name="address">要写入的地址字符串。</param>
        /// <param name="value">要写入的 <see cref="double"/> 值。</param>
        /// <param name="cancellationToken">用于取消异步操作的取消令牌。</param>
        /// <returns>表示异步写入操作的任务。</returns>
        /// <exception cref="ArgumentNullException">当 <paramref name="client"/> 为 null 时抛出。</exception>
        /// <exception cref="IndustrialProtocolException">写入操作失败时抛出。</exception>
        public static Task WriteAsync(this IIndustrialClient client, string address, double value, CancellationToken cancellationToken = default)
        {
            return WriteValueAsync(client, address, DataType.Double, value, 1, cancellationToken);
        }

        /// <summary>
        /// 向指定地址异步写入一个字符串，写入长度由字符串长度自动推断。
        /// </summary>
        /// <param name="client">工业客户端实例。</param>
        /// <param name="address">要写入的地址字符串。</param>
        /// <param name="value">要写入的字符串值。如果为 null，则写入空字符串。</param>
        /// <param name="cancellationToken">用于取消异步操作的取消令牌。</param>
        /// <returns>表示异步写入操作的任务。</returns>
        public static Task WriteAsync(this IIndustrialClient client, string address, string value, CancellationToken cancellationToken = default)
        {
            var text = value ?? string.Empty;
            return WriteValueAsync(client, address, DataType.String, text, ToUShortLength(text.Length), cancellationToken);
        }

        /// <summary>
        /// 向指定地址异步写入一个字节数组，写入长度由数组长度自动推断。
        /// </summary>
        /// <param name="client">工业客户端实例。</param>
        /// <param name="address">要写入的地址字符串。</param>
        /// <param name="value">要写入的字节数组。如果为 null，则写入空数组。</param>
        /// <param name="cancellationToken">用于取消异步操作的取消令牌。</param>
        /// <returns>表示异步写入操作的任务。</returns>
        public static Task WriteAsync(this IIndustrialClient client, string address, byte[] value, CancellationToken cancellationToken = default)
        {
            var bytes = value ?? Array.Empty<byte>();
            return WriteValueAsync(client, address, DataType.ByteArray, bytes, ToUShortLength(bytes.Length), cancellationToken);
        }

        public static Task WriteAsync<T>(this IIndustrialClient client, IndustrialTag<T> tag, T value, CancellationToken cancellationToken = default)
        {
            if (tag == null) throw new ArgumentNullException(nameof(tag));

            return WriteValueAsync(client, tag.Address, tag.DataType, value, tag.Length, cancellationToken);
        }

        public static Task WriteManyAsync(this IIndustrialClient client, params IndustrialWrite[] writes)
        {
            return WriteManyAsync(client, (IReadOnlyCollection<IndustrialWrite>)writes, CancellationToken.None);
        }

        public static Task WriteManyAsync<T>(
            this IIndustrialClient client,
            IReadOnlyDictionary<string, T> values,
            CancellationToken cancellationToken = default)
        {
            if (client == null) throw new ArgumentNullException(nameof(client));
            if (values == null) throw new ArgumentNullException(nameof(values));

            var dataType = InferDataType(typeof(T));
            var requests = new List<WriteRequest>(values.Count);
            foreach (var item in values)
            {
                if (string.IsNullOrWhiteSpace(item.Key))
                {
                    throw new ArgumentException("Write addresses cannot contain null or empty values.", nameof(values));
                }

                requests.Add(new WriteRequest(client.DeviceId, item.Key, dataType, item.Value, GetWriteLength(item.Value)));
            }

            return client.WriteManyAsync(requests, cancellationToken);
        }

        public static Task WriteManyAsync(
            this IIndustrialClient client,
            IReadOnlyCollection<IndustrialWrite> writes,
            CancellationToken cancellationToken = default)
        {
            if (client == null) throw new ArgumentNullException(nameof(client));
            if (writes == null) throw new ArgumentNullException(nameof(writes));

            var requests = new List<WriteRequest>(writes.Count);
            foreach (var write in writes)
            {
                if (write == null) throw new ArgumentException("Writes cannot contain null.", nameof(writes));
                requests.Add(write.ToWriteRequest(client.DeviceId));
            }

            return client.WriteManyAsync(requests, cancellationToken);
        }

        /// <summary>
        /// 向指定地址异步写入一个字符串。
        /// </summary>
        /// <param name="client">工业客户端实例。</param>
        /// <param name="address">要写入的地址字符串。</param>
        /// <param name="value">要写入的字符串值。如果为 null，则写入空字符串。</param>
        /// <param name="length">要写入的字符串长度（字符数）。</param>
        /// <param name="cancellationToken">用于取消异步操作的取消令牌。</param>
        /// <returns>表示异步写入操作的任务。</returns>
        /// <exception cref="ArgumentNullException">当 <paramref name="client"/> 为 null 时抛出。</exception>
        /// <exception cref="IndustrialProtocolException">写入操作失败时抛出。</exception>
        public static Task WriteStringAsync(this IIndustrialClient client, string address, string value, ushort length, CancellationToken cancellationToken = default)
        {
            return WriteValueAsync(client, address, DataType.String, value ?? string.Empty, length, cancellationToken);
        }

        /// <summary>
        /// 向指定地址异步写入一个字节数组。
        /// </summary>
        /// <param name="client">工业客户端实例。</param>
        /// <param name="address">要写入的地址字符串。</param>
        /// <param name="value">要写入的字节数组。如果为 null，则写入空数组。</param>
        /// <param name="length">要写入的字节长度。</param>
        /// <param name="cancellationToken">用于取消异步操作的取消令牌。</param>
        /// <returns>表示异步写入操作的任务。</returns>
        /// <exception cref="ArgumentNullException">当 <paramref name="client"/> 为 null 时抛出。</exception>
        /// <exception cref="IndustrialProtocolException">写入操作失败时抛出。</exception>
        public static Task WriteByteArrayAsync(this IIndustrialClient client, string address, byte[] value, ushort length, CancellationToken cancellationToken = default)
        {
            return WriteValueAsync(client, address, DataType.ByteArray, value ?? Array.Empty<byte>(), length, cancellationToken);
        }

        /// <summary>
        /// 向指定地址异步写入一个指定数据类型和长度的值。
        /// </summary>
        /// <param name="client">工业客户端实例。</param>
        /// <param name="address">要写入的地址字符串。</param>
        /// <param name="dataType">要写入的数据类型，如 <see cref="DataType.Bool"/>、<see cref="DataType.Int32"/> 等。</param>
        /// <param name="value">要写入的值对象。</param>
        /// <param name="length">写入的数据长度。对于基本类型通常为 1；对于字符串和字节数组，表示字符数/字节数。</param>
        /// <param name="cancellationToken">用于取消异步操作的取消令牌。</param>
        /// <returns>表示异步写入操作的任务。</returns>
        /// <exception cref="ArgumentNullException">当 <paramref name="client"/> 为 null 时抛出。</exception>
        /// <exception cref="IndustrialProtocolException">写入操作失败时抛出。</exception>
        public static Task WriteValueAsync(
            this IIndustrialClient client,
            string address,
            DataType dataType,
            object value,
            ushort length = 1,
            CancellationToken cancellationToken = default)
        {
            if (client == null) throw new ArgumentNullException(nameof(client));

            return client.WriteAsync(
                new WriteRequest(client.DeviceId, address, dataType, value, length),
                cancellationToken);
        }

        /// <summary>
        /// 确保读取结果有效。如果结果为 null 或质量状态不为 Good，则抛出协议异常。
        /// </summary>
        /// <param name="result">要验证的 <see cref="DataValue"/> 读取结果实例。</param>
        /// <exception cref="IndustrialProtocolException">
        /// 当 <paramref name="result"/> 为 null，或其 <see cref="DataValue.Quality"/> 不等于 <see cref="QualityStatus.Good"/> 时抛出。
        /// 异常消息会优先使用结果中的 <see cref="DataValue.ErrorMessage"/>，若为空则使用默认格式。
        /// </exception>
        private static void EnsureReadable(DataValue result)
        {
            if (result == null)
            {
                throw new IndustrialProtocolException("Read result is null.");
            }

            if (result.Quality != QualityStatus.Good)
            {
                throw new IndustrialProtocolException(
                    string.IsNullOrWhiteSpace(result.ErrorMessage)
                        ? string.Format("Read failed for address {0}.", result.Address)
                        : result.ErrorMessage);
            }
        }

        /// <summary>
        /// 将读取到的原始值对象转换为目标泛型类型 <typeparamref name="T"/>。
        /// </summary>
        /// <typeparam name="T">目标转换类型。</typeparam>
        /// <param name="value">从设备读取到的原始值对象。</param>
        /// <param name="address">读取操作的目标地址，用于在转换失败时提供错误上下文。</param>
        /// <param name="dataType">读取操作指定的数据类型，用于在转换失败时提供错误上下文。</param>
        /// <returns>返回转换后的 <typeparamref name="T"/> 类型值。</returns>
        /// <exception cref="IndustrialDataConversionException">
        /// 当 <paramref name="value"/> 为 null，或无法将值转换为目标类型时抛出。
        /// </exception>
        /// <remarks>
        /// 该方法首先检查值是否为 null，然后尝试直接类型转换；如果失败，则使用 <see cref="Convert.ChangeType(object, Type)"/>
        /// 进行基础类型转换。所有转换失败都会包装为 <see cref="IndustrialDataConversionException"/> 异常抛出。
        /// </remarks>
        private static T ConvertValue<T>(object value, string address, DataType dataType)
        {
            if (value == null)
            {
                throw new IndustrialDataConversionException(
                    string.Format("Read value is null for address {0}.", address));
            }

            if (value is T typed)
            {
                return typed;
            }

            try
            {
                return (T)Convert.ChangeType(value, typeof(T));
            }
            catch (Exception ex)
            {
                throw new IndustrialDataConversionException(
                    string.Format("Cannot convert address {0} value from {1} to {2} for data type {3}.",
                        address,
                        value.GetType().Name,
                        typeof(T).Name,
                        dataType),
                    ex);
            }
        }

        private static DataType InferDataType(Type type)
        {
            var targetType = Nullable.GetUnderlyingType(type) ?? type;

            if (targetType == typeof(bool)) return DataType.Bool;
            if (targetType == typeof(short)) return DataType.Int16;
            if (targetType == typeof(ushort)) return DataType.UInt16;
            if (targetType == typeof(int)) return DataType.Int32;
            if (targetType == typeof(uint)) return DataType.UInt32;
            if (targetType == typeof(float)) return DataType.Float;
            if (targetType == typeof(double)) return DataType.Double;
            if (targetType == typeof(byte)) return DataType.Byte;
            if (targetType == typeof(char)) return DataType.Char;
            if (targetType == typeof(string)) return DataType.String;
            if (targetType == typeof(byte[])) return DataType.ByteArray;

            throw new IndustrialDataConversionException(
                string.Format("Cannot infer industrial data type from CLR type {0}. Use ReadValueAsync<T> with an explicit DataType.", type.Name));
        }

        private static ushort ToUShortLength(int length)
        {
            if (length > ushort.MaxValue)
            {
                throw new ArgumentOutOfRangeException(nameof(length), "Length cannot exceed UInt16.MaxValue.");
            }

            return (ushort)length;
        }

        private static ushort GetWriteLength(object value)
        {
            if (value is string text)
            {
                return ToUShortLength(text.Length);
            }

            if (value is byte[] bytes)
            {
                return ToUShortLength(bytes.Length);
            }

            return 1;
        }
    }
}
