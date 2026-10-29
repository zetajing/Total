using System;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Reflection;
using System.Runtime.ExceptionServices;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using InduLink.Abstractions;
using InduLink.Protocols.Modbus;
using InduLink.Protocols.OpcUa;
using InduLink.Runtime;
using InduLinkDemo.Views;
using NUnit.Framework;

namespace InduLink.Tests
{
    [TestFixture, NonParallelizable]
    public sealed class SimulatorClientHandoffTests
    {
        [TestCase(1, false)]
        [TestCase(3, true)]
        [TestCase(4, false)]
        [Apartment(ApartmentState.STA)]
        public void ModbusButtonTransfersActualPortProfileAndSelectedPoint(int pointIndex, bool writable)
        {
            EnsureTheme();
            RunDispatcher(async () =>
            {
                var navigation = new VirtualPlcTab();
                var source = (ProtocolSimulatorTab)navigation.FindName("ModbusServerControl");
                var target = new ModbusTab();
                // 加载到真实窗口，覆盖页面已 Loaded 时触发的类型和长度联动事件。
                var window = new Window
                {
                    Content = target, Width = 1100, Height = 650, Left = -10000, Top = -10000,
                    ShowInTaskbar = false, ShowActivated = false,
                };
                var open = (Button)source.FindName("OpenClientButton");
                Assert.That(open.IsEnabled, Is.False);
                ((TextBox)source.FindName("PortTextBox")).Text = "0";
                ((TextBox)source.FindName("SlaveIdTextBox")).Text = "7";
                ((DataGrid)source.FindName("PointsGrid")).SelectedIndex = pointIndex;
                SimulatorClientRequest transferred = null;
                navigation.OpenClientRequested += (_, request) =>
                {
                    target.ApplySimulatorRequest(request);
                    transferred = request;
                };
                try
                {
                    window.Show();
                    await target.Dispatcher.InvokeAsync(() => { }, DispatcherPriority.ContextIdle);
                    Assert.That(target.IsLoaded, Is.True);
                    await source.StartAsync();
                    Assert.That(open.IsEnabled, Is.True);
                    open.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                    Assert.That(transferred, Is.Not.Null);
                    Assert.That(transferred.Port, Is.GreaterThan(0));
                    Assert.That(Text(target, "PortTextBox"), Is.EqualTo(transferred.Port.ToString()));
                    Assert.That(Text(target, "SlaveIdTextBox"), Is.EqualTo("7"));
                    Assert.That(Text(target, "AddressTextBox"), Is.EqualTo(transferred.Address));
                    Assert.That(Text(target, "LengthTextBox"), Is.EqualTo(transferred.Length.ToString()));
                    Assert.That(((ComboBoxItem)((ComboBox)target.FindName("DataTypeComboBox")).SelectedItem).Content,
                        Is.EqualTo(transferred.DataType.ToString()));
                    Assert.That(((ComboBoxItem)((ComboBox)target.FindName("ModelComboBox")).SelectedItem).Tag,
                        Is.EqualTo(ModbusDeviceProfiles.Generic.Key));
                    Assert.That(((Button)target.FindName("WriteButton")).IsEnabled, Is.EqualTo(writable));
                    Assert.That(ClientField(target).GetValue(target), Is.Null, "预填参数不应自动建立连接。");

                    // 使用传递的端口、站号和类型完成真实读取，验证无需再手工补参数。
                    using var client = ModbusTcpClient.Create(transferred.Host, port: transferred.Port,
                        slaveId: transferred.SlaveId, deviceProfile: ModbusDeviceProfiles.Generic);
                    await client.ConnectAsync();
                    var result = await client.ReadAsync(new ReadRequest(client.DeviceId, transferred.Address,
                        transferred.DataType, transferred.Length), CancellationToken.None);
                    Assert.That(result.Quality, Is.EqualTo(QualityStatus.Good));
                    await client.DisconnectAsync();
                    await source.StopAsync();
                    Assert.That(open.IsEnabled, Is.False);
                }
                finally
                {
                    try { await navigation.ResetAsync(); }
                    finally { window.Close(); }
                }
            });
        }

        [TestCase(false)]
        [TestCase(true)]
        [Apartment(ApartmentState.STA)]
        public void OpcUaButtonTransfersResolvedNodeAndSecurityWithoutCredentials(bool secure)
        {
            EnsureTheme();
            RunDispatcher(async () =>
            {
                var source = new ProtocolSimulatorTab();
                source.ConfigureProtocol(true);
                var pki = Path.Combine(Path.GetTempPath(), "indulink-handoff-" + Guid.NewGuid().ToString("N"));
                ((TextBox)source.FindName("EndpointTextBox")).Text = "opc.tcp://localhost:" + FreePort() + "/Handoff";
                ((TextBox)source.FindName("CertificateDirectoryTextBox")).Text = pki;
                ((CheckBox)source.FindName("SecurityCheckBox")).IsChecked = secure;
                var grid = (DataGrid)source.FindName("PointsGrid");
                var row = (SimulatorPointRow)grid.Items[2];
                row.Address = "Custom/Temperature";
                grid.SelectedItem = row;
                var target = new OpcUaTab();
                ((TextBox)target.FindName("UsernameTextBox")).Text = "existing-user";
                ((PasswordBox)target.FindName("PasswordBox")).Password = "existing-password";
                ((CheckBox)target.FindName("AutoAcceptCertificatesCheckBox")).IsChecked = true;
                SimulatorClientRequest transferred = null;
                source.OpenClientRequested += (_, request) =>
                {
                    target.ApplySimulatorRequest(request);
                    transferred = request;
                };
                try
                {
                    await source.StartAsync();
                    ((Button)source.FindName("OpenClientButton")).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                    Assert.That(transferred, Is.Not.Null);
                    Assert.That(Text(target, "NodeIdTextBox"), Is.EqualTo("ns=2;s=Custom/Temperature"));
                    Assert.That(Text(target, "EndpointUrlTextBox"), Is.EqualTo(transferred.Endpoint));
                    Assert.That(((CheckBox)target.FindName("UseSecurityCheckBox")).IsChecked, Is.EqualTo(secure));
                    Assert.That(((CheckBox)target.FindName("AutoAcceptCertificatesCheckBox")).IsChecked, Is.False);
                    Assert.That(Text(target, "UsernameTextBox"), Is.Empty);
                    Assert.That(((PasswordBox)target.FindName("PasswordBox")).Password, Is.Empty);
                    Assert.That(ClientField(target).GetValue(target), Is.Null);
                    if (!secure)
                    {
                        using var client = new OpcUaClient(new OpcUaClientOptions { DeviceId = "handoff", EndpointUrl = transferred.Endpoint });
                        await client.ConnectAsync();
                        var result = await client.ReadAsync(new ReadRequest(client.DeviceId, transferred.Address,
                            transferred.DataType, transferred.Length), CancellationToken.None);
                        Assert.That(result.Quality, Is.EqualTo(QualityStatus.Good));
                        Assert.That(result.Value, Is.EqualTo(23.5f));
                        await client.DisconnectAsync();
                    }
                }
                finally
                {
                    await source.ResetAsync();
                    if (Directory.Exists(pki)) Directory.Delete(pki, true);
                }
            });
        }

        [TestCase(false)]
        [TestCase(true)]
        [Apartment(ApartmentState.STA)]
        public void BusyOrExistingClientIsNeverReplaced(bool opcUa)
        {
            EnsureTheme();
            UserControl target = opcUa ? new OpcUaTab() : new ModbusTab();
            var request = new SimulatorClientRequest
            {
                Protocol = opcUa ? ProtocolKind.OpcUa : ProtocolKind.ModbusTcp,
                Host = "127.0.0.1", Port = 1502, SlaveId = 1, Endpoint = "opc.tcp://localhost:4840/Simulator",
                Address = opcUa ? "ns=2;s=Test" : "HR0", DataType = DataType.Int16, Length = 1, ValueText = "1",
            };
            var addressField = opcUa ? "NodeIdTextBox" : "AddressTextBox";
            var original = Text(target, addressField);
            var operations = target.GetType().GetField("_connectionOperations", BindingFlags.Instance | BindingFlags.NonPublic);
            operations.SetValue(target, 1);
            Assert.Throws<InvalidOperationException>(() => Apply(target, request));
            Assert.That(Text(target, addressField), Is.EqualTo(original));
            operations.SetValue(target, 0);
            using var client = opcUa
                ? (IInduLinkClient)new OpcUaClient(new OpcUaClientOptions { DeviceId = "existing", EndpointUrl = "opc.tcp://localhost:4840" })
                : ModbusTcpClient.Create("127.0.0.1");
            ClientField(target).SetValue(target, client);
            Assert.Throws<InvalidOperationException>(() => Apply(target, request));
            Assert.That(ClientField(target).GetValue(target), Is.SameAs(client));
            Assert.That(Text(target, addressField), Is.EqualTo(original));
        }

        [TestCase("0.0.0.0", "127.0.0.1")]
        [TestCase("::", "::1")]
        [TestCase("[::]", "::1")]
        [TestCase("192.168.1.8", "192.168.1.8")]
        public void WildcardListenerAddressesBecomeLocalConnectionTargets(string listener, string target)
        {
            // 只验证地址转换，不实际开放全接口监听。
            Assert.That(ProtocolSimulatorTab.ClientHost(listener), Is.EqualTo(target));
        }

        private static void Apply(UserControl target, SimulatorClientRequest request)
        {
            if (target is OpcUaTab opcUa) opcUa.ApplySimulatorRequest(request);
            else ((ModbusTab)target).ApplySimulatorRequest(request);
        }
        private static string Text(UserControl control, string name) => ((TextBox)control.FindName(name)).Text;
        private static FieldInfo ClientField(UserControl control) => control.GetType().GetField("_client", BindingFlags.Instance | BindingFlags.NonPublic);
        private static void EnsureTheme()
        {
            var application = Application.Current ?? new Application { ShutdownMode = ShutdownMode.OnExplicitShutdown };
            application.Resources.MergedDictionaries.Add(new ResourceDictionary { Source = new Uri("pack://application:,,,/InduLinkDemo;component/Styles/ModernTheme.xaml") });
        }
        private static int FreePort()
        {
            using var listener = new TcpListener(IPAddress.Loopback, 0);
            listener.Start();
            return ((IPEndPoint)listener.LocalEndpoint).Port;
        }
        private static void RunDispatcher(Func<Task> action)
        {
            Exception failure = null;
            var frame = new DispatcherFrame();
            Dispatcher.CurrentDispatcher.BeginInvoke(new Action(async () =>
            {
                try { await action(); }
                catch (Exception ex) { failure = ex; }
                finally { frame.Continue = false; }
            }));
            Dispatcher.PushFrame(frame);
            if (failure != null) ExceptionDispatchInfo.Capture(failure).Throw();
        }
    }
}
