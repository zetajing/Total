using System;
using System.Threading;
using System.Threading.Tasks;
using IndustrialCommSdk.Abstractions;
using IndustrialCommSdk.Exceptions;

namespace IndustrialCommSdk
{
    public static class IndustrialClientQuickExtensions
    {
        public static Task ConnectAsync(this IIndustrialClient client)
        {
            return client.ConnectAsync(CancellationToken.None);
        }

        public static Task DisconnectAsync(this IIndustrialClient client)
        {
            return client.DisconnectAsync(CancellationToken.None);
        }

        public static Task<bool> ReadBoolAsync(this IIndustrialClient client, string address, CancellationToken cancellationToken = default)
        {
            return ReadValueAsync<bool>(client, address, DataType.Bool, 1, cancellationToken);
        }

        public static Task<short> ReadInt16Async(this IIndustrialClient client, string address, CancellationToken cancellationToken = default)
        {
            return ReadValueAsync<short>(client, address, DataType.Int16, 1, cancellationToken);
        }

        public static Task<ushort> ReadUInt16Async(this IIndustrialClient client, string address, CancellationToken cancellationToken = default)
        {
            return ReadValueAsync<ushort>(client, address, DataType.UInt16, 1, cancellationToken);
        }

        public static Task<int> ReadInt32Async(this IIndustrialClient client, string address, CancellationToken cancellationToken = default)
        {
            return ReadValueAsync<int>(client, address, DataType.Int32, 1, cancellationToken);
        }

        public static Task<uint> ReadUInt32Async(this IIndustrialClient client, string address, CancellationToken cancellationToken = default)
        {
            return ReadValueAsync<uint>(client, address, DataType.UInt32, 1, cancellationToken);
        }

        public static Task<float> ReadFloatAsync(this IIndustrialClient client, string address, CancellationToken cancellationToken = default)
        {
            return ReadValueAsync<float>(client, address, DataType.Float, 1, cancellationToken);
        }

        public static Task<double> ReadDoubleAsync(this IIndustrialClient client, string address, CancellationToken cancellationToken = default)
        {
            return ReadValueAsync<double>(client, address, DataType.Double, 1, cancellationToken);
        }

        public static Task<string> ReadStringAsync(this IIndustrialClient client, string address, ushort length, CancellationToken cancellationToken = default)
        {
            return ReadValueAsync<string>(client, address, DataType.String, length, cancellationToken);
        }

        public static Task<byte[]> ReadByteArrayAsync(this IIndustrialClient client, string address, ushort length, CancellationToken cancellationToken = default)
        {
            return ReadValueAsync<byte[]>(client, address, DataType.ByteArray, length, cancellationToken);
        }

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

        public static Task WriteAsync(this IIndustrialClient client, string address, bool value, CancellationToken cancellationToken = default)
        {
            return WriteValueAsync(client, address, DataType.Bool, value, 1, cancellationToken);
        }

        public static Task WriteAsync(this IIndustrialClient client, string address, short value, CancellationToken cancellationToken = default)
        {
            return WriteValueAsync(client, address, DataType.Int16, value, 1, cancellationToken);
        }

        public static Task WriteAsync(this IIndustrialClient client, string address, ushort value, CancellationToken cancellationToken = default)
        {
            return WriteValueAsync(client, address, DataType.UInt16, value, 1, cancellationToken);
        }

        public static Task WriteAsync(this IIndustrialClient client, string address, int value, CancellationToken cancellationToken = default)
        {
            return WriteValueAsync(client, address, DataType.Int32, value, 1, cancellationToken);
        }

        public static Task WriteAsync(this IIndustrialClient client, string address, uint value, CancellationToken cancellationToken = default)
        {
            return WriteValueAsync(client, address, DataType.UInt32, value, 1, cancellationToken);
        }

        public static Task WriteAsync(this IIndustrialClient client, string address, float value, CancellationToken cancellationToken = default)
        {
            return WriteValueAsync(client, address, DataType.Float, value, 1, cancellationToken);
        }

        public static Task WriteAsync(this IIndustrialClient client, string address, double value, CancellationToken cancellationToken = default)
        {
            return WriteValueAsync(client, address, DataType.Double, value, 1, cancellationToken);
        }

        public static Task WriteStringAsync(this IIndustrialClient client, string address, string value, ushort length, CancellationToken cancellationToken = default)
        {
            return WriteValueAsync(client, address, DataType.String, value ?? string.Empty, length, cancellationToken);
        }

        public static Task WriteByteArrayAsync(this IIndustrialClient client, string address, byte[] value, ushort length, CancellationToken cancellationToken = default)
        {
            return WriteValueAsync(client, address, DataType.ByteArray, value ?? Array.Empty<byte>(), length, cancellationToken);
        }

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
