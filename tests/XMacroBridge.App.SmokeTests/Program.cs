using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;
using XMacroBridge.Presentation.Workspace;

namespace XMacroBridge.App.SmokeTests;

internal static class Program
{
    [STAThread]
    private static int Main(string[] args)
    {
        var failures = new List<string>();
        var expectedTheme = ReadExpectedTheme(args);
        var application = new XMacroBridge.App.App();
        application.InitializeComponent();
        application.Startup += (_, _) =>
        {
            _ = application.Dispatcher.BeginInvoke(
                DispatcherPriority.ApplicationIdle,
                async () => await VerifyWorkspaceAsync(application, failures, expectedTheme));
        };

        var exitCode = application.Run();
        if (failures.Count == 0 && exitCode == 0)
        {
            Console.WriteLine($"PASS WPF workspace startup, accessibility and {expectedTheme} theme smoke test");
            return 0;
        }

        foreach (var failure in failures)
        {
            Console.Error.WriteLine($"FAIL WPF smoke test: {failure}");
        }

        return exitCode == 0 ? 1 : exitCode;
    }

    private static async Task VerifyWorkspaceAsync(
        System.Windows.Application application,
        ICollection<string> failures,
        string expectedTheme)
    {
        try
        {
            var window = application.MainWindow as XMacroBridge.App.MainWindow
                ?? throw new InvalidOperationException("MainWindow was not created.");
            window.ShowInTaskbar = false;
            window.WindowState = WindowState.Minimized;

            var viewModel = window.DataContext as WorkspaceViewModel
                ?? throw new InvalidOperationException("WorkspaceViewModel is not the DataContext.");
            for (var attempt = 0; attempt < 200 && (viewModel.IsBusy || viewModel.Macros.Count == 0); attempt++)
            {
                await Task.Delay(25);
            }

            Assert(viewModel.Macros.Count == 9, "Expected nine imported anonymous fixture macros.");
            Assert(viewModel.SelectedMacro is not null, "The first imported macro was not selected.");
            Assert(viewModel.Diagnostics.Count == 3, "Expected three fixture diagnostics.");

            var macroList = Find<ListBox>(window, "MacroList");
            var eventTimeline = Find<DataGrid>(window, "EventTimeline");
            var exportButton = Find<Button>(window, "ExportButton");
            var importFilesButton = Find<Button>(window, "ImportFilesButton");
            var cancelButton = Find<Button>(window, "CancelButton");
            var severityFilter = Find<ComboBox>(window, "DiagnosticSeverityFilter");
            var scopeFilter = Find<ComboBox>(window, "DiagnosticScopeFilter");
            var diagnosticGroups = Find<ItemsControl>(window, "DiagnosticGroupList");
            var targetFormat = Find<ComboBox>(window, "TargetFormatSelector");
            var progress = Find<ProgressBar>(window, "OperationProgress");

            Assert(macroList.Items.Count == 9, "Macro list binding did not expose nine items.");
            Assert(eventTimeline.Items.Count == viewModel.SelectedMacro!.Events.Count, "Event timeline is out of sync with the selection.");
            Assert(exportButton.IsEnabled, "Export should be enabled for the selected valid fixture.");
            Assert(importFilesButton.IsEnabled, "Import should be enabled after startup import completes.");
            Assert(!cancelButton.IsEnabled, "Cancel should be disabled while idle.");
            Assert(severityFilter.Items.Count == 4, "Severity filter options are incomplete.");
            Assert(scopeFilter.Items.Count >= 2, "Source filter options were not populated.");
            Assert(diagnosticGroups.Items.Count >= 1, "Diagnostic grouping binding is empty.");
            VerifyAccessibilityNames(
                macroList,
                eventTimeline,
                exportButton,
                importFilesButton,
                cancelButton,
                severityFilter,
                scopeFilter,
                diagnosticGroups,
                targetFormat,
                progress);
            VerifyTheme(application, expectedTheme);

            application.Shutdown(0);
        }
        catch (Exception exception)
        {
            failures.Add(exception.Message);
            application.Shutdown(1);
        }
    }

    private static T Find<T>(FrameworkElement root, string name)
        where T : FrameworkElement =>
        root.FindName(name) as T ?? throw new InvalidOperationException($"Control {name} was not found.");

    private static void VerifyAccessibilityNames(params FrameworkElement[] elements)
    {
        foreach (var element in elements)
        {
            Assert(
                !string.IsNullOrWhiteSpace(AutomationProperties.GetName(element)),
                $"{element.Name} does not expose an accessibility name.");
        }

        foreach (var control in elements.OfType<Control>().Where(item => item is Button or ComboBox))
        {
            Assert(control.Focusable && control.IsTabStop, $"{control.Name} is not keyboard focusable.");
        }
    }

    private static void VerifyTheme(System.Windows.Application application, string expectedTheme)
    {
        var page = GetColor(application, "PageBrush");
        var card = GetColor(application, "CardBrush");
        var primary = GetColor(application, "TextPrimaryBrush");
        var secondary = GetColor(application, "TextSecondaryBrush");
        var accent = GetColor(application, "AccentBrush");
        var accentFill = GetColor(application, "AccentFillBrush");
        var accentFillText = GetColor(application, "AccentFillTextBrush");
        var error = GetColor(application, "ErrorBrush");
        var warning = GetColor(application, "WarningBrush");
        var success = GetColor(application, "SuccessBrush");

        var expectedPage = expectedTheme switch
        {
            "light" => Parse("#FFF5F5F7"),
            "dark" => Parse("#FF1C1C1E"),
            "high-contrast" => SystemColors.WindowColor,
            _ => throw new InvalidOperationException($"Unknown smoke-test theme {expectedTheme}."),
        };
        Assert(page == expectedPage, $"{expectedTheme} theme did not apply the expected page color.");

        AssertContrast("primary text on page", primary, page);
        AssertContrast("primary text on card", primary, card);
        AssertContrast("secondary text on page", secondary, page);
        AssertContrast("secondary text on card", secondary, card);
        AssertContrast("accent on card", accent, card);
        AssertContrast("button text on accent fill", accentFillText, accentFill);
        AssertContrast("error on card", error, card);
        AssertContrast("warning on card", warning, card);
        AssertContrast("success on card", success, card);
    }

    private static Color GetColor(System.Windows.Application application, string key) =>
        application.Resources[key] is SolidColorBrush brush
            ? brush.Color
            : throw new InvalidOperationException($"Theme resource {key} is not a SolidColorBrush.");

    private static void AssertContrast(string description, Color foreground, Color background)
    {
        var lighter = Math.Max(RelativeLuminance(foreground), RelativeLuminance(background));
        var darker = Math.Min(RelativeLuminance(foreground), RelativeLuminance(background));
        var ratio = (lighter + 0.05) / (darker + 0.05);
        Assert(ratio >= 4.5, $"Contrast for {description} is {ratio:F2}:1, below 4.5:1.");
    }

    private static double RelativeLuminance(Color color) =>
        0.2126 * Linearize(color.R / 255d) +
        0.7152 * Linearize(color.G / 255d) +
        0.0722 * Linearize(color.B / 255d);

    private static double Linearize(double channel) =>
        channel <= 0.04045 ? channel / 12.92 : Math.Pow((channel + 0.055) / 1.055, 2.4);

    private static Color Parse(string value) => (Color)ColorConverter.ConvertFromString(value)!;

    private static string ReadExpectedTheme(IReadOnlyList<string> args)
    {
        for (var index = 0; index + 1 < args.Count; index++)
        {
            if (string.Equals(args[index], "--theme-test", StringComparison.OrdinalIgnoreCase))
            {
                return args[index + 1].ToLowerInvariant();
            }
        }

        throw new InvalidOperationException("WPF smoke test requires --theme-test.");
    }

    private static void Assert(bool condition, string message)
    {
        if (!condition)
        {
            throw new InvalidOperationException(message);
        }
    }
}
