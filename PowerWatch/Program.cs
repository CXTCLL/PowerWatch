namespace PowerWatch;

internal static class Program
{
    private const string MutexName = "PowerWatch_SingleInstance_v1";
    private const string ShowEventName = "PowerWatch_ShowWindow_v1";

    [STAThread]
    private static void Main(string[] args)
    {
        bool silent = Has(args, "--silent");

        // 安装目录里那个 Uninstall.exe 就是本程序的一个副本，
        // 靠文件名区分：叫 Uninstall 就直接进卸载流程。
        string selfName = Path.GetFileNameWithoutExtension(Environment.ProcessPath ?? "");
        bool asUninstaller =
            selfName.Equals("Uninstall", StringComparison.OrdinalIgnoreCase) ||
            selfName.Equals("卸载", StringComparison.OrdinalIgnoreCase);

        if (Has(args, "--uninstall") || asUninstaller)
        {
            Application.SetHighDpiMode(HighDpiMode.PerMonitorV2);
            Application.EnableVisualStyles();
            Uninstaller.Run(silent);
            return;
        }

        bool startHidden = Has(args, "--tray");

        using var mutex = new Mutex(true, MutexName, out bool created);
        if (!created)
        {
            // 已经在跑了：把那个窗口叫出来，自己退出
            try
            {
                if (EventWaitHandle.TryOpenExisting(ShowEventName, out var ev))
                {
                    ev.Set();
                    ev.Dispose();
                }
            }
            catch { }
            return;
        }

        Application.SetHighDpiMode(HighDpiMode.PerMonitorV2);
        Application.EnableVisualStyles();
        Application.SetCompatibleTextRenderingDefault(false);

        try
        {
            var form = new MainForm(startHidden);

            // 让第二个实例能把窗口叫到前台
            var showEvent = new EventWaitHandle(false, EventResetMode.AutoReset, ShowEventName);
            var watcher = new Thread(() =>
            {
                while (true)
                {
                    try
                    {
                        showEvent.WaitOne();
                        form.BeginInvoke(new Action(form.ShowFromTray));
                    }
                    catch { break; }
                }
            })
            { IsBackground = true, Name = "ShowWindowWatcher" };
            watcher.Start();

            Application.Run(form);
        }
        catch (Exception ex)
        {
            try
            {
                File.AppendAllText(
                    Path.Combine(AppPaths.LogDir, "crash.log"),
                    DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss") + Environment.NewLine + ex + Environment.NewLine);
            }
            catch { }

            if (!silent)
                MessageBox.Show(ex.Message, "PowerWatch 异常退出",
                    MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }

    private static bool Has(string[] args, string flag)
    {
        foreach (var a in args)
            if (string.Equals(a, flag, StringComparison.OrdinalIgnoreCase)) return true;
        return false;
    }
}
