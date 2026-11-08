namespace BTSMTL.WebWorkbench.Shell;

static class Program
{
    [STAThread]
    static void Main(string[] args)
    {
        if (args.Length != 1 || !Uri.TryCreate(args[0], UriKind.Absolute, out Uri? address))
        {
            MessageBox.Show("用法：BTSMTL.WebWorkbench.Shell <工作台网址>，例如 http://127.0.0.1:5411/", "BTSMTL 工作台");
            return;
        }
        Application.SetHighDpiMode(HighDpiMode.PerMonitorV2);
        Application.EnableVisualStyles();
        Application.SetCompatibleTextRenderingDefault(false);
        Application.Run(new ShellForm(address));
    }
}
