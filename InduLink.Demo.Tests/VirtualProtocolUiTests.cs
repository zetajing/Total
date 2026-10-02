using System;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Runtime.ExceptionServices;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using InduLink.Protocols.Modbus;
using InduLink.Protocols.OpcUa;
using InduLink.Runtime;
using InduLinkDemo.Views;
using NUnit.Framework;

namespace InduLink.Tests
{
    [TestFixture, NonParallelizable]
    public sealed class VirtualProtocolUiTests
    {
        [Test, Apartment(ApartmentState.STA)]
        public void VirtualPagesStartEditValuesStopAndCloseWithRealClients()
        {
            var application = Application.Current ?? new Application { ShutdownMode = ShutdownMode.OnExplicitShutdown };
            application.Resources.MergedDictionaries.Add(new ResourceDictionary
            {
                Source = new Uri("pack://application:,,,/InduLinkDemo;component/Styles/ModernTheme.xaml"),
            });
            RunDispatcher(async () =>
            {
                foreach (var opcUa in new[] { false, true })
                {
                    var control = new ProtocolSimulatorTab();
                    control.ConfigureProtocol(opcUa);
                    var port = FreePort();
                    var pki = Path.Combine(Path.GetTempPath(), "indulink-ui-pki-" + Guid.NewGuid().ToString("N"));
                    ((TextBox)control.FindName("PortTextBox")).Text = port.ToString();
                    ((TextBox)control.FindName("EndpointTextBox")).Text = "opc.tcp://localhost:" + port + "/UiTest";
                    ((TextBox)control.FindName("CertificateDirectoryTextBox")).Text = pki;
                    var configuration = control.ExportConfiguration();
                    Assert.That(configuration, Does.Not.Contain("CurrentValue"));
                    control.ImportConfiguration(configuration);
                    Assert.Throws<FormatException>(() => control.ImportConfiguration(configuration.Replace(opcUa ? "opcua" : "modbus-tcp", opcUa ? "modbus-tcp" : "opcua")));
                    try
                    {
                        await control.StartAsync();
                        Assert.That(((Button)control.FindName("StartButton")).IsEnabled, Is.False);
                        Assert.That(((Button)control.FindName("StopButton")).IsEnabled, Is.True);
                        Assert.That(((DataGrid)control.FindName("PointsGrid")).IsReadOnly, Is.True);
                        WriteValueTextBox(control).Text = "false";
                        ((Button)control.FindName("ApplyValueButton")).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                        if (opcUa)
                        {
                            using var client = new OpcUaClient(new OpcUaClientOptions { DeviceId = "ui", EndpointUrl = "opc.tcp://localhost:" + port + "/UiTest", UseSecurity = false });
                            await client.ConnectAsync();
                            Assert.That(await client.ReadBoolAsync("ns=2;s=Demo/Running"), Is.False);
                            await client.DisconnectAsync();
                        }
                        else
                        {
                            using var client = ModbusTcpClient.Create("127.0.0.1", port: port, deviceProfile: ModbusDeviceProfiles.Generic);
                            await client.ConnectAsync();
                            Assert.That(await client.ReadBoolAsync("C0"), Is.False);
                            await client.DisconnectAsync();
                        }
                        await RenderPreviewAsync(control, opcUa ? "opcua-simulator" : "modbus-simulator");
                        await control.StopAsync();
                        Assert.That(((Button)control.FindName("StartButton")).IsEnabled, Is.True);
                        Assert.That(((DataGrid)control.FindName("PointsGrid")).IsReadOnly, Is.False);
                        await control.StartAsync();
                    }
                    finally
                    {
                        await control.ResetAsync();
                        if (Directory.Exists(pki)) Directory.Delete(pki, true);
                    }
                    Assert.That(((Button)control.FindName("StartButton")).IsEnabled, Is.False);
                    using var listener = new TcpListener(IPAddress.Loopback, port);
                    Assert.DoesNotThrow(() => listener.Start());
                }
                // Instantiate the unified navigation, including the retained S7 page.
                var navigation = new VirtualPlcTab();
                await navigation.ResetAsync();
            });
        }

        private static TextBox WriteValueTextBox(ProtocolSimulatorTab control) => (TextBox)control.FindName("WriteValueTextBox");

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

        private static int FreePort()
        {
            using var listener = new TcpListener(IPAddress.Loopback, 0);
            listener.Start();
            return ((IPEndPoint)listener.LocalEndpoint).Port;
        }

        private static async Task RenderPreviewAsync(FrameworkElement control, string name)
        {
            // Optional local visual artifact, enabled only when explicitly configured by the test runner.
            var directory = Environment.GetEnvironmentVariable("INDULINK_TEST_UI_PREVIEW");
            if (string.IsNullOrEmpty(directory)) return;
            Directory.CreateDirectory(directory);
            control.Measure(new Size(1060, 600));
            control.Arrange(new Rect(0, 0, 1060, 600));
            control.UpdateLayout();
            await control.Dispatcher.InvokeAsync(() => { }, DispatcherPriority.ContextIdle);
            control.UpdateLayout();
            var bitmap = new RenderTargetBitmap(1060, 600, 96, 96, PixelFormats.Pbgra32);
            bitmap.Render(control);
            var encoder = new PngBitmapEncoder();
            encoder.Frames.Add(BitmapFrame.Create(bitmap));
            using var file = File.Create(Path.Combine(directory, name + ".png"));
            encoder.Save(file);
        }
    }
}
