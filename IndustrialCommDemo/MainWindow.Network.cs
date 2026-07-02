using System;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using IndustrialCommDemo.Services;

namespace IndustrialCommDemo
{
    public partial class MainWindow
    {
        private bool _networkAdaptersLoaded;

        private async void NetworkSettingsTab_Loaded(object sender, RoutedEventArgs e)
        {
            if (_networkAdaptersLoaded) return;
            _networkAdaptersLoaded = true;
            await RefreshNetworkAdaptersAsync();
        }

        private async void RefreshNetworkAdaptersButton_Click(object sender, RoutedEventArgs e)
        {
            await RefreshNetworkAdaptersAsync();
        }

        private void NetworkAdapterComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            ShowSelectedNetworkAdapter();
        }

        private async Task RefreshNetworkAdaptersAsync(string preferredId = null)
        {
            try
            {
                NetworkSettingsStatusTextBlock.Text = "正在读取网卡信息...";
                NetworkSettingsStatusTextBlock.Foreground = Brushes.DarkGoldenrod;
                var selected = NetworkAdapterComboBox.SelectedItem as NetworkAdapterInfo;
                var selectedId = preferredId ?? (selected == null ? null : selected.Id);
                var adapters = await Task.Run(() => NetworkAdapterService.GetAdapters());

                NetworkAdapterComboBox.ItemsSource = adapters;
                NetworkAdapterComboBox.SelectedItem = adapters.FirstOrDefault(item => item.Id == selectedId)
                    ?? adapters.FirstOrDefault(item => item.CanConfigure)
                    ?? adapters.FirstOrDefault();
                NetworkSettingsStatusTextBlock.Text = adapters.Count == 0 ? "未找到可用网卡。" : "已读取当前网卡配置。";
                NetworkSettingsStatusTextBlock.Foreground = adapters.Count == 0 ? Brushes.IndianRed : Brushes.ForestGreen;
            }
            catch (Exception ex)
            {
                NetworkSettingsStatusTextBlock.Text = "读取失败：" + ex.Message;
                NetworkSettingsStatusTextBlock.Foreground = Brushes.IndianRed;
            }
        }

        private void ShowSelectedNetworkAdapter()
        {
            var adapter = NetworkAdapterComboBox.SelectedItem as NetworkAdapterInfo;
            if (adapter == null) return;

            NetworkAdapterDescriptionTextBlock.Text = adapter.Description;
            NetworkAdapterMacTextBlock.Text = string.IsNullOrEmpty(adapter.MacAddress) ? "（无）" : adapter.MacAddress;
            NetworkAdapterModeTextBlock.Text = adapter.IsDhcpEnabled ? "DHCP（自动获取）" : "静态 IP";
            NetworkIpAddressTextBox.Text = adapter.IpAddress;
            NetworkSubnetMaskTextBox.Text = adapter.SubnetMask;
            NetworkGatewayTextBox.Text = adapter.Gateway;
            NetworkDnsTextBox.Text = adapter.DnsServers;
            ApplyStaticNetworkButton.IsEnabled = adapter.CanConfigure;
            EnableDhcpButton.IsEnabled = adapter.CanConfigure;
            if (!adapter.CanConfigure)
            {
                NetworkSettingsStatusTextBlock.Text = "该接口仅供查看，请选择物理以太网或 Wi-Fi 网卡进行修改。";
                NetworkSettingsStatusTextBlock.Foreground = Brushes.DarkGoldenrod;
            }
        }

        private async void ApplyStaticNetworkButton_Click(object sender, RoutedEventArgs e)
        {
            var adapter = NetworkAdapterComboBox.SelectedItem as NetworkAdapterInfo;
            if (adapter == null)
            {
                MessageBox.Show(this, "请先选择网卡。", "网卡设置", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            var confirmation = MessageBox.Show(this,
                "修改静态 IP 可能立即中断当前网络连接。\n\n确定要修改网卡“" + adapter.Name + "”吗？",
                "确认修改网络配置", MessageBoxButton.YesNo, MessageBoxImage.Warning);
            if (confirmation != MessageBoxResult.Yes) return;

            // WPF 控件只能由 UI 线程访问。先复制输入值，再把纯数据交给后台线程执行 WMI。
            var ipAddress = NetworkIpAddressTextBox.Text;
            var subnetMask = NetworkSubnetMaskTextBox.Text;
            var gateway = NetworkGatewayTextBox.Text;
            var dnsServers = NetworkDnsTextBox.Text;
            await RunNetworkChangeAsync(adapter, () => NetworkAdapterService.ApplyStatic(
                adapter.Name,
                ipAddress,
                subnetMask,
                gateway,
                dnsServers));
        }

        private async void EnableDhcpButton_Click(object sender, RoutedEventArgs e)
        {
            var adapter = NetworkAdapterComboBox.SelectedItem as NetworkAdapterInfo;
            if (adapter == null)
            {
                MessageBox.Show(this, "请先选择网卡。", "网卡设置", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            var confirmation = MessageBox.Show(this,
                "恢复 DHCP 可能立即中断当前网络连接。\n\n确定要修改网卡“" + adapter.Name + "”吗？",
                "确认恢复 DHCP", MessageBoxButton.YesNo, MessageBoxImage.Warning);
            if (confirmation != MessageBoxResult.Yes) return;

            await RunNetworkChangeAsync(adapter, () => NetworkAdapterService.EnableDhcp(adapter.Name));
        }

        private async Task RunNetworkChangeAsync(NetworkAdapterInfo adapter, Action change)
        {
            try
            {
                SetNetworkButtonsEnabled(false);
                NetworkSettingsStatusTextBlock.Text = "正在应用网络配置...";
                NetworkSettingsStatusTextBlock.Foreground = Brushes.DarkGoldenrod;
                await Task.Run(change);
                NetworkSettingsStatusTextBlock.Text = "配置已应用，正在刷新...";
                await Task.Delay(1200);
                await RefreshNetworkAdaptersAsync(adapter.Id);
            }
            catch (Exception ex)
            {
                NetworkSettingsStatusTextBlock.Text = "设置失败：" + ex.Message;
                NetworkSettingsStatusTextBlock.Foreground = Brushes.IndianRed;
                MessageBox.Show(this, ex.Message, "网卡设置失败", MessageBoxButton.OK, MessageBoxImage.Warning);
            }
            finally
            {
                SetNetworkButtonsEnabled(true);
            }
        }

        private void SetNetworkButtonsEnabled(bool enabled)
        {
            RefreshNetworkAdaptersButton.IsEnabled = enabled;
            var adapter = NetworkAdapterComboBox.SelectedItem as NetworkAdapterInfo;
            ApplyStaticNetworkButton.IsEnabled = enabled && adapter != null && adapter.CanConfigure;
            EnableDhcpButton.IsEnabled = enabled && adapter != null && adapter.CanConfigure;
            NetworkAdapterComboBox.IsEnabled = enabled;
        }
    }
}
