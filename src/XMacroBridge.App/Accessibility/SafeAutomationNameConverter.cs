using System.Globalization;
using System.IO;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Data;

namespace XMacroBridge.App.Accessibility;

public sealed class SafeAutomationNameConverter : IValueConverter
{
    private const int MaximumLength = 80;
    private static readonly Regex GuidPattern = new(
        @"\b[0-9a-fA-F]{8}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{12}\b",
        RegexOptions.CultureInvariant);
    private static readonly Regex EmbeddedAbsolutePathPattern = new(
        @"(?:[A-Za-z]:[\\/]|\\\\[^\\/\s]+[\\/])",
        RegexOptions.CultureInvariant);

    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        var text = value as string ?? string.Empty;
        text = Sanitize(text);
        if (string.IsNullOrWhiteSpace(text))
        {
            text = "未命名";
        }

        var prefix = parameter as string;
        return string.IsNullOrWhiteSpace(prefix) ? text : $"{prefix}：{text}";
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) =>
        DependencyProperty.UnsetValue;

    private static string Sanitize(string value)
    {
        var text = value.Trim();
        if (Path.IsPathFullyQualified(text))
        {
            var trimmed = text.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            text = Path.GetFileName(trimmed);
            if (string.IsNullOrWhiteSpace(text))
            {
                text = "路径已隐藏";
            }
        }
        else if (EmbeddedAbsolutePathPattern.IsMatch(text))
        {
            text = "路径已隐藏";
        }

        text = GuidPattern.Replace(text, "标识符");
        return text.Length <= MaximumLength ? text : text[..MaximumLength] + "…";
    }
}
