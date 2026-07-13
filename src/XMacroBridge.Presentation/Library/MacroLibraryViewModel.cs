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
    private string selectedFormat = "all";
    private string selectedSort = "manual";
    private bool isBusy;
    private string statusText = "宏库尚未载入";
    private string warningsText = string.Empty;
    private MacroLibraryItem? selectedXMouseItem;
    private MacroLibraryItem? selectedRazerItem;
    private MacroLibraryItem? selectedItem;
    private string xMousePreviewText = "选择条目以查看解析状态";
    private string razerPreviewText = "选择条目以查看解析状态";
    private string previewText = "选择宏以查看解析状态";
    private string previewContentText = "";
    private CancellationTokenSource? previewCancellation;
    private int previewGeneration;
    private CancellationTokenSource? settingsSaveCancellation;
    private MacroLibraryAppSettings appSettings = new(MacroLibrarySettingsService.DefaultLibraryRootPath);
    private string settingsSaveStatus = "所有设置已保存";
    private bool applyingSettings;
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
        FormatOptions =
        [
            new MacroLibraryFormatOption("all", "全部"),
            new MacroLibraryFormatOption("xmouse", "XMouse"),
            new MacroLibraryFormatOption("razer", "雷云 4"),
        ];
        SortOptions =
        [
            new MacroLibrarySortOption("manual", "手动排序"),
            new MacroLibrarySortOption("name", "按名称"),
            new MacroLibrarySortOption("modified", "按修改时间"),
        ];
        TargetFormatOptions =
        [
            new SettingsOption("razer.macro.xml", "雷云 4 宏 XML"),
            new SettingsOption("xmbc.macro.text", "XMouse 宏文本"),
        ];
        SameNameOptions =
        [
            new SettingsOption("ask", "询问我"),
            new SettingsOption("rename", "自动生成新名称"),
            new SettingsOption("overwrite", "确认后覆盖"),
        ];
        TimelineDensityOptions =
        [
            new SettingsOption("compact", "紧凑（44 px）"),
            new SettingsOption("comfortable", "舒适（52 px）"),
        ];
        FeedbackDurationOptions =
        [
            new SettingsOption("2000", "2 秒"),
            new SettingsOption("3000", "3 秒"),
            new SettingsOption("5000", "5 秒"),
        ];
    }

    public ObservableCollection<MacroLibraryGroupOption> GroupOptions { get; } = [];
    public ObservableCollection<MacroLibraryItem> XMouseItems { get; } = [];
    public ObservableCollection<MacroLibraryItem> RazerItems { get; } = [];
    public ObservableCollection<MacroLibraryItem> VisibleItems { get; } = [];

    public IReadOnlyList<MacroLibraryFormatOption> FormatOptions { get; }

    public IReadOnlyList<MacroLibrarySortOption> SortOptions { get; }

    public IReadOnlyList<SettingsOption> TargetFormatOptions { get; }

    public IReadOnlyList<SettingsOption> SameNameOptions { get; }

    public IReadOnlyList<SettingsOption> TimelineDensityOptions { get; }

    public IReadOnlyList<SettingsOption> FeedbackDurationOptions { get; }

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
                OnPropertyChanged(nameof(SelectedScopeDisplayName));
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
                selectedSort = value ? "modified" : "manual";
                OnPropertyChanged(nameof(SelectedSort));
                RebuildVisibleItems();
                NotifyActionState();
            }
        }
    }

    public string SelectedFormat
    {
        get => selectedFormat;
        set
        {
            if (SetProperty(ref selectedFormat, value ?? "all"))
            {
                RebuildVisibleItems();
                NotifyActionState();
            }
        }
    }

    public string SelectedSort
    {
        get => selectedSort;
        set
        {
            var normalized = value is "name" or "modified" ? value : "manual";
            if (SetProperty(ref selectedSort, normalized))
            {
                sortByModified = normalized == "modified";
                OnPropertyChanged(nameof(SortByModified));
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

    public string SettingsPath => settingsService.SettingsPath;

    public IReadOnlyList<string> LastWorkspacePaths => appSettings.LastWorkspacePaths ?? [];

    public string SettingsSaveStatus
    {
        get => settingsSaveStatus;
        private set => SetProperty(ref settingsSaveStatus, value);
    }

    public string DefaultTargetFormat
    {
        get => appSettings.DefaultTargetFormat;
        set => UpdateSettings(appSettings with { DefaultTargetFormat = value ?? "razer.macro.xml" }, nameof(DefaultTargetFormat));
    }

    public bool RunSafetyCheckBeforeExport
    {
        get => appSettings.RunSafetyCheckBeforeExport;
        set => UpdateSettings(appSettings with { RunSafetyCheckBeforeExport = value }, nameof(RunSafetyCheckBeforeExport));
    }

    public bool OpenOutputFolderAfterExport
    {
        get => appSettings.OpenOutputFolderAfterExport;
        set => UpdateSettings(appSettings with { OpenOutputFolderAfterExport = value }, nameof(OpenOutputFolderAfterExport));
    }

    public string SameNameHandling
    {
        get => appSettings.SameNameHandling;
        set => UpdateSettings(appSettings with { SameNameHandling = value ?? "ask" }, nameof(SameNameHandling));
    }

    public bool PreserveMacroName
    {
        get => appSettings.PreserveMacroName;
        set => UpdateSettings(appSettings with { PreserveMacroName = value }, nameof(PreserveMacroName));
    }

    public bool RestorePreviousWorkspace
    {
        get => appSettings.RestorePreviousWorkspace;
        set => UpdateSettings(appSettings with { RestorePreviousWorkspace = value }, nameof(RestorePreviousWorkspace));
    }

    public bool CheckUnsavedBeforeClose
    {
        get => appSettings.CheckUnsavedBeforeClose;
        set => UpdateSettings(appSettings with { CheckUnsavedBeforeClose = value }, nameof(CheckUnsavedBeforeClose));
    }

    public string TimelineDensity
    {
        get => appSettings.TimelineDensity;
        set => UpdateSettings(appSettings with { TimelineDensity = value ?? "compact" }, nameof(TimelineDensity));
    }

    public string FeedbackDurationKey
    {
        get => appSettings.FeedbackDurationMilliseconds.ToString(System.Globalization.CultureInfo.InvariantCulture);
        set
        {
            var duration = int.TryParse(value, out var parsed) ? Math.Clamp(parsed, 1000, 10000) : 3000;
            UpdateSettings(appSettings with { FeedbackDurationMilliseconds = duration }, nameof(FeedbackDurationKey));
        }
    }

    public bool DeleteToTrash
    {
        get => appSettings.DeleteToTrash;
        set => UpdateSettings(appSettings with { DeleteToTrash = value }, nameof(DeleteToTrash));
    }

    public bool AutoLoadPreview
    {
        get => appSettings.AutoLoadPreview;
        set => UpdateSettings(appSettings with { AutoLoadPreview = value }, nameof(AutoLoadPreview));
    }

    public async Task SaveWorkspacePathsAsync(IEnumerable<string> paths, CancellationToken cancellationToken = default)
    {
        var normalized = paths
            .Where(path => !string.IsNullOrWhiteSpace(path))
            .Select(Path.GetFullPath)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
        appSettings = appSettings with { LibraryRootPath = RootPath, LastWorkspacePaths = normalized };
        settingsSaveCancellation?.Cancel();
        settingsSaveCancellation?.Dispose();
        settingsSaveCancellation = null;
        SettingsSaveStatus = "正在保存…";
        await settingsService.SaveAsync(appSettings, cancellationToken).ConfigureAwait(true);
        SettingsSaveStatus = "所有设置已保存";
        OnPropertyChanged(nameof(LastWorkspacePaths));
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

    public MacroLibraryItem? SelectedItem
    {
        get => selectedItem;
        set
        {
            if (ReferenceEquals(selectedItem, value))
            {
                return;
            }

            selectedItem = value;
            OnPropertyChanged();
            selectedXMouseItem = value?.Kind == MacroLibraryItemKind.XMouseText ? value : null;
            selectedRazerItem = value?.Kind == MacroLibraryItemKind.RazerXml ? value : null;
            OnPropertyChanged(nameof(SelectedXMouseItem));
            OnPropertyChanged(nameof(SelectedRazerItem));
            if (value is null)
            {
                CancelPreview();
                PreviewText = "当前筛选没有可预览的宏";
                PreviewContentText = string.Empty;
            }
            else if (!IsBusy)
            {
                StatusText = $"已选择“{value.Name}” · {value.FormatDisplayName}";
            }

            NotifyActionState();
        }
    }

    public bool HasXMouseItems => XMouseItems.Count > 0;

    public bool HasRazerItems => RazerItems.Count > 0;

    public bool HasVisibleItems => VisibleItems.Count > 0;

    public bool HasSelectedItem => SelectedItem is not null;

    public bool CanUseSelectedItem => !IsBusy && SelectedItem is { IsTrashed: false };

    public bool CanEditSelectedText => CanUseSelectedItem && SelectedItem?.Kind == MacroLibraryItemKind.XMouseText;

    public string SelectedItemFormatText => SelectedItem?.FormatDisplayName ?? string.Empty;

    public string SelectedItemPathText => SelectedItem?.DisplayPath ?? string.Empty;

    public string SelectedItemEventCountText => SelectedItem?.EventCount?.ToString(System.Globalization.CultureInfo.InvariantCulture) ?? "—";

    public string SelectedItemModifiedText => SelectedItem?.ModifiedDisplayText ?? string.Empty;

    public string SelectedScopeDisplayName => GroupOptions
        .FirstOrDefault(option => string.Equals(option.Key, SelectedScope, StringComparison.Ordinal))?.DisplayName ?? "全部宏";

    public string VisibleItemCountText => $"{VisibleItems.Count} 个条目";

    public string SelectedItemMetadataText => SelectedItem is null
        ? string.Empty
        : $"{(string.IsNullOrWhiteSpace(SelectedItem.GroupName) ? "未分组" : SelectedItem.GroupName)} · {SelectedItem.LastWriteTime.LocalDateTime:yyyy-MM-dd HH:mm} · {SelectedItem.Length:N0} B";

    public string FavoriteActionText => SelectedItem?.IsFavorite == true ? "取消收藏" : "收藏";

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
        !IsBusy && SelectedSort == "manual" && SelectedScope != TrashScope && string.IsNullOrWhiteSpace(SearchText);

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

    public string PreviewText
    {
        get => previewText;
        private set => SetProperty(ref previewText, value);
    }

    public string PreviewContentText
    {
        get => previewContentText;
        private set => SetProperty(ref previewContentText, value);
    }

    public async Task InitializeAsync(CancellationToken cancellationToken = default)
    {
        var (settings, warning) = await settingsService.LoadAsync(cancellationToken).ConfigureAwait(true);
        ApplySettings(settings);
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
        appSettings = appSettings with { LibraryRootPath = RootPath };
        SettingsSaveStatus = "正在保存…";
        await settingsService.SaveAsync(appSettings, cancellationToken).ConfigureAwait(true);
        SettingsSaveStatus = "所有设置已保存";
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
            activeItems = await EnrichItemsAsync(snapshot.Items, cancellationToken).ConfigureAwait(true);
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

    public Task<MacroLibraryOperationResult> CopyItemAsync(MacroLibraryItem item, CancellationToken cancellationToken = default) =>
        RunOperationAsync(() => service.CopyItemAsync(RootPath, item.RelativePath, cancellationToken), $"已复制“{item.Name}”", true, cancellationToken);

    public Task<MacroLibraryOperationResult> DeleteItemPermanentlyAsync(MacroLibraryItem item, CancellationToken cancellationToken = default) =>
        RunOperationAsync(() => service.DeleteItemPermanentlyAsync(RootPath, item.RelativePath, cancellationToken), "宏已永久删除", true, cancellationToken);

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

        if (string.Equals(movedItem.RelativePath, targetItem.RelativePath, StringComparison.OrdinalIgnoreCase))
        {
            return new MacroLibraryOperationResult(false, Message: "请选择不同的目标条目。");
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
            SelectedItem = VisibleItems.FirstOrDefault(item =>
                string.Equals(item.RelativePath, moved.RelativePath, StringComparison.OrdinalIgnoreCase));
        }

        return result;
    }

    public Task<string> ReadTextAsync(MacroLibraryItem item, CancellationToken cancellationToken = default) =>
        service.ReadTextAsync(RootPath, item.RelativePath, cancellationToken);

    public void ClearPreview()
    {
        CancelPreview();
        PreviewText = "自动预览已关闭；可手动加载当前宏。";
        PreviewContentText = string.Empty;
    }

    public async Task LoadRawPreviewAsync(MacroLibraryItem item, CancellationToken cancellationToken = default)
    {
        CancelPreview();
        if (item.IsTrashed)
        {
            PreviewText = $"回收站条目 · 原位置 {item.OriginalRelativePath}";
            PreviewContentText = string.Empty;
            return;
        }

        PreviewText = item.ParseSummary ?? item.EventCountText;
        PreviewContentText = await service.ReadContentAsync(RootPath, item.RelativePath, cancellationToken).ConfigureAwait(true);
    }

    public string GetFullPath(MacroLibraryItem item) => service.GetFullPath(RootPath, item.RelativePath);

    public async Task LoadPreviewAsync(MacroLibraryItem item, CancellationToken cancellationToken = default)
    {
        CancelPreview();
        var generation = ++previewGeneration;
        using var linkedCancellation = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        previewCancellation = linkedCancellation;
        var loadingText = "正在解析...";
        SetPreview(item.Kind, loadingText);
        PreviewText = loadingText;
        PreviewContentText = string.Empty;
        if (item.IsTrashed)
        {
            var trashText = $"回收站条目 · 原位置 {item.OriginalRelativePath}";
            SetPreview(item.Kind, trashText);
            PreviewText = trashText;
            previewCancellation = null;
            return;
        }

        try
        {
            PreviewContentText = await service.ReadContentAsync(RootPath, item.RelativePath, linkedCancellation.Token).ConfigureAwait(true);
            if (!IsCurrentPreview(item, generation))
            {
                return;
            }

            var result = await importService.ImportAsync(
                [GetFullPath(item)],
                new MacroImportOptions(MaximumFiles: 1),
                cancellationToken: linkedCancellation.Token).ConfigureAwait(true);
            if (!IsCurrentPreview(item, generation))
            {
                return;
            }

            if (result.Documents.Count == 0)
            {
                var failureText = $"无法解析 · {result.Diagnostics.LastOrDefault()?.Message ?? "未知格式"}";
                SetPreview(item.Kind, failureText);
                PreviewText = failureText;
                return;
            }

            var document = result.Documents[0];
            var errorCount = result.Diagnostics.Count(diagnostic => diagnostic.Severity == Core.Diagnostics.DiagnosticSeverity.Error);
            var warningCount = result.Diagnostics.Count(diagnostic => diagnostic.Severity == Core.Diagnostics.DiagnosticSeverity.Warning);
            var state = errorCount > 0 ? $"{errorCount} 个错误" : warningCount > 0 ? $"{warningCount} 个警告" : "格式正常";
            var summary = $"{document.Name} · {document.Events.Count} 个事件 · {state}";
            SetPreview(item.Kind, summary);
            PreviewText = summary;
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or ArgumentException or InvalidOperationException or NotSupportedException)
        {
            if (IsCurrentPreview(item, generation))
            {
                var failureText = $"无法解析 · {exception.Message}";
                SetPreview(item.Kind, failureText);
                PreviewText = failureText;
            }
        }
        finally
        {
            if (ReferenceEquals(previewCancellation, linkedCancellation))
            {
                previewCancellation = null;
            }
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

    private void ApplySettings(MacroLibraryAppSettings settings)
    {
        applyingSettings = true;
        appSettings = settings with
        {
            LibraryRootPath = Path.GetFullPath(settings.LibraryRootPath),
            DefaultTargetFormat = settings.DefaultTargetFormat is "xmbc.macro.text" ? settings.DefaultTargetFormat : "razer.macro.xml",
            SameNameHandling = settings.SameNameHandling is "rename" or "overwrite" ? settings.SameNameHandling : "ask",
            TimelineDensity = settings.TimelineDensity == "comfortable" ? "comfortable" : "compact",
            FeedbackDurationMilliseconds = Math.Clamp(settings.FeedbackDurationMilliseconds, 1000, 10000),
        };
        applyingSettings = false;
        foreach (var propertyName in new[]
                 {
                     nameof(DefaultTargetFormat), nameof(RunSafetyCheckBeforeExport), nameof(OpenOutputFolderAfterExport),
                     nameof(SameNameHandling), nameof(PreserveMacroName), nameof(RestorePreviousWorkspace),
                     nameof(CheckUnsavedBeforeClose), nameof(TimelineDensity), nameof(FeedbackDurationKey),
                     nameof(DeleteToTrash), nameof(AutoLoadPreview), nameof(SettingsPath), nameof(LastWorkspacePaths),
                 })
        {
            OnPropertyChanged(propertyName);
        }
    }

    private void UpdateSettings(MacroLibraryAppSettings next, string propertyName)
    {
        if (appSettings == next)
        {
            return;
        }

        appSettings = next with { LibraryRootPath = RootPath };
        OnPropertyChanged(propertyName);
        if (!applyingSettings)
        {
            ScheduleSettingsSave();
        }
    }

    private void ScheduleSettingsSave()
    {
        settingsSaveCancellation?.Cancel();
        settingsSaveCancellation?.Dispose();
        settingsSaveCancellation = new CancellationTokenSource();
        _ = SaveSettingsAfterDelayAsync(settingsSaveCancellation);
    }

    private async Task SaveSettingsAfterDelayAsync(CancellationTokenSource owner)
    {
        SettingsSaveStatus = "正在保存…";
        try
        {
            await Task.Delay(300, owner.Token).ConfigureAwait(true);
            await settingsService.SaveAsync(appSettings, owner.Token).ConfigureAwait(true);
            if (ReferenceEquals(settingsSaveCancellation, owner))
            {
                SettingsSaveStatus = "所有设置已保存";
            }
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or ArgumentException or InvalidOperationException or NotSupportedException)
        {
            if (ReferenceEquals(settingsSaveCancellation, owner))
            {
                SettingsSaveStatus = $"保存失败：{exception.Message}";
            }
        }
        finally
        {
            if (ReferenceEquals(settingsSaveCancellation, owner))
            {
                settingsSaveCancellation = null;
            }

            owner.Dispose();
        }
    }

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
        var previousRelativePath = SelectedItem?.RelativePath;
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

        filtered = SelectedFormat switch
        {
            "xmouse" => filtered.Where(item => item.Kind == MacroLibraryItemKind.XMouseText),
            "razer" => filtered.Where(item => item.Kind == MacroLibraryItemKind.RazerXml),
            _ => filtered,
        };

        filtered = SelectedSort switch
        {
            "modified" => filtered.OrderByDescending(item => item.LastWriteTime).ThenBy(item => item.Name, StringComparer.CurrentCultureIgnoreCase),
            "name" => filtered.OrderBy(item => item.Name, StringComparer.CurrentCultureIgnoreCase),
            _ => filtered.OrderBy(item => item.SortOrder).ThenBy(item => item.Name, StringComparer.CurrentCultureIgnoreCase),
        };

        var visible = filtered.ToArray();
        ReplaceCollection(VisibleItems, visible);
        ReplaceCollection(XMouseItems, visible.Where(item => item.Kind == MacroLibraryItemKind.XMouseText));
        ReplaceCollection(RazerItems, visible.Where(item => item.Kind == MacroLibraryItemKind.RazerXml));
        SelectedItem = previousRelativePath is null
            ? VisibleItems.FirstOrDefault()
            : VisibleItems.FirstOrDefault(item => string.Equals(item.RelativePath, previousRelativePath, StringComparison.OrdinalIgnoreCase))
                ?? VisibleItems.FirstOrDefault();
        OnPropertyChanged(nameof(HasXMouseItems));
        OnPropertyChanged(nameof(HasRazerItems));
        OnPropertyChanged(nameof(HasVisibleItems));
        OnPropertyChanged(nameof(VisibleItemCountText));
        OnPropertyChanged(nameof(SelectedScopeDisplayName));
        NotifyActionState();
    }

    private async Task<IReadOnlyList<MacroLibraryItem>> EnrichItemsAsync(
        IReadOnlyList<MacroLibraryItem> items,
        CancellationToken cancellationToken)
    {
        var enriched = new MacroLibraryItem[items.Count];
        for (var index = 0; index < items.Count; index++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var item = items[index];
            StatusText = $"正在读取宏信息 {index + 1} / {items.Count}";
            try
            {
                var result = await importService.ImportAsync(
                    [GetFullPath(item)],
                    new MacroImportOptions(MaximumFiles: 1),
                    cancellationToken: cancellationToken).ConfigureAwait(true);
                var eventCount = result.Documents.Sum(document => document.Events.Count);
                var errorCount = result.Diagnostics.Count(diagnostic => diagnostic.Severity == Core.Diagnostics.DiagnosticSeverity.Error);
                var warningCount = result.Diagnostics.Count(diagnostic => diagnostic.Severity == Core.Diagnostics.DiagnosticSeverity.Warning);
                var parseSummary = result.Documents.Count == 0
                    ? $"无法解析 · {result.Diagnostics.LastOrDefault()?.Message ?? "未知格式"}"
                    : errorCount > 0 ? $"{errorCount} 个错误" : warningCount > 0 ? $"{warningCount} 个警告" : "格式正常";
                enriched[index] = item with
                {
                    EventCount = result.Documents.Count == 0 ? null : eventCount,
                    ParseSummary = parseSummary,
                };
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or ArgumentException or InvalidOperationException or NotSupportedException)
            {
                enriched[index] = item with { ParseSummary = $"无法解析 · {exception.Message}" };
            }
        }

        return enriched;
    }

    private bool IsCurrentPreview(MacroLibraryItem item, int generation) =>
        generation == previewGeneration &&
        SelectedItem is not null &&
        string.Equals(SelectedItem.RelativePath, item.RelativePath, StringComparison.OrdinalIgnoreCase);

    private void CancelPreview()
    {
        previewGeneration++;
        previewCancellation?.Cancel();
        previewCancellation?.Dispose();
        previewCancellation = null;
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
        OnPropertyChanged(nameof(HasVisibleItems));
        OnPropertyChanged(nameof(HasSelectedItem));
        OnPropertyChanged(nameof(CanUseSelectedItem));
        OnPropertyChanged(nameof(CanEditSelectedText));
        OnPropertyChanged(nameof(SelectedItemFormatText));
        OnPropertyChanged(nameof(SelectedItemPathText));
        OnPropertyChanged(nameof(SelectedItemEventCountText));
        OnPropertyChanged(nameof(SelectedItemModifiedText));
        OnPropertyChanged(nameof(VisibleItemCountText));
        OnPropertyChanged(nameof(SelectedItemMetadataText));
        OnPropertyChanged(nameof(FavoriteActionText));
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
        CancelPreview();
        settingsSaveCancellation?.Cancel();
        settingsSaveCancellation?.Dispose();
        settingsSaveCancellation = null;
        watcher?.Dispose();
        refreshTimer.Dispose();
        GC.SuppressFinalize(this);
    }
}
