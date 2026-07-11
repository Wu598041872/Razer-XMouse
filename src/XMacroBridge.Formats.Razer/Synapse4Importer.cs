using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using XMacroBridge.Core.Abstractions;
using XMacroBridge.Core.Conversion;
using XMacroBridge.Core.Diagnostics;
using XMacroBridge.Core.Models;

namespace XMacroBridge.Formats.Razer;

public sealed class Synapse4Importer : IMacroImporter
{
    private static readonly MacroLimits DefaultLimits = new();
    private static readonly JsonDocumentOptions JsonOptions = new()
    {
        AllowTrailingCommas = false,
        CommentHandling = JsonCommentHandling.Disallow,
        MaxDepth = 64,
    };

    public string FormatId => "razer.synapse4";

    public bool CanImport(ReadOnlySpan<byte> header, string? fileName)
    {
        if (!string.Equals(Path.GetExtension(fileName), ".synapse4", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        var text = Encoding.UTF8.GetString(header);
        return text.TrimStart('\uFEFF', ' ', '\t', '\r', '\n').StartsWith('{')
            && text.Contains("\"macros\"", StringComparison.OrdinalIgnoreCase);
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
            using var outer = JsonDocument.Parse(bytes, JsonOptions);
            if (outer.RootElement.ValueKind != JsonValueKind.Object ||
                !TryGetProperty(outer.RootElement, "macros", out var macros) ||
                macros.ValueKind != JsonValueKind.Array)
            {
                return Failure("SYNAPSE4_MACROS_MISSING", "文件不包含有效的 macros 数组。", sourceName);
            }

            var documents = new List<MacroDocument>();
            var diagnostics = new List<ConversionDiagnostic>();
            var macroIndex = 0;
            foreach (var macroEntry in macros.EnumerateArray())
            {
                cancellationToken.ThrowIfCancellationRequested();
                try
                {
                    documents.Add(ParseMacroEntry(macroEntry, macroIndex, sourceName, diagnostics));
                }
                catch (Exception exception) when (exception is FormatException or JsonException or InvalidDataException or OverflowException)
                {
                    diagnostics.Add(new ConversionDiagnostic(
                        "SYNAPSE4_MACRO_INVALID",
                        DiagnosticSeverity.Error,
                        $"第 {macroIndex + 1} 个雷云宏载荷无效：{exception.Message}",
                        SourceContext: sourceName));
                }

                macroIndex++;
            }

            diagnostics.Add(new ConversionDiagnostic(
                "SYNAPSE4_HASH_NOT_VERIFIED",
                DiagnosticSeverity.Info,
                "已读取 .synapse4 宏，但因校验算法尚未确认，未验证 hash 字段。"));

            if (documents.Count == 0 && diagnostics.All(item => item.Severity != DiagnosticSeverity.Error))
            {
                diagnostics.Add(new ConversionDiagnostic(
                    "SYNAPSE4_NO_MACROS",
                    DiagnosticSeverity.Warning,
                    "整包中没有可导入的宏。"));
            }

            return new MacroImportResult(documents, diagnostics);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception exception) when (exception is JsonException or InvalidDataException)
        {
            return Failure("SYNAPSE4_INVALID", $".synapse4 文件无效：{exception.Message}", sourceName);
        }
    }

    private static MacroDocument ParseMacroEntry(
        JsonElement macroEntry,
        int macroIndex,
        string? sourceName,
        ICollection<ConversionDiagnostic> diagnostics)
    {
        if (macroEntry.ValueKind != JsonValueKind.Object)
        {
            throw new FormatException("宏条目不是 JSON 对象。 ");
        }

        var name = TryGetString(macroEntry, "name") ?? $"雷云宏 {macroIndex + 1}";
        var payloadText = TryGetString(macroEntry, "payload")
            ?? throw new FormatException("宏条目缺少 payload。 ");
        byte[] payload;
        try
        {
            payload = Convert.FromBase64String(payloadText);
        }
        catch (FormatException exception)
        {
            throw new FormatException("payload 不是有效 Base64。", exception);
        }

        if (payload.LongLength > DefaultLimits.MaximumFileBytes)
        {
            throw new InvalidDataException("解码后的宏载荷超过大小上限。 ");
        }

        using var inner = JsonDocument.Parse(payload, JsonOptions);
        if (inner.RootElement.ValueKind != JsonValueKind.Object)
        {
            throw new FormatException("解码后的宏载荷不是 JSON 对象。 ");
        }

        var guidText = TryGetString(inner.RootElement, "guid");
        var id = Guid.TryParse(guidText, out var parsedGuid) ? parsedGuid : CreateDeterministicGuid(payload);
        if (!TryGetProperty(inner.RootElement, "macroEvents", out var macroEvents) ||
            macroEvents.ValueKind != JsonValueKind.Array)
        {
            throw new FormatException("宏载荷缺少 macroEvents 数组。 ");
        }

        var events = ParseEvents(macroEvents, diagnostics, name);
        return new MacroDocument(id, name, events, "razer.synapse4.macro", sourceName);
    }

    private static IReadOnlyList<MacroEvent> ParseEvents(
        JsonElement macroEvents,
        ICollection<ConversionDiagnostic> diagnostics,
        string macroName)
    {
        var events = new List<MacroEvent>();
        long sequence = 0;
        foreach (var item in macroEvents.EnumerateArray())
        {
            if (item.ValueKind != JsonValueKind.Object)
            {
                events.Add(new UnknownMacroEvent(sequence, "invalid-json-event", item.GetRawText()));
                diagnostics.Add(new ConversionDiagnostic(
                    "SYNAPSE4_EVENT_INVALID",
                    DiagnosticSeverity.Error,
                    $"宏“{macroName}”包含非对象事件。",
                    sequence,
                    item.GetRawText()));
                sequence++;
                continue;
            }

            var type = GetTypeText(item);
            switch (type)
            {
                case "actionBar":
                    break;
                case "0":
                    events.Add(ParseDelay(item, sequence++));
                    break;
                case "1":
                    events.Add(ParseKey(item, sequence++));
                    break;
                case "2":
                    events.Add(ParseMouse(item, sequence++));
                    break;
                case "7":
                    events.Add(ParseReference(item, sequence++));
                    break;
                default:
                    events.Add(new UnknownMacroEvent(sequence, type ?? "missing", item.GetRawText()));
                    diagnostics.Add(new ConversionDiagnostic(
                        "SYNAPSE4_EVENT_UNKNOWN",
                        DiagnosticSeverity.Error,
                        $"宏“{macroName}”包含未知事件类型 {type ?? "缺失"}。",
                        sequence,
                        item.GetRawText()));
                    sequence++;
                    break;
            }
        }

        return events;
    }

    private static DelayMacroEvent ParseDelay(JsonElement item, long sequence)
    {
        if (!TryGetProperty(item, "Number", out var number))
        {
            throw new FormatException("延时事件缺少 Number。 ");
        }

        var text = number.ValueKind == JsonValueKind.String ? number.GetString() : number.GetRawText();
        var seconds = decimal.Parse(text ?? string.Empty, NumberStyles.Float, CultureInfo.InvariantCulture);
        var milliseconds = decimal.Round(seconds * 1000m, 0, MidpointRounding.AwayFromZero);
        return new DelayMacroEvent(sequence, checked((long)milliseconds));
    }

    private static KeyMacroEvent ParseKey(JsonElement item, long sequence)
    {
        var keyEvent = GetRequiredObject(item, "KeyEvent");
        var makeCode = GetRequiredInt32(keyEvent, "Makecode");
        var transition = ParseTransition(GetRequiredInt32(keyEvent, "State"));
        return new KeyMacroEvent(sequence, makeCode, transition);
    }

    private static MouseMacroEvent ParseMouse(JsonElement item, long sequence)
    {
        var mouseEvent = GetRequiredObject(item, "MouseEvent");
        var code = GetRequiredInt32(mouseEvent, "MouseButton");
        var button = code switch
        {
            0 => MouseButton.Left,
            1 => MouseButton.Right,
            _ => throw new FormatException($"尚未确认雷云鼠标按钮代码 {code} 的含义。"),
        };
        return new MouseMacroEvent(sequence, button, ParseTransition(GetRequiredInt32(mouseEvent, "State")));
    }

    private static MacroReferenceEvent ParseReference(JsonElement item, long sequence)
    {
        var guidText = TryGetString(item, "guid");
        Guid? guid = Guid.TryParse(guidText, out var parsedGuid) ? parsedGuid : null;
        int? index = TryGetProperty(item, "MPIndex", out var indexElement) && indexElement.TryGetInt32(out var parsedIndex)
            ? parsedIndex
            : null;
        if (guid is null && index is null)
        {
            throw new FormatException("宏引用缺少有效 guid 和 MPIndex。 ");
        }

        return new MacroReferenceEvent(sequence, guid, index);
    }

    private static InputTransition ParseTransition(int state) => state switch
    {
        0 => InputTransition.Down,
        1 => InputTransition.Up,
        _ => throw new FormatException($"未知输入状态 {state}。"),
    };

    private static string? GetTypeText(JsonElement item)
    {
        if (!TryGetProperty(item, "Type", out var type))
        {
            return null;
        }

        return type.ValueKind switch
        {
            JsonValueKind.String => type.GetString(),
            JsonValueKind.Number => type.GetRawText(),
            _ => null,
        };
    }

    private static JsonElement GetRequiredObject(JsonElement parent, string name)
    {
        if (!TryGetProperty(parent, name, out var value) || value.ValueKind != JsonValueKind.Object)
        {
            throw new FormatException($"事件缺少对象 {name}。 ");
        }

        return value;
    }

    private static int GetRequiredInt32(JsonElement parent, string name)
    {
        if (!TryGetProperty(parent, name, out var value) || !value.TryGetInt32(out var result))
        {
            throw new FormatException($"字段 {name} 不是有效整数。 ");
        }

        return result;
    }

    private static string? TryGetString(JsonElement parent, string name) =>
        TryGetProperty(parent, name, out var value) && value.ValueKind == JsonValueKind.String
            ? value.GetString()
            : null;

    private static bool TryGetProperty(JsonElement parent, string name, out JsonElement value)
    {
        foreach (var property in parent.EnumerateObject())
        {
            if (string.Equals(property.Name, name, StringComparison.OrdinalIgnoreCase))
            {
                value = property.Value;
                return true;
            }
        }

        value = default;
        return false;
    }

    private static async Task CopyWithLimitAsync(
        Stream input,
        Stream output,
        long maximumBytes,
        CancellationToken cancellationToken)
    {
        var copyBuffer = new byte[81920];
        long total = 0;
        while (true)
        {
            var read = await input.ReadAsync(copyBuffer, cancellationToken).ConfigureAwait(false);
            if (read == 0)
            {
                return;
            }

            total += read;
            if (total > maximumBytes)
            {
                throw new InvalidDataException($"输入文件超过 {maximumBytes} 字节上限。 ");
            }

            await output.WriteAsync(copyBuffer.AsMemory(0, read), cancellationToken).ConfigureAwait(false);
        }
    }

    private static Guid CreateDeterministicGuid(ReadOnlySpan<byte> bytes)
    {
        Span<byte> hash = stackalloc byte[32];
        SHA256.HashData(bytes, hash);
        return new Guid(hash[..16]);
    }

    private static MacroImportResult Failure(string code, string message, string? sourceName) =>
        new([], [new ConversionDiagnostic(code, DiagnosticSeverity.Error, message, SourceContext: sourceName)]);
}
