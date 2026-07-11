using XMacroBridge.Core.Diagnostics;
using XMacroBridge.Core.Models;

namespace XMacroBridge.Core.Abstractions;

public interface IMacroImporter
{
    string FormatId { get; }

    bool CanImport(ReadOnlySpan<byte> header, string? fileName);

    Task<MacroImportResult> ImportAsync(
        Stream input,
        string? sourceName,
        CancellationToken cancellationToken = default);
}

public sealed record MacroImportResult(
    IReadOnlyList<MacroDocument> Documents,
    IReadOnlyList<ConversionDiagnostic> Diagnostics);
