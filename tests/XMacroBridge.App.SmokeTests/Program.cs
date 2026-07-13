using System.Windows;
using System.IO;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shell;
using System.Windows.Threading;
using System.Runtime.InteropServices;
using System.Reflection;
using System.Text.RegularExpressions;
using XMacroBridge.Core.Diagnostics;
using XMacroBridge.Core.Models;
using XMacroBridge.Presentation.Workspace;
using XMacroBridge.Presentation.Library;
using MacroMouseButton = XMacroBridge.Core.Models.MouseButton;
using Ellipse = System.Windows.Shapes.Ellipse;

namespace XMacroBridge.App.SmokeTests;

internal static class Program
{
    [STAThread]
    private static int Main(string[] args)
    {
        var failures = new List<string>();
        VerifyKeyboardCaptureNormalization();
        var application = new XMacroBridge.App.App();
        application.InitializeComponent();
        application.Startup += (_, _) =>
        {
            _ = application.Dispatcher.BeginInvoke(
                DispatcherPriority.ApplicationIdle,
                async () => await VerifyWorkspaceAsync(application, failures));
        };

        var exitCode = application.Run();
        if (failures.Count == 0 && exitCode == 0)
        {
            Console.WriteLine("PASS WPF workspace startup, accessibility and fixed Razer dark style smoke test");
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
        ICollection<string> failures)
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
            var importTextButton = Find<Button>(window, "ImportTextButton");
            var cancelButton = Find<Button>(window, "CancelButton");
            var severityFilter = Find<ComboBox>(window, "DiagnosticSeverityFilter");
            var scopeFilter = Find<ComboBox>(window, "DiagnosticScopeFilter");
            var diagnosticGroups = Find<ItemsControl>(window, "DiagnosticGroupList");
            var targetFormat = Find<ComboBox>(window, "TargetFormatSelector");
            var progress = Find<ProgressBar>(window, "OperationProgress");
            var workspaceScroll = Find<ScrollViewer>(window, "WorkspaceScrollViewer");
            var macroContent = Find<Grid>(window, "MacroContent");
            var diagnosticScroll = Find<ScrollViewer>(window, "DiagnosticScrollViewer");
            var statusText = Find<TextBlock>(window, "StatusTextBlock");
            var splitter = Find<GridSplitter>(window, "TimelineDiagnosticSplitter");
            var virtualKeyTextBox = Find<TextBox>(window, "VirtualKeyTextBox");
            var delayScaleTextBox = Find<TextBox>(window, "DelayScalePercentTextBox");
            var scaleDelaysButton = Find<Button>(window, "ScaleDelaysButton");
            var selectedDelayTextBox = Find<TextBox>(window, "DelayMillisecondsTextBox");
            var applyDelayButton = Find<Button>(window, "ApplyDelayButton");
            var nestedMacroTarget = Find<ComboBox>(window, "NestedMacroTargetSelector");
            var bindNestedMacro = Find<Button>(window, "BindNestedMacroButton");
            var insertNestedMacro = Find<Button>(window, "InsertNestedMacroButton");
            var nestedMacroToolbar = Find<Border>(window, "NestedMacroToolbar");
            var minimizeWindow = Find<Button>(window, "WindowMinimizeButton");
            var maximizeRestoreWindow = Find<Button>(window, "WindowMaximizeRestoreButton");
            var closeWindow = Find<Button>(window, "WindowCloseButton");
            var brandNavigationLogo = Find<Image>(window, "BrandNavigationLogo");
            var brandSettingsLogo = Find<Image>(window, "BrandSettingsLogo");
            var macroNavigation = Find<Button>(window, "MacroNavigationButton");
            var libraryNavigation = Find<Button>(window, "LibraryNavigationButton");
            var settingsNavigation = Find<Button>(window, "SettingsNavigationButton");
            var libraryContent = Find<XMacroBridge.App.MacroLibraryView>(window, "MacroLibraryContent");
            var workspaceToolbar = Find<Border>(window, "WorkspaceToolbar");
            var libraryToolbar = Find<Border>(window, "LibraryToolbar");
            var settingsToolbar = Find<Border>(window, "SettingsToolbar");
            var settingsContent = Find<ScrollViewer>(window, "SettingsContent");
            var eventSearchHost = Find<StackPanel>(window, "EventSearchHost");
            var timelineSelectionToolbar = Find<Border>(window, "TimelineSelectionToolbar");
            var timelineInsertionToolbar = Find<Border>(window, "TimelineInsertionToolbar");
            var noMacroEmptyState = Find<StackPanel>(window, "NoMacroEmptyState");
            var emptyMacroState = Find<StackPanel>(window, "EmptyMacroState");
            var diagnosticPanel = Find<Border>(window, "DiagnosticPanel");
            var workspaceStatusBar = Find<Border>(window, "WorkspaceStatusBar");
            var currentMacroLabel = Find<TextBlock>(window, "CurrentMacroLabel");
            var currentMacroValueText = Find<TextBlock>(window, "CurrentMacroValueText");
            var targetFormatLabel = Find<TextBlock>(window, "TargetFormatLabel");

            window.UpdateLayout();
            VerifyTargetFormatBinding(viewModel, targetFormat);

            Assert(macroList.Items.Count == 8, "Macro list binding did not expose eight fixed fixture items.");
            Assert(eventTimeline.Items.Count == viewModel.SelectedMacro!.Events.Count, "Event timeline is out of sync with the selection.");
            Assert(eventTimeline.HeadersVisibility == DataGridHeadersVisibility.None, "Synapse-style timeline should not show table headers.");
            Assert(eventTimeline.Columns.Count == 1, "Synapse-style timeline should use one full-width event column.");
            Assert(eventTimeline.Columns[0] is DataGridTemplateColumn, "Synapse-style timeline column should use an event template.");
            Assert(eventTimeline.Columns[0].ActualWidth > 0, "Synapse-style timeline event column has no measured width.");
            Assert(eventTimeline.SelectionMode == DataGridSelectionMode.Extended, "Timeline must support extended multi-selection.");
            Assert(exportButton.IsEnabled, "Export should be enabled for the selected valid fixture.");
            Assert(importFilesButton.IsEnabled, "Import should be enabled after startup import completes.");
            Assert(importTextButton.IsEnabled, "Text import should be enabled after startup import completes.");
            Assert(!cancelButton.IsEnabled, "Cancel should be disabled while idle.");
            Assert(severityFilter.Items.Count == 4, "Severity filter options are incomplete.");
            Assert(scopeFilter.Items.Count >= 2, "Source filter options were not populated.");
            Assert(targetFormat.Text == viewModel.SelectedExportFormat.DisplayName, "Target format selector must keep its normal display text.");
            Assert(severityFilter.Text == viewModel.SelectedDiagnosticSeverity.DisplayName, "Severity selector must not display an option object type name.");
            Assert(scopeFilter.Text == viewModel.SelectedDiagnosticScope.DisplayName, "Diagnostic scope selector must not display an option object type name.");
            Assert(diagnosticGroups.Items.Count >= 1, "Diagnostic grouping binding is empty.");
            Assert(splitter.ResizeDirection == GridResizeDirection.Rows, "Timeline/report splitter should resize rows.");
            Assert(splitter.ResizeBehavior == GridResizeBehavior.PreviousAndNext, "Timeline/report splitter should resize both adjacent regions.");
            Assert(delayScaleTextBox.Visibility == Visibility.Collapsed && scaleDelaysButton.Visibility == Visibility.Collapsed, "All-delay scaling controls should be removed from the visible toolbar.");
            Assert(selectedDelayTextBox.Visibility == Visibility.Collapsed && applyDelayButton.Visibility == Visibility.Collapsed, "Selected-delay controls should be removed from the visible toolbar.");
            Assert(window.Icon is not null, "The main window application icon was not loaded.");
            Assert(brandNavigationLogo.Source is not null, "The navigation brand logo was not loaded.");
            Assert(brandSettingsLogo.Source is not null, "The settings brand logo was not loaded.");
            var expectedApplicationVersion = typeof(XMacroBridge.App.MainWindow).Assembly
                .GetCustomAttribute<AssemblyInformationalVersionAttribute>()?
                .InformationalVersion;
            Assert(window.ApplicationVersion == expectedApplicationVersion, "Settings version binding does not expose the application informational version.");
            VerifyProgressiveWorkspaceStates(
                window,
                viewModel,
                eventSearchHost,
                timelineSelectionToolbar,
                timelineInsertionToolbar,
                noMacroEmptyState,
                emptyMacroState,
                diagnosticPanel,
                workspaceStatusBar,
                Find<Button>(window, "EditEventButton"),
                Find<Button>(window, "DeleteEventButton"));
            VerifyAccessibilityNames(
                macroList,
                eventTimeline,
                exportButton,
                importFilesButton,
                importTextButton,
                cancelButton,
                severityFilter,
                scopeFilter,
                diagnosticGroups,
                targetFormat,
                progress,
                workspaceScroll,
                diagnosticScroll,
                statusText,
                splitter);
            VerifyDpiAwareness(window);
            VerifyWindowChrome(window, minimizeWindow, maximizeRestoreWindow, closeWindow);
            Assert(
                !Descendants(window).OfType<TextBlock>().Any(item => item.Text == "本地离线") &&
                !Descendants(window).OfType<Ellipse>().Any(),
                "Title bar local-offline label and green status dot should be removed.");
            VerifyTitleBarNavigationStability(window, macroNavigation, libraryNavigation, settingsNavigation);
            VerifyControlAlignment(
                window,
                Find<Border>(window, "CurrentMacroValueHost"),
                targetFormat,
                exportButton,
                Find<TextBox>(window, "NewDelayMillisecondsTextBox"),
                Find<Button>(window, "InsertDelayButton"),
                Find<TextBox>(window, "VirtualKeyTextBox"),
                Find<ComboBox>(window, "KeyTransitionSelector"),
                Find<Button>(window, "InsertKeyEventButton"),
                severityFilter,
                scopeFilter);
            VerifyHeaderFieldAlignment(
                application,
                window,
                viewModel,
                currentMacroLabel,
                Find<Border>(window, "CurrentMacroValueHost"),
                currentMacroValueText,
                targetFormatLabel,
                targetFormat,
                severityFilter);
            VerifyParameterInputAlignment(
                Find<TextBox>(window, "NewDelayMillisecondsTextBox"),
                virtualKeyTextBox);
            VerifyVirtualKeyCapture(window, viewModel, virtualKeyTextBox);
            VerifyGreenHighlightForeground(
                application,
                exportButton,
                importFilesButton);
            VerifyNormalTextForeground(application, window);
            VerifyCompactLayout(
                workspaceScroll,
                macroContent,
                Find<TextBlock>(window, "EventSearchResultTextBlock"),
                scopeFilter,
                cancelButton,
                insertNestedMacro);
            VerifyLibraryNavigation(
                window,
                macroNavigation,
                libraryNavigation,
                settingsNavigation,
                libraryContent,
                workspaceScroll,
                workspaceToolbar,
                libraryToolbar,
                settingsToolbar,
                settingsContent);
            VerifyTimelineResponsiveResize(window, viewModel, eventTimeline);
            VerifyKeyboardContract(
                importFilesButton,
                Find<Button>(window, "ImportFolderButton"),
                importTextButton,
                macroList,
                targetFormat,
                exportButton,
                eventTimeline,
                splitter,
                severityFilter,
                scopeFilter,
                diagnosticScroll,
                cancelButton);
            VerifyMacroTextDialog(window);
            VerifyMacroLibraryEntryDialog(window);
            VerifyMacroRenameUi(window, macroList);
            VerifyTimelineMultiSelectionUi(window, viewModel, eventTimeline);
            viewModel.SelectedEvent = viewModel.Events.First(item => item.Event is DelayMacroEvent);
            VerifyEditingBindings(
                viewModel,
                window,
                eventTimeline,
                Find<TextBox>(window, "DelayMillisecondsTextBox"),
                Find<TextBox>(window, "NewDelayMillisecondsTextBox"),
                Find<Button>(window, "InsertDelayButton"),
                Find<Button>(window, "CopyEventButton"),
                Find<Button>(window, "DeleteEventButton"),
                Find<Button>(window, "MoveEventUpButton"),
                Find<Button>(window, "MoveEventDownButton"),
                Find<Button>(window, "UndoButton"),
                Find<Button>(window, "RedoButton"));
            VerifyParameterizedInsertionBindings(
                viewModel,
                window,
                Find<TextBox>(window, "VirtualKeyTextBox"),
                Find<ComboBox>(window, "KeyTransitionSelector"),
                Find<CheckBox>(window, "ExtendedKeyCheckBox"),
                Find<Button>(window, "InsertKeyEventButton"),
                Find<ComboBox>(window, "MouseButtonSelector"),
                Find<ComboBox>(window, "MouseTransitionSelector"),
                Find<Button>(window, "InsertMouseEventButton"));
            VerifyNestedMacroBindingUi(
                window,
                viewModel,
                eventTimeline,
                nestedMacroToolbar,
                nestedMacroTarget,
                bindNestedMacro,
                insertNestedMacro);
            VerifySelectedEventReplacement(viewModel, window);
            VerifyEventSearchBindings(
                viewModel,
                window,
                Find<TextBox>(window, "EventSearchTextBox"),
                Find<Button>(window, "FindPreviousEventButton"),
                Find<Button>(window, "FindNextEventButton"),
                Find<TextBlock>(window, "EventSearchResultTextBlock"));
            VerifyEventEditDialog(window);
            VerifyGeneratedAccessibility(viewModel, macroList, eventTimeline, diagnosticGroups, statusText);
            VerifyTimelineVirtualization(viewModel, eventTimeline);
            VerifyTimelineScrollBar(eventTimeline);
            VerifyTheme(application);
            VerifyTablerIconResources(application);

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

        foreach (var control in elements.OfType<Control>().Where(item => item is Button or ComboBox or TextBox or CheckBox))
        {
            Assert(control.Focusable && control.IsTabStop, $"{control.Name} is not keyboard focusable.");
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

    private static void VerifyCompactLayout(
        ScrollViewer workspaceScroll,
        FrameworkElement macroContent,
        params FrameworkElement[] rightEdgeElements)
    {
        Assert(workspaceScroll.ScrollableHeight > 0, "Compact 900x500 layout should provide vertical scrolling instead of clipping.");
        Assert(
            workspaceScroll.ScrollableWidth <= 1,
            $"Compact 900x500 layout should not require horizontal scrolling (ScrollableWidth={workspaceScroll.ScrollableWidth:F1}, ExtentWidth={workspaceScroll.ExtentWidth:F1}, ViewportWidth={workspaceScroll.ViewportWidth:F1}).");
        var contentRight = macroContent.TranslatePoint(new Point(macroContent.ActualWidth, 0), workspaceScroll).X;
        Assert(
            contentRight <= workspaceScroll.ViewportWidth + 0.5,
            $"Compact workspace content exceeds the visible viewport: {contentRight:F1} > {workspaceScroll.ViewportWidth:F1}.");
        foreach (var element in rightEdgeElements)
        {
            if (!element.IsVisible || element.Visibility != Visibility.Visible)
            {
                continue;
            }

            var elementRight = element.TranslatePoint(new Point(element.ActualWidth, 0), workspaceScroll).X;
            Assert(
                elementRight <= workspaceScroll.ViewportWidth + 0.5,
                $"Compact layout clips {element.Name} at the right edge: {elementRight:F1} > {workspaceScroll.ViewportWidth:F1}.");
        }
    }

    private static void VerifyKeyboardCaptureNormalization()
    {
        Assert(
            KeyboardKeyCapture.ResolveEffectiveKey(Key.ImeProcessed, Key.None, Key.A, Key.None) == Key.A,
            "IME-processed keyboard input should resolve to its underlying A key instead of VK 229.");
        Assert(
            KeyboardKeyCapture.ResolveEffectiveKey(Key.DeadCharProcessed, Key.None, Key.None, Key.OemTilde) == Key.OemTilde,
            "Dead-character keyboard input should resolve to its underlying punctuation key.");
        Assert(InputEventDisplayFormatter.FormatVirtualKey(0xBA) == "; 键",
            "OEM punctuation keys should have a readable display name.");
        Assert(InputEventDisplayFormatter.FormatVirtualKey(0xE5) == "输入法处理键",
            "VK_PROCESSKEY must not fall back to a numeric VK label.");
    }

    private static void VerifyTargetFormatBinding(WorkspaceViewModel viewModel, ComboBox targetFormat)
    {
        var xMouse = viewModel.ExportFormats.Single(item => item.FormatId == "xmbc.macro.text");
        targetFormat.SelectedItem = xMouse;
        targetFormat.UpdateLayout();
        Assert(viewModel.TargetFormatId == "xmbc.macro.text",
            "Selecting X-Mouse text did not update the backend target format.");
        Assert(viewModel.SelectedExportFormat.Extension == ".txt",
            "X-Mouse text selection did not produce a .txt export extension.");

        var razer = viewModel.ExportFormats.Single(item => item.FormatId == "razer.macro.xml");
        targetFormat.SelectedItem = razer;
        targetFormat.UpdateLayout();
        Assert(viewModel.TargetFormatId == "razer.macro.xml",
            "Selecting Razer XML did not restore the backend target format.");
    }

    private static void VerifyLibraryNavigation(
        Window window,
        Button macroNavigation,
        Button libraryNavigation,
        Button settingsNavigation,
        XMacroBridge.App.MacroLibraryView libraryContent,
        ScrollViewer workspaceContent,
        Border workspaceToolbar,
        Border libraryToolbar,
        Border settingsToolbar,
        ScrollViewer settingsContent)
    {
        var libraryViewModel = libraryContent.DataContext as MacroLibraryViewModel
            ?? throw new InvalidOperationException("Macro library view does not expose its view model.");
        Assert(
            libraryViewModel.RootPath.StartsWith(Path.GetTempPath(), StringComparison.OrdinalIgnoreCase),
            "Smoke tests must isolate the macro library under the system temporary directory.");

        libraryNavigation.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        window.UpdateLayout();
        Assert(libraryContent.Visibility == Visibility.Visible, "Macro library navigation did not show the library page.");
        Assert(workspaceContent.Visibility == Visibility.Collapsed, "Macro library navigation left the workspace visible.");
        Assert(libraryToolbar.Visibility == Visibility.Visible && workspaceToolbar.Visibility == Visibility.Collapsed, "Macro library toolbar did not replace the conversion toolbar.");
        Assert(Find<TextBox>(window, "LibrarySearchTextBox").IsEnabled, "Macro library search is unavailable.");
        Assert(Find<Button>(window, "RefreshLibraryButton").IsEnabled, "Macro library refresh is unavailable.");
        Assert(libraryViewModel.CanModifyLibrary == !libraryViewModel.IsBusy, "Macro library busy-state action gate is inconsistent.");
        Assert(Find<ListBox>(libraryContent, "XMouseList").AllowDrop && Find<ListBox>(libraryContent, "RazerList").AllowDrop,
            "Macro library lists do not accept drag reordering.");
        Assert(libraryViewModel.CanReorderLibraryItems,
            "Macro library custom ordering should be available in the default unsorted view.");
        Assert(
            Find<StackPanel>(libraryContent, "XMouseEmptyState").Visibility == (libraryViewModel.HasXMouseItems ? Visibility.Collapsed : Visibility.Visible),
            "XMouse library empty state does not match the visible collection.");
        Assert(
            Find<StackPanel>(libraryContent, "RazerEmptyState").Visibility == (libraryViewModel.HasRazerItems ? Visibility.Collapsed : Visibility.Visible),
            "Razer library empty state does not match the visible collection.");
        Assert(
            Find<Grid>(libraryContent, "XMouseActionPanel").Visibility == (libraryViewModel.HasSelectedXMouseItem ? Visibility.Visible : Visibility.Collapsed),
            "XMouse library action panel does not follow selection state.");
        Assert(Find<Border>(libraryContent, "LibraryStatusBar").IsVisible, "Macro library status bar is not visible.");

        settingsNavigation.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        window.UpdateLayout();
        Assert(settingsContent.Visibility == Visibility.Visible, "Settings navigation did not show settings.");
        Assert(settingsToolbar.Visibility == Visibility.Visible && libraryToolbar.Visibility == Visibility.Collapsed, "Settings toolbar visibility is incorrect.");

        macroNavigation.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        window.UpdateLayout();
        Assert(workspaceContent.Visibility == Visibility.Visible && workspaceToolbar.Visibility == Visibility.Visible, "Workspace navigation did not restore the conversion page.");
    }

    private static void VerifyProgressiveWorkspaceStates(
        Window window,
        WorkspaceViewModel viewModel,
        StackPanel eventSearchHost,
        Border selectionToolbar,
        Border insertionToolbar,
        StackPanel noMacroEmptyState,
        StackPanel emptyMacroState,
        Border diagnosticPanel,
        Border statusBar,
        Button editButton,
        Button deleteButton)
    {
        var originalMacro = viewModel.SelectedMacro
            ?? throw new InvalidOperationException("Progressive workspace verification requires a selected macro.");
        var originalEventIndex = viewModel.SelectedEvent?.EventIndex;

        Assert(viewModel.HasSelectedMacro, "Selected-macro state should be true for the startup fixture.");
        Assert(viewModel.HasTimelineEvents == (viewModel.Events.Count > 0), "Timeline-event state is inconsistent.");
        Assert(viewModel.HasDiagnostics == (viewModel.Diagnostics.Count > 0), "Diagnostic presence state is inconsistent.");
        Assert(viewModel.HasFilteredOutDiagnostics == (viewModel.HasDiagnostics && !viewModel.HasFilteredDiagnostics), "Filtered-diagnostic state is inconsistent.");

        viewModel.SelectedMacro = null;
        window.UpdateLayout();
        Assert(!viewModel.HasSelectedMacro, "Clearing the macro did not update HasSelectedMacro.");
        Assert(eventSearchHost.Visibility == Visibility.Collapsed, "Event search should be hidden without a macro.");
        Assert(selectionToolbar.Visibility == Visibility.Collapsed && insertionToolbar.Visibility == Visibility.Collapsed, "Timeline tools should be hidden without a macro.");
        Assert(diagnosticPanel.Visibility == Visibility.Collapsed, "Diagnostic report should collapse without a macro.");
        Assert(noMacroEmptyState.Visibility == Visibility.Visible, "No-macro empty state is not visible.");
        Assert(statusBar.Visibility == Visibility.Visible, "Workspace status bar should remain available without a macro.");

        var emptyMacro = new MacroDocument(Guid.NewGuid(), "匿名化空宏", [], "smoke.synthetic");
        viewModel.Macros.Add(emptyMacro);
        viewModel.SelectedMacro = emptyMacro;
        window.UpdateLayout();
        Assert(!viewModel.HasTimelineEvents, "Empty macro should expose no timeline events.");
        Assert(emptyMacroState.Visibility == Visibility.Visible, "Empty-macro guidance is not visible.");
        Assert(noMacroEmptyState.Visibility == Visibility.Collapsed, "No-macro guidance should hide when an empty macro is selected.");
        Assert(selectionToolbar.Visibility == Visibility.Visible && insertionToolbar.Visibility == Visibility.Visible, "Insertion tools should appear for an empty selected macro.");
        Assert(diagnosticPanel.Visibility == Visibility.Visible, "Diagnostic panel should appear for a selected empty macro.");

        viewModel.SelectedMacro = originalMacro;
        viewModel.Macros.Remove(emptyMacro);
        if (originalEventIndex is { } eventIndex)
        {
            viewModel.SelectedEvent = viewModel.Events.FirstOrDefault(item => item.EventIndex == eventIndex);
        }

        window.UpdateLayout();
        if (viewModel.Events.Count > 0)
        {
            var row = viewModel.Events[0];
            viewModel.SelectedEvent = row;
            viewModel.SetSelectedEventIndices([row.EventIndex]);
            window.UpdateLayout();
            Assert(viewModel.HasSelectedEvents && viewModel.SelectedEventCountText == "已选 1 项", "Single-event selection state is incorrect.");
            Assert(editButton.Visibility == Visibility.Visible && deleteButton.Visibility == Visibility.Visible, "Selection actions should appear after selecting an event.");

            viewModel.SetSelectedEventIndices([]);
            viewModel.SelectedEvent = null;
            window.UpdateLayout();
            Assert(!viewModel.HasSelectedEvents, "Clearing event selection did not update HasSelectedEvents.");
            Assert(editButton.Visibility == Visibility.Collapsed && deleteButton.Visibility == Visibility.Collapsed, "Selection actions should hide without an event.");
        }

        viewModel.SelectedDiagnosticSeverity = viewModel.DiagnosticSeverityOptions[^1];
        viewModel.SelectedDiagnosticScope = viewModel.DiagnosticScopes[^1];
        viewModel.ResetDiagnosticFilters();
        Assert(ReferenceEquals(viewModel.SelectedDiagnosticSeverity, viewModel.DiagnosticSeverityOptions[0]), "ResetDiagnosticFilters did not restore all severities.");
        Assert(ReferenceEquals(viewModel.SelectedDiagnosticScope, viewModel.DiagnosticScopes[0]), "ResetDiagnosticFilters did not restore all diagnostic sources.");
    }

    private static void VerifyNestedMacroBindingUi(
        Window window,
        WorkspaceViewModel viewModel,
        DataGrid eventTimeline,
        Border toolbar,
        ComboBox targetSelector,
        Button bindButton,
        Button insertButton)
    {
        var originalMacro = viewModel.SelectedMacro;
        var parent = viewModel.Macros.Single(item => item.Name == "匿名化父宏");
        var child = viewModel.Macros.Single(item => item.Name == "匿名化子宏");
        viewModel.SelectedMacro = parent;
        viewModel.SelectedEvent = viewModel.Events.Single(item => item.Event is MacroReferenceEvent);
        window.UpdateLayout();

        Assert(targetSelector.Items.Count == viewModel.Macros.Count - 1, "Nested target selector must list other imported macros.");
        Assert(Grid.GetRow(toolbar) == 3 && Grid.GetRow(eventTimeline) == 4, "Nested macro controls should occupy a dedicated toolbar row above the timeline.");
        Assert(double.IsNaN(targetSelector.Width), "Nested target selector should use responsive width rather than a fixed width.");
        Assert(targetSelector.ActualWidth >= targetSelector.MinWidth - 0.5, "Nested target selector was compressed below its usable minimum width.");
        Assert(targetSelector.ActualWidth <= targetSelector.MaxWidth + 0.5, "Nested target selector exceeded its compact maximum width.");
        var insertRight = insertButton.TranslatePoint(new Point(insertButton.ActualWidth, 0), toolbar).X;
        Assert(insertRight <= toolbar.ActualWidth + 0.5, "Nested macro actions are clipped at the toolbar right edge.");
        Assert(ReferenceEquals(targetSelector.SelectedItem, child), "Selecting an existing nested event did not select its imported target.");
        Assert(targetSelector.Text == child.Name, "Nested target selector did not bind the selected macro name locally.");
        targetSelector.ApplyTemplate();
        window.UpdateLayout();
        Assert(
            Descendants(targetSelector).OfType<TextBlock>().Any(item => item.Text == child.Name && item.IsVisible),
            "Collapsed nested target selector did not visibly render the selected macro name.");
        Assert(bindButton.IsEnabled, "Existing nested events should allow manual target binding.");
        Assert(insertButton.IsEnabled, "Selected imported targets should allow nested event insertion.");
        Assert(!string.IsNullOrWhiteSpace(AutomationProperties.GetName(targetSelector)), "Nested target selector has no accessible name.");
        Assert(!string.IsNullOrWhiteSpace(AutomationProperties.GetName(bindButton)), "Nested bind button has no accessible name.");
        Assert(!string.IsNullOrWhiteSpace(AutomationProperties.GetName(insertButton)), "Nested insert button has no accessible name.");

        var beforeCount = viewModel.SelectedMacro.Events.Count;
        insertButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        Assert(
            viewModel.SelectedMacro!.Events.Count == beforeCount + 1 &&
            viewModel.SelectedEvent?.Event is MacroReferenceEvent inserted &&
            inserted.TargetGuid == child.Id,
            "Nested insert button did not add a reference to the selected imported macro.");
        Assert(viewModel.Undo(), "Nested UI insertion should enter undo history.");
        Assert(viewModel.SelectedMacro!.Events.Count == beforeCount, "Undo did not remove the nested UI insertion.");

        viewModel.SelectedMacro = originalMacro;
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

    private static void VerifyVirtualKeyCapture(Window window, WorkspaceViewModel viewModel, TextBox textBox)
    {
        var source = PresentationSource.FromVisual(window)
            ?? throw new InvalidOperationException("Main window has no presentation source for key capture testing.");
        var letterArgs = new KeyEventArgs(Keyboard.PrimaryDevice, source, 0, Key.E)
        {
            RoutedEvent = Keyboard.PreviewKeyDownEvent,
        };
        textBox.RaiseEvent(letterArgs);
        Assert(letterArgs.Handled, "Captured keyboard input should not be typed into the VK field.");
        Assert(viewModel.NewVirtualKeyText == "69", "Pressing E should capture VK 69.");
        Assert(textBox.Text == "E", "The keyboard input field should show the readable key name instead of VK 69.");
        Assert(!viewModel.NewKeyIsExtended, "Letter keys should not be marked extended.");

        var extendedArgs = new KeyEventArgs(Keyboard.PrimaryDevice, source, 0, Key.RightCtrl)
        {
            RoutedEvent = Keyboard.PreviewKeyDownEvent,
        };
        textBox.RaiseEvent(extendedArgs);
        Assert(viewModel.NewVirtualKeyText == "163", "Pressing right Ctrl should capture VK 163.");
        Assert(textBox.Text == "右 Ctrl", "The keyboard input field should show the captured right Ctrl key name.");
        Assert(viewModel.NewKeyIsExtended, "Right Ctrl should automatically enable extended-key mode.");
    }

    private static void VerifyWindowChrome(
        Window window,
        Button minimizeWindow,
        Button maximizeRestoreWindow,
        Button closeWindow)
    {
        var chrome = WindowChrome.GetWindowChrome(window)
            ?? throw new InvalidOperationException("Main window does not expose custom WindowChrome.");
        Assert(window.WindowStyle == WindowStyle.None, "Razer-style title bar must replace the native Windows frame.");
        Assert(chrome.CaptionHeight == 40, "Custom title bar caption height is incorrect.");
        Assert(chrome.ResizeBorderThickness == new Thickness(6), "Custom title bar resize border is incorrect.");
        Assert(!chrome.UseAeroCaptionButtons, "Custom title bar should use application-owned caption buttons.");
        foreach (var button in new[] { minimizeWindow, maximizeRestoreWindow, closeWindow })
        {
            Assert(WindowChrome.GetIsHitTestVisibleInChrome(button), $"{button.Name} is not clickable inside WindowChrome.");
            Assert(!button.IsTabStop && !button.Focusable, $"{button.Name} should not enter the workspace Tab order.");
            Assert(!string.IsNullOrWhiteSpace(AutomationProperties.GetName(button)), $"{button.Name} has no accessibility name.");
            Assert(button.ActualWidth == 46 && button.ActualHeight == 40, $"{button.Name} does not match the Razer-style caption size.");
        }
    }

    private static void VerifyGreenHighlightForeground(System.Windows.Application application, params Button[] buttons)
    {
        var expected = GetColor(application, "RazerGreenTextBrush");
        foreach (var button in buttons)
        {
            Assert(button.Foreground is SolidColorBrush brush && brush.Color == expected, $"{button.Name} should use dark text on green highlight buttons.");
            button.ApplyTemplate();
            button.UpdateLayout();
            foreach (var presenter in Descendants(button).OfType<ContentPresenter>())
            {
                var inherited = TextElement.GetForeground(presenter);
                Assert(
                    inherited is SolidColorBrush inheritedBrush && inheritedBrush.Color == expected,
                    $"{button.Name} content presenter did not inherit the dark green-highlight text color.");
            }

            foreach (var textBlock in Descendants(button).OfType<TextBlock>())
            {
                Assert(
                    textBlock.Foreground is SolidColorBrush textBrush && textBrush.Color == expected,
                    $"{button.Name} rendered text is not dark on a green highlight.");
            }
        }
    }

    private static void VerifyNormalTextForeground(System.Windows.Application application, Window window)
    {
        var expected = GetColor(application, "TextPrimaryBrush");
        var normalText = Descendants(window)
            .OfType<TextBlock>()
            .FirstOrDefault(item => item.Text == "宏转换工作区" && Math.Abs(item.FontSize - 17) < 0.1)
            ?? throw new InvalidOperationException("Normal workspace title text was not rendered.");
        Assert(
            normalText.Foreground is SolidColorBrush brush && brush.Color == expected,
            "Normal text should keep the primary text brush; only green-highlighted controls should use dark text.");
    }

    private static void VerifyTitleBarNavigationStability(
        Window window,
        Button macroNavigation,
        Button libraryNavigation,
        Button settingsNavigation)
    {
        var buttons = new[] { macroNavigation, libraryNavigation, settingsNavigation };
        window.UpdateLayout();
        var baseline = Capture();
        AssertBottomsAligned();

        foreach (var navigationButton in buttons)
        {
            navigationButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            window.UpdateLayout();
            var current = Capture();
            Assert(buttons.Count(button => Equals(button.Tag, "Active")) == 1, "Title bar navigation should expose exactly one active tab.");
            AssertBottomsAligned();

            for (var index = 0; index < buttons.Length; index++)
            {
                AssertStable(current[index].X, baseline[index].X, buttons[index].Name, "X");
                AssertStable(current[index].Width, baseline[index].Width, buttons[index].Name, "width");
                AssertStable(current[index].Height, baseline[index].Height, buttons[index].Name, "height");
            }
        }

        macroNavigation.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        window.UpdateLayout();

        (double X, double Width, double Height, double Bottom)[] Capture() =>
            buttons
                .Select(button =>
                {
                    var origin = button.TranslatePoint(new Point(0, 0), window);
                    var bottom = button.TranslatePoint(new Point(0, button.ActualHeight), window).Y;
                    return (origin.X, button.ActualWidth, button.ActualHeight, bottom);
                })
                .ToArray();

        void AssertStable(double actual, double expected, string name, string dimension) =>
            Assert(
                Math.Abs(actual - expected) <= 0.5,
                $"{name} navigation {dimension} shifted during tab switching: {actual:F1} != {expected:F1}.");

        void AssertBottomsAligned()
        {
            var bottoms = Capture().Select(item => item.Bottom).ToArray();
            Assert(
                bottoms.Max() - bottoms.Min() <= 0.5,
                $"Title bar navigation tabs do not share a bottom edge ({string.Join(", ", bottoms.Select(value => value.ToString("F1")))})." );
        }
    }

    private static void VerifyControlAlignment(
        Window window,
        FrameworkElement currentMacro,
        ComboBox targetFormat,
        Button exportButton,
        TextBox newDelay,
        Button insertDelay,
        TextBox virtualKey,
        ComboBox keyTransition,
        Button insertKey,
        ComboBox severityFilter,
        ComboBox scopeFilter)
    {
        AssertBottomAligned(window, "header actions", currentMacro, targetFormat, exportButton);
        AssertBottomAligned(window, "delay toolbar", newDelay, insertDelay);
        AssertBottomAligned(window, "keyboard toolbar", virtualKey, keyTransition, insertKey);
        AssertBottomAligned(window, "diagnostic filters", severityFilter, scopeFilter);
        foreach (var control in new FrameworkElement[]
                 {
                     currentMacro,
                     targetFormat,
                     exportButton,
                     newDelay,
                     insertDelay,
                     virtualKey,
                     keyTransition,
                     insertKey,
                     severityFilter,
                     scopeFilter,
                 })
        {
            Assert(Math.Abs(control.ActualHeight - 32) <= 0.5, $"{control.Name} is not aligned to the 32-DIP control height.");
        }
    }

    private static void AssertBottomAligned(Window window, string groupName, params FrameworkElement[] elements)
    {
        var bottoms = elements
            .Select(element => element.TranslatePoint(new Point(0, element.ActualHeight), window).Y)
            .ToArray();
        Assert(
            bottoms.Max() - bottoms.Min() <= 1,
            $"{groupName} controls do not share a bottom baseline ({string.Join(", ", bottoms.Select(value => value.ToString("F1")))})." );
    }

    private static void VerifyHeaderFieldAlignment(
        System.Windows.Application application,
        Window window,
        WorkspaceViewModel viewModel,
        TextBlock currentMacroLabel,
        Border currentMacroHost,
        TextBlock currentMacroValue,
        TextBlock targetFormatLabel,
        ComboBox targetFormat,
        ComboBox ordinaryComboBox)
    {
        Assert(currentMacroLabel.HorizontalAlignment == HorizontalAlignment.Center, "Current macro label is not centered in its field column.");
        Assert(targetFormatLabel.HorizontalAlignment == HorizontalAlignment.Center, "Target format label is not centered in its field column.");
        Assert(currentMacroValue.HorizontalAlignment == HorizontalAlignment.Center && currentMacroValue.TextAlignment == TextAlignment.Center,
            "Current macro value is not centered in its read-only field.");
        Assert(targetFormat.HorizontalContentAlignment == HorizontalAlignment.Center,
            "Target format selected value is not centered.");
        Assert(ordinaryComboBox.HorizontalContentAlignment == HorizontalAlignment.Left,
            "Ordinary combo boxes should retain their default left alignment.");

        var controlColor = GetColor(application, "ControlBrush");
        var borderColor = GetColor(application, "BorderBrush");
        Assert(currentMacroHost.Background is SolidColorBrush background && background.Color == controlColor,
            "Current macro field does not use the standard control surface.");
        Assert(currentMacroHost.BorderBrush is SolidColorBrush border && border.Color == borderColor && currentMacroHost.BorderThickness.Left == 1,
            "Current macro field does not use the standard weak border.");
        Assert(currentMacroHost.CornerRadius == new CornerRadius(3), "Current macro field corner radius is inconsistent with other controls.");
        Assert(currentMacroValue.TextTrimming == TextTrimming.CharacterEllipsis,
            "Current macro field must trim long names with an ellipsis.");
        Assert(Equals(currentMacroValue.ToolTip, viewModel.SelectedMacro?.Name),
            "Current macro field must retain the full selected name in its tooltip.");

        targetFormat.ApplyTemplate();
        ordinaryComboBox.ApplyTemplate();
        window.UpdateLayout();
        var targetValueText = Descendants(targetFormat).OfType<TextBlock>()
            .FirstOrDefault(item => item.Text == targetFormat.Text)
            ?? throw new InvalidOperationException("Target format selected-value text was not rendered.");
        var ordinaryValueText = Descendants(ordinaryComboBox).OfType<TextBlock>()
            .FirstOrDefault(item => item.Text == ordinaryComboBox.Text)
            ?? throw new InvalidOperationException("Ordinary combo-box selected-value text was not rendered.");
        Assert(targetValueText.HorizontalAlignment == HorizontalAlignment.Center,
            "Target format template did not apply centered content alignment.");
        Assert(ordinaryValueText.HorizontalAlignment == HorizontalAlignment.Left,
            "Combo-box template changed ordinary selected values away from left alignment.");
    }

    private static void VerifyParameterInputAlignment(params TextBox[] inputs)
    {
        foreach (var input in inputs)
        {
            Assert(input.HorizontalContentAlignment == HorizontalAlignment.Center, $"{input.Name} parameter content is not horizontally centered.");
            Assert(input.VerticalContentAlignment == VerticalAlignment.Center, $"{input.Name} parameter content is not vertically centered.");
            Assert(input.TextAlignment == TextAlignment.Center, $"{input.Name} parameter text is not centered.");
        }
    }

    private static void VerifyMacroTextDialog(Window owner)
    {
        var dialog = new XMacroBridge.App.MacroTextInputDialog
        {
            Owner = owner,
            ShowInTaskbar = false,
            Left = -20_000,
            Top = -20_000,
        };
        try
        {
            dialog.Show();
            var textBox = Find<TextBox>(dialog, "MacroTextBox");
            var importButton = Find<Button>(dialog, "ImportButton");
            Assert(!importButton.IsEnabled, "Blank macro text should not be submittable.");
            textBox.Text = "e{WAITMS:10}{LMB}";
            Assert(importButton.IsEnabled, "Non-empty macro text should enable import.");
            Assert(textBox.TextWrapping == TextWrapping.Wrap, "Long macro text should wrap inside the input box.");
            Assert(
                ScrollViewer.GetHorizontalScrollBarVisibility(textBox) == ScrollBarVisibility.Disabled,
                "Macro text input should not require horizontal scrolling.");
            var dialogText = Descendants(dialog).OfType<TextBlock>().Select(item => item.Text).ToArray();
            Assert(!dialogText.Any(text => text.StartsWith("示例：", StringComparison.Ordinal)), "Macro text dialog should not show the removed example text.");
            Assert(!dialogText.Contains("文本仅在本机内存中解析，不会上传。", StringComparer.Ordinal), "Macro text dialog should not show the removed local-processing note.");
            VerifyAccessibilityNames(textBox, importButton);
            Assert(textBox.TabIndex == 0 && importButton.TabIndex == 2, "Macro text dialog keyboard order is incorrect.");
        }
        finally
        {
            dialog.Close();
        }

        var editDialog = new XMacroBridge.App.MacroTextInputDialog("e{WAITMS:10}{LMB}", isEdit: true)
        {
            Owner = owner,
            ShowInTaskbar = false,
            Left = -20_000,
            Top = -20_000,
        };
        try
        {
            editDialog.Show();
            var textBox = Find<TextBox>(editDialog, "MacroTextBox");
            var applyButton = Find<Button>(editDialog, "ImportButton");
            Assert(textBox.Text == "e{WAITMS:10}{LMB}", "Macro text edit dialog did not preload the current text.");
            Assert(string.Equals(applyButton.Content?.ToString(), "应用修改", StringComparison.Ordinal), "Macro text edit dialog action label is incorrect.");
        }
        finally
        {
            editDialog.Close();
        }
    }

    private static void VerifyMacroLibraryEntryDialog(Window owner)
    {
        var dialog = new XMacroBridge.App.MacroLibraryEntryDialog(
            "新建 XMouse 宏",
            "测试宏",
            ["测试分组"],
            "测试分组",
            showText: true,
            initialText: "e{WAITMS:10}{LMB}")
        {
            Owner = owner,
            ShowInTaskbar = false,
            Left = -20_000,
            Top = -20_000,
        };
        try
        {
            dialog.Show();
            dialog.UpdateLayout();
            var textBox = Find<TextBox>(dialog, "MacroTextBox");
            Assert(textBox.ActualHeight >= 280, $"Macro library text editor is still too short ({textBox.ActualHeight:F1}).");
            Assert(textBox.TextWrapping == TextWrapping.Wrap, "Macro library text editor should wrap long macros.");
            Assert(
                ScrollViewer.GetHorizontalScrollBarVisibility(textBox) == ScrollBarVisibility.Disabled,
                "Macro library text editor should not require horizontal scrolling.");
            Assert(dialog.FindResource("StarFilledIconGeometry") is Geometry, "Tabler filled-star geometry is unavailable.");
        }
        finally
        {
            dialog.Close();
        }
    }

    private static void VerifyContextMenuChrome(
        ContextMenu contextMenu,
        FrameworkElement placementTarget)
    {
        contextMenu.PlacementTarget ??= placementTarget;
        contextMenu.IsOpen = true;
        try
        {
            contextMenu.ApplyTemplate();
            contextMenu.UpdateLayout();
            contextMenu.Dispatcher.Invoke(() => { }, DispatcherPriority.ApplicationIdle);

            var descendants = Enumerable
                .Repeat<DependencyObject>(contextMenu, 1)
                .Concat(Descendants(contextMenu))
                .ToArray();
            Assert(
                !descendants.OfType<ScrollViewer>().Any(),
                "Context menus should not render the default WPF scroll-viewer chrome.");

            var lightSurfaces = descendants
                .SelectMany(GetSurfaceColors)
                .Where(item => item.Color.A > 32 && RelativeLuminance(item.Color) > 0.82)
                .Select(item => $"{item.Element} {item.Color}")
                .ToArray();
            Assert(
                lightSurfaces.Length == 0,
                $"Context menu leaked light system chrome: {string.Join(", ", lightSurfaces)}.");
        }
        finally
        {
            contextMenu.IsOpen = false;
        }
    }

    private static IEnumerable<(string Element, Color Color)> GetSurfaceColors(DependencyObject element)
    {
        Brush? brush = element switch
        {
            ContextMenu menu => menu.Background,
            MenuItem menuItem => menuItem.Background,
            Border border => border.Background,
            Panel panel => panel.Background,
            System.Windows.Shapes.Shape shape => shape.Fill,
            _ => null,
        };
        if (brush is SolidColorBrush solid)
        {
            yield return (element.GetType().Name, solid.Color);
        }
    }

    private static void VerifyMacroRenameUi(Window owner, ListBox macroList)
    {
        Assert(macroList.ContextMenu is { Items.Count: 3 }, "Macro list should expose rename, text-edit and delete actions.");
        var renameMenuItem = macroList.ContextMenu.Items[0] as MenuItem
            ?? throw new InvalidOperationException("Macro context action is not a menu item.");
        Assert(string.Equals(renameMenuItem.Header?.ToString(), "重命名…", StringComparison.Ordinal), "Macro rename menu text is incorrect.");
        Assert(!string.IsNullOrWhiteSpace(AutomationProperties.GetName(renameMenuItem)), "Macro rename menu has no accessibility name.");
        var editTextMenuItem = macroList.ContextMenu.Items[1] as MenuItem
            ?? throw new InvalidOperationException("Macro text-edit context action is not a menu item.");
        Assert(string.Equals(editTextMenuItem.Header?.ToString(), "修改宏文本…", StringComparison.Ordinal), "Macro text-edit menu text is incorrect.");
        Assert(!string.IsNullOrWhiteSpace(AutomationProperties.GetName(editTextMenuItem)), "Macro text-edit menu has no accessibility name.");
        var deleteMenuItem = macroList.ContextMenu.Items[2] as MenuItem
            ?? throw new InvalidOperationException("Macro delete context action is not a menu item.");
        Assert(string.Equals(deleteMenuItem.Header?.ToString(), "删除", StringComparison.Ordinal), "Macro delete menu text is incorrect.");
        Assert(!string.IsNullOrWhiteSpace(AutomationProperties.GetName(deleteMenuItem)), "Macro delete menu has no accessibility name.");
        Assert(!macroList.ContextMenu.HasDropShadow, "Macro context menu should not expose the system popup shadow gutter.");
        Assert(
            ScrollViewer.GetVerticalScrollBarVisibility(macroList.ContextMenu) == ScrollBarVisibility.Disabled &&
            ScrollViewer.GetHorizontalScrollBarVisibility(macroList.ContextMenu) == ScrollBarVisibility.Disabled,
            "Macro context menu should not expose scroll gutters for its two fixed actions.");
        VerifyContextMenuChrome(macroList.ContextMenu, macroList);

        var dynamicMenu = new ContextMenu();
        dynamicMenu.Items.Add(new MenuItem { Header = "重命名" });
        dynamicMenu.Items.Add(new MenuItem { Header = "移动到：未分组" });
        try
        {
            VerifyContextMenuChrome(dynamicMenu, macroList);
        }
        finally
        {
            dynamicMenu.IsOpen = false;
        }

        var dialog = new XMacroBridge.App.MacroRenameDialog("旧名称")
        {
            Owner = owner,
            ShowInTaskbar = false,
            Left = -20_000,
            Top = -20_000,
        };
        try
        {
            dialog.Show();
            var nameTextBox = Find<TextBox>(dialog, "MacroNameTextBox");
            var renameButton = Find<Button>(dialog, "RenameButton");
            Assert(nameTextBox.Text == "旧名称" && renameButton.IsEnabled, "Rename dialog did not populate the current macro name.");
            nameTextBox.Text = "   ";
            Assert(!renameButton.IsEnabled, "Rename dialog should reject a blank name.");
            nameTextBox.Text = "新名称";
            Assert(renameButton.IsEnabled, "Rename dialog should accept a non-empty name.");
            VerifyAccessibilityNames(nameTextBox, renameButton);
        }
        finally
        {
            dialog.Close();
        }
    }

    private static void VerifyTimelineMultiSelectionUi(
        XMacroBridge.App.MainWindow window,
        WorkspaceViewModel viewModel,
        DataGrid eventTimeline)
    {
        eventTimeline.SelectedItems.Clear();
        var addRowToSelection = typeof(XMacroBridge.App.MainWindow).GetMethod(
            "AddTimelineRowToSelection",
            BindingFlags.Instance | BindingFlags.NonPublic)
            ?? throw new InvalidOperationException("Timeline additive click selection handler is absent.");
        var firstRow = eventTimeline.ItemContainerGenerator.ContainerFromIndex(0) as DataGridRow
            ?? throw new InvalidOperationException("First timeline row was not realized for multi-selection UI checks.");
        var secondRow = eventTimeline.ItemContainerGenerator.ContainerFromIndex(1) as DataGridRow
            ?? throw new InvalidOperationException("Second timeline row was not realized for multi-selection UI checks.");
        _ = addRowToSelection.Invoke(window, [firstRow]);
        _ = addRowToSelection.Invoke(window, [secondRow]);
        eventTimeline.UpdateLayout();
        Assert(eventTimeline.SelectedItems.Count == 2, "Repeated plain row clicks must accumulate timeline selection.");
        Assert(viewModel.SelectedEventCount == 2, "Additive row selection did not synchronize with the workspace.");
        Assert(viewModel.CanCopyEvent && viewModel.CanDeleteEvent, "Multi-selection did not enable copy and delete actions.");

        var checkBox = Descendants(firstRow).OfType<CheckBox>().FirstOrDefault()
            ?? throw new InvalidOperationException("Timeline row does not expose a selection checkbox.");
        Assert(AutomationProperties.GetName(checkBox) == "选择时间线事件", "Timeline selection checkbox has an incorrect accessibility name.");
        Assert(Descendants(firstRow).OfType<Button>().Count() >= 2, "Timeline row does not expose inline copy and delete actions.");
        var actions = Descendants(firstRow)
            .OfType<StackPanel>()
            .SingleOrDefault(panel => Equals(panel.Tag, "TimelineActions"))
            ?? throw new InvalidOperationException("Timeline row action panel is absent.");
        actions.Visibility = Visibility.Visible;
        eventTimeline.UpdateLayout();
        var actionRight = actions.TranslatePoint(new Point(actions.ActualWidth, 0), eventTimeline).X;
        Assert(
            actionRight <= eventTimeline.ActualWidth - 8,
            $"Timeline actions overflow the right safe area: {actionRight:F1} > {eventTimeline.ActualWidth - 8:F1}.");
        var dragHandle = Descendants(firstRow)
            .OfType<Border>()
            .SingleOrDefault(border => Equals(border.Tag, "TimelineDragHandle"));
        Assert(dragHandle?.Cursor == Cursors.SizeAll, "Timeline drag handle is absent or not draggable.");

        var updateDropIndicator = typeof(XMacroBridge.App.MainWindow).GetMethod(
            "UpdateTimelineDropIndicator",
            BindingFlags.Instance | BindingFlags.NonPublic)
            ?? throw new InvalidOperationException("Timeline drop indicator handler is absent.");
        var showDragFeedback = typeof(XMacroBridge.App.MainWindow).GetMethod(
            "ShowTimelineDragFeedback",
            BindingFlags.Instance | BindingFlags.NonPublic)
            ?? throw new InvalidOperationException("Timeline floating drag feedback handler is absent.");
        var clearDragFeedback = typeof(XMacroBridge.App.MainWindow).GetMethod(
            "ClearTimelineDragFeedback",
            BindingFlags.Instance | BindingFlags.NonPublic)
            ?? throw new InvalidOperationException("Timeline drag feedback cleanup handler is absent.");
        var feedbackPopup = Find<Popup>(window, "TimelineDragFeedbackPopup");
        var feedbackText = Find<TextBlock>(window, "TimelineDragFeedbackTextBlock");

        _ = updateDropIndicator.Invoke(window, [firstRow, false]);
        eventTimeline.UpdateLayout();
        Assert(Equals(firstRow.Tag, "TimelineInsertBefore"), "Drag target did not expose a before-insertion marker.");
        Assert(firstRow.BorderThickness.Top >= 2, "Before-insertion marker did not render a visible top line.");

        _ = updateDropIndicator.Invoke(window, [firstRow, true]);
        eventTimeline.UpdateLayout();
        Assert(Equals(firstRow.Tag, "TimelineInsertAfter"), "Drag target did not expose an after-insertion marker.");
        Assert(firstRow.BorderThickness.Bottom >= 2, "After-insertion marker did not render a visible bottom line.");

        _ = showDragFeedback.Invoke(window, [2, new Point(80, 60)]);
        Assert(feedbackPopup.IsOpen, "Timeline floating drag feedback did not open.");
        Assert(feedbackText.Text == "移动 2 个操作", "Timeline floating drag feedback count is incorrect.");
        Assert(feedbackPopup.HorizontalOffset == 98 && feedbackPopup.VerticalOffset == 78, "Timeline floating drag feedback does not follow the pointer offset.");

        _ = clearDragFeedback.Invoke(window, null);
        Assert(!feedbackPopup.IsOpen && firstRow.Tag is null, "Timeline drag feedback was not fully cleared.");

        eventTimeline.SelectedItems.Clear();
        viewModel.SetSelectedEventIndices([]);
    }

    private static void VerifyTimelineResponsiveResize(
        Window window,
        WorkspaceViewModel viewModel,
        DataGrid eventTimeline)
    {
        window.Width = 1265;
        window.UpdateLayout();
        window.Width = 900;
        window.UpdateLayout();
        eventTimeline.ScrollIntoView(viewModel.Events[0]);
        eventTimeline.UpdateLayout();

        var expectedMaximum = eventTimeline.ActualWidth - SystemParameters.VerticalScrollBarWidth;
        Assert(eventTimeline.Columns[0].ActualWidth <= expectedMaximum, "Timeline column retained its maximized-window width after shrinking.");
        Assert(eventTimeline.Columns[0].ActualWidth >= 400, "Timeline column collapsed after shrinking the window.");

        var firstRow = eventTimeline.ItemContainerGenerator.ContainerFromIndex(0) as DataGridRow
            ?? throw new InvalidOperationException("First timeline row was not realized after responsive resize.");
        var eventLabel = Descendants(firstRow)
            .OfType<TextBlock>()
            .FirstOrDefault(text => string.Equals(text.Text, viewModel.Events[0].EditorLabel, StringComparison.Ordinal));
        Assert(eventLabel is { IsVisible: true, ActualWidth: > 0 }, "Timeline event content disappeared after shrinking the window.");

        var scrollViewer = Descendants(eventTimeline).OfType<ScrollViewer>().FirstOrDefault()
            ?? throw new InvalidOperationException("Timeline scroll viewer was not realized after responsive resize.");
        Assert(scrollViewer.HorizontalOffset == 0, "Timeline retained a hidden horizontal offset after shrinking.");
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

    private static void VerifyParameterizedInsertionBindings(
        WorkspaceViewModel viewModel,
        Window window,
        TextBox virtualKey,
        ComboBox keyTransition,
        CheckBox extendedKey,
        Button insertKeyEvent,
        ComboBox mouseButton,
        ComboBox mouseTransition,
        Button insertMouseEvent)
    {
        var originalEventCount = viewModel.SelectedMacro!.Events.Count;
        var source = PresentationSource.FromVisual(window)
            ?? throw new InvalidOperationException("Main window has no presentation source for keyboard insertion testing.");
        virtualKey.RaiseEvent(new KeyEventArgs(Keyboard.PrimaryDevice, source, 0, Key.B)
        {
            RoutedEvent = Keyboard.PreviewKeyDownEvent,
        });
        Assert(virtualKey.Text == "B" && viewModel.NewVirtualKeyText == "66",
            "Keyboard insertion input did not capture B as VK 66.");
        extendedKey.IsChecked = true;
        keyTransition.SelectedItem = viewModel.InputTransitionOptions.Single(item => item.Transition == InputTransition.Down);
        insertKeyEvent.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        window.UpdateLayout();
        Assert(
            viewModel.SelectedEvent?.Event is KeyMacroEvent
            {
                VirtualKey: 66,
                Transition: InputTransition.Down,
                IsExtended: true,
            },
            "Keyboard insertion controls did not create the requested key-down event.");
        Assert(!viewModel.CanExport, "Inserted unpaired key-down event should block export.");

        keyTransition.SelectedItem = viewModel.InputTransitionOptions.Single(item => item.Transition == InputTransition.Up);
        insertKeyEvent.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        window.UpdateLayout();
        Assert(viewModel.CanExport, "Inserted key-up event should balance the key-down event.");

        mouseButton.SelectedItem = viewModel.MouseButtonOptions.Single(item => item.Button == MacroMouseButton.XButton2);
        mouseTransition.SelectedItem = viewModel.InputTransitionOptions.Single(item => item.Transition == InputTransition.Down);
        insertMouseEvent.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        window.UpdateLayout();
        Assert(
            viewModel.SelectedEvent?.Event is MouseMacroEvent
            {
                Button: MacroMouseButton.XButton2,
                Transition: InputTransition.Down,
            },
            "Mouse insertion controls did not create the requested mouse-down event.");
        Assert(!viewModel.CanExport, "Inserted unpaired mouse-down event should block export.");

        mouseTransition.SelectedItem = viewModel.InputTransitionOptions.Single(item => item.Transition == InputTransition.Up);
        insertMouseEvent.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        window.UpdateLayout();
        Assert(viewModel.CanExport, "Inserted mouse-up event should balance the mouse-down event.");
        Assert(viewModel.SelectedMacro.Events.Count == originalEventCount + 4, "Parameterized insertion did not add four events.");
    }

    private static void VerifySelectedEventReplacement(WorkspaceViewModel viewModel, Window window)
    {
        viewModel.SelectedEvent = viewModel.Events.First(item => item.Event is KeyMacroEvent);
        var originalKey = (KeyMacroEvent)viewModel.SelectedEvent.Event;
        Assert(viewModel.CanEditSelectedEvent, "Selected keyboard event should be editable.");
        Assert(
            viewModel.UpdateSelectedEvent(originalKey with { VirtualKey = originalKey.VirtualKey == 67 ? 68 : 67 }),
            "Keyboard replacement edit failed.");
        window.UpdateLayout();
        Assert(viewModel.SelectedEvent?.Event is KeyMacroEvent { VirtualKey: 67 } or KeyMacroEvent { VirtualKey: 68 }, "Keyboard replacement was not selected.");
        Assert(viewModel.Undo(), "Keyboard replacement edit was not undoable.");

        viewModel.SelectedEvent = viewModel.Events.First(item => item.Event is MouseMacroEvent);
        var originalMouse = (MouseMacroEvent)viewModel.SelectedEvent.Event;
        var replacementButton = originalMouse.Button == MacroMouseButton.Right ? MacroMouseButton.Left : MacroMouseButton.Right;
        Assert(
            viewModel.UpdateSelectedEvent(originalMouse with { Button = replacementButton }),
            "Mouse replacement edit failed.");
        window.UpdateLayout();
        Assert(viewModel.SelectedEvent?.Event is MouseMacroEvent { Button: var button } && button == replacementButton, "Mouse replacement was not selected.");
        Assert(viewModel.Undo(), "Mouse replacement edit was not undoable.");
    }

    private static void VerifyEventEditDialog(Window owner)
    {
        var delayDialog = new XMacroBridge.App.EventEditDialog(new DelayMacroEvent(3, 250))
        {
            Owner = owner,
            ShowInTaskbar = false,
            Left = -20_000,
            Top = -20_000,
        };
        try
        {
            delayDialog.Show();
            var delay = Find<TextBox>(delayDialog, "DelayTextBox");
            Assert(delay.Text == "250", "Event editor did not preload the delay value.");
            VerifyParameterInputAlignment(delay);
            Assert(delayDialog.Background is SolidColorBrush, "Event editor did not inherit the dark window background.");
            Assert(delayDialog.Icon is not null, "Event editor did not inherit the application icon.");
            delay.Text = "-1";
            Find<Button>(delayDialog, "SaveButton").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            var validationPanel = Find<Grid>(delayDialog, "ValidationPanel");
            Assert(validationPanel.Visibility == Visibility.Visible, "Invalid event input did not show inline validation.");
            Assert(AutomationProperties.GetLiveSetting(validationPanel) == AutomationLiveSetting.Assertive, "Event validation is not exposed as an assertive live region.");
        }
        finally
        {
            delayDialog.Close();
        }

        var keyDialog = new XMacroBridge.App.EventEditDialog(new KeyMacroEvent(4, 69, InputTransition.Down))
        {
            Owner = owner,
            ShowInTaskbar = false,
            Left = -20_000,
            Top = -20_000,
        };
        try
        {
            keyDialog.Show();
            keyDialog.UpdateLayout();
            var keyTextBox = Find<TextBox>(keyDialog, "VirtualKeyTextBox");
            var extendedKey = Find<CheckBox>(keyDialog, "ExtendedKeyCheckBox");
            Assert(keyTextBox.Text == "E", "Keyboard event editor should preload a readable key name.");
            Assert(keyTextBox.IsReadOnly, "Keyboard event editor should capture keys instead of accepting VK text.");
            Assert(extendedKey.IsVisible && extendedKey.ActualHeight > 0, "Keyboard event editor content is clipped before the extended-key option.");
            Assert(keyDialog.SizeToContent == SizeToContent.Height, "Keyboard event editor should size itself to its full content.");

            var source = PresentationSource.FromVisual(keyDialog)
                ?? throw new InvalidOperationException("Event editor has no presentation source for key capture testing.");
            var altArgs = new KeyEventArgs(Keyboard.PrimaryDevice, source, 0, Key.LeftAlt)
            {
                RoutedEvent = Keyboard.PreviewKeyDownEvent,
            };
            keyTextBox.RaiseEvent(altArgs);
            Assert(altArgs.Handled, "Keyboard event editor should consume captured Alt input.");
            Assert(keyTextBox.Text == "左 Alt", "Keyboard event editor should display Alt instead of its numeric VK code.");
            Assert(extendedKey.IsChecked != true, "Left Alt should not be marked as an extended key.");
            Assert(keyDialog.CapturedVirtualKey == 164,
                "Keyboard event editor did not retain Alt as backend VK 164.");
        }
        finally
        {
            keyDialog.Close();
        }
    }

    private static void VerifyEventSearchBindings(
        WorkspaceViewModel viewModel,
        Window window,
        TextBox eventSearch,
        Button findPreviousEvent,
        Button findNextEvent,
        TextBlock eventSearchResult)
    {
        viewModel.EventSearchText = "键盘";
        window.UpdateLayout();
        Assert(eventSearch.Text == "键盘", "Event search text binding did not refresh.");
        Assert(findPreviousEvent.IsEnabled && findNextEvent.IsEnabled, "Matching event search did not enable navigation.");
        Assert(eventSearchResult.Text.StartsWith("0 / ", StringComparison.Ordinal), "Event search count is incorrect before navigation.");
        var workspaceScroll = Find<ScrollViewer>((FrameworkElement)window, "WorkspaceScrollViewer");
        var searchResultRight = eventSearchResult.TranslatePoint(new Point(eventSearchResult.ActualWidth, 0), workspaceScroll).X;
        Assert(searchResultRight <= workspaceScroll.ViewportWidth + 0.5, "Visible event-search status is clipped in the compact workspace.");

        findNextEvent.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        window.UpdateLayout();
        Assert(viewModel.SelectedEvent?.Type.StartsWith("键盘", StringComparison.Ordinal) == true, "Next-result button did not select a matching event.");
        Assert(eventSearchResult.Text.StartsWith("1 / ", StringComparison.Ordinal), "Event search position did not update after navigation.");

        findPreviousEvent.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        window.UpdateLayout();
        Assert(viewModel.SelectedEvent?.Type.StartsWith("键盘", StringComparison.Ordinal) == true, "Previous-result button did not preserve a matching selection.");
        Assert(
            AutomationProperties.GetName(eventSearchResult) == "时间线搜索结果计数",
            "Event search result UIA name must remain static and exclude the search query.");

        var source = PresentationSource.FromVisual(window)
            ?? throw new InvalidOperationException("Workspace has no presentation source for search-key testing.");
        var enterArgs = new KeyEventArgs(Keyboard.PrimaryDevice, source, 0, Key.Enter)
        {
            RoutedEvent = Keyboard.PreviewKeyDownEvent,
        };
        eventSearch.RaiseEvent(enterArgs);
        Assert(enterArgs.Handled, "Enter should navigate to the next event-search result.");

        var escapeArgs = new KeyEventArgs(Keyboard.PrimaryDevice, source, 0, Key.Escape)
        {
            RoutedEvent = Keyboard.PreviewKeyDownEvent,
        };
        eventSearch.RaiseEvent(escapeArgs);
        Assert(escapeArgs.Handled && string.IsNullOrEmpty(viewModel.EventSearchText), "Escape should clear the event search.");

        window.UpdateLayout();
        Assert(eventSearchResult.Text == "未搜索", "Clearing event search did not restore the idle summary.");
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

    private static void VerifyTimelineScrollBar(DataGrid eventTimeline)
    {
        eventTimeline.UpdateLayout();
        var scrollViewer = Descendants(eventTimeline).OfType<ScrollViewer>().FirstOrDefault()
            ?? throw new InvalidOperationException("Timeline scroll viewer was not realized.");
        var scrollBar = Descendants(scrollViewer)
            .OfType<ScrollBar>()
            .FirstOrDefault(item => item.Orientation == Orientation.Vertical && item.IsVisible)
            ?? throw new InvalidOperationException("Timeline vertical scroll bar was not realized.");
        var track = Descendants(scrollBar).OfType<Track>().FirstOrDefault()
            ?? throw new InvalidOperationException("Timeline vertical scroll track was not realized.");
        var thumb = Descendants(scrollBar).OfType<Thumb>().FirstOrDefault()
            ?? throw new InvalidOperationException("Timeline vertical scroll thumb was not realized.");

        Assert(scrollBar.ActualWidth >= 12, "Timeline vertical scroll bar is too narrow to see or operate reliably.");
        Assert(scrollBar.ActualHeight > eventTimeline.ActualHeight / 2, "Timeline vertical scroll bar does not span the list height.");
        Assert(double.IsNaN(track.ViewportSize), "Timeline scroll track must use a fixed-size thumb in compact layouts.");
        Assert(thumb.Height == 48 && thumb.ActualHeight >= 47, "Timeline vertical scroll thumb is not fully visible at its fixed size.");

        var target = Math.Max(1, scrollBar.Maximum / 2);
        track.Value = target;
        eventTimeline.UpdateLayout();
        Assert(Math.Abs(scrollBar.Value - target) < 0.5, "Timeline scroll track is not bound to the scroll bar value.");
        scrollViewer.ScrollToVerticalOffset(target);
        eventTimeline.UpdateLayout();
        Assert(scrollViewer.VerticalOffset > 0, "Timeline scroll viewer did not move to a non-zero offset.");
        scrollViewer.ScrollToTop();
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

    private static void VerifyTheme(System.Windows.Application application)
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
        var navigation = GetColor(application, "NavigationBrush");
        var editor = GetColor(application, "PanelBrush");
        var control = GetColor(application, "ControlBrush");
        var brandGreen = GetColor(application, "RazerGreenBrush");

        Assert(page == Parse("#FF202020"), "Fixed Razer page color is incorrect.");
        Assert(navigation == Parse("#FF090909"), "Branded navigation color is incorrect.");
        Assert(editor == Parse("#FF101010"), "Branded editor color is incorrect.");
        Assert(control == Parse("#FF242424"), "Branded control color is incorrect.");
        Assert(brandGreen == Parse("#FF36E322"), "Razer green color is incorrect.");

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

    private static void VerifyTablerIconResources(System.Windows.Application application)
    {
        var geometryKeys = new[]
        {
            "KeyboardIconGeometry",
            "MouseIconGeometry",
            "ClockIconGeometry",
            "WarningIconGeometry",
            "InfoIconGeometry",
            "ErrorIconGeometry",
            "CopyIconGeometry",
            "DeleteIconGeometry",
            "DragHandleGeometry",
            "ChevronRightIconGeometry",
            "ChevronDownIconGeometry",
            "CheckIconGeometry",
            "StackIconGeometry",
            "SettingsIconGeometry",
        };

        foreach (var key in geometryKeys)
        {
            Assert(application.TryFindResource(key) is Geometry, $"Tabler icon resource {key} is missing.");
        }

        var iconStyle = application.TryFindResource("TablerIconPathStyle") as Style;
        Assert(iconStyle is not null && iconStyle.TargetType == typeof(System.Windows.Shapes.Path), "Tabler icon path style is missing or invalid.");
        Assert(iconStyle!.Setters.OfType<Setter>().Any(item => item.Property == System.Windows.Shapes.Shape.StrokeThicknessProperty), "Tabler icon style does not define a shared stroke thickness.");
        Assert(iconStyle.Setters.OfType<Setter>().Any(item => item.Property == System.Windows.Shapes.Shape.StrokeLineJoinProperty), "Tabler icon style does not define rounded line joins.");
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
