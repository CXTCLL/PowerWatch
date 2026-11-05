namespace Installer;

internal static class Program
{
    [STAThread]
    private static void Main(string[] args)
    {
        Application.SetHighDpiMode(HighDpiMode.PerMonitorV2);
        Application.EnableVisualStyles();
        Application.SetCompatibleTextRenderingDefault(false);

        bool silent = Array.Exists(args, a => a.Equals("--silent", StringComparison.OrdinalIgnoreCase));

        if (silent)
        {
            try
            {
                if (SetupLogic.IsInstalledRunning()) SetupLogic.CloseRunningInstance();
                SetupLogic.Install(autoStart: true, shortcut: true, progress: _ => { });
            }
            catch (Exception ex)
            {
                try
                {
                    File.WriteAllText(
                        Path.Combine(Path.GetTempPath(), "powerwatch_setup_error.txt"),
                        ex.ToString());
                }
                catch { }
                Environment.ExitCode = 1;
            }
            return;
        }

        Application.Run(new SetupForm());
    }
}
