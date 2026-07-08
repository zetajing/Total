using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Navigation;
using System.Windows.Shapes;
using System.Threading;
using System.ComponentModel;
using IndustrialCommSdk;
using IndustrialCommSdk.Abstractions;
using IndustrialCommSdk.Protocols.Modbus;   // Modbus
using IndustrialCommSdk.Protocols.S7;       // Siemens S7
using IndustrialCommSdk.Mes;                // MES TCP 对接
using IndustrialCommSdk.Storage;            // SQL Server 历史存储
using IndustrialCommSdk.Transport;          // 原始 TCP Socket

namespace Refresh_Logs
{
    /// <summary>
    /// MainWindow.xaml 的交互逻辑
    /// </summary>
    public partial class MainWindow : Window
    {
        private readonly IIndustrialClient _client;
        private string _ySubscriptionId;

        public MainWindow()
        {
            InitializeComponent();

            _client = IndustrialClientFactory.CreateModbus(new ModbusTcpClientOptions
            {
                DeviceId = "modbus_tcp",
                Host = "127.0.0.1",
                Port = 502,
                SlaveId = 1,
                DeviceProfile = ModbusDeviceProfiles.InovanceEasyPlc
            });

            Loaded += async (s, e) =>
            {
                try
                {
                    await _client.ConnectAsync();
                    log.Text = "已连接";
                    StartYMonitor();
                }
                catch (Exception ex)
                {
                    log.Text = $"连接失败: {ex.Message}";
                }
            };
        }

        private async void ConnectButton_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                await _client.ConnectAsync();
                log.Text = "已连接";
                StartYMonitor();
            }
            catch (Exception ex)
            {
                log.Text = $"连接失败: {ex.Message}";
            }
        }





        private async void StartYMonitor()
        {
            var requests = new ReadRequest[]
            {
                new ReadRequest(_client.DeviceId, "Y0", DataType.Bool),
                new ReadRequest(_client.DeviceId, "Y1", DataType.Bool),
                new ReadRequest(_client.DeviceId, "Y2", DataType.Bool),
                new ReadRequest(_client.DeviceId, "Y3", DataType.Bool),
                new ReadRequest(_client.DeviceId, "Y4", DataType.Bool),
                new ReadRequest(_client.DeviceId, "Y5", DataType.Bool),
            };

            _ySubscriptionId = await _client.SubscribeAsync(
                new SubscriptionRequest(
                    subscriptionKey: "y-monitor",
                    deviceId: _client.DeviceId,
                    items: requests,
                    interval: TimeSpan.FromMilliseconds(500),
                    reportOnChangeOnly: false
                ),
                (sender, args) =>
                {
                    Dispatcher.Invoke(() =>
                    {
                        var sb = new StringBuilder();
                        foreach (var item in args.Values)
                        {
                            sb.AppendLine($"{item.Address} = {item.Value}");
                        }
                        log.Text = sb.ToString();
                    });
                },
                CancellationToken.None);
        }

        private async void StopYMonitor()
        {
            if (_ySubscriptionId != null)
            {
                await _client.UnsubscribeAsync(_ySubscriptionId, CancellationToken.None);
                _ySubscriptionId = null;
            }
        }

        protected override async void OnClosing(CancelEventArgs e)
        {
            StopYMonitor();
            if (_client.IsConnected)
                await _client.DisconnectAsync();
            _client.Dispose();
            base.OnClosing(e);
        }
       


    }
}
