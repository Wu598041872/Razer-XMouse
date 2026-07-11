using XMacroBridge.Core.Conversion;
using XMacroBridge.Core.Models;

namespace XMacroBridge.Core.Abstractions;

public interface IMacroValidator
{
    MacroValidationResult Validate(MacroDocument document, MacroLimits? limits = null);
}
