namespace BTSMTL.WebWorkbench.Shell;

static class Program
{
    [STAThread]
    static void Main(string[] args)
    {
        bool timed = args.Length == 3 && args[1] == "--surface-timing" && Path.IsPathFullyQualified(args[2]);
        if (args.Length != 1 && !timed || !Uri.TryCreate(args[0], UriKind.Absolute, out Uri? address))
        {
            MessageBox.Show("用法：BTSMTL.WebWorkbench.Shell <工作台网址> [--surface-timing <计时汇总 JSON 绝对路径>]，" +
                "例如 http://127.0.0.1:5411/", "BTSMTL 工作台");
            return;
        }
        Application.SetHighDpiMode(HighDpiMode.PerMonitorV2);
        Application.EnableVisualStyles();
        Application.SetCompatibleTextRenderingDefault(false);
        Application.Run(new ShellForm(address, timed ? new SurfaceTiming(args[2]) : null));
    }
}
