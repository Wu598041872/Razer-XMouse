using System.Collections.ObjectModel;
using XMacroBridge.Application.Formats;
using XMacroBridge.Application.Importing;
using XMacroBridge.Application.Library;
using XMacroBridge.Presentation.Common;

namespace XMacroBridge.Presentation.Library;

public sealed class MacroLibraryViewModel : ObservableObject, IDisposable
{
    public const string AllScope = "all";
    public const string FavoritesScope = "favorites";
    public const string RecentScope = "recent";
    public const string UngroupedScope = "ungrouped";
    public const string TrashScope = "trash";
    private const string GroupPrefix = "group:";
    private readonly MacroLibraryService service;
    private readonly MacroLibrarySettingsService settingsService;
    private readonly MacroImportService importService;
    private readonly SynchronizationContext? synchronizationContext;
    private readonly Timer refreshTimer;
    private IReadOnlyList<MacroLibraryItem> activeItems = [];
    private IReadOnlyList<MacroLibraryItem> trashItems = [];
    private FileSystemWatcher? watcher;
    private string rootPath = MacroLibrarySettingsService.DefaultLibraryRootPath;
    private string selectedScope = AllScope;
    private string searchText = string.Empty;
    private bool sortByModified;
    private bool isBusy;
    private string statusText = "宏库尚未载入";
    private string warningsText = string.Empty;
    private MacroLibraryItem? selectedXMouseItem;
    private MacroLibraryItem? selectedRazerItem;
    private string xMousePreviewText = "选择条目以查看解析状态";
    private string razerPreviewText = "选择条目以查看解析状态";
    private bool disposed;

    public MacroLibraryViewModel(
        MacroLibraryService? service = null,
        MacroLibrarySettingsService? settingsService = null)
    {
        this.service = service ?? new MacroLibraryService();
        this.settingsService = settingsService ?? new MacroLibrarySettingsService();
        importService = new MacroImportService(MacroFormatRegistry.CreateDefault());
        synchronizationContext = SynchronizationContext.Current;
        refreshTimer = new Timer(_ => PostRefresh(), null, Timeout.Infinite, Timeout.Infinite);
    }

    public ObservableCollection<MacroLibraryGroupOption> GroupOptions { get; } = [];
    public ObservableCollection<MacroLibraryItem> XMouseItems { get; } = [];
    public ObservableCollection<MacroLibraryItem> RazerItems { get; } = [];

    public string RootPath
    {
        get => rootPath;
        private set
        {
            if (SetProperty(ref rootPath, value))
            {
                OnPropertyChanged(nameof(RootPathDisplay));
            }
        }
    }

    public string RootPathDisplay => RootPath;

    public string SelectedScope
    {
        get => selectedScope;
        set
        {
            if (SetProperty(ref selectedScope, value))
            {
                RebuildVisibleItems();
                NotifyActionState();
            }
        }
    }

    public string SearchText
    {
        get => searchText;
        set
        {
            if (SetProperty(ref searchText, value))
            {
                RebuildVisibleItems();
                NotifyActionState();
            }
        }
    }

    public bool SortByModified
    {
        get => sortByModified;
        set
        {
            if (SetProperty(ref sortByModified, value))
            {
                RebuildVisibleItems();
                NotifyActionState();
            }
        }
    }

    public bool IsBusy
    {
        get => isBusy;
        private set
        {
            if (SetProperty(ref isBusy, value))
            {
                NotifyActionState();
            }
        }
    }

    public string StatusText
    {
        get => statusText;
        private set => SetProperty(ref statusText, value);
    }

    public string WarningsText
    {
        get => warningsText;
        private set => SetProperty(ref warningsText, value);
    }

    public MacroLibraryItem? SelectedXMouseItem
    {
        get => selectedXMouseItem;
        set
        {
            if (SetProperty(ref selectedXMouseItem, value))
            {
                NotifyActionState();
            }
        }
    }

    public MacroLibraryItem? SelectedRazerItem
    {
        get => selectedRazerItem;
        set
        {
            if (SetProperty(ref selectedRazerItem, value))
            {
                NotifyActionState();
            }
        }
    }

    public bool HasXMouseItems => XMouseItems.Count > 0;

    public bool HasRazerItems => RazerItems.Count > 0;

    public bool HasSelectedXMouseItem => SelectedXMouseItem is not null;

    public bool HasSelectedRazerItem => SelectedRazerItem is not null;

    public bool CanUseSelectedXMouseItem => !IsBusy && SelectedXMouseItem is { IsTrashed: false };

    public bool CanUseSelectedRazerItem => !IsBusy && SelectedRazerItem is { IsTrashed: false };

    public bool CanRenameSelectedGroup => !IsBusy && SelectedScope.StartsWith(GroupPrefix, StringComparison.Ordinal);

    public bool CanDeleteSelectedGroup => CanRenameSelectedGroup &&
        GroupOptions.FirstOrDefault(item => string.Equals(item.Key, SelectedScope, StringComparison.Ordinal))?.ItemCount == 0;

    public bool CanEmptyTrash => !IsBusy && trashItems.Count > 0;

    public bool CanModifyLibrary => !IsBusy;

    public bool CanReorderLibraryItems =>
        !IsBusy && !SortByModified && SelectedScope != TrashScope && string.IsNullOrWhiteSpace(SearchText);

    public string XMousePreviewText
    {
        get => xMousePreviewText;
        private set => SetProperty(ref xMousePreviewText, value);
    }

    public string RazerPreviewText
    {
        get => razerPreviewText;
        private set => SetProperty(ref razerPreviewText, value);
    }

    public async Task InitializeAsync(CancellationToken cancellationToken = default)
    {
        var (settings, warning) = await settingsService.LoadAsync(cancellationToken).ConfigureAwait(true);
        RootPath = settings.LibraryRootPath;
        await RefreshAsync(cancellationToken).ConfigureAwait(true);
        if (!string.IsNullOrWhiteSpace(warning))
        {
            WarningsText = string.IsNullOrWhiteSpace(WarningsText) ? warning : warning + Environment.NewLine + WarningsText;
        }
        ConfigureWatcher();
    }

    public async Task SetRootAsync(string path, CancellationToken cancellationToken = default)
    {
        RootPath = Path.GetFullPath(path);
        await settingsService.SaveAsync(new MacroLibraryAppSettings(RootPath), cancellationToken).ConfigureAwait(true);
        SelectedScope = AllScope;
        await RefreshAsync(cancellationToken).ConfigureAwait(true);
        ConfigureWatcher();
    }

    public Task RestoreDefaultRootAsync(CancellationToken cancellationToken = default) =>
        SetRootAsync(MacroLibrarySettingsService.DefaultLibraryRootPath, cancellationToken);

    public Task<MacroLibraryOperationResult> MigrateAsync(string destination, CancellationToken cancellationToken = default) =>
        RunOperationAsync(() => service.MigrateAsync(RootPath, destination, cancellationToken), "宏库迁移完成", false, cancellationToken);

    public async Task RefreshAsync(CancellationToken cancellationToken = default)
    {
        if (IsBusy)
        {
            return;
        }

        IsBusy = true;
        StatusText = "正在扫描宏库...";
        try
        {
            var snapshot = await service.ScanAsync(RootPath, cancellationToken).ConfigureAwait(true);
            activeItems = snapshot.Items;
            trashItems = snapshot.TrashItems;
            RebuildGroupOptions(snapshot.Groups);
            RebuildVisibleItems();
            WarningsText = string.Join(Environment.NewLine, snapshot.Warnings);
            StatusText = Directory.Exists(RootPath)
                ? $"已载入 {activeItems.Count} 个宏，回收站 {trashItems.Count} 个"
                : "宏库目录尚未创建，可在首次保存时自动创建";
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or ArgumentException or InvalidOperationException or NotSupportedException)
        {
            StatusText = "宏库载入失败";
            WarningsText = exception.Message;
        }
        finally
        {
            IsBusy = false;
        }
    }

    public Task<MacroLibraryOperationResult> CreateGroupAsync(string name, CancellationToken cancellationToken = default) =>
        RunOperationAsync(() => service.CreateGroupAsync(RootPath, name, cancellationToken), "已创建分组", true, cancellationToken);

    public Task<MacroLibraryOperationResult> RenameGroupAsync(string oldName, string newName, CancellationToken cancellationToken = default) =>
        RunOperationAsync(() => service.RenameGroupAsync(RootPath, oldName, newName, cancellationToken), "已重命名分组", true, cancellationToken);

    public Task<MacroLibraryOperationResult> DeleteGroupAsync(string name, CancellationToken cancellationToken = default) =>
        RunOperationAsync(() => service.DeleteEmptyGroupAsync(RootPath, name, cancellationToken), "已删除空分组", true, cancellationToken);

    public Task<MacroLibraryOperationResult> SaveTextAsync(
        string groupName,
        string name,
        string text,
        string? updateRelativePath = null,
        CancellationToken cancellationToken = default) =>
        RunOperationAsync(
            () => service.SaveTextAsync(RootPath, groupName, name, text, updateRelativePath, cancellationToken),
            updateRelativePath is null ? "XMouse 宏已保存" : "XMouse 宏已更新",
            true,
            cancellationToken);

    public Task<MacroLibraryOperationResult> ImportFilesAsync(
        string groupName,
        IEnumerable<string> paths,
        MacroLibraryItemKind kind,
        CancellationToken cancellationToken = default) =>
        RunOperationAsync(
            () => service.ImportFilesAsync(RootPath, groupName, paths, kind, cancellationToken),
            "文件已复制到宏库",
            true,
            cancellationToken);

    public Task<MacroLibraryOperationResult> RenameItemAsync(MacroLibraryItem item, string newName, CancellationToken cancellationToken = default) =>
        RunOperationAsync(() => service.RenameItemAsync(RootPath, item.RelativePath, newName, cancellationToken), "宏已重命名", true, cancellationToken);

    public Task<MacroLibraryOperationResult> MoveItemAsync(MacroLibraryItem item, string groupName, CancellationToken cancellationToken = default) =>
        RunOperationAsync(() => service.MoveItemAsync(RootPath, item.RelativePath, groupName, cancellationToken), "宏已移动", true, cancellationToken);

    public Task<MacroLibraryOperationResult> MoveToTrashAsync(MacroLibraryItem item, CancellationToken cancellationToken = default) =>
        RunOperationAsync(() => service.MoveToTrashAsync(RootPath, item.RelativePath, cancellationToken), "宏已移入回收站", true, cancellationToken);

    public Task<MacroLibraryOperationResult> RestoreAsync(MacroLibraryItem item, CancellationToken cancellationToken = default) =>
        RunOperationAsync(() => service.RestoreAsync(RootPath, item.RelativePath, cancellationToken), "宏已恢复", true, cancellationToken);

    public Task<MacroLibraryOperationResult> DeletePermanentlyAsync(MacroLibraryItem item, CancellationToken cancellationToken = default) =>
        RunOperationAsync(() => service.DeletePermanentlyAsync(RootPath, item.RelativePath, cancellationToken), "宏已永久删除", true, cancellationToken);

    public async Task EmptyTrashAsync(CancellationToken cancellationToken = default)
    {
        await service.EmptyTrashAsync(RootPath, cancellationToken).ConfigureAwait(true);
        await RefreshAsync(cancellationToken).ConfigureAwait(true);
    }

    public async Task ToggleFavoriteAsync(MacroLibraryItem item, CancellationToken cancellationToken = default)
    {
        await service.SetFavoriteAsync(RootPath, item.RelativePath, !item.IsFavorite, cancellationToken).ConfigureAwait(true);
        await RefreshAsync(cancellationToken).ConfigureAwait(true);
    }

    public async Task MarkRecentAsync(MacroLibraryItem item, CancellationToken cancellationToken = default)
    {
        await service.MarkRecentAsync(RootPath, item.RelativePath, cancellationToken).ConfigureAwait(true);
        await RefreshAsync(cancellationToken).ConfigureAwait(true);
    }

    public async Task<MacroLibraryOperationResult> ReorderItemAsync(
        MacroLibraryItem movedItem,
        MacroLibraryItem targetItem,
        bool insertAfter,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(movedItem);
        ArgumentNullException.ThrowIfNull(targetItem);
        if (!CanReorderLibraryItems)
        {
            return new MacroLibraryOperationResult(false, Message: "搜索、回收站或按时间排序状态下不能调整顺序。");
        }

        if (movedItem.Kind != targetItem.Kind ||
            string.Equals(movedItem.RelativePath, targetItem.RelativePath, StringComparison.OrdinalIgnoreCase))
        {
            return new MacroLibraryOperationResult(false, Message: "只能在同一宏列表中调整不同条目的顺序。");
        }

        var ordered = activeItems
            .OrderBy(item => item.SortOrder)
            .ThenBy(item => item.Name, StringComparer.CurrentCultureIgnoreCase)
            .ToList();
        var moved = ordered.FirstOrDefault(item => string.Equals(item.RelativePath, movedItem.RelativePath, StringComparison.OrdinalIgnoreCase));
        var target = ordered.FirstOrDefault(item => string.Equals(item.RelativePath, targetItem.RelativePath, StringComparison.OrdinalIgnoreCase));
        if (moved is null || target is null)
        {
            return new MacroLibraryOperationResult(false, Message: "拖动条目已发生变化，请刷新后重试。");
        }

        ordered.Remove(moved);
        var targetIndex = ordered.IndexOf(target);
        ordered.Insert(Math.Clamp(targetIndex + (insertAfter ? 1 : 0), 0, ordered.Count), moved);
        var result = await RunOperationAsync(
            async () =>
            {
                await service.SetOrderAsync(RootPath, ordered.Select(item => item.RelativePath), cancellationToken).ConfigureAwait(false);
                return new MacroLibraryOperationResult(true, moved.RelativePath);
            },
            "已调整宏顺序",
            true,
            cancellationToken).ConfigureAwait(true);

        if (result.Succeeded)
        {
            if (moved.Kind == MacroLibraryItemKind.XMouseText)
            {
                SelectedXMouseItem = XMouseItems.FirstOrDefault(item => string.Equals(item.RelativePath, moved.RelativePath, StringComparison.OrdinalIgnoreCase));
            }
            else
            {
                SelectedRazerItem = RazerItems.FirstOrDefault(item => string.Equals(item.RelativePath, moved.RelativePath, StringComparison.OrdinalIgnoreCase));
            }
        }

        return result;
    }

    public Task<string> ReadTextAsync(MacroLibraryItem item, CancellationToken cancellationToken = default) =>
        service.ReadTextAsync(RootPath, item.RelativePath, cancellationToken);

    public string GetFullPath(MacroLibraryItem item) => service.GetFullPath(RootPath, item.RelativePath);

    public async Task LoadPreviewAsync(MacroLibraryItem item, CancellationToken cancellationToken = default)
    {
        var loadingText = "正在解析...";
        SetPreview(item.Kind, loadingText);
        if (item.IsTrashed)
        {
            SetPreview(item.Kind, $"回收站条目 · 原位置 {item.OriginalRelativePath}");
            return;
        }

        try
        {
            var result = await importService.ImportAsync(
                [GetFullPath(item)],
                new MacroImportOptions(MaximumFiles: 1),
                cancellationToken: cancellationToken).ConfigureAwait(true);
            if (result.Documents.Count == 0)
            {
                SetPreview(item.Kind, $"无法解析 · {result.Diagnostics.LastOrDefault()?.Message ?? "未知格式"}");
                return;
            }

            var document = result.Documents[0];
            var errorCount = result.Diagnostics.Count(diagnostic => diagnostic.Severity == Core.Diagnostics.DiagnosticSeverity.Error);
            var warningCount = result.Diagnostics.Count(diagnostic => diagnostic.Severity == Core.Diagnostics.DiagnosticSeverity.Warning);
            var state = errorCount > 0 ? $"{errorCount} 个错误" : warningCount > 0 ? $"{warningCount} 个警告" : "格式正常";
            SetPreview(item.Kind, $"{document.Name} · {document.Events.Count} 个事件 · {state}");
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or ArgumentException or InvalidOperationException or NotSupportedException)
        {
            SetPreview(item.Kind, $"无法解析 · {exception.Message}");
        }
    }

    public string PrepareSavePath(string groupName, string name, MacroLibraryItemKind kind, string? updateRelativePath = null) =>
        service.PrepareSavePath(RootPath, groupName, name, kind, updateRelativePath);

    public string GetSelectedGroupName() => SelectedScope switch
    {
        UngroupedScope => string.Empty,
        _ when SelectedScope.StartsWith(GroupPrefix, StringComparison.Ordinal) => SelectedScope[GroupPrefix.Length..],
        _ => string.Empty,
    };

    public IReadOnlyList<string> GetGroupNames() => GroupOptions
        .Where(option => option.Key.StartsWith(GroupPrefix, StringComparison.Ordinal))
        .Select(option => option.DisplayName)
        .ToArray();

    public bool GroupContainsOtherEntries(string groupName) => service.GroupContainsOtherEntries(RootPath, groupName);

    private async Task<MacroLibraryOperationResult> RunOperationAsync(
        Func<Task<MacroLibraryOperationResult>> operation,
        string successStatus,
        bool refresh,
        CancellationToken cancellationToken)
    {
        if (IsBusy)
        {
            return new MacroLibraryOperationResult(false, Message: "宏库正在执行其他操作。");
        }

        IsBusy = true;
        try
        {
            var result = await operation().ConfigureAwait(true);
            StatusText = result.Succeeded ? successStatus : result.Message ?? "操作未完成";
            if (refresh)
            {
                IsBusy = false;
                await RefreshAsync(cancellationToken).ConfigureAwait(true);
            }

            return result;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or ArgumentException or InvalidOperationException or NotSupportedException)
        {
            StatusText = "宏库操作失败";
            WarningsText = exception.Message;
            return new MacroLibraryOperationResult(false, Message: exception.Message);
        }
        finally
        {
            IsBusy = false;
        }
    }

    private void RebuildGroupOptions(IReadOnlyList<MacroLibraryGroup> groups)
    {
        var selected = SelectedScope;
        GroupOptions.Clear();
        GroupOptions.Add(new MacroLibraryGroupOption(AllScope, "全部宏", activeItems.Count));
        GroupOptions.Add(new MacroLibraryGroupOption(FavoritesScope, "收藏", activeItems.Count(item => item.IsFavorite)));
        GroupOptions.Add(new MacroLibraryGroupOption(RecentScope, "最近使用", activeItems.Count(item => item.LastUsedAt is not null)));
        GroupOptions.Add(new MacroLibraryGroupOption(UngroupedScope, "未分组", activeItems.Count(item => string.IsNullOrEmpty(item.GroupName))));
        foreach (var group in groups)
        {
            GroupOptions.Add(new MacroLibraryGroupOption(GroupPrefix + group.Name, group.Name, group.ItemCount));
        }

        GroupOptions.Add(new MacroLibraryGroupOption(TrashScope, "回收站", trashItems.Count));
        if (GroupOptions.All(option => !string.Equals(option.Key, selected, StringComparison.Ordinal)))
        {
            selected = AllScope;
        }

        selectedScope = selected;
        OnPropertyChanged(nameof(SelectedScope));
        NotifyActionState();
    }

    private void RebuildVisibleItems()
    {
        var source = SelectedScope == TrashScope ? trashItems : activeItems;
        IEnumerable<MacroLibraryItem> filtered = SelectedScope switch
        {
            FavoritesScope => source.Where(item => item.IsFavorite),
            RecentScope => source.Where(item => item.LastUsedAt is not null),
            UngroupedScope => source.Where(item => string.IsNullOrEmpty(item.GroupName)),
            TrashScope => source,
            _ when SelectedScope.StartsWith(GroupPrefix, StringComparison.Ordinal) =>
                source.Where(item => string.Equals(item.GroupName, SelectedScope[GroupPrefix.Length..], StringComparison.OrdinalIgnoreCase)),
            _ => source,
        };

        var query = SearchText.Trim();
        if (query.Length > 0)
        {
            filtered = filtered.Where(item =>
                item.Name.Contains(query, StringComparison.CurrentCultureIgnoreCase) ||
                item.GroupName.Contains(query, StringComparison.CurrentCultureIgnoreCase));
        }

        filtered = SortByModified
            ? filtered.OrderByDescending(item => item.LastWriteTime).ThenBy(item => item.Name, StringComparer.CurrentCultureIgnoreCase)
            : filtered.OrderBy(item => item.SortOrder).ThenBy(item => item.Name, StringComparer.CurrentCultureIgnoreCase);

        ReplaceCollection(XMouseItems, filtered.Where(item => item.Kind == MacroLibraryItemKind.XMouseText));
        ReplaceCollection(RazerItems, filtered.Where(item => item.Kind == MacroLibraryItemKind.RazerXml));
        SelectedXMouseItem = XMouseItems.FirstOrDefault();
        SelectedRazerItem = RazerItems.FirstOrDefault();
        OnPropertyChanged(nameof(HasXMouseItems));
        OnPropertyChanged(nameof(HasRazerItems));
        NotifyActionState();
    }

    private void SetPreview(MacroLibraryItemKind kind, string value)
    {
        if (kind == MacroLibraryItemKind.XMouseText)
        {
            XMousePreviewText = value;
        }
        else
        {
            RazerPreviewText = value;
        }
    }

    private static void ReplaceCollection(ObservableCollection<MacroLibraryItem> target, IEnumerable<MacroLibraryItem> source)
    {
        target.Clear();
        foreach (var item in source)
        {
            target.Add(item);
        }
    }

    private void NotifyActionState()
    {
        OnPropertyChanged(nameof(HasSelectedXMouseItem));
        OnPropertyChanged(nameof(HasSelectedRazerItem));
        OnPropertyChanged(nameof(CanUseSelectedXMouseItem));
        OnPropertyChanged(nameof(CanUseSelectedRazerItem));
        OnPropertyChanged(nameof(CanRenameSelectedGroup));
        OnPropertyChanged(nameof(CanDeleteSelectedGroup));
        OnPropertyChanged(nameof(CanEmptyTrash));
        OnPropertyChanged(nameof(CanModifyLibrary));
        OnPropertyChanged(nameof(CanReorderLibraryItems));
    }

    private void ConfigureWatcher()
    {
        watcher?.Dispose();
        watcher = null;
        if (!Directory.Exists(RootPath))
        {
            return;
        }

        try
        {
            watcher = new FileSystemWatcher(RootPath)
            {
                IncludeSubdirectories = true,
                NotifyFilter = NotifyFilters.FileName | NotifyFilters.DirectoryName | NotifyFilters.LastWrite | NotifyFilters.Size,
                EnableRaisingEvents = true,
            };
            watcher.Changed += Watcher_Changed;
            watcher.Created += Watcher_Changed;
            watcher.Deleted += Watcher_Changed;
            watcher.Renamed += Watcher_Changed;
            watcher.Error += Watcher_Error;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or ArgumentException)
        {
            WarningsText = $"无法自动监视宏库，将使用手动刷新：{exception.Message}";
        }
    }

    private void Watcher_Changed(object sender, FileSystemEventArgs e)
    {
        if (!disposed)
        {
            refreshTimer.Change(500, Timeout.Infinite);
        }
    }

    private void Watcher_Error(object sender, ErrorEventArgs e)
    {
        watcher?.Dispose();
        watcher = null;
        WarningsText = "宏库自动刷新已暂停，请使用刷新按钮。";
    }

    private void PostRefresh()
    {
        if (disposed)
        {
            return;
        }

        if (synchronizationContext is null)
        {
            _ = RefreshAsync();
        }
        else
        {
            synchronizationContext.Post(async _ => await RefreshAsync().ConfigureAwait(true), null);
        }
    }

    public void Dispose()
    {
        if (disposed)
        {
            return;
        }

        disposed = true;
        watcher?.Dispose();
        refreshTimer.Dispose();
        GC.SuppressFinalize(this);
    }
}
