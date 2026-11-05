namespace PowerWatch;

/// <summary>
/// 日志清理策略。
/// run_*.csv 每秒 10 行，约 55MB/天，这是占空间的主力，必须按时清。
/// event_*.csv 每个只有 100 行左右（约 8KB），是证据，留得久一些。
/// </summary>
internal static class LogHousekeeper
{
    public const int RunLogKeepDays = 3;
    public const int EventKeepDays = 30;
    public const long TotalCapBytes = 250L * 1024 * 1024;

    public sealed class Result
    {
        public int Deleted;
        public long FreedBytes;
        public long TotalBytes;
        public int FileCount;
    }

    public static Result Clean(string dir, string keepPath)
    {
        var r = new Result();
        if (!Directory.Exists(dir)) return r;

        DateTime now = DateTime.Now;

        try
        {
            // 1) 按期删除
            foreach (var f in Directory.GetFiles(dir, "*.csv"))
            {
                if (string.Equals(f, keepPath, StringComparison.OrdinalIgnoreCase)) continue;

                string name = Path.GetFileName(f);
                int keepDays;
                if (name.StartsWith("run_", StringComparison.OrdinalIgnoreCase)) keepDays = RunLogKeepDays;
                else if (name.StartsWith("event_", StringComparison.OrdinalIgnoreCase)) keepDays = EventKeepDays;
                else if (name.StartsWith("recovered_", StringComparison.OrdinalIgnoreCase)) keepDays = EventKeepDays;
                else continue;   // events.csv 这类索引文件永久保留，它很小

                try
                {
                    var fi = new FileInfo(f);
                    if ((now - fi.LastWriteTime).TotalDays > keepDays)
                    {
                        long len = fi.Length;
                        fi.Delete();
                        r.Deleted++;
                        r.FreedBytes += len;
                    }
                }
                catch { }
            }

            // 2) 总量封顶：还超就按时间从旧到新继续删 run 日志
            long total = TotalSize(dir);
            if (total > TotalCapBytes)
            {
                var runs = new DirectoryInfo(dir)
                    .GetFiles("run_*.csv")
                    .Where(f => !string.Equals(f.FullName, keepPath, StringComparison.OrdinalIgnoreCase))
                    .OrderBy(f => f.LastWriteTime)
                    .ToList();

                foreach (var fi in runs)
                {
                    if (total <= TotalCapBytes) break;
                    try
                    {
                        long len = fi.Length;
                        fi.Delete();
                        total -= len;
                        r.Deleted++;
                        r.FreedBytes += len;
                    }
                    catch { }
                }
            }

            var stat = Measure(dir);
            r.TotalBytes = stat.bytes;
            r.FileCount = stat.count;
        }
        catch { }

        return r;
    }

    private static long TotalSize(string dir)
    {
        try
        {
            long t = 0;
            foreach (var f in Directory.GetFiles(dir, "*.csv"))
                t += new FileInfo(f).Length;
            return t;
        }
        catch { return 0; }
    }

    public static (long bytes, int count) Measure(string dir)
    {
        try
        {
            long t = 0;
            var files = Directory.GetFiles(dir, "*.csv");
            foreach (var f in files) t += new FileInfo(f).Length;
            return (t, files.Length);
        }
        catch { return (0, 0); }
    }

    public static string Human(long bytes)
    {
        if (bytes < 1024) return bytes + " B";
        if (bytes < 1024 * 1024) return (bytes / 1024.0).ToString("F0") + " KB";
        if (bytes < 1024L * 1024 * 1024) return (bytes / 1024.0 / 1024.0).ToString("F1") + " MB";
        return (bytes / 1024.0 / 1024.0 / 1024.0).ToString("F2") + " GB";
    }
}
