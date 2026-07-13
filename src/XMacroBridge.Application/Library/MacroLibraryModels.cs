namespace XMacroBridge.Application.Library;

public enum MacroLibraryItemKind
{
    XMouseText,
    RazerXml,
}

public sealed record MacroLibraryItem(
    string RelativePath,
    string Name,
    string GroupName,
    MacroLibraryItemKind Kind,
    long Length,
    DateTimeOffset LastWriteTime,
    bool IsFavorite,
    DateTimeOffset? LastUsedAt = null,
    bool IsTrashed = false,
    string? OriginalRelativePath = null,
    int SortOrder = int.MaxValue,
    int? EventCount = null,
    string? ParseSummary = null)
{
    public string DisplayGroupName => IsTrashed
        ? "回收站"
        : string.IsNullOrWhiteSpace(GroupName) ? "未分组" : GroupName;

    public string FormatMark => Kind == MacroLibraryItemKind.XMouseText ? "XM" : "R4";

    public string FormatDisplayName => Kind == MacroLibraryItemKind.XMouseText ? "XMouse" : "雷云 4";

    public string EventCountText => EventCount is { } count ? $"{count} 个事件" : "事件数未知";

    public string ModifiedDisplayText
    {
        get
        {
            var local = LastWriteTime.LocalDateTime;
            var today = DateTime.Today;
            if (local.Date == today)
            {
                return $"今天 {local:HH:mm}";
            }

            if (local.Date == today.AddDays(-1))
            {
                return $"昨天 {local:HH:mm}";
            }

            return local.Year == today.Year ? $"{local:M 月 d 日}" : $"{local:yyyy-MM-dd}";
        }
    }

    public string SecondaryText => $"{DisplayGroupName} · {EventCountText} · {ModifiedDisplayText}";

    public string DisplayPath
    {
        get
        {
            var relative = IsTrashed && !string.IsNullOrWhiteSpace(OriginalRelativePath)
                ? OriginalRelativePath
                : RelativePath;
            return $"{DisplayGroupName} / {Path.GetFileName(relative)}";
        }
    }
}

public sealed record MacroLibraryGroup(string Name, int ItemCount);

public sealed record MacroLibrarySnapshot(
    IReadOnlyList<MacroLibraryGroup> Groups,
    IReadOnlyList<MacroLibraryItem> Items,
    IReadOnlyList<MacroLibraryItem> TrashItems,
    IReadOnlyList<string> Warnings);

public sealed record MacroLibraryOperationResult(
    bool Succeeded,
    string? RelativePath = null,
    string? Message = null);

public sealed record MacroLibraryAppSettings(
    string LibraryRootPath,
    string DefaultTargetFormat = "razer.macro.xml",
    bool RunSafetyCheckBeforeExport = true,
    bool OpenOutputFolderAfterExport = false,
    string SameNameHandling = "ask",
    bool PreserveMacroName = true,
    bool RestorePreviousWorkspace = false,
    bool CheckUnsavedBeforeClose = true,
    string TimelineDensity = "compact",
    int FeedbackDurationMilliseconds = 3000,
    bool DeleteToTrash = true,
    bool AutoLoadPreview = true,
    IReadOnlyList<string>? LastWorkspacePaths = null);
