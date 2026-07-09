using System;
using System.Collections.Generic;
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
        private DemoAppContext _ctx;
        private string _deviceConfigPath;
        private string _pointConfigPath;
        private bool _isRefreshingDeviceList;

        public JsonConfigTab()
        {
            InitializeComponent();
            JsonReadResultGrid.ItemsSource = _rows;
        }

        /// <summary>绑定共享上下文并在页面启动时加载 JSON 配置。</summary>
        public void Initialize(DemoAppContext ctx)
        {
            _ctx = ctx ?? throw new ArgumentNullException(nameof(ctx));
            _deviceConfigPath = ResolveConfigPath("devices.json");
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
                LoadSelectedPointConfig();
                SetStatus("已切换点位表：" + _pointConfigPath, Brushes.ForestGreen);
            }
            catch (Exception ex)
            {
                SetStatus("切换设备失败：" + ex.Message, Brushes.IndianRed);
            }
        }

        // 保存前执行离线校验，检查协议参数、点位文件和点位 JSON，不连接 PLC。
        private void ValidateConfigButton_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                SaveConfigFiles();
                var config = IndustrialSdkConfig.Load(_deviceConfigPath);
                var result = config.Validate(Path.GetDirectoryName(_deviceConfigPath));
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
                using (var client = CreateClientFromJson())
                {
                    var report = await client.TestAsync();
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
                var table = LoadPointTable();
                _rows.Clear();
                using (var client = CreateClientFromJson())
                {
                    await client.ConnectAsync();
                    try
                    {
                        var values = await client.ReadManyAsync(table.Tags);
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
                        await client.DisconnectAsync();
                    }
                }

                SetStatus("批量读取完成，共 " + _rows.Count + " 个点位。", Brushes.ForestGreen);
            });
        }

        // 工厂根据 protocol 字段自动选择 Modbus、S7 或 MC 客户端。
        private IIndustrialClient CreateClientFromJson()
        {
            var config = SaveDeviceConfig();
            RefreshDeviceList(config);
            return IndustrialClientFactory.FromConfig(_deviceConfigPath, GetSelectedDeviceName(), _ctx.SdkLogger);
        }

        // 操作前先落盘，保证 Demo 行为与实际部署读取文件时一致。
        private void SaveConfigFiles()
        {
            var config = SaveDeviceConfig();
            RefreshDeviceList(config);
            _pointConfigPath = ResolveSelectedPointConfigPath();
            TagTable.FromJson(PointJsonTextBox.Text);
            Directory.CreateDirectory(Path.GetDirectoryName(_pointConfigPath));
            File.WriteAllText(_pointConfigPath, PointJsonTextBox.Text);
            PointConfigGroupBox.Header = GetPointConfigDisplayName(_pointConfigPath);
        }

        private IndustrialSdkConfig SaveDeviceConfig()
        {
            var config = IndustrialSdkConfig.FromJson(DeviceJsonTextBox.Text);
            Directory.CreateDirectory(Path.GetDirectoryName(_deviceConfigPath));
            File.WriteAllText(_deviceConfigPath, DeviceJsonTextBox.Text);
            return config;
        }

        // 通过 devices.json 的 pointsFile 一步加载当前设备点位表。
        private TagTable LoadPointTable()
        {
            LoadSelectedPointConfig();
            return TagTable.LoadForDevice(_deviceConfigPath, GetSelectedDeviceName());
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

        private void LoadConfigFiles()
        {
            try
            {
                EnsureConfigFileExists(_deviceConfigPath, "devices.json");
                DeviceJsonTextBox.Text = File.ReadAllText(_deviceConfigPath);
                RefreshDeviceList(IndustrialSdkConfig.FromJson(DeviceJsonTextBox.Text));
                LoadSelectedPointConfig(true, false);
                _rows.Clear();
                SetStatus("已读取配置：" + _deviceConfigPath, Brushes.ForestGreen);
            }
            catch (Exception ex)
            {
                SetStatus("读取配置失败：" + ex.Message, Brushes.IndianRed);
                _ctx.HandleError("读取 JSON 配置失败。", ex, true);
            }
        }

        private static string ResolveConfigPath(string fileName)
        {
            return Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Config", fileName);
        }

        private string ResolveSelectedPointConfigPath()
        {
            var config = IndustrialSdkConfig.FromJson(DeviceJsonTextBox.Text);
            var device = config.FindDevice(GetSelectedDeviceName());
            return device.ResolvePointsFile(Path.GetDirectoryName(_deviceConfigPath));
        }

        private void RefreshDeviceList(IndustrialSdkConfig config)
        {
            if (config == null) throw new ArgumentNullException(nameof(config));

            var currentName = DeviceNameComboBox.SelectedItem as string;
            var names = config.Devices
                .Where(device => device != null && !string.IsNullOrWhiteSpace(device.Name))
                .Select(device => device.Name)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();

            if (names.Count == 0)
            {
                throw new InvalidOperationException("devices 至少需要配置一台带 name 的设备。");
            }

            _isRefreshingDeviceList = true;
            try
            {
                DeviceNameComboBox.ItemsSource = names;
                DeviceNameComboBox.SelectedItem = names.FirstOrDefault(name => string.Equals(name, currentName, StringComparison.OrdinalIgnoreCase)) ?? names[0];
            }
            finally
            {
                _isRefreshingDeviceList = false;
            }
        }

        private string GetSelectedDeviceName()
        {
            var name = DeviceNameComboBox.SelectedItem as string;
            if (string.IsNullOrWhiteSpace(name))
            {
                throw new InvalidOperationException("请先从设备列表选择一台设备。");
            }

            return name;
        }

        private void LoadSelectedPointConfig(bool forceReload = false, bool saveCurrent = true)
        {
            var selectedPath = ResolveSelectedPointConfigPath();
            if (!forceReload && string.Equals(selectedPath, _pointConfigPath, StringComparison.OrdinalIgnoreCase))
            {
                return;
            }

            if (saveCurrent && !string.IsNullOrWhiteSpace(_pointConfigPath) && File.Exists(_pointConfigPath))
            {
                File.WriteAllText(_pointConfigPath, PointJsonTextBox.Text);
            }

            if (!File.Exists(selectedPath))
            {
                throw new FileNotFoundException("设备点位配置文件不存在，请按模板创建。", selectedPath);
            }

            _pointConfigPath = selectedPath;
            PointJsonTextBox.Text = File.ReadAllText(_pointConfigPath);
            PointConfigGroupBox.Header = GetPointConfigDisplayName(_pointConfigPath);
        }

        private string GetPointConfigDisplayName(string path)
        {
            var configDirectory = Path.GetDirectoryName(_deviceConfigPath);
            var baseUri = new Uri(AppendDirectorySeparator(configDirectory));
            return Uri.UnescapeDataString(baseUri.MakeRelativeUri(new Uri(path)).ToString()).Replace('/', Path.DirectorySeparatorChar);
        }

        private static string AppendDirectorySeparator(string path)
        {
            return path.EndsWith(Path.DirectorySeparatorChar.ToString(), StringComparison.Ordinal)
                ? path
                : path + Path.DirectorySeparatorChar;
        }

        // 配置不存在时从项目模板复制到程序输出目录。
        private static void EnsureConfigFileExists(string targetPath, string fileName)
        {
            if (File.Exists(targetPath))
            {
                return;
            }

            Directory.CreateDirectory(Path.GetDirectoryName(targetPath));
            var sourcePath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "..", "..", "Config", fileName);
            sourcePath = Path.GetFullPath(sourcePath);
            if (File.Exists(sourcePath))
            {
                File.Copy(sourcePath, targetPath, false);
                return;
            }

            throw new FileNotFoundException("JSON config file was not found.", targetPath);
        }

        private sealed class JsonReadResultRow
        {
            public string Name { get; set; }
            public string Address { get; set; }
            public string Type { get; set; }
            public string Value { get; set; }
            public string Quality { get; set; }
            public string Error { get; set; }
        }
    }
}
