using System;
using System.IO;
using System.Text;
using NUnit.Framework;
using S7TriggerRecorder;

namespace InduLink.Tests;

[TestFixture]
public sealed class S7TriggerRecorderTests
{
    [Test]
    public void Trigger_RecordsStartupTrueOnceAndRearmsOnlyOnFalse()
    {
        var state = new TriggerState();
        Assert.IsTrue(state.ShouldRecord(true));
        state.MarkRecorded();
        Assert.IsFalse(state.ShouldRecord(true));
        Assert.IsFalse(state.ShouldRecord(true));
        Assert.IsFalse(state.ShouldRecord(false));
        Assert.IsTrue(state.ShouldRecord(true));
    }

    [Test]
    public void Trigger_FailedReadOrSaveDoesNotConsumeTrigger()
    {
        var state = new TriggerState();
        Assert.IsTrue(state.ShouldRecord(true));
        // 没有调用 MarkRecorded，模拟读字符串失败；重试仍可记录。
        Assert.IsTrue(state.ShouldRecord(true));
        state.MarkRecorded();
        // 模拟通信中断期间没有新样本，恢复后 TRUE 仍然不能重复记录。
        Assert.IsFalse(state.ShouldRecord(true));
    }

    [Test]
    public void Cache_UsesPreTriggerValueWhenPlcClearsString()
    {
        var cache = new TriggerValueCache();
        var time = new DateTime(2026, 10, 30, 9, 0, 0);
        cache.Observe(false, "previous", time.AddSeconds(-1));
        cache.Observe(false, "G19-MB-LFRTAR0513", time);
        cache.Observe(false, "", time.AddMilliseconds(50));
        var data = cache.Select("", time.AddMilliseconds(100));
        Assert.AreEqual("G19-MB-LFRTAR0513", data.Value);
        Assert.AreEqual("触发前缓存", data.Source);
        Assert.AreEqual(time, data.SampleTime);
        Assert.AreEqual("current", cache.Select("current", time).Value);
        cache.Clear();
        Assert.AreEqual("", cache.Select("", time).Value);
        Assert.IsNull(cache.Select("", time).SampleTime);
        cache.Observe(true, "must-not-cache", time);
        Assert.AreEqual("", cache.Select("", time).Value);
    }

    [Test]
    public void Csv_PreservesSpecialCharactersEmptyValueAndDailyHeader()
    {
        string folder = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        try
        {
            var time = new DateTime(2026, 10, 30, 8, 9, 10, 123);
            RecordStore.Append(folder, time, new RecordedValue("A,\"B\"\r\n中文", "触发前缓存", time.AddSeconds(-1)));
            RecordStore.Append(folder, time.AddSeconds(1), new RecordedValue("", "空值（无触发前缓存）", null));
            RecordStore.Append(folder, time.AddDays(1), new RecordedValue("next", "触发时读取", time.AddDays(1)));
            string path = Path.Combine(folder, "记录_v2_2026-10-30.csv");
            string content = File.ReadAllText(path, Encoding.UTF8);
            Assert.AreEqual("触发时间,字符串,取值来源,字符串采样时间" + Environment.NewLine +
                "\"2026-10-30 08:09:10.123\",\"A,\"\"B\"\"\r\n中文\",\"触发前缓存\",\"2026-10-30 08:09:09.123\"" + Environment.NewLine +
                "\"2026-10-30 08:09:11.123\",\"\",\"空值（无触发前缓存）\",\"\"" + Environment.NewLine, content);
            Assert.AreEqual(2, Directory.GetFiles(folder).Length);
            Assert.AreEqual(0xEF, File.ReadAllBytes(path)[0]);
        }
        finally
        {
            // 只删除本测试创建的临时文件，避免递归删除目录。
            if (Directory.Exists(folder))
            {
                foreach (string file in Directory.GetFiles(folder)) File.Delete(file);
                Directory.Delete(folder);
            }
        }
    }
}
