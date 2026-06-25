using System;
using System.Collections;
using System.Collections.ObjectModel;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Net;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using IndustrialCommDemo.Services;
using IndustrialCommDemo.SocketDebug;
using IndustrialCommSdk;
using IndustrialCommSdk.Abstractions;
using IndustrialCommSdk.Protocols.Mc;
using IndustrialCommSdk.Protocols.Modbus;
using IndustrialCommSdk.Protocols.S7;
using IndustrialCommSdk.Storage;
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
        private ModbusAddressParser _modbusAddressParser = new ModbusAddressParser(ModbusDeviceProfiles.InovanceEasyPlc);
        private IModbusDeviceProfile _modbusProfile = ModbusDeviceProfiles.InovanceEasyPlc;
        private readonly AppLogger _logger;
        private readonly UiStateStore _uiStateStore;
        private DemoUiState _uiState;
        private readonly ObservableCollection<SubscriptionDisplayRow> _modbusSubscriptionRows = new ObservableCollection<SubscriptionDisplayRow>();

        private IIndustrialClient _modbusClient;
        private string _modbusSubscriptionId;
        private IIndustrialClient _s7Client;
        private IIndustrialClient _mcClient;
        private LineBasedTcpServer _socketServer;
        private LineBasedTcpClient _socketClient;
        private BufferedIndustrialDataRecorder _databaseRecorder;
        private int _databaseRecordingEnabled;

        public MainWindow()
        {
            InitializeComponent();

            _logger = new AppLogger(Dispatcher, AppendLogBatch);
            _uiStateStore = new UiStateStore();
            _uiState = _uiStateStore.Load();

            SelectDataType(ModbusDataTypeComboBox, DataType.Int16);
            ApplyModbusProfile();
            LoadUiState();
            RefreshModbusDataTypeState();
            RefreshModbusInputHints();
            RefreshS7AddressInputState();
            UpdateAllStatuses();
            RefreshAddressHistoryCombos();
            ModbusSubscriptionDataGrid.ItemsSource = _modbusSubscriptionRows;

            SetHeaderStatus("就绪", Brushes.LightGreen);
            _logger.Info("工业通讯演示已就绪。");
        }

        private async void ModbusConnectButton_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                await ResetModbusClientAsync();

                var options = new ModbusTcpClientOptions
                {
                    DeviceId = RequireText(ModbusDeviceIdTextBox.Text, "Modbus 设备 ID"),
                    Host = RequireText(ModbusHostTextBox.Text, "Modbus 主机"),
                    Port = ParseIntValue(ModbusPortTextBox.Text, "Modbus 端口"),
                    SlaveId = ParseByteValue(ModbusSlaveIdTextBox.Text, "Modbus 从站 ID"),
                    DeviceProfile = _modbusProfile,
                };

                _modbusClient = IndustrialClientFactory.CreateModbus(options, _logger);
                await _modbusClient.ConnectAsync(CancellationToken.None);

                UpdateModbusStatus();
                SetHeaderStatus("Modbus 已连接", Brushes.LightGreen);
                _logger.Info(string.Format("Modbus 已连接到 {0}:{1}。", options.Host, options.Port));
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

                _s7Client = IndustrialClientFactory.CreateSiemensS7(options, _logger);
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

                _mcClient = IndustrialClientFactory.CreateMitsubishiMc(options, _logger);
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

        private async void DatabaseStartButton_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                await StopDatabaseRecorderAsync();

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
                    _logger);

                try
                {
                    await recorder.StartAsync(CancellationToken.None);
                }
                catch
                {
                    recorder.Dispose();
                    throw;
                }

                _databaseRecorder = recorder;
                Interlocked.Exchange(ref _databaseRecordingEnabled, 1);
                DatabaseEnabledCheckBox.IsChecked = true;
                DatabaseStatusTextBlock.Text = "已连接，正在后台记录读取和轮询数据。";
                DatabaseStatusTextBlock.Foreground = Brushes.ForestGreen;
                SetHeaderStatus("数据库记录已启用", Brushes.LightGreen);
                _logger.Info("SQL Server 历史数据记录已启用，数据表=" + options.TableName + "。");
            }
            catch (Exception ex)
            {
                DatabaseEnabledCheckBox.IsChecked = false;
                Interlocked.Exchange(ref _databaseRecordingEnabled, 0);
                DatabaseStatusTextBlock.Text = "连接失败：" + ex.Message;
                DatabaseStatusTextBlock.Foreground = Brushes.IndianRed;
                HandleActionError("数据库连接失败。", ex, true);
            }
        }

        private async void DatabaseStopButton_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                await StopDatabaseRecorderAsync();
                DatabaseEnabledCheckBox.IsChecked = false;
                DatabaseStatusTextBlock.Text = "已停止";
                DatabaseStatusTextBlock.Foreground = Brushes.DarkGoldenrod;
                _logger.Info("SQL Server 历史数据记录已停止。");
            }
            catch (Exception ex)
            {
                HandleActionError("停止数据库记录失败。", ex, true);
            }
        }

        private void QueueDatabaseValues(IIndustrialClient client, IReadOnlyCollection<DataValue> values)
        {
            var recorder = _databaseRecorder;
            if (recorder == null || Volatile.Read(ref _databaseRecordingEnabled) == 0 || client == null || values == null || values.Count == 0)
            {
                return;
            }

            try
            {
                recorder.TryRecord(client.Kind, client.DeviceId, values);
            }
            catch (Exception ex)
            {
                // 写库只是旁路能力；即使数据转换或队列操作异常，也不能反向破坏通信回调。
                _logger.Error("采集结果加入数据库队列失败。", ex);
            }
        }

        private async Task StopDatabaseRecorderAsync()
        {
            var recorder = _databaseRecorder;
            _databaseRecorder = null;
            Interlocked.Exchange(ref _databaseRecordingEnabled, 0);
            if (recorder == null)
            {
                return;
            }

            try
            {
                await recorder.StopAsync(CancellationToken.None);
            }
            finally
            {
                recorder.Dispose();
            }
        }

        private void ClearLogButton_Click(object sender, RoutedEventArgs e)
        {
            LogTextBox.Clear();
            _logger.Info("日志已清空。");
        }

        protected override async void OnClosed(EventArgs e)
        {
            SaveUiState();
            await ResetModbusClientAsync();
            await ResetS7ClientAsync();
            await ResetMcClientAsync();
            await ResetSocketClientAsync();
            await ResetSocketServerAsync();
            await StopDatabaseRecorderAsync();
            _logger.Dispose();
            base.OnClosed(e);
        }

        private void OnModbusSubscriptionReceived(object sender, SubscriptionEvent e)
        {
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

        private void UpdateAllStatuses()
        {
            UpdateModbusStatus();
            UpdateSocketServerStatus();
            UpdateSocketClientStatus();
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

        private void LoadUiState()
        {
            ApplyModbusState();
            ApplySocketState();
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

            _uiState.Socket.ServerIp = SocketServerIpTextBox.Text;
            _uiState.Socket.ServerPort = SocketServerPortTextBox.Text;
            _uiState.Socket.ClientHost = SocketClientHostTextBox.Text;
            _uiState.Socket.ClientPort = SocketClientPortTextBox.Text;
            _uiState.Socket.EchoEnabled = SocketServerEchoCheckBox.IsChecked ?? true;
            _uiState.Socket.ServerMessage = SocketServerMessageTextBox.Text;
            _uiState.Socket.ClientMessage = SocketClientMessageTextBox.Text;

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

            _uiStateStore.Save(_uiState);
        }

        private void ApplyModbusState()
        {
            var state = _uiState.Modbus ?? new ModbusUiState();
            SetIfNotEmpty(ModbusDeviceIdTextBox, state.DeviceId);
            SetIfNotEmpty(ModbusHostTextBox, state.Host);
            SetIfNotEmpty(ModbusPortTextBox, state.Port);
            SetIfNotEmpty(ModbusSlaveIdTextBox, state.SlaveId);
            SelectModbusModel(state.ModelKey);
            SetIfNotEmpty(ModbusAddressTextBox, state.Address);
            SetIfNotEmpty(ModbusLengthTextBox, state.Length);
            SetIfNotEmpty(ModbusWriteValueTextBox, state.WriteValue);
            SetIfNotEmpty(ModbusPollIntervalTextBox, state.PollInterval);
        }

        private IModbusDeviceProfile GetSelectedModbusProfile()
        {
            var selectedItem = ModbusModelComboBox.SelectedItem as ComboBoxItem;
            var key = selectedItem != null ? selectedItem.Tag as string : null;
            if (string.IsNullOrWhiteSpace(key))
            {
                return ModbusDeviceProfiles.InovanceEasyPlc;
            }

            return ModbusDeviceProfiles.All.FirstOrDefault(profile => string.Equals(profile.Key, key, StringComparison.OrdinalIgnoreCase))
                ?? ModbusDeviceProfiles.InovanceEasyPlc;
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

        private void ApplyDatabaseState()
        {
            var state = _uiState.Database ?? new DatabaseUiState();
            SetIfNotEmpty(DatabaseConnectionStringTextBox, state.ConnectionString);
            SetIfNotEmpty(DatabaseTableNameTextBox, state.TableName);
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
                    analysis.IsBitFamily ? "位地址模式，只允许 Bool" : "寄存器地址模式，可读写寄存器类型",
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
                ModbusWriteHintTextBlock.Text = "单地址写入沿用当前数据类型和长度。";
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

        private sealed class S7AddressInputInfo
        {
            public S7AddressInputInfo(string normalizedAddress, DataType? inferredDataType, ushort? inferredLength)
            {
                NormalizedAddress = normalizedAddress;
                InferredDataType = inferredDataType;
                InferredLength = inferredLength;
            }

            public string NormalizedAddress { get; private set; }
            public DataType? InferredDataType { get; private set; }
            public ushort? InferredLength { get; private set; }
        }

        private void AppendLogBatch(IReadOnlyList<string> messages)
        {
            foreach (var message in messages)
            {
                LogTextBox.AppendText(message + Environment.NewLine);
            }

            LogTextBox.ScrollToEnd();
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

        private sealed class SubscriptionDisplayRow
        {
            public string Address { get; set; }
            public string DataType { get; set; }
            public string ValueText { get; set; }
            public string QualityText { get; set; }
            public string TimestampText { get; set; }
            public string ErrorMessage { get; set; }
        }

        private sealed class ModbusAddressInputAnalysis
        {
            public List<string> Addresses { get; private set; }
            public bool IsValid { get; private set; }
            public bool IsBitFamily { get; private set; }
            public string ErrorMessage { get; private set; }
            public int AddressCount
            {
                get { return Addresses == null ? 0 : Addresses.Count; }
            }

            public static ModbusAddressInputAnalysis Empty()
            {
                return new ModbusAddressInputAnalysis
                {
                    Addresses = new List<string>(),
                    IsValid = true,
                };
            }

            public static ModbusAddressInputAnalysis Valid(List<string> addresses, bool isBitFamily)
            {
                return new ModbusAddressInputAnalysis
                {
                    Addresses = addresses,
                    IsValid = true,
                    IsBitFamily = isBitFamily,
                };
            }

            public static ModbusAddressInputAnalysis Invalid(List<string> addresses, string errorMessage)
            {
                return new ModbusAddressInputAnalysis
                {
                    Addresses = addresses ?? new List<string>(),
                    IsValid = false,
                    ErrorMessage = errorMessage,
                };
            }
        }
    }
}
