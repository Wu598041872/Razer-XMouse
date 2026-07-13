using XMacroBridge.Core.Diagnostics;

namespace XMacroBridge.Application.Exporting;

public sealed record MacroTextResult(
    string? Text,
    IReadOnlyList<ConversionDiagnostic> Diagnostics);
