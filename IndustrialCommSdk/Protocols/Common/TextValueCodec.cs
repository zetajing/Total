using System;
using System.Globalization;
using System.Text;
using IndustrialCommSdk.Abstractions;
using IndustrialCommSdk.Exceptions;

namespace IndustrialCommSdk.Protocols.Common
{
    internal static class TextValueCodec
    {
        public static byte[] Encode(DataType type, object value)
        {
            if (value == null) throw new ArgumentNullException(nameof(value));
            if (type == DataType.ByteArray)
            {
                var bytes = value as byte[];
                if (bytes == null) throw new IndustrialDataConversionException("ByteArray requires byte[].");
                return (byte[])bytes.Clone();
            }
            try
            {
                var text = type == DataType.Bool
                    ? (Convert.ToBoolean(value, CultureInfo.InvariantCulture) ? "true" : "false")
                    : Convert.ToString(value, CultureInfo.InvariantCulture);
                return Encoding.UTF8.GetBytes(text ?? string.Empty);
            }
            catch (Exception ex) { throw new IndustrialDataConversionException("Cannot encode value as " + type + ".", ex); }
        }

        public static object Decode(DataType type, byte[] bytes)
        {
            if (bytes == null) return null;
            if (type == DataType.ByteArray) return (byte[])bytes.Clone();
            var text = Encoding.UTF8.GetString(bytes);
            try
            {
                switch (type)
                {
                    case DataType.Bool: return bool.Parse(text);
                    case DataType.Byte: return byte.Parse(text, CultureInfo.InvariantCulture);
                    case DataType.Char: return char.Parse(text);
                    case DataType.Int16: return short.Parse(text, CultureInfo.InvariantCulture);
                    case DataType.UInt16: return ushort.Parse(text, CultureInfo.InvariantCulture);
                    case DataType.Int32: return int.Parse(text, CultureInfo.InvariantCulture);
                    case DataType.UInt32: return uint.Parse(text, CultureInfo.InvariantCulture);
                    case DataType.Float: return float.Parse(text, CultureInfo.InvariantCulture);
                    case DataType.Double: return double.Parse(text, CultureInfo.InvariantCulture);
                    case DataType.String: return text;
                    default: throw new NotSupportedException("Unsupported text data type: " + type);
                }
            }
            catch (Exception ex) { throw new IndustrialDataConversionException("Cannot decode payload as " + type + ".", ex); }
        }
    }
}
