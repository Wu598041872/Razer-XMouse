using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using Microsoft.Win32;
using XMacroBridge.Application.Library;
using XMacroBridge.Core.Models;
using XMacroBridge.Presentation.Library;
using XMacroBridge.Presentation.Workspace;

namespace XMacroBridge.App;

public partial class MainWindow : Window
{
    private const string TimelineDragFormat = "XMacroBridge.TimelineEventIndices";
    private const string TimelineInsertBeforeTag = "TimelineInsertBefore";
    private const string TimelineInsertAfterTag = "TimelineInsertAfter";
    private readonly WorkspaceViewModel viewModel;
    private readonly MacroLibraryViewModel libraryViewModel;
    private AppPage currentPage = AppPage.Workspace;
    private MacroLibraryItem? openedLibraryItem;
    private Point timelineDragStart;
    private bool timelineDragArmed;
    private DataGridRow? timelineDropIndicatorRow;
    private bool restoringTimelineSelection;
    private bool resizingWorkspaceContent;

    public MainWindow()
    {
        InitializeComponent();
        DarkWindowAssist.Apply(this);
        viewModel = WorkspaceViewModel.CreateDefault();
        libraryViewModel = new MacroLibraryViewModel();
        DataContext = viewModel;
        MacroLibraryContent.DataContext = libraryViewModel;
        MacroLibraryContent.OpenRequested += MacroLibrary_OpenRequested;
    }

    public string ApplicationVersion =>
        Assembly.GetExecutingAssembly()
            .GetCustomAttribute<AssemblyInformationalVersionAttribute>()?
            .InformationalVersion
        ?? Assembly.GetExecutingAssembly().GetName().Version?.ToString(3)
        ?? "1.0.0";

    private async void Window_Loaded(object sender, RoutedEventArgs e) => await libraryViewModel.InitializeAsync();

    private void Window_StateChanged(object? sender, EventArgs e)
    {
        var isMaximized = WindowState == WindowState.Maximized;
        WindowMaximizeRestoreIcon.Data = (Geometry)FindResource(
            isMaximized ? "RestoreWindowGeometry" : "MaximizeWindowGeometry");
        WindowMaximizeRestoreButton.ToolTip = isMaximized ? "还原" : "最大化";
    }

    private void WindowMinimize_Click(object sender, RoutedEventArgs e) =>
        WindowState = WindowState.Minimized;

    private void WindowMaximizeRestore_Click(object sender, RoutedEventArgs e) =>
        WindowState = WindowState == WindowState.Maximized ? WindowState.Normal : WindowState.Maximized;

    private void WindowClose_Click(object sender, RoutedEventArgs e) => Close();

    private void MacroNavigation_Click(object sender, RoutedEventArgs e) => ShowPage(AppPage.Workspace);

    private void LibraryNavigation_Click(object sender, RoutedEventArgs e) => ShowPage(AppPage.Library);

    private void SettingsNavigation_Click(object sender, RoutedEventArgs e) => ShowPage(AppPage.Settings);

    private void ShowPage(AppPage page)
    {
        currentPage = page;
        WorkspaceScrollViewer.Visibility = page == AppPage.Workspace ? Visibility.Visible : Visibility.Collapsed;
        MacroLibraryContent.Visibility = page == AppPage.Library ? Visibility.Visible : Visibility.Collapsed;
        SettingsContent.Visibility = page == AppPage.Settings ? Visibility.Visible : Visibility.Collapsed;
        WorkspaceToolbar.Visibility = page == AppPage.Workspace ? Visibility.Visible : Visibility.Collapsed;
        LibraryToolbar.Visibility = page == AppPage.Library ? Visibility.Visible : Visibility.Collapsed;
        SettingsToolbar.Visibility = page == AppPage.Settings ? Visibility.Visible : Visibility.Collapsed;
        MacroNavigationButton.Tag = page == AppPage.Workspace ? "Active" : null;
        LibraryNavigationButton.Tag = page == AppPage.Library ? "Active" : null;
        SettingsNavigationButton.Tag = page == AppPage.Settings ? "Active" : null;
    }

    private void Window_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if ((Keyboard.Modifiers & ModifierKeys.Control) == 0)
        {
            return;
        }

        if (e.Key == Key.O)
        {
            ShowPage(AppPage.Workspace);
            ImportFiles_Click(this, new RoutedEventArgs());
            e.Handled = true;
            return;
        }

        if (e.Key == Key.F)
        {
            var searchBox = currentPage switch
            {
                AppPage.Workspace => EventSearchTextBox,
                AppPage.Library => LibrarySearchTextBox,
                _ => null,
            };
            if (searchBox is not null && searchBox.IsVisible)
            {
                searchBox.Focus();
                searchBox.SelectAll();
                e.Handled = true;
            }

            return;
        }

        if (currentPage != AppPage.Workspace || Keyboard.FocusedElement is TextBoxBase)
        {
            return;
        }

        if (e.Key == Key.Z)
        {
            if (viewModel.Undo())
            {
                RestoreTimelineSelection();
            }

            e.Handled = true;
        }
        else if (e.Key == Key.Y)
        {
            if (viewModel.Redo())
            {
                RestoreTimelineSelection();
            }

            e.Handled = true;
        }
    }

    private void EventSearchTextBox_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Escape)
        {
            viewModel.EventSearchText = string.Empty;
            e.Handled = true;
            return;
        }

        if (e.Key != Key.Enter)
        {
            return;
        }

        var found = (Keyboard.Modifiers & ModifierKeys.Shift) != 0
            ? viewModel.FindPreviousEvent()
            : viewModel.FindNextEvent();
        if (found)
        {
            EventTimeline.ScrollIntoView(viewModel.SelectedEvent);
        }

        e.Handled = true;
    }

    private void LibrarySearchTextBox_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Escape)
        {
            libraryViewModel.SearchText = string.Empty;
            e.Handled = true;
        }
    }

    private void ResetDiagnosticFilters_Click(object sender, RoutedEventArgs e) =>
        viewModel.ResetDiagnosticFilters();

    private void OpenThirdPartyNotices_Click(object sender, RoutedEventArgs e)
    {
        var noticesPath = Path.Combine(AppContext.BaseDirectory, "THIRD-PARTY-NOTICES.md");
        if (!File.Exists(noticesPath))
        {
            AppMessageDialog.Show(this, "许可说明不可用", "未在当前程序目录找到 THIRD-PARTY-NOTICES.md。", AppMessageKind.Warning);
            return;
        }

        try
        {
            Process.Start(new ProcessStartInfo(noticesPath) { UseShellExecute = true });
        }
        catch (Exception exception) when (exception is Win32Exception or InvalidOperationException)
        {
            AppMessageDialog.Show(this, "无法打开许可说明", "请从程序目录手动打开 THIRD-PARTY-NOTICES.md。", AppMessageKind.Warning);
        }
    }

    private void WorkspaceScrollViewer_Loaded(object sender, RoutedEventArgs e)
    {
        ResizeWorkspaceContent();
        _ = Dispatcher.BeginInvoke(DispatcherPriority.Loaded, ResizeWorkspaceContent);
    }

    private void WorkspaceScrollViewer_SizeChanged(object sender, SizeChangedEventArgs e) =>
        ResizeWorkspaceContent();

    private void WorkspaceScrollViewer_ScrollChanged(object sender, ScrollChangedEventArgs e)
    {
        if (Math.Abs(e.ViewportWidthChange) > 0.1 || Math.Abs(e.ViewportHeightChange) > 0.1)
        {
            ResizeWorkspaceContent();
        }
    }

    private void ResizeWorkspaceContent()
    {
        if (resizingWorkspaceContent || WorkspaceScrollViewer.ViewportWidth <= 0 || WorkspaceScrollViewer.ViewportHeight <= 0)
        {
            return;
        }

        resizingWorkspaceContent = true;
        try
        {
            var horizontalInset = MacroContent.Margin.Left + MacroContent.Margin.Right;
            var verticalInset = MacroContent.Margin.Top + MacroContent.Margin.Bottom;
            var width = Math.Max(MacroContent.MinWidth, WorkspaceScrollViewer.ViewportWidth - horizontalInset);
            var height = Math.Max(MacroContent.MinHeight, WorkspaceScrollViewer.ViewportHeight - verticalInset);

            if (Math.Abs(MacroContent.Width - width) > 0.5 || double.IsNaN(MacroContent.Width))
            {
                MacroContent.Width = width;
            }

            if (Math.Abs(MacroContent.Height - height) > 0.5 || double.IsNaN(MacroContent.Height))
            {
                MacroContent.Height = height;
            }
        }
        finally
        {
            resizingWorkspaceContent = false;
        }
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

    private async void ImportText_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new MacroTextInputDialog
        {
            Owner = this,
        };
        if (dialog.ShowDialog() == true)
        {
            await ImportTextAsync(dialog.MacroText);
        }
    }

    private void RenameMacro_Click(object sender, RoutedEventArgs e)
    {
        if (viewModel.SelectedMacro is not { } macro)
        {
            return;
        }

        viewModel.SelectedMacro = macro;
        var dialog = new MacroRenameDialog(macro.Name)
        {
            Owner = this,
        };
        if (dialog.ShowDialog() == true)
        {
            viewModel.RenameMacro(macro, dialog.MacroName);
        }
    }

    private void DeleteMacro_Click(object sender, RoutedEventArgs e)
    {
        if (viewModel.SelectedMacro is not { } macro)
        {
            return;
        }

        viewModel.DeleteMacro(macro);
    }

    private void MacroList_PreviewMouseRightButtonDown(object sender, MouseButtonEventArgs e)
    {
        var current = e.OriginalSource as DependencyObject;
        while (current is not null and not ListBoxItem)
        {
            current = VisualTreeHelper.GetParent(current);
        }

        if (current is ListBoxItem item)
        {
            item.IsSelected = true;
        }
    }

    private async void ModifyMacroText_Click(object sender, RoutedEventArgs e)
    {
        if (viewModel.SelectedMacro is not { } macro)
        {
            return;
        }

        var text = await viewModel.GetMacroTextAsync(macro);
        if (text is null)
        {
            AppMessageDialog.Show(
                this,
                "无法修改宏文本",
                "当前宏包含无法转换为 X-Mouse 文本的事件，请查看兼容性与安全报告。",
                AppMessageKind.Warning);
            return;
        }

        var dialog = new MacroTextInputDialog(text, isEdit: true)
        {
            Owner = this,
        };
        if (dialog.ShowDialog() == true)
        {
            await viewModel.ReplaceMacroTextAsync(macro, dialog.MacroText);
        }
    }

    private async void Export_Click(object sender, RoutedEventArgs e)
    {
        if (viewModel.SelectedMacro is null)
        {
            return;
        }

        if (TargetFormatSelector.SelectedItem is ExportFormatOption selectedFormat)
        {
            viewModel.TargetFormatId = selectedFormat.FormatId;
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
            AppMessageDialog.Show(this, "导出未完成", message, AppMessageKind.Warning);
        }
    }

    private void Cancel_Click(object sender, RoutedEventArgs e) => viewModel.Cancel();

    private void ApplyDelay_Click(object sender, RoutedEventArgs e) => viewModel.UpdateSelectedDelay();

    private void EditEvent_Click(object sender, RoutedEventArgs e) => EditSelectedEvent();

    private void EventTimeline_PreviewMouseRightButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (FindAncestor<DataGridRow>(e.OriginalSource as DependencyObject) is { } row)
        {
            row.IsSelected = true;
            row.Focus();
        }
    }

    private async void SaveToLibrary_Click(object sender, RoutedEventArgs e)
    {
        if (viewModel.SelectedMacro is null)
        {
            return;
        }

        var kind = string.Equals(viewModel.TargetFormatId, "xmbc.macro.text", StringComparison.OrdinalIgnoreCase)
            ? MacroLibraryItemKind.XMouseText
            : MacroLibraryItemKind.RazerXml;
        string? updateRelativePath = null;
        if (openedLibraryItem is not null && openedLibraryItem.Kind == kind && !openedLibraryItem.IsTrashed)
        {
            var choice = AppMessageDialog.ShowQuestion(
                this,
                "保存到宏库",
                $"是否更新宏库中的“{openedLibraryItem.Name}”？也可以另存为新条目。",
                "更新现有条目",
                "另存为",
                "取消");
            if (choice == AppDialogResult.Cancel)
            {
                return;
            }

            if (choice == AppDialogResult.Primary)
            {
                updateRelativePath = openedLibraryItem.RelativePath;
            }
        }

        string groupName;
        string name;
        if (updateRelativePath is not null)
        {
            groupName = openedLibraryItem!.GroupName;
            name = openedLibraryItem.Name;
        }
        else
        {
            var dialog = new MacroLibraryEntryDialog(
                "保存到宏库",
                viewModel.SelectedMacro.Name,
                libraryViewModel.GetGroupNames(),
                libraryViewModel.GetSelectedGroupName(),
                showText: false) { Owner = this };
            if (dialog.ShowDialog() != true)
            {
                return;
            }

            groupName = dialog.GroupName;
            name = dialog.EntryName;
        }

        try
        {
            var target = libraryViewModel.PrepareSavePath(groupName, name, kind, updateRelativePath);
            var result = await viewModel.ExportAsync(
                target,
                overwrite: updateRelativePath is not null,
                allowExplicitSourceUpdate: updateRelativePath is not null);
            if (!result.Succeeded)
            {
                AppMessageDialog.Show(this, "保存到宏库未完成", result.Diagnostics.LastOrDefault()?.Message ?? "请查看兼容性报告。", AppMessageKind.Warning);
                return;
            }

            await libraryViewModel.RefreshAsync();
            openedLibraryItem = libraryViewModel.XMouseItems.Concat(libraryViewModel.RazerItems)
                .FirstOrDefault(item => string.Equals(item.RelativePath.Replace('/', Path.DirectorySeparatorChar), Path.GetRelativePath(libraryViewModel.RootPath, target), StringComparison.OrdinalIgnoreCase));
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or ArgumentException or InvalidOperationException or NotSupportedException)
        {
            AppMessageDialog.Show(this, "无法保存到宏库", exception.Message, AppMessageKind.Warning);
        }
    }

    private async void MacroLibrary_OpenRequested(MacroLibraryItem item)
    {
        try
        {
            var path = libraryViewModel.GetFullPath(item);
            var imported = await viewModel.ImportLibraryItemAsync(path);
            if (imported is null)
            {
                AppMessageDialog.Show(this, "无法打开宏", "文件没有生成可用的宏，请查看转换区诊断。", AppMessageKind.Warning);
                ShowPage(AppPage.Workspace);
                return;
            }

            openedLibraryItem = item;
            await libraryViewModel.MarkRecentAsync(item);
            ShowPage(AppPage.Workspace);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or ArgumentException or InvalidOperationException or NotSupportedException)
        {
            AppMessageDialog.Show(this, "无法打开宏库条目", exception.Message, AppMessageKind.Warning);
        }
    }

    private async void RefreshLibrary_Click(object sender, RoutedEventArgs e) => await libraryViewModel.RefreshAsync();

    private void OpenLibraryFolder_Click(object sender, RoutedEventArgs e)
    {
        if (!Directory.Exists(libraryViewModel.RootPath))
        {
            AppMessageDialog.Show(this, "宏库目录尚未创建", "首次保存宏时会自动创建，也可以先在设置中选择已有目录。", AppMessageKind.Information);
            return;
        }

        Process.Start(new ProcessStartInfo("explorer.exe", $"\"{libraryViewModel.RootPath}\"") { UseShellExecute = true });
    }

    private async void ChooseLibraryRoot_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFolderDialog { Title = "选择宏库保存路径", Multiselect = false };
        if (dialog.ShowDialog(this) == true)
        {
            await libraryViewModel.SetRootAsync(dialog.FolderName);
        }
    }

    private async void RestoreDefaultLibraryRoot_Click(object sender, RoutedEventArgs e) =>
        await libraryViewModel.RestoreDefaultRootAsync();

    private async void MigrateLibrary_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFolderDialog { Title = "选择空目录作为新的宏库", Multiselect = false };
        if (dialog.ShowDialog(this) != true)
        {
            return;
        }

        var result = await libraryViewModel.MigrateAsync(dialog.FolderName);
        if (!result.Succeeded)
        {
            AppMessageDialog.Show(this, "宏库迁移未完成", result.Message ?? "请确认目标目录为空。", AppMessageKind.Warning);
            return;
        }

        await libraryViewModel.SetRootAsync(dialog.FolderName);
        AppMessageDialog.Show(this, "宏库迁移完成", "文件已复制并校验，旧宏库仍保留在原位置。", AppMessageKind.Information);
    }

    private void EventTimeline_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (!restoringTimelineSelection)
        {
            viewModel.SetSelectedEventIndices(GetSelectedEventIndices());
        }
    }

    private void EventTimeline_Loaded(object sender, RoutedEventArgs e)
    {
        ResizeTimelineEventColumn();
        _ = Dispatcher.BeginInvoke(DispatcherPriority.Loaded, ResizeTimelineEventColumn);
    }

    private void EventTimeline_SizeChanged(object sender, SizeChangedEventArgs e) =>
        ResizeTimelineEventColumn();

    private void ResizeTimelineEventColumn()
    {
        var availableWidth = EventTimeline.ActualWidth - SystemParameters.VerticalScrollBarWidth - 2;
        if (availableWidth <= 0)
        {
            return;
        }

        if (Math.Abs(TimelineEventColumn.ActualWidth - availableWidth) > 0.5)
        {
            TimelineEventColumn.Width = new DataGridLength(availableWidth, DataGridLengthUnitType.Pixel);
        }

        FindDescendant<ScrollViewer>(EventTimeline)?.ScrollToHorizontalOffset(0);
    }

    private void EventTimeline_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        timelineDragArmed = false;
        timelineDragStart = e.GetPosition(EventTimeline);

        if (Keyboard.Modifiers != ModifierKeys.None ||
            FindAncestor<DataGridRow>(e.OriginalSource as DependencyObject) is not { } row ||
            FindAncestor<ButtonBase>(e.OriginalSource as DependencyObject) is not null ||
            FindTaggedAncestor(e.OriginalSource as DependencyObject, "TimelineDragHandle") is not null)
        {
            return;
        }

        AddTimelineRowToSelection(row);
        if (e.ClickCount > 1)
        {
            if (row.Item is MacroEventRow item)
            {
                viewModel.SelectedEvent = item;
            }

            EditSelectedEvent();
        }
        else
        {
            timelineDragArmed = true;
        }

        e.Handled = true;
    }

    private void EventTimeline_PreviewMouseLeftButtonUp(object sender, MouseButtonEventArgs e) =>
        timelineDragArmed = false;

    private void EventSelectionCheckBox_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (FindAncestor<DataGridRow>(sender as DependencyObject) is not { Item: MacroEventRow item } row)
        {
            return;
        }

        if (row.IsSelected)
        {
            EventTimeline.SelectedItems.Remove(item);
        }
        else
        {
            EventTimeline.SelectedItems.Add(item);
        }

        row.Focus();
        e.Handled = true;
    }

    private void EventDragHandle_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (FindAncestor<DataGridRow>(sender as DependencyObject) is { } row)
        {
            EnsureRowSelected(row);
            timelineDragStart = e.GetPosition(EventTimeline);
            timelineDragArmed = true;
            e.Handled = true;
        }
    }

    private void EventTimeline_PreviewMouseMove(object sender, MouseEventArgs e)
    {
        if (e.LeftButton != MouseButtonState.Pressed)
        {
            timelineDragArmed = false;
            return;
        }

        if (!timelineDragArmed)
        {
            return;
        }

        var current = e.GetPosition(EventTimeline);
        if (Math.Abs(current.X - timelineDragStart.X) < SystemParameters.MinimumHorizontalDragDistance &&
            Math.Abs(current.Y - timelineDragStart.Y) < SystemParameters.MinimumVerticalDragDistance)
        {
            return;
        }

        var selectedIndices = GetSelectedEventIndices();
        if (selectedIndices.Length == 0)
        {
            return;
        }

        timelineDragArmed = false;
        var data = new DataObject(TimelineDragFormat, selectedIndices);
        try
        {
            _ = DragDrop.DoDragDrop(EventTimeline, data, DragDropEffects.Move);
        }
        finally
        {
            timelineDragArmed = false;
            ClearTimelineDragFeedback();
        }
    }

    private void EventTimeline_DragOver(object sender, DragEventArgs e)
    {
        if (e.Data.GetData(TimelineDragFormat) is not int[] selectedIndices || selectedIndices.Length == 0)
        {
            e.Effects = DragDropEffects.None;
            ClearTimelineDragFeedback();
            e.Handled = true;
            return;
        }

        ShowTimelineDragFeedback(selectedIndices.Length, e.GetPosition(EventTimeline));
        if (TryResolveTimelineDropTarget(e, selectedIndices, out var targetRow, out _, out var insertAfter))
        {
            e.Effects = DragDropEffects.Move;
            UpdateTimelineDropIndicator(targetRow, insertAfter);
        }
        else
        {
            e.Effects = DragDropEffects.None;
            UpdateTimelineDropIndicator(null, insertAfter: false);
        }

        e.Handled = true;
    }

    private void EventTimeline_DragLeave(object sender, DragEventArgs e)
    {
        ClearTimelineDragFeedback();
        e.Handled = true;
    }

    private void EventTimeline_Drop(object sender, DragEventArgs e)
    {
        timelineDragArmed = false;
        ClearTimelineDragFeedback();
        if (e.Data.GetData(TimelineDragFormat) is not int[] selectedIndices || selectedIndices.Length == 0)
        {
            return;
        }

        if (!TryResolveTimelineDropTarget(e, selectedIndices, out _, out var target, out var insertAfter) || target is null)
        {
            return;
        }

        if (viewModel.MoveSelectedEventsTo(selectedIndices, target.EventIndex, insertAfter))
        {
            RestoreTimelineSelection();
        }

        e.Handled = true;
    }

    private bool TryResolveTimelineDropTarget(
        DragEventArgs e,
        IReadOnlyCollection<int> selectedIndices,
        out DataGridRow? targetRow,
        out MacroEventRow? target,
        out bool insertAfter)
    {
        var selectedSet = selectedIndices.ToHashSet();
        targetRow = FindAncestor<DataGridRow>(e.OriginalSource as DependencyObject);
        target = targetRow?.Item as MacroEventRow;
        insertAfter = targetRow is not null && e.GetPosition(targetRow).Y >= targetRow.ActualHeight / 2;

        if (target is null)
        {
            target = viewModel.Events.LastOrDefault(item => !selectedSet.Contains(item.EventIndex));
            targetRow = target is null
                ? null
                : EventTimeline.ItemContainerGenerator.ContainerFromItem(target) as DataGridRow;
            insertAfter = true;
        }

        return target is not null && !selectedSet.Contains(target.EventIndex);
    }

    private void UpdateTimelineDropIndicator(DataGridRow? row, bool insertAfter)
    {
        if (timelineDropIndicatorRow is not null &&
            (ReferenceEquals(timelineDropIndicatorRow, row) ||
             timelineDropIndicatorRow.Tag is TimelineInsertBeforeTag or TimelineInsertAfterTag))
        {
            timelineDropIndicatorRow.Tag = null;
        }

        timelineDropIndicatorRow = row;
        if (row is not null)
        {
            row.Tag = insertAfter ? TimelineInsertAfterTag : TimelineInsertBeforeTag;
        }
    }

    private void ShowTimelineDragFeedback(int itemCount, Point position)
    {
        TimelineDragFeedbackTextBlock.Text = $"移动 {itemCount} 个操作";
        TimelineDragFeedbackPopup.HorizontalOffset = position.X + 18;
        TimelineDragFeedbackPopup.VerticalOffset = position.Y + 18;
        TimelineDragFeedbackPopup.IsOpen = true;
    }

    private void ClearTimelineDragFeedback()
    {
        UpdateTimelineDropIndicator(null, insertAfter: false);
        TimelineDragFeedbackPopup.IsOpen = false;
    }

    private void EditSelectedEvent()
    {
        if (!viewModel.CanEditSelectedEvent || viewModel.SelectedEvent?.Event is not { } macroEvent)
        {
            return;
        }

        var dialog = new EventEditDialog(macroEvent) { Owner = this };
        if (dialog.ShowDialog() == true && dialog.EditedEvent is not null)
        {
            viewModel.UpdateSelectedEvent(dialog.EditedEvent);
            EventTimeline.ScrollIntoView(viewModel.SelectedEvent);
        }
    }

    private void ScaleDelays_Click(object sender, RoutedEventArgs e) => viewModel.ScaleAllDelays();

    private void VirtualKeyTextBox_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (!KeyboardKeyCapture.TryCapture(e, out var captured))
        {
            return;
        }

        viewModel.CaptureNewVirtualKey(captured.VirtualKey, captured.IsExtended);
        e.Handled = true;
    }

    private void InsertDelay_Click(object sender, RoutedEventArgs e) => viewModel.InsertDelayAfterSelection();

    private void InsertKeyEvent_Click(object sender, RoutedEventArgs e) => viewModel.InsertKeyboardEvent();

    private void InsertMouseEvent_Click(object sender, RoutedEventArgs e) => viewModel.InsertMouseEvent();

    private void BindNestedMacro_Click(object sender, RoutedEventArgs e) => viewModel.BindSelectedReferenceTarget();

    private void InsertNestedMacro_Click(object sender, RoutedEventArgs e) => viewModel.InsertMacroReference();

    private void CopyEvent_Click(object sender, RoutedEventArgs e) => CopyTimelineSelection();

    private void DeleteEvent_Click(object sender, RoutedEventArgs e) => DeleteTimelineSelection();

    private void MoveEventUp_Click(object sender, RoutedEventArgs e) => MoveTimelineSelection(-1);

    private void MoveEventDown_Click(object sender, RoutedEventArgs e) => MoveTimelineSelection(1);

    private void InlineCopyEvent_Click(object sender, RoutedEventArgs e)
    {
        EnsureActionRowSelected(sender as DependencyObject);
        CopyTimelineSelection();
        e.Handled = true;
    }

    private void InlineDeleteEvent_Click(object sender, RoutedEventArgs e)
    {
        EnsureActionRowSelected(sender as DependencyObject);
        DeleteTimelineSelection();
        e.Handled = true;
    }

    private void Undo_Click(object sender, RoutedEventArgs e) => viewModel.Undo();

    private void Redo_Click(object sender, RoutedEventArgs e) => viewModel.Redo();

    private void FindPreviousEvent_Click(object sender, RoutedEventArgs e)
    {
        if (viewModel.FindPreviousEvent())
        {
            EventTimeline.ScrollIntoView(viewModel.SelectedEvent);
        }
    }

    private void FindNextEvent_Click(object sender, RoutedEventArgs e)
    {
        if (viewModel.FindNextEvent())
        {
            EventTimeline.ScrollIntoView(viewModel.SelectedEvent);
        }
    }

    private void Window_DragOver(object sender, DragEventArgs e)
    {
        var canAccept = e.Data.GetDataPresent(DataFormats.FileDrop) &&
                        (currentPage == AppPage.Library || viewModel.CanImport);
        e.Effects = !canAccept
            ? DragDropEffects.None
            : DragDropEffects.Copy;
        e.Handled = true;
    }

    private async void Window_Drop(object sender, DragEventArgs e)
    {
        if (e.Data.GetData(DataFormats.FileDrop) is not string[] paths)
        {
            return;
        }

        if (currentPage == AppPage.Library)
        {
            var textFiles = paths.Where(path => string.Equals(Path.GetExtension(path), ".txt", StringComparison.OrdinalIgnoreCase)).ToArray();
            var xmlFiles = paths.Where(path => string.Equals(Path.GetExtension(path), ".xml", StringComparison.OrdinalIgnoreCase)).ToArray();
            if (textFiles.Length > 0)
            {
                await libraryViewModel.ImportFilesAsync(libraryViewModel.GetSelectedGroupName(), textFiles, MacroLibraryItemKind.XMouseText);
            }

            if (xmlFiles.Length > 0)
            {
                await libraryViewModel.ImportFilesAsync(libraryViewModel.GetSelectedGroupName(), xmlFiles, MacroLibraryItemKind.RazerXml);
            }

            return;
        }

        if (!viewModel.CanImport)
        {
            return;
        }

        await ImportPathsAsync(paths);
    }

    private void Window_Closing(object? sender, CancelEventArgs e)
    {
        viewModel.Cancel();
        libraryViewModel.Dispose();
    }

    internal async void ImportStartupPaths(IEnumerable<string> paths) => await ImportPathsAsync(paths);

    private async Task ImportPathsAsync(IEnumerable<string> paths)
    {
        try
        {
            await viewModel.ImportAsync(paths);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException)
        {
            AppMessageDialog.Show(this, "无法导入所选路径", exception.Message, AppMessageKind.Warning);
        }
    }

    private async Task ImportTextAsync(string text)
    {
        try
        {
            await viewModel.ImportTextAsync(text);
        }
        catch (Exception exception) when (exception is ArgumentException or NotSupportedException)
        {
            AppMessageDialog.Show(this, "无法导入宏文本", exception.Message, AppMessageKind.Warning);
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

    private int[] GetSelectedEventIndices()
    {
        var selected = EventTimeline.SelectedItems
            .OfType<MacroEventRow>()
            .OrderBy(item => item.DisplayIndex)
            .Select(item => item.EventIndex)
            .Distinct()
            .ToArray();
        return selected.Length > 0
            ? selected
            : viewModel.SelectedEvent is null
                ? []
                : [viewModel.SelectedEvent.EventIndex];
    }

    private void CopyTimelineSelection()
    {
        var selected = GetSelectedEventIndices();
        var succeeded = selected.Length > 1
            ? viewModel.CopySelectedEvents(selected)
            : viewModel.CopySelectedEvent();
        if (succeeded)
        {
            RestoreTimelineSelection();
        }
    }

    private void DeleteTimelineSelection()
    {
        var selected = GetSelectedEventIndices();
        var succeeded = selected.Length > 1
            ? viewModel.DeleteSelectedEvents(selected)
            : viewModel.DeleteSelectedEvent();
        if (succeeded)
        {
            RestoreTimelineSelection();
        }
    }

    private void MoveTimelineSelection(int offset)
    {
        var selected = GetSelectedEventIndices();
        var succeeded = selected.Length > 1
            ? viewModel.MoveSelectedEvents(selected, offset)
            : offset < 0
                ? viewModel.MoveSelectedEventUp()
                : viewModel.MoveSelectedEventDown();
        if (succeeded)
        {
            RestoreTimelineSelection();
        }
    }

    private void RestoreTimelineSelection()
    {
        _ = Dispatcher.BeginInvoke(() =>
        {
            restoringTimelineSelection = true;
            try
            {
                var selectedIndices = viewModel.SelectedEventIndices.Count > 0
                    ? viewModel.SelectedEventIndices.ToHashSet()
                    : viewModel.SelectedEvent is null
                        ? []
                        : new HashSet<int> { viewModel.SelectedEvent.EventIndex };
                EventTimeline.SelectedItems.Clear();
                foreach (var row in viewModel.Events.Where(item => selectedIndices.Contains(item.EventIndex)))
                {
                    EventTimeline.SelectedItems.Add(row);
                }

                if (EventTimeline.SelectedItems.Count > 0)
                {
                    EventTimeline.ScrollIntoView(EventTimeline.SelectedItems[0]);
                }
            }
            finally
            {
                restoringTimelineSelection = false;
            }
        });
    }

    private void EnsureActionRowSelected(DependencyObject? source)
    {
        if (FindAncestor<DataGridRow>(source) is { } row)
        {
            EnsureRowSelected(row);
        }
    }

    private void EnsureRowSelected(DataGridRow row)
    {
        if (row.IsSelected)
        {
            return;
        }

        EventTimeline.SelectedItems.Clear();
        row.IsSelected = true;
        row.Focus();
    }

    private void AddTimelineRowToSelection(DataGridRow row)
    {
        if (row.Item is not MacroEventRow item)
        {
            return;
        }

        if (!row.IsSelected)
        {
            EventTimeline.SelectedItems.Add(item);
        }

        row.Focus();
    }

    private static FrameworkElement? FindTaggedAncestor(DependencyObject? current, object tag)
    {
        while (current is not null)
        {
            if (current is FrameworkElement element && Equals(element.Tag, tag))
            {
                return element;
            }

            current = VisualTreeHelper.GetParent(current);
        }

        return null;
    }

    private static T? FindAncestor<T>(DependencyObject? current)
        where T : DependencyObject
    {
        while (current is not null and not T)
        {
            current = VisualTreeHelper.GetParent(current);
        }

        return current as T;
    }

    private static T? FindDescendant<T>(DependencyObject current)
        where T : DependencyObject
    {
        for (var index = 0; index < VisualTreeHelper.GetChildrenCount(current); index++)
        {
            var child = VisualTreeHelper.GetChild(current, index);
            if (child is T match)
            {
                return match;
            }

            if (FindDescendant<T>(child) is { } descendant)
            {
                return descendant;
            }
        }

        return null;
    }

    private enum AppPage
    {
        Workspace,
        Library,
        Settings,
    }

}
