using System;
using System.IO;
using System.Linq;
using System.Reflection;

namespace IndustrialCommSdk.Storage
{
    /// <summary>统一管理应用产生的日志、状态和缓存目录，并禁止写入 C 盘。</summary>
    public static class StoragePathProvider
    {
        private const string ConfigFileName = "data-directory.txt";
        private static readonly object SyncRoot = new object();
        private static string _dataRoot;

        public static string DataRoot
        {
            get { lock (SyncRoot) return _dataRoot ?? (_dataRoot = LoadOrCreateDefault()); }
        }

        public static string LogsRoot => Path.Combine(DataRoot, "Logs");
        public static string StateRoot => Path.Combine(DataRoot, "State");
        public static string CacheRoot => Path.Combine(DataRoot, "Cache");

        public static void SetDataRoot(string path)
        {
            var normalized = Validate(path);
            Directory.CreateDirectory(normalized);
            Directory.CreateDirectory(Path.Combine(normalized, "Logs", "Demo"));
            Directory.CreateDirectory(Path.Combine(normalized, "Logs", "SDK"));
            Directory.CreateDirectory(Path.Combine(normalized, "State"));
            Directory.CreateDirectory(Path.Combine(normalized, "Cache"));
            File.WriteAllText(GetConfigPath(normalized), normalized);
            lock (SyncRoot) _dataRoot = normalized;
        }

        private static string LoadOrCreateDefault()
        {
            var appDirectory = GetApplicationDirectory();
            var candidate = Path.Combine(appDirectory, "Data");
            if (IsDriveC(candidate))
            {
                var drive = DriveInfo.GetDrives().FirstOrDefault(item => item.IsReady && item.DriveType == DriveType.Fixed && !string.Equals(item.Name.Substring(0, 1), "C", StringComparison.OrdinalIgnoreCase));
                if (drive == null) throw new InvalidOperationException("软件位于 C 盘且没有可用的非 C 固定磁盘，请先配置数据目录。");
                candidate = Path.Combine(drive.RootDirectory.FullName, "IndustrialCommDemoData");
            }
            var configPath = GetConfigPath(candidate);
            if (File.Exists(configPath))
            {
                var configured = File.ReadAllText(configPath).Trim();
                if (!string.IsNullOrWhiteSpace(configured)) return Validate(configured);
            }

            SetDataRoot(candidate);
            return candidate;
        }

        private static string Validate(string path)
        {
            if (string.IsNullOrWhiteSpace(path)) throw new ArgumentException("数据目录不能为空。", nameof(path));
            var fullPath = Path.GetFullPath(Environment.ExpandEnvironmentVariables(path.Trim()));
            if (IsDriveC(fullPath)) throw new InvalidOperationException("禁止将日志、缓存或状态文件保存到 C 盘。");
            return fullPath.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        }

        private static bool IsDriveC(string path)
        {
            return string.Equals(Path.GetPathRoot(Path.GetFullPath(path)), "C:\\", StringComparison.OrdinalIgnoreCase);
        }

        private static string GetConfigPath(string dataRoot) { return Path.Combine(dataRoot, ConfigFileName); }
        private static string GetApplicationDirectory()
        {
            var assembly = Assembly.GetEntryAssembly() ?? Assembly.GetExecutingAssembly();
            return Path.GetDirectoryName(assembly.Location) ?? AppDomain.CurrentDomain.BaseDirectory;
        }
    }
}
