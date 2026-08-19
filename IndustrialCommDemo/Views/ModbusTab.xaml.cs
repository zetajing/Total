using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Globalization;
using System.Linq;
using System.Threading;
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
            RefreshCapabilityText();
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
                RefreshCapabilityText();
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
                RefreshCapabilityText();
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
                RefreshCapabilityText();
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
                RefreshCapabilityText();
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
                RefreshCapabilityText();
            });
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

    }
}
