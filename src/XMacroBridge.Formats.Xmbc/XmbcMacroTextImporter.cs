using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using XMacroBridge.Core.Abstractions;
using XMacroBridge.Core.Conversion;
using XMacroBridge.Core.Diagnostics;
using XMacroBridge.Core.Models;
using XMacroBridge.Core.Text;

namespace XMacroBridge.Formats.Xmbc;

public sealed class XmbcMacroTextImporter : IMacroImporter
{
    private readonly MacroLimits limits;

    public XmbcMacroTextImporter(MacroLimits? limits = null)
    {
        this.limits = limits ?? new MacroLimits();
        if (this.limits.MaximumEventsPerMacro < 1)
        {
            throw new ArgumentOutOfRangeException(nameof(limits), "事件上限必须至少为 1。 ");
        }
    }

    public string FormatId => "xmbc.macro.text";

    public bool CanImport(ReadOnlySpan<byte> header, string? fileName)
    {
        if (!string.IsNullOrWhiteSpace(fileName))
        {
            return string.Equals(Path.GetExtension(fileName), ".txt", StringComparison.OrdinalIgnoreCase);
        }

        return TextEncodingDetector.TryDecodePrefix(header, out var text)
            && text.Length > 0
            && (text.Contains('{') || text.Any(char.IsLetterOrDigit));
    }

    public async Task<MacroImportResult> ImportAsync(
        Stream input,
        string? sourceName,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(input);

        try
        {
            await using var buffer = new MemoryStream();
            await CopyWithLimitAsync(input, buffer, limits.MaximumFileBytes, cancellationToken).ConfigureAwait(false);
            var bytes = buffer.ToArray();
            var text = TextEncodingDetector.Decode(bytes).TrimEnd('\r', '\n');
            var diagnostics = new List<ConversionDiagnostic>();
            var events = Parse(text, diagnostics, limits);
            var name = Path.GetFileNameWithoutExtension(sourceName) ?? "XMBC 宏文本";
            var document = new MacroDocument(CreateDeterministicGuid(bytes), name, events, FormatId, sourceName);
            return new MacroImportResult([document], diagnostics);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception exception) when (exception is InvalidDataException or DecoderFallbackException)
        {
            return new MacroImportResult(
                [],
                [new ConversionDiagnostic("XMBC_TEXT_INVALID", DiagnosticSeverity.Error, $"XMBC 宏文本无效：{exception.Message}", SourceContext: DiagnosticContext.FromSourceName(sourceName))]);
        }
    }

    private static IReadOnlyList<MacroEvent> Parse(
        string text,
        ICollection<ConversionDiagnostic> diagnostics,
        MacroLimits limits)
    {
        var result = new List<MacroEvent>();
        InputTransition? explicitTransition = null;
        var pendingModifiers = new List<(int VirtualKey, string Name, bool IsExtended)>();
        long? pendingHoldMilliseconds = null;
        long sequence = 0;
        var eventLimitReached = false;

        for (var index = 0; index < text.Length;)
        {
            if (text[index] == '{')
            {
                var closingBrace = text.IndexOf('}', index + 1);
                if (closingBrace < 0)
                {
                    AddUnknown(result, diagnostics, ref sequence, text[index..], "未闭合的 XMBC 标记");
                    break;
                }

                var token = text[(index + 1)..closingBrace];
                index = closingBrace + 1;
                if (token.Equals("PRESS", StringComparison.OrdinalIgnoreCase))
                {
                    explicitTransition = InputTransition.Down;
                }
                else if (token.Equals("RELEASE", StringComparison.OrdinalIgnoreCase))
                {
                    explicitTransition = InputTransition.Up;
                }
                else if (token.Equals("CLEAR", StringComparison.OrdinalIgnoreCase))
                {
                    pendingModifiers.Clear();
                }
                else if (TryParseDelayToken(token, out var delayMilliseconds))
                {
                    result.Add(new DelayMacroEvent(sequence++, delayMilliseconds));
                }
                else if (TryParseHoldToken(token, out var holdMilliseconds))
                {
                    if (explicitTransition is not null)
                    {
                        AddUnknown(result, diagnostics, ref sequence, "{" + token + "}", "HOLD/HOLDMS 不能与持续 PRESS/RELEASE 模式组合");
                    }
                    else
                    {
                        pendingHoldMilliseconds = holdMilliseconds;
                    }
                }
                else if (TryParseModifier(token, out var modifierKey, out var modifierName, out var modifierExtended))
                {
                    if (pendingModifiers.All(item => item.VirtualKey != modifierKey))
                    {
                        pendingModifiers.Add((modifierKey, modifierName, modifierExtended));
                    }
                }
                else if (TryParseMouseToken(token, out var button, out var mouseTransition))
                {
                    if (pendingModifiers.Count > 0 || pendingHoldMilliseconds is not null)
                    {
                        AddUnknown(result, diagnostics, ref sequence, "{" + token + "}", "辅助键或 HOLD 标记不能应用到鼠标标记");
                        pendingModifiers.Clear();
                        pendingHoldMilliseconds = null;
                    }
                    else if (mouseTransition is { } transition)
                    {
                        result.Add(new MouseMacroEvent(sequence++, button, transition));
                    }
                    else
                    {
                        result.Add(new MouseMacroEvent(sequence++, button, InputTransition.Down));
                        result.Add(new MouseMacroEvent(sequence++, button, InputTransition.Up));
                    }
                }
                else if (TryParseVirtualKeyToken(token, out var virtualKey, out var keyName, out var isExtended))
                {
                    EmitKey(
                        result,
                        ref sequence,
                        virtualKey,
                        keyName,
                        isExtended,
                        explicitTransition,
                        pendingModifiers,
                        ref pendingHoldMilliseconds);
                }
                else if (TryParseScanCodeToken(token, out var scanCode, out var scanCodeExtended))
                {
                    EmitScanCode(
                        result,
                        ref sequence,
                        scanCode,
                        scanCodeExtended,
                        explicitTransition,
                        pendingModifiers,
                        ref pendingHoldMilliseconds);
                }
                else if (TryParseRandomDelay(token, out var minimumDelay, out var maximumDelay))
                {
                    result.Add(new RandomDelayMacroEvent(sequence++, minimumDelay, maximumDelay));
                }
                else if (TryClassifyCommand(token, out var category))
                {
                    if (pendingModifiers.Count > 0 || pendingHoldMilliseconds is not null)
                    {
                        AddUnknown(result, diagnostics, ref sequence, "{" + token + "}", "辅助键或 HOLD 标记不能应用到 XMBC 命令");
                        pendingModifiers.Clear();
                        pendingHoldMilliseconds = null;
                    }
                    else
                    {
                        result.Add(new XmbcCommandMacroEvent(sequence++, "{" + token + "}", category));
                    }
                }
                else
                {
                    AddUnknown(result, diagnostics, ref sequence, "{" + token + "}", "未知或尚未支持的 XMBC 标记");
                    pendingModifiers.Clear();
                    pendingHoldMilliseconds = null;
                }

                if (TrimToEventLimit(result, diagnostics, ref sequence, limits.MaximumEventsPerMacro))
                {
                    eventLimitReached = true;
                    break;
                }

                continue;
            }

            var character = text[index++];
            if (character is '\r' or '\n')
            {
                continue;
            }

            if (TryMapCharacter(character, out var keyCode, out var displayName))
            {
                EmitKey(
                    result,
                    ref sequence,
                    keyCode,
                    displayName,
                    false,
                    explicitTransition,
                    pendingModifiers,
                    ref pendingHoldMilliseconds);
            }
            else
            {
                AddUnknown(result, diagnostics, ref sequence, character.ToString(), "尚未支持的普通字符");
                pendingModifiers.Clear();
                pendingHoldMilliseconds = null;
            }

            if (TrimToEventLimit(result, diagnostics, ref sequence, limits.MaximumEventsPerMacro))
            {
                eventLimitReached = true;
                break;
            }
        }

        if (!eventLimitReached && (pendingModifiers.Count > 0 || pendingHoldMilliseconds is not null))
        {
            AddUnknown(result, diagnostics, ref sequence, "end-of-macro", "宏结尾存在没有目标按键的辅助键或 HOLD 标记");
            _ = TrimToEventLimit(result, diagnostics, ref sequence, limits.MaximumEventsPerMacro);
        }

        return result;
    }

    private static bool TrimToEventLimit(
        List<MacroEvent> events,
        ICollection<ConversionDiagnostic> diagnostics,
        ref long sequence,
        int maximumEvents)
    {
        if (events.Count <= maximumEvents)
        {
            return false;
        }

        events.RemoveRange(maximumEvents, events.Count - maximumEvents);
        events[^1] = new UnknownMacroEvent(maximumEvents - 1, "import.event-limit");
        sequence = events.Count;
        diagnostics.Add(new ConversionDiagnostic(
            "IMPORT_EVENT_LIMIT",
            DiagnosticSeverity.Error,
            $"宏事件数超过上限 {maximumEvents}，已停止继续解析。"));
        return true;
    }

    private static void EmitKey(
        ICollection<MacroEvent> events,
        ref long sequence,
        int virtualKey,
        string displayName,
        bool isExtended,
        InputTransition? explicitTransition,
        IList<(int VirtualKey, string Name, bool IsExtended)> pendingModifiers,
        ref long? pendingHoldMilliseconds)
    {
        if (explicitTransition is { } transition)
        {
            foreach (var modifier in pendingModifiers)
            {
                events.Add(new KeyMacroEvent(sequence++, modifier.VirtualKey, transition, modifier.Name, modifier.IsExtended));
            }

            events.Add(new KeyMacroEvent(sequence++, virtualKey, transition, displayName, isExtended));
            pendingModifiers.Clear();
            pendingHoldMilliseconds = null;
            return;
        }

        foreach (var modifier in pendingModifiers)
        {
            events.Add(new KeyMacroEvent(sequence++, modifier.VirtualKey, InputTransition.Down, modifier.Name, modifier.IsExtended));
        }

        events.Add(new KeyMacroEvent(sequence++, virtualKey, InputTransition.Down, displayName, isExtended));
        if (pendingHoldMilliseconds is { } hold)
        {
            events.Add(new DelayMacroEvent(sequence++, hold));
        }

        events.Add(new KeyMacroEvent(sequence++, virtualKey, InputTransition.Up, displayName, isExtended));
        for (var index = pendingModifiers.Count - 1; index >= 0; index--)
        {
            var modifier = pendingModifiers[index];
            events.Add(new KeyMacroEvent(sequence++, modifier.VirtualKey, InputTransition.Up, modifier.Name, modifier.IsExtended));
        }

        pendingModifiers.Clear();
        pendingHoldMilliseconds = null;
    }

    private static void EmitScanCode(
        ICollection<MacroEvent> events,
        ref long sequence,
        int scanCode,
        bool isExtended,
        InputTransition? explicitTransition,
        IList<(int VirtualKey, string Name, bool IsExtended)> pendingModifiers,
        ref long? pendingHoldMilliseconds)
    {
        if (explicitTransition is { } transition)
        {
            foreach (var modifier in pendingModifiers)
            {
                events.Add(new KeyMacroEvent(sequence++, modifier.VirtualKey, transition, modifier.Name, modifier.IsExtended));
            }

            events.Add(new ScanCodeMacroEvent(sequence++, scanCode, transition, isExtended));
            pendingModifiers.Clear();
            pendingHoldMilliseconds = null;
            return;
        }

        foreach (var modifier in pendingModifiers)
        {
            events.Add(new KeyMacroEvent(sequence++, modifier.VirtualKey, InputTransition.Down, modifier.Name, modifier.IsExtended));
        }

        events.Add(new ScanCodeMacroEvent(sequence++, scanCode, InputTransition.Down, isExtended));
        if (pendingHoldMilliseconds is { } hold)
        {
            events.Add(new DelayMacroEvent(sequence++, hold));
        }

        events.Add(new ScanCodeMacroEvent(sequence++, scanCode, InputTransition.Up, isExtended));
        for (var index = pendingModifiers.Count - 1; index >= 0; index--)
        {
            var modifier = pendingModifiers[index];
            events.Add(new KeyMacroEvent(sequence++, modifier.VirtualKey, InputTransition.Up, modifier.Name, modifier.IsExtended));
        }

        pendingModifiers.Clear();
        pendingHoldMilliseconds = null;
    }

    private static bool TryParseDelayToken(string token, out long milliseconds)
    {
        milliseconds = 0;
        if (TryGetNumericSuffix(token, "WAITMS", out var millisecondsText))
        {
            return long.TryParse(millisecondsText, NumberStyles.None, CultureInfo.InvariantCulture, out milliseconds);
        }

        if (TryGetNumericSuffix(token, "WAIT", out var secondsText) &&
            long.TryParse(secondsText, NumberStyles.None, CultureInfo.InvariantCulture, out var seconds))
        {
            try
            {
                milliseconds = checked(seconds * 1000);
                return true;
            }
            catch (OverflowException)
            {
                return false;
            }
        }

        return false;
    }

    private static bool TryParseHoldToken(string token, out long milliseconds)
    {
        milliseconds = 0;
        if (TryGetNumericSuffix(token, "HOLDMS", out var millisecondsText))
        {
            return long.TryParse(millisecondsText, NumberStyles.None, CultureInfo.InvariantCulture, out milliseconds);
        }

        if (TryGetNumericSuffix(token, "HOLD", out var secondsText) &&
            long.TryParse(secondsText, NumberStyles.None, CultureInfo.InvariantCulture, out var seconds))
        {
            try
            {
                milliseconds = checked(seconds * 1000);
                return true;
            }
            catch (OverflowException)
            {
                return false;
            }
        }

        return false;
    }

    private static bool TryGetNumericSuffix(string token, string prefix, out string suffix)
    {
        suffix = string.Empty;
        if (!token.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        suffix = token[prefix.Length..];
        if (suffix.StartsWith(':'))
        {
            suffix = suffix[1..];
        }

        return suffix.Length > 0 && suffix.All(char.IsDigit);
    }

    private static bool TryParseRandomDelay(string token, out long minimum, out long maximum)
    {
        minimum = 0;
        maximum = 0;
        if (!token.StartsWith("WAITMS:", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        var parts = token[7..].Split('-', StringSplitOptions.TrimEntries);
        return parts.Length == 2
            && long.TryParse(parts[0], NumberStyles.None, CultureInfo.InvariantCulture, out minimum)
            && long.TryParse(parts[1], NumberStyles.None, CultureInfo.InvariantCulture, out maximum);
    }

    private static bool TryParseScanCodeToken(string token, out int scanCode, out bool isExtended)
    {
        isExtended = token.StartsWith("SCE:", StringComparison.OrdinalIgnoreCase);
        var prefix = isExtended ? "SCE:" : "SC:";
        if (!token.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
        {
            scanCode = 0;
            return false;
        }

        return int.TryParse(token[prefix.Length..], NumberStyles.Integer, CultureInfo.InvariantCulture, out scanCode)
            && scanCode is >= 0 and <= 65535;
    }

    private static bool TryClassifyCommand(string token, out string category)
    {
        var upper = token.ToUpperInvariant();
        category = upper switch
        {
            "NUMLOCKON" or "NUMLOCKOFF" or "CAPSLOCKON" or "CAPSLOCKOFF" or "SCROLLLOCKON" or "SCROLLLOCKOFF" => "lock-state",
            "ACTIVATE" or "ACTIVATEPARENT" or "ACTIVATETOP" or "CURSORBUSY" or "CURSORDEFAULT"
                or "INVERTXY" or "INVERTX" or "INVERTY" or "LOCKXY" or "LOCKX" or "LOCKY"
                or "LOCKC" or "UNLOCKXY" or "UNLOCKX" or "UNLOCKY" => "action",
            "OD" or "OU" or "OR" => "trigger-condition",
            _ when HasCommandPrefix(upper, "CB:") => "clipboard",
            _ when HasAnyCommandPrefix(upper, "MADD:", "MSET:", "PSET:", "ASET:", "MSAVE:", "MREST:") => "pointer",
            _ when HasAnyCommandPrefix(upper, "RUN:", "RUNHID:", "RUNMAX:", "RUNMIN:", "RUNADM:", "KILL:") => "process",
            _ when HasAnyCommandPrefix(upper, "POSTWM:", "SENDWM:") => "windows-message",
            _ when HasCommandPrefix(upper, "LAYER:") => "layer",
            _ => string.Empty,
        };
        return category.Length > 0;
    }

    private static bool HasCommandPrefix(string token, string prefix) =>
        token.StartsWith(prefix, StringComparison.Ordinal);

    private static bool HasAnyCommandPrefix(string token, params string[] prefixes) =>
        prefixes.Any(prefix => HasCommandPrefix(token, prefix));

    private static bool TryParseModifier(
        string token,
        out int virtualKey,
        out string displayName,
        out bool isExtended)
    {
        (virtualKey, displayName, isExtended) = token.ToUpperInvariant() switch
        {
            "CTRL" => (0x11, "CTRL", false),
            "RCTRL" => (0xA3, "RCTRL", true),
            "ALT" => (0x12, "ALT", false),
            "RALT" => (0xA5, "RALT", true),
            "SHIFT" => (0x10, "SHIFT", false),
            "RSHIFT" => (0xA1, "RSHIFT", false),
            "LWIN" => (0x5B, "LWIN", true),
            "RWIN" => (0x5C, "RWIN", true),
            "APPS" => (0x5D, "APPS", true),
            _ => (0, string.Empty, false),
        };
        return virtualKey != 0;
    }

    private static bool TryParseVirtualKeyToken(
        string token,
        out int virtualKey,
        out string displayName,
        out bool isExtended)
    {
        displayName = token.ToUpperInvariant();
        isExtended = false;
        if (TryParseFunctionKey(displayName, out virtualKey))
        {
            return true;
        }

        if (TryParseNumpadKey(displayName, out virtualKey, out isExtended))
        {
            return true;
        }

        if (TryParseNumericKeyCode(displayName, "VKC", out virtualKey))
        {
            return true;
        }

        if (TryParseNumericKeyCode(displayName, "EXT", out virtualKey))
        {
            isExtended = true;
            return true;
        }

        virtualKey = displayName switch
        {
            "DEL" => 0x2E,
            "INS" => 0x2D,
            "PGUP" => 0x21,
            "PGDN" => 0x22,
            "HOME" => 0x24,
            "END" => 0x23,
            "RETURN" => 0x0D,
            "ESCAPE" => 0x1B,
            "BACKSPACE" => 0x08,
            "TAB" => 0x09,
            "PRTSCN" => 0x2C,
            "PAUSE" => 0x13,
            "SPACE" => 0x20,
            "CAPSLOCK" => 0x14,
            "NUMLOCK" => 0x90,
            "SCROLLLOCK" => 0x91,
            "BREAK" => 0x03,
            "UP" => 0x26,
            "DOWN" => 0x28,
            "LEFT" => 0x25,
            "RIGHT" => 0x27,
            "VOL+" => 0xAF,
            "VOL-" => 0xAE,
            "MUTE" => 0xAD,
            "MEDIAPLAY" => 0xB3,
            "MEDIASTOP" => 0xB2,
            "MEDIANEXT" => 0xB0,
            "MEDIAPREV" => 0xB1,
            "BACK" => 0xA6,
            "FORWARD" => 0xA7,
            "REFRESH" => 0xA8,
            "STOP" => 0xA9,
            "SEARCH" => 0xAA,
            "FAVORITES" => 0xAB,
            "WEBHOME" => 0xAC,
            _ => 0,
        };
        return virtualKey != 0;
    }

    private static bool TryParseFunctionKey(string token, out int virtualKey)
    {
        virtualKey = 0;
        if (token.Length < 2 || token[0] != 'F' ||
            !int.TryParse(token[1..], NumberStyles.None, CultureInfo.InvariantCulture, out var number) ||
            number is < 1 or > 24)
        {
            return false;
        }

        virtualKey = 0x70 + number - 1;
        return true;
    }

    private static bool TryParseNumpadKey(string token, out int virtualKey, out bool isExtended)
    {
        isExtended = false;
        if (token.Length == 4 && token.StartsWith("NUM", StringComparison.Ordinal) && char.IsDigit(token[3]))
        {
            virtualKey = 0x60 + (token[3] - '0');
            return true;
        }

        virtualKey = token switch
        {
            "NUM+" => 0x6B,
            "NUM-" => 0x6D,
            "NUM." => 0x6E,
            "NUM/" => 0x6F,
            "NUM*" => 0x6A,
            "NUMENTER" => 0x0D,
            _ => 0,
        };
        isExtended = token is "NUM/" or "NUMENTER";
        return virtualKey != 0;
    }

    private static bool TryParseNumericKeyCode(string token, string prefix, out int virtualKey)
    {
        virtualKey = 0;
        if (!token.StartsWith(prefix + ":", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        return int.TryParse(token[(prefix.Length + 1)..], NumberStyles.Integer, CultureInfo.InvariantCulture, out virtualKey)
            && virtualKey is >= 0 and <= 255;
    }

    private static bool TryMapCharacter(char character, out int virtualKey, out string displayName)
    {
        displayName = character.ToString();
        if (character is >= 'a' and <= 'z')
        {
            virtualKey = char.ToUpperInvariant(character);
            return true;
        }

        if (character is >= 'A' and <= 'Z' or >= '0' and <= '9')
        {
            virtualKey = character;
            return true;
        }

        if (character == ' ')
        {
            virtualKey = 0x20;
            displayName = "Space";
            return true;
        }

        virtualKey = 0;
        return false;
    }

    private static bool TryParseMouseToken(
        string token,
        out MouseButton button,
        out InputTransition? transition)
    {
        var upper = token.ToUpperInvariant().Replace("XMB1", "MB4", StringComparison.Ordinal)
            .Replace("XMB2", "MB5", StringComparison.Ordinal);
        (button, transition) = upper switch
        {
            "LMB" => (MouseButton.Left, (InputTransition?)null),
            "LMBD" => (MouseButton.Left, InputTransition.Down),
            "LMBU" => (MouseButton.Left, InputTransition.Up),
            "RMB" => (MouseButton.Right, (InputTransition?)null),
            "RMBD" => (MouseButton.Right, InputTransition.Down),
            "RMBU" => (MouseButton.Right, InputTransition.Up),
            "MMB" => (MouseButton.Middle, (InputTransition?)null),
            "MMBD" => (MouseButton.Middle, InputTransition.Down),
            "MMBU" => (MouseButton.Middle, InputTransition.Up),
            "MB4" => (MouseButton.XButton1, (InputTransition?)null),
            "MB4D" => (MouseButton.XButton1, InputTransition.Down),
            "MB4U" => (MouseButton.XButton1, InputTransition.Up),
            "MB5" => (MouseButton.XButton2, (InputTransition?)null),
            "MB5D" => (MouseButton.XButton2, InputTransition.Down),
            "MB5U" => (MouseButton.XButton2, InputTransition.Up),
            "MWUP" => (MouseButton.WheelUp, (InputTransition?)null),
            "MWDN" => (MouseButton.WheelDown, (InputTransition?)null),
            "TILTL" => (MouseButton.TiltLeft, (InputTransition?)null),
            "TILTR" => (MouseButton.TiltRight, (InputTransition?)null),
            _ => (MouseButton.Left, (InputTransition?)null),
        };
        return upper is "LMB" or "LMBD" or "LMBU" or "RMB" or "RMBD" or "RMBU"
            or "MMB" or "MMBD" or "MMBU" or "MB4" or "MB4D" or "MB4U"
            or "MB5" or "MB5D" or "MB5U" or "MWUP" or "MWDN" or "TILTL" or "TILTR";
    }

    private static void AddUnknown(
        ICollection<MacroEvent> events,
        ICollection<ConversionDiagnostic> diagnostics,
        ref long sequence,
        string raw,
        string reason)
    {
        events.Add(new UnknownMacroEvent(sequence, "xmbc.token", raw));
        diagnostics.Add(new ConversionDiagnostic(
            "XMBC_TOKEN_UNKNOWN",
            DiagnosticSeverity.Error,
            $"{reason}：{raw}",
            sequence));
        sequence++;
    }

    private static async Task CopyWithLimitAsync(
        Stream input,
        Stream output,
        long maximumBytes,
        CancellationToken cancellationToken)
    {
        var buffer = new byte[81920];
        long total = 0;
        while (true)
        {
            var read = await input.ReadAsync(buffer, cancellationToken).ConfigureAwait(false);
            if (read == 0)
            {
                return;
            }

            total += read;
            if (total > maximumBytes)
            {
                throw new InvalidDataException($"输入文本超过 {maximumBytes} 字节上限。 ");
            }

            await output.WriteAsync(buffer.AsMemory(0, read), cancellationToken).ConfigureAwait(false);
        }
    }

    private static Guid CreateDeterministicGuid(ReadOnlySpan<byte> bytes)
    {
        Span<byte> hash = stackalloc byte[32];
        SHA256.HashData(bytes, hash);
        return new Guid(hash[..16]);
    }
}
