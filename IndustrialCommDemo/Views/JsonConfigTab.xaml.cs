using System;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using IndustrialCommSdk;
using IndustrialCommSdk.Abstractions;
using IndustrialCommSdk.Configuration;
using IndustrialCommSdk.Runtime;

namespace IndustrialCommDemo.Views
{
    /// <summary>通过 devices.json、协议 settings JSON 和点位表完成配置、诊断与批量读取。</summary>
    public partial class JsonConfigTab : UserControl
    {
        private readonly ObservableCollection<JsonReadResultRow> _rows = new ObservableCollection<JsonReadResultRow>();
        private readonly ObservableCollection<PointEditorRow> _pointRows = new ObservableCollection<PointEditorRow>();
        private DemoAppContext _ctx;
        private string _deviceConfigPath;
        private string _pointConfigPath;
        private bool _isRefreshingDeviceList;
        private bool _isLoadingDeviceForm;

        private IndustrialSdk Sdk { get { return _ctx.Runtime.Sdk; } }

        public JsonConfigTab()
        {
            InitializeComponent();
            JsonReadResultGrid.ItemsSource = _rows;
            PointEditorGrid.ItemsSource = _pointRows;
            PointTypeColumn.ItemsSource = Enum.GetNames(typeof(DataType));
        }

        public void Initialize(DemoAppContext ctx)
        {
            _ctx = ctx ?? throw new ArgumentNullException(nameof(ctx));
            _deviceConfigPath = _ctx.Runtime.ConfigFilePath;
            LoadConfigFiles();
        }

        private void ReloadConfigButton_Click(object sender, RoutedEventArgs e) { LoadConfigFiles(); }

        private void SaveConfigButton_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                SaveConfigFiles();
                SetStatus("配置已保存：" + _deviceConfigPath, Brushes.ForestGreen);
                _ctx.DemoLogger.Info("JSON 配置已保存。devices=" + _deviceConfigPath + " points=" + _pointConfigPath);
            }
            catch (Exception ex)
            {
                SetStatus("保存配置失败：" + ex.Message, Brushes.IndianRed);
                _ctx.HandleError("保存 JSON 配置失败。", ex, true);
            }
        }

        private void DeviceNameComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (_isRefreshingDeviceList) return;
            try
            {
                LoadDeviceForm();
                LoadSelectedPointConfig();
                SetStatus("已切换点位表：" + _pointConfigPath, Brushes.ForestGreen);
            }
            catch (Exception ex) { SetStatus("切换设备失败：" + ex.Message, Brushes.IndianRed); }
        }

        private void ProtocolComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (_ctx == null || _isLoadingDeviceForm || ProtocolComboBox.SelectedItem == null) return;
            try
            {
                var provider = Sdk.Protocols.Get(GetSelectedProtocol());
                SettingsJsonTextBox.Text = Sdk.Configuration.SerializeSettings(provider.CreateDefaultSettings());
                SetStatus("已加载协议默认 settings，请应用到 JSON。", Brushes.DarkGoldenrod);
            }
            catch (Exception ex) { SetStatus("加载协议 settings 失败：" + ex.Message, Brushes.IndianRed); }
        }

        private void FormatSettingsButton_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                var settings = Sdk.Configuration.ParseSettings(GetSelectedProtocol(), SettingsJsonTextBox.Text);
                SettingsJsonTextBox.Text = Sdk.Configuration.SerializeSettings(settings);
                SetStatus("协议 settings JSON 有效。", Brushes.ForestGreen);
            }
            catch (Exception ex) { _ctx.HandleError("协议 settings JSON 无效。", ex, true); }
        }

        private void ApplyDeviceFormButton_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                var config = Sdk.ParseConfiguration(DeviceJsonTextBox.Text);
                var originalName = GetSelectedDeviceName();
                var device = config.FindDevice(originalName);
                var newName = RequireText(DeviceNameTextBox.Text, "设备名称不能为空。");
                if (config.Devices.Any(item => item != device && string.Equals(item.Name, newName, StringComparison.OrdinalIgnoreCase)))
                    throw new InvalidOperationException("设备名称不能重复：" + newName);

                var protocol = GetSelectedProtocol();
                var settings = Sdk.Configuration.ParseSettings(protocol, SettingsJsonTextBox.Text);
                var errors = Sdk.Protocols.Get(protocol).Validate(settings);
                if (errors.Count > 0) throw new InvalidOperationException(string.Join(Environment.NewLine, errors));

                device.Name = newName;
                device.Protocol = protocol;
                device.DeviceId = EmptyToNull(DeviceIdTextBox.Text);
                device.PointsFile = RequireText(PointsFileTextBox.Text, "点位文件不能为空。");
                device.Enabled = EnabledCheckBox.IsChecked != false;
                device.Runtime = new IndustrialDeviceRuntimeOptions
                {
                    PollingIntervalMilliseconds = ParsePositiveInt(PollingIntervalTextBox.Text, "轮询周期"),
                    ReconnectDelayMilliseconds = ParsePositiveInt(ReconnectDelayTextBox.Text, "重连周期"),
                    OperationTimeoutMilliseconds = ParsePositiveInt(OperationTimeoutTextBox.Text, "操作超时"),
                };
                device.Settings = settings;

                DeviceJsonTextBox.Text = Sdk.SerializeConfiguration(config);
                RefreshDeviceList(config);
                DeviceNameComboBox.SelectedItem = newName;
                SetStatus("公共参数和 settings 已应用，请保存配置。", Brushes.ForestGreen);
            }
            catch (Exception ex) { _ctx.HandleError("应用设备参数失败。", ex, true); }
        }

        private void AddDeviceButton_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                var config = Sdk.ParseConfiguration(DeviceJsonTextBox.Text);
                var index = 1;
                string name;
                do { name = "device" + index++; }
                while (config.Devices.Any(item => item != null && string.Equals(item.Name, name, StringComparison.OrdinalIgnoreCase)));
                var provider = Sdk.Protocols.Get("modbus-tcp");
                config.Devices.Add(new IndustrialDeviceConfig
                {
                    Name = name,
                    Protocol = provider.Protocol,
                    PointsFile = "points/" + name + ".json",
                    Enabled = false,
                    Runtime = new IndustrialDeviceRuntimeOptions(),
                    Settings = provider.CreateDefaultSettings(),
                });
                DeviceJsonTextBox.Text = Sdk.SerializeConfiguration(config);
                RefreshDeviceList(config);
                DeviceNameComboBox.SelectedItem = name;
                LoadDeviceForm();
                SetStatus("已新增 " + name + "，请完善 settings 和点位文件后保存。", Brushes.ForestGreen);
            }
            catch (Exception ex) { _ctx.HandleError("新增设备失败。", ex, true); }
        }

        private void DeleteDeviceButton_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                var config = Sdk.ParseConfiguration(DeviceJsonTextBox.Text);
                if (config.Devices.Count <= 1) throw new InvalidOperationException("至少需要保留一台设备。");
                var name = GetSelectedDeviceName();
                config.Devices.Remove(config.FindDevice(name));
                DeviceJsonTextBox.Text = Sdk.SerializeConfiguration(config);
                RefreshDeviceList(config);
                LoadSelectedPointConfig(true, false);
                SetStatus("已从配置中删除 " + name + "，保存后生效。", Brushes.DarkGoldenrod);
            }
            catch (Exception ex) { _ctx.HandleError("删除设备失败。", ex, true); }
        }

        private void ValidateConfigButton_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                SaveConfigFiles();
                var config = Sdk.LoadConfiguration(_deviceConfigPath);
                var result = config.Validate(Path.GetDirectoryName(_deviceConfigPath), Sdk.Protocols, _ctx.SdkLogger);
                if (!result.IsValid) throw new InvalidOperationException(string.Join(Environment.NewLine, result.Errors.Take(8)));
                SetStatus("配置校验通过，共 " + config.Devices.Count + " 台设备。", Brushes.ForestGreen);
            }
            catch (Exception ex) { _ctx.HandleError("JSON 配置校验失败。", ex, true); }
        }

        private async void TestConnectionButton_Click(object sender, RoutedEventArgs e)
        {
            await RunAsync("JSON 连接测试", async () =>
            {
                using (var device = OpenConfiguredDevice())
                {
                    var report = await device.Client.TestAsync();
                    SetStatus(report.ToString(), report.IsSuccess ? Brushes.ForestGreen : Brushes.IndianRed);
                }
            });
        }

        private async void ReadPointsButton_Click(object sender, RoutedEventArgs e)
        {
            await RunAsync("JSON 点位批量读取", async () =>
            {
                SaveConfigFiles();
                _rows.Clear();
                using (var device = OpenConfiguredDevice())
                {
                    await device.ConnectAsync();
                    try
                    {
                        var values = await device.ReadManyAsync();
                        for (var index = 0; index < device.Tags.Tags.Count; index++)
                        {
                            var tag = device.Tags.Tags[index];
                            var value = values.Values[index];
                            _rows.Add(new JsonReadResultRow
                            {
                                Name = tag.Name, Address = tag.Address, Type = tag.DataType.ToString(),
                                Value = value.Value == null ? string.Empty : value.Value.ToString(),
                                Quality = value.Quality.ToString(), Error = value.ErrorMessage,
                            });
                        }
                    }
                    finally { await device.DisconnectAsync(); }
                }
                SetStatus("批量读取完成，共 " + _rows.Count + " 个点位。", Brushes.ForestGreen);
            });
        }

        private IndustrialConfiguredClient OpenConfiguredDevice()
        {
            SaveDeviceConfig();
            return Sdk.Open(_deviceConfigPath, GetSelectedDeviceName());
        }

        private void SaveConfigFiles()
        {
            var config = SaveDeviceConfig();
            RefreshDeviceList(config);
            _pointConfigPath = ResolveSelectedPointConfigPath();
            if (PointEditorTabControl.SelectedIndex == 0) ApplyPointRowsToJson();
            var table = TagTable.FromJson(PointJsonTextBox.Text);
            table.SaveJson(_pointConfigPath);
            PointJsonTextBox.Text = table.ToJson();
            PointConfigGroupBox.Header = GetPointConfigDisplayName(_pointConfigPath);
        }

        private IndustrialSdkConfig SaveDeviceConfig()
        {
            var config = Sdk.ParseConfiguration(DeviceJsonTextBox.Text);
            foreach (var device in config.Devices)
            {
                var provider = Sdk.Protocols.Get(device.Protocol);
                var errors = provider.Validate(device.Settings);
                if (errors.Count > 0)
                    throw new InvalidOperationException("设备 '" + device.Name + "' settings 无效：" +
                        string.Join(Environment.NewLine, errors));
            }
            Sdk.SaveConfiguration(config, _deviceConfigPath);
            DeviceJsonTextBox.Text = Sdk.SerializeConfiguration(config);
            return config;
        }

        private async Task RunAsync(string actionName, Func<Task> action)
        {
            try { SetButtonsEnabled(false); SetStatus("正在执行：" + actionName + "...", Brushes.DarkGoldenrod); await action(); }
            catch (Exception ex) { SetStatus(actionName + "失败：" + ex.Message, Brushes.IndianRed); _ctx.HandleError(actionName + "失败。", ex, true); }
            finally { SetButtonsEnabled(true); }
        }

        private void SetButtonsEnabled(bool enabled)
        {
            DeviceJsonTextBox.IsEnabled = enabled;
            SettingsJsonTextBox.IsEnabled = enabled;
            PointJsonTextBox.IsEnabled = enabled;
            DeviceNameComboBox.IsEnabled = enabled;
        }

        private void SetStatus(string text, Brush foreground)
        {
            JsonConfigStatusTextBlock.Text = text;
            JsonConfigStatusTextBlock.Foreground = foreground;
            _ctx.SetHeaderStatus(text, foreground);
        }
    }
}
