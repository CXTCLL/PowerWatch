using System.Diagnostics;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text;

namespace Installer;

/// <summary>安装动作本体：解包、落文件、建快捷方式、写注册表。</summary>
internal static class SetupLogic
{
    public static void Install(bool autoStart, bool shortcut, Action<string> progress)
    {
        progress("准备目录…");
        Directory.CreateDirectory(PowerWatch.AppPaths.InstallDir);

        progress("写入程序文件…");
        var asm = Assembly.GetExecutingAssembly();
        int count = 0;
        foreach (var res in asm.GetManifestResourceNames())
        {
            if (!res.StartsWith("Payload.", StringComparison.Ordinal)) continue;
            string fileName = res.Substring("Payload.".Length);
            string dest = Path.Combine(PowerWatch.AppPaths.InstallDir, fileName);

            using (var src = asm.GetManifestResourceStream(res))
            using (var dst = File.Create(dest))
            {
                src.CopyTo(dst);
            }
            count++;
        }
        if (count == 0) throw new InvalidOperationException("安装包内没有可释放的文件。");

        // 卸载程序就是主程序的一个副本，靠文件名进卸载流程。
        // 这样安装目录里有一个看得见、点得动的卸载入口。
        progress("生成卸载程序…");
        try
        {
            File.Copy(
                Path.Combine(PowerWatch.AppPaths.InstallDir, "PowerWatch.exe"),
                Path.Combine(PowerWatch.AppPaths.InstallDir, "Uninstall.exe"),
                overwrite: true);
        }
        catch { }

        if (shortcut)
        {
            progress("创建快捷方式…");
            try
            {
                string lnk = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.StartMenu),
                    "Programs", "PowerWatch.lnk");
                Shortcut.Create(lnk, PowerWatch.AppPaths.InstalledExe,
                    PowerWatch.AppPaths.InstallDir, "PowerWatch 供电瞬断监测");
            }
            catch { /* 快捷方式失败不影响主流程 */ }
        }

        progress("注册开机自启…");
        PowerWatch.AppPaths.SetAutoStart(autoStart);

        progress("登记卸载信息…");
        PowerWatch.AppPaths.WriteUninstallEntry();
    }

    /// <summary>把正在运行的旧实例关掉，否则文件覆盖不掉。</summary>
    public static bool CloseRunningInstance()
    {
        bool closed = false;
        try
        {
            foreach (var p in Process.GetProcessesByName("PowerWatch"))
            {
                try
                {
                    string path = p.MainModule?.FileName ?? "";
                    if (!path.Equals(PowerWatch.AppPaths.InstalledExe, StringComparison.OrdinalIgnoreCase))
                        continue;

                    p.CloseMainWindow();
                    if (!p.WaitForExit(2500)) p.Kill(true);
                    closed = true;
                }
                catch { }
            }
        }
        catch { }
        return closed;
    }

    public static bool IsInstalledRunning()
    {
        try
        {
            foreach (var p in Process.GetProcessesByName("PowerWatch"))
            {
                try
                {
                    string path = p.MainModule?.FileName ?? "";
                    if (path.Equals(PowerWatch.AppPaths.InstalledExe, StringComparison.OrdinalIgnoreCase))
                        return true;
                }
                catch { }
            }
        }
        catch { }
        return false;
    }
}

/// <summary>用 IShellLink 生成 .lnk，不依赖 WScript.Shell。</summary>
internal static class Shortcut
{
    [ComImport, Guid("00021401-0000-0000-C000-000000000046")]
    private class CShellLink { }

    [ComImport, InterfaceType(ComInterfaceType.InterfaceIsIUnknown),
     Guid("000214F9-0000-0000-C000-000000000046")]
    private interface IShellLinkW
    {
        void GetPath([Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder pszFile, int cch, IntPtr pfd, uint fFlags);
        void GetIDList(out IntPtr ppidl);
        void SetIDList(IntPtr pidl);
        void GetDescription([Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder pszName, int cch);
        void SetDescription([MarshalAs(UnmanagedType.LPWStr)] string pszName);
        void GetWorkingDirectory([Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder pszDir, int cch);
        void SetWorkingDirectory([MarshalAs(UnmanagedType.LPWStr)] string pszDir);
        void GetArguments([Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder pszArgs, int cch);
        void SetArguments([MarshalAs(UnmanagedType.LPWStr)] string pszArgs);
        void GetHotkey(out short pwHotkey);
        void SetHotkey(short wHotkey);
        void GetShowCmd(out int piShowCmd);
        void SetShowCmd(int iShowCmd);
        void GetIconLocation([Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder pszIconPath, int cch, out int piIcon);
        void SetIconLocation([MarshalAs(UnmanagedType.LPWStr)] string pszIconPath, int iIcon);
        void SetRelativePath([MarshalAs(UnmanagedType.LPWStr)] string pszPathRel, uint dwReserved);
        void Resolve(IntPtr hwnd, uint fFlags);
        void SetPath([MarshalAs(UnmanagedType.LPWStr)] string pszFile);
    }

    [ComImport, InterfaceType(ComInterfaceType.InterfaceIsIUnknown),
     Guid("0000010b-0000-0000-C000-000000000046")]
    private interface IPersistFile
    {
        void GetClassID(out Guid pClassID);
        [PreserveSig] int IsDirty();
        void Load([MarshalAs(UnmanagedType.LPWStr)] string pszFileName, uint dwMode);
        void Save([MarshalAs(UnmanagedType.LPWStr)] string pszFileName, [MarshalAs(UnmanagedType.Bool)] bool fRemember);
        void SaveCompleted([MarshalAs(UnmanagedType.LPWStr)] string pszFileName);
        void GetCurFile([MarshalAs(UnmanagedType.LPWStr)] string ppszFileName);
    }

    public static void Create(string lnkPath, string target, string workDir, string description)
    {
        string dir = Path.GetDirectoryName(lnkPath);
        if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);

        object obj = new CShellLink();
        try
        {
            var link = (IShellLinkW)obj;
            link.SetPath(target);
            link.SetWorkingDirectory(workDir);
            link.SetDescription(description);
            link.SetIconLocation(target, 0);
            ((IPersistFile)link).Save(lnkPath, true);
        }
        finally
        {
            if (Marshal.IsComObject(obj)) Marshal.ReleaseComObject(obj);
        }
    }
}
