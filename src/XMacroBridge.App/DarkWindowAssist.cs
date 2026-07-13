using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;

namespace XMacroBridge.App;

internal static class DarkWindowAssist
{
    private const int DwmUseImmersiveDarkMode = 20;

    public static void Apply(Window window)
    {
        window.Icon ??= System.Windows.Application.Current?.MainWindow?.Icon;
        if (new WindowInteropHelper(window).Handle != nint.Zero)
        {
            ApplyCore(window);
            return;
        }

        window.SourceInitialized += OnSourceInitialized;
    }

    private static void OnSourceInitialized(object? sender, EventArgs e)
    {
        if (sender is Window window)
        {
            window.SourceInitialized -= OnSourceInitialized;
            ApplyCore(window);
        }
    }

    private static void ApplyCore(Window window)
    {
        if (SystemParameters.HighContrast)
        {
            return;
        }

        try
        {
            var enabled = 1;
            _ = DwmSetWindowAttribute(
                new WindowInteropHelper(window).Handle,
                DwmUseImmersiveDarkMode,
                ref enabled,
                Marshal.SizeOf<int>());
        }
        catch (DllNotFoundException)
        {
            // Unsupported Windows versions keep the native title-bar appearance.
        }
        catch (EntryPointNotFoundException)
        {
            // Unsupported Windows versions keep the native title-bar appearance.
        }
    }

    [DllImport("dwmapi.dll")]
    private static extern int DwmSetWindowAttribute(nint windowHandle, int attribute, ref int value, int valueSize);
}
