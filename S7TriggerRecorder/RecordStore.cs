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
    internal static string ToCsvLine(DateTime time, RecordedValue data) =>
        $"\"{time:yyyy-MM-dd HH:mm:ss.fff}\",\"{data.Value.Replace("\"", "\"\"")}\",\"{data.Source}\",\"{data.SampleTime:yyyy-MM-dd HH:mm:ss.fff}\"";

    public static void Append(string folder, DateTime time, RecordedValue data)
    {
        Directory.CreateDirectory(folder);
        // 使用新文件名，保留旧版两列 CSV，避免在同一个文件内混用表头。
        string path = Path.Combine(folder, $"记录_v2_{time:yyyy-MM-dd}.csv");
        bool writeHeader = !File.Exists(path) || new FileInfo(path).Length == 0;
        using var writer = new StreamWriter(path, append: true, new UTF8Encoding(true));
        if (writeHeader) writer.WriteLine("触发时间,字符串,取值来源,字符串采样时间");
        writer.WriteLine(ToCsvLine(time, data));
    }
}

internal sealed record RecordedValue(string Value, string Source, DateTime? SampleTime);

// PLC 可能在置位触发时清空字符串长度，因此缓存触发前最近的有效内容。
internal sealed class TriggerValueCache
{
    private RecordedValue cached;

    public void Observe(bool active, string value, DateTime time)
    {
        if (!active && value.Length > 0)
            cached = new RecordedValue(value, "触发前缓存", time);
    }

    public RecordedValue Select(string value, DateTime time)
    {
        if (value.Length > 0) return new RecordedValue(value, "触发时读取", time);
        return cached ?? new RecordedValue("", "空值（无触发前缓存）", null);
    }

    // 成功记录后及断线时清除，下一次事件必须有新的触发前采样。
    public void Clear() => cached = null;
}
