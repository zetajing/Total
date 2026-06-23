using System.Configuration;
using System.Data;
using System.Windows;
using LogHelper;

namespace IndustrialCommDemo;

/// <summary>
/// Interaction logic for App.xaml
/// </summary>
public partial class App : Application
{
    protected override void OnExit(ExitEventArgs e)
    {
        LogDisplayHelper.Shutdown();
        base.OnExit(e);
    }
}
