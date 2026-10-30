using System;
using System.IO;
using System.Text;

namespace S7TriggerRecorder;

// 只在成功保存后锁住本次触发；只有再次读到 FALSE 才允许下次记录。
internal sealed class TriggerState
{
    private bool recorded;

    public bool ShouldRecord(bool value)
    {
        if (!value) recorded = false;
        return value && !recorded;
    }

    public void MarkRecorded() => recorded = true;
}

internal static class RecordStore
{
    // 所有字段使用 CSV 引号转义，保留逗号、双引号、换行和空字符串。
    internal static string ToCsvLine(DateTime time, string value) =>
        $"\"{time:yyyy-MM-dd HH:mm:ss.fff}\",\"{value.Replace("\"", "\"\"")}\"";

    public static void Append(string folder, DateTime time, string value)
    {
        Directory.CreateDirectory(folder);
        string path = Path.Combine(folder, $"记录_{time:yyyy-MM-dd}.csv");
        bool writeHeader = !File.Exists(path) || new FileInfo(path).Length == 0;
        using var writer = new StreamWriter(path, append: true, new UTF8Encoding(true));
        if (writeHeader) writer.WriteLine("时间,字符串");
        writer.WriteLine(ToCsvLine(time, value));
    }
}
