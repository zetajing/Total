using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Windows.Controls;
using System.Windows.Media;
using IndustrialCommSdk;
using IndustrialCommSdk.Abstractions;
using IndustrialCommSdk.Configuration;

namespace IndustrialCommDemo.Views
{
    public partial class JsonConfigTab
    {
        private void LoadConfigFiles()
        {
            try
            {
                EnsureConfigFileExists(_deviceConfigPath, "devices.json");
                var config = Sdk.LoadConfiguration(_deviceConfigPath);
                DeviceJsonTextBox.Text = Sdk.SerializeConfiguration(config);
                RefreshDeviceList(config);
                LoadSelectedPointConfig(true, false);
                _rows.Clear();
                SetStatus("已读取配置：" + _deviceConfigPath, Brushes.ForestGreen);
            }
            catch (Exception ex) { SetStatus("读取配置失败：" + ex.Message, Brushes.IndianRed); _ctx.HandleError("读取 JSON 配置失败。", ex, true); }
        }

        private string ResolveSelectedPointConfigPath()
        {
            var config = Sdk.ParseConfiguration(DeviceJsonTextBox.Text);
            return config.FindDevice(GetSelectedDeviceName()).ResolvePointsFile(Path.GetDirectoryName(_deviceConfigPath));
        }

        private void RefreshDeviceList(IndustrialSdkConfig config)
        {
            if (config == null) throw new ArgumentNullException(nameof(config));
            var currentName = DeviceNameComboBox.SelectedItem as string;
            var names = config.Devices.Where(device => device != null && !string.IsNullOrWhiteSpace(device.Name))
                .Select(device => device.Name).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
            if (names.Count == 0) throw new InvalidOperationException("devices 至少需要一台带 name 的设备。");
            _isRefreshingDeviceList = true;
            try
            {
                DeviceNameComboBox.ItemsSource = names;
                DeviceNameComboBox.SelectedItem = names.FirstOrDefault(name => string.Equals(name, currentName, StringComparison.OrdinalIgnoreCase)) ?? names[0];
            }
            finally { _isRefreshingDeviceList = false; }
            LoadDeviceForm();
        }

        private void LoadDeviceForm()
        {
            var selectedName = DeviceNameComboBox.SelectedItem as string;
            if (string.IsNullOrWhiteSpace(selectedName) || string.IsNullOrWhiteSpace(DeviceJsonTextBox.Text)) return;
            var device = Sdk.ParseConfiguration(DeviceJsonTextBox.Text).FindDevice(selectedName);
            _isLoadingDeviceForm = true;
            try
            {
                DeviceNameTextBox.Text = device.Name ?? string.Empty;
                SelectProtocol(device.Protocol);
                DeviceIdTextBox.Text = device.DeviceId ?? string.Empty;
                PointsFileTextBox.Text = device.PointsFile ?? string.Empty;
                PollingIntervalTextBox.Text = device.Runtime.PollingIntervalMilliseconds.ToString();
                ReconnectDelayTextBox.Text = device.Runtime.ReconnectDelayMilliseconds.ToString();
                OperationTimeoutTextBox.Text = device.Runtime.OperationTimeoutMilliseconds.ToString();
                EnabledCheckBox.IsChecked = device.Enabled;
                SettingsJsonTextBox.Text = Sdk.Configuration.SerializeSettings(device.Settings);
            }
            finally { _isLoadingDeviceForm = false; }
        }

        private void SelectProtocol(string protocol)
        {
            foreach (var item in ProtocolComboBox.Items.OfType<ComboBoxItem>())
                if (string.Equals(item.Content as string, protocol, StringComparison.Ordinal)) { ProtocolComboBox.SelectedItem = item; return; }
            throw new InvalidOperationException("配置包含未注册协议：" + protocol);
        }

        private string GetSelectedProtocol()
        {
            var item = ProtocolComboBox.SelectedItem as ComboBoxItem;
            return RequireText(item == null ? null : item.Content as string, "请选择协议。");
        }

        private static string RequireText(string value, string errorMessage)
        {
            if (string.IsNullOrWhiteSpace(value)) throw new InvalidOperationException(errorMessage);
            return value.Trim();
        }

        private static string EmptyToNull(string value) { return string.IsNullOrWhiteSpace(value) ? null : value.Trim(); }

        private static int ParsePositiveInt(string text, string fieldName)
        {
            int value;
            if (!int.TryParse(text, out value) || value <= 0) throw new InvalidOperationException(fieldName + "必须是大于 0 的整数。");
            return value;
        }

        private string GetSelectedDeviceName()
        {
            var name = DeviceNameComboBox.SelectedItem as string;
            if (string.IsNullOrWhiteSpace(name)) throw new InvalidOperationException("请先选择一台设备。");
            return name;
        }

        private void LoadSelectedPointConfig(bool forceReload = false, bool saveCurrent = true)
        {
            var selectedPath = ResolveSelectedPointConfigPath();
            if (!forceReload && string.Equals(selectedPath, _pointConfigPath, StringComparison.OrdinalIgnoreCase)) return;
            if (saveCurrent && !string.IsNullOrWhiteSpace(_pointConfigPath) && File.Exists(_pointConfigPath))
            {
                if (PointEditorTabControl.SelectedIndex == 0) ApplyPointRowsToJson();
                File.WriteAllText(_pointConfigPath, PointJsonTextBox.Text);
            }
            if (!File.Exists(selectedPath))
            {
                _pointConfigPath = selectedPath;
                _pointRows.Clear();
                _pointRows.Add(new PointEditorRow());
                PointJsonTextBox.Text = "{\r\n  \"tags\": []\r\n}";
                PointConfigGroupBox.Header = GetPointConfigDisplayName(_pointConfigPath) + "（待创建）";
                return;
            }
            _pointConfigPath = selectedPath;
            PointJsonTextBox.Text = File.ReadAllText(_pointConfigPath);
            LoadPointRows();
            PointConfigGroupBox.Header = GetPointConfigDisplayName(_pointConfigPath);
        }

        private void LoadPointRows()
        {
            var table = TagTable.FromJson(PointJsonTextBox.Text);
            _pointRows.Clear();
            foreach (var tag in table.Tags)
                _pointRows.Add(new PointEditorRow { Name = tag.Name, Address = tag.Address, Type = tag.DataType.ToString(), Length = tag.Length });
        }

        private void ApplyPointRowsToJson()
        {
            PointEditorGrid.CommitEdit(DataGridEditingUnit.Cell, true);
            PointEditorGrid.CommitEdit(DataGridEditingUnit.Row, true);
            var tags = new List<IndustrialTag>();
            var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var row in _pointRows.Where(item => item != null && (!string.IsNullOrWhiteSpace(item.Name) || !string.IsNullOrWhiteSpace(item.Address))))
            {
                var name = RequireText(row.Name, "点位名称不能为空。");
                if (!names.Add(name)) throw new InvalidOperationException("点位名称不能重复：" + name);
                DataType dataType;
                if (!Enum.TryParse(row.Type, true, out dataType)) throw new InvalidOperationException("不支持的点位类型：" + row.Type);
                if (row.Length == 0) throw new InvalidOperationException("点位长度必须大于 0：" + name);
                tags.Add(new IndustrialTag(RequireText(row.Address, "点位地址不能为空。"), dataType, row.Length, name));
            }
            if (tags.Count == 0) throw new InvalidOperationException("点位表至少需要一个点位。");
            PointJsonTextBox.Text = new TagTable(tags).ToJson();
        }

        private string GetPointConfigDisplayName(string path)
        {
            var baseUri = new Uri(AppendDirectorySeparator(Path.GetDirectoryName(_deviceConfigPath)));
            return Uri.UnescapeDataString(baseUri.MakeRelativeUri(new Uri(path)).ToString()).Replace('/', Path.DirectorySeparatorChar);
        }

        private static string AppendDirectorySeparator(string path)
        {
            return path.EndsWith(Path.DirectorySeparatorChar.ToString(), StringComparison.Ordinal) ? path : path + Path.DirectorySeparatorChar;
        }

        private static void EnsureConfigFileExists(string targetPath, string fileName)
        {
            if (File.Exists(targetPath)) return;
            Directory.CreateDirectory(Path.GetDirectoryName(targetPath));
            var sourcePath = Path.GetFullPath(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "..", "..", "Config", fileName));
            if (File.Exists(sourcePath)) { File.Copy(sourcePath, targetPath, false); return; }
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

        private sealed class PointEditorRow
        {
            public string Name { get; set; }
            public string Address { get; set; }
            public string Type { get; set; } = DataType.Int16.ToString();
            public ushort Length { get; set; } = 1;
        }
    }
}
