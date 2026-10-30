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
        var cache = new TriggerValueCache();
        int count = 0;
        while (true)
        {
            token.ThrowIfCancellationRequested();
            RecordedValue record = null;
            DateTime time = default;
            try
            {
                if (!client.IsConnected) await client.ConnectAsync(token);
                bool active = await client.ReadBoolAsync("DB2001.DBX1404.6", token);
                // 每轮读取 STRING，保留 FALSE 阶段最近的非空值。
                string current = await client.ReadDbStringAsync("DB2001.DBX314.0", 50, token);
                DateTime sampleTime = DateTime.Now;
                cache.Observe(active, current, sampleTime);
                valueLabel.Text = "实时字符串：" + (current.Length == 0 ? "（PLC 当前长度为 0）" : current);
                statusLabel.Text = $"运行中 | 触发位：{active} | 本次已记录：{count} 条";
                if (trigger.ShouldRecord(active))
                {
                    time = DateTime.Now;
                    record = cache.Select(current, sampleTime);
                }
            }
            catch (OperationCanceledException) when (token.IsCancellationRequested) { throw; }
            catch (Exception ex)
            {
                // 通信失败不当成 FALSE，不解除本次触发；1 秒后重新连接。
                statusLabel.Text = "通信异常，1 秒后重连：" + ex.GetBaseException().Message;
                cache.Clear();
                valueLabel.Text = "实时字符串：通信中断，缓存已清除";
                await client.DisconnectAsync(CancellationToken.None);
                await Task.Delay(1000, token);
                continue;
            }

            if (record != null)
            {
                // 落盘失败直接停止，避免界面显示“记录成功”却没有文件。
                RecordStore.Append(recordFolder, time, record);
                trigger.MarkRecorded();
                cache.Clear();
                count++;
                recordsGrid.Rows.Insert(0, count, time.ToString("yyyy-MM-dd HH:mm:ss.fff"),
                    record.Value.Length == 0 ? "（空字符串）" : record.Value,
                    record.Source, record.SampleTime?.ToString("yyyy-MM-dd HH:mm:ss.fff") ?? "—");
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
