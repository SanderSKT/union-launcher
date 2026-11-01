using System.Windows.Forms;
namespace UnionLauncher;
internal static class Program
{
    [STAThread]
    private static void Main(string[] args)
    {
        ApplicationConfiguration.Initialize();
        Application.Run(new LauncherWindow(args.Length == 1 ? args[0] : null));
    }
}
