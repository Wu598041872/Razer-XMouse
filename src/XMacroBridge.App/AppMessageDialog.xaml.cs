using System.Windows;
using System.Windows.Media;

namespace XMacroBridge.App;

public enum AppMessageKind
{
    Information,
    Warning,
    Error,
}

public enum AppDialogResult
{
    Cancel,
    Secondary,
    Primary,
}

public partial class AppMessageDialog : Window
{
    private AppMessageDialog(string heading, string message, AppMessageKind kind)
    {
        InitializeComponent();
        DarkWindowAssist.Apply(this);
        HeadingText.Text = heading;
        MessageText.Text = message;
        var geometryKey = kind switch
        {
            AppMessageKind.Warning => "WarningIconGeometry",
            AppMessageKind.Error => "ErrorIconGeometry",
            _ => "InfoIconGeometry",
        };
        var brushKey = kind switch
        {
            AppMessageKind.Warning => "WarningBrush",
            AppMessageKind.Error => "ErrorBrush",
            _ => "InfoBrush",
        };
        KindIcon.Data = (Geometry)FindResource(geometryKey);
        KindIcon.Stroke = (Brush)FindResource(brushKey);
    }

    public static void Show(Window owner, string heading, string message, AppMessageKind kind = AppMessageKind.Information)
    {
        var dialog = new AppMessageDialog(heading, message, kind) { Owner = owner };
        _ = dialog.ShowDialog();
    }

    public static bool Confirm(
        Window owner,
        string heading,
        string message,
        string confirmText = "继续",
        bool isDangerous = false)
    {
        return ShowQuestion(owner, heading, message, confirmText, null, "取消", AppMessageKind.Warning, isDangerous) ==
               AppDialogResult.Primary;
    }

    public static AppDialogResult ShowQuestion(
        Window owner,
        string heading,
        string message,
        string primaryText,
        string? secondaryText,
        string? cancelText,
        AppMessageKind kind = AppMessageKind.Information,
        bool isDangerous = false)
    {
        var dialog = new AppMessageDialog(heading, message, kind) { Owner = owner };
        dialog.PrimaryButton.Content = primaryText;
        if (isDangerous)
        {
            dialog.PrimaryButton.Style = (Style)dialog.FindResource("DangerButtonStyle");
        }

        if (!string.IsNullOrWhiteSpace(secondaryText))
        {
            dialog.SecondaryButton.Content = secondaryText;
            dialog.SecondaryButton.Visibility = Visibility.Visible;
        }

        if (!string.IsNullOrWhiteSpace(cancelText))
        {
            dialog.CancelButton.Content = cancelText;
            dialog.CancelButton.Visibility = Visibility.Visible;
        }

        _ = dialog.ShowDialog();
        return dialog.Result;
    }

    public AppDialogResult Result { get; private set; } = AppDialogResult.Cancel;

    private void Primary_Click(object sender, RoutedEventArgs e)
    {
        Result = AppDialogResult.Primary;
        DialogResult = true;
    }

    private void Secondary_Click(object sender, RoutedEventArgs e)
    {
        Result = AppDialogResult.Secondary;
        DialogResult = true;
    }

    private void Cancel_Click(object sender, RoutedEventArgs e)
    {
        Result = AppDialogResult.Cancel;
        DialogResult = false;
    }
}
