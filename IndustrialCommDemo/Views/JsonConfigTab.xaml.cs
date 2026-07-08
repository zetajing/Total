using System;
using System.Collections.ObjectModel;
using System.IO;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using IndustrialCommSdk;
using IndustrialCommSdk.Abstractions;

namespace IndustrialCommDemo.Views
{
    public partial class JsonConfigTab : UserControl
    {
        private readonly ObservableCollection<JsonReadResultRow> _rows = new ObservableCollection<JsonReadResultRow>();
        private DemoAppContext _ctx;
        private string _deviceConfigPath;
        private string _pointConfigPath;

        public JsonConfigTab()
        {
            InitializeComponent();
            JsonReadResultGrid.ItemsSource = _rows;
        }

        public void Initialize(DemoAppContext ctx)
        {
            _ctx = ctx ?? throw new ArgumentNullException(nameof(ctx));
            _deviceConfigPath = ResolveConfigPath("devices.json");
            LoadConfigFiles();
        }

        private void ReloadConfigButton_Click(object sender, RoutedEventArgs e)
        {
            LoadConfigFiles();
        }

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

        private void DeviceNameTextBox_LostKeyboardFocus(object sender, System.Windows.Input.KeyboardFocusChangedEventArgs e)
        {
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

        private IIndustrialClient CreateClientFromJson()
        {
            SaveDeviceConfig();
            return IndustrialClientFactory.FromConfig(_deviceConfigPath, DeviceNameTextBox.Text, _ctx.SdkLogger);
        }

        private void SaveConfigFiles()
        {
            SaveDeviceConfig();
            _pointConfigPath = ResolveSelectedPointConfigPath();
            Directory.CreateDirectory(Path.GetDirectoryName(_pointConfigPath));
            File.WriteAllText(_pointConfigPath, PointJsonTextBox.Text);
            PointConfigGroupBox.Header = GetPointConfigDisplayName(_pointConfigPath);
        }

        private void SaveDeviceConfig()
        {
            Directory.CreateDirectory(Path.GetDirectoryName(_deviceConfigPath));
            File.WriteAllText(_deviceConfigPath, DeviceJsonTextBox.Text);
        }

        private TagTable LoadPointTable()
        {
            LoadSelectedPointConfig();
            return TagTable.LoadForDevice(_deviceConfigPath, DeviceNameTextBox.Text);
        }

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
            DeviceNameTextBox.IsEnabled = enabled;
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
            var device = config.FindDevice(DeviceNameTextBox.Text);
            return device.ResolvePointsFile(Path.GetDirectoryName(_deviceConfigPath));
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
