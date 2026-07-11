using XMacroBridge.Core.Diagnostics;

namespace XMacroBridge.Core.Conversion;

public sealed record MacroValidationResult(IReadOnlyList<ConversionDiagnostic> Diagnostics)
{
    public bool HasErrors => Diagnostics.Any(item => item.Severity == DiagnosticSeverity.Error);
}
