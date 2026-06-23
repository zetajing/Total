using System.Configuration;
using System.Data;
using System.Windows;
using LogHelper;

namespace IndustrialCommDemo;

/// <summary>
/// App.xaml 的交互逻辑。
/// 表示 WPF 应用程序的入口点，负责在应用程序退出时执行日志系统的清理工作。
/// </summary>
public partial class App : Application
{
    /// <summary>
    /// 重写 <see cref="Application.OnExit"/> 方法，在应用程序退出时执行清理操作。
    /// </summary>
    /// <param name="e">包含退出事件数据的 <see cref="ExitEventArgs"/> 实例。</param>
    protected override void OnExit(ExitEventArgs e)
    {
        // 关闭日志显示辅助工具，释放相关资源
        LogDisplayHelper.Shutdown();
        base.OnExit(e);
    }
}
