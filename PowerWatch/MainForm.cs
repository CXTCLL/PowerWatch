using System.Diagnostics;
using System.Drawing.Drawing2D;

namespace PowerWatch;

internal static class Theme
{
    public static readonly Color Bg = Color.FromArgb(0xF6, 0xF6, 0xF4);
    public static readonly Color Card = Color.White;
    public static readonly Color Line = Color.FromArgb(0xE4, 0xE3, 0xDE);
    public static readonly Color Ink = Color.FromArgb(0x1E, 0x1E, 0x1C);
    public static readonly Color Muted = Color.FromArgb(0x6E, 0x6E, 0x68);
    public static readonly Color Accent = Color.FromArgb(0x18, 0x5F, 0xA5);
    public static readonly Color OkInk = Color.FromArgb(0x2E, 0x6B, 0x33);
    public static readonly Color OkBg = Color.FromArgb(0xED, 0xF5, 0xED);
    public static readonly Color WarnInk = Color.FromArgb(0xB4, 0x53, 0x09);
    public static readonly Color WarnBg = Color.FromArgb(0xFB, 0xF2, 0xE4);
    public static readonly Color BadInk = Color.FromArgb(0xB3, 0x26, 0x1E);

    public const string Ui = "Microsoft YaHei UI";
    public const string Mono = "Consolas";
}

/// <summary>布局按 96 DPI 写，缩放交给 Dpi.S。</summary>
internal sealed class MetricCard : Panel
{
    private readonly Label _cap;
    private readonly Label _val;
    private readonly Label _unit;

    public MetricCard(string caption, string unit)
    {
        DoubleBuffered = true;
        BackColor = Theme.Card;
        Padding = Dpi.Pad(12, 6, 12, 6);

        _cap = new Label
        {
            Text = caption,
            Font = new Font(Theme.Ui, 8.5f),
            ForeColor = Theme.Muted,
            Dock = DockStyle.Top,
            Height = Dpi.S(20),
            AutoSize = false
        };
        _unit = new Label
        {
            Text = unit,
            Font = new Font(Theme.Ui, 8f),
            ForeColor = Theme.Muted,
            Dock = DockStyle.Bottom,
            Height = Dpi.S(16),
            AutoSize = false
        };
        _val = new Label
        {
            Text = "--",
            Font = new Font(Theme.Mono, 16.5f),
            ForeColor = Theme.Ink,
            Dock = DockStyle.Fill,
            TextAlign = ContentAlignment.MiddleLeft,
            AutoSize = false
        };

        Controls.Add(_val);
        Controls.Add(_unit);
        Controls.Add(_cap);
    }

    public void Set(string text, Color? color = null)
    {
        if (_val.Text != text) _val.Text = text;
        _val.ForeColor = color ?? Theme.Ink;
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
        var r = new Rectangle(0, 0, Width - 1, Height - 1);
        using var p = new Pen(Theme.Line, 1f);
        using var path = Round(r, Dpi.S(9));
        e.Graphics.DrawPath(p, path);
    }

    internal static GraphicsPath Round(Rectangle r, int radius)
    {
        var path = new GraphicsPath();
        int d = radius * 2;
        if (r.Width <= d || r.Height <= d) { path.AddRectangle(r); return path; }
        path.AddArc(r.X, r.Y, d, d, 180, 90);
        path.AddArc(r.Right - d, r.Y, d, d, 270, 90);
        path.AddArc(r.Right - d, r.Bottom - d, d, d, 0, 90);
        path.AddArc(r.X, r.Bottom - d, d, d, 90, 90);
        path.CloseFigure();
        return path;
    }
}

/// <summary>最近 60 秒电池电压走势，底部色带表示交流/电池状态。</summary>
internal sealed class TracePanel : Panel
{
    private const int MaxPoints = 600;
    private readonly float[] _mv = new float[MaxPoints];
    private readonly byte[] _ac = new byte[MaxPoints];
    private readonly byte[] _chg = new byte[MaxPoints];
    private int _n;

    public TracePanel()
    {
        DoubleBuffered = true;
        BackColor = Color.FromArgb(0xFA, 0xFA, 0xF8);
    }

    public void Pull(Sampler s)
    {
        _n = s.CopyTrace(MaxPoints, _mv, _ac, _chg);
        Invalidate();
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        g.SmoothingMode = SmoothingMode.AntiAlias;
        int w = Width, h = Height;
        using (var bg = new SolidBrush(BackColor)) g.FillRectangle(bg, 0, 0, w, h);

        int bandH = Dpi.S(6);
        int plotH = h - bandH - Dpi.S(4);
        if (w < 40 || plotH < 30) { DrawBorder(g, w, h); return; }

        int offset = MaxPoints - _n;
        float cell = (float)w / (MaxPoints - 1);

        if (_n > 1)
        {
            using var acBrush = new SolidBrush(Color.FromArgb(0xCF, 0xE3, 0xCF));
            using var dcBrush = new SolidBrush(Color.FromArgb(0xF0, 0xD8, 0xB8));
            for (int i = 0; i < _n - 1; i++)
                g.FillRectangle(_ac[i] == 1 ? acBrush : dcBrush,
                    (offset + i) * cell, h - bandH, cell + 1.2f, bandH);
        }

        float min = float.MaxValue, max = float.MinValue;
        int valid = 0;
        for (int i = 0; i < _n; i++)
        {
            if (_mv[i] < 100) continue;
            valid++;
            if (_mv[i] < min) min = _mv[i];
            if (_mv[i] > max) max = _mv[i];
        }

        if (valid >= 2)
        {
            float pad = Math.Max(90f, (max - min) * 0.3f);
            min -= pad; max += pad;

            using var grid = new Pen(Color.FromArgb(0xEC, 0xEB, 0xE6), 1f);
            using var txtBrush = new SolidBrush(Theme.Muted);
            using var f = new Font(Theme.Mono, 7.5f);

            for (int i = 0; i <= 2; i++)
            {
                float y = Dpi.S(4) + (plotH - Dpi.S(8)) * i / 2f;
                g.DrawLine(grid, 0, y, w, y);
                float v = max - (max - min) * i / 2f;
                g.DrawString(v.ToString("F0"), f, txtBrush, Dpi.S(3), y + 1);
            }

            var pts = new PointF[_n];
            float usable = plotH - Dpi.S(10);
            for (int i = 0; i < _n; i++)
            {
                float v = _mv[i] < 100 ? min : _mv[i];
                float y = Dpi.S(5) + usable - (v - min) / (max - min) * usable;
                pts[i] = new PointF((offset + i) * cell, y);
            }
            using var pen = new Pen(Theme.Accent, 1.6f) { LineJoin = LineJoin.Round };
            if (pts.Length > 1) g.DrawLines(pen, pts);
        }
        else
        {
            using var txtBrush = new SolidBrush(Theme.Muted);
            using var f = new Font(Theme.Ui, 8.5f);
            g.DrawString("等待电压数据…", f, txtBrush, Dpi.S(8), h / 2f - Dpi.S(10));
        }

        DrawBorder(g, w, h);
    }

    private static void DrawBorder(Graphics g, int w, int h)
    {
        using var p = new Pen(Theme.Line, 1f);
        g.DrawRectangle(p, 0, 0, w - 1, h - 1);
    }
}

internal sealed class MainForm : Form
{
    private readonly Sampler _sampler = new();
    private readonly System.Windows.Forms.Timer _ui = new() { Interval = 200 };
    private readonly System.Windows.Forms.Timer _cleanup = new() { Interval = 3600_000 };

    private TrayController _tray;
    private bool _exiting;
    private readonly bool _hiddenAtStart;
    private bool _inTray;          // 窗口是否收在托盘里，UI 刷新靠它判断

    private Panel _header;
    private Label _lblState, _lblHint, _lblCount;
    private MetricCard _cCpu, _cGpu, _cBv, _cBi, _cSv, _cSi, _cPct, _cHz;
    private TracePanel _trace;
    private ListView _list;
    private ToolStripStatusLabel _sLog, _sMem, _sInfo;
    private ToolTip _tips;

    private readonly PowerEvent[] _evtBuf = new PowerEvent[32];
    private int _memTick = 24;

    public MainForm(bool startHidden)
    {
        _hiddenAtStart = startHidden;

        // 布局由 Dpi.S 手动缩放，关掉框架自动缩放以免重复放大
        AutoScaleMode = AutoScaleMode.None;

        Text = "PowerWatch · 供电瞬断监测";
        ClientSize = Dpi.Sz(700, 720);
        MinimumSize = Dpi.Sz(640, 600);
        BackColor = Theme.Bg;
        Font = new Font(Theme.Ui, 9f);
        DoubleBuffered = true;
        StartPosition = FormStartPosition.CenterScreen;

        // 自启时不希望登录瞬间闪一下窗口，先设全透明，藏好之后再恢复
        if (startHidden) Opacity = 0;

        var root = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 5,
            BackColor = Theme.Bg
        };
        root.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, Dpi.S(88)));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, Dpi.S(190)));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, Dpi.S(152)));
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, Dpi.S(26)));

        root.Controls.Add(BuildHeader(), 0, 0);
        root.Controls.Add(BuildMetrics(), 0, 1);
        root.Controls.Add(BuildChart(), 0, 2);
        root.Controls.Add(BuildList(), 0, 3);
        root.Controls.Add(BuildStatus(), 0, 4);

        Controls.Add(root);
        _ui.Tick += OnTick;
        _cleanup.Tick += OnCleanup;
    }

    private Control BuildHeader()
    {
        _header = new Panel { Dock = DockStyle.Fill, BackColor = Theme.OkBg, Margin = Padding.Empty };

        _lblState = new Label
        {
            Text = "正在启动…",
            Font = new Font(Theme.Ui, 18f, FontStyle.Bold),
            ForeColor = Theme.OkInk,
            Location = Dpi.P(20, 8),
            Size = Dpi.Sz(400, 44),
            AutoSize = false,
            TextAlign = ContentAlignment.MiddleLeft
        };
        _lblHint = new Label
        {
            Text = "采样中",
            Font = new Font(Theme.Ui, 9f),
            ForeColor = Theme.Muted,
            Location = Dpi.P(22, 56),
            Size = Dpi.Sz(460, 20),
            AutoSize = false
        };
        _lblCount = new Label
        {
            Text = "事件 0",
            Font = new Font(Theme.Mono, 14f, FontStyle.Bold),
            ForeColor = Theme.Muted,
            TextAlign = ContentAlignment.MiddleRight,
            Location = Dpi.P(496, 26),
            Size = Dpi.Sz(180, 34),
            Anchor = AnchorStyles.Top | AnchorStyles.Right,
            AutoSize = false
        };

        _header.Controls.Add(_lblState);
        _header.Controls.Add(_lblHint);
        _header.Controls.Add(_lblCount);
        _header.Resize += (_, __) => _lblCount.Left = _header.Width - _lblCount.Width - Dpi.S(18);
        return _header;
    }

    private Control BuildMetrics()
    {
        var grid = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 4,
            RowCount = 2,
            Padding = Dpi.Pad(14, 10, 14, 6),
            BackColor = Theme.Bg,
            Margin = Padding.Empty
        };
        for (int i = 0; i < 4; i++) grid.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 25f));
        for (int i = 0; i < 2; i++) grid.RowStyles.Add(new RowStyle(SizeType.Percent, 50f));

        var margin = new Padding(Dpi.S(4));
        _cCpu = new MetricCard("CPU 占用", "%") { Dock = DockStyle.Fill, Margin = margin };
        _cGpu = new MetricCard("GPU 占用", "%") { Dock = DockStyle.Fill, Margin = margin };
        _cBv = new MetricCard("电池电压", "mV") { Dock = DockStyle.Fill, Margin = margin };
        _cBi = new MetricCard("电池电流", "mA") { Dock = DockStyle.Fill, Margin = margin };
        _cSv = new MetricCard("供电电压 · 推算", "mV") { Dock = DockStyle.Fill, Margin = margin };
        _cSi = new MetricCard("供电电流 · 推算", "mA") { Dock = DockStyle.Fill, Margin = margin };
        _cPct = new MetricCard("电池电量", "%") { Dock = DockStyle.Fill, Margin = margin };
        _cHz = new MetricCard("采样率", "Hz") { Dock = DockStyle.Fill, Margin = margin };

        grid.Controls.Add(_cCpu, 0, 0);
        grid.Controls.Add(_cGpu, 1, 0);
        grid.Controls.Add(_cBv, 2, 0);
        grid.Controls.Add(_cBi, 3, 0);
        grid.Controls.Add(_cSv, 0, 1);
        grid.Controls.Add(_cSi, 1, 1);
        grid.Controls.Add(_cPct, 2, 1);
        grid.Controls.Add(_cHz, 3, 1);

        _tips = new ToolTip { AutoPopDelay = 12000, InitialDelay = 500, ReshowDelay = 200 };
        _tips.SetToolTip(_cBv, "直接读自电池管理芯片的端电压（IOCTL，不是 WMI）。断电瞬间的跌落就体现在这一格。");
        _tips.SetToolTip(_cBi, "正数=放电，负数=充电。用于判断 AC 掉线那一刻电池是否接管。");
        _tips.SetToolTip(_cSv, "外接供电时取电池端电压。适配器的输入电压在消费级笔记本上没有标准读取接口，因此这是推算值而非实测。");
        _tips.SetToolTip(_cSi, "充电时的充电电流绝对值；未充电时为 0。适配器总输出电流无法在系统内测得，故为推算值。");
        _tips.SetToolTip(_cGpu, "\\GPU Engine 计数集中所有 3D / Compute 引擎的占用之和，含核显与独显，上限截到 100%。");
        _tips.SetToolTip(_cHz, "实际采样频率。正常应稳定在 10 Hz 附近，明显偏低说明系统负载过重。");

        return grid;
    }

    private Control BuildChart()
    {
        var host = new Panel
        {
            Dock = DockStyle.Fill,
            Padding = Dpi.Pad(18, 4, 18, 6),
            BackColor = Theme.Bg,
            Margin = Padding.Empty
        };
        _trace = new TracePanel { Dock = DockStyle.Fill };
        host.Controls.Add(_trace);
        return host;
    }

    private Control BuildList()
    {
        var host = new Panel
        {
            Dock = DockStyle.Fill,
            Padding = Dpi.Pad(18, 4, 18, 8),
            BackColor = Theme.Bg,
            Margin = Padding.Empty
        };

        var cap = new Label
        {
            Text = "事件记录（双击行打开该事件前后 5 秒的快照）",
            Dock = DockStyle.Top,
            Height = Dpi.S(24),
            Font = new Font(Theme.Ui, 8.5f),
            ForeColor = Theme.Muted
        };

        _list = new ListView
        {
            Dock = DockStyle.Fill,
            View = View.Details,
            FullRowSelect = true,
            GridLines = false,
            BorderStyle = BorderStyle.FixedSingle,
            Font = new Font(Theme.Ui, 9f),
            BackColor = Color.White,
            HideSelection = false
        };
        _list.Columns.Add("时间", Dpi.S(108));
        _list.Columns.Add("类型", Dpi.S(140));
        _list.Columns.Add("详情", Dpi.S(380));
        _list.DoubleClick += OnListDoubleClick;

        host.Controls.Add(_list);
        host.Controls.Add(cap);
        return host;
    }

    private Control BuildStatus()
    {
        var strip = new StatusStrip
        {
            BackColor = Theme.Card,
            SizingGrip = false,
            Font = new Font(Theme.Ui, 8.5f),
            Dock = DockStyle.Fill
        };
        _sLog = new ToolStripStatusLabel("日志：—")
        {
            AutoSize = false,
            Width = Dpi.S(300),
            TextAlign = ContentAlignment.MiddleLeft
        };
        _sInfo = new ToolStripStatusLabel("") { AutoSize = true };
        var spacer = new ToolStripStatusLabel("") { Spring = true };
        _sMem = new ToolStripStatusLabel("内存 —") { AutoSize = true };

        strip.Items.Add(_sLog);
        strip.Items.Add(_sInfo);
        strip.Items.Add(spacer);
        strip.Items.Add(_sMem);
        return strip;
    }

    protected override void OnLoad(EventArgs e)
    {
        base.OnLoad(e);
        try { Process.GetCurrentProcess().PriorityClass = ProcessPriorityClass.BelowNormal; } catch { }

        _sampler.Start();
        _tray = new TrayController(this, _sampler, ExitApp);

        RunCleanup(silent: true);
        _cleanup.Start();

        string logName = Path.GetFileName(_sampler.CurrentLogPath);
        _sLog.Text = "日志：" + logName;
        _sLog.ToolTipText = _sampler.CurrentLogPath;

        if (_sampler.UiBatteryAvailable == 0)
        {
            _sInfo.Text = "电池接口不可用";
            _sInfo.ForeColor = Theme.BadInk;
        }
        else if (_sampler.UiGpuAvailable == 0)
        {
            _sInfo.Text = "GPU 计数集不可用";
            _sInfo.ForeColor = Theme.WarnInk;
        }

        if (!string.IsNullOrEmpty(_sampler.RecoveryPath))
        {
            _lblHint.Text = "上次会话异常结束，日志尾部已另存为 recovered 文件";
            _lblHint.ForeColor = Theme.BadInk;
        }

        RefreshAutoStartLabel();
        _ui.Start();
    }

    protected override void OnShown(EventArgs e)
    {
        base.OnShown(e);

        // 开机自启时不弹窗。窗体状态保持 Normal，只做 Hide——
        // 一旦设成 Minimized，窗口被任务栏或外壳重新显示时 UI 刷新的判断会永远为假，
        // 界面就会卡在"正在启动…"不动。
        if (_hiddenAtStart)
        {
            _inTray = true;
            Hide();
            Opacity = 1;   // 藏好了再恢复不透明，避免下次打开时整窗透明
        }
    }

    /// <summary>窗口一旦变为可见，就说明它已经回到屏幕上了，解除托盘态。</summary>
    protected override void OnVisibleChanged(EventArgs e)
    {
        if (Visible) _inTray = false;
        base.OnVisibleChanged(e);
    }

    /// <summary>窗口被激活就说明它已经在屏幕上了，解除托盘态。</summary>
    protected override void OnActivated(EventArgs e)
    {
        _inTray = false;
        base.OnActivated(e);
    }

    public void RefreshAutoStartLabel()
    {
        if (!AppPaths.IsInstalled)
        {
            _sInfo.Text = "未安装 · 开机自启不可用";
            _sInfo.ForeColor = Theme.Muted;
            return;
        }

        _sInfo.Text = AppPaths.AutoStartEnabled ? "开机自启：开" : "开机自启：关";
        _sInfo.ForeColor = AppPaths.AutoStartEnabled ? Theme.OkInk : Theme.Muted;
    }

    private void OnCleanup(object sender, EventArgs e) => RunCleanup(silent: false);

    private void RunCleanup(bool silent)
    {
        var r = LogHousekeeper.Clean(AppPaths.LogDir, _sampler.CurrentLogPath);
        if (!silent && r.Deleted > 0 && _tray != null)
            _tray.ShowBalloon("已清理旧日志",
                $"删除 {r.Deleted} 个文件，释放 {LogHousekeeper.Human(r.FreedBytes)}");
    }

    private void OnTick(object sender, EventArgs e)
    {
        bool ac = _sampler.UiAc == 1;

        if (_tray != null)
        {
            string tip = "PowerWatch · " + (ac ? "外部供电" : "电池供电") + " · " +
                         _sampler.UiBattMv + " mV";
            _tray.UpdateState(ac, tip);
        }

        // 收在托盘里就不必重绘界面；这里只看 _inTray，不依赖 Visible / WindowState，
        // 因为窗口可能被外壳重新显示而这两个属性并不可靠。
        if (_inTray) return;
        _header.BackColor = ac ? Theme.OkBg : Theme.WarnBg;
        _lblState.Text = ac ? "外部供电" : "电池供电";
        _lblState.ForeColor = ac ? Theme.OkInk : Theme.WarnInk;

        if (_sampler.UiCharging == 1) _lblHint.Text = "适配器在线 · 充电中";
        else if (ac) _lblHint.Text = "适配器在线 · 未充电";
        else _lblHint.Text = "适配器离线 · 电池放电";
        _lblHint.ForeColor = ac ? Theme.Muted : Theme.WarnInk;

        _lblCount.Text = "事件 " + _sampler.EventCount;

        _cCpu.Set((_sampler.UiCpu10 / 10.0).ToString("F1"));
        _cGpu.Set(_sampler.UiGpu10 < 0 ? "--" : (_sampler.UiGpu10 / 10.0).ToString("F1"));

        _cBv.Set(_sampler.UiBattMv.ToString());
        int ma = _sampler.UiBattMa;
        _cBi.Set(ma.ToString(), ma < 0 ? Theme.OkInk : (ac ? Theme.Ink : Theme.WarnInk));

        _cSv.Set(ac ? _sampler.UiBattMv.ToString() : "--");
        _cSi.Set(ac ? Math.Abs(ma).ToString() : "--");
        _cPct.Set(_sampler.UiPercent < 0 ? "--" : _sampler.UiPercent.ToString());
        _cHz.Set((_sampler.UiHz10 / 10.0).ToString("F1"));

        _trace.Pull(_sampler);

        int n = _sampler.DequeueEvents(_evtBuf);
        for (int i = 0; i < n; i++)
        {
            var ev = _evtBuf[i];
            var item = new ListViewItem(ev.Time.ToString("HH:mm:ss.fff"));
            item.SubItems.Add(ev.Kind);
            item.SubItems.Add(ev.Detail);
            item.Tag = ev;
            item.ForeColor = ev.Kind == "AC_BACK" || ev.Kind == "MANUAL" ? Theme.OkInk : Theme.BadInk;
            _list.Items.Insert(0, item);
        }
        while (_list.Items.Count > 400) _list.Items.RemoveAt(_list.Items.Count - 1);

        if (++_memTick >= 25)
        {
            _memTick = 0;
            try
            {
                using var p = Process.GetCurrentProcess();
                var stat = LogHousekeeper.Measure(AppPaths.LogDir);
                _sMem.Text = "内存 " + (p.WorkingSet64 / 1024 / 1024) + "MB · 日志 " +
                             LogHousekeeper.Human(stat.bytes);
                _sMem.ToolTipText = "日志目录共 " + stat.count + " 个文件\n" + AppPaths.LogDir;
            }
            catch { }
        }
    }

    private void OnListDoubleClick(object sender, EventArgs e)
    {
        if (_list.SelectedItems.Count == 0) return;
        if (_list.SelectedItems[0].Tag is not PowerEvent ev) return;

        try
        {
            string path = ev.SnapshotPath;
            if (!string.IsNullOrEmpty(path) && File.Exists(path))
                Process.Start(new ProcessStartInfo(path) { UseShellExecute = true });
            else
                _tray?.OpenFolder();
        }
        catch { }
    }

    public void ShowFromTray()
    {
        _inTray = false;
        Show();
        WindowState = FormWindowState.Normal;
        Activate();
        BringToFront();
        _ui.Start();
    }

    public void ExitApp()
    {
        _exiting = true;
        Close();
    }

    protected override void OnFormClosing(FormClosingEventArgs e)
    {
        if (!_exiting && e.CloseReason == CloseReason.UserClosing)
        {
            e.Cancel = true;
            _inTray = true;
            Hide();
            _tray?.NotifyMinimized();
            return;
        }

        _ui.Stop();
        _cleanup.Stop();
        _tray?.Dispose();
        _sampler.Dispose();
        base.OnFormClosing(e);
    }
}
