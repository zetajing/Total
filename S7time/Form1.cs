using IndustrialCommSdk.Abstractions;
using IndustrialCommSdk.Protocols.S7;
using S7.Net;
using System;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using static System.Windows.Forms.VisualStyles.VisualStyleElement;
using SdkDataType = IndustrialCommSdk.Abstractions.DataType;

namespace S7time
{
    public partial class Form1 : Form
    {
        private SiemensS7Client _client;

        public Form1()
        {
            InitializeComponent();
        }

        private async void connect_Click(object sender, EventArgs e)
        {
            try
            {
                _client?.Dispose();

                _client = new SiemensS7Client(
                    new SiemensS7ClientOptions
                    {
                        DeviceId = "s7time",
                        Host = ip.Text.Trim(),
                        CpuType = CpuType.S71200,
                        Rack = 0,
                        Slot = 1,
                        ConnectTimeoutMilliseconds = 5000,
                        OperationTimeoutMilliseconds = 5000
                    });

                await _client.ConnectAsync(CancellationToken.None);

                logview.AppendText("连接成功\r\n");
            }
            catch (Exception ex)
            {
                _client?.Dispose();
                _client = null;
                logview.AppendText("连接失败：" + ex.Message + "\r\n");

            }
        }
        protected override void OnFormClosed(FormClosedEventArgs e)
        {
            _client?.Dispose();
            base.OnFormClosed(e);
        }

        private async void read_Click(object sender, EventArgs e)
        {
            if (_client == null || !_client.IsConnected)
            {
                MessageBox.Show("请先连接 PLC");
                return;
            }

            try
            {
                SdkDataType dataType;
                ushort length = 1;

                switch (type.Text.Trim().ToLowerInvariant())
                {
                    case "bool":
                        dataType = SdkDataType.Bool;
                        break;

                    case "string":
                        dataType = SdkDataType.S7String;
                        length = 60;
                        break;

                    case "int":
                        dataType = SdkDataType.Int16;
                        break;

                    default:
                        MessageBox.Show("不支持的数据类型：" + type.Text);
                        return;
                }

                var totalWatch = Stopwatch.StartNew();

                var readWatch = Stopwatch.StartNew();
                var readResult = await _client.ReadAsync(
                    new ReadRequest(
                        _client.DeviceId,
                        read_address.Text.Trim(),
                        dataType,
                        length),
                    CancellationToken.None);
                readWatch.Stop();

                if (readResult.Quality != QualityStatus.Good)
                {
                    MessageBox.Show("读取失败：" + readResult.ErrorMessage);
                    return;
                }

                var delayWatch = Stopwatch.StartNew();
                await Task.Delay(50);
                delayWatch.Stop();

                var writeWatch = Stopwatch.StartNew();
                await _client.WriteAsync(
                    new WriteRequest(
                        _client.DeviceId,
                        write_address.Text.Trim(),
                        SdkDataType.Bool,
                        true),
                    CancellationToken.None);
                writeWatch.Stop();

                totalWatch.Stop();

                logview.AppendText(
                    string.Format(
                        "[{0:HH:mm:ss.fff}] 读取值={1}，读取耗时={2:F3}ms，等待={3:F3}ms，写入耗时={4:F3}ms，通信耗时={5:F3}ms，总耗时={6:F3}ms{7}",
                        DateTime.Now,
                        readResult.Value,
                        readWatch.Elapsed.TotalMilliseconds,
                        delayWatch.Elapsed.TotalMilliseconds,
                        writeWatch.Elapsed.TotalMilliseconds,
                        readWatch.Elapsed.TotalMilliseconds + writeWatch.Elapsed.TotalMilliseconds,
                        totalWatch.Elapsed.TotalMilliseconds,
                        Environment.NewLine));
            }
            catch (Exception ex)
            {
                logview.AppendText("错误：" + ex.Message + Environment.NewLine);
            }
        }
    }
}
