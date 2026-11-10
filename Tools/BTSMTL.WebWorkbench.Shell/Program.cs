namespace BTSMTL.WebWorkbench.Shell;

static class Program
{
    [STAThread]
    static void Main(string[] args)
    {
        Application.SetHighDpiMode(HighDpiMode.PerMonitorV2);
        Application.EnableVisualStyles();
        Application.SetCompatibleTextRenderingDefault(false);
        try
        {
            bool timed = args.Length >= 2 && args[^2] == "--surface-timing" && Path.IsPathFullyQualified(args[^1]);
            SurfaceTiming? timing = timed ? new SurfaceTiming(args[^1]) : null;
            string[] command = timed ? args[..^2] : args;
            if (command.Length == 0)
            {
                using DesktopLaunch? desktop = DesktopLaunch.Claim();
                if (desktop is not null)
                    Application.Run(new ShellForm(desktop.Connect, timing, desktop));
                return;
            }
            if (command.Length != 1 || !Uri.TryCreate(command[0], UriKind.Absolute, out var address))
                throw new ArgumentException("请直接打开已发布的工作台，或传入现有工作台网址。计时需提供 --surface-timing 与 JSON 绝对路径。");
            Application.Run(new ShellForm(_ => Task.FromResult(address), timing));
        }
        catch (Exception error)
        {
            MessageBox.Show(error.Message, "无法打开 BTSMTL 工作台", MessageBoxButtons.OK, MessageBoxIcon.Error);
            Environment.ExitCode = 1;
        }
    }
}
