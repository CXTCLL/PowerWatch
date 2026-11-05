using System.Diagnostics;

namespace PowerWatch;

/// <summary>卸载：清注册表、清快捷方式、清目录。自身文件靠一个延迟的 cmd 删掉。</summary>
internal static class Uninstaller
{
    public static void Run(bool silent)
    {
        Log("=== 卸载开始  silent=" + silent + "  自身=" + Environment.ProcessPath);

        if (!silent)
        {
            var r = MessageBox.Show(
                "确定卸载 PowerWatch 供电监测吗？\n\n" +
                "将会移除：\n" +
                "  · 程序文件（" + AppPaths.InstallDir + "）\n" +
                "  · 开机自启项\n" +
                "  · 开始菜单快捷方式\n\n" +
                "已经记录下来的日志也会一并删除，如果想留着做证据，请先取消并把\nlogs 目录复制出去。\n\n继续卸载？",
                "卸载 PowerWatch",
                MessageBoxButtons.YesNo,
                MessageBoxIcon.Warning,
                MessageBoxDefaultButton.Button2);

            if (r != DialogResult.Yes) { Log("用户取消"); return; }
        }

        // 程序还在跑的话文件删不掉，先关掉它
        if (CloseRunningInstances())
        {
            Log("已关闭正在运行的实例");
            if (!silent)
                MessageBox.Show("已关闭正在运行的 PowerWatch。", "卸载 PowerWatch",
                    MessageBoxButtons.OK, MessageBoxIcon.Information);
        }

        Step("移除开机自启", () => AppPaths.SetAutoStart(false));
        Step("移除卸载登记", AppPaths.RemoveUninstallEntry);

        string startMenu = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.StartMenu), "Programs");
        Step("移除开始菜单快捷方式", () => DeleteFile(Path.Combine(startMenu, "PowerWatch.lnk")));

        string desktop = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory), "PowerWatch.lnk");
        Step("移除桌面快捷方式", () => DeleteFile(desktop));

        string logDir = AppPaths.LogDir;
        Step("删除日志目录", () =>
        {
            if (Directory.Exists(logDir)) Directory.Delete(logDir, true);
        });

        // 自己正跑在要删的目录里，交给 cmd 延迟两秒动手。
        // 顺带再删一次快捷方式：万一刚才那次被外壳程序占用，两秒后肯定松开了。
        string dir = AppPaths.InstallDir;
        string lnk = Path.Combine(startMenu, "PowerWatch.lnk");
        Step("安排删除程序目录", () =>
        {
            var psi = new ProcessStartInfo("cmd.exe",
                "/c ping -n 3 127.0.0.1 >nul & del /f /q \"" + lnk + "\" 2>nul & rd /s /q \"" + dir + "\"")
            {
                CreateNoWindow = true,
                UseShellExecute = false,
                WindowStyle = ProcessWindowStyle.Hidden
            };
            Process.Start(psi);
        });

        Log("=== 卸载流程结束");

        if (!silent)
            MessageBox.Show("已卸载。程序目录会在几秒后消失。", "PowerWatch",
                MessageBoxButtons.OK, MessageBoxIcon.Information);
    }

    private static void Step(string name, Action action)
    {
        try
        {
            action();
            Log("  [OK] " + name);
        }
        catch (Exception ex)
        {
            Log("  [失败] " + name + " -> " + ex.GetType().Name + ": " + ex.Message);
        }
    }

    private static void DeleteFile(string path)
    {
        if (!File.Exists(path)) { Log("       (文件不存在，跳过) " + path); return; }

        // 刚创建不久的 .lnk 可能正被外壳程序读取（图标缓存），会瞬时占用。
        // 退避重试几次，比一次失败就放弃靠谱得多。
        for (int i = 0; i < 6; i++)
        {
            try
            {
                File.SetAttributes(path, FileAttributes.Normal);
                File.Delete(path);
                Log("       (已删除) " + path);
                return;
            }
            catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException)
            {
                Thread.Sleep(150 * (i + 1));
            }
            catch (Exception ex)
            {
                Log("       (删除异常) " + ex.GetType().Name + ": " + ex.Message);
                return;
            }
        }
        Log("       (重试后仍失败，交给延迟命令) " + path);
    }

    /// <summary>关掉安装目录里正在跑的 PowerWatch，否则 exe 被占用删不掉。</summary>
    private static bool CloseRunningInstances()
    {
        bool closed = false;
        string self = Path.GetFullPath(Environment.ProcessPath ?? "");

        foreach (var p in Process.GetProcessesByName("PowerWatch"))
        {
            try
            {
                string path = Path.GetFullPath(p.MainModule?.FileName ?? "");
                if (string.Equals(path, self, StringComparison.OrdinalIgnoreCase)) continue;
                if (!string.Equals(path, Path.GetFullPath(AppPaths.InstalledExe),
                        StringComparison.OrdinalIgnoreCase)) continue;

                p.Kill(true);
                p.WaitForExit(2500);
                closed = true;
            }
            catch { }
        }
        return closed;
    }

    private static void Log(string msg)
    {
        try
        {
            File.AppendAllText(
                Path.Combine(Path.GetTempPath(), "powerwatch_uninstall.log"),
                DateTime.Now.ToString("HH:mm:ss.fff") + "  " + msg + Environment.NewLine);
        }
        catch { }
    }
}
