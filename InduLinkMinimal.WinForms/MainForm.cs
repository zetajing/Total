using System;
using System.Windows.Forms;
using InduLink.Abstractions;
using InduLink.Transport;

namespace InduLinkMinimal.WinForms
{
    /// <summary>
    /// 工业通讯协议最小验证主窗体。
    /// 每个页签对应一种协议，用于快速确认 SDK、网络和现场设备参数。
    /// </summary>
    public partial class MainForm : Form
    {
        private IInduLinkClient _modbusTcpClient;
        private IInduLinkClient _modbusRtuClient;
        private IInduLinkClient _s7Client;
        private IInduLinkClient _mcClient;
        private TcpTransportClient _rawTcpClient;
        private FramedTcpClient _framedTcpClient;

        public MainForm()
        {
            InitializeComponent();
            LoadModbusProfiles();
        }

        protected override void OnFormClosed(FormClosedEventArgs e)
        {
            DisposeClient(_modbusTcpClient);
            DisposeClient(_modbusRtuClient);
            DisposeClient(_s7Client);
            DisposeClient(_mcClient);
            DisposeClient(_rawTcpClient);
            DisposeClient(_framedTcpClient);
            base.OnFormClosed(e);
        }

        private static void DisposeClient(IDisposable client)
        {
            client?.Dispose();
        }
    }
}
