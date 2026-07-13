namespace XMacroBridge.Presentation.Library;

public sealed record MacroLibraryGroupOption(string Key, string DisplayName, int ItemCount = 0);

public sealed record MacroLibraryFormatOption(string Key, string DisplayName);

public sealed record MacroLibrarySortOption(string Key, string DisplayName);

public sealed record SettingsOption(string Key, string DisplayName);
