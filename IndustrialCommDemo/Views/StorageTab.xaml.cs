using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using LogHelper;

namespace IndustrialCommDemo.Views
{
    public partial class StorageTab : UserControl
    {
        private DemoAppContext _ctx;

        public StorageTab()
        {
            InitializeComponent();
        }

        public void Initialize(DemoAppContext ctx)
        {
            _ctx = ctx ?? throw new ArgumentNullException(nameof(ctx));
            DataDirectoryTextBox.Text = StoragePathProvider.DataRoot;
            DataDirectoryHintTextBlock.Text = "当前目录：" + StoragePathProvider.DataRoot;
        }

        private void ApplyDataDirectoryButton_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                _ctx.UiStateStore.Save(_ctx.UiState);
                StoragePathProvider.SetDataRoot(DataDirectoryTextBox.Text);
                var newStore = new Services.UiStateStore();
                newStore.Save(_ctx.UiState);
                DataDirectoryTextBox.Text = StoragePathProvider.DataRoot;
                DataDirectoryHintTextBlock.Text = "已应用：" + StoragePathProvider.DataRoot + "（后续日志、状态和缓存将写入此处）";
                _ctx.DemoLogger.Info("本地数据目录已切换到 " + StoragePathProvider.DataRoot);
            }
            catch (Exception ex)
            {
                DataDirectoryHintTextBlock.Text = ex.Message;
                DataDirectoryHintTextBlock.Foreground = Brushes.OrangeRed;
                _ctx.HandleError("数据目录设置失败。", ex, true);
            }
        }
    }
}
