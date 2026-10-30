namespace S7TriggerRecorder;

partial class MainForm
{
    private TextBox ipBox;
    private ComboBox cpuBox;
    private NumericUpDown rackBox, slotBox;
    private Button startButton, stopButton, folderButton;
    private Label statusLabel, pathLabel;
    private DataGridView recordsGrid;

    // 标准 WinForms 控件，界面与采集流程分别维护。
    private void InitializeComponent()
    {
        ipBox = new TextBox { Text = "192.168.0.50", Width = 135 };
        cpuBox = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Width = 105 };
        rackBox = new NumericUpDown { Maximum = 7, Width = 45 };
        slotBox = new NumericUpDown { Maximum = 31, Value = 1, Width = 45 };
        startButton = new Button { Text = "开始记录", AutoSize = true };
        stopButton = new Button { Text = "停止", Enabled = false, AutoSize = true };
        folderButton = new Button { Text = "打开记录目录", AutoSize = true };
        statusLabel = new Label { Text = "待启动", Dock = DockStyle.Fill, AutoSize = true };
        pathLabel = new Label { Dock = DockStyle.Fill, AutoSize = true };
        recordsGrid = new DataGridView
        {
            Dock = DockStyle.Fill, ReadOnly = true, AllowUserToAddRows = false,
            AllowUserToDeleteRows = false, RowHeadersVisible = false,
            AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill,
            SelectionMode = DataGridViewSelectionMode.FullRowSelect
        };
        recordsGrid.Columns.Add("Number", "序号");
        recordsGrid.Columns.Add("Time", "触发时间（电脑本地时间）");
        recordsGrid.Columns.Add("Value", "STRING[50] 值");
        recordsGrid.Columns[0].FillWeight = 18;
        var settings = new FlowLayoutPanel { Dock = DockStyle.Fill, AutoSize = true };
        settings.Controls.AddRange(new Control[] {
            new Label { Text = "PLC IP", AutoSize = true, Padding = new Padding(0, 7, 0, 0) }, ipBox,
            new Label { Text = "CPU", AutoSize = true, Padding = new Padding(0, 7, 0, 0) }, cpuBox,
            new Label { Text = "Rack", AutoSize = true, Padding = new Padding(0, 7, 0, 0) }, rackBox,
            new Label { Text = "Slot", AutoSize = true, Padding = new Padding(0, 7, 0, 0) }, slotBox,
            startButton, stopButton, folderButton });
        var layout = new TableLayoutPanel { Dock = DockStyle.Fill, RowCount = 5, ColumnCount = 1, Padding = new Padding(12) };
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 40));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 35));
        layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 35));
        layout.Controls.Add(settings, 0, 0);
        layout.Controls.Add(new Label { Text = "100 ms 轮询 DB2001.DBX1404.6；变为 TRUE 时记录 DB2001.DBX314.0 STRING[50]，直到读到 FALSE 才再次触发。", Dock = DockStyle.Fill, AutoSize = true }, 0, 1);
        layout.Controls.Add(statusLabel, 0, 2);
        layout.Controls.Add(recordsGrid, 0, 3);
        layout.Controls.Add(pathLabel, 0, 4);
        Controls.Add(layout);
        Text = "S7 PLC 字符串触发记录器";
        ClientSize = new Size(1050, 580);
        MinimumSize = new Size(950, 450);
        StartPosition = FormStartPosition.CenterScreen;
        AutoScaleMode = AutoScaleMode.Font;
        startButton.Click += StartButton_Click;
        stopButton.Click += StopButton_Click;
        folderButton.Click += FolderButton_Click;
        FormClosing += MainForm_FormClosing;
    }
}
