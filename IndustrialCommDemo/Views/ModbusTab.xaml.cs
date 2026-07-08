using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Globalization;
using System.IO.Ports;
using System.Linq;
using System.Text;
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
    /// <summary>演示 Modbus TCP/RTU 连接、单点读写、批量操作和轮询订阅。</summary>
    public partial class ModbusTab : UserControl
    {
        private static readonly DataType[] RegisterDataTypes =
        {
            DataType.Int16, DataType.UInt16, DataType.Int32, DataType.UInt32,
            DataType.Float, DataType.Double, DataType.String, DataType.ByteArray,
        };

        private DemoAppContext _ctx;
        private IIndustrialClient _client;
        private string _subscriptionId;
        private ModbusAddressParser _addressParser = new ModbusAddressParser(ModbusDeviceProfiles.InovanceEasyPlc);
        private IModbusDeviceProfile _profile = ModbusDeviceProfiles.InovanceEasyPlc;
        private readonly ObservableCollection<SubscriptionDisplayRow> _subRows = new ObservableCollection<SubscriptionDisplayRow>();

        public ModbusTab()
        {
            InitializeComponent();
            SubscriptionDataGrid.ItemsSource = _subRows;
        }

        public void Initialize(DemoAppContext ctx)
        {
            _ctx = ctx ?? throw new ArgumentNullException(nameof(ctx));
            ComboHelper.SelectDataType(DataTypeComboBox, DataType.Int16);
            RefreshProfileOptions();
            ApplyProfile();
            ApplySavedState();
            RefreshDataTypeState();
            RefreshInputHints();
            RefreshSerialPorts();
        }

        public async Task ResetClientAsync()
        {
            var client = _client;
            _client = null;

            if (client == null) { UpdateStatus(); return; }

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
        }

        private void UpdateStatus()
        {
            if (_client == null) { StatusTextBlock.Text = "未连接"; StatusTextBlock.Foreground = Brushes.IndianRed; return; }
            if (_client.IsConnected) { StatusTextBlock.Text = "已连接"; StatusTextBlock.Foreground = Brushes.ForestGreen; return; }
            var health = _client.GetHealth();
            StatusTextBlock.Text = health?.Status.ToString() ?? "未连接";
            StatusTextBlock.Foreground = Brushes.DarkGoldenrod;
        }

        private bool EnsureConnected()
        {
            if (_client != null && _client.IsConnected) return true;
            MessageBox.Show("请先连接 Modbus 设备。", "连接", MessageBoxButton.OK, MessageBoxImage.Information);
            return false;
        }

        private string GetDeviceId() => ParseHelper.RequireText(DeviceIdTextBox.Text, "Modbus 设备 ID");

        // ── Connect / Disconnect ──

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
                    _ctx.SetHeaderStatus("Modbus 已连接", Brushes.LightGreen);
                    _ctx.DemoLogger.Info(string.Format("Modbus 已连接到 {0}:{1}。", options.Host, options.Port));
                }
            }
            catch (Exception ex)
            {
                UpdateStatus();
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
                _ctx.SetHeaderStatus("Modbus 已断开", Brushes.Khaki);
                _ctx.DemoLogger.Info("Modbus 已断开。");
            }
            catch (Exception ex)
            {
                _ctx.HandleError("Modbus 断开失败。", ex, false);
            }
        }

        // ── Connection type switch ──

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
            if (isRtu) RefreshSerialPorts();
        }

        // ── RTU frame tracing ──

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

        // ── Serial ports ──

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

        // ── Read / Write / Subscribe ──

        private async void ReadButton_Click(object sender, RoutedEventArgs e)
        {
            if (!EnsureConnected()) return;
            try
            {
                var requests = BuildReadRequests();
                if (requests.Count == 1)
                {
                    var result = await _client.ReadAsync(requests[0], CancellationToken.None);
                    ResultTextBlock.Text = FormatHelper.FormatDataValue(result);
                    UpdateSubRows(new[] { result });
                    _ctx.QueueDatabaseValues(_client, new[] { result });
                }
                else
                {
                    var result = await _client.ReadManyAsync(requests, CancellationToken.None);
                    ResultTextBlock.Text = string.Format(CultureInfo.InvariantCulture,
                        "批量读取完成：共 {0} 个地址，成功 {1} 个，失败 {2} 个。",
                        result.Values.Count,
                        result.Values.Count(item => item.Quality == QualityStatus.Good),
                        result.Values.Count(item => item.Quality != QualityStatus.Good));
                    UpdateSubRows(result.Values);
                    _ctx.QueueDatabaseValues(_client, result.Values);
                }

                AddressHistoryHelper.RememberRecentAddresses(_ctx.UiState.Modbus.RecentAddresses, requests.Select(r => r.Address));
                ComboHelper.RefreshAddressHistory(AddressHistoryComboBox, _ctx.UiState.Modbus.RecentAddresses);
                UpdateStatus();
                _ctx.SetHeaderStatus("Modbus 读取完成", Brushes.LightGreen);
                _ctx.DemoLogger.Info("Modbus 读取完成。");
            }
            catch (Exception ex)
            {
                _ctx.HandleError("Modbus 读取失败。", ex, true);
            }
        }

        private async void WriteButton_Click(object sender, RoutedEventArgs e)
        {
            if (!EnsureConnected()) return;
            try
            {
                var requests = BuildWriteRequests();
                if (requests.Count == 1)
                {
                    await _client.WriteAsync(requests[0], CancellationToken.None);
                    ResultTextBlock.Text = string.Format("写入成功：{0} = {1}", requests[0].Address, FormatHelper.FormatDisplayValue(requests[0].Value));
                    _ctx.DemoLogger.Info(string.Format("Modbus 写入完成：{0}。", requests[0].Address));
                }
                else
                {
                    await _client.WriteManyAsync(requests, CancellationToken.None);
                    ResultTextBlock.Text = string.Format(CultureInfo.InvariantCulture,
                        "批量写入完成：共 {0} 个地址，模式={1}。",
                        requests.Count,
                        IsSingleWriteValueBroadcast(requests.Count) ? "单值广播" : "逐项写入");
                    _ctx.DemoLogger.Info(string.Format("Modbus 批量写入完成：{0} 个地址。", requests.Count));
                }

                AddressHistoryHelper.RememberRecentAddresses(_ctx.UiState.Modbus.RecentAddresses, requests.Select(r => r.Address));
                ComboHelper.RefreshAddressHistory(AddressHistoryComboBox, _ctx.UiState.Modbus.RecentAddresses);
                UpdateStatus();
                _ctx.SetHeaderStatus("Modbus 写入完成", Brushes.LightGreen);
            }
            catch (Exception ex)
            {
                _ctx.HandleError("Modbus 写入失败。", ex, true);
            }
        }

        private async void SubscribeButton_Click(object sender, RoutedEventArgs e)
        {
            if (!EnsureConnected()) return;
            try
            {
                if (!string.IsNullOrWhiteSpace(_subscriptionId))
                    await _client.UnsubscribeAsync(_subscriptionId, CancellationToken.None);

                var addresses = ValidateAndGetAddresses();
                var dataType = ComboHelper.GetSelectedDataType(DataTypeComboBox);
                var length = ParseHelper.ParseUShortValue(LengthTextBox.Text, "Modbus 长度");
                var items = addresses
                    .Select(address => new ReadRequest(GetDeviceId(), address, dataType, length))
                    .ToArray();

                var request = new SubscriptionRequest(
                    "modbus-sub-" + DateTime.Now.ToString("HHmmss", CultureInfo.InvariantCulture),
                    GetDeviceId(), items,
                    TimeSpan.FromMilliseconds(ParseHelper.ParseIntValue(PollIntervalTextBox.Text, "Modbus 轮询间隔")),
                    false);

                _subscriptionId = await _client.SubscribeAsync(request, OnSubscriptionReceived, CancellationToken.None);
                AddressHistoryHelper.RememberRecentAddresses(_ctx.UiState.Modbus.RecentAddresses, addresses);
                ComboHelper.RefreshAddressHistory(AddressHistoryComboBox, _ctx.UiState.Modbus.RecentAddresses);
                ResultTextBlock.Text = "订阅已启动：" + _subscriptionId;
                _ctx.SetHeaderStatus("Modbus 订阅已启动", Brushes.LightGreen);
                _ctx.DemoLogger.Info("Modbus 订阅已启动。");
            }
            catch (Exception ex)
            {
                _ctx.HandleError("Modbus 订阅失败。", ex, true);
            }
        }

        private async void UnsubscribeButton_Click(object sender, RoutedEventArgs e)
        {
            if (_client == null || string.IsNullOrWhiteSpace(_subscriptionId)) return;
            try
            {
                await _client.UnsubscribeAsync(_subscriptionId, CancellationToken.None);
                _ctx.DemoLogger.Info("Modbus 订阅已停止。");
            }
            catch (Exception ex)
            {
                _ctx.HandleError("Modbus 取消订阅失败。", ex, false);
            }
            finally
            {
                _subscriptionId = null;
                ResultTextBlock.Text = "订阅已停止。";
                _subRows.Clear();
                _ctx.SetHeaderStatus("Modbus 订阅已停止", Brushes.Khaki);
            }
        }

        private void OnSubscriptionReceived(object sender, SubscriptionEvent e)
        {
            _ctx.QueueDatabaseValues(_client, e.Values);
            _ctx.RunOnUi(() =>
            {
                ResultTextBlock.Text = string.Join(Environment.NewLine, e.Values.Select(FormatHelper.FormatDataValue).ToArray());
                UpdateSubRows(e.Values);
                UpdateStatus();
            });
        }

        // ── Profile / Model ──

        private void ModelComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (!IsLoaded) return;
            ApplyProfile();
            RefreshDataTypeState();
        }

        private void ApplyProfile()
        {
            _profile = GetSelectedProfile();
            _addressParser = new ModbusAddressParser(_profile);
            ExampleAddressTextBlock.Text = _profile.ExampleAddresses;
            if (string.IsNullOrWhiteSpace(AddressTextBox.Text))
                AddressTextBox.Text = _profile.DefaultAddress;
        }

        private IModbusDeviceProfile GetSelectedProfile()
        {
            var key = (ModelComboBox.SelectedItem as ComboBoxItem)?.Tag as string;
            if (string.IsNullOrWhiteSpace(key)) return ModbusDeviceProfiles.Generic;
            return ModbusDeviceProfiles.All.FirstOrDefault(p => string.Equals(p.Key, key, StringComparison.OrdinalIgnoreCase))
                ?? ModbusDeviceProfiles.Generic;
        }

        private void RefreshProfileOptions()
        {
            if (ModelComboBox == null) return;
            var selectedKey = (ModelComboBox.SelectedItem as ComboBoxItem)?.Tag as string;
            var isRtu = ConnectionTypeComboBox.SelectedIndex == 1;
            ModelComboBox.Items.Clear();
            if (isRtu)
            {
                ModelComboBox.Items.Add(new ComboBoxItem { Content = "通用 Modbus", Tag = ModbusDeviceProfiles.Generic.Key });
            }
            else
            {
                ModelComboBox.Items.Add(new ComboBoxItem { Content = "汇川 EasyPLC", Tag = ModbusDeviceProfiles.InovanceEasyPlc.Key });
                ModelComboBox.Items.Add(new ComboBoxItem { Content = "三菱 Modbus TCP", Tag = ModbusDeviceProfiles.MitsubishiModbusTcp.Key });
            }
            SelectModel(selectedKey);
        }

        private void SelectModel(string modelKey)
        {
            if (string.IsNullOrWhiteSpace(modelKey)) { ModelComboBox.SelectedIndex = 0; return; }
            foreach (var item in ModelComboBox.Items.OfType<ComboBoxItem>())
            {
                if (string.Equals(item.Tag as string, modelKey, StringComparison.OrdinalIgnoreCase))
                { ModelComboBox.SelectedItem = item; return; }
            }
            ModelComboBox.SelectedIndex = 0;
        }

        // ── Address / DataType events ──

        private void AddressTextBox_TextChanged(object sender, TextChangedEventArgs e) { if (!IsLoaded) return; RefreshDataTypeState(); RefreshInputHints(); }
        private void DataTypeComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e) { if (!IsLoaded) return; RefreshLengthSuggestion(); RefreshInputHints(); }
        private void WriteValueTextBox_TextChanged(object sender, TextChangedEventArgs e) { if (!IsLoaded) return; RefreshLengthSuggestion(); RefreshInputHints(); }
        private void AddressHistoryComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e) { ComboHelper.ApplyHistorySelection(AddressHistoryComboBox, AddressTextBox); }

        // ── DataType / length / hints ──

        private void RefreshDataTypeState()
        {
            var addresses = ParseHelper.SplitAddresses(AddressTextBox.Text);
            var isBitAddress = addresses.Count > 0 && addresses.All(IsModbusBitAddress);
            if (isBitAddress)
            {
                ComboHelper.SetEnabledDataTypes(DataTypeComboBox, new[] { DataType.Bool });
                ComboHelper.SelectDataType(DataTypeComboBox, DataType.Bool);
            }
            else
            {
                ComboHelper.SetEnabledDataTypes(DataTypeComboBox, RegisterDataTypes);
                if (ComboHelper.GetSelectedDataType(DataTypeComboBox) == DataType.Bool)
                    ComboHelper.SelectDataType(DataTypeComboBox, DataType.Int16);
            }
            RefreshLengthSuggestion();
        }

        private void RefreshInputHints()
        {
            var analysis = AnalyzeAddresses();
            if (!analysis.IsValid)
            {
                AddressHintTextBlock.Foreground = Brushes.OrangeRed;
                AddressHintTextBlock.Text = analysis.ErrorMessage;
            }
            else if (analysis.AddressCount == 0)
            {
                AddressHintTextBlock.Foreground = Brushes.DimGray;
                AddressHintTextBlock.Text = "支持逗号、分号、换行输入多个地址。";
            }
            else
            {
                AddressHintTextBlock.Foreground = Brushes.ForestGreen;
                var modeText = analysis.AddressCount == 1 ? "单地址" : string.Format(CultureInfo.InvariantCulture, "多地址（{0} 个）", analysis.AddressCount);
                AddressHintTextBlock.Text = string.Format(CultureInfo.InvariantCulture,
                    "{0}，{1}。{2}", modeText,
                    analysis.IsBitFamily ? "位地址模式，只允许 Bool 解析" : "寄存器地址模式，可选择值的解析方式",
                    analysis.AddressCount > 1 ? "读取、订阅、写入都支持批量。" : "可直接读取、写入或订阅。");
            }
            RefreshWriteHint(analysis);
        }

        private void RefreshWriteHint(ModbusAddressInputAnalysis analysis)
        {
            if (!analysis.IsValid || analysis.AddressCount == 0)
            {
                WriteHintTextBlock.Foreground = Brushes.DimGray;
                WriteHintTextBlock.Text = "多地址写入时，可填写 1 个值广播，或填写与地址数相同的多个值。";
                return;
            }
            if (analysis.AddressCount == 1)
            {
                WriteHintTextBlock.Foreground = Brushes.DimGray;
                WriteHintTextBlock.Text = "单地址写入沿用当前解析类型和长度。";
                return;
            }
            var values = ParseHelper.SplitBatchWriteValues(WriteValueTextBox.Text);
            if (values.Count <= 1)
            {
                WriteHintTextBlock.Foreground = Brushes.ForestGreen;
                WriteHintTextBlock.Text = string.Format(CultureInfo.InvariantCulture, "当前将把同一个值写到 {0} 个地址。", analysis.AddressCount);
            }
            else if (values.Count == analysis.AddressCount)
            {
                WriteHintTextBlock.Foreground = Brushes.ForestGreen;
                WriteHintTextBlock.Text = string.Format(CultureInfo.InvariantCulture, "当前将按顺序逐项写入 {0} 个地址。", analysis.AddressCount);
            }
            else
            {
                WriteHintTextBlock.Foreground = Brushes.OrangeRed;
                WriteHintTextBlock.Text = string.Format(CultureInfo.InvariantCulture,
                    "多地址写入时，写入值数量必须是 1 个或 {0} 个，当前为 {1} 个。", analysis.AddressCount, values.Count);
            }
        }

        private void RefreshLengthSuggestion()
        {
            if (IsModbusBitAddress(AddressTextBox.Text)) { LengthTextBox.Text = "1"; return; }
            try
            {
                var len = GetSuggestedLength(ComboHelper.GetSelectedDataType(DataTypeComboBox), WriteValueTextBox.Text);
                LengthTextBox.Text = len.ToString(CultureInfo.InvariantCulture);
            }
            catch { LengthTextBox.Text = "1"; }
        }

        private static ushort GetSuggestedLength(DataType dataType, string textValue)
        {
            // Byte 和 Char 在 SDK 的 GetRequiredRegisterLength 中已覆盖。
            return IndustrialCommSdk.Protocols.Common.RegisterValueCodec.GetRequiredRegisterLength(dataType, textValue);
        }

        // ── Request builders ──

        private List<ReadRequest> BuildReadRequests()
        {
            var addresses = ValidateAndGetAddresses();
            var deviceId = GetDeviceId();
            var dataType = ComboHelper.GetSelectedDataType(DataTypeComboBox);
            var length = ParseHelper.ParseUShortValue(LengthTextBox.Text, "Modbus 长度");
            return addresses.Select(a => new ReadRequest(deviceId, a, dataType, length)).ToList();
        }

        private List<WriteRequest> BuildWriteRequests()
        {
            var addresses = ValidateAndGetAddresses();
            var dataType = ComboHelper.GetSelectedDataType(DataTypeComboBox);
            var length = ParseHelper.ParseUShortValue(LengthTextBox.Text, "Modbus 长度");
            var deviceId = GetDeviceId();

            if (addresses.Count == 1)
            {
                return new List<WriteRequest>
                {
                    new WriteRequest(deviceId, addresses[0], dataType,
                        ParseHelper.ParseValue(WriteValueTextBox.Text, dataType, length), length)
                };
            }

            var values = ParseHelper.SplitBatchWriteValues(WriteValueTextBox.Text);
            if (values.Count == 0) throw new InvalidOperationException("Modbus 写入值不能为空。");
            if (values.Count != 1 && values.Count != addresses.Count)
                throw new InvalidOperationException(string.Format(CultureInfo.InvariantCulture,
                    "多地址写入时，写入值数量必须是 1 个或与地址数量一致（{0} 个）。", addresses.Count));

            if (values.Count == 1)
            {
                var parsed = ParseHelper.ParseValue(values[0], dataType, length);
                return addresses.Select(a => new WriteRequest(deviceId, a, dataType, parsed, length)).ToList();
            }

            return addresses
                .Zip(values, (a, vt) => new WriteRequest(deviceId, a, dataType, ParseHelper.ParseValue(vt, dataType, length), length))
                .ToList();
        }

        private bool IsSingleWriteValueBroadcast(int addressCount)
        {
            return addressCount > 1 && ParseHelper.SplitBatchWriteValues(WriteValueTextBox.Text).Count <= 1;
        }

        // ── Address parsing / validation ──

        // SplitAddresses 使用 ParseHelper.SplitAddresses（SharedHelpers.cs）

        private static bool IsModbusBitAddress(string address)
        {
            if (string.IsNullOrWhiteSpace(address)) return false;
            switch (char.ToUpperInvariant(address.Trim()[0]))
            {
                case 'X': case 'Y': case 'M': case 'S': case 'B': return true;
                default: return false;
            }
        }

        private List<string> ValidateAndGetAddresses()
        {
            var analysis = AnalyzeAddresses();
            if (!analysis.IsValid) throw new InvalidOperationException(analysis.ErrorMessage);
            return analysis.Addresses;
        }

        private ModbusAddressInputAnalysis AnalyzeAddresses()
        {
            var addresses = ParseHelper.SplitAddresses(AddressTextBox.Text);
            if (addresses.Count == 0) return ModbusAddressInputAnalysis.Empty();

            var parsedAreas = new List<ModbusArea>(addresses.Count);
            foreach (var address in addresses)
            {
                try
                {
                    var parsed = (ModbusAddress)_addressParser.Parse(address);
                    parsedAreas.Add(parsed.Area);
                }
                catch
                {
                    return ModbusAddressInputAnalysis.Invalid(addresses, "存在无法识别的 Modbus 地址，请检查前缀和编号。");
                }
            }

            var hasBit = parsedAreas.Any(a => a == ModbusArea.Coil || a == ModbusArea.DiscreteInput);
            var hasReg = parsedAreas.Any(a => !(a == ModbusArea.Coil || a == ModbusArea.DiscreteInput));
            if (hasBit && hasReg)
                return ModbusAddressInputAnalysis.Invalid(addresses, "多地址模式下，位地址和寄存器地址不能混用。");

            return ModbusAddressInputAnalysis.Valid(addresses, hasBit);
        }

        // ── Subscription rows ──

        private void UpdateSubRows(IReadOnlyList<DataValue> values)
        {
            if (values == null)
            {
                _subRows.Clear();
                return;
            }

            var ordered = values.OrderBy(v => v.Address, StringComparer.OrdinalIgnoreCase).ToList();

            // Remove rows whose address no longer appears in the new data
            for (var i = _subRows.Count - 1; i >= 0; i--)
            {
                if (!ordered.Any(v => v.Address.Equals(_subRows[i].Address, StringComparison.OrdinalIgnoreCase)))
                    _subRows.RemoveAt(i);
            }

            // Update existing rows and add new ones
            foreach (var v in ordered)
            {
                var existing = _subRows.FirstOrDefault(
                    r => r.Address.Equals(v.Address, StringComparison.OrdinalIgnoreCase));
                if (existing != null)
                {
                    existing.ValueText = FormatHelper.FormatDisplayValue(v.Value);
                    existing.QualityText = FormatHelper.FormatQualityLabel(v.Quality);
                    existing.TimestampText = v.Timestamp.ToLocalTime().ToString("HH:mm:ss.fff", CultureInfo.InvariantCulture);
                    existing.ErrorMessage = v.ErrorMessage ?? string.Empty;
                }
                else
                {
                    _subRows.Add(new SubscriptionDisplayRow
                    {
                        Address = v.Address,
                        DataType = v.DataType.ToString(),
                        ValueText = FormatHelper.FormatDisplayValue(v.Value),
                        QualityText = FormatHelper.FormatQualityLabel(v.Quality),
                        TimestampText = v.Timestamp.ToLocalTime().ToString("HH:mm:ss.fff", CultureInfo.InvariantCulture),
                        ErrorMessage = v.ErrorMessage ?? string.Empty,
                    });
                }
            }
        }

        // ── State ──

        public void SaveState()
        {
            _ctx.UiState.Modbus.DeviceId = DeviceIdTextBox.Text;
            _ctx.UiState.Modbus.Host = HostTextBox.Text;
            _ctx.UiState.Modbus.Port = PortTextBox.Text;
            _ctx.UiState.Modbus.SlaveId = SlaveIdTextBox.Text;
            _ctx.UiState.Modbus.ModelKey = _profile?.Key ?? ModbusDeviceProfiles.InovanceEasyPlc.Key;
            _ctx.UiState.Modbus.Address = AddressTextBox.Text;
            _ctx.UiState.Modbus.Length = LengthTextBox.Text;
            _ctx.UiState.Modbus.WriteValue = WriteValueTextBox.Text;
            _ctx.UiState.Modbus.PollInterval = PollIntervalTextBox.Text;
            _ctx.UiState.Modbus.ConnectionType = ConnectionTypeComboBox.SelectedIndex == 1 ? "Rtu" : "Tcp";
            _ctx.UiState.Modbus.PortName = PortNameComboBox.Text;
            _ctx.UiState.Modbus.BaudRate = BaudRateComboBox.Text;
            _ctx.UiState.Modbus.DataBits = DataBitsComboBox.Text;
            _ctx.UiState.Modbus.Parity = (ParityComboBox.SelectedItem as ComboBoxItem)?.Tag?.ToString();
            _ctx.UiState.Modbus.StopBits = (StopBitsComboBox.SelectedItem as ComboBoxItem)?.Tag?.ToString();
        }

        private void ApplySavedState()
        {
            var state = _ctx.UiState.Modbus ?? new Services.ModbusUiState();
            ComboHelper.SetIfNotEmpty(DeviceIdTextBox, state.DeviceId);
            ComboHelper.SetIfNotEmpty(HostTextBox, state.Host);
            ComboHelper.SetIfNotEmpty(PortTextBox, state.Port);
            ComboHelper.SetIfNotEmpty(SlaveIdTextBox, state.SlaveId);
            if (string.Equals(state.ConnectionType, "Rtu", StringComparison.OrdinalIgnoreCase))
                ConnectionTypeComboBox.SelectedIndex = 1;
            RefreshProfileOptions();
            SelectModel(state.ModelKey);
            ApplyProfile();
            ComboHelper.SetIfNotEmpty(AddressTextBox, state.Address);
            ComboHelper.SetIfNotEmpty(LengthTextBox, state.Length);
            ComboHelper.SetIfNotEmpty(WriteValueTextBox, state.WriteValue);
            ComboHelper.SetIfNotEmpty(PollIntervalTextBox, state.PollInterval);
            if (!string.IsNullOrEmpty(state.PortName)) PortNameComboBox.Text = state.PortName;
            if (!string.IsNullOrEmpty(state.BaudRate)) BaudRateComboBox.Text = state.BaudRate;
            ComboHelper.SelectComboBoxByContent(DataBitsComboBox, state.DataBits);
            ComboHelper.SelectComboBoxByTag(ParityComboBox, state.Parity);
            ComboHelper.SelectComboBoxByTag(StopBitsComboBox, state.StopBits);
        }
    }
}
