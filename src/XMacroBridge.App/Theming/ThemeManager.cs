using System.ComponentModel;
using System.Windows;
using System.Windows.Media;
using Microsoft.Win32;

namespace XMacroBridge.App.Theming;

internal sealed class ThemeManager : IDisposable
{
    private const string PersonalizeRegistryPath = @"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize";
    private readonly System.Windows.Application application;
    private bool disposed;

    public ThemeManager(System.Windows.Application application)
    {
        this.application = application ?? throw new ArgumentNullException(nameof(application));
    }

    public void Start()
    {
        ApplyCurrentTheme();
        SystemEvents.UserPreferenceChanged += SystemEvents_UserPreferenceChanged;
        SystemParameters.StaticPropertyChanged += SystemParameters_StaticPropertyChanged;
    }

    public void Dispose()
    {
        if (disposed)
        {
            return;
        }

        disposed = true;
        SystemEvents.UserPreferenceChanged -= SystemEvents_UserPreferenceChanged;
        SystemParameters.StaticPropertyChanged -= SystemParameters_StaticPropertyChanged;
    }

    private void SystemEvents_UserPreferenceChanged(object sender, UserPreferenceChangedEventArgs e) =>
        QueueThemeRefresh();

    private void SystemParameters_StaticPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (string.Equals(e.PropertyName, nameof(SystemParameters.HighContrast), StringComparison.Ordinal))
        {
            QueueThemeRefresh();
        }
    }

    private void QueueThemeRefresh()
    {
        if (!application.Dispatcher.HasShutdownStarted)
        {
            _ = application.Dispatcher.BeginInvoke(ApplyCurrentTheme);
        }
    }

    private void ApplyCurrentTheme()
    {
        var palette = SystemParameters.HighContrast
            ? CreateHighContrastPalette()
            : IsDarkApplicationThemeEnabled()
                ? CreateDarkPalette()
                : CreateLightPalette();

        SetBrush("PageBrush", palette.Page);
        SetBrush("CardBrush", palette.Card);
        SetBrush("TextPrimaryBrush", palette.TextPrimary);
        SetBrush("TextSecondaryBrush", palette.TextSecondary);
        SetBrush("DividerBrush", palette.Divider);
        SetBrush("AccentBrush", palette.Accent);
        SetBrush("AccentHoverBrush", palette.AccentHover);
        SetBrush("SelectionBrush", palette.Selection);
        SetBrush("ErrorBrush", palette.Error);
        SetBrush("WarningBrush", palette.Warning);
        SetBrush("SuccessBrush", palette.Success);
        SetBrush("SurfaceSecondaryBrush", palette.SurfaceSecondary);
        SetBrush("CardBorderBrush", palette.CardBorder);
        SetBrush("AlternateRowBrush", palette.AlternateRow);
        SetBrush("DiagnosticSurfaceBrush", palette.DiagnosticSurface);
        SetBrush("SuccessSurfaceBrush", palette.SuccessSurface);
    }

    private void SetBrush(string key, Color color) =>
        application.Resources[key] = new SolidColorBrush(color);

    private static bool IsDarkApplicationThemeEnabled()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(PersonalizeRegistryPath, false);
            return key?.GetValue("AppsUseLightTheme") is int value && value == 0;
        }
        catch (Exception exception) when (exception is UnauthorizedAccessException or System.Security.SecurityException)
        {
            return false;
        }
    }

    private static ThemePalette CreateLightPalette() => new(
        Parse("#FFF5F5F7"),
        Parse("#FFFFFFFF"),
        Parse("#FF1D1D1F"),
        Parse("#FF6E6E73"),
        Parse("#FFE5E5EA"),
        Parse("#FF007AFF"),
        Parse("#FF0066D6"),
        Parse("#FFEAF3FF"),
        Parse("#FFD70015"),
        Parse("#FFB05A00"),
        Parse("#FF19753B"),
        Parse("#FFF2F2F7"),
        Parse("#0D000000"),
        Parse("#66F5F5F7"),
        Parse("#FFF8F8FA"),
        Parse("#FFEAF7EE"));

    private static ThemePalette CreateDarkPalette() => new(
        Parse("#FF1C1C1E"),
        Parse("#FF2C2C2E"),
        Parse("#FFF5F5F7"),
        Parse("#FFAEAEB2"),
        Parse("#FF48484A"),
        Parse("#FF0A84FF"),
        Parse("#FF409CFF"),
        Parse("#FF263C59"),
        Parse("#FFFF453A"),
        Parse("#FFFF9F0A"),
        Parse("#FF30D158"),
        Parse("#FF3A3A3C"),
        Parse("#33FFFFFF"),
        Parse("#1AFFFFFF"),
        Parse("#FF343436"),
        Parse("#FF153D24"));

    private static ThemePalette CreateHighContrastPalette() => new(
        SystemColors.WindowColor,
        SystemColors.WindowColor,
        SystemColors.WindowTextColor,
        SystemColors.GrayTextColor,
        SystemColors.WindowTextColor,
        SystemColors.HighlightColor,
        SystemColors.HotTrackColor,
        SystemColors.HighlightColor,
        SystemColors.WindowTextColor,
        SystemColors.WindowTextColor,
        SystemColors.WindowTextColor,
        SystemColors.ControlColor,
        SystemColors.WindowTextColor,
        SystemColors.ControlColor,
        SystemColors.ControlColor,
        SystemColors.HighlightColor);

    private static Color Parse(string value) => (Color)ColorConverter.ConvertFromString(value)!;

    private sealed record ThemePalette(
        Color Page,
        Color Card,
        Color TextPrimary,
        Color TextSecondary,
        Color Divider,
        Color Accent,
        Color AccentHover,
        Color Selection,
        Color Error,
        Color Warning,
        Color Success,
        Color SurfaceSecondary,
        Color CardBorder,
        Color AlternateRow,
        Color DiagnosticSurface,
        Color SuccessSurface);
}
