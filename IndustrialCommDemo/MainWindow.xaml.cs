using System;
using System.Collections;
using System.Collections.ObjectModel;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Net;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Microsoft.Win32;
using System.IO.Ports;
using IndustrialCommDemo.Services;
using IndustrialCommDemo.SocketDebug;
using IndustrialCommSdk;
using IndustrialCommSdk.Abstractions;
using IndustrialCommSdk.Protocols.Mc;
using IndustrialCommSdk.Protocols.Modbus;
using IndustrialCommSdk.Protocols.S7;
using IndustrialCommSdk.Storage;
using IndustrialCommSdk.Mes;
using CpuType = S7.Net.CpuType;

namespace IndustrialCommDemo
{
    public partial class MainWindow : Window
    {
        private static readonly DataType[] ModbusRegisterDataTypes =
        {
            DataType.Int16,
            DataType.UInt16,
            DataType.Int32,
            DataType.UInt32,
            DataType.Float,
            DataType.Double,
            DataType.String,
            DataType.ByteArray,
        };
        private const int MaxRecentAddressCount = 12;
        private const int MaxDatabaseHistoryRowCount = 500;
        private const int DatabaseHistoryQueryBatchSize = 200;
        private ModbusAddressParser _modbusAddressParser = new ModbusAddressParser(ModbusDeviceProfiles.InovanceEasyPlc);
        private IModbusDeviceProfile _modbusProfile = ModbusDeviceProfiles.InovanceEasyPlc;
        private readonly AppLogger _logger;
        private readonly AppLogger _sdkLogger;
        private UiStateStore _uiStateStore;
        private DemoUiState _uiState;
        private readonly ObservableCollection<SubscriptionDisplayRow> _modbusSubscriptionRows = new ObservableCollection<SubscriptionDisplayRow>();
        private readonly ObservableCollection<DatabaseHistoryDisplayRow> _databaseHistoryRows = new ObservableCollection<DatabaseHistoryDisplayRow>();
        private readonly ObservableCollection<DatabaseHistoryDisplayRow> _databaseLatestRows = new ObservableCollection<DatabaseHistoryDisplayRow>();
        private readonly ObservableCollection<DatabaseHistoryDisplayRow> _databaseQueryRows = new ObservableCollection<DatabaseHistoryDisplayRow>();

        private IIndustrialClient _modbusClient;
        private string _modbusSubscriptionId;
        private IIndustrialClient _s7Client;
        private IIndustrialClient _mcClient;
        private LineBasedTcpServer _socketServer;
        private LineBasedTcpClient _socketClient;
        private IMesClient _mesClient;
        // 数据库记录器仅在用户点击“测试并启用”且连接成功后存在。
        // 它内部使用后台队列，不会在 PLC 读取回调中直接执行 SQL。
        private BufferedIndustrialDataRecorder _databaseRecorder;
        private CancellationTokenSource _databaseHistoryCancellation;
        private Task _databaseHistoryTask;
        private SqlServerIndustrialDataStore _databaseManagementStore;
        private int _databaseQueryPage = 1;
        private long _databaseQueryTotal;

        // Modbus 轮询回调可能运行在非 UI 线程，因此不能通过读取 WPF CheckBox 判断状态。
        // 使用 Interlocked/Volatile 管理这个整数标志，可以安全地跨线程读写启用状态。
        private int _databaseRecordingEnabled;

        public MainWindow()
        {
            InitializeComponent();

            _logger = new AppLogger(Dispatcher, AppendLogBatch, "DEMO");
            _sdkLogger = new AppLogger(Dispatcher, AppendSdkLogBatch, "SDK");
            _uiStateStore = new UiStateStore();
            _uiState = _uiStateStore.Load();
            DataDirectoryTextBox.Text = LogHelper.StoragePathProvider.DataRoot;
            DataDirectoryHintTextBlock.Text = "当前目录：" + LogHelper.StoragePathProvider.DataRoot;

            SelectDataType(ModbusDataTypeComboBox, DataType.Int16);
            RefreshModbusProfileOptions();
            ApplyModbusProfile();
            InitializeDatabaseManagementControls();
            LoadUiState();
            RefreshModbusDataTypeState();
            RefreshModbusInputHints();
            RefreshS7AddressInputState();
            UpdateAllStatuses();
            RefreshAddressHistoryCombos();
            ModbusSubscriptionDataGrid.ItemsSource = _modbusSubscriptionRows;
            DatabaseHistoryDataGrid.ItemsSource = _databaseHistoryRows;
            DatabaseLatestDataGrid.ItemsSource = _databaseLatestRows;
            DatabaseQueryDataGrid.ItemsSource = _databaseQueryRows;

            SetHeaderStatus("就绪", Brushes.LightGreen);
            _logger.Info("工业通讯演示已就绪。");
        }

        private async void ModbusConnectButton_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                await ResetModbusClientAsync();

                var isRtu = ModbusConnectionTypeComboBox.SelectedIndex == 1;

                if (isRtu)
                {
                    var options = new ModbusRtuClientOptions
                    {
                        DeviceId = RequireText(ModbusDeviceIdTextBox.Text, "Modbus 设备 ID"),
                        PortName = RequireText(ModbusPortNameComboBox.Text, "串口号"),
                        BaudRate = ParseIntValue(ModbusBaudRateComboBox.Text, "波特率"),
                        DataBits = ParseIntValue(ModbusDataBitsComboBox.Text, "数据位"),
                        Parity = (Parity)Enum.Parse(typeof(Parity), ((ComboBoxItem)ModbusParityComboBox.SelectedItem).Tag.ToString()),
                        StopBits = (StopBits)Enum.Parse(typeof(StopBits), ((ComboBoxItem)ModbusStopBitsComboBox.SelectedItem).Tag.ToString()),
                        ReadTimeout = ParseIntValue(ModbusRtuTimeoutTextBox.Text, "RTU 响应超时"),
                        WriteTimeout = ParseIntValue(ModbusRtuTimeoutTextBox.Text, "RTU 发送超时"),
                        SlaveId = ParseByteValue(ModbusSlaveIdTextBox.Text, "Modbus 从站 ID"),
                        DeviceProfile = _modbusProfile,
                    };

                    _modbusClient = IndustrialClientFactory.CreateModbusRtu(options, _sdkLogger);
                    ((ModbusRtuClient)_modbusClient).FrameTraced += ModbusRtuClient_FrameTraced;
                    await _modbusClient.ConnectAsync(CancellationToken.None);

                    UpdateModbusStatus();
                    SetHeaderStatus("Modbus RTU 已连接", Brushes.LightGreen);
                    _logger.Info(string.Format("Modbus RTU 已连接到 {0}。", options.PortName));
                }
                else
                {
                    var options = new ModbusTcpClientOptions
                    {
                        DeviceId = RequireText(ModbusDeviceIdTextBox.Text, "Modbus 设备 ID"),
                        Host = RequireText(ModbusHostTextBox.Text, "Modbus 主机"),
                        Port = ParseIntValue(ModbusPortTextBox.Text, "Modbus 端口"),
                        SlaveId = ParseByteValue(ModbusSlaveIdTextBox.Text, "Modbus 从站 ID"),
                        DeviceProfile = _modbusProfile,
                    };

                    _modbusClient = IndustrialClientFactory.CreateModbus(options, _sdkLogger);
                    await _modbusClient.ConnectAsync(CancellationToken.None);

                    UpdateModbusStatus();
                    SetHeaderStatus("Modbus 已连接", Brushes.LightGreen);
                    _logger.Info(string.Format("Modbus 已连接到 {0}:{1}。", options.Host, options.Port));
                }
            }
            catch (Exception ex)
            {
                UpdateModbusStatus();
                HandleActionError("Modbus 连接失败。", ex, true);
            }
        }

        private async void ModbusDisconnectButton_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                await ResetModbusClientAsync();
                ModbusResultTextBlock.Text = "已断开。";
                _modbusSubscriptionRows.Clear();
                UpdateModbusStatus();
                SetHeaderStatus("Modbus 已断开", Brushes.Khaki);
                _logger.Info("Modbus 已断开。");
            }
            catch (Exception ex)
            {
                HandleActionError("Modbus 断开失败。", ex, false);
            }
        }

        private void ModbusConnectionTypeComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            // InitializeComponent 期间 SelectedIndex="0" 会触发此事件，
            // 此时其他命名控件可能尚未创建，需要提前返回避免空引用。
            if (ModbusHostLabel == null) return;

            var isRtu = ModbusConnectionTypeComboBox.SelectedIndex == 1;
            var tcpVisibility = isRtu ? Visibility.Collapsed : Visibility.Visible;
            var rtuVisibility = isRtu ? Visibility.Visible : Visibility.Collapsed;
            ModbusRtuFramePanel.Visibility = rtuVisibility;

            // TCP 控件
            ModbusHostLabel.Visibility = tcpVisibility;
            ModbusHostTextBox.Visibility = tcpVisibility;
            ModbusPortLabel.Visibility = tcpVisibility;
            ModbusPortTextBox.Visibility = tcpVisibility;

            // RTU 控件
            ModbusPortNameLabel.Visibility = rtuVisibility;
            ModbusPortNameComboBox.Visibility = rtuVisibility;
            ModbusBaudRateLabel.Visibility = rtuVisibility;
            ModbusBaudRateComboBox.Visibility = rtuVisibility;
            ModbusDataBitsLabel.Visibility = rtuVisibility;
            ModbusDataBitsComboBox.Visibility = rtuVisibility;
            ModbusParityLabel.Visibility = rtuVisibility;
            ModbusParityComboBox.Visibility = rtuVisibility;
            ModbusStopBitsLabel.Visibility = rtuVisibility;
            ModbusStopBitsComboBox.Visibility = rtuVisibility;
            ModbusRtuTimeoutLabel.Visibility = rtuVisibility;
            ModbusRtuTimeoutTextBox.Visibility = rtuVisibility;

            RefreshModbusProfileOptions();
            ApplyModbusProfile();
            RefreshModbusDataTypeState();

            // 切换到 RTU 时刷新可用串口列表
            if (isRtu)
            {
                RefreshModbusSerialPorts();
            }
        }

        private void ModbusRtuClient_FrameTraced(object sender, ModbusRtuFrameEventArgs e)
        {
            Dispatcher.BeginInvoke(new Action(() =>
            {
                var text = e.Hex + (e.CrcValid ? "  [CRC OK]" : "  [CRC 错误]");
                if (e.Direction == ModbusRtuFrameDirection.Transmit)
                    ModbusRtuTxTextBox.Text = text;
                else
                    ModbusRtuRxTextBox.Text = text;
            }));
        }

        private async void ModbusRtuRawSendButton_Click(object sender, RoutedEventArgs e)
        {
            var client = _modbusClient as ModbusRtuClient;
            if (client == null || !client.IsConnected)
            {
                MessageBox.Show(this, "请先使用 Modbus RTU（串口）方式连接。", "RTU 调试", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            try
            {
                var frame = ModbusRtuFrameCodec.ParseHex(ModbusRtuRawRequestTextBox.Text);
                _logger.Info(string.Format("RTU 调试请求已解析：{0} 字节，自动 CRC={1}。", frame.Length, ModbusRtuAutoCrcCheckBox.IsChecked == true ? "是" : "否"));
                if (ModbusRtuAutoCrcCheckBox.IsChecked == true)
                {
                    frame = ModbusRtuFrameCodec.AppendCrc(frame);
                    _logger.Trace("RTU 调试请求已追加 CRC16（低字节在前）。");
                }
                var response = await client.TransceiveRawAsync(frame, CancellationToken.None);
                ModbusResultTextBlock.Text = response.Length == 0
                    ? "广播报文发送完成（站号 0 不返回响应）。"
                    : "原始 RTU 报文收发完成，响应 CRC 正确。";
            }
            catch (IndustrialCommSdk.Exceptions.IndustrialTimeoutException ex)
            {
                ModbusRtuRxTextBox.Text = "接收超时 / 从站无响应";
                ModbusResultTextBlock.Text = ex.Message;
                HandleActionError("原始 RTU 接收超时。", ex, false);
            }
            catch (Exception ex)
            {
                HandleActionError("原始 RTU 报文发送失败。", ex, true);
            }
        }

        private void RefreshModbusSerialPorts()
        {
            var currentText = ModbusPortNameComboBox.Text;
            ModbusPortNameComboBox.Items.Clear();
            try
            {
                foreach (var port in SerialPort.GetPortNames())
                {
                    ModbusPortNameComboBox.Items.Add(port);
                }
            }
            catch
            {
                // GetPortNames 在某些环境下可能失败，忽略
            }

            // 恢复之前的选择或默认值
            if (!string.IsNullOrEmpty(currentText) && ModbusPortNameComboBox.Items.Contains(currentText))
            {
                ModbusPortNameComboBox.Text = currentText;
            }
            else if (ModbusPortNameComboBox.Items.Count > 0)
            {
                ModbusPortNameComboBox.SelectedIndex = 0;
            }
            else
            {
                ModbusPortNameComboBox.Text = "COM1";
            }
        }

        private async void ModbusReadButton_Click(object sender, RoutedEventArgs e)
        {
            if (!EnsureClientConnected(_modbusClient, "Modbus 设备"))
            {
                return;
            }

            try
            {
                var requests = BuildModbusReadRequests();
                if (requests.Count == 1)
                {
                    var result = await _modbusClient.ReadAsync(requests[0], CancellationToken.None);
                    ModbusResultTextBlock.Text = FormatDataValue(result);
                    UpdateSubscriptionRows(_modbusSubscriptionRows, new[] { result });
                    // 手动读取和轮询读取走同一条后台写库通道；数据库未启用时该方法会立即返回。
                    QueueDatabaseValues(_modbusClient, new[] { result });
                }
                else
                {
                    var result = await _modbusClient.ReadManyAsync(requests, CancellationToken.None);
                    ModbusResultTextBlock.Text = string.Format(
                        CultureInfo.InvariantCulture,
                        "批量读取完成：共 {0} 个地址，成功 {1} 个，失败 {2} 个。",
                        result.Values.Count,
                        result.Values.Count(item => item.Quality == QualityStatus.Good),
                        result.Values.Count(item => item.Quality != QualityStatus.Good));
                    UpdateSubscriptionRows(_modbusSubscriptionRows, result.Values);
                    QueueDatabaseValues(_modbusClient, result.Values);
                }

                RememberRecentAddresses(_uiState.Modbus.RecentAddresses, requests.Select(item => item.Address));
                RefreshAddressHistory(ModbusAddressHistoryComboBox, _uiState.Modbus.RecentAddresses);
                UpdateModbusStatus();
                SetHeaderStatus("Modbus 读取完成", Brushes.LightGreen);
                _logger.Info("Modbus 读取完成。");
            }
            catch (Exception ex)
            {
                HandleActionError("Modbus 读取失败。", ex, true);
            }
        }

        private async void ModbusWriteButton_Click(object sender, RoutedEventArgs e)
        {
            if (!EnsureClientConnected(_modbusClient, "Modbus 设备"))
            {
                return;
            }

            try
            {
                var requests = BuildModbusWriteRequests();
                if (requests.Count == 1)
                {
                    await _modbusClient.WriteAsync(requests[0], CancellationToken.None);
                    ModbusResultTextBlock.Text = string.Format("写入成功：{0} = {1}", requests[0].Address, FormatDisplayValue(requests[0].Value));
                    _logger.Info(string.Format("Modbus 写入完成：{0}。", requests[0].Address));
                }
                else
                {
                    await _modbusClient.WriteManyAsync(requests, CancellationToken.None);
                    ModbusResultTextBlock.Text = string.Format(
                        CultureInfo.InvariantCulture,
                        "批量写入完成：共 {0} 个地址，模式={1}。",
                        requests.Count,
                        IsSingleWriteValueBroadcast(requests.Count) ? "单值广播" : "逐项写入");
                    _logger.Info(string.Format("Modbus 批量写入完成：{0} 个地址。", requests.Count));
                }

                RememberRecentAddresses(_uiState.Modbus.RecentAddresses, requests.Select(item => item.Address));
                RefreshAddressHistory(ModbusAddressHistoryComboBox, _uiState.Modbus.RecentAddresses);
                UpdateModbusStatus();
                SetHeaderStatus("Modbus 写入完成", Brushes.LightGreen);
            }
            catch (Exception ex)
            {
                HandleActionError("Modbus 写入失败。", ex, true);
            }
        }

        private async void ModbusSubscribeButton_Click(object sender, RoutedEventArgs e)
        {
            if (!EnsureClientConnected(_modbusClient, "Modbus 设备"))
            {
                return;
            }

            try
            {
                if (!string.IsNullOrWhiteSpace(_modbusSubscriptionId))
                {
                    await _modbusClient.UnsubscribeAsync(_modbusSubscriptionId, CancellationToken.None);
                }

                var addresses = ValidateAndGetModbusAddresses();
                var dataType = GetSelectedDataType(ModbusDataTypeComboBox);
                var length = ParseUShortValue(ModbusLengthTextBox.Text, "Modbus 长度");
                var items = addresses
                    .Select(address => new ReadRequest(GetModbusDeviceId(), address, dataType, length))
                    .ToArray();

                var request = new SubscriptionRequest(
                    "modbus-sub-" + DateTime.Now.ToString("HHmmss", CultureInfo.InvariantCulture),
                    GetModbusDeviceId(),
                    items,
                    TimeSpan.FromMilliseconds(ParseIntValue(ModbusPollIntervalTextBox.Text, "Modbus 轮询间隔")),
                    false);

                _modbusSubscriptionId = await _modbusClient.SubscribeAsync(request, OnModbusSubscriptionReceived, CancellationToken.None);
                RememberRecentAddresses(_uiState.Modbus.RecentAddresses, addresses);
                RefreshAddressHistory(ModbusAddressHistoryComboBox, _uiState.Modbus.RecentAddresses);
                ModbusResultTextBlock.Text = "订阅已启动：" + _modbusSubscriptionId;
                SetHeaderStatus("Modbus 订阅已启动", Brushes.LightGreen);
                _logger.Info("Modbus 订阅已启动。");
            }
            catch (Exception ex)
            {
                HandleActionError("Modbus 订阅失败。", ex, true);
            }
        }

        private async void ModbusUnsubscribeButton_Click(object sender, RoutedEventArgs e)
        {
            if (_modbusClient == null || string.IsNullOrWhiteSpace(_modbusSubscriptionId))
            {
                return;
            }

            try
            {
                await _modbusClient.UnsubscribeAsync(_modbusSubscriptionId, CancellationToken.None);
                _logger.Info("Modbus 订阅已停止。");
            }
            catch (Exception ex)
            {
                HandleActionError("Modbus 取消订阅失败。", ex, false);
            }
            finally
            {
                _modbusSubscriptionId = null;
                ModbusResultTextBlock.Text = "订阅已停止。";
                _modbusSubscriptionRows.Clear();
                SetHeaderStatus("Modbus 订阅已停止", Brushes.Khaki);
            }
        }

        private void ModbusModelComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (!IsLoaded)
            {
                return;
            }

            ApplyModbusProfile();
            RefreshModbusDataTypeState();
        }

        private void ModbusAddressTextBox_TextChanged(object sender, TextChangedEventArgs e)
        {
            if (!IsLoaded)
            {
                return;
            }

            RefreshModbusDataTypeState();
            RefreshModbusInputHints();
        }

        private void ModbusDataTypeComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (!IsLoaded)
            {
                return;
            }

            RefreshModbusLengthSuggestion();
            RefreshModbusInputHints();
        }

        private void ModbusWriteValueTextBox_TextChanged(object sender, TextChangedEventArgs e)
        {
            if (!IsLoaded)
            {
                return;
            }

            RefreshModbusLengthSuggestion();
            RefreshModbusInputHints();
        }

        private void S7AddressTextBox_TextChanged(object sender, TextChangedEventArgs e)
        {
            if (!IsLoaded)
            {
                return;
            }

            RefreshS7AddressInputState();
        }

        private void S7DataTypeComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (!IsLoaded)
            {
                return;
            }

            RefreshS7LengthSuggestion();
        }

        private void ModbusAddressHistoryComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            ApplyHistorySelection(ModbusAddressHistoryComboBox, ModbusAddressTextBox);
        }

        private async void SocketStartServerButton_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                await ResetSocketServerAsync();

                _socketServer = new LineBasedTcpServer();
                _socketServer.ClientConnected += SocketServer_ClientConnected;
                _socketServer.ClientDisconnected += SocketServer_ClientDisconnected;
                _socketServer.MessageReceived += SocketServer_MessageReceived;

                var listenAddress = ParseListenAddress(SocketServerIpTextBox.Text);
                var port = ParseIntValue(SocketServerPortTextBox.Text, "Socket 服务端端口");
                await _socketServer.StartAsync(listenAddress, port, CancellationToken.None);

                UpdateSocketServerStatus();
                SetHeaderStatus("Socket 服务端已启动", Brushes.LightGreen);
                _logger.Info(string.Format("Socket 服务端已在 {0}:{1} 启动。", listenAddress, port));
            }
            catch (Exception ex)
            {
                UpdateSocketServerStatus();
                HandleActionError("Socket 服务端启动失败。", ex, true);
            }
        }

        private async void SocketStopServerButton_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                await ResetSocketServerAsync();
                SetHeaderStatus("Socket 服务端已停止", Brushes.Khaki);
                _logger.Info("Socket 服务端已停止。");
            }
            catch (Exception ex)
            {
                HandleActionError("Socket 服务端停止失败。", ex, false);
            }
        }

        private async void SocketConnectClientButton_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                await ResetSocketClientAsync();

                _socketClient = new LineBasedTcpClient();
                _socketClient.Connected += SocketClient_Connected;
                _socketClient.Disconnected += SocketClient_Disconnected;
                _socketClient.MessageReceived += SocketClient_MessageReceived;

                var host = RequireText(SocketClientHostTextBox.Text, "Socket 服务端主机");
                var port = ParseIntValue(SocketClientPortTextBox.Text, "Socket 服务端端口");
                await _socketClient.ConnectAsync(host, port, CancellationToken.None);

                UpdateSocketClientStatus();
                SetHeaderStatus("Socket 客户端已连接", Brushes.LightGreen);
                _logger.Info(string.Format("Socket 客户端已连接到 {0}:{1}。", host, port));
            }
            catch (Exception ex)
            {
                UpdateSocketClientStatus();
                HandleActionError("Socket 客户端连接失败。", ex, true);
            }
        }

        private async void SocketDisconnectClientButton_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                await ResetSocketClientAsync();
                SetHeaderStatus("Socket 客户端已断开", Brushes.Khaki);
                _logger.Info("Socket 客户端已断开。");
            }
            catch (Exception ex)
            {
                HandleActionError("Socket 客户端断开失败。", ex, false);
            }
        }

        private async void SocketSendFromServerButton_Click(object sender, RoutedEventArgs e)
        {
            if (_socketServer == null || !_socketServer.IsRunning)
            {
                MessageBox.Show(this, "请先启动 Socket 服务端。", "Socket", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            try
            {
                var message = SocketServerMessageTextBox.Text ?? string.Empty;
                await _socketServer.BroadcastAsync(message, CancellationToken.None);
                SetHeaderStatus("Socket 服务端广播已发送", Brushes.LightGreen);
                _logger.Info("Socket 服务端广播已发送。");
            }
            catch (Exception ex)
            {
                HandleActionError("Socket 服务端发送失败。", ex, true);
            }
        }

        private async void SocketSendFromClientButton_Click(object sender, RoutedEventArgs e)
        {
            if (_socketClient == null || !_socketClient.IsConnected)
            {
                MessageBox.Show(this, "请先连接 Socket 客户端。", "Socket", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            try
            {
                var message = SocketClientMessageTextBox.Text ?? string.Empty;
                await _socketClient.SendAsync(message, CancellationToken.None);
                SetHeaderStatus("Socket 客户端消息已发送", Brushes.LightGreen);
                _logger.Info("Socket 客户端消息已发送。");
            }
            catch (Exception ex)
            {
                HandleActionError("Socket 客户端发送失败。", ex, true);
            }
        }

        private async void S7ConnectButton_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                await ResetS7ClientAsync();

                var options = new SiemensS7ClientOptions
                {
                    DeviceId = RequireText(S7DeviceIdTextBox.Text, "S7 设备 ID"),
                    Host = RequireText(S7HostTextBox.Text, "S7 主机"),
                    Rack = ParseShortValue(S7RackTextBox.Text, "S7 机架"),
                    Slot = ParseShortValue(S7SlotTextBox.Text, "S7 槽位"),
                    CpuType = CpuType.S71200,
                };

                _s7Client = IndustrialClientFactory.CreateSiemensS7(options, _sdkLogger);
                await _s7Client.ConnectAsync(CancellationToken.None);

                UpdateS7Status();
                SetHeaderStatus("S7 已连接", Brushes.LightGreen);
                _logger.Info(string.Format("S7 已连接到 {0}。", options.Host));
            }
            catch (Exception ex)
            {
                UpdateS7Status();
                HandleActionError("S7 连接失败。", ex, true);
            }
        }

        private async void S7DisconnectButton_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                await ResetS7ClientAsync();
                S7ResultTextBlock.Text = "已断开。";
                UpdateS7Status();
                SetHeaderStatus("S7 已断开", Brushes.Khaki);
                _logger.Info("S7 已断开。");
            }
            catch (Exception ex)
            {
                HandleActionError("S7 断开失败。", ex, false);
            }
        }

        private async void S7ReadButton_Click(object sender, RoutedEventArgs e)
        {
            if (!EnsureClientConnected(_s7Client, "S7 设备"))
            {
                return;
            }

            try
            {
                var result = await _s7Client.ReadAsync(
                    BuildS7ReadRequest(),
                    CancellationToken.None);

                S7ResultTextBlock.Text = FormatDataValue(result);
                QueueDatabaseValues(_s7Client, new[] { result });
                RememberRecentAddress(_uiState.S7.RecentAddresses, S7AddressTextBox.Text);
                RefreshAddressHistory(S7AddressHistoryComboBox, _uiState.S7.RecentAddresses);
                SetHeaderStatus("S7 读取完成", Brushes.LightGreen);
                _logger.Info("S7 读取完成。");
            }
            catch (Exception ex)
            {
                HandleActionError("S7 读取失败。", ex, true);
            }
        }

        private async void S7WriteButton_Click(object sender, RoutedEventArgs e)
        {
            if (!EnsureClientConnected(_s7Client, "S7 设备"))
            {
                return;
            }

            try
            {
                var request = BuildS7WriteRequest();
                await _s7Client.WriteAsync(request, CancellationToken.None);
                RememberRecentAddress(_uiState.S7.RecentAddresses, request.Address);
                RefreshAddressHistory(S7AddressHistoryComboBox, _uiState.S7.RecentAddresses);
                S7ResultTextBlock.Text = string.Format("写入成功：{0} = {1}", request.Address, FormatDisplayValue(request.Value));
                SetHeaderStatus("S7 写入完成", Brushes.LightGreen);
                _logger.Info("S7 写入完成。");
            }
            catch (Exception ex)
            {
                HandleActionError("S7 写入失败。", ex, true);
            }
        }

        private async void McConnectButton_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                await ResetMcClientAsync();

                var options = new MitsubishiMcClientOptions
                {
                    DeviceId = RequireText(McDeviceIdTextBox.Text, "MC 设备 ID"),
                    Host = RequireText(McHostTextBox.Text, "MC 主机"),
                    Port = ParseIntValue(McPortTextBox.Text, "MC 端口"),
                };

                _mcClient = IndustrialClientFactory.CreateMitsubishiMc(options, _sdkLogger);
                await _mcClient.ConnectAsync(CancellationToken.None);

                UpdateMcStatus();
                SetHeaderStatus("MC 已连接", Brushes.LightGreen);
                _logger.Info(string.Format("MC 已连接到 {0}:{1}。", options.Host, options.Port));
            }
            catch (Exception ex)
            {
                UpdateMcStatus();
                HandleActionError("MC 连接失败。", ex, true);
            }
        }

        private async void McDisconnectButton_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                await ResetMcClientAsync();
                McResultTextBlock.Text = "已断开。";
                UpdateMcStatus();
                SetHeaderStatus("MC 已断开", Brushes.Khaki);
                _logger.Info("MC 已断开。");
            }
            catch (Exception ex)
            {
                HandleActionError("MC 断开失败。", ex, false);
            }
        }

        private async void McReadButton_Click(object sender, RoutedEventArgs e)
        {
            if (!EnsureClientConnected(_mcClient, "MC 设备"))
            {
                return;
            }

            try
            {
                var result = await _mcClient.ReadAsync(
                    BuildReadRequest(McDeviceIdTextBox, McAddressTextBox, McDataTypeComboBox, McLengthTextBox),
                    CancellationToken.None);

                McResultTextBlock.Text = FormatDataValue(result);
                QueueDatabaseValues(_mcClient, new[] { result });
                RememberRecentAddress(_uiState.Mc.RecentAddresses, McAddressTextBox.Text);
                RefreshAddressHistory(McAddressHistoryComboBox, _uiState.Mc.RecentAddresses);
                SetHeaderStatus("MC 读取完成", Brushes.LightGreen);
                _logger.Info("MC 读取完成。");
            }
            catch (Exception ex)
            {
                HandleActionError("MC 读取失败。", ex, true);
            }
        }

        private async void McWriteButton_Click(object sender, RoutedEventArgs e)
        {
            if (!EnsureClientConnected(_mcClient, "MC 设备"))
            {
                return;
            }

            try
            {
                var request = BuildWriteRequest(McDeviceIdTextBox, McAddressTextBox, McDataTypeComboBox, McLengthTextBox, McWriteValueTextBox);
                await _mcClient.WriteAsync(request, CancellationToken.None);
                RememberRecentAddress(_uiState.Mc.RecentAddresses, request.Address);
                RefreshAddressHistory(McAddressHistoryComboBox, _uiState.Mc.RecentAddresses);
                McResultTextBlock.Text = string.Format("写入成功：{0} = {1}", request.Address, FormatDisplayValue(request.Value));
                SetHeaderStatus("MC 写入完成", Brushes.LightGreen);
                _logger.Info("MC 写入完成。");
            }
            catch (Exception ex)
            {
                HandleActionError("MC 写入失败。", ex, true);
            }
        }

        private async void MesConnectButton_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                await ResetMesClientAsync();
                var options = new MesClientOptions
                {
                    Host = RequireText(MesHostTextBox.Text, "MES 主机"),
                    Port = ParseIntValue(MesPortTextBox.Text, "MES 端口"),
                    DeviceNo = RequireText(MesDeviceNoTextBox.Text, "MES 设备编号"),
                    DeviceName = RequireText(MesDeviceNameTextBox.Text, "MES 设备名称"),
                    DeviceIp = RequireText(MesDeviceIpTextBox.Text, "MES 设备 IP"),
                    DeviceMac = RequireText(MesDeviceMacTextBox.Text, "MES 设备 MAC"),
                    AutoReconnect = true,
                };
                var client = new MesTcpClient(options, _sdkLogger);
                client.ConnectionStateChanged += MesClient_ConnectionStateChanged;
                client.FaCheckReceived += MesClient_FaCheckReceived;
                client.FaNumReceived += MesClient_FaNumReceived;
                client.RawMessage += MesClient_RawMessage;
                client.ProtocolError += MesClient_ProtocolError;
                _mesClient = client;
                await client.ConnectAsync(CancellationToken.None);
                SetHeaderStatus("MES 已连接", Brushes.LightGreen);
            }
            catch (Exception ex)
            {
                UpdateMesStatus();
                HandleActionError("MES 首次连接失败，后台将继续重连。", ex, true);
            }
        }

        private async void MesDisconnectButton_Click(object sender, RoutedEventArgs e)
        {
            try { await ResetMesClientAsync(); SetHeaderStatus("MES 已断开", Brushes.Khaki); }
            catch (Exception ex) { HandleActionError("MES 断开失败。", ex, false); }
        }

        private async void MesOnlineButton_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                RequireMesClient();
                await _mesClient.SendOnlineAsync(CancellationToken.None);
                SetHeaderStatus("MES 上线信息已发送", Brushes.LightGreen);
            }
            catch (Exception ex) { HandleActionError("MES 上线信息发送失败。", ex, true); }
        }

        private async void MesSendTrackButton_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                RequireMesClient();
                var message = new FaTrackMessage
                {
                    Message = new FaTrackBody
                    {
                        Process = RequireText(MesProcessTextBox.Text, "MES 工序编码"),
                        SerialNo = RequireText(MesSerialNoTextBox.Text, "MES SN"),
                        Number = RequireText(MesNumberTextBox.Text, "MES 螺丝数量"),
                        Parameters = ParseMesParameters(MesParametersTextBox.Text),
                    },
                };
                await _mesClient.SendTrackAsync(message, CancellationToken.None);
                SetHeaderStatus("MES FATRACK 已发送", Brushes.LightGreen);
            }
            catch (Exception ex) { HandleActionError("MES FATRACK 发送失败。", ex, true); }
        }

        /// <summary>
        /// “测试并启用”按钮事件。
        /// 先停止旧记录器，再验证当前连接字符串、创建历史表，最后才把记录状态切换为启用。
        /// async void 仅用于 WPF 事件处理程序；普通 SDK 方法应返回 Task，方便调用方等待和捕获异常。
        /// </summary>
        private async void DatabaseStartButton_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                // 用户可能修改连接字符串后再次点击按钮。先排空并释放旧实例，避免两个消费者同时写库。
                await StopDatabaseRecorderAsync();
                if (_databaseManagementStore != null) { _databaseManagementStore.Dispose(); _databaseManagementStore = null; }

                // UI 文本先经过非空校验，再交给 SDK 配置类继续校验表名和超时。
                var options = new SqlServerDataStoreOptions
                {
                    ConnectionString = RequireText(DatabaseConnectionStringTextBox.Text, "数据库连接字符串"),
                    TableName = RequireText(DatabaseTableNameTextBox.Text, "历史表名"),
                    CommandTimeoutSeconds = 15,
                };
                var store = new SqlServerIndustrialDataStore(options);
                var recorder = new BufferedIndustrialDataRecorder(
                    store,
                    new BufferedDataRecorderOptions
                    {
                        BatchSize = 100,
                        QueueCapacity = 1000,
                        RetryCount = 2,
                    },
                    _sdkLogger);

                try
                {
                    // StartAsync 会真实连接数据库，并以幂等方式创建历史表。
                    await recorder.StartAsync(CancellationToken.None);
                }
                catch
                {
                    // 初始化失败时记录器尚未交给字段管理，因此必须在这里主动释放。
                    recorder.Dispose();
                    throw;
                }

                // 只有上面的数据库初始化完整成功后，才发布记录器引用和启用标志。
                _databaseRecorder = recorder;
                _databaseManagementStore = new SqlServerIndustrialDataStore(options);
                Interlocked.Exchange(ref _databaseRecordingEnabled, 1);
                _databaseHistoryRows.Clear();
                DatabaseHistoryStatusTextBlock.Text = "正在读取最近的历史数据...";
                DatabaseHistoryStatusTextBlock.Foreground = Brushes.SteelBlue;
                _databaseHistoryCancellation = new CancellationTokenSource();
                _databaseHistoryTask = RefreshDatabaseHistoryAsync(store, _databaseHistoryCancellation.Token);
                await RefreshDatabaseFilterOptionsAsync();
                await RefreshDatabaseLatestAsync();
                DatabaseEnabledCheckBox.IsChecked = true;
                DatabaseStatusTextBlock.Text = "已连接，正在后台记录读取和轮询数据。";
                DatabaseStatusTextBlock.Foreground = Brushes.ForestGreen;
                SetHeaderStatus("数据库记录已启用", Brushes.LightGreen);
                _logger.Info("SQL Server 历史数据记录已启用，数据表=" + options.TableName + "。");
            }
            catch (Exception ex)
            {
                // 数据库启用失败只更新数据库状态，不影响已经建立的 PLC 连接。
                DatabaseEnabledCheckBox.IsChecked = false;
                Interlocked.Exchange(ref _databaseRecordingEnabled, 0);
                DatabaseStatusTextBlock.Text = "连接失败：" + ex.Message;
                DatabaseStatusTextBlock.Foreground = Brushes.IndianRed;
                HandleActionError("数据库连接失败。", ex, true);
            }
        }

        /// <summary>
        /// “停止记录”按钮事件。停止时会等待已经进入队列的数据写完，再释放连接相关资源。
        /// </summary>
        private async void DatabaseStopButton_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                await StopDatabaseRecorderAsync();
                DatabaseEnabledCheckBox.IsChecked = false;
                DatabaseStatusTextBlock.Text = "已停止";
                DatabaseStatusTextBlock.Foreground = Brushes.DarkGoldenrod;
                DatabaseHistoryStatusTextBlock.Text = string.Format(
                    CultureInfo.InvariantCulture,
                    "刷新已停止，保留当前 {0} 条",
                    _databaseHistoryRows.Count);
                DatabaseHistoryStatusTextBlock.Foreground = Brushes.DarkGoldenrod;
                UpdateDatabaseRecorderMetrics();
                _logger.Info("SQL Server 历史数据记录已停止。");
            }
            catch (Exception ex)
            {
                HandleActionError("停止数据库记录失败。", ex, true);
            }
        }

        /// <summary>
        /// 把一次设备读取结果交给后台数据库记录器。
        /// 此方法会被 UI 线程和 Modbus 后台轮询线程共同调用，所以只能执行线程安全、非阻塞操作。
        /// </summary>
        /// <param name="client">产生这批数据的工业通信客户端，用于取得协议和设备 ID。</param>
        /// <param name="values">本次读取产生的一个或多个数据值。</param>
        private void QueueDatabaseValues(IIndustrialClient client, IReadOnlyCollection<DataValue> values)
        {
            // 先把字段复制到局部变量，避免另一线程停止记录器后字段在多次访问之间发生变化。
            var recorder = _databaseRecorder;
            if (recorder == null || Volatile.Read(ref _databaseRecordingEnabled) == 0 || client == null || values == null || values.Count == 0)
            {
                return;
            }

            try
            {
                // TryRecord 只复制数据并尝试入队，不会在当前线程打开数据库连接。
                recorder.TryRecord(client.Kind, client.DeviceId, values);
            }
            catch (Exception ex)
            {
                // 写库只是旁路能力；即使数据转换或队列操作异常，也不能反向破坏通信回调。
                _logger.Error("采集结果加入数据库队列失败。", ex);
            }
        }

        /// <summary>
        /// 统一停止并释放数据库记录器。
        /// 窗口关闭、用户点击停止以及重新连接数据库都会复用此方法，避免遗漏释放逻辑。
        /// </summary>
        private async Task StopDatabaseRecorderAsync()
        {
            // 先撤销共享引用和启用标志，阻止新的 PLC 回调继续入队。
            var recorder = _databaseRecorder;
            _databaseRecorder = null;
            Interlocked.Exchange(ref _databaseRecordingEnabled, 0);

            // 查询循环必须先于存储对象停止，避免它在记录器释放存储后继续发起 SQL 查询。
            var historyCancellation = _databaseHistoryCancellation;
            var historyTask = _databaseHistoryTask;
            _databaseHistoryCancellation = null;
            _databaseHistoryTask = null;
            if (historyCancellation != null)
            {
                historyCancellation.Cancel();
                try
                {
                    if (historyTask != null)
                    {
                        await historyTask;
                    }
                }
                finally
                {
                    historyCancellation.Dispose();
                }
            }

            if (recorder == null)
            {
                return;
            }

            try
            {
                // StopAsync 会调用 CompleteAdding，并等待队列中已有数据全部处理完。
                await recorder.StopAsync(CancellationToken.None);
            }
            finally
            {
                // 即使停止过程抛出异常，仍要释放队列、取消令牌和具体数据存储。
                recorder.Dispose();
            }
        }

        private async Task RefreshDatabaseHistoryAsync(SqlServerIndustrialDataStore store, CancellationToken cancellationToken)
        {
            var lastId = 0L;
            var initialLoad = true;
            var errorLogged = false;

            try
            {
                while (true)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    try
                    {
                        var records = initialLoad
                            ? await store.ReadLatestAsync(MaxDatabaseHistoryRowCount, cancellationToken).ConfigureAwait(false)
                            : await store.ReadAfterAsync(lastId, DatabaseHistoryQueryBatchSize, cancellationToken).ConfigureAwait(false);

                        if (initialLoad)
                        {
                            initialLoad = false;
                            if (records.Count > 0)
                            {
                                lastId = records.Max(item => item.Id);
                            }

                            RunOnUi(() => ReplaceDatabaseHistoryRows(records));
                        }
                        else if (records.Count > 0)
                        {
                            lastId = records[records.Count - 1].Id;
                            RunOnUi(() => PrependDatabaseHistoryRows(records));
                        }
                        else
                        {
                            RunOnUi(UpdateDatabaseHistoryStatus);
                        }
                        RunOnUi(UpdateDatabaseRecorderMetrics);

                        errorLogged = false;
                        // 满批说明数据库中可能还有未读取数据，立即继续追赶；否则按一秒频率刷新。
                        if (records.Count < DatabaseHistoryQueryBatchSize)
                        {
                            await Task.Delay(TimeSpan.FromSeconds(1), cancellationToken).ConfigureAwait(false);
                        }
                    }
                    catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
                    {
                        throw;
                    }
                    catch (Exception ex)
                    {
                        if (!errorLogged)
                        {
                            _logger.Error("数据库实时表格刷新失败，将自动重试。", ex);
                            errorLogged = true;
                        }

                        RunOnUi(() =>
                        {
                            DatabaseHistoryStatusTextBlock.Text = "刷新失败，将自动重试：" + ex.Message;
                            DatabaseHistoryStatusTextBlock.Foreground = Brushes.IndianRed;
                        });
                        await Task.Delay(TimeSpan.FromSeconds(2), cancellationToken).ConfigureAwait(false);
                    }
                }
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                // 用户停止记录或关闭窗口时属于正常结束。
            }
        }

        private void ReplaceDatabaseHistoryRows(IReadOnlyList<IndustrialDataRecord> records)
        {
            _databaseHistoryRows.Clear();
            foreach (var record in records)
            {
                _databaseHistoryRows.Add(DatabaseHistoryDisplayRow.FromRecord(record));
            }

            UpdateDatabaseHistoryStatus();
        }

        private void PrependDatabaseHistoryRows(IReadOnlyList<IndustrialDataRecord> records)
        {
            // 增量查询按 Id 升序返回，逐条插入首行后自然形成“最新记录在最上方”。
            foreach (var record in records)
            {
                _databaseHistoryRows.Insert(0, DatabaseHistoryDisplayRow.FromRecord(record));
            }

            while (_databaseHistoryRows.Count > MaxDatabaseHistoryRowCount)
            {
                _databaseHistoryRows.RemoveAt(_databaseHistoryRows.Count - 1);
            }

            UpdateDatabaseHistoryStatus();
        }

        private void UpdateDatabaseHistoryStatus()
        {
            DatabaseHistoryStatusTextBlock.Text = string.Format(
                CultureInfo.InvariantCulture,
                "已显示 {0} 条 · {1}",
                _databaseHistoryRows.Count,
                DateTime.Now.ToString("HH:mm:ss", CultureInfo.InvariantCulture));
            DatabaseHistoryStatusTextBlock.Foreground = Brushes.ForestGreen;
        }

        private void InitializeDatabaseManagementControls()
        {
            AddFilterComboItem(DatabaseQueryProtocolComboBox, "全部", null);
            foreach (ProtocolKind value in Enum.GetValues(typeof(ProtocolKind))) AddFilterComboItem(DatabaseQueryProtocolComboBox, value.ToString(), value);
            AddFilterComboItem(DatabaseQueryDataTypeComboBox, "全部", null);
            foreach (DataType value in Enum.GetValues(typeof(DataType))) AddFilterComboItem(DatabaseQueryDataTypeComboBox, value.ToString(), value);
            AddFilterComboItem(DatabaseQueryQualityComboBox, "全部", null);
            foreach (QualityStatus value in Enum.GetValues(typeof(QualityStatus))) AddFilterComboItem(DatabaseQueryQualityComboBox, value.ToString(), value);
            DatabaseQueryProtocolComboBox.SelectedIndex = DatabaseQueryDataTypeComboBox.SelectedIndex = DatabaseQueryQualityComboBox.SelectedIndex = 0;
        }

        private static void AddFilterComboItem(ComboBox combo, string text, object value)
        {
            combo.Items.Add(new ComboBoxItem { Content = text, Tag = value });
        }

        private async Task RefreshDatabaseFilterOptionsAsync()
        {
            var store = _databaseManagementStore; if (store == null) return;
            var options = await store.GetFilterOptionsAsync(null, 200, CancellationToken.None);
            DatabaseLatestDeviceComboBox.ItemsSource = new[] { string.Empty }.Concat(options.DeviceIds).ToArray();
            DatabaseQueryDeviceComboBox.ItemsSource = new[] { string.Empty }.Concat(options.DeviceIds).ToArray();
        }

        private async void DatabaseLatestRefreshButton_Click(object sender, RoutedEventArgs e)
        {
            try { await RefreshDatabaseLatestAsync(); } catch (Exception ex) { HandleActionError("刷新最新值失败。", ex, true); }
        }

        private async Task RefreshDatabaseLatestAsync()
        {
            var store = RequireDatabaseManagementStore();
            var filter = new HistoryQueryFilter { DeviceId = NormalizeFilterText(DatabaseLatestDeviceComboBox.Text) };
            var records = await store.GetLatestValuesAsync(filter, 500, CancellationToken.None);
            _databaseLatestRows.Clear(); foreach (var record in records) _databaseLatestRows.Add(DatabaseHistoryDisplayRow.FromRecord(record));
        }

        private async void DatabaseQueryButton_Click(object sender, RoutedEventArgs e) { _databaseQueryPage = 1; await ExecuteDatabaseQueryAsync(true); }
        private async void DatabasePreviousPageButton_Click(object sender, RoutedEventArgs e) { if (_databaseQueryPage > 1) { _databaseQueryPage--; await ExecuteDatabaseQueryAsync(false); } }
        private async void DatabaseNextPageButton_Click(object sender, RoutedEventArgs e) { if (_databaseQueryPage * GetDatabasePageSize() < _databaseQueryTotal) { _databaseQueryPage++; await ExecuteDatabaseQueryAsync(false); } }

        private async Task ExecuteDatabaseQueryAsync(bool refreshOptions)
        {
            try
            {
                var store = RequireDatabaseManagementStore(); var filter = BuildDatabaseFilter(); var pageSize = GetDatabasePageSize();
                var page = await store.QueryPageAsync(new HistoryPageRequest { Filter = filter, PageNumber = _databaseQueryPage, PageSize = pageSize }, CancellationToken.None);
                var summary = await store.GetSummaryAsync(filter, CancellationToken.None);
                _databaseQueryRows.Clear(); foreach (var record in page.Records) _databaseQueryRows.Add(DatabaseHistoryDisplayRow.FromRecord(record));
                _databaseQueryTotal = page.TotalCount;
                DatabaseQueryStatusTextBlock.Text = string.Format(CultureInfo.InvariantCulture, "第 {0}/{1} 页 · 共 {2} 条 · Good {3} · Bad {4} · Stale {5} · Unknown {6} · {7} ～ {8}", page.PageNumber, Math.Max(1, (page.TotalCount + pageSize - 1) / pageSize), page.TotalCount, summary.GoodCount, summary.BadCount, summary.StaleCount, summary.UnknownCount, summary.EarliestTimestamp.HasValue ? summary.EarliestTimestamp.Value.ToLocalTime().ToString("yyyy-MM-dd HH:mm") : "--", summary.LatestTimestamp.HasValue ? summary.LatestTimestamp.Value.ToLocalTime().ToString("yyyy-MM-dd HH:mm") : "--");
                if (refreshOptions) await RefreshDatabaseFilterOptionsAsync();
            }
            catch (Exception ex) { HandleActionError("历史数据查询失败。", ex, true); }
        }

        private void DatabaseQueryResetButton_Click(object sender, RoutedEventArgs e)
        {
            DatabaseQueryDeviceComboBox.Text = DatabaseQueryAddressTextBox.Text = DatabaseQueryFromTextBox.Text = DatabaseQueryToTextBox.Text = string.Empty;
            DatabaseQueryContainsCheckBox.IsChecked = false; DatabaseQueryProtocolComboBox.SelectedIndex = DatabaseQueryDataTypeComboBox.SelectedIndex = DatabaseQueryQualityComboBox.SelectedIndex = 0;
            _databaseQueryPage = 1;
        }

        private async void DatabaseExportButton_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                var dialog = new SaveFileDialog { Filter = "CSV 文件 (*.csv)|*.csv", FileName = "industrial-history-" + DateTime.Now.ToString("yyyyMMdd-HHmmss") + ".csv" };
                if (dialog.ShowDialog(this) != true) return;
                var store = RequireDatabaseManagementStore(); var filter = BuildDatabaseFilter(); if (!filter.ToTime.HasValue) filter.ToTime = DateTimeOffset.UtcNow; const int limit = 50000; var exported = 0; var page = 1;
                var summary = await store.GetSummaryAsync(filter, CancellationToken.None); var truncated = summary.TotalCount > limit;
                using (var stream = File.Create(dialog.FileName))
                {
                    while (exported < limit)
                    {
                        var result = await store.QueryPageAsync(new HistoryPageRequest { Filter = filter, PageNumber = page++, PageSize = 1000 }, CancellationToken.None);
                        await CsvHistoryExporter.WriteBatchAsync(result.Records, stream, exported == 0, CancellationToken.None);
                        exported += result.Records.Count;
                        if (result.Records.Count < 1000) break;
                    }
                }
                MessageBox.Show(this, string.Format("已导出 {0} 条。{1}", exported, truncated ? "结果超过 50,000 条，已截断。" : string.Empty), "导出完成");
            }
            catch (Exception ex) { HandleActionError("导出历史数据失败。", ex, true); }
        }

        private async void DatabaseDeleteButton_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                var store = RequireDatabaseManagementStore(); var filter = BuildDatabaseFilter(); EnsureDestructiveFilter(filter);
                var summary = await store.GetSummaryAsync(filter, CancellationToken.None); if (summary.TotalCount == 0) { MessageBox.Show(this, "没有匹配记录。"); return; }
                if (MessageBox.Show(this, "确定删除匹配的 " + summary.TotalCount + " 条记录？", "确认删除", MessageBoxButton.YesNo, MessageBoxImage.Warning) != MessageBoxResult.Yes) return;
                var removed = await store.DeleteAsync(filter, CancellationToken.None); MessageBox.Show(this, "已删除 " + removed + " 条记录。"); await ExecuteDatabaseQueryAsync(true); await RefreshDatabaseLatestAsync();
            }
            catch (Exception ex) { HandleActionError("删除历史数据失败。", ex, true); }
        }

        private async void DatabaseCleanupButton_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                var item = DatabaseRetentionComboBox.SelectedItem as ComboBoxItem; var days = int.Parse(Convert.ToString(item.Tag), CultureInfo.InvariantCulture);
                var filter = new HistoryQueryFilter { ToTime = DateTimeOffset.Now.AddDays(-days) }; var store = RequireDatabaseManagementStore(); var summary = await store.GetSummaryAsync(filter, CancellationToken.None);
                if (summary.TotalCount == 0) { MessageBox.Show(this, "没有需要清理的数据。"); return; }
                if (MessageBox.Show(this, string.Format("确定删除 {0} 天以前的 {1} 条记录？", days, summary.TotalCount), "确认清理", MessageBoxButton.YesNo, MessageBoxImage.Warning) != MessageBoxResult.Yes) return;
                var removed = await store.DeleteAsync(filter, CancellationToken.None); MessageBox.Show(this, "已清理 " + removed + " 条记录。"); await ExecuteDatabaseQueryAsync(true);
            }
            catch (Exception ex) { HandleActionError("清理历史数据失败。", ex, true); }
        }

        private HistoryQueryFilter BuildDatabaseFilter()
        {
            return new HistoryQueryFilter { DeviceId = NormalizeFilterText(DatabaseQueryDeviceComboBox.Text), Address = NormalizeFilterText(DatabaseQueryAddressTextBox.Text),
                AddressMatchMode = DatabaseQueryContainsCheckBox.IsChecked == true ? HistoryAddressMatchMode.Contains : HistoryAddressMatchMode.Exact,
                Protocol = GetSelectedFilterValue<ProtocolKind>(DatabaseQueryProtocolComboBox), DataType = GetSelectedFilterValue<DataType>(DatabaseQueryDataTypeComboBox), Quality = GetSelectedFilterValue<QualityStatus>(DatabaseQueryQualityComboBox),
                FromTime = ParseOptionalDatabaseTime(DatabaseQueryFromTextBox.Text), ToTime = ParseOptionalDatabaseTime(DatabaseQueryToTextBox.Text) };
        }

        private static T? GetSelectedFilterValue<T>(ComboBox combo) where T : struct { var item = combo.SelectedItem as ComboBoxItem; return item != null && item.Tag is T ? (T?)item.Tag : null; }
        private static string GetComboTagText(ComboBox combo) { var item = combo.SelectedItem as ComboBoxItem; return item == null || item.Tag == null ? null : item.Tag.ToString(); }
        private static void SelectComboByTag(ComboBox combo, string tag) { if (string.IsNullOrWhiteSpace(tag)) return; foreach (var item in combo.Items.OfType<ComboBoxItem>()) if (item.Tag != null && string.Equals(item.Tag.ToString(), tag, StringComparison.OrdinalIgnoreCase)) { combo.SelectedItem = item; return; } }
        private static void SelectComboByContent(ComboBox combo, string content) { foreach (var item in combo.Items.OfType<ComboBoxItem>()) if (string.Equals(Convert.ToString(item.Content), content, StringComparison.OrdinalIgnoreCase)) { combo.SelectedItem = item; return; } }
        private static string NormalizeFilterText(string text) { return string.IsNullOrWhiteSpace(text) ? null : text.Trim(); }
        private static DateTimeOffset? ParseOptionalDatabaseTime(string text) { if (string.IsNullOrWhiteSpace(text)) return null; DateTimeOffset value; if (!DateTimeOffset.TryParse(text, CultureInfo.CurrentCulture, DateTimeStyles.AssumeLocal, out value)) throw new InvalidOperationException("时间格式无效，请使用 yyyy-MM-dd HH:mm:ss。"); return value; }
        private int GetDatabasePageSize() { var item = DatabaseQueryPageSizeComboBox.SelectedItem as ComboBoxItem; return int.Parse(Convert.ToString(item.Content), CultureInfo.InvariantCulture); }
        private SqlServerIndustrialDataStore RequireDatabaseManagementStore() { if (_databaseManagementStore == null) throw new InvalidOperationException("请先测试并启用数据库连接。"); return _databaseManagementStore; }
        private static void EnsureDestructiveFilter(HistoryQueryFilter filter) { if (string.IsNullOrWhiteSpace(filter.DeviceId) && string.IsNullOrWhiteSpace(filter.Address) && !filter.Protocol.HasValue && !filter.DataType.HasValue && !filter.Quality.HasValue && !filter.FromTime.HasValue && !filter.ToTime.HasValue) throw new InvalidOperationException("删除操作必须至少设置一个筛选条件。"); }

        private void UpdateDatabaseRecorderMetrics()
        {
            var snapshot = _databaseRecorder == null ? null : _databaseRecorder.GetSnapshot();
            DatabaseRecorderMetricsTextBlock.Text = snapshot == null ? "队列 0 · 已接收 0 · 已写入 0 · 丢弃 0 · 失败 0" : string.Format(CultureInfo.InvariantCulture, "队列 {0} · 已接收 {1} · 已写入 {2} · 丢弃 {3} · 失败 {4} · 最近写入 {5}{6}", snapshot.QueuedBatchCount, snapshot.AcceptedRecordCount, snapshot.WrittenRecordCount, snapshot.DroppedRecordCount, snapshot.WriteFailureCount, snapshot.LastSuccessfulWrite.HasValue ? snapshot.LastSuccessfulWrite.Value.ToLocalTime().ToString("HH:mm:ss") : "--", string.IsNullOrWhiteSpace(snapshot.LastError) ? string.Empty : " · " + snapshot.LastError);
        }

        private void ClearLogButton_Click(object sender, RoutedEventArgs e)
        {
            if (LogTabControl.SelectedIndex == 1)
            {
                SdkLogTextBox.Clear();
                _sdkLogger.Info("SDK 日志已清空。");
            }
            else
            {
                LogTextBox.Clear();
                _logger.Info("Demo 日志已清空。");
            }
        }

        protected override async void OnClosed(EventArgs e)
        {
            // 先保存非敏感 UI 配置，再断开设备，使通信回调不再产生新的数据库记录。
            SaveUiState();
            await ResetModbusClientAsync();
            await ResetS7ClientAsync();
            await ResetMcClientAsync();
            await ResetSocketClientAsync();
            await ResetSocketServerAsync();
            await ResetMesClientAsync();
            // 设备都停止后再排空写库队列，保证退出前尽量保存最后一批采集值。
            await StopDatabaseRecorderAsync();
            if (_databaseManagementStore != null) { _databaseManagementStore.Dispose(); _databaseManagementStore = null; }
            _logger.Dispose();
            _sdkLogger.Dispose();
            base.OnClosed(e);
        }

        private void OnModbusSubscriptionReceived(object sender, SubscriptionEvent e)
        {
            // 数据先从轮询线程快速进入后台数据库队列，再切换到 UI 线程刷新表格。
            // 如果反过来在 UI 委托中执行数据库操作，数据库变慢会直接造成界面卡顿。
            QueueDatabaseValues(_modbusClient, e.Values);
            RunOnUi(() =>
            {
                ModbusResultTextBlock.Text = string.Join(Environment.NewLine, e.Values.Select(FormatDataValue).ToArray());
                UpdateSubscriptionRows(_modbusSubscriptionRows, e.Values);
                UpdateModbusStatus();
            });
        }

        private void S7AddressHistoryComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            ApplyHistorySelection(S7AddressHistoryComboBox, S7AddressTextBox);
        }

        private void McAddressHistoryComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            ApplyHistorySelection(McAddressHistoryComboBox, McAddressTextBox);
        }

        private void SocketServer_ClientConnected(object sender, SocketSessionEventArgs e)
        {
            RunOnUi(() =>
            {
                UpdateSocketServerStatus();
                SetHeaderStatus("Socket 服务端接受了一个客户端", Brushes.LightGreen);
                _logger.Info(string.Format("Socket 服务端客户端已连接：{0}。", e.RemoteEndPoint));
            });
        }

        private void SocketServer_ClientDisconnected(object sender, SocketSessionEventArgs e)
        {
            RunOnUi(() =>
            {
                UpdateSocketServerStatus();
                SetHeaderStatus("客户端已从服务端断开", Brushes.Khaki);
                _logger.Info(string.Format("Socket 服务端客户端已断开：{0}。", e.RemoteEndPoint));
            });
        }

        private void SocketServer_MessageReceived(object sender, SocketTextMessageEventArgs e)
        {
            var shouldEcho = Dispatcher.CheckAccess()
                ? SocketServerEchoCheckBox.IsChecked == true
                : Dispatcher.Invoke(() => SocketServerEchoCheckBox.IsChecked == true);

            RunOnUi(() =>
            {
                SetHeaderStatus("Socket 服务端收到数据", Brushes.LightGreen);
                _logger.Info(string.Format("Socket 服务端收到来自 {0} 的数据：{1}", e.RemoteEndPoint, e.Message));
            });

            if (shouldEcho && _socketServer != null)
            {
                _ = _socketServer.SendToAsync(e.SessionId, "echo: " + e.Message, CancellationToken.None);
            }
        }

        private void SocketClient_Connected(object sender, EventArgs e)
        {
            RunOnUi(UpdateSocketClientStatus);
        }

        private void SocketClient_Disconnected(object sender, EventArgs e)
        {
            RunOnUi(() =>
            {
                UpdateSocketClientStatus();
                SetHeaderStatus("Socket 客户端已断开", Brushes.Khaki);
            });
        }

        private void SocketClient_MessageReceived(object sender, SocketTextMessageEventArgs e)
        {
            RunOnUi(() =>
            {
                SocketClientLastReceivedTextBlock.Text = e.Message;
                SetHeaderStatus("Socket 客户端收到数据", Brushes.LightGreen);
                _logger.Info(string.Format("Socket 客户端收到：{0}", e.Message));
            });
        }

        private void MesClient_ConnectionStateChanged(object sender, MesConnectionStateChangedEventArgs e)
        {
            RunOnUi(() =>
            {
                UpdateMesStatus();
                if (!string.IsNullOrWhiteSpace(e.ErrorMessage)) MesProtocolErrorTextBlock.Text = e.ErrorMessage;
            });
        }

        private void MesClient_FaCheckReceived(object sender, MesMessageEventArgs<FaCheckMessage> e)
        {
            RunOnUi(() =>
            {
                var body = e.Message == null ? null : e.Message.Message;
                var result = body == null ? string.Empty : body.Result;
                MesFaCheckTextBlock.Text = body == null ? "消息缺少 message 内容" :
                    string.Format(CultureInfo.InvariantCulture, "{0} · SN={1} · 工序={2}{3}", result, body.SerialNo, body.Process,
                        string.IsNullOrWhiteSpace(body.Description) ? string.Empty : " · " + body.Description);
                MesFaCheckTextBlock.Foreground = ResultBrush(result);
            });
        }

        private void MesClient_FaNumReceived(object sender, MesMessageEventArgs<FaNumMessage> e)
        {
            RunOnUi(() =>
            {
                var result = e.Message == null || e.Message.Message == null ? string.Empty : e.Message.Message.Result;
                MesFaNumTextBlock.Text = string.IsNullOrWhiteSpace(result) ? "消息缺少 result" : result;
                MesFaNumTextBlock.Foreground = ResultBrush(result);
            });
        }

        private void MesClient_RawMessage(object sender, MesRawMessageEventArgs e)
        {
            RunOnUi(() => AppendMesTraffic(e.Sent ? "发送" : "接收", e.Message));
        }

        private void MesClient_ProtocolError(object sender, MesProtocolErrorEventArgs e)
        {
            RunOnUi(() => MesProtocolErrorTextBlock.Text = e.ErrorMessage +
                (e.Exception == null ? string.Empty : " " + e.Exception.Message));
        }

        private async Task ResetModbusClientAsync()
        {
            var client = _modbusClient;
            _modbusClient = null;

            if (client == null)
            {
                UpdateModbusStatus();
                return;
            }

            try
            {
                if (!string.IsNullOrWhiteSpace(_modbusSubscriptionId))
                {
                    await client.UnsubscribeAsync(_modbusSubscriptionId, CancellationToken.None);
                }
            }
            catch
            {
            }
            finally
            {
                _modbusSubscriptionId = null;
            }

            try
            {
                await client.DisconnectAsync(CancellationToken.None);
            }
            catch
            {
            }

            client.Dispose();
            UpdateModbusStatus();
        }

        private async Task ResetS7ClientAsync()
        {
            var client = _s7Client;
            _s7Client = null;

            if (client == null)
            {
                UpdateS7Status();
                return;
            }

            try
            {
                await client.DisconnectAsync(CancellationToken.None);
            }
            catch
            {
            }

            client.Dispose();
            UpdateS7Status();
        }

        private async Task ResetMcClientAsync()
        {
            var client = _mcClient;
            _mcClient = null;

            if (client == null)
            {
                UpdateMcStatus();
                return;
            }

            try
            {
                await client.DisconnectAsync(CancellationToken.None);
            }
            catch
            {
            }

            client.Dispose();
            UpdateMcStatus();
        }

        private async Task ResetSocketServerAsync()
        {
            if (_socketServer == null)
            {
                UpdateSocketServerStatus();
                return;
            }

            var server = _socketServer;
            _socketServer = null;

            try
            {
                await server.StopAsync(CancellationToken.None);
            }
            catch
            {
            }

            server.ClientConnected -= SocketServer_ClientConnected;
            server.ClientDisconnected -= SocketServer_ClientDisconnected;
            server.MessageReceived -= SocketServer_MessageReceived;
            server.Dispose();

            UpdateSocketServerStatus();
            SocketServerSessionsTextBlock.Text = "0";
        }

        private async Task ResetSocketClientAsync()
        {
            if (_socketClient == null)
            {
                UpdateSocketClientStatus();
                return;
            }

            var client = _socketClient;
            _socketClient = null;

            client.Connected -= SocketClient_Connected;
            client.Disconnected -= SocketClient_Disconnected;
            client.MessageReceived -= SocketClient_MessageReceived;

            try
            {
                await client.DisconnectAsync(CancellationToken.None);
            }
            catch
            {
            }

            client.Dispose();
            UpdateSocketClientStatus();
        }

        private async Task ResetMesClientAsync()
        {
            var client = _mesClient;
            _mesClient = null;
            if (client == null) { UpdateMesStatus(); return; }
            client.ConnectionStateChanged -= MesClient_ConnectionStateChanged;
            client.FaCheckReceived -= MesClient_FaCheckReceived;
            client.FaNumReceived -= MesClient_FaNumReceived;
            client.RawMessage -= MesClient_RawMessage;
            client.ProtocolError -= MesClient_ProtocolError;
            try { await client.DisconnectAsync(CancellationToken.None); }
            catch { }
            finally { client.Dispose(); UpdateMesStatus(); }
        }

        private void UpdateAllStatuses()
        {
            UpdateModbusStatus();
            UpdateSocketServerStatus();
            UpdateSocketClientStatus();
            UpdateMesStatus();
            UpdateS7Status();
            UpdateMcStatus();
        }

        private void UpdateModbusStatus()
        {
            UpdateClientStatus(_modbusClient, ModbusStatusTextBlock);
        }

        private void UpdateS7Status()
        {
            UpdateClientStatus(_s7Client, S7StatusTextBlock);
        }

        private void UpdateMcStatus()
        {
            UpdateClientStatus(_mcClient, McStatusTextBlock);
        }

        private void UpdateSocketServerStatus()
        {
            var isRunning = _socketServer != null && _socketServer.IsRunning;
            SocketServerStatusTextBlock.Text = isRunning ? "运行中" : "已停止";
            SocketServerStatusTextBlock.Foreground = isRunning ? Brushes.ForestGreen : Brushes.IndianRed;
            SocketServerSessionsTextBlock.Text = (_socketServer == null ? 0 : _socketServer.SessionCount).ToString(CultureInfo.InvariantCulture);
        }

        private void UpdateSocketClientStatus()
        {
            var isConnected = _socketClient != null && _socketClient.IsConnected;
            SocketClientStatusTextBlock.Text = isConnected ? "已连接" : "未连接";
            SocketClientStatusTextBlock.Foreground = isConnected ? Brushes.ForestGreen : Brushes.IndianRed;
            if (!isConnected)
            {
                SocketClientLastReceivedTextBlock.Text = "（无）";
            }
        }

        private static void UpdateClientStatus(IIndustrialClient client, TextBlock statusBlock)
        {
            if (client == null)
            {
                statusBlock.Text = "未连接";
                statusBlock.Foreground = Brushes.IndianRed;
                return;
            }

            if (client.IsConnected)
            {
                statusBlock.Text = "已连接";
                statusBlock.Foreground = Brushes.ForestGreen;
                return;
            }

            var health = client.GetHealth();
            statusBlock.Text = health == null ? "未连接" : health.Status.ToString();
            statusBlock.Foreground = Brushes.DarkGoldenrod;
        }

        private void ApplyModbusProfile()
        {
            _modbusProfile = GetSelectedModbusProfile();
            _modbusAddressParser = new ModbusAddressParser(_modbusProfile);
            ModbusExampleAddressTextBlock.Text = _modbusProfile.ExampleAddresses;

            if (string.IsNullOrWhiteSpace(ModbusAddressTextBox.Text))
            {
                ModbusAddressTextBox.Text = _modbusProfile.DefaultAddress;
            }
        }

        private void RefreshModbusProfileOptions()
        {
            if (ModbusModelComboBox == null)
            {
                return;
            }

            var selectedKey = (ModbusModelComboBox.SelectedItem as ComboBoxItem)?.Tag as string;
            var isRtu = ModbusConnectionTypeComboBox.SelectedIndex == 1;

            ModbusModelComboBox.Items.Clear();
            if (isRtu)
            {
                ModbusModelComboBox.Items.Add(new ComboBoxItem
                {
                    Content = "通用 Modbus",
                    Tag = ModbusDeviceProfiles.Generic.Key,
                });
            }
            else
            {
                ModbusModelComboBox.Items.Add(new ComboBoxItem
                {
                    Content = "汇川 EasyPLC",
                    Tag = ModbusDeviceProfiles.InovanceEasyPlc.Key,
                });
                ModbusModelComboBox.Items.Add(new ComboBoxItem
                {
                    Content = "三菱 Modbus TCP",
                    Tag = ModbusDeviceProfiles.MitsubishiModbusTcp.Key,
                });
            }

            SelectModbusModel(selectedKey);
        }

        private void LoadUiState()
        {
            ApplyModbusState();
            ApplySocketState();
            ApplyMesState();
            ApplyS7State();
            ApplyMcState();
            ApplyDatabaseState();
        }

        private void SaveUiState()
        {
            if (_uiState == null)
            {
                _uiState = new DemoUiState();
            }

            _uiState.Modbus.DeviceId = ModbusDeviceIdTextBox.Text;
            _uiState.Modbus.Host = ModbusHostTextBox.Text;
            _uiState.Modbus.Port = ModbusPortTextBox.Text;
            _uiState.Modbus.SlaveId = ModbusSlaveIdTextBox.Text;
            _uiState.Modbus.ModelKey = _modbusProfile != null ? _modbusProfile.Key : ModbusDeviceProfiles.InovanceEasyPlc.Key;
            _uiState.Modbus.Address = ModbusAddressTextBox.Text;
            _uiState.Modbus.Length = ModbusLengthTextBox.Text;
            _uiState.Modbus.WriteValue = ModbusWriteValueTextBox.Text;
            _uiState.Modbus.PollInterval = ModbusPollIntervalTextBox.Text;
            _uiState.Modbus.ConnectionType = ModbusConnectionTypeComboBox.SelectedIndex == 1 ? "Rtu" : "Tcp";
            _uiState.Modbus.PortName = ModbusPortNameComboBox.Text;
            _uiState.Modbus.BaudRate = ModbusBaudRateComboBox.Text;
            _uiState.Modbus.DataBits = ModbusDataBitsComboBox.Text;
            _uiState.Modbus.Parity = (ModbusParityComboBox.SelectedItem as ComboBoxItem)?.Tag?.ToString();
            _uiState.Modbus.StopBits = (ModbusStopBitsComboBox.SelectedItem as ComboBoxItem)?.Tag?.ToString();

            _uiState.Socket.ServerIp = SocketServerIpTextBox.Text;
            _uiState.Socket.ServerPort = SocketServerPortTextBox.Text;
            _uiState.Socket.ClientHost = SocketClientHostTextBox.Text;
            _uiState.Socket.ClientPort = SocketClientPortTextBox.Text;
            _uiState.Socket.EchoEnabled = SocketServerEchoCheckBox.IsChecked ?? true;
            _uiState.Socket.ServerMessage = SocketServerMessageTextBox.Text;
            _uiState.Socket.ClientMessage = SocketClientMessageTextBox.Text;

            _uiState.Mes.Host = MesHostTextBox.Text;
            _uiState.Mes.Port = MesPortTextBox.Text;
            _uiState.Mes.DeviceNo = MesDeviceNoTextBox.Text;
            _uiState.Mes.DeviceName = MesDeviceNameTextBox.Text;
            _uiState.Mes.DeviceIp = MesDeviceIpTextBox.Text;
            _uiState.Mes.DeviceMac = MesDeviceMacTextBox.Text;
            _uiState.Mes.Process = MesProcessTextBox.Text;
            _uiState.Mes.SerialNo = MesSerialNoTextBox.Text;
            _uiState.Mes.Number = MesNumberTextBox.Text;
            _uiState.Mes.Parameters = MesParametersTextBox.Text;

            _uiState.S7.DeviceId = S7DeviceIdTextBox.Text;
            _uiState.S7.Host = S7HostTextBox.Text;
            _uiState.S7.PortOrRack = S7RackTextBox.Text;
            _uiState.S7.SlotOrLength = S7SlotTextBox.Text;
            _uiState.S7.Address = S7AddressTextBox.Text;
            _uiState.S7.Length = S7LengthTextBox.Text;
            _uiState.S7.WriteValue = S7WriteValueTextBox.Text;

            _uiState.Mc.DeviceId = McDeviceIdTextBox.Text;
            _uiState.Mc.Host = McHostTextBox.Text;
            _uiState.Mc.PortOrRack = McPortTextBox.Text;
            _uiState.Mc.Address = McAddressTextBox.Text;
            _uiState.Mc.Length = McLengthTextBox.Text;
            _uiState.Mc.WriteValue = McWriteValueTextBox.Text;

            _uiState.Database.ConnectionString = DatabaseConnectionStringTextBox.Text;
            _uiState.Database.TableName = DatabaseTableNameTextBox.Text;
            _uiState.Database.QueryDeviceId = DatabaseQueryDeviceComboBox.Text;
            _uiState.Database.QueryAddress = DatabaseQueryAddressTextBox.Text;
            _uiState.Database.AddressContains = DatabaseQueryContainsCheckBox.IsChecked == true;
            _uiState.Database.FromTime = DatabaseQueryFromTextBox.Text;
            _uiState.Database.ToTime = DatabaseQueryToTextBox.Text;
            _uiState.Database.PageSize = GetDatabasePageSize();
            var retention = DatabaseRetentionComboBox.SelectedItem as ComboBoxItem;
            _uiState.Database.RetentionDays = retention == null ? 30 : int.Parse(Convert.ToString(retention.Tag), CultureInfo.InvariantCulture);
            _uiState.Database.Protocol = GetComboTagText(DatabaseQueryProtocolComboBox);
            _uiState.Database.DataType = GetComboTagText(DatabaseQueryDataTypeComboBox);
            _uiState.Database.Quality = GetComboTagText(DatabaseQueryQualityComboBox);

            _uiStateStore.Save(_uiState);
        }

        private void ApplyModbusState()
        {
            var state = _uiState.Modbus ?? new ModbusUiState();
            SetIfNotEmpty(ModbusDeviceIdTextBox, state.DeviceId);
            SetIfNotEmpty(ModbusHostTextBox, state.Host);
            SetIfNotEmpty(ModbusPortTextBox, state.Port);
            SetIfNotEmpty(ModbusSlaveIdTextBox, state.SlaveId);

            // RTU 状态恢复
            if (string.Equals(state.ConnectionType, "Rtu", StringComparison.OrdinalIgnoreCase))
            {
                ModbusConnectionTypeComboBox.SelectedIndex = 1;
            }
            RefreshModbusProfileOptions();
            SelectModbusModel(state.ModelKey);
            ApplyModbusProfile();
            SetIfNotEmpty(ModbusAddressTextBox, state.Address);
            SetIfNotEmpty(ModbusLengthTextBox, state.Length);
            SetIfNotEmpty(ModbusWriteValueTextBox, state.WriteValue);
            SetIfNotEmpty(ModbusPollIntervalTextBox, state.PollInterval);
            if (!string.IsNullOrEmpty(state.PortName))
            {
                ModbusPortNameComboBox.Text = state.PortName;
            }
            if (!string.IsNullOrEmpty(state.BaudRate))
            {
                ModbusBaudRateComboBox.Text = state.BaudRate;
            }
            SelectComboBoxByContent(ModbusDataBitsComboBox, state.DataBits);
            SelectComboBoxByTag(ModbusParityComboBox, state.Parity);
            SelectComboBoxByTag(ModbusStopBitsComboBox, state.StopBits);
        }

        private IModbusDeviceProfile GetSelectedModbusProfile()
        {
            var selectedItem = ModbusModelComboBox.SelectedItem as ComboBoxItem;
            var key = selectedItem != null ? selectedItem.Tag as string : null;
            if (string.IsNullOrWhiteSpace(key))
            {
                return ModbusDeviceProfiles.Generic;
            }

            return ModbusDeviceProfiles.All.FirstOrDefault(profile => string.Equals(profile.Key, key, StringComparison.OrdinalIgnoreCase))
                ?? ModbusDeviceProfiles.Generic;
        }

        private void SelectModbusModel(string modelKey)
        {
            if (string.IsNullOrWhiteSpace(modelKey))
            {
                ModbusModelComboBox.SelectedIndex = 0;
                return;
            }

            foreach (var item in ModbusModelComboBox.Items.OfType<ComboBoxItem>())
            {
                if (string.Equals(item.Tag as string, modelKey, StringComparison.OrdinalIgnoreCase))
                {
                    ModbusModelComboBox.SelectedItem = item;
                    return;
                }
            }

            ModbusModelComboBox.SelectedIndex = 0;
        }

        private void ApplySocketState()
        {
            var state = _uiState.Socket ?? new SocketUiState();
            SetIfNotEmpty(SocketServerIpTextBox, state.ServerIp);
            SetIfNotEmpty(SocketServerPortTextBox, state.ServerPort);
            SetIfNotEmpty(SocketClientHostTextBox, state.ClientHost);
            SetIfNotEmpty(SocketClientPortTextBox, state.ClientPort);
            SetIfNotEmpty(SocketServerMessageTextBox, state.ServerMessage);
            SetIfNotEmpty(SocketClientMessageTextBox, state.ClientMessage);
            SocketServerEchoCheckBox.IsChecked = state.EchoEnabled;
        }

        private void ApplyMesState()
        {
            var state = _uiState.Mes ?? new MesUiState();
            SetIfNotEmpty(MesHostTextBox, state.Host);
            SetIfNotEmpty(MesPortTextBox, state.Port);
            SetIfNotEmpty(MesDeviceNoTextBox, state.DeviceNo);
            SetIfNotEmpty(MesDeviceNameTextBox, state.DeviceName);
            SetIfNotEmpty(MesDeviceIpTextBox, state.DeviceIp);
            SetIfNotEmpty(MesDeviceMacTextBox, state.DeviceMac);
            SetIfNotEmpty(MesProcessTextBox, state.Process);
            SetIfNotEmpty(MesSerialNoTextBox, state.SerialNo);
            SetIfNotEmpty(MesNumberTextBox, state.Number);
            SetIfNotEmpty(MesParametersTextBox, state.Parameters);
        }

        private void ApplyS7State()
        {
            var state = _uiState.S7 ?? new ProtocolUiState();
            SetIfNotEmpty(S7DeviceIdTextBox, state.DeviceId);
            SetIfNotEmpty(S7HostTextBox, state.Host);
            SetIfNotEmpty(S7RackTextBox, state.PortOrRack);
            SetIfNotEmpty(S7SlotTextBox, state.SlotOrLength);
            SetIfNotEmpty(S7AddressTextBox, state.Address);
            SetIfNotEmpty(S7LengthTextBox, state.Length);
            SetIfNotEmpty(S7WriteValueTextBox, state.WriteValue);
        }

        private void ApplyMcState()
        {
            var state = _uiState.Mc ?? new ProtocolUiState();
            SetIfNotEmpty(McDeviceIdTextBox, state.DeviceId);
            SetIfNotEmpty(McHostTextBox, state.Host);
            SetIfNotEmpty(McPortTextBox, state.PortOrRack);
            SetIfNotEmpty(McAddressTextBox, state.Address);
            SetIfNotEmpty(McLengthTextBox, state.Length);
            SetIfNotEmpty(McWriteValueTextBox, state.WriteValue);
        }

        private void UpdateMesStatus()
        {
            var state = _mesClient == null ? MesConnectionState.Disconnected : _mesClient.State;
            switch (state)
            {
                case MesConnectionState.Connected: MesStatusTextBlock.Text = "已连接"; MesStatusTextBlock.Foreground = Brushes.ForestGreen; break;
                case MesConnectionState.Connecting: MesStatusTextBlock.Text = "正在连接"; MesStatusTextBlock.Foreground = Brushes.SteelBlue; break;
                case MesConnectionState.Reconnecting: MesStatusTextBlock.Text = "正在重连"; MesStatusTextBlock.Foreground = Brushes.DarkGoldenrod; break;
                default: MesStatusTextBlock.Text = "未连接"; MesStatusTextBlock.Foreground = Brushes.IndianRed; break;
            }
        }

        private void ApplyDatabaseState()
        {
            var state = _uiState.Database ?? new DatabaseUiState();

            // 只恢复连接字符串和表名，不自动连接数据库。
            // 每次启动都由用户点击“测试并启用”，可以明确看到本次连接是否成功。
            SetIfNotEmpty(DatabaseConnectionStringTextBox, state.ConnectionString);
            SetIfNotEmpty(DatabaseTableNameTextBox, state.TableName);
            DatabaseQueryDeviceComboBox.Text = state.QueryDeviceId ?? string.Empty;
            DatabaseQueryAddressTextBox.Text = state.QueryAddress ?? string.Empty;
            DatabaseQueryContainsCheckBox.IsChecked = state.AddressContains;
            DatabaseQueryFromTextBox.Text = state.FromTime ?? string.Empty;
            DatabaseQueryToTextBox.Text = state.ToTime ?? string.Empty;
            SelectComboByContent(DatabaseQueryPageSizeComboBox, state.PageSize <= 0 ? "100" : state.PageSize.ToString(CultureInfo.InvariantCulture));
            SelectComboByTag(DatabaseQueryProtocolComboBox, state.Protocol);
            SelectComboByTag(DatabaseQueryDataTypeComboBox, state.DataType);
            SelectComboByTag(DatabaseQueryQualityComboBox, state.Quality);
            foreach (var item in DatabaseRetentionComboBox.Items.OfType<ComboBoxItem>()) if (Convert.ToString(item.Tag) == (state.RetentionDays <= 0 ? 30 : state.RetentionDays).ToString(CultureInfo.InvariantCulture)) { DatabaseRetentionComboBox.SelectedItem = item; break; }
            DatabaseEnabledCheckBox.IsChecked = false;
            DatabaseStatusTextBlock.Text = "未启用";
        }

        private void RefreshAddressHistoryCombos()
        {
            RefreshAddressHistory(ModbusAddressHistoryComboBox, _uiState.Modbus.RecentAddresses);
            RefreshAddressHistory(S7AddressHistoryComboBox, _uiState.S7.RecentAddresses);
            RefreshAddressHistory(McAddressHistoryComboBox, _uiState.Mc.RecentAddresses);
        }

        private void RefreshModbusDataTypeState()
        {
            var addresses = SplitAddresses(ModbusAddressTextBox.Text);
            var isBitAddress = addresses.Count > 0 && addresses.All(IsModbusBitAddress);
            if (isBitAddress)
            {
                SetEnabledDataTypes(ModbusDataTypeComboBox, new[] { DataType.Bool });
                SelectDataType(ModbusDataTypeComboBox, DataType.Bool);
            }
            else
            {
                SetEnabledDataTypes(ModbusDataTypeComboBox, ModbusRegisterDataTypes);
                if (GetSelectedDataType(ModbusDataTypeComboBox) == DataType.Bool)
                {
                    SelectDataType(ModbusDataTypeComboBox, DataType.Int16);
                }
            }

            RefreshModbusLengthSuggestion();
        }

        private void RefreshModbusInputHints()
        {
            var analysis = AnalyzeModbusAddresses();
            if (!analysis.IsValid)
            {
                ModbusAddressHintTextBlock.Foreground = Brushes.OrangeRed;
                ModbusAddressHintTextBlock.Text = analysis.ErrorMessage;
            }
            else if (analysis.AddressCount == 0)
            {
                ModbusAddressHintTextBlock.Foreground = Brushes.DimGray;
                ModbusAddressHintTextBlock.Text = "支持逗号、分号、换行输入多个地址。";
            }
            else
            {
                ModbusAddressHintTextBlock.Foreground = Brushes.ForestGreen;
                var modeText = analysis.AddressCount == 1 ? "单地址" : string.Format(CultureInfo.InvariantCulture, "多地址（{0} 个）", analysis.AddressCount);
                ModbusAddressHintTextBlock.Text = string.Format(
                    CultureInfo.InvariantCulture,
                    "{0}，{1}。{2}",
                    modeText,
                    analysis.IsBitFamily ? "位地址模式，只允许 Bool 解析" : "寄存器地址模式，可选择值的解析方式",
                    analysis.AddressCount > 1 ? "读取、订阅、写入都支持批量。" : "可直接读取、写入或订阅。");
            }

            RefreshModbusWriteHint(analysis);
        }

        private void RefreshModbusWriteHint(ModbusAddressInputAnalysis analysis)
        {
            if (!analysis.IsValid || analysis.AddressCount == 0)
            {
                ModbusWriteHintTextBlock.Foreground = Brushes.DimGray;
                ModbusWriteHintTextBlock.Text = "多地址写入时，可填写 1 个值广播，或填写与地址数相同的多个值。";
                return;
            }

            if (analysis.AddressCount == 1)
            {
                ModbusWriteHintTextBlock.Foreground = Brushes.DimGray;
                ModbusWriteHintTextBlock.Text = "单地址写入沿用当前解析类型和长度。";
                return;
            }

            var values = SplitBatchWriteValues(ModbusWriteValueTextBox.Text);
            if (values.Count <= 1)
            {
                ModbusWriteHintTextBlock.Foreground = Brushes.ForestGreen;
                ModbusWriteHintTextBlock.Text = string.Format(CultureInfo.InvariantCulture, "当前将把同一个值写到 {0} 个地址。", analysis.AddressCount);
                return;
            }

            if (values.Count == analysis.AddressCount)
            {
                ModbusWriteHintTextBlock.Foreground = Brushes.ForestGreen;
                ModbusWriteHintTextBlock.Text = string.Format(CultureInfo.InvariantCulture, "当前将按顺序逐项写入 {0} 个地址。", analysis.AddressCount);
                return;
            }

            ModbusWriteHintTextBlock.Foreground = Brushes.OrangeRed;
            ModbusWriteHintTextBlock.Text = string.Format(
                CultureInfo.InvariantCulture,
                "多地址写入时，写入值数量必须是 1 个或 {0} 个，当前为 {1} 个。",
                analysis.AddressCount,
                values.Count);
        }

        private void RefreshModbusLengthSuggestion()
        {
            if (IsModbusBitAddress(ModbusAddressTextBox.Text))
            {
                if (string.IsNullOrWhiteSpace(ModbusLengthTextBox.Text))
                {
                    ModbusLengthTextBox.Text = "1";
                }

                return;
            }

            try
            {
                var suggestedLength = GetSuggestedRegisterLength(GetSelectedDataType(ModbusDataTypeComboBox), ModbusWriteValueTextBox.Text);
                ModbusLengthTextBox.Text = suggestedLength.ToString(CultureInfo.InvariantCulture);
            }
            catch
            {
                ModbusLengthTextBox.Text = "1";
            }
        }

        private static ushort GetSuggestedRegisterLength(DataType dataType, string textValue)
        {
            switch (dataType)
            {
                case DataType.Byte:
                case DataType.Char:
                case DataType.Int16:
                case DataType.UInt16:
                    return 1;
                case DataType.Int32:
                case DataType.UInt32:
                case DataType.Float:
                    return 2;
                case DataType.Double:
                    return 4;
                case DataType.String:
                    return Math.Max((ushort)1, (ushort)((Encoding.ASCII.GetByteCount(textValue ?? string.Empty) + 1) / 2));
                case DataType.ByteArray:
                    return Math.Max((ushort)1, (ushort)((ParseByteArray(textValue).Length + 1) / 2));
                default:
                    return 1;
            }
        }

        private List<ReadRequest> BuildModbusReadRequests()
        {
            var addresses = ValidateAndGetModbusAddresses();

            var deviceId = GetModbusDeviceId();
            var dataType = GetSelectedDataType(ModbusDataTypeComboBox);
            var length = ParseUShortValue(ModbusLengthTextBox.Text, "Modbus 长度");

            return addresses
                .Select(address => new ReadRequest(deviceId, address, dataType, length))
                .ToList();
        }

        private List<WriteRequest> BuildModbusWriteRequests()
        {
            var addresses = ValidateAndGetModbusAddresses();
            var dataType = GetSelectedDataType(ModbusDataTypeComboBox);
            var length = ParseUShortValue(ModbusLengthTextBox.Text, "Modbus 长度");
            var deviceId = GetModbusDeviceId();

            if (addresses.Count == 1)
            {
                return new List<WriteRequest>
                {
                    new WriteRequest(
                        deviceId,
                        addresses[0],
                        dataType,
                        ParseValue(ModbusWriteValueTextBox.Text, dataType, length),
                        length)
                };
            }

            var values = SplitBatchWriteValues(ModbusWriteValueTextBox.Text);
            if (values.Count == 0)
            {
                throw new InvalidOperationException("Modbus 写入值不能为空。");
            }

            if (values.Count != 1 && values.Count != addresses.Count)
            {
                throw new InvalidOperationException(string.Format(
                    CultureInfo.InvariantCulture,
                    "多地址写入时，写入值数量必须是 1 个或与地址数量一致（{0} 个）。",
                    addresses.Count));
            }

            if (values.Count == 1)
            {
                var parsedValue = ParseValue(values[0], dataType, length);
                return addresses
                    .Select(address => new WriteRequest(deviceId, address, dataType, parsedValue, length))
                    .ToList();
            }

            return addresses
                .Zip(values, (address, valueText) => new WriteRequest(deviceId, address, dataType, ParseValue(valueText, dataType, length), length))
                .ToList();
        }

        private ReadRequest BuildReadRequest(TextBox deviceIdTextBox, TextBox addressTextBox, ComboBox dataTypeComboBox, TextBox lengthTextBox)
        {
            return new ReadRequest(
                RequireText(deviceIdTextBox.Text, "设备 ID"),
                RequireText(addressTextBox.Text, "地址"),
                GetSelectedDataType(dataTypeComboBox),
                ParseUShortValue(lengthTextBox.Text, "长度"));
        }

        private WriteRequest BuildWriteRequest(TextBox deviceIdTextBox, TextBox addressTextBox, ComboBox dataTypeComboBox, TextBox lengthTextBox, TextBox valueTextBox)
        {
            var dataType = GetSelectedDataType(dataTypeComboBox);
            var length = ParseUShortValue(lengthTextBox.Text, "长度");

            return new WriteRequest(
                RequireText(deviceIdTextBox.Text, "设备 ID"),
                RequireText(addressTextBox.Text, "地址"),
                dataType,
                ParseValue(valueTextBox.Text, dataType, length),
                length);
        }

        private ReadRequest BuildS7ReadRequest()
        {
            var typedAddress = ParseS7AddressInput(S7AddressTextBox.Text);
            var dataType = typedAddress.InferredDataType ?? GetSelectedDataType(S7DataTypeComboBox);
            var length = typedAddress.InferredLength ?? ParseUShortValue(S7LengthTextBox.Text, "长度");

            return new ReadRequest(
                RequireText(S7DeviceIdTextBox.Text, "设备 ID"),
                typedAddress.NormalizedAddress,
                dataType,
                length);
        }

        private WriteRequest BuildS7WriteRequest()
        {
            var typedAddress = ParseS7AddressInput(S7AddressTextBox.Text);
            var dataType = typedAddress.InferredDataType ?? GetSelectedDataType(S7DataTypeComboBox);
            var length = typedAddress.InferredLength ?? ParseUShortValue(S7LengthTextBox.Text, "长度");

            return new WriteRequest(
                RequireText(S7DeviceIdTextBox.Text, "设备 ID"),
                typedAddress.NormalizedAddress,
                dataType,
                ParseValue(S7WriteValueTextBox.Text, dataType, length),
                length);
        }

        private string GetModbusDeviceId()
        {
            return RequireText(ModbusDeviceIdTextBox.Text, "Modbus 设备 ID");
        }

        private static object ParseValue(string text, DataType dataType, ushort length)
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

        /// <summary>
        /// 解析 S7 输入框中的地址文本。
        /// 这里同时兼容两种输入习惯：
        /// 1. 直接输入标准地址，如 DB200.DBD6。
        /// 2. 直接粘贴 TIA/符号表里的“类型 + 地址”文本，如 DINT %DB200.DBD6、LREAL P#DB200.DBX20.0。
        ///
        /// 之所以要在 UI 层先做这一步，是因为像 DBD/DBB 这样的绝对地址本身并不能唯一说明业务类型：
        /// DBD 既可能是 DINT，也可能是 DWORD 或 REAL；DBB 既可能是 BYTE，也可能是 CHAR。
        /// </summary>
        private static S7AddressInputInfo ParseS7AddressInput(string input)
        {
            var text = RequireText(input, "S7 地址");
            var tokens = text.Split(new[] { ' ', '\t', '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);

            string typeToken = null;
            string addressToken = text;
            if (tokens.Length >= 2 && TryMapS7DeclarationType(tokens[0], out _, out _))
            {
                typeToken = tokens[0];
                addressToken = tokens[tokens.Length - 1];
            }

            var normalizedAddress = NormalizeS7DeclarationAddress(addressToken);
            if (typeToken != null && TryMapS7DeclarationType(typeToken, out var declaredType, out var declaredLength))
            {
                return new S7AddressInputInfo(normalizedAddress, declaredType, declaredLength);
            }

            if (TryInferS7DataTypeFromAddress(normalizedAddress, out var inferredType, out var inferredLength))
            {
                return new S7AddressInputInfo(normalizedAddress, inferredType, inferredLength);
            }

            return new S7AddressInputInfo(normalizedAddress, null, null);
        }

        private void RefreshS7AddressInputState()
        {
            try
            {
                var typedAddress = ParseS7AddressInput(S7AddressTextBox.Text);
                if (typedAddress.InferredDataType.HasValue)
                {
                    SelectDataType(S7DataTypeComboBox, typedAddress.InferredDataType.Value);
                }

                if (typedAddress.InferredLength.HasValue)
                {
                    S7LengthTextBox.Text = typedAddress.InferredLength.Value.ToString(CultureInfo.InvariantCulture);
                }
                else
                {
                    RefreshS7LengthSuggestion();
                }
            }
            catch
            {
                // 输入尚未完成时不打断用户编辑，等真正读写时再报具体错误。
            }
        }

        private void RefreshS7LengthSuggestion()
        {
            S7LengthTextBox.Text = "1";
        }

        private static string NormalizeS7DeclarationAddress(string addressToken)
        {
            var value = RequireText(addressToken, "S7 地址").Trim().ToUpperInvariant();
            if (value.StartsWith("P#", StringComparison.Ordinal))
            {
                value = value.Substring(2);
            }

            if (value.StartsWith("%", StringComparison.Ordinal))
            {
                value = value.Substring(1);
            }

            return value;
        }

        private static bool TryInferS7DataTypeFromAddress(string normalizedAddress, out DataType dataType, out ushort length)
        {
            length = 1;
            var address = (normalizedAddress ?? string.Empty).Trim().ToUpperInvariant();

            if (address.Contains(".DBX") || IsBitAreaAddress(address))
            {
                dataType = DataType.Bool;
                return true;
            }

            dataType = default(DataType);
            length = 0;
            return false;
        }

        private static bool TryMapS7DeclarationType(string typeToken, out DataType dataType, out ushort length)
        {
            length = 1;
            switch ((typeToken ?? string.Empty).Trim().ToUpperInvariant())
            {
                case "BOOL":
                    dataType = DataType.Bool;
                    return true;
                case "BYTE":
                    dataType = DataType.Byte;
                    return true;
                case "CHAR":
                    dataType = DataType.Char;
                    return true;
                case "INT":
                    dataType = DataType.Int16;
                    return true;
                case "WORD":
                    dataType = DataType.UInt16;
                    return true;
                case "DINT":
                    dataType = DataType.Int32;
                    return true;
                case "DWORD":
                    dataType = DataType.UInt32;
                    return true;
                case "REAL":
                    dataType = DataType.Float;
                    return true;
                case "LREAL":
                    dataType = DataType.Double;
                    return true;
                case "STRING":
                    dataType = DataType.String;
                    return true;
                default:
                    dataType = default(DataType);
                    length = 0;
                    return false;
            }
        }

        private static bool StartsWithAny(string value, params string[] prefixes)
        {
            return prefixes.Any(prefix => value.StartsWith(prefix, StringComparison.OrdinalIgnoreCase));
        }

        private static bool IsBitAreaAddress(string value)
        {
            var upper = (value ?? string.Empty).Trim().ToUpperInvariant();
            return (upper.StartsWith("M", StringComparison.Ordinal) ||
                    upper.StartsWith("I", StringComparison.Ordinal) ||
                    upper.StartsWith("Q", StringComparison.Ordinal)) &&
                   upper.Contains(".");
        }

        private static object ParseBoolValue(string text, ushort length)
        {
            var tokens = SplitTokens(text);
            if (length > 1 && tokens.Count > 1)
            {
                return tokens.Select(ParseBooleanToken).ToArray();
            }

            return ParseBooleanToken(text);
        }

        private static bool ParseBooleanToken(string text)
        {
            var token = (text ?? string.Empty).Trim();
            if (string.Equals(token, "1", StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }

            if (string.Equals(token, "0", StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }

            return bool.Parse(token);
        }

        private static byte[] ParseByteArray(string text)
        {
            var input = (text ?? string.Empty).Trim();
            if (input.Length == 0)
            {
                return new byte[0];
            }

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
                {
                    buffer[index] = byte.Parse(compact.Substring(index * 2, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture);
                }

                return buffer;
            }

            return Encoding.ASCII.GetBytes(input);
        }

        private static bool IsHexCharacter(char value)
        {
            return
                (value >= '0' && value <= '9') ||
                (value >= 'a' && value <= 'f') ||
                (value >= 'A' && value <= 'F');
        }

        private static List<string> SplitTokens(string text)
        {
            return (text ?? string.Empty)
                .Split(new[] { ',', ';', ' ', '\r', '\n', '\t' }, StringSplitOptions.RemoveEmptyEntries)
                .ToList();
        }

        private static List<string> SplitAddresses(string text)
        {
            return (text ?? string.Empty)
                .Split(new[] { ',', ';', '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries)
                .Select(item => item.Trim())
                .Where(item => item.Length > 0)
                .ToList();
        }

        private static List<string> SplitBatchWriteValues(string text)
        {
            return (text ?? string.Empty)
                .Split(new[] { ',', ';', '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries)
                .Select(item => item.Trim())
                .Where(item => item.Length > 0)
                .ToList();
        }

        private static bool IsModbusBitAddress(string address)
        {
            if (string.IsNullOrWhiteSpace(address))
            {
                return false;
            }

            switch (char.ToUpperInvariant(address.Trim()[0]))
            {
                case 'X':
                case 'Y':
                case 'M':
                case 'S':
                case 'B':
                    return true;
                default:
                    return false;
            }
        }

        private List<string> ValidateAndGetModbusAddresses()
        {
            var analysis = AnalyzeModbusAddresses();
            if (!analysis.IsValid)
            {
                throw new InvalidOperationException(analysis.ErrorMessage);
            }

            return analysis.Addresses;
        }

        private ModbusAddressInputAnalysis AnalyzeModbusAddresses()
        {
            var addresses = SplitAddresses(ModbusAddressTextBox.Text);
            if (addresses.Count == 0)
            {
                return ModbusAddressInputAnalysis.Empty();
            }

            var parsedAreas = new List<ModbusArea>(addresses.Count);
            foreach (var address in addresses)
            {
                try
                {
                    var parsed = (ModbusAddress)_modbusAddressParser.Parse(address);
                    parsedAreas.Add(parsed.Area);
                }
                catch
                {
                    return ModbusAddressInputAnalysis.Invalid(addresses, "存在无法识别的 Modbus 地址，请检查前缀和编号。");
                }
            }

            var hasBitArea = parsedAreas.Any(IsBitArea);
            var hasRegisterArea = parsedAreas.Any(area => !IsBitArea(area));
            if (hasBitArea && hasRegisterArea)
            {
                return ModbusAddressInputAnalysis.Invalid(addresses, "多地址模式下，位地址和寄存器地址不能混用。");
            }

            return ModbusAddressInputAnalysis.Valid(addresses, hasBitArea);
        }

        private static bool IsBitArea(ModbusArea area)
        {
            return area == ModbusArea.Coil || area == ModbusArea.DiscreteInput;
        }

        private bool IsSingleWriteValueBroadcast(int addressCount)
        {
            if (addressCount <= 1)
            {
                return false;
            }

            return SplitBatchWriteValues(ModbusWriteValueTextBox.Text).Count <= 1;
        }

        private static DataType GetSelectedDataType(ComboBox comboBox)
        {
            var item = comboBox.SelectedItem as ComboBoxItem;
            if (item == null)
            {
                throw new InvalidOperationException("请先选择数据类型。");
            }

            return (DataType)Enum.Parse(typeof(DataType), item.Content.ToString());
        }

        private static void SetEnabledDataTypes(ComboBox comboBox, IReadOnlyCollection<DataType> enabledTypes)
        {
            foreach (var item in comboBox.Items.OfType<ComboBoxItem>())
            {
                DataType parsedType;
                if (Enum.TryParse(item.Content.ToString(), out parsedType))
                {
                    item.IsEnabled = enabledTypes.Contains(parsedType);
                }
            }
        }

        private static void SelectDataType(ComboBox comboBox, DataType dataType)
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

        private static void SetIfNotEmpty(TextBox textBox, string value)
        {
            if (!string.IsNullOrWhiteSpace(value))
            {
                textBox.Text = value;
            }
        }

        private static void SelectComboBoxByContent(ComboBox comboBox, string content)
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

        private static void SelectComboBoxByTag(ComboBox comboBox, string tag)
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

        private static void RefreshAddressHistory(ComboBox comboBox, IReadOnlyCollection<string> addresses)
        {
            comboBox.ItemsSource = null;
            comboBox.ItemsSource = addresses == null ? Array.Empty<string>() : addresses.ToArray();
            comboBox.SelectedIndex = -1;
        }

        private static void ApplyHistorySelection(ComboBox comboBox, TextBox addressTextBox)
        {
            var value = comboBox.SelectedItem as string;
            if (!string.IsNullOrWhiteSpace(value))
            {
                addressTextBox.Text = value;
            }

            comboBox.SelectedIndex = -1;
        }

        private static void RememberRecentAddresses(ICollection<string> target, IEnumerable<string> addresses)
        {
            if (target == null || addresses == null)
            {
                return;
            }

            foreach (var address in addresses)
            {
                RememberRecentAddress(target, address);
            }
        }

        private static void RememberRecentAddress(ICollection<string> target, string address)
        {
            var value = (address ?? string.Empty).Trim();
            if (value.Length == 0)
            {
                return;
            }

            var items = target as List<string>;
            if (items == null)
            {
                return;
            }

            items.RemoveAll(item => string.Equals(item, value, StringComparison.OrdinalIgnoreCase));
            items.Insert(0, value);
            if (items.Count > MaxRecentAddressCount)
            {
                items.RemoveRange(MaxRecentAddressCount, items.Count - MaxRecentAddressCount);
            }
        }

        private static string RequireText(string text, string fieldName)
        {
            var value = (text ?? string.Empty).Trim();
            if (value.Length == 0)
            {
                throw new InvalidOperationException(fieldName + " 不能为空。");
            }

            return value;
        }

        private void RequireMesClient()
        {
            if (_mesClient == null || !_mesClient.IsConnected)
                throw new InvalidOperationException("请先连接 MES。");
        }

        private static Dictionary<string, string> ParseMesParameters(string text)
        {
            var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            foreach (var rawLine in (text ?? string.Empty).Split(new[] { "\r\n", "\n" }, StringSplitOptions.RemoveEmptyEntries))
            {
                var line = rawLine.Trim();
                if (line.Length == 0) continue;
                var separator = line.IndexOf('=');
                if (separator <= 0) throw new InvalidOperationException("MES 参数必须使用 key=value 格式，每行一个。");
                var key = line.Substring(0, separator).Trim();
                if (key.Length == 0) throw new InvalidOperationException("MES 参数名不能为空。");
                result[key] = line.Substring(separator + 1).Trim();
            }
            return result;
        }

        private static Brush ResultBrush(string result)
        {
            if (string.Equals(result, "OK", StringComparison.OrdinalIgnoreCase)) return Brushes.ForestGreen;
            if (string.Equals(result, "NG", StringComparison.OrdinalIgnoreCase)) return Brushes.IndianRed;
            return Brushes.DarkGoldenrod;
        }

        private void AppendMesTraffic(string direction, string message)
        {
            MesTrafficTextBox.AppendText(string.Format(CultureInfo.InvariantCulture, "[{0:HH:mm:ss.fff}] {1} {2}{3}",
                DateTime.Now, direction, message, Environment.NewLine));
            if (MesTrafficTextBox.Text.Length > 50000)
                MesTrafficTextBox.Text = MesTrafficTextBox.Text.Substring(MesTrafficTextBox.Text.Length - 40000);
            MesTrafficTextBox.ScrollToEnd();
        }

        private static int ParseIntValue(string text, string fieldName)
        {
            int value;
            if (!int.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out value))
            {
                throw new InvalidOperationException(fieldName + " 格式无效。");
            }

            return value;
        }

        private static short ParseShortValue(string text, string fieldName)
        {
            short value;
            if (!short.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out value))
            {
                throw new InvalidOperationException(fieldName + " 格式无效。");
            }

            return value;
        }

        private static byte ParseByteValue(string text, string fieldName)
        {
            byte value;
            if (!byte.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out value))
            {
                throw new InvalidOperationException(fieldName + " 格式无效。");
            }

            return value;
        }

        private static ushort ParseUShortValue(string text, string fieldName)
        {
            ushort value;
            if (!ushort.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out value))
            {
                throw new InvalidOperationException(fieldName + " 格式无效。");
            }

            return value;
        }

        private static IPAddress ParseListenAddress(string text)
        {
            var value = RequireText(text, "监听 IP");
            if (value == "0.0.0.0")
            {
                return IPAddress.Any;
            }

            IPAddress address;
            if (!IPAddress.TryParse(value, out address))
            {
                throw new InvalidOperationException("监听 IP 格式无效。");
            }

            return address;
        }

        private bool EnsureClientConnected(IIndustrialClient client, string displayName)
        {
            if (client != null && client.IsConnected)
            {
                return true;
            }

            MessageBox.Show(this, "请先连接 " + displayName + "。", "连接", MessageBoxButton.OK, MessageBoxImage.Information);
            return false;
        }

        private static void UpdateSubscriptionRows(
            ObservableCollection<SubscriptionDisplayRow> rows,
            IReadOnlyList<DataValue> values)
        {
            rows.Clear();
            if (values == null)
            {
                return;
            }

            foreach (var value in values.OrderBy(item => item.Address, StringComparer.OrdinalIgnoreCase))
            {
                rows.Add(new SubscriptionDisplayRow
                {
                    Address = value.Address,
                    DataType = value.DataType.ToString(),
                    ValueText = FormatDisplayValue(value.Value),
                    QualityText = FormatQualityLabel(value.Quality),
                    TimestampText = value.Timestamp.ToLocalTime().ToString("HH:mm:ss.fff", CultureInfo.InvariantCulture),
                    ErrorMessage = value.ErrorMessage ?? string.Empty,
                });
            }
        }

        private static string FormatDataValue(DataValue value)
        {
            if (value == null)
            {
                return "<null>";
            }

            var raw = value.RawData == null ? string.Empty : BitConverter.ToString(value.RawData);
            var text = string.Format(
                CultureInfo.InvariantCulture,
                "{0} | 类型={1} | 值={2} | 质量={3} | 原始={4}",
                value.Address,
                value.DataType,
                FormatDisplayValue(value.Value),
                FormatQualityLabel(value.Quality),
                raw);
            if (!string.IsNullOrWhiteSpace(value.ErrorMessage))
            {
                text += string.Format(CultureInfo.InvariantCulture, " | 错误={0}", value.ErrorMessage);
            }

            return text;
        }

        private static string FormatQuality(QualityStatus quality)
        {
            switch (quality)
            {
                case QualityStatus.Good:
                    return "正常(Good)";
                case QualityStatus.Bad:
                    return "失败(Bad)";
                case QualityStatus.Stale:
                    return "过期(Stale)";
                case QualityStatus.Unknown:
                default:
                    return "未知(Unknown)";
            }
        }

        private static string FormatQualityLabel(QualityStatus quality)
        {
            switch (quality)
            {
                case QualityStatus.Good:
                    return "\u6B63\u5E38 (Good)";
                case QualityStatus.Bad:
                    return "\u5931\u8D25 (Bad)";
                case QualityStatus.Stale:
                    return "\u8FC7\u671F (Stale)";
                case QualityStatus.Unknown:
                default:
                    return "\u672A\u77E5 (Unknown)";
            }
        }

        private static string FormatDisplayValue(object value)
        {
            if (value == null)
            {
                return "<null>";
            }

            if (value is float single)
            {
                return single.ToString("R", CultureInfo.InvariantCulture);
            }

            if (value is double @double)
            {
                return @double.ToString("R", CultureInfo.InvariantCulture);
            }

            if (value is char)
            {
                return value.ToString();
            }

            var bytes = value as byte[];
            if (bytes != null)
            {
                return BitConverter.ToString(bytes);
            }

            if (!(value is string))
            {
                var sequence = value as IEnumerable;
                if (sequence != null)
                {
                    var items = new List<string>();
                    foreach (var item in sequence)
                    {
                        items.Add(Convert.ToString(item, CultureInfo.InvariantCulture));
                    }

                    return string.Join(", ", items);
                }
            }

            return Convert.ToString(value, CultureInfo.InvariantCulture);
        }

        private void AppendLogBatch(IReadOnlyList<string> messages)
        {
            foreach (var message in messages)
            {
                LogTextBox.AppendText(message + Environment.NewLine);
            }

            LogTextBox.ScrollToEnd();
        }

        private void ApplyDataDirectoryButton_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                SaveUiState();
                LogHelper.StoragePathProvider.SetDataRoot(DataDirectoryTextBox.Text);
                _uiStateStore = new UiStateStore();
                _uiStateStore.Save(_uiState);
                DataDirectoryTextBox.Text = LogHelper.StoragePathProvider.DataRoot;
                DataDirectoryHintTextBlock.Text = "已应用：" + LogHelper.StoragePathProvider.DataRoot + "（后续日志、状态和缓存将写入此处）";
                _logger.Info("本地数据目录已切换到 " + LogHelper.StoragePathProvider.DataRoot);
            }
            catch (Exception ex)
            {
                DataDirectoryHintTextBlock.Text = ex.Message;
                DataDirectoryHintTextBlock.Foreground = Brushes.OrangeRed;
                HandleActionError("数据目录设置失败。", ex, true);
            }
        }

        private void AppendSdkLogBatch(IReadOnlyList<string> messages)
        {
            foreach (var message in messages)
                SdkLogTextBox.AppendText(message + Environment.NewLine);
            SdkLogTextBox.ScrollToEnd();
        }

        private void HandleActionError(string summary, Exception exception, bool showDialog)
        {
            _logger.Error(summary, exception);
            SetHeaderStatus(summary, Brushes.OrangeRed);

            if (showDialog)
            {
                MessageBox.Show(this, exception.Message, summary, MessageBoxButton.OK, MessageBoxImage.Warning);
            }
        }

        private void SetHeaderStatus(string text, Brush foreground)
        {
            HeaderStatusTextBlock.Text = text;
            HeaderStatusTextBlock.Foreground = foreground;
        }

        private void RunOnUi(Action action)
        {
            if (Dispatcher.CheckAccess())
            {
                action();
                return;
            }

            Dispatcher.BeginInvoke(action);
        }

    }
}
