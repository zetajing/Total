using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Net;
using System.Text;
using System.Windows.Controls;
using System.Windows.Media;
using IndustrialCommSdk.Abstractions;

namespace IndustrialCommDemo.Helpers
{
    internal static class ParseHelper
    {
        public static string RequireText(string text, string fieldName)
        {
            var value = (text ?? string.Empty).Trim();
            if (value.Length == 0)
                throw new InvalidOperationException(fieldName + " 不能为空。");
            return value;
        }

        public static int ParseIntValue(string text, string fieldName)
        {
            int value;
            if (!int.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out value))
                throw new InvalidOperationException(fieldName + " 格式无效。");
            return value;
        }

        public static short ParseShortValue(string text, string fieldName)
        {
            short value;
            if (!short.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out value))
                throw new InvalidOperationException(fieldName + " 格式无效。");
            return value;
        }

        public static byte ParseByteValue(string text, string fieldName)
        {
            byte value;
            if (!byte.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out value))
                throw new InvalidOperationException(fieldName + " 格式无效。");
            return value;
        }

        public static ushort ParseUShortValue(string text, string fieldName)
        {
            ushort value;
            if (!ushort.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out value))
                throw new InvalidOperationException(fieldName + " 格式无效。");
            return value;
        }

        public static IPAddress ParseListenAddress(string text)
        {
            var value = RequireText(text, "监听 IP");
            if (value == "0.0.0.0") return IPAddress.Any;
            IPAddress address;
            if (!IPAddress.TryParse(value, out address))
                throw new InvalidOperationException("监听 IP 格式无效。");
            return address;
        }

        public static object ParseValue(string text, DataType dataType, ushort length)
        {
            switch (dataType)
            {
                case DataType.Bool:
                    return ParseBoolValue(text, length);
                case DataType.Byte:
                    return byte.Parse(text, NumberStyles.Integer, CultureInfo.InvariantCulture);
                case DataType.Char:
                    return string.IsNullOrEmpty(text) ? '\0' : text[0];
                case DataType.Int16:
                    return short.Parse(text, NumberStyles.Integer, CultureInfo.InvariantCulture);
                case DataType.UInt16:
                    return ushort.Parse(text, NumberStyles.Integer, CultureInfo.InvariantCulture);
                case DataType.Int32:
                    return int.Parse(text, NumberStyles.Integer, CultureInfo.InvariantCulture);
                case DataType.UInt32:
                    return uint.Parse(text, NumberStyles.Integer, CultureInfo.InvariantCulture);
                case DataType.Float:
                    return float.Parse(text, NumberStyles.Float | NumberStyles.AllowThousands, CultureInfo.InvariantCulture);
                case DataType.Double:
                    return double.Parse(text, NumberStyles.Float | NumberStyles.AllowThousands, CultureInfo.InvariantCulture);
                case DataType.String:
                    return text ?? string.Empty;
                case DataType.ByteArray:
                    return ParseByteArray(text);
                default:
                    throw new InvalidOperationException("不支持的数据类型。");
            }
        }

        private static object ParseBoolValue(string text, ushort length)
        {
            var tokens = SplitTokens(text);
            if (length > 1 && tokens.Count > 1)
                return tokens.Select(ParseBooleanToken).ToArray();
            return ParseBooleanToken(text);
        }

        private static bool ParseBooleanToken(string text)
        {
            var token = (text ?? string.Empty).Trim();
            if (string.Equals(token, "1", StringComparison.OrdinalIgnoreCase)) return true;
            if (string.Equals(token, "0", StringComparison.OrdinalIgnoreCase)) return false;
            return bool.Parse(token);
        }

        public static byte[] ParseByteArray(string text)
        {
            var input = (text ?? string.Empty).Trim();
            if (input.Length == 0) return new byte[0];

            var tokens = SplitTokens(input);
            if (tokens.Count > 1)
            {
                return tokens
                    .Select(token => byte.Parse(token.Replace("0x", string.Empty).Replace("0X", string.Empty), NumberStyles.HexNumber, CultureInfo.InvariantCulture))
                    .ToArray();
            }

            var compact = input.Replace("0x", string.Empty).Replace("0X", string.Empty).Replace("-", string.Empty).Replace(" ", string.Empty);
            if (compact.Length > 0 && compact.Length % 2 == 0 && compact.All(IsHexCharacter))
            {
                var buffer = new byte[compact.Length / 2];
                for (var index = 0; index < buffer.Length; index++)
                    buffer[index] = byte.Parse(compact.Substring(index * 2, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture);
                return buffer;
            }

            return Encoding.ASCII.GetBytes(input);
        }

        private static bool IsHexCharacter(char value)
        {
            return (value >= '0' && value <= '9') ||
                   (value >= 'a' && value <= 'f') ||
                   (value >= 'A' && value <= 'F');
        }

        public static List<string> SplitTokens(string text)
        {
            return (text ?? string.Empty)
                .Split(new[] { ',', ';', ' ', '\r', '\n', '\t' }, StringSplitOptions.RemoveEmptyEntries)
                .ToList();
        }

        public static List<string> SplitAddresses(string text)
        {
            return (text ?? string.Empty)
                .Split(new[] { ',', ';', '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries)
                .Select(item => item.Trim())
                .Where(item => item.Length > 0)
                .ToList();
        }

        public static List<string> SplitBatchWriteValues(string text)
        {
            return (text ?? string.Empty)
                .Split(new[] { ',', ';', '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries)
                .Select(item => item.Trim())
                .Where(item => item.Length > 0)
                .ToList();
        }
    }

    internal static class FormatHelper
    {
        public static string FormatDataValue(DataValue value)
        {
            if (value == null) return "<null>";
            var raw = value.RawData == null ? string.Empty : BitConverter.ToString(value.RawData);
            var text = string.Format(CultureInfo.InvariantCulture,
                "{0} | 类型={1} | 值={2} | 质量={3} | 原始={4}",
                value.Address, value.DataType,
                FormatDisplayValue(value.Value),
                FormatQualityLabel(value.Quality),
                raw);
            if (!string.IsNullOrWhiteSpace(value.ErrorMessage))
                text += string.Format(CultureInfo.InvariantCulture, " | 错误={0}", value.ErrorMessage);
            return text;
        }

        public static string FormatDisplayValue(object value)
        {
            // Delegate to SDK's FormatValue for the common path,
            // then add Demo-specific display enhancements.
            if (value == null) return "<null>";
            if (value is float single) return single.ToString("R", CultureInfo.InvariantCulture);
            if (value is double @double) return @double.ToString("R", CultureInfo.InvariantCulture);
            if (value is char) return value.ToString();
            if (!(value is string))
            {
                // byte[] 已在 SDK FormatValueStatic 中处理为 BitConverter.ToString
                var sequence = value as System.Collections.IEnumerable;
                if (sequence != null)
                {
                    var items = new List<string>();
                    foreach (var item in sequence)
                        items.Add(Convert.ToString(item, CultureInfo.InvariantCulture));
                    return string.Join(", ", items);
                }
            }
            return IndustrialCommSdk.Storage.IndustrialDataRecord.FormatValueStatic(value) ?? string.Empty;
        }

        public static string FormatQualityLabel(QualityStatus quality)
        {
            switch (quality)
            {
                case QualityStatus.Good: return "正常 (Good)";
                case QualityStatus.Bad: return "失败 (Bad)";
                case QualityStatus.Stale: return "过期 (Stale)";
                default: return "未知 (Unknown)";
            }
        }

        public static string FormatQuality(QualityStatus quality)
        {
            switch (quality)
            {
                case QualityStatus.Good: return "正常(Good)";
                case QualityStatus.Bad: return "失败(Bad)";
                case QualityStatus.Stale: return "过期(Stale)";
                default: return "未知(Unknown)";
            }
        }

        public static Brush ResultBrush(string result)
        {
            if (string.Equals(result, "OK", StringComparison.OrdinalIgnoreCase)) return System.Windows.Media.Brushes.ForestGreen;
            if (string.Equals(result, "NG", StringComparison.OrdinalIgnoreCase)) return System.Windows.Media.Brushes.IndianRed;
            return System.Windows.Media.Brushes.DarkGoldenrod;
        }
    }

    internal static class ComboHelper
    {
        public static DataType GetSelectedDataType(ComboBox comboBox)
        {
            var item = comboBox.SelectedItem as ComboBoxItem;
            if (item == null) throw new InvalidOperationException("请先选择数据类型。");
            return (DataType)Enum.Parse(typeof(DataType), item.Content.ToString());
        }

        public static void SetEnabledDataTypes(ComboBox comboBox, IReadOnlyCollection<DataType> enabledTypes)
        {
            foreach (var item in comboBox.Items.OfType<ComboBoxItem>())
            {
                DataType parsedType;
                if (Enum.TryParse(item.Content.ToString(), out parsedType))
                    item.IsEnabled = enabledTypes.Contains(parsedType);
            }
        }

        public static void SelectDataType(ComboBox comboBox, DataType dataType)
        {
            foreach (var item in comboBox.Items.OfType<ComboBoxItem>())
            {
                DataType parsedType;
                if (Enum.TryParse(item.Content.ToString(), out parsedType) && parsedType == dataType)
                {
                    comboBox.SelectedItem = item;
                    return;
                }
            }
        }

        public static void SetIfNotEmpty(System.Windows.Controls.TextBox textBox, string value)
        {
            if (!string.IsNullOrWhiteSpace(value)) textBox.Text = value;
        }

        public static void SelectComboBoxByContent(ComboBox comboBox, string content)
        {
            if (string.IsNullOrWhiteSpace(content)) return;
            foreach (var item in comboBox.Items.OfType<ComboBoxItem>())
            {
                if (string.Equals(item.Content?.ToString(), content, StringComparison.Ordinal))
                {
                    comboBox.SelectedItem = item;
                    return;
                }
            }
        }

        public static void SelectComboBoxByTag(ComboBox comboBox, string tag)
        {
            if (string.IsNullOrWhiteSpace(tag)) return;
            foreach (var item in comboBox.Items.OfType<ComboBoxItem>())
            {
                if (string.Equals(item.Tag?.ToString(), tag, StringComparison.OrdinalIgnoreCase))
                {
                    comboBox.SelectedItem = item;
                    return;
                }
            }
        }

        public static void RefreshAddressHistory(ComboBox comboBox, IReadOnlyCollection<string> addresses)
        {
            comboBox.ItemsSource = null;
            comboBox.ItemsSource = addresses == null ? Array.Empty<string>() : addresses.ToArray();
            comboBox.SelectedIndex = -1;
        }

        public static void ApplyHistorySelection(ComboBox comboBox, System.Windows.Controls.TextBox addressTextBox)
        {
            var value = comboBox.SelectedItem as string;
            if (!string.IsNullOrWhiteSpace(value))
                addressTextBox.Text = value;
            comboBox.SelectedIndex = -1;
        }
    }

    internal static class AddressHistoryHelper
    {
        private const int MaxRecentAddressCount = 12;

        public static void RememberRecentAddresses(ICollection<string> target, IEnumerable<string> addresses)
        {
            if (target == null || addresses == null) return;
            foreach (var address in addresses)
                RememberRecentAddress(target, address);
        }

        public static void RememberRecentAddress(ICollection<string> target, string address)
        {
            var value = (address ?? string.Empty).Trim();
            if (value.Length == 0) return;
            var items = target as List<string>;
            if (items == null) return;
            items.RemoveAll(item => string.Equals(item, value, StringComparison.OrdinalIgnoreCase));
            items.Insert(0, value);
            if (items.Count > MaxRecentAddressCount)
                items.RemoveRange(MaxRecentAddressCount, items.Count - MaxRecentAddressCount);
        }
    }
}
