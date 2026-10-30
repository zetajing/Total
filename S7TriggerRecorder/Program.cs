namespace S7TriggerRecorder;

internal static class Program
{
    // WinForms 入口，程序打开后由用户点击“开始记录”。
    [STAThread]
    private static void Main()
    {
        ApplicationConfiguration.Initialize();
        Application.Run(new MainForm());
    }
}
