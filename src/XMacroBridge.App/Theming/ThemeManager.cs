using System.ComponentModel;
using System.Windows;
using System.Windows.Media;
using Microsoft.Win32;

namespace XMacroBridge.App.Theming;

internal sealed class ThemeManager : IDisposable
{
    private const string PersonalizeRegistryPath = @"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize";
    private readonly System.Windows.Application application;
    private readonly ThemeMode mode;
    private bool disposed;

    public ThemeManager(System.Windows.Application application, ThemeMode mode = ThemeMode.System)
    {
        this.application = application ?? throw new ArgumentNullException(nameof(application));
        this.mode = mode;
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
            : mode switch
            {
                ThemeMode.Light => CreateLightPalette(),
                ThemeMode.Dark => CreateDarkPalette(),
                ThemeMode.HighContrast => CreateHighContrastPalette(),
                _ => IsDarkApplicationThemeEnabled() ? CreateDarkPalette() : CreateLightPalette(),
            };

        SetBrush("PageBrush", palette.Page);
        SetBrush("CardBrush", palette.Card);
        SetBrush("TextPrimaryBrush", palette.TextPrimary);
        SetBrush("TextSecondaryBrush", palette.TextSecondary);
        SetBrush("DividerBrush", palette.Divider);
        SetBrush("AccentBrush", palette.Accent);
        SetBrush("AccentHoverBrush", palette.AccentHover);
        SetBrush("AccentFillBrush", palette.AccentFill);
        SetBrush("AccentFillTextBrush", palette.AccentFillText);
        SetBrush("AccentFillHoverBrush", palette.AccentFillHover);
        SetBrush("AccentFillPressedBrush", palette.AccentFillPressed);
        SetBrush("FocusRingBrush", palette.FocusRing);
        SetBrush("PrimaryFocusRingBrush", palette.PrimaryFocusRing);
        SetBrush("SelectionBrush", palette.Selection);
        SetBrush("SelectionTextBrush", palette.SelectionText);
        SetBrush("ErrorBrush", palette.Error);
        SetBrush("WarningBrush", palette.Warning);
        SetBrush("SuccessBrush", palette.Success);
        SetBrush("SurfaceSecondaryBrush", palette.SurfaceSecondary);
        SetBrush("CardBorderBrush", palette.CardBorder);
        SetBrush("AlternateRowBrush", palette.AlternateRow);
        SetBrush("DiagnosticSurfaceBrush", palette.DiagnosticSurface);
        SetBrush("SuccessSurfaceBrush", palette.SuccessSurface);
        SetBrush("SuccessSurfaceTextBrush", palette.SuccessSurfaceText);
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
        Parse("#FF005FC7"),
        Parse("#FF004FA8"),
        Parse("#FF005FC7"),
        Parse("#FFFFFFFF"),
        Parse("#FF004FA8"),
        Parse("#FF003F86"),
        Parse("#FF000000"),
        Parse("#FFFFFFFF"),
        Parse("#FFEAF3FF"),
        Parse("#FF1D1D1F"),
        Parse("#FFD70015"),
        Parse("#FFB05A00"),
        Parse("#FF19753B"),
        Parse("#FFF2F2F7"),
        Parse("#0D000000"),
        Parse("#66F5F5F7"),
        Parse("#FFF8F8FA"),
        Parse("#FFEAF7EE"),
        Parse("#FF19753B"));

    private static ThemePalette CreateDarkPalette() => new(
        Parse("#FF1C1C1E"),
        Parse("#FF2C2C2E"),
        Parse("#FFF5F5F7"),
        Parse("#FFAEAEB2"),
        Parse("#FF48484A"),
        Parse("#FF66B2FF"),
        Parse("#FF8CC5FF"),
        Parse("#FF0068D9"),
        Parse("#FFFFFFFF"),
        Parse("#FF005ABF"),
        Parse("#FF004A9E"),
        Parse("#FFFFFFFF"),
        Parse("#FFFFFFFF"),
        Parse("#FF263C59"),
        Parse("#FFF5F5F7"),
        Parse("#FFFF7369"),
        Parse("#FFFF9F0A"),
        Parse("#FF30D158"),
        Parse("#FF3A3A3C"),
        Parse("#33FFFFFF"),
        Parse("#1AFFFFFF"),
        Parse("#FF343436"),
        Parse("#FF153D24"),
        Parse("#FF30D158"));

    private static ThemePalette CreateHighContrastPalette() => new(
        Page: SystemColors.WindowColor,
        Card: SystemColors.WindowColor,
        TextPrimary: SystemColors.WindowTextColor,
        TextSecondary: SystemColors.WindowTextColor,
        Divider: SystemColors.WindowTextColor,
        Accent: SystemColors.WindowTextColor,
        AccentHover: SystemColors.WindowTextColor,
        AccentFill: SystemColors.WindowTextColor,
        AccentFillText: SystemColors.WindowColor,
        AccentFillHover: SystemColors.WindowTextColor,
        AccentFillPressed: SystemColors.WindowTextColor,
        FocusRing: SystemColors.WindowTextColor,
        PrimaryFocusRing: SystemColors.WindowColor,
        Selection: SystemColors.WindowColor,
        SelectionText: SystemColors.WindowTextColor,
        Error: SystemColors.WindowTextColor,
        Warning: SystemColors.WindowTextColor,
        Success: SystemColors.WindowTextColor,
        SurfaceSecondary: SystemColors.WindowColor,
        CardBorder: SystemColors.WindowTextColor,
        AlternateRow: SystemColors.WindowColor,
        DiagnosticSurface: SystemColors.WindowColor,
        SuccessSurface: SystemColors.WindowTextColor,
        SuccessSurfaceText: SystemColors.WindowColor);

    private static Color Parse(string value) => (Color)ColorConverter.ConvertFromString(value)!;

    private sealed record ThemePalette(
        Color Page,
        Color Card,
        Color TextPrimary,
        Color TextSecondary,
        Color Divider,
        Color Accent,
        Color AccentHover,
        Color AccentFill,
        Color AccentFillText,
        Color AccentFillHover,
        Color AccentFillPressed,
        Color FocusRing,
        Color PrimaryFocusRing,
        Color Selection,
        Color SelectionText,
        Color Error,
        Color Warning,
        Color Success,
        Color SurfaceSecondary,
        Color CardBorder,
        Color AlternateRow,
        Color DiagnosticSurface,
        Color SuccessSurface,
        Color SuccessSurfaceText);
}
