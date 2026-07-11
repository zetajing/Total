using System;
using System.Windows.Forms;

namespace IndustrialCommMinimal.WinForms
{
    /// <summary>
    /// WinForms 最小协议验证程序的进程入口。
    /// 该类型只负责初始化 WinForms 运行环境并显示主窗体，不承载任何协议业务逻辑。
    /// </summary>
    internal static class Program
    {
        /// <summary>
        /// 以单线程单元（STA）模式启动桌面程序。
        /// WinForms、剪贴板和部分 COM 控件要求 UI 主线程运行在 STA 模式。
        /// </summary>
        [STAThread]
        private static void Main()
        {
            // 使用操作系统当前主题绘制按钮、页签等标准控件。
            Application.EnableVisualStyles();

            // 保持 .NET Framework WinForms 的默认文本渲染兼容行为。
            Application.SetCompatibleTextRenderingDefault(false);

            // 创建消息循环；MainForm 关闭后消息循环结束，进程随之退出。
            Application.Run(new MainForm());
        }
    }
}
