using System;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using IndustrialCommDemo.Services;

namespace IndustrialCommDemo.Views
{
    public partial class DeviceRuntimeTab : UserControl
    {
        private readonly ObservableCollection<RuntimeDeviceInfo> _devices = new ObservableCollection<RuntimeDeviceInfo>();
        private readonly ObservableCollection<RuntimeValueInfo> _values = new ObservableCollection<RuntimeValueInfo>();
        private DemoAppContext _ctx;

        public DeviceRuntimeTab()
        {
            InitializeComponent();
            DeviceGrid.ItemsSource = _devices;
            ValueGrid.ItemsSource = _values;
        }

        public async void Initialize(DemoAppContext ctx)
        {
            _ctx = ctx ?? throw new ArgumentNullException(nameof(ctx));
            _ctx.Runtime.DevicesChanged += Runtime_DevicesChanged;
            _ctx.Runtime.DeviceStateChanged += Runtime_DeviceStateChanged;
            _ctx.Runtime.ValuesReceived += Runtime_ValuesReceived;
            await RunAsync("加载设备配置", () => _ctx.Runtime.ReloadAsync());
        }

        private async void ReloadButton_Click(object sender, RoutedEventArgs e)
        {
            _values.Clear();
            await RunAsync("重新加载设备配置", () => _ctx.Runtime.ReloadAsync());
        }

        private async void StartButton_Click(object sender, RoutedEventArgs e)
        {
            await RunAsync("启动设备运行中心", () => _ctx.Runtime.StartAsync());
        }

        private async void StopButton_Click(object sender, RoutedEventArgs e)
        {
            await RunAsync("停止设备运行中心", () => _ctx.Runtime.StopAsync());
        }

        private void Runtime_DevicesChanged(object sender, RuntimeDevicesChangedEventArgs e)
        {
            _ctx.RunOnUi(() =>
            {
                _devices.Clear();
                foreach (var device in e.Devices) _devices.Add(device);
            });
        }

        private void Runtime_DeviceStateChanged(object sender, RuntimeDeviceStateEventArgs e)
        {
            _ctx.RunOnUi(() =>
            {
                var current = _devices.FirstOrDefault(device => string.Equals(device.Name, e.DeviceName, StringComparison.OrdinalIgnoreCase));
                if (current == null) return;
                var index = _devices.IndexOf(current);
                _devices[index] = new RuntimeDeviceInfo
                {
                    Name = current.Name,
                    Protocol = current.Protocol,
                    Endpoint = current.Endpoint,
                    State = e.State,
                    Error = e.Error,
                };
            });
        }

        private void Runtime_ValuesReceived(object sender, RuntimeValuesEventArgs e)
        {
            _ctx.RunOnUi(() =>
            {
                foreach (var value in e.Values)
                {
                    var old = _values.FirstOrDefault(item =>
                        string.Equals(item.DeviceName, value.DeviceName, StringComparison.OrdinalIgnoreCase) &&
                        string.Equals(item.TagName, value.TagName, StringComparison.OrdinalIgnoreCase));
                    if (old == null) _values.Add(value);
                    else _values[_values.IndexOf(old)] = value;
                }
            });
        }

        private async Task RunAsync(string actionName, Func<Task> action)
        {
            SetBusy(true);
            try
            {
                RuntimeStatusTextBlock.Text = actionName + "...";
                RuntimeStatusTextBlock.Foreground = Brushes.DarkGoldenrod;
                await action();
                RuntimeStatusTextBlock.Text = actionName + "完成，共 " + _devices.Count + " 台启用设备。";
                RuntimeStatusTextBlock.Foreground = Brushes.ForestGreen;
                _ctx.SetHeaderStatus(actionName + "完成", Brushes.LightGreen);
                _ctx.DemoLogger.Info(actionName + "完成。devices=" + _devices.Count);
            }
            catch (Exception ex)
            {
                RuntimeStatusTextBlock.Text = actionName + "失败：" + ex.Message;
                RuntimeStatusTextBlock.Foreground = Brushes.IndianRed;
                _ctx.HandleError(actionName + "失败。", ex, true);
            }
            finally
            {
                SetBusy(false);
            }
        }

        private void SetBusy(bool busy)
        {
            ReloadButton.IsEnabled = !busy && !_ctx.Runtime.IsRunning;
            StartButton.IsEnabled = !busy && !_ctx.Runtime.IsRunning;
            StopButton.IsEnabled = !busy && _ctx.Runtime.IsRunning;
        }
    }
}
