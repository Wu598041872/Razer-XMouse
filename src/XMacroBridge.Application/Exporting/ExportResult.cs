using XMacroBridge.Core.Diagnostics;

namespace XMacroBridge.Application.Exporting;

public sealed record ExportResult(
    bool Succeeded,
    string? OutputPath,
    IReadOnlyList<ConversionDiagnostic> Diagnostics);
