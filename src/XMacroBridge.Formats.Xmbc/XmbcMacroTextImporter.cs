using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using XMacroBridge.Core.Abstractions;
using XMacroBridge.Core.Conversion;
using XMacroBridge.Core.Diagnostics;
using XMacroBridge.Core.Models;

namespace XMacroBridge.Formats.Xmbc;

public sealed class XmbcMacroTextImporter : IMacroImporter
{
    private static readonly MacroLimits DefaultLimits = new();

    public string FormatId => "xmbc.macro.text";

    public bool CanImport(ReadOnlySpan<byte> header, string? fileName)
    {
        if (!string.IsNullOrWhiteSpace(fileName) &&
            !string.Equals(Path.GetExtension(fileName), ".txt", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        var text = Encoding.UTF8.GetString(header);
        return text.Contains("{WAITMS:", StringComparison.OrdinalIgnoreCase)
            || text.Contains("{PRESS}", StringComparison.OrdinalIgnoreCase)
            || text.Contains("{LMB}", StringComparison.OrdinalIgnoreCase);
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
            await CopyWithLimitAsync(input, buffer, DefaultLimits.MaximumFileBytes, cancellationToken).ConfigureAwait(false);
            var bytes = buffer.ToArray();
            var text = new UTF8Encoding(false, true).GetString(bytes).TrimEnd('\r', '\n');
            var diagnostics = new List<ConversionDiagnostic>();
            var events = Parse(text, diagnostics);
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
                [new ConversionDiagnostic("XMBC_TEXT_INVALID", DiagnosticSeverity.Error, $"XMBC 宏文本无效：{exception.Message}", SourceContext: sourceName)]);
        }
    }

    private static IReadOnlyList<MacroEvent> Parse(
        string text,
        ICollection<ConversionDiagnostic> diagnostics)
    {
        var result = new List<MacroEvent>();
        InputTransition? pendingTransition = null;
        long sequence = 0;

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
                    pendingTransition = SetPendingTransition(pendingTransition, InputTransition.Down, result, diagnostics, ref sequence, token);
                }
                else if (token.Equals("RELEASE", StringComparison.OrdinalIgnoreCase))
                {
                    pendingTransition = SetPendingTransition(pendingTransition, InputTransition.Up, result, diagnostics, ref sequence, token);
                }
                else if (token.StartsWith("WAITMS:", StringComparison.OrdinalIgnoreCase))
                {
                    if (pendingTransition is not null)
                    {
                        AddUnknown(result, diagnostics, ref sequence, "{" + token + "}", "PRESS/RELEASE 后必须紧跟按键");
                        pendingTransition = null;
                        continue;
                    }

                    var number = token[7..];
                    if (!long.TryParse(number, NumberStyles.None, CultureInfo.InvariantCulture, out var milliseconds))
                    {
                        AddUnknown(result, diagnostics, ref sequence, "{" + token + "}", "WAITMS 延时不是有效非负整数");
                    }
                    else
                    {
                        result.Add(new DelayMacroEvent(sequence++, milliseconds));
                    }
                }
                else if (TryParseMouseToken(token, out var button, out var explicitTransition))
                {
                    if (pendingTransition is not null)
                    {
                        AddUnknown(result, diagnostics, ref sequence, "{" + token + "}", "鼠标标记不能再使用 PRESS/RELEASE 前缀");
                        pendingTransition = null;
                    }
                    else if (explicitTransition is { } transition)
                    {
                        result.Add(new MouseMacroEvent(sequence++, button, transition));
                    }
                    else
                    {
                        result.Add(new MouseMacroEvent(sequence++, button, InputTransition.Down));
                        result.Add(new MouseMacroEvent(sequence++, button, InputTransition.Up));
                    }
                }
                else if (TryParseNamedKey(token, out var virtualKey))
                {
                    AddKey(result, ref sequence, virtualKey, token, ref pendingTransition);
                }
                else
                {
                    AddUnknown(result, diagnostics, ref sequence, "{" + token + "}", "未知 XMBC 标记");
                    pendingTransition = null;
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
                AddKey(result, ref sequence, keyCode, displayName, ref pendingTransition);
            }
            else
            {
                AddUnknown(result, diagnostics, ref sequence, character.ToString(), "尚未支持的普通字符");
                pendingTransition = null;
            }
        }

        if (pendingTransition is not null)
        {
            AddUnknown(result, diagnostics, ref sequence, pendingTransition.ToString()!, "宏结尾存在没有目标按键的 PRESS/RELEASE");
        }

        return result;
    }

    private static InputTransition? SetPendingTransition(
        InputTransition? current,
        InputTransition next,
        ICollection<MacroEvent> events,
        ICollection<ConversionDiagnostic> diagnostics,
        ref long sequence,
        string token)
    {
        if (current is not null)
        {
            AddUnknown(events, diagnostics, ref sequence, "{" + token + "}", "连续出现多个 PRESS/RELEASE 标记");
        }

        return next;
    }

    private static void AddKey(
        ICollection<MacroEvent> events,
        ref long sequence,
        int virtualKey,
        string displayName,
        ref InputTransition? pendingTransition)
    {
        if (pendingTransition is { } transition)
        {
            events.Add(new KeyMacroEvent(sequence++, virtualKey, transition, displayName));
            pendingTransition = null;
            return;
        }

        events.Add(new KeyMacroEvent(sequence++, virtualKey, InputTransition.Down, displayName));
        events.Add(new KeyMacroEvent(sequence++, virtualKey, InputTransition.Up, displayName));
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

    private static bool TryParseNamedKey(string token, out int virtualKey)
    {
        virtualKey = token.ToUpperInvariant() switch
        {
            "ALT" => 0x12,
            "SHIFT" => 0x10,
            "CTRL" => 0x11,
            _ => 0,
        };
        return virtualKey != 0;
    }

    private static bool TryParseMouseToken(
        string token,
        out MouseButton button,
        out InputTransition? transition)
    {
        (button, transition) = token.ToUpperInvariant() switch
        {
            "LMB" => (MouseButton.Left, (InputTransition?)null),
            "LMBD" => (MouseButton.Left, InputTransition.Down),
            "LMBU" => (MouseButton.Left, InputTransition.Up),
            "RMB" => (MouseButton.Right, (InputTransition?)null),
            "RMBD" => (MouseButton.Right, InputTransition.Down),
            "RMBU" => (MouseButton.Right, InputTransition.Up),
            _ => (MouseButton.Left, (InputTransition?)null),
        };
        return token.Equals("LMB", StringComparison.OrdinalIgnoreCase)
            || token.Equals("LMBD", StringComparison.OrdinalIgnoreCase)
            || token.Equals("LMBU", StringComparison.OrdinalIgnoreCase)
            || token.Equals("RMB", StringComparison.OrdinalIgnoreCase)
            || token.Equals("RMBD", StringComparison.OrdinalIgnoreCase)
            || token.Equals("RMBU", StringComparison.OrdinalIgnoreCase);
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
            sequence,
            raw));
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
