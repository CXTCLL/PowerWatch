using System.Diagnostics;
using System.Drawing.Drawing2D;
using System.Runtime.InteropServices;

namespace PowerWatch;

/// <summary>托盘图标与右键菜单。关闭窗口后程序靠它继续存活。</summary>
internal sealed class TrayController : IDisposable
{
    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool DestroyIcon(IntPtr hIcon);

    private readonly NotifyIcon _ni;
    private readonly MainForm _form;
    private readonly Sampler _sampler;
    private readonly Action _exit;

    private readonly ToolStripMenuItem _autoStart;
    private Icon _iconAc;
    private Icon _iconDc;
    private bool _currentAc = true;
    private bool _balloonShown;
    private bool _disposed;

    public TrayController(MainForm form, Sampler sampler, Action exit)
    {
        _form = form;
        _sampler = sampler;
        _exit = exit;

        _iconAc = MakeIcon(Color.FromArgb(0x2E, 0x6B, 0x33));
        _iconDc = MakeIcon(Color.FromArgb(0xB4, 0x53, 0x09));

        var menu = new ContextMenuStrip { Font = new Font(Theme.Ui, 9f), ShowImageMargin = false };

        var open = new ToolStripMenuItem("打开主窗口") { Font = new Font(Theme.Ui, 9f, FontStyle.Bold) };
        open.Click += (_, __) => ShowWindow();

        var snap = new ToolStripMenuItem("立即保存快照（前后 5 秒）");
        snap.Click += (_, __) => { _sampler.CaptureNow(); ShowBalloon("已记录", "5 秒后将写入快照文件"); };

        var logs = new ToolStripMenuItem("打开日志目录");
        logs.Click += (_, __) => OpenFolder();

        _autoStart = new ToolStripMenuItem("开机自动启动")
        {
            Checked = AppPaths.IsInstalled && AppPaths.AutoStartEnabled,
            Enabled = AppPaths.IsInstalled
        };
        _autoStart.Click += (_, __) =>
        {
            bool want = !_autoStart.Checked;
            AppPaths.SetAutoStart(want);
            _autoStart.Checked = want;
            _form.RefreshAutoStartLabel();
        };

        var clean = new ToolStripMenuItem("清理旧日志");
        clean.Click += (_, __) =>
        {
            var r = LogHousekeeper.Clean(AppPaths.LogDir, _sampler.CurrentLogPath);
            ShowBalloon("清理完成",
                r.Deleted == 0 ? "没有需要清理的文件" : $"删除 {r.Deleted} 个文件，释放 {LogHousekeeper.Human(r.FreedBytes)}");
        };

        var about = new ToolStripMenuItem("关于 PowerWatch v" + AppPaths.Version) { Enabled = false };

        var quit = new ToolStripMenuItem("退出");
        quit.Click += (_, __) => _exit();

        menu.Items.Add(open);
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add(snap);
        menu.Items.Add(logs);
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add(_autoStart);
        menu.Items.Add(clean);
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add(about);
        menu.Items.Add(quit);

        _ni = new NotifyIcon
        {
            Icon = _iconAc,
            Text = "PowerWatch · 正在采样",
            Visible = true,
            ContextMenuStrip = menu
        };
        _ni.DoubleClick += (_, __) => ShowWindow();
        _ni.BalloonTipClicked += (_, __) => ShowWindow();
    }

    public void UpdateState(bool ac, string tooltip)
    {
        if (_disposed) return;

        if (ac != _currentAc)
        {
            _currentAc = ac;
            _ni.Icon = ac ? _iconAc : _iconDc;
        }

        if (tooltip.Length > 60) tooltip = tooltip.Substring(0, 60);
        if (_ni.Text != tooltip) _ni.Text = tooltip;
    }

    public void RefreshAutoStart()
    {
        _autoStart.Checked = AppPaths.IsInstalled && AppPaths.AutoStartEnabled;
        _autoStart.Enabled = AppPaths.IsInstalled;
    }

    public void ShowWindow()
    {
        _form.ShowFromTray();
    }

    public void ShowBalloon(string title, string text)
    {
        try
        {
            _ni.BalloonTipTitle = title;
            _ni.BalloonTipText = text;
            _ni.ShowBalloonTip(2500);
        }
        catch { }
    }

    /// <summary>第一次缩到托盘时提示一次，避免用户以为程序没了。</summary>
    public void NotifyMinimized()
    {
        if (_balloonShown) return;
        _balloonShown = true;
        ShowBalloon("PowerWatch 仍在后台运行",
            "窗口已收进托盘，继续以 10Hz 记录供电数据。双击图标可以重新打开。");
    }

    public void OpenFolder()
    {
        try
        {
            Process.Start(new ProcessStartInfo(AppPaths.LogDir) { UseShellExecute = true });
        }
        catch { }
    }

    private static Icon MakeIcon(Color accent)
    {
        using var bmp = new Bitmap(32, 32);
        using (var g = Graphics.FromImage(bmp))
        {
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.Clear(Color.Transparent);

            using var bg = new SolidBrush(accent);
            g.FillEllipse(bg, 0.5f, 0.5f, 31f, 31f);

            var bolt = new[]
            {
                new PointF(18.5f, 4.5f),
                new PointF(9.5f, 18f),
                new PointF(15f, 18f),
                new PointF(12.5f, 27.5f),
                new PointF(22.5f, 13.5f),
                new PointF(16.5f, 13.5f)
            };
            g.FillPolygon(Brushes.White, bolt);
        }

        IntPtr h = bmp.GetHicon();
        try
        {
            using var tmp = Icon.FromHandle(h);
            return (Icon)tmp.Clone();
        }
        finally
        {
            DestroyIcon(h);
        }
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        try { _ni.Visible = false; _ni.Dispose(); } catch { }
        _iconAc?.Dispose();
        _iconDc?.Dispose();
    }
}
