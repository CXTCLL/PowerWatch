using System.Diagnostics;

namespace Installer;

internal sealed class SetupForm : Form
{
    private readonly CheckBox _autoStart;
    private readonly CheckBox _shortcut;
    private readonly CheckBox _launch;
    private readonly Button _install;
    private readonly Button _cancel;
    private readonly Label _status;
    private readonly ProgressBar _bar;

    private static int S(double v) => PowerWatch.Dpi.S(v);
    private static Point P(double x, double y) => PowerWatch.Dpi.P(x, y);

    public SetupForm()
    {
        AutoScaleMode = AutoScaleMode.None;

        Text = "PowerWatch 安装程序";
        ClientSize = new Size(S(560), S(400));
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false;
        MinimizeBox = false;
        StartPosition = FormStartPosition.CenterScreen;
        BackColor = Color.White;
        Font = new Font("Microsoft YaHei UI", 9f);

        var title = new Label
        {
            Text = "PowerWatch 供电瞬断监测",
            Font = new Font("Microsoft YaHei UI", 16f, FontStyle.Bold),
            ForeColor = Color.FromArgb(0x1E, 0x1E, 0x1C),
            Location = P(28, 24),
            Size = new Size(S(504), S(34)),
            AutoSize = false
        };

        var sub = new Label
        {
            Text = "版本 " + PowerWatch.AppPaths.Version + "　·　为用户 " +
                   Environment.UserName + " 安装，不需要管理员权限",
            Font = new Font("Microsoft YaHei UI", 8.5f),
            ForeColor = Color.FromArgb(0x6E, 0x6E, 0x68),
            Location = P(30, 60),
            Size = new Size(S(504), S(20)),
            AutoSize = false
        };

        var line = new Panel
        {
            Location = P(28, 88),
            Size = new Size(S(504), Math.Max(1, S(1))),
            BackColor = Color.FromArgb(0xE4, 0xE3, 0xDE)
        };

        var pathCap = new Label
        {
            Text = "安装位置",
            Font = new Font("Microsoft YaHei UI", 8.5f),
            ForeColor = Color.FromArgb(0x6E, 0x6E, 0x68),
            Location = P(30, 102),
            Size = new Size(S(200), S(18)),
            AutoSize = false
        };

        var pathBox = new TextBox
        {
            Text = PowerWatch.AppPaths.InstallDir,
            ReadOnly = true,
            Location = P(30, 122),
            Size = new Size(S(500), S(24)),
            BorderStyle = BorderStyle.FixedSingle,
            BackColor = Color.FromArgb(0xF6, 0xF6, 0xF4),
            Font = new Font("Consolas", 9f)
        };

        _autoStart = new CheckBox
        {
            Text = "开机自动启动，登录后静默驻留托盘",
            Checked = true,
            Location = P(30, 162),
            Size = new Size(S(500), S(24))
        };

        _shortcut = new CheckBox
        {
            Text = "创建开始菜单快捷方式",
            Checked = true,
            Location = P(30, 190),
            Size = new Size(S(500), S(24))
        };

        _launch = new CheckBox
        {
            Text = "安装完成后立即运行",
            Checked = true,
            Location = P(30, 218),
            Size = new Size(S(500), S(24))
        };

        var hint = new Label
        {
            Text = "运行时关闭窗口不会退出，程序会缩到托盘继续以 10Hz 记录。\n" +
                   "日志超过 3 天自动清理，总量封顶 250MB。",
            Font = new Font("Microsoft YaHei UI", 8.5f),
            ForeColor = Color.FromArgb(0x6E, 0x6E, 0x68),
            Location = P(30, 254),
            Size = new Size(S(500), S(40)),
            AutoSize = false
        };

        _status = new Label
        {
            Text = "",
            Font = new Font("Microsoft YaHei UI", 8.5f),
            ForeColor = Color.FromArgb(0x18, 0x5F, 0xA5),
            Location = P(30, 304),
            Size = new Size(S(340), S(20)),
            AutoSize = false
        };

        _bar = new ProgressBar
        {
            Location = P(30, 326),
            Size = new Size(S(500), S(6)),
            Style = ProgressBarStyle.Continuous,
            Minimum = 0,
            Maximum = 100,
            Value = 0
        };

        _install = new Button
        {
            Text = "安装",
            Location = P(348, 348),
            Size = new Size(S(88), S(32)),
            BackColor = Color.FromArgb(0x18, 0x5F, 0xA5),
            ForeColor = Color.White,
            FlatStyle = FlatStyle.Flat
        };
        _install.FlatAppearance.BorderSize = 0;
        _install.Click += OnInstall;

        _cancel = new Button
        {
            Text = "取消",
            Location = P(444, 348),
            Size = new Size(S(86), S(32)),
            FlatStyle = FlatStyle.Flat
        };
        _cancel.Click += (_, __) => Close();

        Controls.AddRange(new Control[]
        {
            title, sub, line, pathCap, pathBox,
            _autoStart, _shortcut, _launch, hint, _status, _bar, _install, _cancel
        });
    }

    private void OnInstall(object sender, EventArgs e)
    {
        if (SetupLogic.IsInstalledRunning())
        {
            var r = MessageBox.Show(
                "PowerWatch 正在运行。安装需要替换程序文件，必须先关闭它。\n\n" +
                "现在关闭并继续安装吗？",
                "正在运行", MessageBoxButtons.YesNo, MessageBoxIcon.Question);

            if (r != DialogResult.Yes) return;
            SetupLogic.CloseRunningInstance();
        }

        _install.Enabled = false;
        _cancel.Enabled = false;
        Cursor = Cursors.WaitCursor;

        try
        {
            SetupLogic.Install(_autoStart.Checked, _shortcut.Checked, Report);
            _bar.Value = 100;
            _status.Text = "安装完成";
        }
        catch (Exception ex)
        {
            Cursor = Cursors.Default;
            _status.Text = "安装失败";
            _status.ForeColor = Color.FromArgb(0xB3, 0x26, 0x1E);
            MessageBox.Show(ex.Message, "安装失败", MessageBoxButtons.OK, MessageBoxIcon.Error);
            _install.Enabled = true;
            _cancel.Enabled = true;
            return;
        }

        Cursor = Cursors.Default;

        if (_launch.Checked)
        {
            try
            {
                Process.Start(new ProcessStartInfo(PowerWatch.AppPaths.InstalledExe)
                {
                    UseShellExecute = true,
                    Arguments = "--tray"
                });
            }
            catch { }
        }

        MessageBox.Show(
            "安装完成。\n\n" +
            "· 程序位置：" + PowerWatch.AppPaths.InstallDir + "\n" +
            (_autoStart.Checked ? "· 已设置为开机自动启动\n" : "· 未设置开机自启\n") +
            "· 关闭窗口后仍在托盘运行，右键托盘图标可以退出\n\n" +
            "卸载：在「应用和功能」里搜索 PowerWatch，或运行\n" +
            PowerWatch.AppPaths.InstalledExe + " --uninstall",
            "PowerWatch", MessageBoxButtons.OK, MessageBoxIcon.Information);

        Close();
    }

    private void Report(string text)
    {
        try
        {
            _status.Text = text;
            // Value 的上限是 Maximum，越界会抛异常把整个安装流程打断
            if (_bar.Value < _bar.Maximum)
                _bar.Value = Math.Min(_bar.Maximum, _bar.Value + 20);
        }
        catch { }
        Application.DoEvents();
    }
}
