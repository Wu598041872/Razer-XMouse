using System.IO;

namespace XMacroBridge.App;

public partial class App : System.Windows.Application
{
    protected override void OnStartup(System.Windows.StartupEventArgs e)
    {
        base.OnStartup(e);

        var window = new MainWindow();
        MainWindow = window;
        window.Show();

        var startupPaths = ReadStartupPaths(e.Args).ToArray();
        if (startupPaths.Length > 0)
        {
            window.ImportStartupPaths(startupPaths);
        }
    }

    private static IEnumerable<string> ReadStartupPaths(IReadOnlyList<string> arguments)
    {
        foreach (var argument in arguments)
        {
            if (File.Exists(argument) || Directory.Exists(argument))
            {
                yield return argument;
            }
        }
    }
}
