using System.IO;
using XMacroBridge.App.Theming;

namespace XMacroBridge.App;

public partial class App : System.Windows.Application
{
    private const string ThemeTestEnvironmentVariable = "XMACROBRIDGE_TEST_MODE";
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
        for (var index = 0; index < arguments.Count; index++)
        {
            if (TryParseThemeArgument(arguments, index, out var mode))
            {
                return mode;
            }
        }

        return ThemeMode.System;
    }

    private static IEnumerable<string> ReadStartupPaths(IReadOnlyList<string> arguments)
    {
        for (var index = 0; index < arguments.Count; index++)
        {
            if (TryParseThemeArgument(arguments, index, out _))
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

    private static bool TryParseThemeArgument(
        IReadOnlyList<string> arguments,
        int index,
        out ThemeMode mode)
    {
        mode = ThemeMode.System;
        if (!string.Equals(Environment.GetEnvironmentVariable(ThemeTestEnvironmentVariable), "1", StringComparison.Ordinal) ||
            index + 1 >= arguments.Count ||
            !string.Equals(arguments[index], "--theme-test", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        mode = arguments[index + 1].ToLowerInvariant() switch
        {
            "light" => ThemeMode.Light,
            "dark" => ThemeMode.Dark,
            "high-contrast" => ThemeMode.HighContrast,
            _ => ThemeMode.System,
        };
        return mode != ThemeMode.System;
    }
}
