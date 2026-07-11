using System.IO;
using XMacroBridge.App.Theming;

namespace XMacroBridge.App;

public partial class App : System.Windows.Application
{
    private ThemeManager? themeManager;

    protected override void OnStartup(System.Windows.StartupEventArgs e)
    {
        themeManager = new ThemeManager(this, ParseThemeMode(e.Args));
        themeManager.Start();
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

    protected override void OnExit(System.Windows.ExitEventArgs e)
    {
        themeManager?.Dispose();
        base.OnExit(e);
    }

    private static ThemeMode ParseThemeMode(IReadOnlyList<string> arguments)
    {
        for (var index = 0; index + 1 < arguments.Count; index++)
        {
            if (!string.Equals(arguments[index], "--theme-test", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            return arguments[index + 1].ToLowerInvariant() switch
            {
                "light" => ThemeMode.Light,
                "dark" => ThemeMode.Dark,
                "high-contrast" => ThemeMode.HighContrast,
                _ => ThemeMode.System,
            };
        }

        return ThemeMode.System;
    }

    private static IEnumerable<string> ReadStartupPaths(IReadOnlyList<string> arguments)
    {
        for (var index = 0; index < arguments.Count; index++)
        {
            if (string.Equals(arguments[index], "--theme-test", StringComparison.OrdinalIgnoreCase))
            {
                index++;
                continue;
            }

            if (File.Exists(arguments[index]) || Directory.Exists(arguments[index]))
            {
                yield return arguments[index];
            }
        }
    }
}
