using System.Windows.Controls;

namespace IndustrialCommDemo
{
    public partial class MainWindow
    {
        /// <summary>
        /// 按实际使用流程排列页面：先连接和读写设备，再进行联调，最后处理数据与系统配置。
        /// 页面仍保留独立名称，避免顺序变化影响事件绑定和状态保存。
        /// </summary>
        private void ArrangeMainTabs()
        {
            var tabs = new TabItem[]
            {
                ModbusTab,
                S7Tab,
                McTab,
                SocketTab,
                MesTab,
                DatabaseTab,
                NetworkTab,
                StorageTab,
            };

            ProtocolTabControl.Items.Clear();
            foreach (var tab in tabs)
            {
                ProtocolTabControl.Items.Add(tab);
            }
            ProtocolTabControl.SelectedItem = ModbusTab;
        }
    }
}
