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
    }

    protected override void OnExit(System.Windows.ExitEventArgs e)
    {
        themeManager?.Dispose();
        base.OnExit(e);
    }
}
