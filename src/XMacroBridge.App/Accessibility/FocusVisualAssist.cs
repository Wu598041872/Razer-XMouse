using System.Windows;
using System.Windows.Media;

namespace XMacroBridge.App.Accessibility;

public static class FocusVisualAssist
{
    public static readonly DependencyProperty FocusRingBrushProperty = DependencyProperty.RegisterAttached(
        "FocusRingBrush",
        typeof(Brush),
        typeof(FocusVisualAssist),
        new FrameworkPropertyMetadata(null));

    public static Brush? GetFocusRingBrush(DependencyObject element) =>
        (Brush?)element.GetValue(FocusRingBrushProperty);

    public static void SetFocusRingBrush(DependencyObject element, Brush? value) =>
        element.SetValue(FocusRingBrushProperty, value);
}
