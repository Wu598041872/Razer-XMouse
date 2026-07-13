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
    int SortOrder = int.MaxValue);

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

public sealed record MacroLibraryAppSettings(string LibraryRootPath);
