using System.IO;
using XMacroBridge.App.Theming;

namespace XMacroBridge.App;

public partial class App : System.Windows.Application
{
    private ThemeManager? themeManager;

    protected override void OnStartup(System.Windows.StartupEventArgs e)
    {
        themeManager = new ThemeManager(this);
        themeManager.Start();
        base.OnStartup(e);

        var window = new MainWindow();
        MainWindow = window;
        window.Show();

        var startupPaths = e.Args
            .Where(path => File.Exists(path) || Directory.Exists(path))
            .ToArray();
        if (startupPaths.Length > 0)
        {
            window.ImportStartupPaths(startupPaths);
        }
    }

    protected override void OnExit(System.Windows.ExitEventArgs e)
    {
        themeManager?.Dispose();
        base.OnExit(e);
    }
}
