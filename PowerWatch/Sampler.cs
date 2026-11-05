using System.Diagnostics;
using System.Globalization;
using System.Text;

namespace PowerWatch;

public struct PowerSample
{
    public long Ms;
    public DateTime Time;
    public int Cpu10;        // CPU% × 10
    public int Gpu10;        // GPU% × 10，-1 表示无数据
    public int BattMv;
    public int BattMa;       // 正=放电，负=充电
    public int BattMw;
    public int CapMwh;
    public int Percent;      // -1 未知
    public bool Ac;
    public bool Charging;
    public bool Discharging;
    public uint RawState;
    public int DtMs;
}

public sealed class PowerEvent
{
    public long Ms;
    public DateTime Time;
    public string Kind;      // AC_LOST / AC_BACK / CHG_DROP / VSAG
    public string Title;
    public string Detail;
    public string SnapshotPath;
}

/// <summary>
/// 100ms 采样循环。所有数据先落盘（8KB 缓冲 + 每秒 flush），
/// 整机掉电也能保住日志——这是排查真断电的前提。
/// </summary>
internal sealed class Sampler : IDisposable
{
    public const int SampleMs = 100;
    private const int RingSize = 400;          // 40 秒滚动窗口
    private const int PreSeconds = 5;
    private const int PostSeconds = 5;

    /// <summary>CSV 表头。全程日志和事件快照共用，避免两处写岔。</summary>
    private const string CsvHeader =
        "时间,毫秒,CPU占用%,GPU占用%,电池电压mV,电池电流mA,电池功率mW,剩余容量mWh,电量%,交流在线,充电中,放电中,电源状态";

    private readonly BatteryReader _battery = new();
    private readonly GpuMeter _gpu = new();
    private readonly object _gate = new();
    private readonly PowerSample[] _ring = new PowerSample[RingSize];
    private readonly List<PowerEvent> _pending = new();
    private readonly StringBuilder _sb = new(320);
    private readonly Queue<PowerEvent> _events = new();

    private Thread _thread;
    private volatile bool _running;
    private volatile bool _disposed;
    private int _writeIndex;
    private int _count;

    private StreamWriter _log;
    private FileStream _fs;
    private string _logPath;
    private long _lastFlushMs;
    private int _syncTick;

    private long _lastIdle, _lastKernel, _lastUser;
    private long _lastTick;
    private int _tickCounter;

    private int _hzAccumMs;
    private int _hzCount;

    private bool _prevAc;
    private bool _prevCharging;
    private bool _hasPrev;
    private long _lastStateChangeMs;

    private bool _battOk;
    private bool _gpuOk;

    public string LogDirectory { get; }
    public string CurrentLogPath => _logPath;
    public string RecoveryPath { get; private set; }
    public int EventCount { get; private set; }

    // ---- 供 UI 无锁读取 ----
    public volatile int UiCpu10;
    public volatile int UiGpu10 = -1;
    public volatile int UiBattMv;
    public volatile int UiBattMa;
    public volatile int UiBattMw;
    public volatile int UiPercent = -1;
    public volatile int UiAc;
    public volatile int UiCharging;
    public volatile int UiDischarging;
    public volatile int UiActualMs = SampleMs;
    public volatile int UiHz10 = 100;
    public volatile int UiSamples;
    public volatile int UiBatteryAvailable;
    public volatile int UiGpuAvailable;

    public Sampler()
    {
        string baseDir = AppContext.BaseDirectory;
        try
        {
            LogDirectory = Path.Combine(baseDir, "logs");
            Directory.CreateDirectory(LogDirectory);
            string probe = Path.Combine(LogDirectory, ".w");
            File.WriteAllText(probe, "1");
            File.Delete(probe);
        }
        catch
        {
            LogDirectory = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "PowerWatch", "logs");
            Directory.CreateDirectory(LogDirectory);
        }
    }

    public void Start()
    {
        RecoverPreviousSession();

        _battOk = _battery.Open();
        UiBatteryAvailable = _battOk ? 1 : 0;

        _gpuOk = _gpu.Init();
        UiGpuAvailable = _gpuOk ? 1 : 0;

        string name = "run_" + DateTime.Now.ToString("yyyyMMdd_HHmmss") + ".csv";
        _logPath = Path.Combine(LogDirectory, name);
        _fs = new FileStream(_logPath, FileMode.Create, FileAccess.Write,
            FileShare.ReadWrite | FileShare.Delete, 8192);
        _log = new StreamWriter(_fs, new UTF8Encoding(true)) { AutoFlush = false };

        _log.WriteLine("# PowerWatch v1  start=" + DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"));
        _log.WriteLine("# batt_ok=" + _battOk + " gpu_ok=" + _gpuOk);
        _log.WriteLine(CsvHeader);

        _running = true;
        _thread = new Thread(Loop)
        {
            IsBackground = true,
            Priority = ThreadPriority.AboveNormal,
            Name = "PowerWatchSampler"
        };
        _thread.Start();
    }

    public void Stop()
    {
        _running = false;
        try { _thread?.Join(1500); } catch { }
        try
        {
            if (_log != null)
            {
                _log.WriteLine("# END " + DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"));
                _log.Flush();
                _log.Dispose();
                _log = null;
            }
        }
        catch { }
    }

    private void Loop()
    {
        var sw = Stopwatch.StartNew();
        long next = SampleMs;

        while (_running)
        {
            long now = sw.ElapsedMilliseconds;
            int sleep = (int)(next - now);
            if (sleep > 0)
            {
                Thread.Sleep(sleep);
            }
            else if (sleep < -SampleMs)
            {
                next = now;
            }
            next += SampleMs;

            try { Tick(sw.ElapsedMilliseconds); } catch { }
        }
    }

    private void Tick(long ms)
    {
        var s = new PowerSample { Ms = ms, Time = DateTime.Now };
        s.DtMs = _lastTick == 0 ? SampleMs : (int)(ms - _lastTick);
        _lastTick = ms;

        // ---- CPU ----
        if (Native.GetSystemTimes(out long idle, out long kernel, out long user))
        {
            long di = idle - _lastIdle, dk = kernel - _lastKernel, du = user - _lastUser;
            long total = dk + du;
            if (_lastKernel != 0 && total > 0)
            {
                double pct = (total - di) * 100.0 / total;
                if (pct < 0) pct = 0; else if (pct > 100) pct = 100;
                s.Cpu10 = (int)Math.Round(pct * 10);
            }
            _lastIdle = idle; _lastKernel = kernel; _lastUser = user;
        }

        // ---- GPU：500ms 更新一次 ----
        if (_gpuOk && (_tickCounter % 5) == 0)
        {
            float g = _gpu.Sample();
            if (g >= 0) s.Gpu10 = (int)Math.Round(g * 10);
            else s.Gpu10 = UiGpu10;
        }
        else
        {
            s.Gpu10 = UiGpu10;
        }

        // ---- 电源状态 ----
        bool spsOk = Native.GetSystemPowerStatus(out var sps);
        if (spsOk && sps.BatteryLifePercent != 255)
            s.Percent = sps.BatteryLifePercent;

        if (_battOk && _battery.TryRead(out var bs))
        {
            s.BattMv = bs.VoltageMv;
            s.BattMa = bs.CurrentMa;
            s.BattMw = bs.RateMw;
            s.CapMwh = bs.CapacityMwh;
            s.Ac = bs.OnLine;
            s.Charging = bs.Charging;
            s.Discharging = bs.Discharging;
            s.RawState = bs.RawPowerState;
        }
        else
        {
            s.Ac = spsOk && sps.ACLineStatus == 1;
        }

        _tickCounter++;

        DetectEvents(s);

        lock (_gate)
        {
            _ring[_writeIndex] = s;
            _writeIndex = (_writeIndex + 1) % RingSize;
            if (_count < RingSize) _count++;
        }

        WriteLine(s, ms);
        UiSamples++;

        ProcessPending(ms);

        // ---- 发布给 UI ----
        UiCpu10 = s.Cpu10;
        if (s.Gpu10 >= 0) UiGpu10 = s.Gpu10;
        UiBattMv = s.BattMv;
        UiBattMa = s.BattMa;
        UiBattMw = s.BattMw;
        UiPercent = s.Percent;
        UiAc = s.Ac ? 1 : 0;
        UiCharging = s.Charging ? 1 : 0;
        UiDischarging = s.Discharging ? 1 : 0;
        UiActualMs = s.DtMs;

        _hzAccumMs += s.DtMs;
        _hzCount++;
        if (_hzAccumMs >= 1000 && _hzCount > 0)
        {
            UiHz10 = (int)(_hzCount * 10000.0 / _hzAccumMs);
            _hzAccumMs = 0;
            _hzCount = 0;
        }
    }

    private void DetectEvents(PowerSample s)
    {
        if (!_hasPrev)
        {
            _prevAc = s.Ac;
            _prevCharging = s.Charging;
            _hasPrev = true;
            return;
        }

        if (s.Ac != _prevAc)
        {
            if (!s.Ac)
                Raise(s, "AC_LOST", "交流供电丢失",
                    "电压 " + s.BattMv + "mV，电流 " + s.BattMa + "mA，PowerState=0x" + s.RawState.ToString("X4"));
            else
                Raise(s, "AC_BACK", "交流供电恢复",
                    "电压 " + s.BattMv + "mV，PowerState=0x" + s.RawState.ToString("X4"));

            _prevAc = s.Ac;
            _prevCharging = s.Charging;
            _lastStateChangeMs = s.Ms;
            return;
        }

        if (_prevCharging && !s.Charging && s.Discharging && s.Ac)
        {
            Raise(s, "CHG_DROP", "充电中断（交流仍在线）",
                "PowerState=0x" + s.RawState.ToString("X4") + "，电流 " + s.BattMa + "mA");
            _lastStateChangeMs = s.Ms;
        }
        _prevCharging = s.Charging;

        // 电压跌落：300ms 内掉超过 300mV。
        // 状态刚切换后的 4 秒内不判——适配器断开瞬间电池端带着"表面电荷"，
        // 几百毫伏的回落是正常的弛豫过程，不是故障。
        if (s.BattMv > 5000 && s.Ms - _lastStateChangeMs > 4000)
        {
            PowerSample old = default;
            lock (_gate)
            {
                if (_count >= 4)
                {
                    int idx = _writeIndex - 3;
                    if (idx < 0) idx += RingSize;
                    old = _ring[idx];
                }
            }
            if (old.BattMv > 5000 && old.BattMv - s.BattMv > 300)
            {
                Raise(s, "VSAG", "电压跌落",
                    (old.BattMv - s.BattMv) + "mV / 300ms（" + old.BattMv + " → " + s.BattMv + "）");
                _lastStateChangeMs = s.Ms;
            }
        }
    }

    private void Raise(PowerSample s, string kind, string title, string detail)
    {
        var ev = new PowerEvent
        {
            Ms = s.Ms,
            Time = s.Time,
            Kind = kind,
            Title = title,
            Detail = detail
        };

        EventCount++;
        lock (_pending) _pending.Add(ev);
        lock (_events) _events.Enqueue(ev);

        // 事件点立刻强制落盘，确保断电瞬间的记录一定在硬盘上
        try { _log?.Flush(); _fs?.Flush(true); } catch { }

        try
        {
            File.AppendAllText(
                Path.Combine(LogDirectory, "events.csv"),
                s.Time.ToString("yyyy-MM-dd HH:mm:ss.fff") + "," + kind + "," + title + "," + detail
                + Environment.NewLine,
                Encoding.UTF8);
        }
        catch { }
    }

    private void ProcessPending(long nowMs)
    {
        PowerEvent due = null;
        lock (_pending)
        {
            for (int i = 0; i < _pending.Count; i++)
            {
                if (nowMs - _pending[i].Ms >= PostSeconds * 1000L)
                {
                    due = _pending[i];
                    _pending.RemoveAt(i);
                    break;
                }
            }
        }
        if (due != null) WriteSnapshot(due);
    }

    private void WriteSnapshot(PowerEvent e)
    {
        var buf = new List<PowerSample>(_count);
        lock (_gate)
        {
            for (int i = 0; i < _count; i++)
            {
                int idx = (_writeIndex - _count + i + RingSize * 2) % RingSize;
                buf.Add(_ring[idx]);
            }
        }

        long from = e.Ms - PreSeconds * 1000L;
        long to = e.Ms + PostSeconds * 1000L;

        // 找离事件最近的那一行打标记
        int markIdx = -1;
        long best = long.MaxValue;
        for (int i = 0; i < buf.Count; i++)
        {
            long d = Math.Abs(buf[i].Ms - e.Ms);
            if (d < best) { best = d; markIdx = i; }
        }

        string file = Path.Combine(LogDirectory,
            "event_" + e.Time.ToString("yyyyMMdd_HHmmss_fff") + "_" + e.Kind + ".csv");

        try
        {
            using var w = new StreamWriter(file, false, new UTF8Encoding(true));
            w.WriteLine("# 事件: " + e.Title + "  @ " + e.Time.ToString("yyyy-MM-dd HH:mm:ss.fff"));
            w.WriteLine("# 详情: " + e.Detail);
            w.WriteLine("# 窗口: 前 " + PreSeconds + " 秒 / 后 " + PostSeconds + " 秒，采样间隔 " + SampleMs + "ms");
            w.WriteLine(CsvHeader);

            int written = 0;
            for (int i = 0; i < buf.Count; i++)
            {
                if (buf[i].Ms < from || buf[i].Ms > to) continue;
                w.WriteLine(Row(buf[i]) + (i == markIdx ? ",<== EVENT" : ""));
                written++;
            }
            e.SnapshotPath = file;
            if (written == 0) e.SnapshotPath = file + " (空窗口)";
        }
        catch { }
    }

    private string Row(PowerSample s)
    {
        _sb.Clear();
        _sb.Append(s.Time.ToString("HH:mm:ss.fff")).Append(',')
           .Append(s.Ms).Append(',')
           .Append((s.Cpu10 / 10.0).ToString("F1", CultureInfo.InvariantCulture)).Append(',')
           .Append(s.Gpu10 < 0 ? "" : (s.Gpu10 / 10.0).ToString("F1", CultureInfo.InvariantCulture)).Append(',')
           .Append(s.BattMv).Append(',')
           .Append(s.BattMa).Append(',')
           .Append(s.BattMw).Append(',')
           .Append(s.CapMwh).Append(',')
           .Append(s.Percent < 0 ? "" : s.Percent.ToString(CultureInfo.InvariantCulture)).Append(',')
           .Append(s.Ac ? '1' : '0').Append(',')
           .Append(s.Charging ? '1' : '0').Append(',')
           .Append(s.Discharging ? '1' : '0').Append(',')
           .Append("0x").Append(s.RawState.ToString("X4", CultureInfo.InvariantCulture));
        return _sb.ToString();
    }

    private void WriteLine(PowerSample s, long ms)
    {
        try
        {
            _log.WriteLine(Row(s));

            // 每 500ms 交给操作系统，每 2s 强制落盘一次。
            // 硬掉电时 OS 缓存会一起丢，只有落到盘上的才算数。
            if (ms - _lastFlushMs >= 500)
            {
                _lastFlushMs = ms;
                _log.Flush();
                if (++_syncTick >= 4)
                {
                    _syncTick = 0;
                    try { _fs.Flush(true); } catch { }
                }
            }
        }
        catch { }
    }

    /// <summary>复制最近 N 个采样的曲线数据，供绘图。</summary>
    public int CopyTrace(int max, float[] mv, byte[] ac, byte[] chg)
    {
        lock (_gate)
        {
            int n = Math.Min(Math.Min(_count, max), Math.Min(mv.Length, ac.Length));
            for (int i = 0; i < n; i++)
            {
                int idx = (_writeIndex - n + i + RingSize * 2) % RingSize;
                mv[i] = _ring[idx].BattMv;
                ac[i] = (byte)(_ring[idx].Ac ? 1 : 0);
                chg[i] = (byte)(_ring[idx].Charging ? 1 : 0);
            }
            return n;
        }
    }

    /// <summary>手动打一个标记点，5 秒后生成前后 5 秒的快照。</summary>
    public void CaptureNow()
    {
        PowerSample s;
        lock (_gate)
        {
            s = _count > 0 ? _ring[(_writeIndex - 1 + RingSize) % RingSize] : default;
        }
        if (s.Ms == 0)
            s = new PowerSample { Ms = _lastTick, Time = DateTime.Now, BattMv = UiBattMv };
        Raise(s, "MANUAL", "手动快照", "用户手动触发，记录此刻前后 5 秒");
    }

    public int DequeueEvents(PowerEvent[] dest)    {
        lock (_events)
        {
            int n = 0;
            while (_events.Count > 0 && n < dest.Length) dest[n++] = _events.Dequeue();
            return n;
        }
    }

    private void RecoverPreviousSession()
    {
        try
        {
            var files = Directory.GetFiles(LogDirectory, "run_*.csv");
            if (files.Length == 0) return;
            Array.Sort(files, StringComparer.OrdinalIgnoreCase);
            string last = files[^1];
            string tail = ReadTail(last, 64 * 1024);
            if (string.IsNullOrEmpty(tail)) return;
            if (tail.Contains("# END")) return;

            string dst = Path.Combine(LogDirectory,
                "recovered_" + DateTime.Now.ToString("yyyyMMdd_HHmmss") + ".csv");
            File.WriteAllText(dst,
                "# 上一次会话未正常结束（疑似整机掉电），以下是它的日志尾部\r\n" + tail,
                new UTF8Encoding(true));
            RecoveryPath = dst;
        }
        catch { }
    }

    private static string ReadTail(string path, int bytes)
    {
        using var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
        long len = fs.Length;
        long start = Math.Max(0, len - bytes);
        fs.Seek(start, SeekOrigin.Begin);
        var buf = new byte[len - start];
        int read = fs.Read(buf, 0, buf.Length);
        return Encoding.UTF8.GetString(buf, 0, read);
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        Stop();
        _battery.Dispose();
        _gpu.Dispose();
    }
}
