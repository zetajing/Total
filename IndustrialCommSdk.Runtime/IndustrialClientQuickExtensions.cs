using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using IndustrialCommSdk.Abstractions;
using IndustrialCommSdk.Exceptions;

namespace IndustrialCommSdk.Runtime
{
    /// <summary>工业客户端的连接与强类型读取快捷扩展。</summary>
    public static partial class IndustrialClientQuickExtensions
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

        public static async Task UseAsync<TClient>(
            this TClient client,
            Func<TClient, Task> operation,
            CancellationToken cancellationToken = default)
            where TClient : IIndustrialClient
        {
            if (client == null) throw new ArgumentNullException(nameof(client));
            if (operation == null) throw new ArgumentNullException(nameof(operation));

            try
            {
                await client.ConnectAsync(cancellationToken).ConfigureAwait(false);
                await operation(client).ConfigureAwait(false);
            }
            finally
            {
                try
                {
                    if (client.IsConnected)
                    {
                        await client.DisconnectAsync(CancellationToken.None).ConfigureAwait(false);
                    }
                }
                finally
                {
                    client.Dispose();
                }
            }
        }

        public static async Task<TResult> UseAsync<TClient, TResult>(
            this TClient client,
            Func<TClient, Task<TResult>> operation,
            CancellationToken cancellationToken = default)
            where TClient : IIndustrialClient
        {
            if (client == null) throw new ArgumentNullException(nameof(client));
            if (operation == null) throw new ArgumentNullException(nameof(operation));

            try
            {
                await client.ConnectAsync(cancellationToken).ConfigureAwait(false);
                return await operation(client).ConfigureAwait(false);
            }
            finally
            {
                try
                {
                    if (client.IsConnected)
                    {
                        await client.DisconnectAsync(CancellationToken.None).ConfigureAwait(false);
                    }
                }
                finally
                {
                    client.Dispose();
                }
            }
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
        /// 从指定地址异步读取一个值，数据类型由泛型参数自动推断。
        /// </summary>
        /// <typeparam name="T">要读取的 CLR 类型。</typeparam>
        /// <param name="client">工业客户端实例。</param>
        /// <param name="address">要读取的地址字符串。</param>
        /// <param name="cancellationToken">用于取消异步操作的取消令牌。</param>
        /// <returns>返回表示读取结果的任务，包含类型 <typeparamref name="T"/> 的值。</returns>
        public static Task<T> ReadAsync<T>(this IIndustrialClient client, string address, CancellationToken cancellationToken = default)
        {
            return ReadValueAsync<T>(client, address, InferDataType(typeof(T)), 1, cancellationToken);
        }

        /// <summary>
        /// 从指定地址异步读取一个指定长度的值，适用于字符串和字节数组等变长类型。
        /// </summary>
        /// <typeparam name="T">要读取的 CLR 类型。</typeparam>
        /// <param name="client">工业客户端实例。</param>
        /// <param name="address">要读取的地址字符串。</param>
        /// <param name="length">读取长度。</param>
        /// <param name="cancellationToken">用于取消异步操作的取消令牌。</param>
        /// <returns>返回表示读取结果的任务，包含类型 <typeparamref name="T"/> 的值。</returns>
        public static Task<T> ReadAsync<T>(this IIndustrialClient client, string address, ushort length, CancellationToken cancellationToken = default)
        {
            return ReadValueAsync<T>(client, address, InferDataType(typeof(T)), length, cancellationToken);
        }

        public static Task<T> ReadAsync<T>(this IIndustrialClient client, IndustrialTag<T> tag, CancellationToken cancellationToken = default)
        {
            if (tag == null) throw new ArgumentNullException(nameof(tag));

            return ReadValueAsync<T>(client, tag.Address, tag.DataType, tag.Length, cancellationToken);
        }

        public static async Task<IndustrialResult<T>> TryReadAsync<T>(this IIndustrialClient client, string address, CancellationToken cancellationToken = default)
        {
            return await TryReadValueAsync<T>(client, address, InferDataType(typeof(T)), 1, cancellationToken).ConfigureAwait(false);
        }

        public static async Task<IndustrialResult<T>> TryReadAsync<T>(this IIndustrialClient client, string address, ushort length, CancellationToken cancellationToken = default)
        {
            return await TryReadValueAsync<T>(client, address, InferDataType(typeof(T)), length, cancellationToken).ConfigureAwait(false);
        }

        public static async Task<IndustrialResult<T>> TryReadAsync<T>(this IIndustrialClient client, IndustrialTag<T> tag, CancellationToken cancellationToken = default)
        {
            if (tag == null) throw new ArgumentNullException(nameof(tag));

            return await TryReadValueAsync<T>(client, tag.Address, tag.DataType, tag.Length, cancellationToken).ConfigureAwait(false);
        }

        public static async Task<IndustrialResult<T>> TryReadValueAsync<T>(
            this IIndustrialClient client,
            string address,
            DataType dataType,
            ushort length = 1,
            CancellationToken cancellationToken = default)
        {
            if (client == null) throw new ArgumentNullException(nameof(client));

            DataValue result = null;
            try
            {
                result = await client.ReadAsync(
                    new ReadRequest(client.DeviceId, address, dataType, length),
                    cancellationToken).ConfigureAwait(false);

                EnsureReadable(result);
                return IndustrialResult<T>.Success(ConvertValue<T>(result.Value, address, dataType), result);
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                return IndustrialResult<T>.Failure(ex.Message, result, ex);
            }
        }

        public static Task<IndustrialTagReadResult> ReadManyAsync(this IIndustrialClient client, params IndustrialTag[] tags)
        {
            return ReadManyAsync(client, (IReadOnlyCollection<IndustrialTag>)tags, CancellationToken.None);
        }

        public static Task<IReadOnlyDictionary<string, T>> ReadManyAsync<T>(this IIndustrialClient client, params string[] addresses)
        {
            return ReadManyAsync<T>(client, (IReadOnlyCollection<string>)addresses, CancellationToken.None);
        }

        public static Task<IReadOnlyDictionary<string, T>> ReadManyAsync<T>(
            this IIndustrialClient client,
            IReadOnlyCollection<string> addresses,
            CancellationToken cancellationToken = default)
        {
            return ReadManyAsync<T>(client, addresses, 1, cancellationToken);
        }

        public static async Task<IReadOnlyDictionary<string, T>> ReadManyAsync<T>(
            this IIndustrialClient client,
            IReadOnlyCollection<string> addresses,
            ushort length,
            CancellationToken cancellationToken = default)
        {
            if (client == null) throw new ArgumentNullException(nameof(client));
            if (addresses == null) throw new ArgumentNullException(nameof(addresses));

            var dataType = InferDataType(typeof(T));
            var requestAddresses = new List<string>(addresses.Count);
            var requests = new List<ReadRequest>(addresses.Count);
            foreach (var address in addresses)
            {
                if (string.IsNullOrWhiteSpace(address))
                {
                    throw new ArgumentException("Addresses cannot contain null or empty values.", nameof(addresses));
                }

                requestAddresses.Add(address);
                requests.Add(new ReadRequest(client.DeviceId, address, dataType, length));
            }

            var result = await client.ReadManyAsync(requests, cancellationToken).ConfigureAwait(false);
            var values = new Dictionary<string, T>(StringComparer.OrdinalIgnoreCase);
            for (var i = 0; i < result.Values.Count; i++)
            {
                var value = result.Values[i];
                EnsureReadable(value);
                values[requestAddresses[i]] = ConvertValue<T>(value.Value, requestAddresses[i], dataType);
            }

            return values;
        }

        public static async Task<IndustrialTagReadResult> ReadManyAsync(
            this IIndustrialClient client,
            IReadOnlyCollection<IndustrialTag> tags,
            CancellationToken cancellationToken = default)
        {
            if (client == null) throw new ArgumentNullException(nameof(client));
            if (tags == null) throw new ArgumentNullException(nameof(tags));

            var tagList = new List<IndustrialTag>(tags.Count);
            var requests = new List<ReadRequest>(tags.Count);
            foreach (var tag in tags)
            {
                if (tag == null) throw new ArgumentException("Tags cannot contain null.", nameof(tags));
                tagList.Add(tag);
                requests.Add(tag.ToReadRequest(client.DeviceId));
            }

            var result = await client.ReadManyAsync(requests, cancellationToken).ConfigureAwait(false);
            return new IndustrialTagReadResult(tagList, result.Values);
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
    }
}
