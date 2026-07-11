using XMacroBridge.Core.Conversion;
using XMacroBridge.Core.Diagnostics;
using XMacroBridge.Core.Models;

namespace XMacroBridge.Core.Abstractions;

public interface INestedMacroResolver
{
    MacroResolutionResult Resolve(
        MacroDocument root,
        IReadOnlyList<MacroDocument> availableDocuments,
        MacroLimits? limits = null);
}

public sealed record MacroResolutionResult(
    MacroDocument? Document,
    IReadOnlyList<ConversionDiagnostic> Diagnostics);
