using XMacroBridge.Core.Diagnostics;

namespace XMacroBridge.Presentation.Workspace;

public sealed record DiagnosticGroup(
    string Title,
    IReadOnlyList<ConversionDiagnostic> Diagnostics)
{
    public string CountText => $"{Diagnostics.Count} 条";
}
