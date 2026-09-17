using System;
using System.Text;
using System.Threading;
using InduLink.Transport;

namespace InduLinkMinimal.WinForms
{
    public partial class MainForm
    {
        private async void RawTcpConnectButton_Click(object sender, EventArgs e)
        {
            await RunAsync(RawTcpOutputTextBox, async () =>
            {
                DisposeRawTcpClients();

                var options = new TcpTransportOptions
                {
                    Host = RawTcpHostTextBox.Text.Trim(),
                    Port = ParsePort(RawTcpPortTextBox.Text),
                    AutoReconnect = false,
                };

                if (RawTcpFramingComboBox.Text == "原始字节流")
                {
                    _rawTcpClient = new TcpTransportClient(options);
                    await _rawTcpClient.ConnectAsync(CancellationToken.None);
                }
                else
                {
                    _framedTcpClient = new FramedTcpClient(options, CreateSelectedFramer());
                    await _framedTcpClient.ConnectAsync(CancellationToken.None);
                }

                Append(RawTcpOutputTextBox, $"已连接，模式={RawTcpFramingComboBox.Text}");
            });
        }

        private async void RawTcpSendButton_Click(object sender, EventArgs e)
        {
            await RunAsync(RawTcpOutputTextBox, async () =>
            {
                var payload = RawTcpPayloadTextBox.Text;
                var bytes = Encoding.UTF8.GetBytes(payload);
                byte[] response;

                if (_framedTcpClient?.IsConnected == true)
                {
                    await _framedTcpClient.SendFrameAsync(bytes, CancellationToken.None);
                    response = await _framedTcpClient.ReceiveFrameAsync(CancellationToken.None);
                }
                else
                {
                    if (_rawTcpClient?.IsConnected != true)
                    {
                        throw new InvalidOperationException("请先连接。");
                    }

                    await _rawTcpClient.SendAsync(bytes, CancellationToken.None);
                    response = await _rawTcpClient.ReceiveAsync(4096, CancellationToken.None);
                }

                Append(RawTcpOutputTextBox, $"TX: {payload}");
                Append(RawTcpOutputTextBox, $"RX: {Encoding.UTF8.GetString(response)}");
            });
        }

        private async void RawTcpDisconnectButton_Click(object sender, EventArgs e)
        {
            await RunAsync(RawTcpOutputTextBox, async () =>
            {
                if (_rawTcpClient != null)
                {
                    await _rawTcpClient.DisconnectAsync(CancellationToken.None);
                }

                if (_framedTcpClient != null)
                {
                    await _framedTcpClient.DisconnectAsync(CancellationToken.None);
                }

                DisposeRawTcpClients();
                Append(RawTcpOutputTextBox, "已断开");
            });
        }

        private void DisposeRawTcpClients()
        {
            _rawTcpClient?.Dispose();
            _framedTcpClient?.Dispose();
            _rawTcpClient = null;
            _framedTcpClient = null;
        }

        private ITcpMessageFramer CreateSelectedFramer()
        {
            var maximum = ParsePositive(RawTcpMaximumFrameLengthTextBox.Text, "最大帧长");
            return RawTcpFramingComboBox.Text switch
            {
                "固定长度" => new FixedLengthMessageFramer(
                    ParsePositive(RawTcpFrameLengthTextBox.Text, "固定帧长")),
                "分隔符" => new DelimiterMessageFramer(
                    ParseEscapedBytes(RawTcpDelimiterTextBox.Text),
                    maximum),
                "2字节长度头" => new LengthPrefixMessageFramer(2, maximum),
                "4字节长度头" => new LengthPrefixMessageFramer(4, maximum),
                _ => throw new InvalidOperationException("请选择有效的分帧模式。"),
            };
        }

        private static byte[] ParseEscapedBytes(string text)
        {
            var value = (text ?? string.Empty)
                .Replace("\\r", "\r")
                .Replace("\\n", "\n")
                .Replace("\\0", "\0");

            if (value.Length == 0)
            {
                throw new FormatException("分隔符不能为空。");
            }

            return Encoding.UTF8.GetBytes(value);
        }
    }
}
