using XMacroBridge.Core.Diagnostics;
using XMacroBridge.Core.Models;

namespace XMacroBridge.Core.Abstractions;

public interface IMacroExporter
{
    string FormatId { get; }

    Task<IReadOnlyList<ConversionDiagnostic>> ExportAsync(
        MacroDocument document,
        Stream output,
        CancellationToken cancellationToken = default);
}
