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

namespace IndustrialCommDemo.Views
{
    /// <summary>
    /// 演示仅通过 devices.json 和设备点位表完成客户端创建、诊断与批量读取。
    /// 配置文件从程序输出目录的 Config 文件夹读取，便于直接部署和修改。
    /// </summary>
    public partial class JsonConfigTab : UserControl
    {
        private readonly ObservableCollection<JsonReadResultRow> _rows = new ObservableCollection<JsonReadResultRow>();
        private readonly ObservableCollection<PointEditorRow> _pointRows = new ObservableCollection<PointEditorRow>();
        private DemoAppContext _ctx;
        private string _deviceConfigPath;
        private string _pointConfigPath;
        private bool _isRefreshingDeviceList;

        public JsonConfigTab()
        {
            InitializeComponent();
            JsonReadResultGrid.ItemsSource = _rows;
            PointEditorGrid.ItemsSource = _pointRows;
            PointTypeColumn.ItemsSource = Enum.GetNames(typeof(DataType));
        }

        /// <summary>绑定共享上下文并在页面启动时加载 JSON 配置。</summary>
        public void Initialize(DemoAppContext ctx)
        {
            _ctx = ctx ?? throw new ArgumentNullException(nameof(ctx));
            _deviceConfigPath = _ctx.Runtime.ConfigFilePath;
            LoadConfigFiles();
        }

        // 丢弃编辑器中的未保存修改，重新读取磁盘文件。
        private void ReloadConfigButton_Click(object sender, RoutedEventArgs e)
        {
            LoadConfigFiles();
        }

        // 同时保存设备列表和当前设备关联的点位表。
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

        // 从 JSON 设备列表选择设备，避免手工输入名称造成找不到配置的错误。
        private void DeviceNameComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (_isRefreshingDeviceList)
            {
                return;
            }

            try
            {
                LoadDeviceForm();
                LoadSelectedPointConfig();
                SetStatus("已切换点位表：" + _pointConfigPath, Brushes.ForestGreen);
            }
            catch (Exception ex)
            {
                SetStatus("切换设备失败：" + ex.Message, Brushes.IndianRed);
            }
        }

        // 常用参数表单只修改 SDK 配置模型，再由 SDK 统一序列化回 JSON。
        private void ApplyDeviceFormButton_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                var config = IndustrialSdkConfig.FromJson(DeviceJsonTextBox.Text);
                var originalName = GetSelectedDeviceName();
                var device = config.FindDevice(originalName);
                var newName = RequireText(DeviceNameTextBox.Text, "设备名称不能为空。");

                if (config.Devices.Any(item => item != device && string.Equals(item.Name, newName, StringComparison.OrdinalIgnoreCase)))
                    throw new InvalidOperationException("设备名称不能重复：" + newName);

                device.Name = newName;
                device.Protocol = GetSelectedProtocol();
                device.Host = EmptyToNull(HostTextBox.Text);
                device.Port = ParseOptionalInt(PortTextBox.Text, "TCP 端口");
                device.PortName = EmptyToNull(PortNameTextBox.Text);
                device.SlaveId = ParseOptionalByte(SlaveIdTextBox.Text, "从站号");
                device.PointsFile = RequireText(PointsFileTextBox.Text, "点位文件不能为空。");
                device.PollingIntervalMilliseconds = ParseOptionalInt(PollingIntervalTextBox.Text, "轮询周期");
                device.ReconnectDelayMilliseconds = ParseOptionalInt(ReconnectDelayTextBox.Text, "重连周期");
                device.Enabled = EnabledCheckBox.IsChecked != false;

                DeviceJsonTextBox.Text = config.ToJson();
                RefreshDeviceList(config);
                DeviceNameComboBox.SelectedItem = newName;
                SetStatus("常用参数已应用，请点击“保存配置”写入磁盘。", Brushes.ForestGreen);
            }
            catch (Exception ex)
            {
                SetStatus("应用设备参数失败：" + ex.Message, Brushes.IndianRed);
                _ctx.HandleError("应用设备参数失败。", ex, true);
            }
        }

        private void AddDeviceButton_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                var config = IndustrialSdkConfig.FromJson(DeviceJsonTextBox.Text);
                var index = 1;
                string name;
                do { name = "device" + index++; }
                while (config.Devices.Any(item => item != null && string.Equals(item.Name, name, StringComparison.OrdinalIgnoreCase)));

                config.Devices.Add(new IndustrialDeviceConfig
                {
                    Name = name,
                    Protocol = "modbus-tcp",
                    Host = "127.0.0.1",
                    Port = 502,
                    SlaveId = 1,
                    PointsFile = "points/" + name + ".json",
                    Enabled = false,
                    PollingIntervalMilliseconds = 1000,
                    ReconnectDelayMilliseconds = 3000,
                });

                DeviceJsonTextBox.Text = config.ToJson();
                RefreshDeviceList(config);
                DeviceNameComboBox.SelectedItem = name;
                LoadDeviceForm();
                SetStatus("已新增 " + name + "，请完善参数和点位文件后保存。", Brushes.ForestGreen);
            }
            catch (Exception ex)
            {
                _ctx.HandleError("新增设备失败。", ex, true);
            }
        }

        private void DeleteDeviceButton_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                var config = IndustrialSdkConfig.FromJson(DeviceJsonTextBox.Text);
                if (config.Devices.Count <= 1) throw new InvalidOperationException("至少需要保留一台设备。");
                var name = GetSelectedDeviceName();
                config.Devices.Remove(config.FindDevice(name));
                DeviceJsonTextBox.Text = config.ToJson();
                RefreshDeviceList(config);
                LoadDeviceForm();
                LoadSelectedPointConfig(true, false);
                SetStatus("已从配置中删除 " + name + "，保存后生效。", Brushes.DarkGoldenrod);
            }
            catch (Exception ex)
            {
                _ctx.HandleError("删除设备失败。", ex, true);
            }
        }

        // 保存前执行离线校验，检查协议参数、点位文件和点位 JSON，不连接 PLC。
        private void ValidateConfigButton_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                SaveConfigFiles();
                var config = IndustrialSdkConfig.Load(_deviceConfigPath);
                var result = config.Validate(
                    Path.GetDirectoryName(_deviceConfigPath),
                    device => IndustrialClientFactory.FromConfig(device));
                if (result.IsValid)
                {
                    SetStatus("配置校验通过，共 " + config.Devices.Count + " 台设备。", Brushes.ForestGreen);
                    _ctx.DemoLogger.Info("JSON 配置校验通过。devices=" + config.Devices.Count);
                    return;
                }

                var message = string.Join(Environment.NewLine, result.Errors.Take(5));
                if (result.Errors.Count > 5)
                {
                    message += Environment.NewLine + "其余 " + (result.Errors.Count - 5) + " 项错误请查看日志。";
                }

                SetStatus("配置校验失败：" + Environment.NewLine + message, Brushes.IndianRed);
                foreach (var error in result.Errors)
                {
                    _ctx.DemoLogger.Warn("JSON 配置校验：" + error);
                }
            }
            catch (Exception ex)
            {
                SetStatus("配置校验失败：" + ex.Message, Brushes.IndianRed);
                _ctx.HandleError("JSON 配置校验失败。", ex, true);
            }
        }

        // 连接诊断自动创建、连接并释放客户端，不执行点位读写。
        private async void TestConnectionButton_Click(object sender, RoutedEventArgs e)
        {
            await RunAsync("JSON 连接测试", async () =>
            {
                using (var device = OpenConfiguredDevice())
                {
                    var report = await device.Client.TestAsync();
                    SetStatus(report.ToString(), report.IsSuccess ? Brushes.ForestGreen : Brushes.IndianRed);
                    _ctx.DemoLogger.Info(report.ToString());
                }
            });
        }

        // 返回值与 Tags 使用相同索引，逐项显示值、质量和错误。
        private async void ReadPointsButton_Click(object sender, RoutedEventArgs e)
        {
            await RunAsync("JSON 点位批量读取", async () =>
            {
                SaveConfigFiles();
                _rows.Clear();
                using (var device = OpenConfiguredDevice())
                {
                    var table = device.Tags;
                    await device.ConnectAsync();
                    try
                    {
                        var values = await device.ReadManyAsync();
                        for (var i = 0; i < table.Tags.Count; i++)
                        {
                            var tag = table.Tags[i];
                            var value = values.Values[i];
                            _rows.Add(new JsonReadResultRow
                            {
                                Name = tag.Name,
                                Address = tag.Address,
                                Type = tag.DataType.ToString(),
                                Value = value.Value == null ? string.Empty : value.Value.ToString(),
                                Quality = value.Quality.ToString(),
                                Error = value.ErrorMessage,
                            });
                        }
                    }
                    finally
                    {
                        await device.DisconnectAsync();
                    }
                }

                SetStatus("批量读取完成，共 " + _rows.Count + " 个点位。", Brushes.ForestGreen);
            });
        }

        // 从 JSON 同时装配协议客户端和点位表，Demo 与 SDK 用户走同一条部署路径。
        private IndustrialConfiguredClient OpenConfiguredDevice()
        {
            var config = SaveDeviceConfig();
            RefreshDeviceList(config);
            return IndustrialDeployment.Open(_deviceConfigPath, GetSelectedDeviceName(), _ctx.SdkLogger);
        }

        // 操作前先落盘，保证 Demo 行为与实际部署读取文件时一致。
        private void SaveConfigFiles()
        {
            var config = SaveDeviceConfig();
            RefreshDeviceList(config);
            _pointConfigPath = ResolveSelectedPointConfigPath();
            if (PointEditorTabControl.SelectedIndex == 0) ApplyPointRowsToJson();
            TagTable.FromJson(PointJsonTextBox.Text);
            TagTable.FromJson(PointJsonTextBox.Text).SaveJson(_pointConfigPath);
            PointJsonTextBox.Text = TagTable.FromJson(PointJsonTextBox.Text).ToJson();
            PointConfigGroupBox.Header = GetPointConfigDisplayName(_pointConfigPath);
        }

        private IndustrialSdkConfig SaveDeviceConfig()
        {
            var config = IndustrialSdkConfig.FromJson(DeviceJsonTextBox.Text);
            config.Save(_deviceConfigPath);
            DeviceJsonTextBox.Text = config.ToJson();
            return config;
        }

        // 统一处理按钮状态、执行提示和异常记录。
        private async Task RunAsync(string actionName, Func<Task> action)
        {
            try
            {
                SetButtonsEnabled(false);
                SetStatus("正在执行：" + actionName + "...", Brushes.DarkGoldenrod);
                await action();
            }
            catch (Exception ex)
            {
                SetStatus(actionName + "失败：" + ex.Message, Brushes.IndianRed);
                _ctx.HandleError(actionName + "失败。", ex, true);
            }
            finally
            {
                SetButtonsEnabled(true);
            }
        }

        private void SetButtonsEnabled(bool enabled)
        {
            DeviceJsonTextBox.IsEnabled = enabled;
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
