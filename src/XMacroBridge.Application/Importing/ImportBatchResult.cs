using XMacroBridge.Core.Diagnostics;
using XMacroBridge.Core.Models;

namespace XMacroBridge.Application.Importing;

public sealed record ImportBatchResult(
    IReadOnlyList<MacroDocument> Documents,
    IReadOnlyList<ConversionDiagnostic> Diagnostics,
    IReadOnlyList<string> ProcessedFiles);
