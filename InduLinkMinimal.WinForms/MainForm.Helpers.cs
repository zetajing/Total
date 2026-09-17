using System;
using System.Globalization;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using InduLink;
using InduLink.Abstractions;
using InduLink.Mes;

namespace InduLinkMinimal.WinForms
{
    public partial class MainForm
    {
        private async void MesHttpSendButton_Click(object sender, EventArgs e)
        {
            await RunAsync(MesHttpOutputTextBox, async () =>
            {
                using var client = new MesHttpClient(new MesHttpClientOptions
                {
                    BaseUrl = MesHttpUrlTextBox.Text.Trim(),
                });

                var response = await client.SendJsonAsync(
                    MesHttpEndpointTextBox.Text.Trim(),
                    MesHttpJsonTextBox.Text,
                    CancellationToken.None);

                Append(MesHttpOutputTextBox, $"响应: status={response.StatusCode}, body={response.Body}");
            });
        }

        private static async Task RunAsync(TextBox output, Func<Task> action)
        {
            try
            {
                await action();
            }
            catch (Exception ex)
            {
                Append(output, $"错误: {ex.Message}");
            }
        }

        private static void Append(TextBox output, string text)
        {
            if (output.IsDisposed)
            {
                return;
            }

            if (output.InvokeRequired)
            {
                output.BeginInvoke(new Action<TextBox, string>(Append), output, text);
                return;
            }

            output.AppendText($"[{DateTime.Now:HH:mm:ss}] {text}{Environment.NewLine}");
        }

        private static void EnsureConnected(IInduLinkClient client)
        {
            if (client?.IsConnected != true)
            {
                throw new InvalidOperationException("请先连接。");
            }
        }

        private static DataType ParseDataType(string value)
        {
            return Enum.Parse<DataType>(value);
        }

        private static int ParsePort(string value)
        {
            var port = ParsePositive(value, "端口");
            if (port > 65535)
            {
                throw new ArgumentOutOfRangeException(nameof(value), "端口必须小于 65536。");
            }

            return port;
        }

        private static byte ParseSlaveId(string value)
        {
            var id = ParsePositive(value, "站号");
            if (id > 247)
            {
                throw new ArgumentOutOfRangeException(nameof(value), "站号必须在 1 到 247 之间。");
            }

            return (byte)id;
        }

        private static int ParsePositive(string value, string name)
        {
            if (!int.TryParse(value, out var result) || result <= 0)
            {
                throw new FormatException($"{name}必须是正整数。");
            }

            return result;
        }

        private static int ParseNonNegative(string value, string name)
        {
            if (!int.TryParse(value, out var result) || result < 0)
            {
                throw new FormatException($"{name}必须是非负整数。");
            }

            return result;
        }

        private static object ConvertValue(string value, DataType type)
        {
            return type switch
            {
                DataType.Bool => bool.Parse(value),
                DataType.Int16 => short.Parse(value, CultureInfo.InvariantCulture),
                DataType.UInt16 => ushort.Parse(value, CultureInfo.InvariantCulture),
                DataType.Int32 => int.Parse(value, CultureInfo.InvariantCulture),
                DataType.UInt32 => uint.Parse(value, CultureInfo.InvariantCulture),
                DataType.Float => float.Parse(value, CultureInfo.InvariantCulture),
                DataType.Double => double.Parse(value, CultureInfo.InvariantCulture),
                DataType.Byte => byte.Parse(value, CultureInfo.InvariantCulture),
                DataType.Char => char.Parse(value),
                DataType.ByteArray => Convert.FromBase64String(value),
                _ => value,
            };
        }
    }
}
