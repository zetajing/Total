namespace S7time
{
    partial class Form1
    {
        /// <summary>
        /// 必需的设计器变量。
        /// </summary>
        private System.ComponentModel.IContainer components = null;

        /// <summary>
        /// 清理所有正在使用的资源。
        /// </summary>
        /// <param name="disposing">如果应释放托管资源，为 true；否则为 false。</param>
        protected override void Dispose(bool disposing)
        {
            if (disposing && (components != null))
            {
                components.Dispose();
            }
            base.Dispose(disposing);
        }

        #region Windows 窗体设计器生成的代码

        /// <summary>
        /// 设计器支持所需的方法 - 不要修改
        /// 使用代码编辑器修改此方法的内容。
        /// </summary>
        private void InitializeComponent()
        {
            this.connect = new System.Windows.Forms.Button();
            this.ip = new System.Windows.Forms.TextBox();
            this.logview = new System.Windows.Forms.TextBox();
            this.read_address = new System.Windows.Forms.TextBox();
            this.write_address = new System.Windows.Forms.TextBox();
            this.read = new System.Windows.Forms.Button();
            this.type = new System.Windows.Forms.ComboBox();
            this.label1 = new System.Windows.Forms.Label();
            this.SuspendLayout();
            // 
            // connect
            // 
            this.connect.AutoSize = true;
            this.connect.Location = new System.Drawing.Point(320, 35);
            this.connect.Name = "connect";
            this.connect.Size = new System.Drawing.Size(81, 28);
            this.connect.TabIndex = 0;
            this.connect.Text = "连接";
            this.connect.UseVisualStyleBackColor = true;
            this.connect.Click += new System.EventHandler(this.connect_Click);
            // 
            // ip
            // 
            this.ip.Location = new System.Drawing.Point(62, 35);
            this.ip.Name = "ip";
            this.ip.Size = new System.Drawing.Size(230, 28);
            this.ip.TabIndex = 1;
            this.ip.Text = "192.168.0.40";
            // 
            // logview
            // 
            this.logview.Location = new System.Drawing.Point(48, 138);
            this.logview.Multiline = true;
            this.logview.Name = "logview";
            this.logview.Size = new System.Drawing.Size(384, 272);
            this.logview.TabIndex = 2;
            // 
            // read_address
            // 
            this.read_address.Location = new System.Drawing.Point(613, 60);
            this.read_address.Name = "read_address";
            this.read_address.Size = new System.Drawing.Size(98, 28);
            this.read_address.TabIndex = 3;
            // 
            // write_address
            // 
            this.write_address.Location = new System.Drawing.Point(611, 238);
            this.write_address.Name = "write_address";
            this.write_address.Size = new System.Drawing.Size(100, 28);
            this.write_address.TabIndex = 4;
            // 
            // read
            // 
            this.read.Location = new System.Drawing.Point(718, 60);
            this.read.Name = "read";
            this.read.Size = new System.Drawing.Size(75, 27);
            this.read.TabIndex = 5;
            this.read.Text = "读取";
            this.read.UseVisualStyleBackColor = true;
            this.read.Click += new System.EventHandler(this.read_Click);
            // 
            // type
            // 
            this.type.FormattingEnabled = true;
            this.type.Items.AddRange(new object[] {
            "bool",
            "string",
            "int"});
            this.type.Location = new System.Drawing.Point(472, 62);
            this.type.Name = "type";
            this.type.Size = new System.Drawing.Size(121, 26);
            this.type.TabIndex = 6;
            // 
            // label1
            // 
            this.label1.AutoSize = true;
            this.label1.Location = new System.Drawing.Point(621, 203);
            this.label1.Name = "label1";
            this.label1.Size = new System.Drawing.Size(80, 18);
            this.label1.TabIndex = 7;
            this.label1.Text = "写入地址";
            // 
            // Form1
            // 
            this.AutoScaleDimensions = new System.Drawing.SizeF(9F, 18F);
            this.AutoScaleMode = System.Windows.Forms.AutoScaleMode.Font;
            this.ClientSize = new System.Drawing.Size(836, 442);
            this.Controls.Add(this.label1);
            this.Controls.Add(this.type);
            this.Controls.Add(this.read);
            this.Controls.Add(this.write_address);
            this.Controls.Add(this.read_address);
            this.Controls.Add(this.logview);
            this.Controls.Add(this.ip);
            this.Controls.Add(this.connect);
            this.Name = "Form1";
            this.Text = "Form1";
            this.ResumeLayout(false);
            this.PerformLayout();

        }

        #endregion

        private System.Windows.Forms.Button connect;
        private System.Windows.Forms.TextBox ip;
        private System.Windows.Forms.TextBox logview;
        private System.Windows.Forms.TextBox read_address;
        private System.Windows.Forms.TextBox write_address;
        private System.Windows.Forms.Button read;
        private System.Windows.Forms.ComboBox type;
        private System.Windows.Forms.Label label1;
    }
}

