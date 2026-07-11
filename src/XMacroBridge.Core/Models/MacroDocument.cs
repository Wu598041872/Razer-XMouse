namespace XMacroBridge.Core.Models;

public sealed record MacroDocument(
    Guid Id,
    string Name,
    IReadOnlyList<MacroEvent> Events,
    string? SourceFormat = null,
    string? SourcePath = null);
