using Microsoft.Win32;

namespace PowerWatch;

/// <summary>安装路径与注册表项的集中管理。</summary>
internal static class AppPaths
{
    public const string AppName = "PowerWatch";
    public const string Version = "1.1.0";
    public const string Publisher = "本地工具";

    public const string RunKeyPath = @"Software\Microsoft\Windows\CurrentVersion\Run";
    public const string RunValueName = "PowerWatch";
    public const string UninstallKeyPath = @"Software\Microsoft\Windows\CurrentVersion\Uninstall\PowerWatch";

    /// <summary>%LOCALAPPDATA%\Programs\PowerWatch —— 无需管理员权限即可写入。</summary>
    public static string InstallDir => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "Programs", "PowerWatch");

    public static string InstalledExe => Path.Combine(InstallDir, "PowerWatch.exe");

    public static bool IsInstalled => File.Exists(InstalledExe);

    /// <summary>当前进程是否就是从安装目录跑起来的。</summary>
    public static bool RunningFromInstallDir
    {
        get
        {
            try
            {
                string cur = Path.GetFullPath(Environment.ProcessPath ?? "");
                string ins = Path.GetFullPath(InstalledExe);
                return string.Equals(cur, ins, StringComparison.OrdinalIgnoreCase);
            }
            catch { return false; }
        }
    }

    public static string LogDir
    {
        get
        {
            string dir = Path.Combine(
                RunningFromInstallDir ? InstallDir : AppContext.BaseDirectory, "logs");
            Directory.CreateDirectory(dir);
            return dir;
        }
    }

    // ---------- 开机自启 ----------

    public static bool AutoStartEnabled
    {
        get
        {
            using var k = Registry.CurrentUser.OpenSubKey(RunKeyPath);
            return k?.GetValue(RunValueName) != null;
        }
    }

    public static void SetAutoStart(bool on)
    {
        using var k = Registry.CurrentUser.CreateSubKey(RunKeyPath, true);
        if (k == null) return;

        if (on)
        {
            if (!IsInstalled) return;
            k.SetValue(RunValueName, "\"" + InstalledExe + "\" --tray");
        }
        else
        {
            if (k.GetValue(RunValueName) != null) k.DeleteValue(RunValueName, false);
        }
    }

    // ---------- 卸载信息（显示在"应用和功能"里） ----------

    public static void WriteUninstallEntry()
    {
        using var k = Registry.CurrentUser.CreateSubKey(UninstallKeyPath, true);
        if (k == null) return;

        string uninstaller = Path.Combine(InstallDir, "Uninstall.exe");
        string unCmd = File.Exists(uninstaller) ? uninstaller : InstalledExe;

        k.SetValue("DisplayName", "PowerWatch 供电监测");
        k.SetValue("DisplayVersion", Version);
        k.SetValue("Publisher", Publisher);
        k.SetValue("InstallLocation", InstallDir);
        k.SetValue("DisplayIcon", InstalledExe);
        k.SetValue("UninstallString", "\"" + unCmd + "\"");
        k.SetValue("QuietUninstallString", "\"" + InstalledExe + "\" --uninstall --silent");
        k.SetValue("NoModify", 1, RegistryValueKind.DWord);
        k.SetValue("NoRepair", 1, RegistryValueKind.DWord);
        k.SetValue("EstimatedSize", EstimateKb(), RegistryValueKind.DWord);
    }

    public static void RemoveUninstallEntry()
    {
        try
        {
            Registry.CurrentUser.DeleteSubKeyTree(UninstallKeyPath, false);
        }
        catch { }
    }

    private static int EstimateKb()
    {
        try
        {
            long total = 0;
            foreach (var f in Directory.GetFiles(InstallDir, "*", SearchOption.AllDirectories))
                total += new FileInfo(f).Length;
            return (int)(total / 1024);
        }
        catch { return 0; }
    }
}
