using System;
using System.IO.Ports;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using IndustrialCommDemo.Helpers;
using IndustrialCommSdk;
using IndustrialCommSdk.Abstractions;
using IndustrialCommSdk.Protocols.Modbus;

namespace IndustrialCommDemo.Views
{
    public partial class ModbusTab
    {
        public async Task ResetClientAsync()
        {
            var client = _client;
            _client = null;

            if (client == null) { UpdateStatus(); RefreshCapabilityText(); return; }

            try
            {
                if (!string.IsNullOrWhiteSpace(_subscriptionId))
                    await client.UnsubscribeAsync(_subscriptionId, CancellationToken.None);
            }
            catch { }
            finally { _subscriptionId = null; }

            if (client is ModbusRtuClient rtuClient)
                rtuClient.FrameTraced -= RtuClient_FrameTraced;

            try { await client.DisconnectAsync(CancellationToken.None); } catch { }
            client.Dispose();
            UpdateStatus();
            RefreshCapabilityText();
        }

        private void UpdateStatus()
        {
            if (_client == null) { StatusTextBlock.Text = "未连接"; StatusTextBlock.Foreground = Brushes.IndianRed; return; }
            if (_client.IsConnected) { StatusTextBlock.Text = "已连接"; StatusTextBlock.Foreground = Brushes.ForestGreen; return; }
            var health = _client.GetHealth();
            StatusTextBlock.Text = health?.Status.ToString() ?? "未连接";
            StatusTextBlock.Foreground = Brushes.DarkGoldenrod;
        }

        private void RefreshCapabilityText()
        {
            if (CapabilityTextBlock == null) return;
            var capabilities = _client == null
                ? ProtocolCapabilities.ForProtocol(GetSelectedProtocolKind())
                : IndustrialClientPlatformExtensions.GetCapabilities(_client);
            CapabilityTextBlock.Text = CapabilityDisplayHelper.Format(capabilities);
        }

        private ProtocolKind GetSelectedProtocolKind()
        {
            return ConnectionTypeComboBox != null && ConnectionTypeComboBox.SelectedIndex == 1
                ? ProtocolKind.ModbusRtu
                : ProtocolKind.ModbusTcp;
        }

        private bool EnsureConnected()
        {
            if (_client != null && _client.IsConnected) return true;
            MessageBox.Show("请先连接 Modbus 设备。", "连接", MessageBoxButton.OK, MessageBoxImage.Information);
            return false;
        }

        private string GetDeviceId() => ParseHelper.RequireText(DeviceIdTextBox.Text, "Modbus 设备 ID");

        private async void ConnectButton_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                await ResetClientAsync();
                var isRtu = ConnectionTypeComboBox.SelectedIndex == 1;

                if (isRtu)
                {
                    var options = new ModbusRtuClientOptions
                    {
                        DeviceId = ParseHelper.RequireText(DeviceIdTextBox.Text, "Modbus 设备 ID"),
                        PortName = ParseHelper.RequireText(PortNameComboBox.Text, "串口号"),
                        BaudRate = ParseHelper.ParseIntValue(BaudRateComboBox.Text, "波特率"),
                        DataBits = ParseHelper.ParseIntValue(DataBitsComboBox.Text, "数据位"),
                        Parity = (Parity)Enum.Parse(typeof(Parity), ((ComboBoxItem)ParityComboBox.SelectedItem).Tag.ToString()),
                        StopBits = (StopBits)Enum.Parse(typeof(StopBits), ((ComboBoxItem)StopBitsComboBox.SelectedItem).Tag.ToString()),
                        ReadTimeout = ParseHelper.ParseIntValue(RtuTimeoutTextBox.Text, "RTU 响应超时"),
                        WriteTimeout = ParseHelper.ParseIntValue(RtuTimeoutTextBox.Text, "RTU 发送超时"),
                        SlaveId = ParseHelper.ParseByteValue(SlaveIdTextBox.Text, "Modbus 从站 ID"),
                        DeviceProfile = _profile,
                    };
                    _client = new ModbusRtuClient(options, _ctx.SdkLogger);
                    ((ModbusRtuClient)_client).FrameTraced += RtuClient_FrameTraced;
                    await _client.ConnectAsync(CancellationToken.None);
                    UpdateStatus();
                    RefreshCapabilityText();
                    _ctx.SetHeaderStatus("Modbus RTU 已连接", Brushes.LightGreen);
                    _ctx.DemoLogger.Info(string.Format("Modbus RTU 已连接到 {0}。", options.PortName));
                }
                else
                {
                    var options = new ModbusTcpClientOptions
                    {
                        DeviceId = ParseHelper.RequireText(DeviceIdTextBox.Text, "Modbus 设备 ID"),
                        Host = ParseHelper.RequireText(HostTextBox.Text, "Modbus 主机"),
                        Port = ParseHelper.ParseIntValue(PortTextBox.Text, "Modbus 端口"),
                        SlaveId = ParseHelper.ParseByteValue(SlaveIdTextBox.Text, "Modbus 从站 ID"),
                        DeviceProfile = _profile,
                    };
                    _client = new ModbusTcpClient(options, _ctx.SdkLogger);
                    await _client.ConnectAsync(CancellationToken.None);
                    UpdateStatus();
                    RefreshCapabilityText();
                    _ctx.SetHeaderStatus("Modbus 已连接", Brushes.LightGreen);
                    _ctx.DemoLogger.Info(string.Format("Modbus 已连接到 {0}:{1}。", options.Host, options.Port));
                }
            }
            catch (Exception ex)
            {
                UpdateStatus();
                RefreshCapabilityText();
                _ctx.HandleError("Modbus 连接失败。", ex, true);
            }
        }

        private async void DisconnectButton_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                await ResetClientAsync();
                ResultTextBlock.Text = "已断开。";
                _subRows.Clear();
                UpdateStatus();
                RefreshCapabilityText();
                _ctx.SetHeaderStatus("Modbus 已断开", Brushes.Khaki);
                _ctx.DemoLogger.Info("Modbus 已断开。");
            }
            catch (Exception ex)
            {
                _ctx.HandleError("Modbus 断开失败。", ex, false);
            }
        }

        private void ConnectionTypeComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (HostLabel == null) return;
            var isRtu = ConnectionTypeComboBox.SelectedIndex == 1;
            var tcpVis = isRtu ? Visibility.Collapsed : Visibility.Visible;
            var rtuVis = isRtu ? Visibility.Visible : Visibility.Collapsed;
            RtuFramePanel.Visibility = rtuVis;
            HostLabel.Visibility = tcpVis; HostTextBox.Visibility = tcpVis;
            PortLabel.Visibility = tcpVis; PortTextBox.Visibility = tcpVis;
            PortNameLabel.Visibility = rtuVis; PortNameComboBox.Visibility = rtuVis;
            BaudRateLabel.Visibility = rtuVis; BaudRateComboBox.Visibility = rtuVis;
            DataBitsLabel.Visibility = rtuVis; DataBitsComboBox.Visibility = rtuVis;
            ParityLabel.Visibility = rtuVis; ParityComboBox.Visibility = rtuVis;
            StopBitsLabel.Visibility = rtuVis; StopBitsComboBox.Visibility = rtuVis;
            RtuTimeoutLabel.Visibility = rtuVis; RtuTimeoutTextBox.Visibility = rtuVis;
            RefreshProfileOptions();
            ApplyProfile();
            RefreshDataTypeState();
            RefreshCapabilityText();
            if (isRtu) RefreshSerialPorts();
        }

        private void RtuClient_FrameTraced(object sender, ModbusRtuFrameEventArgs e)
        {
            _ctx.RunOnUi(() =>
            {
                var text = e.Hex + (e.CrcValid ? "  [CRC OK]" : "  [CRC 错误]");
                if (e.Direction == ModbusRtuFrameDirection.Transmit)
                    RtuTxTextBox.Text = text;
                else
                    RtuRxTextBox.Text = text;
            });
        }

        private async void RtuRawSendButton_Click(object sender, RoutedEventArgs e)
        {
            var client = _client as ModbusRtuClient;
            if (client == null || !client.IsConnected)
            {
                MessageBox.Show("请先使用 Modbus RTU（串口）方式连接。", "RTU 调试", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }
            try
            {
                var frame = ModbusRtuFrameCodec.ParseHex(RtuRawRequestTextBox.Text);
                _ctx.DemoLogger.Info(string.Format("RTU 调试请求已解析：{0} 字节，自动 CRC={1}。", frame.Length, RtuAutoCrcCheckBox.IsChecked == true ? "是" : "否"));
                if (RtuAutoCrcCheckBox.IsChecked == true)
                {
                    frame = ModbusRtuFrameCodec.AppendCrc(frame);
                    _ctx.DemoLogger.Trace("RTU 调试请求已追加 CRC16（低字节在前）。");
                }
                var response = await client.TransceiveRawAsync(frame, CancellationToken.None);
                ResultTextBlock.Text = response.Length == 0
                    ? "广播报文发送完成（站号 0 不返回响应）。"
                    : "原始 RTU 报文收发完成，响应 CRC 正确。";
            }
            catch (IndustrialCommSdk.Exceptions.IndustrialTimeoutException ex)
            {
                RtuRxTextBox.Text = "接收超时 / 从站无响应";
                ResultTextBlock.Text = ex.Message;
                _ctx.HandleError("原始 RTU 接收超时。", ex, false);
            }
            catch (Exception ex)
            {
                _ctx.HandleError("原始 RTU 报文发送失败。", ex, true);
            }
        }

        private void RefreshSerialPorts()
        {
            var currentText = PortNameComboBox.Text;
            PortNameComboBox.Items.Clear();
            try
            {
                foreach (var port in SerialPort.GetPortNames())
                    PortNameComboBox.Items.Add(port);
            }
            catch { }

            if (!string.IsNullOrEmpty(currentText) && PortNameComboBox.Items.Contains(currentText))
                PortNameComboBox.Text = currentText;
            else if (PortNameComboBox.Items.Count > 0)
                PortNameComboBox.SelectedIndex = 0;
            else
                PortNameComboBox.Text = "COM1";
        }
    }
}
