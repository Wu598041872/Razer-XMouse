using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Xml;
using System.Xml.Linq;
using XMacroBridge.Core.Abstractions;
using XMacroBridge.Core.Conversion;
using XMacroBridge.Core.Diagnostics;
using XMacroBridge.Core.Models;
using XMacroBridge.Core.Text;

namespace XMacroBridge.Formats.Razer;

public sealed class RazerMacroXmlImporter : IMacroImporter
{
    private readonly MacroLimits limits;

    public RazerMacroXmlImporter(MacroLimits? limits = null)
    {
        this.limits = limits ?? new MacroLimits();
        if (this.limits.MaximumEventsPerMacro < 1)
        {
            throw new ArgumentOutOfRangeException(nameof(limits), "事件上限必须至少为 1。 ");
        }
    }

    public string FormatId => "razer.macro.xml";

    public bool CanImport(ReadOnlySpan<byte> header, string? fileName)
    {
        if (!string.Equals(Path.GetExtension(fileName), ".xml", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        return TextEncodingDetector.TryDecodePrefix(header, out var text)
            && text.Contains("<Macro", StringComparison.OrdinalIgnoreCase);
    }

    public async Task<MacroImportResult> ImportAsync(
        Stream input,
        string? sourceName,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(input);

        var diagnostics = new List<ConversionDiagnostic>();
        try
        {
            await using var buffer = new MemoryStream();
            await CopyWithLimitAsync(input, buffer, limits.MaximumFileBytes, cancellationToken).ConfigureAwait(false);
            var bytes = buffer.ToArray();
            TextEncodingDetector.ValidateXmlEncoding(bytes);
            buffer.Position = 0;

            var settings = new XmlReaderSettings
            {
                Async = true,
                DtdProcessing = DtdProcessing.Prohibit,
                XmlResolver = null,
                MaxCharactersInDocument = limits.MaximumFileBytes,
                IgnoreComments = true,
                IgnoreWhitespace = true,
            };

            using var reader = XmlReader.Create(buffer, settings);
            var xml = await XDocument.LoadAsync(reader, LoadOptions.SetLineInfo, cancellationToken).ConfigureAwait(false);
            var root = xml.Root;
            if (root is null || !string.Equals(root.Name.LocalName, "Macro", StringComparison.OrdinalIgnoreCase))
            {
                return Failure("RAZER_XML_ROOT", "文件根元素不是 <Macro>，无法作为雷云独立宏读取。", sourceName);
            }

            var name = GetChildValue(root, "Name");
            if (string.IsNullOrWhiteSpace(name))
            {
                name = Path.GetFileNameWithoutExtension(sourceName) ?? "未命名雷云宏";
                diagnostics.Add(new ConversionDiagnostic(
                    "RAZER_NAME_MISSING",
                    DiagnosticSeverity.Warning,
                    "宏缺少名称，已使用文件名或默认名称。"));
            }

            var documentId = ParseDocumentId(root, bytes, diagnostics, name);

            var parsedEvents = ParseEvents(root, diagnostics, name, limits);
            var events = RazerLoopExpander.Expand(parsedEvents, diagnostics, name, limits, "RAZER");
            var document = new MacroDocument(documentId, name, events, FormatId, sourceName);
            return new MacroImportResult([document], diagnostics);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception exception) when (exception is XmlException or InvalidDataException or FormatException or OverflowException or DecoderFallbackException)
        {
            return Failure("RAZER_XML_INVALID", $"雷云 XML 无效：{exception.Message}", sourceName);
        }
    }

    private static IReadOnlyList<MacroEvent> ParseEvents(
        XElement root,
        ICollection<ConversionDiagnostic> diagnostics,
        string macroName,
        MacroLimits limits)
    {
        var container = root.Elements().FirstOrDefault(element =>
            string.Equals(element.Name.LocalName, "MacroEvents", StringComparison.OrdinalIgnoreCase));
        if (container is null)
        {
            diagnostics.Add(new ConversionDiagnostic(
                "RAZER_EVENTS_MISSING",
                DiagnosticSeverity.Warning,
                "宏不包含 <MacroEvents>，已作为空宏读取。"));
            return [];
        }

        var result = new List<MacroEvent>();
        var usesVersion4NativeEncoding = string.Equals(GetChildValue(root, "Version"), "4", StringComparison.Ordinal);
        var mouseButtonEncoding = DetectMouseButtonEncoding(root, container);
        long sequence = 0;
        var sourceEventIndex = 0;
        foreach (var element in container.Elements().Where(item =>
                     string.Equals(item.Name.LocalName, "MacroEvent", StringComparison.OrdinalIgnoreCase)))
        {
            sourceEventIndex++;
            var type = GetChildValue(element, "Type");
            if (type == "actionBar")
            {
                diagnostics.Add(new ConversionDiagnostic(
                    "RAZER_EDITOR_METADATA_IGNORED",
                    DiagnosticSeverity.Info,
                    "已忽略雷云编辑器 actionBar 元数据。",
                    SourceContext: macroName));
                continue;
            }

            if (sequence >= limits.MaximumEventsPerMacro)
            {
                result[^1] = new UnknownMacroEvent(sequence - 1, "import.event-limit");
                diagnostics.Add(new ConversionDiagnostic(
                    "IMPORT_EVENT_LIMIT",
                    DiagnosticSeverity.Error,
                    $"宏“{macroName}”的事件数超过上限 {limits.MaximumEventsPerMacro}，已停止继续解析。",
                    SourceContext: macroName));
                break;
            }

            var eventSequence = sequence++;
            var rawPayload = element.ToString(SaveOptions.DisableFormatting);
            try
            {
                switch (type)
                {
                    case "0":
                        result.Add(ParseDelay(element, eventSequence, diagnostics, macroName));
                        break;
                    case "1":
                        result.Add(ParseKey(element, eventSequence, usesVersion4NativeEncoding));
                        break;
                    case "2":
                        result.Add(ParseMouse(element, eventSequence, mouseButtonEncoding));
                        break;
                    case "6":
                        result.Add(ParseType6Event(element, eventSequence));
                        break;
                    case "7":
                        result.Add(ParseReference(element, eventSequence));
                        break;
                    default:
                        result.Add(new UnknownMacroEvent(eventSequence, type ?? "missing", rawPayload));
                        diagnostics.Add(new ConversionDiagnostic(
                            "RAZER_EVENT_UNKNOWN",
                            DiagnosticSeverity.Error,
                            $"{FormatEventLocation(element, sourceEventIndex)}无法识别事件类型：{type ?? "缺失"}。",
                            eventSequence,
                            macroName));
                        break;
                }
            }
            catch (Exception exception) when (exception is FormatException or OverflowException)
            {
                result.Add(new UnknownMacroEvent(eventSequence, type ?? "missing", rawPayload));
                diagnostics.Add(new ConversionDiagnostic(
                    "RAZER_EVENT_INVALID",
                    DiagnosticSeverity.Error,
                    $"宏“{macroName}”中的{FormatEventLocation(element, sourceEventIndex)}无效：{exception.Message}",
                    eventSequence,
                    macroName));
            }
        }

        return result;
    }

    private static string FormatEventLocation(XElement element, int sourceEventIndex)
    {
        var lineInfo = (IXmlLineInfo)element;
        return lineInfo.HasLineInfo()
            ? $"第 {sourceEventIndex} 个雷云事件（第 {lineInfo.LineNumber} 行，第 {lineInfo.LinePosition} 列）"
            : $"第 {sourceEventIndex} 个雷云事件";
    }

    private static DelayMacroEvent ParseDelay(
        XElement element,
        long sequence,
        ICollection<ConversionDiagnostic> diagnostics,
        string macroName)
    {
        var value = GetRequiredChildValue(element, "Number");
        var conversion = RazerDelayConverter.Convert(value);
        if (conversion.PrecisionLost)
        {
            diagnostics.Add(new ConversionDiagnostic(
                "RAZER_DELAY_PRECISION_LOSS",
                DiagnosticSeverity.Warning,
                $"雷云延时 {value} 秒无法精确表示为整数毫秒，已转换为 {conversion.Milliseconds} ms。",
                sequence,
                macroName));
        }

        return new DelayMacroEvent(sequence, conversion.Milliseconds);
    }

    private static MacroEvent ParseType6Event(XElement element, long sequence)
    {
        var loopEvent = element.Elements().FirstOrDefault(item =>
            string.Equals(item.Name.LocalName, "LoopEvent", StringComparison.OrdinalIgnoreCase));
        var delay = GetChildValue(element, "Delay");
        if (loopEvent is not null && delay is not null)
        {
            throw new FormatException("Type 6 事件不能同时包含 Delay 和 LoopEvent。 ");
        }

        if (loopEvent is not null)
        {
            var number = GetRequiredChildValue(element, "Number");
            if (!int.TryParse(number, NumberStyles.Integer, CultureInfo.InvariantCulture, out var count) || count <= 0)
            {
                throw new FormatException($"循环次数 {number} 不是有效的正整数。 ");
            }

            var state = GetRequiredChildValue(loopEvent, "State");
            return state switch
            {
                "0" => new RazerLoopBoundaryEvent(sequence, true, count),
                "1" => new RazerLoopBoundaryEvent(sequence, false, count),
                _ => throw new FormatException($"未知的循环状态值：{state}。"),
            };
        }

        if (delay is null)
        {
            throw new FormatException("Type 6 事件既不包含 Delay，也不包含 LoopEvent。 ");
        }

        var value = delay;
        if (!long.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var milliseconds) ||
            milliseconds < 0)
        {
            throw new FormatException($"Type 6 延时 {value} 不是有效的非负整数毫秒。 ");
        }

        return new DelayMacroEvent(sequence, milliseconds);
    }

    private static KeyMacroEvent ParseKey(XElement element, long sequence, bool usesVersion4NativeEncoding)
    {
        var keyEvent = GetRequiredChild(element, "KeyEvent");
        var makeCode = int.Parse(GetRequiredChildValue(keyEvent, "Makecode"), CultureInfo.InvariantCulture);
        int virtualKey;
        if (usesVersion4NativeEncoding)
        {
            if (makeCode is < 1 or > 255)
            {
                throw new FormatException($"雷云 Version 4 虚拟键码 {makeCode} 不在 1–255 范围内。");
            }

            virtualKey = makeCode;
        }
        else if (!RazerInputCodeConverter.TryMakeCodeToVirtualKey(makeCode, out virtualKey))
        {
            throw new FormatException($"雷云键盘扫描码 {makeCode} 无法转换为 Windows 虚拟键码。");
        }
        var transition = ParseTransition(GetRequiredChildValue(keyEvent, "State"));
        return new KeyMacroEvent(sequence, virtualKey, transition);
    }

    private static MouseMacroEvent ParseMouse(
        XElement element,
        long sequence,
        RazerMouseButtonEncoding encoding)
    {
        var mouseEvent = GetRequiredChild(element, "MouseEvent");
        var buttonCode = int.Parse(GetRequiredChildValue(mouseEvent, "MouseButton"), CultureInfo.InvariantCulture);
        var button = (encoding, buttonCode) switch
        {
            (RazerMouseButtonEncoding.ZeroBased, 0) => MouseButton.Left,
            (RazerMouseButtonEncoding.ZeroBased, 1) => MouseButton.Right,
            (RazerMouseButtonEncoding.OneBased, 1) => MouseButton.Left,
            (RazerMouseButtonEncoding.OneBased, 2) => MouseButton.Right,
            _ => throw new FormatException($"尚未确认雷云鼠标按钮代码 {buttonCode} 的含义。"),
        };

        var transition = ParseTransition(GetRequiredChildValue(mouseEvent, "State"));
        return new MouseMacroEvent(sequence, button, transition);
    }

    private static RazerMouseButtonEncoding DetectMouseButtonEncoding(XElement root, XElement container)
    {
        if (string.Equals(GetChildValue(root, "Version"), "4", StringComparison.Ordinal))
        {
            return RazerMouseButtonEncoding.ZeroBased;
        }

        foreach (var element in container.Elements().Where(item =>
                     string.Equals(item.Name.LocalName, "MacroEvent", StringComparison.OrdinalIgnoreCase) &&
                     GetChildValue(item, "Type") == "2"))
        {
            var mouseEvent = element.Elements().FirstOrDefault(item =>
                string.Equals(item.Name.LocalName, "MouseEvent", StringComparison.OrdinalIgnoreCase));
            var buttonText = mouseEvent is null ? null : GetChildValue(mouseEvent, "MouseButton");
            if (buttonText == "0")
            {
                return RazerMouseButtonEncoding.ZeroBased;
            }
        }

        return RazerMouseButtonEncoding.OneBased;
    }

    private static MacroReferenceEvent ParseReference(XElement element, long sequence)
    {
        var guidText = GetChildValue(element, "guid");
        Guid? guid = Guid.TryParse(guidText, out var parsedGuid) ? parsedGuid : null;
        var indexText = GetChildValue(element, "MPIndex");
        int? index = int.TryParse(indexText, NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsedIndex)
            ? parsedIndex
            : null;

        if (guid is null && index is null)
        {
            throw new FormatException("嵌套宏引用同时缺少有效的 guid 和 MPIndex。 ");
        }

        return new MacroReferenceEvent(sequence, guid, index);
    }

    private static InputTransition ParseTransition(string state) => state switch
    {
        "0" => InputTransition.Down,
        "1" => InputTransition.Up,
        _ => throw new FormatException($"未知的按键状态值：{state}。"),
    };

    private static string? GetChildValue(XElement parent, string localName) =>
        parent.Elements().FirstOrDefault(element =>
            string.Equals(element.Name.LocalName, localName, StringComparison.OrdinalIgnoreCase))?.Value;

    private static string GetRequiredChildValue(XElement parent, string localName) =>
        GetChildValue(parent, localName) ?? throw new FormatException($"事件缺少 <{localName}>。 ");

    private static XElement GetRequiredChild(XElement parent, string localName) =>
        parent.Elements().FirstOrDefault(element =>
            string.Equals(element.Name.LocalName, localName, StringComparison.OrdinalIgnoreCase))
        ?? throw new FormatException($"事件缺少 <{localName}>。 ");

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
                throw new InvalidDataException($"输入文件超过 {maximumBytes} 字节上限。 ");
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

    private static Guid ParseDocumentId(
        XElement root,
        ReadOnlySpan<byte> bytes,
        ICollection<ConversionDiagnostic> diagnostics,
        string macroName)
    {
        var value = GetChildValue(root, "Guid");
        if (Guid.TryParse(value, out var embeddedGuid))
        {
            return embeddedGuid;
        }

        if (!string.IsNullOrWhiteSpace(value))
        {
            diagnostics.Add(new ConversionDiagnostic(
                "RAZER_GUID_INVALID",
                DiagnosticSeverity.Warning,
                $"宏“{macroName}”的根 Guid 无效，已使用文件内容生成稳定标识。",
                SourceContext: macroName));
        }

        return CreateDeterministicGuid(bytes);
    }

    private static MacroImportResult Failure(string code, string message, string? sourceName) =>
        new(
            [],
            [new ConversionDiagnostic(code, DiagnosticSeverity.Error, message, SourceContext: DiagnosticContext.FromSourceName(sourceName))]);

    private enum RazerMouseButtonEncoding
    {
        ZeroBased,
        OneBased,
    }
}
