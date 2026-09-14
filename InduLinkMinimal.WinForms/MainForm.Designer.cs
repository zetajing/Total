namespace InduLinkMinimal.WinForms
{
    // 本文件由 Visual Studio WinForms 设计器维护。
    // 这里只保存控件声明、布局属性和事件绑定；协议通讯逻辑统一放在 MainForm.cs 中。
    // 手工修改后应立即用“查看设计器”验证，避免破坏设计器的代码序列化结构。
    partial class MainForm
    {
        private System.ComponentModel.IContainer components = null;
        private System.Windows.Forms.TabControl ProtocolTabControl;
        private System.Windows.Forms.TabPage ModbusTcpTabPage, ModbusRtuTabPage, S7TabPage, McTabPage, RawTcpTabPage, MesHttpTabPage;
        private System.Windows.Forms.FlowLayoutPanel ModbusTcpPanel, ModbusRtuPanel, S7Panel, McPanel, RawTcpPanel, MesHttpPanel;
        private System.Windows.Forms.Label label1, label2, label3, label4, label5, label6, label7, label8, label9, label10, label11, label12, label13, label14, label15, label16, label17, label18, label19, label20, label21, label22, label23, label24, label25, label26, label27, label34, label35, label36, label37, label38, label39, label40, label41, label42;
        private System.Windows.Forms.TextBox ModbusTcpHostTextBox, ModbusTcpPortTextBox, ModbusTcpSlaveTextBox, ModbusTcpAddressTextBox, ModbusTcpValueTextBox, ModbusTcpOutputTextBox;
        private System.Windows.Forms.ComboBox ModbusTcpTypeComboBox, ModbusTcpProfileComboBox;
        private System.Windows.Forms.Button ModbusTcpConnectButton, ModbusTcpReadButton, ModbusTcpWriteButton, ModbusTcpDisconnectButton;
        private System.Windows.Forms.TextBox ModbusRtuPortTextBox, ModbusRtuBaudTextBox, ModbusRtuSlaveTextBox, ModbusRtuAddressTextBox, ModbusRtuValueTextBox, ModbusRtuOutputTextBox;
        private System.Windows.Forms.ComboBox ModbusRtuTypeComboBox;
        private System.Windows.Forms.Button ModbusRtuConnectButton, ModbusRtuReadButton, ModbusRtuWriteButton, ModbusRtuDisconnectButton;
        private System.Windows.Forms.TextBox S7HostTextBox, S7RackTextBox, S7SlotTextBox, S7AddressTextBox, S7ValueTextBox, S7OutputTextBox;
        private System.Windows.Forms.ComboBox S7TypeComboBox;
        private System.Windows.Forms.Button S7ConnectButton, S7ReadButton, S7WriteButton, S7DisconnectButton;
        private System.Windows.Forms.TextBox McHostTextBox, McPortTextBox, McTimeoutTextBox, McAddressTextBox, McValueTextBox, McOutputTextBox;
        private System.Windows.Forms.ComboBox McTypeComboBox;
        private System.Windows.Forms.Button McConnectButton, McReadButton, McWriteButton, McDisconnectButton;
        private System.Windows.Forms.TextBox RawTcpHostTextBox, RawTcpPortTextBox, RawTcpPayloadTextBox, RawTcpDelimiterTextBox, RawTcpFrameLengthTextBox, RawTcpMaximumFrameLengthTextBox, RawTcpOutputTextBox;
        private System.Windows.Forms.ComboBox RawTcpFramingComboBox;
        private System.Windows.Forms.Button RawTcpConnectButton, RawTcpSendButton, RawTcpDisconnectButton;
        private System.Windows.Forms.TextBox MesHttpUrlTextBox, MesHttpEndpointTextBox, MesHttpJsonTextBox, MesHttpOutputTextBox;
        private System.Windows.Forms.Button MesHttpSendButton;

        /// <summary>释放设计器创建并登记在 components 容器中的控件资源。</summary>
        protected override void Dispose(bool disposing)
        {
            if (disposing && (components != null)) components.Dispose();
            base.Dispose(disposing);
        }

        /// <summary>
        /// 创建六个协议页签及其输入、操作和日志控件，并绑定 MainForm.cs 中的事件处理方法。
        /// 此方法只应由窗体构造函数调用。
        /// </summary>
        private void InitializeComponent()
        {
            ProtocolTabControl = new System.Windows.Forms.TabControl();
            ModbusTcpTabPage = new System.Windows.Forms.TabPage();
            ModbusTcpPanel = new System.Windows.Forms.FlowLayoutPanel();
            label1 = new System.Windows.Forms.Label();
            ModbusTcpHostTextBox = new System.Windows.Forms.TextBox();
            label2 = new System.Windows.Forms.Label();
            ModbusTcpPortTextBox = new System.Windows.Forms.TextBox();
            label3 = new System.Windows.Forms.Label();
            ModbusTcpSlaveTextBox = new System.Windows.Forms.TextBox();
            label4 = new System.Windows.Forms.Label();
            ModbusTcpAddressTextBox = new System.Windows.Forms.TextBox();
            label5 = new System.Windows.Forms.Label();
            ModbusTcpTypeComboBox = new System.Windows.Forms.ComboBox();
            label6 = new System.Windows.Forms.Label();
            ModbusTcpValueTextBox = new System.Windows.Forms.TextBox();
            ModbusTcpConnectButton = new System.Windows.Forms.Button();
            ModbusTcpReadButton = new System.Windows.Forms.Button();
            ModbusTcpWriteButton = new System.Windows.Forms.Button();
            ModbusTcpDisconnectButton = new System.Windows.Forms.Button();
            ModbusTcpOutputTextBox = new System.Windows.Forms.TextBox();
            label42 = new System.Windows.Forms.Label();
            ModbusTcpProfileComboBox = new System.Windows.Forms.ComboBox();
            ModbusRtuTabPage = new System.Windows.Forms.TabPage();
            ModbusRtuPanel = new System.Windows.Forms.FlowLayoutPanel();
            label7 = new System.Windows.Forms.Label();
            ModbusRtuPortTextBox = new System.Windows.Forms.TextBox();
            label8 = new System.Windows.Forms.Label();
            ModbusRtuBaudTextBox = new System.Windows.Forms.TextBox();
            label9 = new System.Windows.Forms.Label();
            ModbusRtuSlaveTextBox = new System.Windows.Forms.TextBox();
            label10 = new System.Windows.Forms.Label();
            ModbusRtuAddressTextBox = new System.Windows.Forms.TextBox();
            label11 = new System.Windows.Forms.Label();
            ModbusRtuTypeComboBox = new System.Windows.Forms.ComboBox();
            label12 = new System.Windows.Forms.Label();
            ModbusRtuValueTextBox = new System.Windows.Forms.TextBox();
            ModbusRtuConnectButton = new System.Windows.Forms.Button();
            ModbusRtuReadButton = new System.Windows.Forms.Button();
            ModbusRtuWriteButton = new System.Windows.Forms.Button();
            ModbusRtuDisconnectButton = new System.Windows.Forms.Button();
            ModbusRtuOutputTextBox = new System.Windows.Forms.TextBox();
            S7TabPage = new System.Windows.Forms.TabPage();
            S7Panel = new System.Windows.Forms.FlowLayoutPanel();
            label13 = new System.Windows.Forms.Label();
            S7HostTextBox = new System.Windows.Forms.TextBox();
            label14 = new System.Windows.Forms.Label();
            S7RackTextBox = new System.Windows.Forms.TextBox();
            label15 = new System.Windows.Forms.Label();
            S7SlotTextBox = new System.Windows.Forms.TextBox();
            label16 = new System.Windows.Forms.Label();
            S7AddressTextBox = new System.Windows.Forms.TextBox();
            label17 = new System.Windows.Forms.Label();
            S7TypeComboBox = new System.Windows.Forms.ComboBox();
            label18 = new System.Windows.Forms.Label();
            S7ValueTextBox = new System.Windows.Forms.TextBox();
            S7ConnectButton = new System.Windows.Forms.Button();
            S7ReadButton = new System.Windows.Forms.Button();
            S7WriteButton = new System.Windows.Forms.Button();
            S7DisconnectButton = new System.Windows.Forms.Button();
            S7OutputTextBox = new System.Windows.Forms.TextBox();
            McTabPage = new System.Windows.Forms.TabPage();
            McPanel = new System.Windows.Forms.FlowLayoutPanel();
            label19 = new System.Windows.Forms.Label();
            McHostTextBox = new System.Windows.Forms.TextBox();
            label20 = new System.Windows.Forms.Label();
            McPortTextBox = new System.Windows.Forms.TextBox();
            label21 = new System.Windows.Forms.Label();
            McTimeoutTextBox = new System.Windows.Forms.TextBox();
            label22 = new System.Windows.Forms.Label();
            McAddressTextBox = new System.Windows.Forms.TextBox();
            label23 = new System.Windows.Forms.Label();
            McTypeComboBox = new System.Windows.Forms.ComboBox();
            label24 = new System.Windows.Forms.Label();
            McValueTextBox = new System.Windows.Forms.TextBox();
            McConnectButton = new System.Windows.Forms.Button();
            McReadButton = new System.Windows.Forms.Button();
            McWriteButton = new System.Windows.Forms.Button();
            McDisconnectButton = new System.Windows.Forms.Button();
            McOutputTextBox = new System.Windows.Forms.TextBox();
            RawTcpTabPage = new System.Windows.Forms.TabPage();
            RawTcpPanel = new System.Windows.Forms.FlowLayoutPanel();
            label25 = new System.Windows.Forms.Label();
            RawTcpHostTextBox = new System.Windows.Forms.TextBox();
            label26 = new System.Windows.Forms.Label();
            RawTcpPortTextBox = new System.Windows.Forms.TextBox();
            label38 = new System.Windows.Forms.Label();
            RawTcpFramingComboBox = new System.Windows.Forms.ComboBox();
            label39 = new System.Windows.Forms.Label();
            RawTcpDelimiterTextBox = new System.Windows.Forms.TextBox();
            label40 = new System.Windows.Forms.Label();
            RawTcpFrameLengthTextBox = new System.Windows.Forms.TextBox();
            label41 = new System.Windows.Forms.Label();
            RawTcpMaximumFrameLengthTextBox = new System.Windows.Forms.TextBox();
            label27 = new System.Windows.Forms.Label();
            RawTcpPayloadTextBox = new System.Windows.Forms.TextBox();
            RawTcpConnectButton = new System.Windows.Forms.Button();
            RawTcpSendButton = new System.Windows.Forms.Button();
            RawTcpDisconnectButton = new System.Windows.Forms.Button();
            RawTcpOutputTextBox = new System.Windows.Forms.TextBox();
            MesHttpTabPage = new System.Windows.Forms.TabPage();
            MesHttpPanel = new System.Windows.Forms.FlowLayoutPanel();
            label34 = new System.Windows.Forms.Label();
            MesHttpUrlTextBox = new System.Windows.Forms.TextBox();
            label35 = new System.Windows.Forms.Label();
            MesHttpEndpointTextBox = new System.Windows.Forms.TextBox();
            label36 = new System.Windows.Forms.Label();
            MesHttpJsonTextBox = new System.Windows.Forms.TextBox();
            MesHttpSendButton = new System.Windows.Forms.Button();
            MesHttpOutputTextBox = new System.Windows.Forms.TextBox();
            label37 = new System.Windows.Forms.Label();
            ProtocolTabControl.SuspendLayout();
            ModbusTcpTabPage.SuspendLayout();
            ModbusTcpPanel.SuspendLayout();
            ModbusRtuTabPage.SuspendLayout();
            ModbusRtuPanel.SuspendLayout();
            S7TabPage.SuspendLayout();
            S7Panel.SuspendLayout();
            McTabPage.SuspendLayout();
            McPanel.SuspendLayout();
            RawTcpTabPage.SuspendLayout();
            RawTcpPanel.SuspendLayout();
            MesHttpTabPage.SuspendLayout();
            MesHttpPanel.SuspendLayout();
            SuspendLayout();
            // 
            // ProtocolTabControl
            // 
            ProtocolTabControl.Controls.Add(ModbusTcpTabPage);
            ProtocolTabControl.Controls.Add(ModbusRtuTabPage);
            ProtocolTabControl.Controls.Add(S7TabPage);
            ProtocolTabControl.Controls.Add(McTabPage);
            ProtocolTabControl.Controls.Add(RawTcpTabPage);
            ProtocolTabControl.Controls.Add(MesHttpTabPage);
            ProtocolTabControl.Dock = System.Windows.Forms.DockStyle.Fill;
            ProtocolTabControl.Location = new System.Drawing.Point(0, 0);
            ProtocolTabControl.Name = "ProtocolTabControl";
            ProtocolTabControl.SelectedIndex = 0;
            ProtocolTabControl.Size = new System.Drawing.Size(984, 681);
            ProtocolTabControl.TabIndex = 0;
            // 
            // ModbusTcpTabPage
            // 
            ModbusTcpTabPage.Controls.Add(ModbusTcpPanel);
            ModbusTcpTabPage.Location = new System.Drawing.Point(4, 29);
            ModbusTcpTabPage.Name = "ModbusTcpTabPage";
            ModbusTcpTabPage.Size = new System.Drawing.Size(976, 648);
            ModbusTcpTabPage.TabIndex = 0;
            ModbusTcpTabPage.Text = "Modbus TCP";
            // 
            // ModbusTcpPanel
            // 
            ModbusTcpPanel.AutoScroll = true;
            ModbusTcpPanel.Controls.Add(label1);
            ModbusTcpPanel.Controls.Add(ModbusTcpHostTextBox);
            ModbusTcpPanel.Controls.Add(label2);
            ModbusTcpPanel.Controls.Add(ModbusTcpPortTextBox);
            ModbusTcpPanel.Controls.Add(label3);
            ModbusTcpPanel.Controls.Add(ModbusTcpSlaveTextBox);
            ModbusTcpPanel.Controls.Add(label42);
            ModbusTcpPanel.Controls.Add(ModbusTcpProfileComboBox);
            ModbusTcpPanel.Controls.Add(label4);
            ModbusTcpPanel.Controls.Add(ModbusTcpAddressTextBox);
            ModbusTcpPanel.Controls.Add(label5);
            ModbusTcpPanel.Controls.Add(ModbusTcpTypeComboBox);
            ModbusTcpPanel.Controls.Add(label6);
            ModbusTcpPanel.Controls.Add(ModbusTcpValueTextBox);
            ModbusTcpPanel.Controls.Add(ModbusTcpConnectButton);
            ModbusTcpPanel.Controls.Add(ModbusTcpReadButton);
            ModbusTcpPanel.Controls.Add(ModbusTcpWriteButton);
            ModbusTcpPanel.Controls.Add(ModbusTcpDisconnectButton);
            ModbusTcpPanel.Controls.Add(ModbusTcpOutputTextBox);
            ModbusTcpPanel.Dock = System.Windows.Forms.DockStyle.Fill;
            ModbusTcpPanel.FlowDirection = System.Windows.Forms.FlowDirection.TopDown;
            ModbusTcpPanel.Location = new System.Drawing.Point(0, 0);
            ModbusTcpPanel.Name = "ModbusTcpPanel";
            ModbusTcpPanel.Size = new System.Drawing.Size(976, 648);
            ModbusTcpPanel.TabIndex = 0;
            ModbusTcpPanel.WrapContents = false;
            // 
            // label1
            // 
            label1.Location = new System.Drawing.Point(3, 0);
            label1.Name = "label1";
            label1.Size = new System.Drawing.Size(100, 23);
            label1.TabIndex = 0;
            label1.Text = "主机";
            // 
            // ModbusTcpHostTextBox
            // 
            ModbusTcpHostTextBox.Location = new System.Drawing.Point(3, 26);
            ModbusTcpHostTextBox.Name = "ModbusTcpHostTextBox";
            ModbusTcpHostTextBox.Size = new System.Drawing.Size(100, 27);
            ModbusTcpHostTextBox.TabIndex = 1;
            ModbusTcpHostTextBox.Text = "127.0.0.1";
            // 
            // label2
            // 
            label2.Location = new System.Drawing.Point(3, 56);
            label2.Name = "label2";
            label2.Size = new System.Drawing.Size(100, 23);
            label2.TabIndex = 2;
            label2.Text = "端口";
            // 
            // ModbusTcpPortTextBox
            // 
            ModbusTcpPortTextBox.Location = new System.Drawing.Point(3, 82);
            ModbusTcpPortTextBox.Name = "ModbusTcpPortTextBox";
            ModbusTcpPortTextBox.Size = new System.Drawing.Size(100, 27);
            ModbusTcpPortTextBox.TabIndex = 3;
            ModbusTcpPortTextBox.Text = "502";
            // 
            // label3
            // 
            label3.Location = new System.Drawing.Point(3, 112);
            label3.Name = "label3";
            label3.Size = new System.Drawing.Size(100, 23);
            label3.TabIndex = 4;
            label3.Text = "站号";
            // 
            // ModbusTcpSlaveTextBox
            // 
            ModbusTcpSlaveTextBox.Location = new System.Drawing.Point(3, 138);
            ModbusTcpSlaveTextBox.Name = "ModbusTcpSlaveTextBox";
            ModbusTcpSlaveTextBox.Size = new System.Drawing.Size(100, 27);
            ModbusTcpSlaveTextBox.TabIndex = 5;
            ModbusTcpSlaveTextBox.Text = "1";
            // 
            // label4
            // 
            label4.Location = new System.Drawing.Point(3, 225);
            label4.Name = "label4";
            label4.Size = new System.Drawing.Size(100, 23);
            label4.TabIndex = 6;
            label4.Text = "地址";
            // 
            // ModbusTcpAddressTextBox
            // 
            ModbusTcpAddressTextBox.Location = new System.Drawing.Point(3, 251);
            ModbusTcpAddressTextBox.Name = "ModbusTcpAddressTextBox";
            ModbusTcpAddressTextBox.Size = new System.Drawing.Size(100, 27);
            ModbusTcpAddressTextBox.TabIndex = 7;
            ModbusTcpAddressTextBox.Text = "HR0";
            // 
            // label5
            // 
            label5.Location = new System.Drawing.Point(3, 281);
            label5.Name = "label5";
            label5.Size = new System.Drawing.Size(100, 23);
            label5.TabIndex = 8;
            label5.Text = "数据类型";
            // 
            // ModbusTcpTypeComboBox
            // 
            ModbusTcpTypeComboBox.Items.AddRange(new object[] { "Bool", "Int16", "UInt16", "Int32", "UInt32", "Float", "Double", "Byte", "Char", "String", "ByteArray" });
            ModbusTcpTypeComboBox.Location = new System.Drawing.Point(3, 307);
            ModbusTcpTypeComboBox.Name = "ModbusTcpTypeComboBox";
            ModbusTcpTypeComboBox.Size = new System.Drawing.Size(121, 28);
            ModbusTcpTypeComboBox.TabIndex = 9;
            ModbusTcpTypeComboBox.Text = "Int16";
            // 
            // label6
            // 
            label6.Location = new System.Drawing.Point(3, 338);
            label6.Name = "label6";
            label6.Size = new System.Drawing.Size(100, 23);
            label6.TabIndex = 10;
            label6.Text = "写入值";
            // 
            // ModbusTcpValueTextBox
            // 
            ModbusTcpValueTextBox.Location = new System.Drawing.Point(3, 364);
            ModbusTcpValueTextBox.Name = "ModbusTcpValueTextBox";
            ModbusTcpValueTextBox.Size = new System.Drawing.Size(100, 27);
            ModbusTcpValueTextBox.TabIndex = 11;
            ModbusTcpValueTextBox.Text = "0";
            // 
            // ModbusTcpConnectButton
            // 
            ModbusTcpConnectButton.Location = new System.Drawing.Point(3, 397);
            ModbusTcpConnectButton.Name = "ModbusTcpConnectButton";
            ModbusTcpConnectButton.Size = new System.Drawing.Size(75, 23);
            ModbusTcpConnectButton.TabIndex = 12;
            ModbusTcpConnectButton.Text = "连接";
            ModbusTcpConnectButton.Click += ModbusTcpConnectButton_Click;
            // 
            // ModbusTcpReadButton
            // 
            ModbusTcpReadButton.Location = new System.Drawing.Point(3, 426);
            ModbusTcpReadButton.Name = "ModbusTcpReadButton";
            ModbusTcpReadButton.Size = new System.Drawing.Size(75, 23);
            ModbusTcpReadButton.TabIndex = 13;
            ModbusTcpReadButton.Text = "读取";
            ModbusTcpReadButton.Click += InduLinkReadButton_Click;
            // 
            // ModbusTcpWriteButton
            // 
            ModbusTcpWriteButton.Location = new System.Drawing.Point(3, 455);
            ModbusTcpWriteButton.Name = "ModbusTcpWriteButton";
            ModbusTcpWriteButton.Size = new System.Drawing.Size(75, 23);
            ModbusTcpWriteButton.TabIndex = 14;
            ModbusTcpWriteButton.Text = "写入";
            ModbusTcpWriteButton.Click += InduLinkWriteButton_Click;
            // 
            // ModbusTcpDisconnectButton
            // 
            ModbusTcpDisconnectButton.Location = new System.Drawing.Point(3, 484);
            ModbusTcpDisconnectButton.Name = "ModbusTcpDisconnectButton";
            ModbusTcpDisconnectButton.Size = new System.Drawing.Size(75, 23);
            ModbusTcpDisconnectButton.TabIndex = 15;
            ModbusTcpDisconnectButton.Text = "断开";
            ModbusTcpDisconnectButton.Click += InduLinkDisconnectButton_Click;
            // 
            // ModbusTcpOutputTextBox
            // 
            ModbusTcpOutputTextBox.Location = new System.Drawing.Point(3, 513);
            ModbusTcpOutputTextBox.Multiline = true;
            ModbusTcpOutputTextBox.Name = "ModbusTcpOutputTextBox";
            ModbusTcpOutputTextBox.Size = new System.Drawing.Size(850, 220);
            ModbusTcpOutputTextBox.TabIndex = 16;
            // 
            // label42
            // 
            label42.Location = new System.Drawing.Point(3, 168);
            label42.Name = "label42";
            label42.Size = new System.Drawing.Size(100, 23);
            label42.TabIndex = 17;
            label42.Text = "设备配置（选择后自动更新示例地址）";
            // 
            // ModbusTcpProfileComboBox
            // 
            ModbusTcpProfileComboBox.Location = new System.Drawing.Point(3, 194);
            ModbusTcpProfileComboBox.Name = "ModbusTcpProfileComboBox";
            ModbusTcpProfileComboBox.Size = new System.Drawing.Size(121, 28);
            ModbusTcpProfileComboBox.TabIndex = 18;
            // 
            // ModbusRtuTabPage
            // 
            ModbusRtuTabPage.Controls.Add(ModbusRtuPanel);
            ModbusRtuTabPage.Location = new System.Drawing.Point(4, 29);
            ModbusRtuTabPage.Name = "ModbusRtuTabPage";
            ModbusRtuTabPage.Size = new System.Drawing.Size(976, 648);
            ModbusRtuTabPage.TabIndex = 1;
            ModbusRtuTabPage.Text = "Modbus RTU";
            // 
            // ModbusRtuPanel
            // 
            ModbusRtuPanel.AutoScroll = true;
            ModbusRtuPanel.Controls.Add(label7);
            ModbusRtuPanel.Controls.Add(ModbusRtuPortTextBox);
            ModbusRtuPanel.Controls.Add(label8);
            ModbusRtuPanel.Controls.Add(ModbusRtuBaudTextBox);
            ModbusRtuPanel.Controls.Add(label9);
            ModbusRtuPanel.Controls.Add(ModbusRtuSlaveTextBox);
            ModbusRtuPanel.Controls.Add(label10);
            ModbusRtuPanel.Controls.Add(ModbusRtuAddressTextBox);
            ModbusRtuPanel.Controls.Add(label11);
            ModbusRtuPanel.Controls.Add(ModbusRtuTypeComboBox);
            ModbusRtuPanel.Controls.Add(label12);
            ModbusRtuPanel.Controls.Add(ModbusRtuValueTextBox);
            ModbusRtuPanel.Controls.Add(ModbusRtuConnectButton);
            ModbusRtuPanel.Controls.Add(ModbusRtuReadButton);
            ModbusRtuPanel.Controls.Add(ModbusRtuWriteButton);
            ModbusRtuPanel.Controls.Add(ModbusRtuDisconnectButton);
            ModbusRtuPanel.Controls.Add(ModbusRtuOutputTextBox);
            ModbusRtuPanel.Dock = System.Windows.Forms.DockStyle.Fill;
            ModbusRtuPanel.FlowDirection = System.Windows.Forms.FlowDirection.TopDown;
            ModbusRtuPanel.Location = new System.Drawing.Point(0, 0);
            ModbusRtuPanel.Name = "ModbusRtuPanel";
            ModbusRtuPanel.Size = new System.Drawing.Size(976, 648);
            ModbusRtuPanel.TabIndex = 0;
            ModbusRtuPanel.WrapContents = false;
            // 
            // label7
            // 
            label7.Location = new System.Drawing.Point(3, 0);
            label7.Name = "label7";
            label7.Size = new System.Drawing.Size(100, 23);
            label7.TabIndex = 0;
            label7.Text = "串口";
            // 
            // ModbusRtuPortTextBox
            // 
            ModbusRtuPortTextBox.Location = new System.Drawing.Point(3, 26);
            ModbusRtuPortTextBox.Name = "ModbusRtuPortTextBox";
            ModbusRtuPortTextBox.Size = new System.Drawing.Size(100, 27);
            ModbusRtuPortTextBox.TabIndex = 1;
            ModbusRtuPortTextBox.Text = "COM3";
            // 
            // label8
            // 
            label8.Location = new System.Drawing.Point(3, 56);
            label8.Name = "label8";
            label8.Size = new System.Drawing.Size(100, 23);
            label8.TabIndex = 2;
            label8.Text = "波特率";
            // 
            // ModbusRtuBaudTextBox
            // 
            ModbusRtuBaudTextBox.Location = new System.Drawing.Point(3, 82);
            ModbusRtuBaudTextBox.Name = "ModbusRtuBaudTextBox";
            ModbusRtuBaudTextBox.Size = new System.Drawing.Size(100, 27);
            ModbusRtuBaudTextBox.TabIndex = 3;
            ModbusRtuBaudTextBox.Text = "9600";
            // 
            // label9
            // 
            label9.Location = new System.Drawing.Point(3, 112);
            label9.Name = "label9";
            label9.Size = new System.Drawing.Size(100, 23);
            label9.TabIndex = 4;
            label9.Text = "站号";
            // 
            // ModbusRtuSlaveTextBox
            // 
            ModbusRtuSlaveTextBox.Location = new System.Drawing.Point(3, 138);
            ModbusRtuSlaveTextBox.Name = "ModbusRtuSlaveTextBox";
            ModbusRtuSlaveTextBox.Size = new System.Drawing.Size(100, 27);
            ModbusRtuSlaveTextBox.TabIndex = 5;
            ModbusRtuSlaveTextBox.Text = "1";
            // 
            // label10
            // 
            label10.Location = new System.Drawing.Point(3, 168);
            label10.Name = "label10";
            label10.Size = new System.Drawing.Size(100, 23);
            label10.TabIndex = 6;
            label10.Text = "地址";
            // 
            // ModbusRtuAddressTextBox
            // 
            ModbusRtuAddressTextBox.Location = new System.Drawing.Point(3, 194);
            ModbusRtuAddressTextBox.Name = "ModbusRtuAddressTextBox";
            ModbusRtuAddressTextBox.Size = new System.Drawing.Size(100, 27);
            ModbusRtuAddressTextBox.TabIndex = 7;
            ModbusRtuAddressTextBox.Text = "HR0";
            // 
            // label11
            // 
            label11.Location = new System.Drawing.Point(3, 224);
            label11.Name = "label11";
            label11.Size = new System.Drawing.Size(100, 23);
            label11.TabIndex = 8;
            label11.Text = "数据类型";
            // 
            // ModbusRtuTypeComboBox
            // 
            ModbusRtuTypeComboBox.Items.AddRange(new object[] { "Bool", "Int16", "UInt16", "Int32", "UInt32", "Float", "Double", "Byte", "Char", "String", "ByteArray" });
            ModbusRtuTypeComboBox.Location = new System.Drawing.Point(3, 250);
            ModbusRtuTypeComboBox.Name = "ModbusRtuTypeComboBox";
            ModbusRtuTypeComboBox.Size = new System.Drawing.Size(121, 28);
            ModbusRtuTypeComboBox.TabIndex = 9;
            ModbusRtuTypeComboBox.Text = "Int16";
            // 
            // label12
            // 
            label12.Location = new System.Drawing.Point(3, 281);
            label12.Name = "label12";
            label12.Size = new System.Drawing.Size(100, 23);
            label12.TabIndex = 10;
            label12.Text = "写入值";
            // 
            // ModbusRtuValueTextBox
            // 
            ModbusRtuValueTextBox.Location = new System.Drawing.Point(3, 307);
            ModbusRtuValueTextBox.Name = "ModbusRtuValueTextBox";
            ModbusRtuValueTextBox.Size = new System.Drawing.Size(100, 27);
            ModbusRtuValueTextBox.TabIndex = 11;
            ModbusRtuValueTextBox.Text = "0";
            // 
            // ModbusRtuConnectButton
            // 
            ModbusRtuConnectButton.Location = new System.Drawing.Point(3, 340);
            ModbusRtuConnectButton.Name = "ModbusRtuConnectButton";
            ModbusRtuConnectButton.Size = new System.Drawing.Size(75, 23);
            ModbusRtuConnectButton.TabIndex = 12;
            ModbusRtuConnectButton.Text = "连接";
            ModbusRtuConnectButton.Click += ModbusRtuConnectButton_Click;
            // 
            // ModbusRtuReadButton
            // 
            ModbusRtuReadButton.Location = new System.Drawing.Point(3, 369);
            ModbusRtuReadButton.Name = "ModbusRtuReadButton";
            ModbusRtuReadButton.Size = new System.Drawing.Size(75, 23);
            ModbusRtuReadButton.TabIndex = 13;
            ModbusRtuReadButton.Text = "读取";
            ModbusRtuReadButton.Click += InduLinkReadButton_Click;
            // 
            // ModbusRtuWriteButton
            // 
            ModbusRtuWriteButton.Location = new System.Drawing.Point(3, 398);
            ModbusRtuWriteButton.Name = "ModbusRtuWriteButton";
            ModbusRtuWriteButton.Size = new System.Drawing.Size(75, 23);
            ModbusRtuWriteButton.TabIndex = 14;
            ModbusRtuWriteButton.Text = "写入";
            ModbusRtuWriteButton.Click += InduLinkWriteButton_Click;
            // 
            // ModbusRtuDisconnectButton
            // 
            ModbusRtuDisconnectButton.Location = new System.Drawing.Point(3, 427);
            ModbusRtuDisconnectButton.Name = "ModbusRtuDisconnectButton";
            ModbusRtuDisconnectButton.Size = new System.Drawing.Size(75, 23);
            ModbusRtuDisconnectButton.TabIndex = 15;
            ModbusRtuDisconnectButton.Text = "断开";
            ModbusRtuDisconnectButton.Click += InduLinkDisconnectButton_Click;
            // 
            // ModbusRtuOutputTextBox
            // 
            ModbusRtuOutputTextBox.Location = new System.Drawing.Point(3, 456);
            ModbusRtuOutputTextBox.Multiline = true;
            ModbusRtuOutputTextBox.Name = "ModbusRtuOutputTextBox";
            ModbusRtuOutputTextBox.Size = new System.Drawing.Size(850, 220);
            ModbusRtuOutputTextBox.TabIndex = 16;
            // 
            // S7TabPage
            // 
            S7TabPage.Controls.Add(S7Panel);
            S7TabPage.Location = new System.Drawing.Point(4, 29);
            S7TabPage.Name = "S7TabPage";
            S7TabPage.Size = new System.Drawing.Size(976, 648);
            S7TabPage.TabIndex = 2;
            S7TabPage.Text = "Siemens S7";
            // 
            // S7Panel
            // 
            S7Panel.AutoScroll = true;
            S7Panel.Controls.Add(label13);
            S7Panel.Controls.Add(S7HostTextBox);
            S7Panel.Controls.Add(label14);
            S7Panel.Controls.Add(S7RackTextBox);
            S7Panel.Controls.Add(label15);
            S7Panel.Controls.Add(S7SlotTextBox);
            S7Panel.Controls.Add(label16);
            S7Panel.Controls.Add(S7AddressTextBox);
            S7Panel.Controls.Add(label17);
            S7Panel.Controls.Add(S7TypeComboBox);
            S7Panel.Controls.Add(label18);
            S7Panel.Controls.Add(S7ValueTextBox);
            S7Panel.Controls.Add(S7ConnectButton);
            S7Panel.Controls.Add(S7ReadButton);
            S7Panel.Controls.Add(S7WriteButton);
            S7Panel.Controls.Add(S7DisconnectButton);
            S7Panel.Controls.Add(S7OutputTextBox);
            S7Panel.Dock = System.Windows.Forms.DockStyle.Fill;
            S7Panel.FlowDirection = System.Windows.Forms.FlowDirection.TopDown;
            S7Panel.Location = new System.Drawing.Point(0, 0);
            S7Panel.Name = "S7Panel";
            S7Panel.Size = new System.Drawing.Size(976, 648);
            S7Panel.TabIndex = 0;
            S7Panel.WrapContents = false;
            // 
            // label13
            // 
            label13.Location = new System.Drawing.Point(3, 0);
            label13.Name = "label13";
            label13.Size = new System.Drawing.Size(100, 23);
            label13.TabIndex = 0;
            label13.Text = "主机";
            // 
            // S7HostTextBox
            // 
            S7HostTextBox.Location = new System.Drawing.Point(3, 26);
            S7HostTextBox.Name = "S7HostTextBox";
            S7HostTextBox.Size = new System.Drawing.Size(100, 27);
            S7HostTextBox.TabIndex = 1;
            S7HostTextBox.Text = "127.0.0.1";
            // 
            // label14
            // 
            label14.Location = new System.Drawing.Point(3, 56);
            label14.Name = "label14";
            label14.Size = new System.Drawing.Size(100, 23);
            label14.TabIndex = 2;
            label14.Text = "机架";
            // 
            // S7RackTextBox
            // 
            S7RackTextBox.Location = new System.Drawing.Point(3, 82);
            S7RackTextBox.Name = "S7RackTextBox";
            S7RackTextBox.Size = new System.Drawing.Size(100, 27);
            S7RackTextBox.TabIndex = 3;
            S7RackTextBox.Text = "0";
            // 
            // label15
            // 
            label15.Location = new System.Drawing.Point(3, 112);
            label15.Name = "label15";
            label15.Size = new System.Drawing.Size(100, 23);
            label15.TabIndex = 4;
            label15.Text = "插槽";
            // 
            // S7SlotTextBox
            // 
            S7SlotTextBox.Location = new System.Drawing.Point(3, 138);
            S7SlotTextBox.Name = "S7SlotTextBox";
            S7SlotTextBox.Size = new System.Drawing.Size(100, 27);
            S7SlotTextBox.TabIndex = 5;
            S7SlotTextBox.Text = "1";
            // 
            // label16
            // 
            label16.Location = new System.Drawing.Point(3, 168);
            label16.Name = "label16";
            label16.Size = new System.Drawing.Size(100, 23);
            label16.TabIndex = 6;
            label16.Text = "地址";
            // 
            // S7AddressTextBox
            // 
            S7AddressTextBox.Location = new System.Drawing.Point(3, 194);
            S7AddressTextBox.Name = "S7AddressTextBox";
            S7AddressTextBox.Size = new System.Drawing.Size(100, 27);
            S7AddressTextBox.TabIndex = 7;
            S7AddressTextBox.Text = "DB1.DBW0";
            // 
            // label17
            // 
            label17.Location = new System.Drawing.Point(3, 224);
            label17.Name = "label17";
            label17.Size = new System.Drawing.Size(100, 23);
            label17.TabIndex = 8;
            label17.Text = "数据类型";
            // 
            // S7TypeComboBox
            // 
            S7TypeComboBox.Items.AddRange(new object[] { "Bool", "Int16", "UInt16", "Int32", "UInt32", "Float", "Double", "Byte", "Char", "String", "ByteArray" });
            S7TypeComboBox.Location = new System.Drawing.Point(3, 250);
            S7TypeComboBox.Name = "S7TypeComboBox";
            S7TypeComboBox.Size = new System.Drawing.Size(121, 28);
            S7TypeComboBox.TabIndex = 9;
            S7TypeComboBox.Text = "Int16";
            // 
            // label18
            // 
            label18.Location = new System.Drawing.Point(3, 281);
            label18.Name = "label18";
            label18.Size = new System.Drawing.Size(100, 23);
            label18.TabIndex = 10;
            label18.Text = "写入值";
            // 
            // S7ValueTextBox
            // 
            S7ValueTextBox.Location = new System.Drawing.Point(3, 307);
            S7ValueTextBox.Name = "S7ValueTextBox";
            S7ValueTextBox.Size = new System.Drawing.Size(100, 27);
            S7ValueTextBox.TabIndex = 11;
            S7ValueTextBox.Text = "0";
            // 
            // S7ConnectButton
            // 
            S7ConnectButton.Location = new System.Drawing.Point(3, 340);
            S7ConnectButton.Name = "S7ConnectButton";
            S7ConnectButton.Size = new System.Drawing.Size(75, 23);
            S7ConnectButton.TabIndex = 12;
            S7ConnectButton.Text = "连接";
            S7ConnectButton.Click += S7ConnectButton_Click;
            // 
            // S7ReadButton
            // 
            S7ReadButton.Location = new System.Drawing.Point(3, 369);
            S7ReadButton.Name = "S7ReadButton";
            S7ReadButton.Size = new System.Drawing.Size(75, 23);
            S7ReadButton.TabIndex = 13;
            S7ReadButton.Text = "读取";
            S7ReadButton.Click += InduLinkReadButton_Click;
            // 
            // S7WriteButton
            // 
            S7WriteButton.Location = new System.Drawing.Point(3, 398);
            S7WriteButton.Name = "S7WriteButton";
            S7WriteButton.Size = new System.Drawing.Size(75, 23);
            S7WriteButton.TabIndex = 14;
            S7WriteButton.Text = "写入";
            S7WriteButton.Click += InduLinkWriteButton_Click;
            // 
            // S7DisconnectButton
            // 
            S7DisconnectButton.Location = new System.Drawing.Point(3, 427);
            S7DisconnectButton.Name = "S7DisconnectButton";
            S7DisconnectButton.Size = new System.Drawing.Size(75, 23);
            S7DisconnectButton.TabIndex = 15;
            S7DisconnectButton.Text = "断开";
            S7DisconnectButton.Click += InduLinkDisconnectButton_Click;
            // 
            // S7OutputTextBox
            // 
            S7OutputTextBox.Location = new System.Drawing.Point(3, 456);
            S7OutputTextBox.Multiline = true;
            S7OutputTextBox.Name = "S7OutputTextBox";
            S7OutputTextBox.Size = new System.Drawing.Size(850, 220);
            S7OutputTextBox.TabIndex = 16;
            // 
            // McTabPage
            // 
            McTabPage.Controls.Add(McPanel);
            McTabPage.Location = new System.Drawing.Point(4, 29);
            McTabPage.Name = "McTabPage";
            McTabPage.Size = new System.Drawing.Size(976, 648);
            McTabPage.TabIndex = 3;
            McTabPage.Text = "Mitsubishi MC";
            // 
            // McPanel
            // 
            McPanel.AutoScroll = true;
            McPanel.Controls.Add(label19);
            McPanel.Controls.Add(McHostTextBox);
            McPanel.Controls.Add(label20);
            McPanel.Controls.Add(McPortTextBox);
            McPanel.Controls.Add(label21);
            McPanel.Controls.Add(McTimeoutTextBox);
            McPanel.Controls.Add(label22);
            McPanel.Controls.Add(McAddressTextBox);
            McPanel.Controls.Add(label23);
            McPanel.Controls.Add(McTypeComboBox);
            McPanel.Controls.Add(label24);
            McPanel.Controls.Add(McValueTextBox);
            McPanel.Controls.Add(McConnectButton);
            McPanel.Controls.Add(McReadButton);
            McPanel.Controls.Add(McWriteButton);
            McPanel.Controls.Add(McDisconnectButton);
            McPanel.Controls.Add(McOutputTextBox);
            McPanel.Dock = System.Windows.Forms.DockStyle.Fill;
            McPanel.FlowDirection = System.Windows.Forms.FlowDirection.TopDown;
            McPanel.Location = new System.Drawing.Point(0, 0);
            McPanel.Name = "McPanel";
            McPanel.Size = new System.Drawing.Size(976, 648);
            McPanel.TabIndex = 0;
            McPanel.WrapContents = false;
            // 
            // label19
            // 
            label19.Location = new System.Drawing.Point(3, 0);
            label19.Name = "label19";
            label19.Size = new System.Drawing.Size(100, 23);
            label19.TabIndex = 0;
            label19.Text = "主机";
            // 
            // McHostTextBox
            // 
            McHostTextBox.Location = new System.Drawing.Point(3, 26);
            McHostTextBox.Name = "McHostTextBox";
            McHostTextBox.Size = new System.Drawing.Size(100, 27);
            McHostTextBox.TabIndex = 1;
            McHostTextBox.Text = "127.0.0.1";
            // 
            // label20
            // 
            label20.Location = new System.Drawing.Point(3, 56);
            label20.Name = "label20";
            label20.Size = new System.Drawing.Size(100, 23);
            label20.TabIndex = 2;
            label20.Text = "端口";
            // 
            // McPortTextBox
            // 
            McPortTextBox.Location = new System.Drawing.Point(3, 82);
            McPortTextBox.Name = "McPortTextBox";
            McPortTextBox.Size = new System.Drawing.Size(100, 27);
            McPortTextBox.TabIndex = 3;
            McPortTextBox.Text = "5000";
            // 
            // label21
            // 
            label21.Location = new System.Drawing.Point(3, 112);
            label21.Name = "label21";
            label21.Size = new System.Drawing.Size(100, 23);
            label21.TabIndex = 4;
            label21.Text = "接收超时(ms)";
            // 
            // McTimeoutTextBox
            // 
            McTimeoutTextBox.Location = new System.Drawing.Point(3, 138);
            McTimeoutTextBox.Name = "McTimeoutTextBox";
            McTimeoutTextBox.Size = new System.Drawing.Size(100, 27);
            McTimeoutTextBox.TabIndex = 5;
            McTimeoutTextBox.Text = "5000";
            // 
            // label22
            // 
            label22.Location = new System.Drawing.Point(3, 168);
            label22.Name = "label22";
            label22.Size = new System.Drawing.Size(100, 23);
            label22.TabIndex = 6;
            label22.Text = "地址";
            // 
            // McAddressTextBox
            // 
            McAddressTextBox.Location = new System.Drawing.Point(3, 194);
            McAddressTextBox.Name = "McAddressTextBox";
            McAddressTextBox.Size = new System.Drawing.Size(100, 27);
            McAddressTextBox.TabIndex = 7;
            McAddressTextBox.Text = "D100";
            // 
            // label23
            // 
            label23.Location = new System.Drawing.Point(3, 224);
            label23.Name = "label23";
            label23.Size = new System.Drawing.Size(100, 23);
            label23.TabIndex = 8;
            label23.Text = "数据类型";
            // 
            // McTypeComboBox
            // 
            McTypeComboBox.Items.AddRange(new object[] { "Bool", "Int16", "UInt16", "Int32", "UInt32", "Float", "Double", "Byte", "Char", "String", "ByteArray" });
            McTypeComboBox.Location = new System.Drawing.Point(3, 250);
            McTypeComboBox.Name = "McTypeComboBox";
            McTypeComboBox.Size = new System.Drawing.Size(121, 28);
            McTypeComboBox.TabIndex = 9;
            McTypeComboBox.Text = "Int16";
            // 
            // label24
            // 
            label24.Location = new System.Drawing.Point(3, 281);
            label24.Name = "label24";
            label24.Size = new System.Drawing.Size(100, 23);
            label24.TabIndex = 10;
            label24.Text = "写入值";
            // 
            // McValueTextBox
            // 
            McValueTextBox.Location = new System.Drawing.Point(3, 307);
            McValueTextBox.Name = "McValueTextBox";
            McValueTextBox.Size = new System.Drawing.Size(100, 27);
            McValueTextBox.TabIndex = 11;
            McValueTextBox.Text = "0";
            // 
            // McConnectButton
            // 
            McConnectButton.Location = new System.Drawing.Point(3, 340);
            McConnectButton.Name = "McConnectButton";
            McConnectButton.Size = new System.Drawing.Size(75, 23);
            McConnectButton.TabIndex = 12;
            McConnectButton.Text = "连接";
            McConnectButton.Click += McConnectButton_Click;
            // 
            // McReadButton
            // 
            McReadButton.Location = new System.Drawing.Point(3, 369);
            McReadButton.Name = "McReadButton";
            McReadButton.Size = new System.Drawing.Size(75, 23);
            McReadButton.TabIndex = 13;
            McReadButton.Text = "读取";
            McReadButton.Click += InduLinkReadButton_Click;
            // 
            // McWriteButton
            // 
            McWriteButton.Location = new System.Drawing.Point(3, 398);
            McWriteButton.Name = "McWriteButton";
            McWriteButton.Size = new System.Drawing.Size(75, 23);
            McWriteButton.TabIndex = 14;
            McWriteButton.Text = "写入";
            McWriteButton.Click += InduLinkWriteButton_Click;
            // 
            // McDisconnectButton
            // 
            McDisconnectButton.Location = new System.Drawing.Point(3, 427);
            McDisconnectButton.Name = "McDisconnectButton";
            McDisconnectButton.Size = new System.Drawing.Size(75, 23);
            McDisconnectButton.TabIndex = 15;
            McDisconnectButton.Text = "断开";
            McDisconnectButton.Click += InduLinkDisconnectButton_Click;
            // 
            // McOutputTextBox
            // 
            McOutputTextBox.Location = new System.Drawing.Point(3, 456);
            McOutputTextBox.Multiline = true;
            McOutputTextBox.Name = "McOutputTextBox";
            McOutputTextBox.Size = new System.Drawing.Size(850, 220);
            McOutputTextBox.TabIndex = 16;
            // 
            // RawTcpTabPage
            // 
            RawTcpTabPage.Controls.Add(RawTcpPanel);
            RawTcpTabPage.Location = new System.Drawing.Point(4, 29);
            RawTcpTabPage.Name = "RawTcpTabPage";
            RawTcpTabPage.Size = new System.Drawing.Size(976, 648);
            RawTcpTabPage.TabIndex = 4;
            RawTcpTabPage.Text = "原始 TCP";
            // 
            // RawTcpPanel
            // 
            RawTcpPanel.AutoScroll = true;
            RawTcpPanel.Controls.Add(label25);
            RawTcpPanel.Controls.Add(RawTcpHostTextBox);
            RawTcpPanel.Controls.Add(label26);
            RawTcpPanel.Controls.Add(RawTcpPortTextBox);
            RawTcpPanel.Controls.Add(label38);
            RawTcpPanel.Controls.Add(RawTcpFramingComboBox);
            RawTcpPanel.Controls.Add(label39);
            RawTcpPanel.Controls.Add(RawTcpDelimiterTextBox);
            RawTcpPanel.Controls.Add(label40);
            RawTcpPanel.Controls.Add(RawTcpFrameLengthTextBox);
            RawTcpPanel.Controls.Add(label41);
            RawTcpPanel.Controls.Add(RawTcpMaximumFrameLengthTextBox);
            RawTcpPanel.Controls.Add(label27);
            RawTcpPanel.Controls.Add(RawTcpPayloadTextBox);
            RawTcpPanel.Controls.Add(RawTcpConnectButton);
            RawTcpPanel.Controls.Add(RawTcpSendButton);
            RawTcpPanel.Controls.Add(RawTcpDisconnectButton);
            RawTcpPanel.Controls.Add(RawTcpOutputTextBox);
            RawTcpPanel.Dock = System.Windows.Forms.DockStyle.Fill;
            RawTcpPanel.FlowDirection = System.Windows.Forms.FlowDirection.TopDown;
            RawTcpPanel.Location = new System.Drawing.Point(0, 0);
            RawTcpPanel.Name = "RawTcpPanel";
            RawTcpPanel.Size = new System.Drawing.Size(976, 648);
            RawTcpPanel.TabIndex = 0;
            RawTcpPanel.WrapContents = false;
            // 
            // label25
            // 
            label25.Location = new System.Drawing.Point(3, 0);
            label25.Name = "label25";
            label25.Size = new System.Drawing.Size(100, 23);
            label25.TabIndex = 0;
            label25.Text = "主机";
            // 
            // RawTcpHostTextBox
            // 
            RawTcpHostTextBox.Location = new System.Drawing.Point(3, 26);
            RawTcpHostTextBox.Name = "RawTcpHostTextBox";
            RawTcpHostTextBox.Size = new System.Drawing.Size(100, 27);
            RawTcpHostTextBox.TabIndex = 1;
            RawTcpHostTextBox.Text = "127.0.0.1";
            // 
            // label26
            // 
            label26.Location = new System.Drawing.Point(3, 56);
            label26.Name = "label26";
            label26.Size = new System.Drawing.Size(100, 23);
            label26.TabIndex = 2;
            label26.Text = "端口";
            // 
            // RawTcpPortTextBox
            // 
            RawTcpPortTextBox.Location = new System.Drawing.Point(3, 82);
            RawTcpPortTextBox.Name = "RawTcpPortTextBox";
            RawTcpPortTextBox.Size = new System.Drawing.Size(100, 27);
            RawTcpPortTextBox.TabIndex = 3;
            RawTcpPortTextBox.Text = "9000";
            // 
            // label38
            // 
            label38.Location = new System.Drawing.Point(3, 112);
            label38.Name = "label38";
            label38.Size = new System.Drawing.Size(100, 23);
            label38.TabIndex = 4;
            label38.Text = "分帧模式";
            // 
            // RawTcpFramingComboBox
            // 
            RawTcpFramingComboBox.Items.AddRange(new object[] { "原始字节流", "固定长度", "分隔符", "2字节长度头", "4字节长度头" });
            RawTcpFramingComboBox.Location = new System.Drawing.Point(3, 138);
            RawTcpFramingComboBox.Name = "RawTcpFramingComboBox";
            RawTcpFramingComboBox.Size = new System.Drawing.Size(121, 28);
            RawTcpFramingComboBox.TabIndex = 5;
            RawTcpFramingComboBox.Text = "原始字节流";
            // 
            // label39
            // 
            label39.Location = new System.Drawing.Point(3, 169);
            label39.Name = "label39";
            label39.Size = new System.Drawing.Size(100, 23);
            label39.TabIndex = 6;
            label39.Text = "分隔符(支持 \\r \\n \\0)";
            // 
            // RawTcpDelimiterTextBox
            // 
            RawTcpDelimiterTextBox.Location = new System.Drawing.Point(3, 195);
            RawTcpDelimiterTextBox.Name = "RawTcpDelimiterTextBox";
            RawTcpDelimiterTextBox.Size = new System.Drawing.Size(100, 27);
            RawTcpDelimiterTextBox.TabIndex = 7;
            RawTcpDelimiterTextBox.Text = "\\r\\n";
            // 
            // label40
            // 
            label40.Location = new System.Drawing.Point(3, 225);
            label40.Name = "label40";
            label40.Size = new System.Drawing.Size(100, 23);
            label40.TabIndex = 8;
            label40.Text = "固定帧长";
            // 
            // RawTcpFrameLengthTextBox
            // 
            RawTcpFrameLengthTextBox.Location = new System.Drawing.Point(3, 251);
            RawTcpFrameLengthTextBox.Name = "RawTcpFrameLengthTextBox";
            RawTcpFrameLengthTextBox.Size = new System.Drawing.Size(100, 27);
            RawTcpFrameLengthTextBox.TabIndex = 9;
            RawTcpFrameLengthTextBox.Text = "8";
            // 
            // label41
            // 
            label41.Location = new System.Drawing.Point(3, 281);
            label41.Name = "label41";
            label41.Size = new System.Drawing.Size(100, 23);
            label41.TabIndex = 10;
            label41.Text = "最大帧长";
            // 
            // RawTcpMaximumFrameLengthTextBox
            // 
            RawTcpMaximumFrameLengthTextBox.Location = new System.Drawing.Point(3, 307);
            RawTcpMaximumFrameLengthTextBox.Name = "RawTcpMaximumFrameLengthTextBox";
            RawTcpMaximumFrameLengthTextBox.Size = new System.Drawing.Size(100, 27);
            RawTcpMaximumFrameLengthTextBox.TabIndex = 11;
            RawTcpMaximumFrameLengthTextBox.Text = "1048576";
            // 
            // label27
            // 
            label27.Location = new System.Drawing.Point(3, 337);
            label27.Name = "label27";
            label27.Size = new System.Drawing.Size(100, 23);
            label27.TabIndex = 12;
            label27.Text = "发送文本";
            // 
            // RawTcpPayloadTextBox
            // 
            RawTcpPayloadTextBox.Location = new System.Drawing.Point(3, 363);
            RawTcpPayloadTextBox.Name = "RawTcpPayloadTextBox";
            RawTcpPayloadTextBox.Size = new System.Drawing.Size(100, 27);
            RawTcpPayloadTextBox.TabIndex = 13;
            RawTcpPayloadTextBox.Text = "hello";
            // 
            // RawTcpConnectButton
            // 
            RawTcpConnectButton.Location = new System.Drawing.Point(3, 396);
            RawTcpConnectButton.Name = "RawTcpConnectButton";
            RawTcpConnectButton.Size = new System.Drawing.Size(75, 23);
            RawTcpConnectButton.TabIndex = 14;
            RawTcpConnectButton.Text = "连接";
            RawTcpConnectButton.Click += RawTcpConnectButton_Click;
            // 
            // RawTcpSendButton
            // 
            RawTcpSendButton.Location = new System.Drawing.Point(3, 425);
            RawTcpSendButton.Name = "RawTcpSendButton";
            RawTcpSendButton.Size = new System.Drawing.Size(75, 23);
            RawTcpSendButton.TabIndex = 15;
            RawTcpSendButton.Text = "发送并接收";
            RawTcpSendButton.Click += RawTcpSendButton_Click;
            // 
            // RawTcpDisconnectButton
            // 
            RawTcpDisconnectButton.Location = new System.Drawing.Point(3, 454);
            RawTcpDisconnectButton.Name = "RawTcpDisconnectButton";
            RawTcpDisconnectButton.Size = new System.Drawing.Size(75, 23);
            RawTcpDisconnectButton.TabIndex = 16;
            RawTcpDisconnectButton.Text = "断开";
            RawTcpDisconnectButton.Click += RawTcpDisconnectButton_Click;
            // 
            // RawTcpOutputTextBox
            // 
            RawTcpOutputTextBox.Location = new System.Drawing.Point(3, 483);
            RawTcpOutputTextBox.Multiline = true;
            RawTcpOutputTextBox.Name = "RawTcpOutputTextBox";
            RawTcpOutputTextBox.Size = new System.Drawing.Size(850, 220);
            RawTcpOutputTextBox.TabIndex = 17;
            // 
            // MesHttpTabPage
            // 
            MesHttpTabPage.Controls.Add(MesHttpPanel);
            MesHttpTabPage.Location = new System.Drawing.Point(4, 29);
            MesHttpTabPage.Name = "MesHttpTabPage";
            MesHttpTabPage.Size = new System.Drawing.Size(976, 648);
            MesHttpTabPage.TabIndex = 5;
            MesHttpTabPage.Text = "MES HTTP JSON";
            // 
            // MesHttpPanel
            // 
            MesHttpPanel.AutoScroll = true;
            MesHttpPanel.Controls.Add(label34);
            MesHttpPanel.Controls.Add(MesHttpUrlTextBox);
            MesHttpPanel.Controls.Add(label35);
            MesHttpPanel.Controls.Add(MesHttpEndpointTextBox);
            MesHttpPanel.Controls.Add(label36);
            MesHttpPanel.Controls.Add(MesHttpJsonTextBox);
            MesHttpPanel.Controls.Add(MesHttpSendButton);
            MesHttpPanel.Controls.Add(MesHttpOutputTextBox);
            MesHttpPanel.Dock = System.Windows.Forms.DockStyle.Fill;
            MesHttpPanel.FlowDirection = System.Windows.Forms.FlowDirection.TopDown;
            MesHttpPanel.Location = new System.Drawing.Point(0, 0);
            MesHttpPanel.Name = "MesHttpPanel";
            MesHttpPanel.Size = new System.Drawing.Size(976, 648);
            MesHttpPanel.TabIndex = 0;
            MesHttpPanel.WrapContents = false;
            // 
            // label34
            // 
            label34.Location = new System.Drawing.Point(3, 0);
            label34.Name = "label34";
            label34.Size = new System.Drawing.Size(100, 23);
            label34.TabIndex = 0;
            label34.Text = "API 地址";
            // 
            // MesHttpUrlTextBox
            // 
            MesHttpUrlTextBox.Location = new System.Drawing.Point(3, 26);
            MesHttpUrlTextBox.Name = "MesHttpUrlTextBox";
            MesHttpUrlTextBox.Size = new System.Drawing.Size(100, 27);
            MesHttpUrlTextBox.TabIndex = 1;
            MesHttpUrlTextBox.Text = "http://127.0.0.1:8080/api";
            // 
            // label35
            // 
            label35.Location = new System.Drawing.Point(3, 56);
            label35.Name = "label35";
            label35.Size = new System.Drawing.Size(100, 23);
            label35.TabIndex = 2;
            label35.Text = "相对端点";
            // 
            // MesHttpEndpointTextBox
            // 
            MesHttpEndpointTextBox.Location = new System.Drawing.Point(3, 82);
            MesHttpEndpointTextBox.Name = "MesHttpEndpointTextBox";
            MesHttpEndpointTextBox.Size = new System.Drawing.Size(100, 27);
            MesHttpEndpointTextBox.TabIndex = 3;
            MesHttpEndpointTextBox.Text = "/upload";
            // 
            // label36
            // 
            label36.Location = new System.Drawing.Point(3, 112);
            label36.Name = "label36";
            label36.Size = new System.Drawing.Size(100, 23);
            label36.TabIndex = 4;
            label36.Text = "JSON 正文";
            // 
            // MesHttpJsonTextBox
            // 
            MesHttpJsonTextBox.Location = new System.Drawing.Point(3, 138);
            MesHttpJsonTextBox.Multiline = true;
            MesHttpJsonTextBox.Name = "MesHttpJsonTextBox";
            MesHttpJsonTextBox.Size = new System.Drawing.Size(850, 150);
            MesHttpJsonTextBox.TabIndex = 5;
            MesHttpJsonTextBox.Text = "{ \"sample\": \"value\" }";
            // 
            // MesHttpSendButton
            // 
            MesHttpSendButton.Location = new System.Drawing.Point(3, 294);
            MesHttpSendButton.Name = "MesHttpSendButton";
            MesHttpSendButton.Size = new System.Drawing.Size(75, 23);
            MesHttpSendButton.TabIndex = 6;
            MesHttpSendButton.Text = "发送 JSON";
            MesHttpSendButton.Click += MesHttpSendButton_Click;
            // 
            // MesHttpOutputTextBox
            // 
            MesHttpOutputTextBox.Location = new System.Drawing.Point(3, 323);
            MesHttpOutputTextBox.Multiline = true;
            MesHttpOutputTextBox.Name = "MesHttpOutputTextBox";
            MesHttpOutputTextBox.Size = new System.Drawing.Size(850, 220);
            MesHttpOutputTextBox.TabIndex = 7;
            // 
            // label37
            // 
            label37.Location = new System.Drawing.Point(0, 0);
            label37.Name = "label37";
            label37.Size = new System.Drawing.Size(100, 23);
            label37.TabIndex = 0;
            // 
            // MainForm
            // 
            ClientSize = new System.Drawing.Size(984, 681);
            Controls.Add(ProtocolTabControl);
            Name = "MainForm";
            StartPosition = System.Windows.Forms.FormStartPosition.CenterScreen;
            Text = "InduLink 协议最小系统";
            ProtocolTabControl.ResumeLayout(false);
            ModbusTcpTabPage.ResumeLayout(false);
            ModbusTcpPanel.ResumeLayout(false);
            ModbusTcpPanel.PerformLayout();
            ModbusRtuTabPage.ResumeLayout(false);
            ModbusRtuPanel.ResumeLayout(false);
            ModbusRtuPanel.PerformLayout();
            S7TabPage.ResumeLayout(false);
            S7Panel.ResumeLayout(false);
            S7Panel.PerformLayout();
            McTabPage.ResumeLayout(false);
            McPanel.ResumeLayout(false);
            McPanel.PerformLayout();
            RawTcpTabPage.ResumeLayout(false);
            RawTcpPanel.ResumeLayout(false);
            RawTcpPanel.PerformLayout();
            MesHttpTabPage.ResumeLayout(false);
            MesHttpPanel.ResumeLayout(false);
            MesHttpPanel.PerformLayout();
            ResumeLayout(false);
        }
    }
}
