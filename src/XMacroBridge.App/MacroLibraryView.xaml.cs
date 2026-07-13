using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using Microsoft.Win32;
using XMacroBridge.Application.Library;
using XMacroBridge.Presentation.Library;

namespace XMacroBridge.App;

public partial class MacroLibraryView : UserControl
{
    private const string LibraryDragFormat = "XMacroBridge.MacroLibraryItem";
    private const string InsertBeforeTag = "LibraryInsertBefore";
    private const string InsertAfterTag = "LibraryInsertAfter";
    private Point libraryDragStart;
    private ListBox? libraryDragSource;
    private MacroLibraryItem? libraryDraggedItem;

    public MacroLibraryView()
    {
        InitializeComponent();
    }

    public event Action<MacroLibraryItem>? OpenRequested;

    private MacroLibraryViewModel ViewModel => (MacroLibraryViewModel)DataContext;

    private Window Owner => Window.GetWindow(this)!;

    private MacroLibraryItem? SelectedItem =>
        XMouseList.IsKeyboardFocusWithin || XMouseList.SelectedItem is not null && RazerList.SelectedItem is null
            ? ViewModel.SelectedXMouseItem
            : ViewModel.SelectedRazerItem ?? ViewModel.SelectedXMouseItem;

    private async void CreateGroup_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new MacroRenameDialog(string.Empty) { Owner = Owner, Title = "新建宏库分组" };
        if (dialog.ShowDialog() == true)
        {
            await ShowResultAsync(ViewModel.CreateGroupAsync(dialog.MacroName));
        }
    }

    private async void RenameGroup_Click(object sender, RoutedEventArgs e)
    {
        if (GroupList.SelectedItem is not MacroLibraryGroupOption option || !option.Key.StartsWith("group:", StringComparison.Ordinal))
        {
            AppMessageDialog.Show(Owner, "请选择分组", "只有自定义分组可以重命名。", AppMessageKind.Information);
            return;
        }

        var oldName = option.DisplayName;
        if (ViewModel.GroupContainsOtherEntries(oldName) &&
            !AppMessageDialog.Confirm(
                Owner,
                "重命名分组",
                "该分组包含 PNG、其他附件或子目录。重命名真实文件夹时它们也会一起移动，是否继续？",
                "继续重命名"))
        {
            return;
        }

        var dialog = new MacroRenameDialog(oldName) { Owner = Owner, Title = "重命名宏库分组" };
        if (dialog.ShowDialog() == true && !string.Equals(oldName, dialog.MacroName, StringComparison.Ordinal))
        {
            await ShowResultAsync(ViewModel.RenameGroupAsync(oldName, dialog.MacroName));
        }
    }

    private async void DeleteGroup_Click(object sender, RoutedEventArgs e)
    {
        if (GroupList.SelectedItem is not MacroLibraryGroupOption option || !option.Key.StartsWith("group:", StringComparison.Ordinal))
        {
            return;
        }

        if (AppMessageDialog.Confirm(Owner, "删除空分组", $"仅空文件夹可以删除。确认删除分组“{option.DisplayName}”？", "删除", isDangerous: true))
        {
            await ShowResultAsync(ViewModel.DeleteGroupAsync(option.DisplayName));
        }
    }

    private async void EmptyTrash_Click(object sender, RoutedEventArgs e)
    {
        if (AppMessageDialog.Confirm(Owner, "清空回收站", "确认永久清空宏库回收站？此操作无法撤销。", "永久清空", isDangerous: true))
        {
            await ViewModel.EmptyTrashAsync();
        }
    }

    private async void NewText_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new MacroLibraryEntryDialog(
            "新建 XMouse 宏",
            string.Empty,
            ViewModel.GetGroupNames(),
            ViewModel.GetSelectedGroupName(),
            showText: true) { Owner = Owner };
        if (dialog.ShowDialog() == true)
        {
            await ShowResultAsync(ViewModel.SaveTextAsync(dialog.GroupName, dialog.EntryName, dialog.MacroText));
        }
    }

    private void ImportTextFiles_Click(object sender, RoutedEventArgs e) => ImportFiles(MacroLibraryItemKind.XMouseText);

    private void ImportXmlFiles_Click(object sender, RoutedEventArgs e) => ImportFiles(MacroLibraryItemKind.RazerXml);

    private async void ImportFiles(MacroLibraryItemKind kind)
    {
        var extension = kind == MacroLibraryItemKind.XMouseText ? "txt" : "xml";
        var dialog = new OpenFileDialog
        {
            Title = kind == MacroLibraryItemKind.XMouseText ? "导入 XMouse 宏文本" : "导入雷蛇宏 XML",
            Filter = $"{extension.ToUpperInvariant()} 文件|*.{extension}",
            Multiselect = true,
            CheckFileExists = true,
        };
        if (dialog.ShowDialog(Owner) == true)
        {
            await ShowResultAsync(ViewModel.ImportFilesAsync(ViewModel.GetSelectedGroupName(), dialog.FileNames, kind));
        }
    }

    private void OpenSelected_Click(object sender, RoutedEventArgs e)
    {
        var item = ResolveItem(sender);
        if (item is null)
        {
            return;
        }

        if (item.IsTrashed)
        {
            AppMessageDialog.Show(Owner, "条目位于回收站", "请先恢复该宏，再打开到转换区。", AppMessageKind.Information);
            return;
        }

        OpenRequested?.Invoke(item);
    }

    private async void EditText_Click(object sender, RoutedEventArgs e)
    {
        var item = ViewModel.SelectedXMouseItem;
        if (item is null || item.IsTrashed)
        {
            return;
        }

        var text = await ViewModel.ReadTextAsync(item);
        var dialog = new MacroTextInputDialog(text, isEdit: true) { Owner = Owner };
        if (dialog.ShowDialog() == true)
        {
            await ShowResultAsync(ViewModel.SaveTextAsync(item.GroupName, item.Name, dialog.MacroText, item.RelativePath));
        }
    }

    private async void CopyText_Click(object sender, RoutedEventArgs e)
    {
        var item = ViewModel.SelectedXMouseItem;
        if (item is null || item.IsTrashed)
        {
            return;
        }

        Clipboard.SetText(await ViewModel.ReadTextAsync(item));
        await ViewModel.MarkRecentAsync(item);
    }

    private async void Favorite_Click(object sender, RoutedEventArgs e)
    {
        var item = ResolveItem(sender);
        if (item is not null && !item.IsTrashed)
        {
            await ViewModel.ToggleFavoriteAsync(item);
        }
    }

    private async void LibraryList_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (sender is ListBox { SelectedItem: MacroLibraryItem item })
        {
            await ViewModel.LoadPreviewAsync(item);
        }
    }

    private void LibraryList_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        libraryDragSource = null;
        libraryDraggedItem = null;
        if (!ViewModel.CanReorderLibraryItems || sender is not ListBox listBox ||
            FindAncestor<ListBoxItem>(e.OriginalSource as DependencyObject) is not { DataContext: MacroLibraryItem item })
        {
            return;
        }

        libraryDragStart = e.GetPosition(listBox);
        libraryDragSource = listBox;
        libraryDraggedItem = item;
        listBox.SelectedItem = item;
    }

    private void LibraryList_PreviewMouseMove(object sender, MouseEventArgs e)
    {
        if (e.LeftButton != MouseButtonState.Pressed || sender is not ListBox listBox ||
            !ReferenceEquals(listBox, libraryDragSource) || libraryDraggedItem is null || !ViewModel.CanReorderLibraryItems)
        {
            return;
        }

        var current = e.GetPosition(listBox);
        if (Math.Abs(current.X - libraryDragStart.X) < SystemParameters.MinimumHorizontalDragDistance &&
            Math.Abs(current.Y - libraryDragStart.Y) < SystemParameters.MinimumVerticalDragDistance)
        {
            return;
        }

        var payload = new LibraryDragPayload(listBox.Name, libraryDraggedItem.RelativePath);
        libraryDraggedItem = null;
        try
        {
            _ = DragDrop.DoDragDrop(listBox, new DataObject(LibraryDragFormat, payload), DragDropEffects.Move);
        }
        finally
        {
            ClearLibraryDropIndicators();
            libraryDragSource = null;
        }
    }

    private void LibraryList_DragOver(object sender, DragEventArgs e)
    {
        if (!TryResolveLibraryDrop(sender, e, out _, out var targetContainer, out var insertAfter))
        {
            e.Effects = DragDropEffects.None;
            ClearLibraryDropIndicators();
            e.Handled = true;
            return;
        }

        e.Effects = DragDropEffects.Move;
        ShowLibraryDropIndicator(targetContainer, insertAfter);
        e.Handled = true;
    }

    private void LibraryList_DragLeave(object sender, DragEventArgs e) => ClearLibraryDropIndicators();

    private async void LibraryList_Drop(object sender, DragEventArgs e)
    {
        if (!TryResolveLibraryDrop(sender, e, out var movedItem, out var targetContainer, out var insertAfter) ||
            targetContainer.DataContext is not MacroLibraryItem targetItem)
        {
            ClearLibraryDropIndicators();
            return;
        }

        ClearLibraryDropIndicators();
        await ShowResultAsync(ViewModel.ReorderItemAsync(movedItem, targetItem, insertAfter));
        e.Handled = true;
    }

    private bool TryResolveLibraryDrop(
        object sender,
        DragEventArgs e,
        out MacroLibraryItem movedItem,
        out ListBoxItem targetContainer,
        out bool insertAfter)
    {
        movedItem = null!;
        targetContainer = null!;
        insertAfter = false;
        if (!ViewModel.CanReorderLibraryItems || sender is not ListBox listBox ||
            e.Data.GetData(LibraryDragFormat) is not LibraryDragPayload payload ||
            !string.Equals(payload.SourceListName, listBox.Name, StringComparison.Ordinal))
        {
            return false;
        }

        movedItem = listBox.Items.OfType<MacroLibraryItem>()
            .FirstOrDefault(item => string.Equals(item.RelativePath, payload.RelativePath, StringComparison.OrdinalIgnoreCase))!;
        if (movedItem is null)
        {
            return false;
        }

        targetContainer = FindAncestor<ListBoxItem>(e.OriginalSource as DependencyObject)
            ?? (listBox.Items.Count > 0 ? listBox.ItemContainerGenerator.ContainerFromIndex(listBox.Items.Count - 1) as ListBoxItem : null)!;
        if (targetContainer is null || targetContainer.DataContext is not MacroLibraryItem targetItem || targetItem.Kind != movedItem.Kind)
        {
            return false;
        }

        insertAfter = FindAncestor<ListBoxItem>(e.OriginalSource as DependencyObject) is null ||
            e.GetPosition(targetContainer).Y > targetContainer.ActualHeight / 2;
        return !string.Equals(targetItem.RelativePath, movedItem.RelativePath, StringComparison.OrdinalIgnoreCase);
    }

    private void ShowLibraryDropIndicator(ListBoxItem target, bool insertAfter)
    {
        ClearLibraryDropIndicators();
        target.Tag = insertAfter ? InsertAfterTag : InsertBeforeTag;
    }

    private void ClearLibraryDropIndicators()
    {
        foreach (var listBox in new[] { XMouseList, RazerList })
        {
            for (var index = 0; index < listBox.Items.Count; index++)
            {
                if (listBox.ItemContainerGenerator.ContainerFromIndex(index) is ListBoxItem container &&
                    container.Tag is InsertBeforeTag or InsertAfterTag)
                {
                    container.Tag = null;
                }
            }
        }
    }

    private static T? FindAncestor<T>(DependencyObject? current)
        where T : DependencyObject
    {
        while (current is not null)
        {
            if (current is T match)
            {
                return match;
            }

            current = VisualTreeHelper.GetParent(current);
        }

        return null;
    }

    private void MoreItemActions_Click(object sender, RoutedEventArgs e)
    {
        var item = ResolveItem(sender);
        if (item is null || sender is not Button button)
        {
            return;
        }

        var menu = new ContextMenu();
        if (item.IsTrashed)
        {
            menu.Items.Add(CreateMenuItem("恢复", async () => await ShowResultAsync(ViewModel.RestoreAsync(item))));
            menu.Items.Add(CreateMenuItem("永久删除", async () =>
            {
                if (AppMessageDialog.Confirm(Owner, "永久删除", $"永久删除“{item.Name}”？此操作无法撤销。", "永久删除", isDangerous: true))
                {
                    await ShowResultAsync(ViewModel.DeletePermanentlyAsync(item));
                }
            }, isDangerous: true));
        }
        else
        {
            menu.Items.Add(CreateMenuItem("重命名", async () => await RenameItemAsync(item)));
            menu.Items.Add(CreateMenuItem("移动到：未分组", async () => await ShowResultAsync(ViewModel.MoveItemAsync(item, string.Empty))));
            foreach (var group in ViewModel.GetGroupNames())
            {
                var targetGroup = group;
                menu.Items.Add(CreateMenuItem($"移动到：{group}", async () => await ShowResultAsync(ViewModel.MoveItemAsync(item, targetGroup))));
            }

            menu.Items.Add(CreateMenuItem("移入回收站", async () =>
            {
                if (AppMessageDialog.Confirm(Owner, "删除宏", $"将“{item.Name}”移入宏库回收站？", "移入回收站", isDangerous: true))
                {
                    await ShowResultAsync(ViewModel.MoveToTrashAsync(item));
                }
            }, isDangerous: true));
        }

        menu.PlacementTarget = button;
        menu.IsOpen = true;
    }

    private async Task RenameItemAsync(MacroLibraryItem item)
    {
        var dialog = new MacroRenameDialog(item.Name) { Owner = Owner };
        if (dialog.ShowDialog() == true)
        {
            await ShowResultAsync(ViewModel.RenameItemAsync(item, dialog.MacroName));
        }
    }

    private void RevealSelected_Click(object sender, RoutedEventArgs e)
    {
        var item = ViewModel.SelectedRazerItem;
        if (item is null || item.IsTrashed)
        {
            return;
        }

        Process.Start(new ProcessStartInfo("explorer.exe", $"/select,\"{ViewModel.GetFullPath(item)}\"") { UseShellExecute = true });
    }

    private MacroLibraryItem? ResolveItem(object sender)
    {
        if (ReferenceEquals(sender, RazerList) || sender is FrameworkElement { Tag: "razer" })
        {
            return ViewModel.SelectedRazerItem;
        }

        if (ReferenceEquals(sender, XMouseList) || sender is FrameworkElement { Tag: "xmouse" })
        {
            return ViewModel.SelectedXMouseItem;
        }

        if (RazerList.IsKeyboardFocusWithin)
        {
            return ViewModel.SelectedRazerItem;
        }

        return ViewModel.SelectedXMouseItem ?? ViewModel.SelectedRazerItem;
    }

    private static MenuItem CreateMenuItem(string header, Func<Task> action, bool isDangerous = false)
    {
        var item = new MenuItem { Header = header, Tag = isDangerous ? "Danger" : null };
        item.Click += async (_, _) => await action();
        return item;
    }

    private async Task ShowResultAsync(Task<MacroLibraryOperationResult> operation)
    {
        var result = await operation;
        if (!result.Succeeded)
        {
            AppMessageDialog.Show(Owner, "宏库操作未完成", result.Message ?? "请检查路径和文件状态。", AppMessageKind.Warning);
        }
    }

    private sealed record LibraryDragPayload(string SourceListName, string RelativePath);
}
