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
        var orderedEvents = document.Events.OrderBy(item => item.Sequence).ToArray();
        ValidateAtomicMousePairs(orderedEvents, diagnostics);
        var builder = new StringBuilder();
        if (!diagnostics.Any(item => item.Severity == DiagnosticSeverity.Error))
        {
            for (var index = 0; index < orderedEvents.Length; index++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var macroEvent = orderedEvents[index];
                switch (macroEvent)
                {
                    case DelayMacroEvent delay:
                        builder.Append("{WAITMS:");
                        builder.Append(delay.Milliseconds.ToString(CultureInfo.InvariantCulture));
                        builder.Append('}');
                        break;
                    case RandomDelayMacroEvent randomDelay:
                        builder.Append("{WAITMS:");
                        builder.Append(randomDelay.MinimumMilliseconds.ToString(CultureInfo.InvariantCulture));
                        builder.Append('-');
                        builder.Append(randomDelay.MaximumMilliseconds.ToString(CultureInfo.InvariantCulture));
                        builder.Append('}');
                        break;
                    case KeyMacroEvent key when TryFormatKey(key, out var keyText):
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
                    case MouseMacroEvent mouse when IsAtomicMouseButton(mouse.Button):
                        builder.Append(FormatAtomicMouse(mouse.Button));
                        index++;
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
                    case ScanCodeMacroEvent scanCode:
                        builder.Append(scanCode.Transition == InputTransition.Down ? "{PRESS}" : "{RELEASE}");
                        builder.Append(scanCode.IsExtended ? "{SCE:" : "{SC:");
                        builder.Append(scanCode.ScanCode.ToString(CultureInfo.InvariantCulture));
                        builder.Append('}');
                        break;
                    case XmbcCommandMacroEvent command:
                        builder.Append(command.RawTag);
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

    private static bool TryFormatKey(KeyMacroEvent key, out string text)
    {
        if (key.IsExtended)
        {
            text = $"{{EXT:{key.VirtualKey.ToString(CultureInfo.InvariantCulture)}}}";
            return true;
        }

        if (key.VirtualKey is >= 0x41 and <= 0x5A or >= 0x30 and <= 0x39)
        {
            text = ((char)key.VirtualKey).ToString().ToLowerInvariant();
            return true;
        }

        if (key.VirtualKey is >= 0x70 and <= 0x87)
        {
            text = $"{{F{key.VirtualKey - 0x70 + 1}}}";
            return true;
        }

        if (key.VirtualKey is >= 0x60 and <= 0x69)
        {
            text = $"{{NUM{key.VirtualKey - 0x60}}}";
            return true;
        }

        text = key.VirtualKey switch
        {
            0x08 => "{BACKSPACE}",
            0x09 => "{TAB}",
            0x0D => "{RETURN}",
            0x13 => "{PAUSE}",
            0x14 => "{CAPSLOCK}",
            0x1B => "{ESCAPE}",
            0x20 => "{SPACE}",
            0x21 => "{PGUP}",
            0x22 => "{PGDN}",
            0x23 => "{END}",
            0x24 => "{HOME}",
            0x25 => "{LEFT}",
            0x26 => "{UP}",
            0x27 => "{RIGHT}",
            0x28 => "{DOWN}",
            0x2C => "{PRTSCN}",
            0x2D => "{INS}",
            0x2E => "{DEL}",
            0x6A => "{NUM*}",
            0x6B => "{NUM+}",
            0x6D => "{NUM-}",
            0x6E => "{NUM.}",
            0x6F => "{NUM/}",
            0x90 => "{NUMLOCK}",
            0x91 => "{SCROLLLOCK}",
            0xA6 => "{BACK}",
            0xA7 => "{FORWARD}",
            0xA8 => "{REFRESH}",
            0xA9 => "{STOP}",
            0xAA => "{SEARCH}",
            0xAB => "{FAVORITES}",
            0xAC => "{WEBHOME}",
            0xAD => "{MUTE}",
            0xAE => "{VOL-}",
            0xAF => "{VOL+}",
            0xB0 => "{MEDIANEXT}",
            0xB1 => "{MEDIAPREV}",
            0xB2 => "{MEDIASTOP}",
            0xB3 => "{MEDIAPLAY}",
            _ => $"{{VKC:{key.VirtualKey.ToString(CultureInfo.InvariantCulture)}}}",
        };
        return true;
    }

    private static bool TryFormatMouse(MouseMacroEvent mouse, out string text)
    {
        text = (mouse.Button, mouse.Transition) switch
        {
            (MouseButton.Left, InputTransition.Down) => "{LMBD}",
            (MouseButton.Left, InputTransition.Up) => "{LMBU}",
            (MouseButton.Right, InputTransition.Down) => "{RMBD}",
            (MouseButton.Right, InputTransition.Up) => "{RMBU}",
            (MouseButton.Middle, InputTransition.Down) => "{MMBD}",
            (MouseButton.Middle, InputTransition.Up) => "{MMBU}",
            (MouseButton.XButton1, InputTransition.Down) => "{MB4D}",
            (MouseButton.XButton1, InputTransition.Up) => "{MB4U}",
            (MouseButton.XButton2, InputTransition.Down) => "{MB5D}",
            (MouseButton.XButton2, InputTransition.Up) => "{MB5U}",
            _ => string.Empty,
        };
        return text.Length > 0;
    }

    private static void ValidateAtomicMousePairs(
        IReadOnlyList<MacroEvent> events,
        ICollection<ConversionDiagnostic> diagnostics)
    {
        for (var index = 0; index < events.Count; index++)
        {
            if (events[index] is not MouseMacroEvent mouse || !IsAtomicMouseButton(mouse.Button))
            {
                continue;
            }

            if (mouse.Transition == InputTransition.Down &&
                index + 1 < events.Count &&
                events[index + 1] is MouseMacroEvent
                {
                    Transition: InputTransition.Up,
                } release &&
                release.Button == mouse.Button)
            {
                index++;
                continue;
            }

            diagnostics.Add(new ConversionDiagnostic(
                "XMBC_EXPORT_ATOMIC_MOUSE_PAIR_REQUIRED",
                DiagnosticSeverity.Error,
                $"XMBC 的 {mouse.Button} 是原子动作，必须使用相邻的按下/释放事件表示。",
                mouse.Sequence));
        }
    }

    private static bool IsAtomicMouseButton(MouseButton button) =>
        button is MouseButton.WheelUp or MouseButton.WheelDown or MouseButton.TiltLeft or MouseButton.TiltRight;

    private static string FormatAtomicMouse(MouseButton button) => button switch
    {
        MouseButton.WheelUp => "{MWUP}",
        MouseButton.WheelDown => "{MWDN}",
        MouseButton.TiltLeft => "{TILTL}",
        MouseButton.TiltRight => "{TILTR}",
        _ => throw new ArgumentOutOfRangeException(nameof(button), button, "不是 XMBC 原子鼠标动作。"),
    };
}
