using System.Diagnostics;
using InduLink.Protocols.S7;
using InduLink.Runtime;
using S7.Net;

namespace S7TriggerRecorder;

public partial class MainForm : Form
{
    private CancellationTokenSource stopSource;
    private Task runningTask;
    private bool closing;
    private readonly string recordFolder = Path.Combine(AppContext.BaseDirectory, "记录");

    public MainForm()
    {
        InitializeComponent();
        cpuBox.Items.AddRange(new object[] { CpuType.S71200, CpuType.S71500, CpuType.S7300, CpuType.S7400 });
        cpuBox.SelectedIndex = 0;
        pathLabel.Text = "记录目录：" + recordFolder;
    }

    private async void StartButton_Click(object sender, EventArgs e)
    {
        if (runningTask != null) return;
        var options = new SiemensS7ClientOptions
        {
            DeviceId = "trigger-recorder", Host = ipBox.Text.Trim(),
            CpuType = (CpuType)cpuBox.SelectedItem,
            Rack = (short)rackBox.Value, Slot = (short)slotBox.Value,
            ConnectTimeoutMilliseconds = 2000, OperationTimeoutMilliseconds = 2000,
            AutoReconnect = false
        };
        stopSource = new CancellationTokenSource();
        SetRunning(true);
        runningTask = RunAsync(options, stopSource.Token);
        try { await runningTask; }
        catch (OperationCanceledException) { statusLabel.Text = "已停止"; }
        catch (Exception ex) { statusLabel.Text = "已停止：" + ex.Message; }
        finally
        {
            runningTask = null;
            stopSource.Dispose();
            stopSource = null;
            SetRunning(false);
        }
    }

    private async Task RunAsync(SiemensS7ClientOptions options, CancellationToken token)
    {
        using var client = new SiemensS7Client(options);
        using var timer = new PeriodicTimer(TimeSpan.FromMilliseconds(100));
        var trigger = new TriggerState();
        int count = 0;
        while (true)
        {
            token.ThrowIfCancellationRequested();
            string value = null;
            DateTime time = default;
            try
            {
                if (!client.IsConnected) await client.ConnectAsync(token);
                bool active = await client.ReadBoolAsync("DB2001.DBX1404.6", token);
                statusLabel.Text = $"运行中 | 触发位：{active} | 本次已记录：{count} 条";
                if (trigger.ShouldRecord(active))
                {
                    time = DateTime.Now;
                    // STRING[50] 从字节 314 开始，库自动处理 2 字节头和实际长度。
                    value = await client.ReadDbStringAsync("DB2001.DBX314.0", 50, token);
                }
            }
            catch (OperationCanceledException) when (token.IsCancellationRequested) { throw; }
            catch (Exception ex)
            {
                // 通信失败不当成 FALSE，不解除本次触发；1 秒后重新连接。
                statusLabel.Text = "通信异常，1 秒后重连：" + ex.GetBaseException().Message;
                await client.DisconnectAsync(CancellationToken.None);
                await Task.Delay(1000, token);
                continue;
            }

            if (value != null)
            {
                // 落盘失败直接停止，避免界面显示“记录成功”却没有文件。
                RecordStore.Append(recordFolder, time, value);
                trigger.MarkRecorded();
                count++;
                recordsGrid.Rows.Insert(0, count, time.ToString("yyyy-MM-dd HH:mm:ss.fff"), value);
                if (recordsGrid.Rows.Count > 1000) recordsGrid.Rows.RemoveAt(1000);
                statusLabel.Text = $"运行中 | 触发位：True | 本次已记录：{count} 条";
            }
            // 等待下一节拍，异步读完成后才继续，避免多个请求重叠。
            await timer.WaitForNextTickAsync(token);
        }
    }

    private void StopButton_Click(object sender, EventArgs e)
    {
        stopButton.Enabled = false;
        stopSource?.Cancel();
    }

    private void FolderButton_Click(object sender, EventArgs e)
    {
        try
        {
            Directory.CreateDirectory(recordFolder);
            Process.Start(new ProcessStartInfo(recordFolder) { UseShellExecute = true });
        }
        catch (Exception ex) { MessageBox.Show(this, ex.Message, "无法打开目录"); }
    }

    private void SetRunning(bool running)
    {
        startButton.Enabled = !running;
        stopButton.Enabled = running;
        ipBox.Enabled = cpuBox.Enabled = rackBox.Enabled = slotBox.Enabled = !running;
    }

    private async void MainForm_FormClosing(object sender, FormClosingEventArgs e)
    {
        if (runningTask == null) return;
        e.Cancel = true;
        if (closing) return;
        closing = true;
        stopSource.Cancel();
        try { await runningTask; }
        catch (Exception) { /* 关闭时等待读取结束，随后由 using 释放 PLC 连接。 */ }
        Close();
    }
}
