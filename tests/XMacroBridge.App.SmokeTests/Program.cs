using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using System.Runtime.InteropServices;
using System.Reflection;
using System.Text.RegularExpressions;
using XMacroBridge.Core.Diagnostics;
using XMacroBridge.Core.Models;
using XMacroBridge.Presentation.Workspace;

namespace XMacroBridge.App.SmokeTests;

internal static class Program
{
    [STAThread]
    private static int Main(string[] args)
    {
        var failures = new List<string>();
        VerifyThemeOverrideIsolation();
        var requestedTheme = ReadExpectedTheme(args);
        var expectedTheme = SystemParameters.HighContrast ? "high-contrast" : requestedTheme;
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
            window.Left = -20_000;
            window.Top = -20_000;
            window.Width = 900;
            window.Height = 500;
            window.WindowState = WindowState.Normal;

            var viewModel = window.DataContext as WorkspaceViewModel
                ?? throw new InvalidOperationException("WorkspaceViewModel is not the DataContext.");
            for (var attempt = 0; attempt < 200 && (viewModel.IsBusy || viewModel.Macros.Count == 0); attempt++)
            {
                await Task.Delay(25);
            }

            Assert(viewModel.Macros.Count == 8, "Expected eight imported fixed smoke-test macros.");
            Assert(viewModel.SelectedMacro is not null, "The first imported macro was not selected.");
            Assert(viewModel.Diagnostics.Count == 3, "Expected three fixture diagnostics.");
            Assert(viewModel.Macros.Any(item => item.Name == "匿名化基础宏"), "Razer fixture macro is absent.");
            Assert(viewModel.Macros.Any(item => item.Name == "匿名化父宏"), "Synapse parent fixture macro is absent.");
            Assert(viewModel.Macros.Any(item => item.Name == "basic-key-delay"), "XMBC text fixture macro is absent.");

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
            var workspaceScroll = Find<ScrollViewer>(window, "WorkspaceScrollViewer");
            var diagnosticScroll = Find<ScrollViewer>(window, "DiagnosticScrollViewer");
            var statusText = Find<TextBlock>(window, "StatusTextBlock");
            var delayMilliseconds = Find<TextBox>(window, "DelayMillisecondsTextBox");
            var applyDelay = Find<Button>(window, "ApplyDelayButton");
            var delayScalePercent = Find<TextBox>(window, "DelayScalePercentTextBox");
            var scaleDelays = Find<Button>(window, "ScaleDelaysButton");
            var undo = Find<Button>(window, "UndoButton");
            var redo = Find<Button>(window, "RedoButton");
            var newDelayMilliseconds = Find<TextBox>(window, "NewDelayMillisecondsTextBox");
            var insertDelay = Find<Button>(window, "InsertDelayButton");
            var copyEvent = Find<Button>(window, "CopyEventButton");
            var deleteEvent = Find<Button>(window, "DeleteEventButton");
            var moveEventUp = Find<Button>(window, "MoveEventUpButton");
            var moveEventDown = Find<Button>(window, "MoveEventDownButton");

            viewModel.SelectedEvent = viewModel.Events.FirstOrDefault(item => item.IsFixedDelay)
                ?? throw new InvalidOperationException("The selected smoke-test macro has no fixed delay to edit.");

            window.UpdateLayout();

            Assert(macroList.Items.Count == 8, "Macro list binding did not expose eight fixed fixture items.");
            Assert(eventTimeline.Items.Count == viewModel.SelectedMacro!.Events.Count, "Event timeline is out of sync with the selection.");
            Assert(exportButton.IsEnabled, "Export should be enabled for the selected valid fixture.");
            Assert(importFilesButton.IsEnabled, "Import should be enabled after startup import completes.");
            Assert(!cancelButton.IsEnabled, "Cancel should be disabled while idle.");
            Assert(severityFilter.Items.Count == 4, "Severity filter options are incomplete.");
            Assert(scopeFilter.Items.Count >= 2, "Source filter options were not populated.");
            Assert(diagnosticGroups.Items.Count >= 1, "Diagnostic grouping binding is empty.");
            Assert(applyDelay.IsEnabled, "Selected fixed delay should enable the apply action.");
            Assert(scaleDelays.IsEnabled, "A macro with delays should enable batch scaling.");
            Assert(!undo.IsEnabled && !redo.IsEnabled, "A new workspace should have empty edit history.");
            Assert(insertDelay.IsEnabled, "A macro below the event limit should allow delay insertion.");
            Assert(copyEvent.IsEnabled && deleteEvent.IsEnabled, "Selected event should enable copy and delete.");
            Assert(moveEventUp.IsEnabled || moveEventDown.IsEnabled, "A selected event in a multi-event macro should allow movement.");
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
                progress,
                workspaceScroll,
                diagnosticScroll,
                statusText,
                delayMilliseconds,
                applyDelay,
                delayScalePercent,
                scaleDelays,
                undo,
                redo,
                newDelayMilliseconds,
                insertDelay,
                copyEvent,
                deleteEvent,
                moveEventUp,
                moveEventDown);
            VerifyDpiAwareness(window);
            VerifyCompactLayout(workspaceScroll);
            VerifyKeyboardContract(
                importFilesButton,
                Find<Button>(window, "ImportFolderButton"),
                macroList,
                targetFormat,
                exportButton,
                eventTimeline,
                newDelayMilliseconds,
                insertDelay,
                copyEvent,
                deleteEvent,
                moveEventUp,
                moveEventDown,
                delayMilliseconds,
                applyDelay,
                delayScalePercent,
                scaleDelays,
                undo,
                redo,
                severityFilter,
                scopeFilter,
                diagnosticScroll,
                cancelButton);
            VerifyEditingBindings(
                viewModel,
                window,
                eventTimeline,
                delayMilliseconds,
                newDelayMilliseconds,
                insertDelay,
                copyEvent,
                deleteEvent,
                moveEventUp,
                moveEventDown,
                undo,
                redo);
            VerifyGeneratedAccessibility(viewModel, macroList, eventTimeline, diagnosticGroups, statusText);
            VerifyTimelineVirtualization(viewModel, eventTimeline);
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

        foreach (var control in elements.OfType<Control>().Where(item => item is Button or ComboBox or TextBox))
        {
            Assert(control.Focusable && control.IsTabStop, $"{control.Name} is not keyboard focusable.");
        }
    }

    private static void VerifyThemeOverrideIsolation()
    {
        var previousValue = Environment.GetEnvironmentVariable("XMACROBRIDGE_TEST_MODE");
        try
        {
            Environment.SetEnvironmentVariable("XMACROBRIDGE_TEST_MODE", null);
            var parseThemeMode = typeof(XMacroBridge.App.App).GetMethod(
                "ParseThemeMode",
                BindingFlags.NonPublic | BindingFlags.Static)
                ?? throw new InvalidOperationException("Theme argument parser was not found.");
            var result = parseThemeMode.Invoke(null, [new[] { "--theme-test", "dark" }]);
            Assert(string.Equals(result?.ToString(), "System", StringComparison.Ordinal), "Theme test override escaped the test-mode boundary.");
        }
        finally
        {
            Environment.SetEnvironmentVariable("XMACROBRIDGE_TEST_MODE", previousValue);
        }
    }

    private static void VerifyDpiAwareness(Window window)
    {
        var context = GetWindowDpiAwarenessContext(new System.Windows.Interop.WindowInteropHelper(window).Handle);
        var awareness = GetAwarenessFromDpiAwarenessContext(context);
        Assert(awareness == 2, $"Expected Per-Monitor DPI awareness, actual value was {awareness}.");
        Assert(
            AreDpiAwarenessContextsEqual(context, new nint(-4)),
            "Expected the WPF window to use PerMonitorV2 rather than PerMonitor v1.");
    }

    private static void VerifyCompactLayout(ScrollViewer workspaceScroll)
    {
        Assert(workspaceScroll.ScrollableHeight > 0, "Compact 900x500 layout should provide vertical scrolling instead of clipping.");
        Assert(
            workspaceScroll.ScrollableWidth <= 1,
            $"Compact 900x500 layout should not require horizontal scrolling (ScrollableWidth={workspaceScroll.ScrollableWidth:F1}, ExtentWidth={workspaceScroll.ExtentWidth:F1}, ViewportWidth={workspaceScroll.ViewportWidth:F1}).");
    }

    private static void VerifyKeyboardContract(params Control[] controls)
    {
        var expectedTabIndex = 0;
        foreach (var control in controls)
        {
            Assert(control.TabIndex == expectedTabIndex, $"{control.Name} has TabIndex {control.TabIndex}, expected {expectedTabIndex}.");
            expectedTabIndex++;
        }

        var eventTimeline = controls.OfType<DataGrid>().Single();
        Assert(KeyboardNavigation.GetTabNavigation(eventTimeline) == KeyboardNavigationMode.Once, "Event timeline must be a single Tab stop.");
        Assert(
            Descendants(eventTimeline).OfType<DataGridCell>().All(cell => !cell.IsTabStop),
            "Read-only event timeline cells must not create a Tab trap.");

        var diagnosticScroll = controls.OfType<ScrollViewer>().Single();
        Assert(KeyboardNavigation.GetTabNavigation(diagnosticScroll) == KeyboardNavigationMode.None, "Diagnostic entries must use one named scroll-region Tab stop.");

        VerifyFocusRing(controls.OfType<Button>().Single(item => item.Name == "ImportFolderButton"), "FocusRingBrush");
        VerifyFocusRing(controls.OfType<Button>().Single(item => item.Name == "ExportButton"), "PrimaryFocusRingBrush");
    }

    private static void VerifyEditingBindings(
        WorkspaceViewModel viewModel,
        Window window,
        DataGrid eventTimeline,
        TextBox delayMilliseconds,
        TextBox newDelayMilliseconds,
        Button insertDelay,
        Button copyEvent,
        Button deleteEvent,
        Button moveEventUp,
        Button moveEventDown,
        Button undo,
        Button redo)
    {
        var selectedRow = viewModel.SelectedEvent
            ?? throw new InvalidOperationException("Timeline selection binding is empty.");
        var originalDelay = ((DelayMacroEvent)selectedRow.Event).Milliseconds;
        Assert(
            delayMilliseconds.Text == originalDelay.ToString(),
            "Selected delay value was not populated into the editor.");

        viewModel.DelayMillisecondsText = (originalDelay + 1).ToString();
        Assert(viewModel.UpdateSelectedDelay(), "Smoke-test delay edit failed.");
        window.UpdateLayout();
        Assert(undo.IsEnabled && !redo.IsEnabled, "Edit history buttons did not refresh after an edit.");
        Assert(
            eventTimeline.SelectedItem is MacroEventRow { IsFixedDelay: true },
            "Timeline selection was not restored after immutable macro replacement.");

        Assert(viewModel.Undo(), "Smoke-test undo failed.");
        window.UpdateLayout();
        Assert(redo.IsEnabled, "Redo button did not enable after undo.");
        Assert(
            viewModel.SelectedMacro!.Events.OfType<DelayMacroEvent>().Any(item => item.Milliseconds == originalDelay),
            "Undo did not restore the original delay in the WPF workspace.");

        var originalEventCount = viewModel.SelectedMacro.Events.Count;
        newDelayMilliseconds.Text = "7";
        insertDelay.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        window.UpdateLayout();
        Assert(viewModel.SelectedMacro.Events.Count == originalEventCount + 1, "Insert button did not add a delay event.");
        Assert(viewModel.SelectedEvent?.Event is DelayMacroEvent { Milliseconds: 7 }, "Insert button did not select the new delay.");
        Assert(copyEvent.IsEnabled && deleteEvent.IsEnabled, "Inserted event did not enable copy and delete actions.");

        copyEvent.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        window.UpdateLayout();
        Assert(viewModel.SelectedMacro.Events.Count == originalEventCount + 2, "Copy button did not duplicate the selected event.");
        var copiedDisplayIndex = viewModel.SelectedEvent?.DisplayIndex
            ?? throw new InvalidOperationException("Copied event selection is missing.");
        if (moveEventDown.IsEnabled)
        {
            moveEventDown.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            window.UpdateLayout();
            Assert(viewModel.SelectedEvent?.DisplayIndex == copiedDisplayIndex + 1, "Move-down button did not move the selected event.");
        }

        if (moveEventUp.IsEnabled)
        {
            var beforeMoveUp = viewModel.SelectedEvent!.DisplayIndex;
            moveEventUp.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            window.UpdateLayout();
            Assert(viewModel.SelectedEvent?.DisplayIndex == beforeMoveUp - 1, "Move-up button did not move the selected event.");
        }

        deleteEvent.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        window.UpdateLayout();
        Assert(viewModel.SelectedMacro.Events.Count == originalEventCount + 1, "Delete button did not remove the selected event.");
    }

    private static void VerifyFocusRing(Button button, string expectedBrushKey)
    {
        Assert(button.Focus(), $"{button.Name} could not receive keyboard focus.");
        button.UpdateLayout();
        var border = button.Template.FindName("ButtonBorder", button) as Border
            ?? throw new InvalidOperationException($"{button.Name} template border was not generated.");
        var actualBrush = border.BorderBrush as SolidColorBrush
            ?? throw new InvalidOperationException($"{button.Name} focus border is not a solid brush.");
        var expectedBrush = System.Windows.Application.Current.Resources[expectedBrushKey] as SolidColorBrush
            ?? throw new InvalidOperationException($"Theme resource {expectedBrushKey} is unavailable.");
        Assert(
            actualBrush.Color == expectedBrush.Color,
            $"{button.Name} did not apply {expectedBrushKey} while focused " +
            $"(actual={actualBrush.Color}, expected={expectedBrush.Color}, focused={button.IsKeyboardFocused}, within={button.IsKeyboardFocusWithin}).");
        Assert(border.BorderThickness.Left >= 2, $"{button.Name} focus ring is thinner than 2 DIP.");
    }

    private static void VerifyGeneratedAccessibility(
        WorkspaceViewModel viewModel,
        ListBox macroList,
        DataGrid eventTimeline,
        ItemsControl diagnosticGroups,
        TextBlock statusText)
    {
        var sensitiveGuid = Guid.Parse("aaaaaaaa-bbbb-cccc-dddd-eeeeeeeeeeee");
        var syntheticMacro = new MacroDocument(
            sensitiveGuid,
            $@"C:\Private\macro-{sensitiveGuid}.xml",
            [new UnknownMacroEvent(0, "SyntheticEvent", $@"C:\Private\payload-{sensitiveGuid}.txt")],
            "smoke.synthetic",
            @"C:\Private\macro.xml");
        viewModel.Macros.Add(syntheticMacro);
        viewModel.SelectedMacro = syntheticMacro;
        viewModel.Diagnostics.Add(new ConversionDiagnostic(
            "SMOKE_UIA_REDACTION",
            DiagnosticSeverity.Info,
            $"匿名化诊断详情 C:\\Private\\macro-{sensitiveGuid}.txt",
            SourceContext: @"C:\Private\macro.txt"));
        diagnosticGroups.UpdateLayout();

        for (var index = 0; index < macroList.Items.Count; index++)
        {
            macroList.ScrollIntoView(macroList.Items[index]);
            macroList.UpdateLayout();
            var macroItem = macroList.ItemContainerGenerator.ContainerFromIndex(index) as ListBoxItem
                ?? throw new InvalidOperationException($"Macro item container {index} was not generated.");
            AssertSafeAutomationName(macroItem, $"macro item {index}");
        }

        for (var index = 0; index < eventTimeline.Items.Count; index++)
        {
            var eventRow = eventTimeline.ItemContainerGenerator.ContainerFromIndex(index) as DataGridRow
                ?? throw new InvalidOperationException($"Event row container {index} was not generated.");
            AssertSafeAutomationName(eventRow, $"event row {index}");
        }

        for (var groupIndex = 0; groupIndex < diagnosticGroups.Items.Count; groupIndex++)
        {
            var diagnosticGroup = diagnosticGroups.ItemContainerGenerator.ContainerFromIndex(groupIndex) as FrameworkElement
                ?? throw new InvalidOperationException($"Diagnostic group container {groupIndex} was not generated.");
            AssertSafeAutomationName(diagnosticGroup, $"diagnostic group {groupIndex}");
            var diagnosticItems = Descendants(diagnosticGroup).OfType<ItemsControl>().FirstOrDefault()
                ?? throw new InvalidOperationException($"Diagnostic item list {groupIndex} was not generated.");
            diagnosticItems.UpdateLayout();
            for (var itemIndex = 0; itemIndex < diagnosticItems.Items.Count; itemIndex++)
            {
                var diagnosticItem = diagnosticItems.ItemContainerGenerator.ContainerFromIndex(itemIndex) as FrameworkElement
                    ?? throw new InvalidOperationException($"Diagnostic item container {groupIndex}:{itemIndex} was not generated.");
                AssertSafeAutomationName(diagnosticItem, $"diagnostic item {groupIndex}:{itemIndex}");
            }
        }

        Assert(
            AutomationProperties.GetLiveSetting(statusText) == AutomationLiveSetting.Polite,
            "Status text must expose a polite live region.");
        Assert(
            AutomationProperties.GetName(statusText).Contains(statusText.Text, StringComparison.Ordinal),
            "Status live region must expose the current status text in its accessibility name.");
    }

    private static void VerifyTimelineVirtualization(WorkspaceViewModel viewModel, DataGrid eventTimeline)
    {
        var events = Enumerable.Range(0, 5_000)
            .Select(index => (MacroEvent)new DelayMacroEvent(index, 1))
            .ToArray();
        var largeMacro = new MacroDocument(Guid.NewGuid(), "匿名化大型时间线", events, "smoke.synthetic");
        viewModel.Macros.Add(largeMacro);
        viewModel.SelectedMacro = largeMacro;
        eventTimeline.UpdateLayout();

        var realizedRows = Descendants(eventTimeline).OfType<DataGridRow>().Count();
        Assert(
            realizedRows < 200,
            $"Timeline virtualization is ineffective: {realizedRows} of {events.Length} rows were realized.");
    }

    private static void AssertSafeAutomationName(DependencyObject element, string description)
    {
        var name = AutomationProperties.GetName(element);
        Assert(!string.IsNullOrWhiteSpace(name), $"Generated {description} has no accessibility name.");
        Assert(
            !new[] { "MacroDocument", "MacroEventRow", "ConversionDiagnostic", "DiagnosticGroup" }
                .Any(typeName => name.Contains(typeName, StringComparison.Ordinal)),
            $"Generated {description} leaks an internal type name.");
        Assert(
            !Regex.IsMatch(name, @"(?:[A-Za-z]:[\\/]|\\\\[^\\]+\\[^\\]+)"),
            $"Generated {description} leaks an absolute path.");
        Assert(
            !Regex.IsMatch(name, @"\b[0-9a-fA-F]{8}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{12}\b"),
            $"Generated {description} leaks a GUID.");
    }

    private static IEnumerable<DependencyObject> Descendants(DependencyObject root)
    {
        for (var index = 0; index < VisualTreeHelper.GetChildrenCount(root); index++)
        {
            var child = VisualTreeHelper.GetChild(root, index);
            yield return child;
            foreach (var descendant in Descendants(child))
            {
                yield return descendant;
            }
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
        var accentFillHover = GetColor(application, "AccentFillHoverBrush");
        var accentFillPressed = GetColor(application, "AccentFillPressedBrush");
        var focusRing = GetColor(application, "FocusRingBrush");
        var primaryFocusRing = GetColor(application, "PrimaryFocusRingBrush");
        var selection = GetColor(application, "SelectionBrush");
        var selectionText = GetColor(application, "SelectionTextBrush");
        var surfaceSecondary = GetColor(application, "SurfaceSecondaryBrush");
        var alternateRow = Composite(GetColor(application, "AlternateRowBrush"), card);
        var diagnosticSurface = GetColor(application, "DiagnosticSurfaceBrush");
        var error = GetColor(application, "ErrorBrush");
        var warning = GetColor(application, "WarningBrush");
        var success = GetColor(application, "SuccessBrush");
        var successSurface = GetColor(application, "SuccessSurfaceBrush");
        var successSurfaceText = GetColor(application, "SuccessSurfaceTextBrush");

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
        AssertContrast("primary text on secondary surface", primary, surfaceSecondary);
        AssertContrast("secondary text on secondary surface", secondary, surfaceSecondary);
        AssertContrast("primary text on alternate row", primary, alternateRow);
        AssertContrast("secondary text on alternate row", secondary, alternateRow);
        AssertContrast("primary text on diagnostic surface", primary, diagnosticSurface);
        AssertContrast("secondary text on diagnostic surface", secondary, diagnosticSurface);
        AssertContrast("accent on card", accent, card);
        AssertContrast("accent on diagnostic surface", accent, diagnosticSurface);
        AssertContrast("button text on accent fill", accentFillText, accentFill);
        AssertContrast("button text on hover fill", accentFillText, accentFillHover);
        AssertContrast("button text on pressed fill", accentFillText, accentFillPressed);
        AssertContrastMinimum("secondary button focus ring", focusRing, surfaceSecondary, 3);
        AssertContrastMinimum("secondary button active focus ring", focusRing, selection, 3);
        AssertContrastMinimum("primary button focus ring", primaryFocusRing, accentFill, 3);
        AssertContrastMinimum("primary hover focus ring", primaryFocusRing, accentFillHover, 3);
        AssertContrastMinimum("primary pressed focus ring", primaryFocusRing, accentFillPressed, 3);
        AssertContrast("selection text on selection", selectionText, selection);
        AssertContrast("error on diagnostic surface", error, diagnosticSurface);
        AssertContrast("warning on diagnostic surface", warning, diagnosticSurface);
        AssertContrast("success on diagnostic surface", success, diagnosticSurface);
        AssertContrast("success badge text on success surface", successSurfaceText, successSurface);
    }

    private static Color GetColor(System.Windows.Application application, string key) =>
        application.Resources[key] is SolidColorBrush brush
            ? brush.Color
            : throw new InvalidOperationException($"Theme resource {key} is not a SolidColorBrush.");

    private static Color Composite(Color foreground, Color background)
    {
        var alpha = foreground.A / 255d;
        return Color.FromArgb(
            255,
            (byte)Math.Round(foreground.R * alpha + background.R * (1 - alpha)),
            (byte)Math.Round(foreground.G * alpha + background.G * (1 - alpha)),
            (byte)Math.Round(foreground.B * alpha + background.B * (1 - alpha)));
    }

    private static void AssertContrast(string description, Color foreground, Color background)
        => AssertContrastMinimum(description, foreground, background, 4.5);

    private static void AssertContrastMinimum(string description, Color foreground, Color background, double minimum)
    {
        var lighter = Math.Max(RelativeLuminance(foreground), RelativeLuminance(background));
        var darker = Math.Min(RelativeLuminance(foreground), RelativeLuminance(background));
        var ratio = (lighter + 0.05) / (darker + 0.05);
        Assert(ratio >= minimum, $"Contrast for {description} is {ratio:F2}:1, below {minimum:F1}:1.");
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

    [DllImport("user32.dll")]
    private static extern nint GetWindowDpiAwarenessContext(nint windowHandle);

    [DllImport("user32.dll")]
    private static extern int GetAwarenessFromDpiAwarenessContext(nint value);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool AreDpiAwarenessContextsEqual(nint first, nint second);
}
