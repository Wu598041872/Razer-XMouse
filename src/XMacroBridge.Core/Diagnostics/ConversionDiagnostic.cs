namespace XMacroBridge.Core.Diagnostics;

public sealed record ConversionDiagnostic(
    string Code,
    DiagnosticSeverity Severity,
    string Message,
    long? EventSequence = null,
    string? SourceContext = null);
