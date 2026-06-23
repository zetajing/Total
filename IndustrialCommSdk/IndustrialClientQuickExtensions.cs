using System;
using System.Threading;
using System.Threading.Tasks;
using IndustrialCommSdk.Abstractions;
using IndustrialCommSdk.Exceptions;

namespace IndustrialCommSdk
{
    /// <summary>
    /// 工业客户端快速扩展方法类，为 <see cref="IIndustrialClient"/> 提供便捷的读写扩展方法。
    /// </summary>
    /// <remarks>
    /// 通过本扩展类，您可以以强类型方式对工业设备进行数据的读写操作，
    /// 无需直接处理 <see cref="ReadRequest"/> 和 <see cref="WriteRequest"/> 的构造细节。
    /// 支持布尔、16/32 位整数、浮点数、双精度浮点数、字符串和字节数组等常用数据类型。
    /// </remarks>
    public static class IndustrialClientQuickExtensions
    {
        /// <summary>
        /// 使用默认取消令牌异步连接到远程工业设备。
        /// </summary>
        /// <param name="client">要执行连接操作的 <see cref="IIndustrialClient"/> 实例。</param>
        /// <returns>表示异步连接操作的任务。</returns>
        /// <exception cref="ArgumentNullException">当 <paramref name="client"/> 为 null 时抛出。</exception>
        public static Task ConnectAsync(this IIndustrialClient client)
        {
            return client.ConnectAsync(CancellationToken.None);
        }

        /// <summary>
        /// 使用默认取消令牌异步断开与远程工业设备的连接。
        /// </summary>
        /// <param name="client">要执行断开连接操作的 <see cref="IIndustrialClient"/> 实例。</param>
        /// <returns>表示异步断开连接操作的任务。</returns>
        /// <exception cref="ArgumentNullException">当 <paramref name="client"/> 为 null 时抛出。</exception>
        public static Task DisconnectAsync(this IIndustrialClient client)
        {
            return client.DisconnectAsync(CancellationToken.None);
        }

        /// <summary>
        /// 从指定地址异步读取一个布尔值。
        /// </summary>
        /// <param name="client">工业客户端实例。</param>
        /// <param name="address">要读取的地址字符串，格式取决于底层协议（如 "M100"、"D100.0" 等）。</param>
        /// <param name="cancellationToken">用于取消异步操作的取消令牌。</param>
        /// <returns>返回表示读取结果的任务，包含布尔类型的值。</returns>
        /// <exception cref="ArgumentNullException">当 <paramref name="client"/> 为 null 时抛出。</exception>
        /// <exception cref="IndustrialProtocolException">读取操作失败或结果状态异常时抛出。</exception>
        /// <exception cref="IndustrialDataConversionException">数据转换失败时抛出。</exception>
        public static Task<bool> ReadBoolAsync(this IIndustrialClient client, string address, CancellationToken cancellationToken = default)
        {
            return ReadValueAsync<bool>(client, address, DataType.Bool, 1, cancellationToken);
        }

        /// <summary>
        /// 从指定地址异步读取一个 16 位有符号整数。
        /// </summary>
        /// <param name="client">工业客户端实例。</param>
        /// <param name="address">要读取的地址字符串，格式取决于底层协议。</param>
        /// <param name="cancellationToken">用于取消异步操作的取消令牌。</param>
        /// <returns>返回表示读取结果的任务，包含 <see cref="short"/> 类型的值。</returns>
        /// <exception cref="ArgumentNullException">当 <paramref name="client"/> 为 null 时抛出。</exception>
        /// <exception cref="IndustrialProtocolException">读取操作失败或结果状态异常时抛出。</exception>
        /// <exception cref="IndustrialDataConversionException">数据转换失败时抛出。</exception>
        public static Task<short> ReadInt16Async(this IIndustrialClient client, string address, CancellationToken cancellationToken = default)
        {
            return ReadValueAsync<short>(client, address, DataType.Int16, 1, cancellationToken);
        }

        /// <summary>
        /// 从指定地址异步读取一个 16 位无符号整数。
        /// </summary>
        /// <param name="client">工业客户端实例。</param>
        /// <param name="address">要读取的地址字符串，格式取决于底层协议。</param>
        /// <param name="cancellationToken">用于取消异步操作的取消令牌。</param>
        /// <returns>返回表示读取结果的任务，包含 <see cref="ushort"/> 类型的值。</returns>
        /// <exception cref="ArgumentNullException">当 <paramref name="client"/> 为 null 时抛出。</exception>
        /// <exception cref="IndustrialProtocolException">读取操作失败或结果状态异常时抛出。</exception>
        /// <exception cref="IndustrialDataConversionException">数据转换失败时抛出。</exception>
        public static Task<ushort> ReadUInt16Async(this IIndustrialClient client, string address, CancellationToken cancellationToken = default)
        {
            return ReadValueAsync<ushort>(client, address, DataType.UInt16, 1, cancellationToken);
        }

        /// <summary>
        /// 从指定地址异步读取一个 32 位有符号整数。
        /// </summary>
        /// <param name="client">工业客户端实例。</param>
        /// <param name="address">要读取的地址字符串，格式取决于底层协议。</param>
        /// <param name="cancellationToken">用于取消异步操作的取消令牌。</param>
        /// <returns>返回表示读取结果的任务，包含 <see cref="int"/> 类型的值。</returns>
        /// <exception cref="ArgumentNullException">当 <paramref name="client"/> 为 null 时抛出。</exception>
        /// <exception cref="IndustrialProtocolException">读取操作失败或结果状态异常时抛出。</exception>
        /// <exception cref="IndustrialDataConversionException">数据转换失败时抛出。</exception>
        public static Task<int> ReadInt32Async(this IIndustrialClient client, string address, CancellationToken cancellationToken = default)
        {
            return ReadValueAsync<int>(client, address, DataType.Int32, 1, cancellationToken);
        }

        /// <summary>
        /// 从指定地址异步读取一个 32 位无符号整数。
        /// </summary>
        /// <param name="client">工业客户端实例。</param>
        /// <param name="address">要读取的地址字符串，格式取决于底层协议。</param>
        /// <param name="cancellationToken">用于取消异步操作的取消令牌。</param>
        /// <returns>返回表示读取结果的任务，包含 <see cref="uint"/> 类型的值。</returns>
        /// <exception cref="ArgumentNullException">当 <paramref name="client"/> 为 null 时抛出。</exception>
        /// <exception cref="IndustrialProtocolException">读取操作失败或结果状态异常时抛出。</exception>
        /// <exception cref="IndustrialDataConversionException">数据转换失败时抛出。</exception>
        public static Task<uint> ReadUInt32Async(this IIndustrialClient client, string address, CancellationToken cancellationToken = default)
        {
            return ReadValueAsync<uint>(client, address, DataType.UInt32, 1, cancellationToken);
        }

        /// <summary>
        /// 从指定地址异步读取一个单精度浮点数。
        /// </summary>
        /// <param name="client">工业客户端实例。</param>
        /// <param name="address">要读取的地址字符串，格式取决于底层协议。</param>
        /// <param name="cancellationToken">用于取消异步操作的取消令牌。</param>
        /// <returns>返回表示读取结果的任务，包含 <see cref="float"/> 类型的值。</returns>
        /// <exception cref="ArgumentNullException">当 <paramref name="client"/> 为 null 时抛出。</exception>
        /// <exception cref="IndustrialProtocolException">读取操作失败或结果状态异常时抛出。</exception>
        /// <exception cref="IndustrialDataConversionException">数据转换失败时抛出。</exception>
        public static Task<float> ReadFloatAsync(this IIndustrialClient client, string address, CancellationToken cancellationToken = default)
        {
            return ReadValueAsync<float>(client, address, DataType.Float, 1, cancellationToken);
        }

        /// <summary>
        /// 从指定地址异步读取一个双精度浮点数。
        /// </summary>
        /// <param name="client">工业客户端实例。</param>
        /// <param name="address">要读取的地址字符串，格式取决于底层协议。</param>
        /// <param name="cancellationToken">用于取消异步操作的取消令牌。</param>
        /// <returns>返回表示读取结果的任务，包含 <see cref="double"/> 类型的值。</returns>
        /// <exception cref="ArgumentNullException">当 <paramref name="client"/> 为 null 时抛出。</exception>
        /// <exception cref="IndustrialProtocolException">读取操作失败或结果状态异常时抛出。</exception>
        /// <exception cref="IndustrialDataConversionException">数据转换失败时抛出。</exception>
        public static Task<double> ReadDoubleAsync(this IIndustrialClient client, string address, CancellationToken cancellationToken = default)
        {
            return ReadValueAsync<double>(client, address, DataType.Double, 1, cancellationToken);
        }

        /// <summary>
        /// 从指定地址异步读取一个字符串。
        /// </summary>
        /// <param name="client">工业客户端实例。</param>
        /// <param name="address">要读取的地址字符串，格式取决于底层协议。</param>
        /// <param name="length">要读取的字符串长度（字符数）。</param>
        /// <param name="cancellationToken">用于取消异步操作的取消令牌。</param>
        /// <returns>返回表示读取结果的任务，包含 <see cref="string"/> 类型的值。</returns>
        /// <exception cref="ArgumentNullException">当 <paramref name="client"/> 为 null 时抛出。</exception>
        /// <exception cref="IndustrialProtocolException">读取操作失败或结果状态异常时抛出。</exception>
        /// <exception cref="IndustrialDataConversionException">数据转换失败时抛出。</exception>
        public static Task<string> ReadStringAsync(this IIndustrialClient client, string address, ushort length, CancellationToken cancellationToken = default)
        {
            return ReadValueAsync<string>(client, address, DataType.String, length, cancellationToken);
        }

        /// <summary>
        /// 从指定地址异步读取一个字节数组。
        /// </summary>
        /// <param name="client">工业客户端实例。</param>
        /// <param name="address">要读取的地址字符串，格式取决于底层协议。</param>
        /// <param name="length">要读取的字节长度。</param>
        /// <param name="cancellationToken">用于取消异步操作的取消令牌。</param>
        /// <returns>返回表示读取结果的任务，包含字节数组。</returns>
        /// <exception cref="ArgumentNullException">当 <paramref name="client"/> 为 null 时抛出。</exception>
        /// <exception cref="IndustrialProtocolException">读取操作失败或结果状态异常时抛出。</exception>
        /// <exception cref="IndustrialDataConversionException">数据转换失败时抛出。</exception>
        public static Task<byte[]> ReadByteArrayAsync(this IIndustrialClient client, string address, ushort length, CancellationToken cancellationToken = default)
        {
            return ReadValueAsync<byte[]>(client, address, DataType.ByteArray, length, cancellationToken);
        }

        /// <summary>
        /// 从指定地址异步读取指定数据类型和长度的值，并自动转换为泛型类型 <typeparamref name="T"/>。
        /// </summary>
        /// <typeparam name="T">返回值的类型，支持 bool、short、ushort、int、uint、float、double、string、byte[] 等。</typeparam>
        /// <param name="client">工业客户端实例。</param>
        /// <param name="address">要读取的地址字符串，格式取决于底层协议。</param>
        /// <param name="dataType">要读取的数据类型，如 <see cref="DataType.Bool"/>、<see cref="DataType.Int32"/> 等。</param>
        /// <param name="length">读取的数据长度。对于基本类型通常为 1；对于字符串和字节数组，表示字符数/字节数。</param>
        /// <param name="cancellationToken">用于取消异步操作的取消令牌。</param>
        /// <returns>返回表示读取结果的任务，包含类型 <typeparamref name="T"/> 的值。</returns>
        /// <exception cref="ArgumentNullException">当 <paramref name="client"/> 为 null 时抛出。</exception>
        /// <exception cref="IndustrialProtocolException">读取操作失败或结果状态异常时抛出。</exception>
        /// <exception cref="IndustrialDataConversionException">数据转换失败时抛出。</exception>
        public static async Task<T> ReadValueAsync<T>(
            this IIndustrialClient client,
            string address,
            DataType dataType,
            ushort length = 1,
            CancellationToken cancellationToken = default)
        {
            if (client == null) throw new ArgumentNullException(nameof(client));

            var result = await client.ReadAsync(
                new ReadRequest(client.DeviceId, address, dataType, length),
                cancellationToken).ConfigureAwait(false);

            EnsureReadable(result);
            return ConvertValue<T>(result.Value, address, dataType);
        }

        /// <summary>
        /// 向指定地址异步写入一个布尔值。
        /// </summary>
        /// <param name="client">工业客户端实例。</param>
        /// <param name="address">要写入的地址字符串。</param>
        /// <param name="value">要写入的布尔值。</param>
        /// <param name="cancellationToken">用于取消异步操作的取消令牌。</param>
        /// <returns>表示异步写入操作的任务。</returns>
        /// <exception cref="ArgumentNullException">当 <paramref name="client"/> 为 null 时抛出。</exception>
        /// <exception cref="IndustrialProtocolException">写入操作失败时抛出。</exception>
        public static Task WriteAsync(this IIndustrialClient client, string address, bool value, CancellationToken cancellationToken = default)
        {
            return WriteValueAsync(client, address, DataType.Bool, value, 1, cancellationToken);
        }

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
    }
}
