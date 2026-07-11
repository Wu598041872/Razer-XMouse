using System.ComponentModel;
using System.IO;
using System.Windows;
using Microsoft.Win32;
using XMacroBridge.Presentation.Workspace;

namespace XMacroBridge.App;

public partial class MainWindow : Window
{
    private readonly WorkspaceViewModel viewModel;

    public MainWindow()
    {
        InitializeComponent();
        viewModel = WorkspaceViewModel.CreateDefault();
        DataContext = viewModel;
    }

    private async void ImportFiles_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFileDialog
        {
            Title = "选择雷云或 X-Mouse 宏文件",
            Filter = "支持的宏文件|*.xml;*.synapse4;*.xmbcp;*.txt|雷云宏|*.xml;*.synapse4|X-Mouse 宏|*.xmbcp;*.xml;*.txt|所有文件|*.*",
            Multiselect = true,
            CheckFileExists = true,
        };

        if (dialog.ShowDialog(this) == true)
        {
            await ImportPathsAsync(dialog.FileNames);
        }
    }

    private async void ImportFolder_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFolderDialog
        {
            Title = "选择包含宏文件的文件夹",
            Multiselect = false,
        };

        if (dialog.ShowDialog(this) == true)
        {
            await ImportPathsAsync([dialog.FolderName]);
        }
    }

    private async void Export_Click(object sender, RoutedEventArgs e)
    {
        if (viewModel.SelectedMacro is null)
        {
            return;
        }

        var format = viewModel.SelectedExportFormat;
        var dialog = new SaveFileDialog
        {
            Title = "安全导出宏",
            AddExtension = true,
            DefaultExt = format.Extension,
            Filter = $"{format.DisplayName}|*{format.Extension}",
            FileName = CreateSafeSuggestedName(viewModel.SelectedMacro.Name, format.Extension),
            OverwritePrompt = true,
        };

        if (dialog.ShowDialog(this) != true)
        {
            return;
        }

        var overwrite = File.Exists(dialog.FileName);
        var result = await viewModel.ExportAsync(dialog.FileName, overwrite);
        if (!result.Succeeded)
        {
            var message = result.Diagnostics.LastOrDefault()?.Message ?? "导出未完成，请查看兼容性与安全报告。";
            MessageBox.Show(this, message, "XMacro Bridge", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    private void Cancel_Click(object sender, RoutedEventArgs e) => viewModel.Cancel();

    private void Window_DragOver(object sender, DragEventArgs e)
    {
        e.Effects = !viewModel.CanImport || !e.Data.GetDataPresent(DataFormats.FileDrop)
            ? DragDropEffects.None
            : DragDropEffects.Copy;
        e.Handled = true;
    }

    private async void Window_Drop(object sender, DragEventArgs e)
    {
        if (!viewModel.CanImport || e.Data.GetData(DataFormats.FileDrop) is not string[] paths)
        {
            return;
        }

        await ImportPathsAsync(paths);
    }

    private void Window_Closing(object? sender, CancelEventArgs e) => viewModel.Cancel();

    internal async void ImportStartupPaths(IEnumerable<string> paths) => await ImportPathsAsync(paths);

    private async Task ImportPathsAsync(IEnumerable<string> paths)
    {
        try
        {
            await viewModel.ImportAsync(paths);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException)
        {
            MessageBox.Show(
                this,
                $"无法导入所选路径：{exception.Message}",
                "XMacro Bridge",
                MessageBoxButton.OK,
                MessageBoxImage.Warning);
        }
    }

    private static string CreateSafeSuggestedName(string name, string extension)
    {
        var invalidCharacters = Path.GetInvalidFileNameChars();
        var safeName = new string(name.Select(character => invalidCharacters.Contains(character) ? '_' : character).ToArray()).Trim();
        if (string.IsNullOrWhiteSpace(safeName))
        {
            safeName = "转换后的宏";
        }

        return safeName.EndsWith(extension, StringComparison.OrdinalIgnoreCase)
            ? safeName
            : safeName + extension;
    }
}
