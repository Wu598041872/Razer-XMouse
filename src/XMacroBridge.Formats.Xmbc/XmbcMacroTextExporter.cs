using System.Globalization;
using System.Text;
using XMacroBridge.Core.Abstractions;
using XMacroBridge.Core.Conversion;
using XMacroBridge.Core.Diagnostics;
using XMacroBridge.Core.Models;

namespace XMacroBridge.Formats.Xmbc;

public sealed class XmbcMacroTextExporter : IMacroExporter
{
    public string FormatId => "xmbc.macro.text";

    public async Task<IReadOnlyList<ConversionDiagnostic>> ExportAsync(
        MacroDocument document,
        Stream output,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(document);
        ArgumentNullException.ThrowIfNull(output);

        var diagnostics = new List<ConversionDiagnostic>();
        diagnostics.AddRange(new MacroValidator().Validate(document).Diagnostics);
        var builder = new StringBuilder();
        if (!diagnostics.Any(item => item.Severity == DiagnosticSeverity.Error))
        {
            foreach (var macroEvent in document.Events.OrderBy(item => item.Sequence))
            {
                cancellationToken.ThrowIfCancellationRequested();
                switch (macroEvent)
                {
                    case DelayMacroEvent delay:
                        builder.Append("{WAITMS:");
                        builder.Append(delay.Milliseconds.ToString(CultureInfo.InvariantCulture));
                        builder.Append('}');
                        break;
                    case KeyMacroEvent key when TryFormatKey(key.VirtualKey, out var keyText):
                        builder.Append(key.Transition == InputTransition.Down ? "{PRESS}" : "{RELEASE}");
                        builder.Append(keyText);
                        break;
                    case KeyMacroEvent key:
                        diagnostics.Add(new ConversionDiagnostic(
                            "XMBC_EXPORT_KEY_UNSUPPORTED",
                            DiagnosticSeverity.Error,
                            $"XMBC 文本导出尚不支持虚拟键码 {key.VirtualKey}。",
                            key.Sequence));
                        break;
                    case MouseMacroEvent mouse when TryFormatMouse(mouse, out var mouseText):
                        builder.Append(mouseText);
                        break;
                    case MouseMacroEvent mouse:
                        diagnostics.Add(new ConversionDiagnostic(
                            "XMBC_EXPORT_MOUSE_UNSUPPORTED",
                            DiagnosticSeverity.Error,
                            $"XMBC 文本导出尚不支持鼠标按钮 {mouse.Button}。",
                            mouse.Sequence));
                        break;
                }
            }
        }

        if (diagnostics.Any(item => item.Severity == DiagnosticSeverity.Error))
        {
            return diagnostics;
        }

        var bytes = new UTF8Encoding(false).GetBytes(builder.ToString());
        await output.WriteAsync(bytes, cancellationToken).ConfigureAwait(false);
        return diagnostics;
    }

    private static bool TryFormatKey(int virtualKey, out string text)
    {
        if (virtualKey is >= 0x41 and <= 0x5A or >= 0x30 and <= 0x39)
        {
            text = ((char)virtualKey).ToString().ToLowerInvariant();
            return true;
        }

        text = virtualKey switch
        {
            0x10 => "{SHIFT}",
            0x11 => "{CTRL}",
            0x12 => "{ALT}",
            0x20 => " ",
            _ => string.Empty,
        };
        return text.Length > 0;
    }

    private static bool TryFormatMouse(MouseMacroEvent mouse, out string text)
    {
        text = (mouse.Button, mouse.Transition) switch
        {
            (MouseButton.Left, InputTransition.Down) => "{LMBD}",
            (MouseButton.Left, InputTransition.Up) => "{LMBU}",
            (MouseButton.Right, InputTransition.Down) => "{RMBD}",
            (MouseButton.Right, InputTransition.Up) => "{RMBU}",
            _ => string.Empty,
        };
        return text.Length > 0;
    }
}
