using System.Globalization;
using System.Text;
using System.Xml;
using System.Xml.Linq;
using XMacroBridge.Core.Abstractions;
using XMacroBridge.Core.Conversion;
using XMacroBridge.Core.Diagnostics;
using XMacroBridge.Core.Models;

namespace XMacroBridge.Formats.Razer;

public sealed class RazerMacroXmlExporter : IMacroExporter
{
    public string FormatId => "razer.macro.xml";

    public async Task<IReadOnlyList<ConversionDiagnostic>> ExportAsync(
        MacroDocument document,
        Stream output,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(document);
        ArgumentNullException.ThrowIfNull(output);

        var diagnostics = new List<ConversionDiagnostic>();
        diagnostics.AddRange(new MacroValidator().Validate(document).Diagnostics);
        foreach (var key in document.Events.OfType<KeyMacroEvent>().Where(item => item.IsExtended))
        {
            diagnostics.Add(new ConversionDiagnostic(
                "RAZER_EXPORT_EXTENDED_KEY_UNSUPPORTED",
                DiagnosticSeverity.Error,
                $"尚未确认雷云 XML 对扩展虚拟键码 {key.VirtualKey} 的编码方式。",
                key.Sequence));
        }

        foreach (var mouse in document.Events.OfType<MouseMacroEvent>())
        {
            if (mouse.Button is not MouseButton.Left and not MouseButton.Right)
            {
                diagnostics.Add(new ConversionDiagnostic(
                    "RAZER_EXPORT_MOUSE_UNSUPPORTED",
                    DiagnosticSeverity.Error,
                    $"雷云 XML 导出尚未确认鼠标按钮 {mouse.Button} 的编码。",
                    mouse.Sequence));
            }
        }

        foreach (var randomDelay in document.Events.OfType<RandomDelayMacroEvent>())
        {
            diagnostics.Add(new ConversionDiagnostic(
                "RAZER_EXPORT_RANDOM_DELAY_UNSUPPORTED",
                DiagnosticSeverity.Error,
                "雷云独立宏 XML 无法等价表示 XMBC 随机延时。",
                randomDelay.Sequence));
        }

        foreach (var scanCode in document.Events.OfType<ScanCodeMacroEvent>())
        {
            diagnostics.Add(new ConversionDiagnostic(
                "RAZER_EXPORT_SCAN_CODE_UNSUPPORTED",
                DiagnosticSeverity.Error,
                "尚未确认雷云独立宏 XML 的扫描码事件编码。",
                scanCode.Sequence));
        }

        foreach (var command in document.Events.OfType<XmbcCommandMacroEvent>())
        {
            diagnostics.Add(new ConversionDiagnostic(
                "RAZER_EXPORT_XMBC_COMMAND_UNSUPPORTED",
                DiagnosticSeverity.Error,
                $"雷云宏无法等价表示 XMBC {command.Category} 命令。",
                command.Sequence,
                command.RawTag));
        }

        if (diagnostics.Any(item => item.Severity == DiagnosticSeverity.Error))
        {
            return diagnostics;
        }

        var eventContainer = new XElement("MacroEvents", CreateActionBar());
        var activeKeyIds = new Dictionary<int, int>();
        var activeMouseIds = new Dictionary<MouseButton, int>();
        var nextId = 1;

        foreach (var macroEvent in document.Events.OrderBy(item => item.Sequence))
        {
            cancellationToken.ThrowIfCancellationRequested();
            switch (macroEvent)
            {
                case DelayMacroEvent delay:
                    eventContainer.Add(CreateDelay(delay));
                    break;
                case KeyMacroEvent key:
                    eventContainer.Add(CreateKey(key, activeKeyIds, ref nextId));
                    break;
                case MouseMacroEvent mouse:
                    eventContainer.Add(CreateMouse(mouse, activeMouseIds, ref nextId));
                    break;
            }
        }

        var xml = new XDocument(
            new XDeclaration("1.0", "utf-8", null),
            new XElement("Macro", new XElement("Name", document.Name), eventContainer));
        await using var buffer = new MemoryStream();
        var settings = new XmlWriterSettings
        {
            Async = true,
            Encoding = new UTF8Encoding(false),
            Indent = true,
            IndentChars = "   ",
            NewLineChars = "\r\n",
            NewLineHandling = NewLineHandling.Replace,
            OmitXmlDeclaration = false,
        };
        using (var writer = XmlWriter.Create(buffer, settings))
        {
            cancellationToken.ThrowIfCancellationRequested();
            xml.Save(writer);
            writer.Flush();
        }

        buffer.Position = 0;
        await buffer.CopyToAsync(output, cancellationToken).ConfigureAwait(false);
        return diagnostics;
    }

    private static XElement CreateActionBar() =>
        new(
            "MacroEvent",
            new XElement("Type", "actionBar"),
            new XElement("recordProfile"),
            new XElement("selected", "false"));

    private static XElement CreateDelay(DelayMacroEvent delay)
    {
        var seconds = delay.Milliseconds / 1000m;
        return new XElement(
            "MacroEvent",
            new XElement("Type", 0),
            new XElement("Number", seconds.ToString("0.000", CultureInfo.InvariantCulture)),
            new XElement("selected", "false"));
    }

    private static XElement CreateKey(
        KeyMacroEvent key,
        IDictionary<int, int> activeIds,
        ref int nextId)
    {
        var id = GetEventId(key.VirtualKey, key.Transition, activeIds, ref nextId);
        var state = key.Transition == InputTransition.Down ? 0 : 1;
        return new XElement(
            "MacroEvent",
            new XElement("Type", 1),
            new XElement("Id", id),
            new XElement(
                "KeyEvent",
                new XElement("Makecode", key.VirtualKey),
                new XElement("State", state)),
            new XElement("flag", state),
            new XElement("selected", "false"),
            new XElement("isPairing", "false"));
    }

    private static XElement CreateMouse(
        MouseMacroEvent mouse,
        IDictionary<MouseButton, int> activeIds,
        ref int nextId)
    {
        var id = GetEventId(mouse.Button, mouse.Transition, activeIds, ref nextId);
        var state = mouse.Transition == InputTransition.Down ? 0 : 1;
        var buttonCode = mouse.Button == MouseButton.Left ? 0 : 1;
        return new XElement(
            "MacroEvent",
            new XElement("Type", 2),
            new XElement("Id", id),
            new XElement(
                "MouseEvent",
                new XElement("MouseButton", buttonCode),
                new XElement("State", state)),
            new XElement("selected", "false"),
            new XElement("isPairing", "false"));
    }

    private static int GetEventId<TKey>(
        TKey key,
        InputTransition transition,
        IDictionary<TKey, int> activeIds,
        ref int nextId)
        where TKey : notnull
    {
        if (transition == InputTransition.Down)
        {
            var assigned = nextId++;
            activeIds.Add(key, assigned);
            return assigned;
        }

        var existing = activeIds[key];
        activeIds.Remove(key);
        return existing;
    }
}
