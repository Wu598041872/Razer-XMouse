using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;
using XMacroBridge.Presentation.Workspace;

namespace XMacroBridge.App.SmokeTests;

internal static class Program
{
    [STAThread]
    private static int Main()
    {
        var failures = new List<string>();
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
            Console.WriteLine("PASS WPF workspace startup and binding smoke test");
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
            window.WindowState = WindowState.Minimized;

            var viewModel = window.DataContext as WorkspaceViewModel
                ?? throw new InvalidOperationException("WorkspaceViewModel is not the DataContext.");
            for (var attempt = 0; attempt < 200 && (viewModel.IsBusy || viewModel.Macros.Count == 0); attempt++)
            {
                await Task.Delay(25);
            }

            Assert(viewModel.Macros.Count == 8, "Expected eight imported anonymous fixture macros.");
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

            Assert(macroList.Items.Count == 8, "Macro list binding did not expose eight items.");
            Assert(eventTimeline.Items.Count == viewModel.SelectedMacro!.Events.Count, "Event timeline is out of sync with the selection.");
            Assert(exportButton.IsEnabled, "Export should be enabled for the selected valid fixture.");
            Assert(importFilesButton.IsEnabled, "Import should be enabled after startup import completes.");
            Assert(!cancelButton.IsEnabled, "Cancel should be disabled while idle.");
            Assert(severityFilter.Items.Count == 4, "Severity filter options are incomplete.");
            Assert(scopeFilter.Items.Count >= 2, "Source filter options were not populated.");
            Assert(diagnosticGroups.Items.Count >= 1, "Diagnostic grouping binding is empty.");
            Assert(application.Resources["PageBrush"] is SolidColorBrush, "Theme resources were not initialized.");

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

    private static void Assert(bool condition, string message)
    {
        if (!condition)
        {
            throw new InvalidOperationException(message);
        }
    }
}
