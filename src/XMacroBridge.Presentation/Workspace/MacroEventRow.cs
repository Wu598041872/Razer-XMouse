using System.Globalization;
using XMacroBridge.Core.Models;

namespace XMacroBridge.Presentation.Workspace;

public sealed record MacroEventRow(
    int DisplayIndex,
    int EventIndex,
    MacroEvent Event,
    string Type,
    string Details)
{
    public long Sequence => Event.Sequence;

    public int DisplayNumber => DisplayIndex + 1;

    public bool IsFixedDelay => Event is DelayMacroEvent;

    public MacroEventVisualKind VisualKind => Event switch
    {
        KeyMacroEvent or ScanCodeMacroEvent => MacroEventVisualKind.Keyboard,
        MouseMacroEvent => MacroEventVisualKind.Mouse,
        DelayMacroEvent or RandomDelayMacroEvent => MacroEventVisualKind.Delay,
        UnknownMacroEvent => MacroEventVisualKind.Error,
        _ => MacroEventVisualKind.Info,
    };

    public string TransitionSymbol => Event switch
    {
        KeyMacroEvent { Transition: InputTransition.Down } or
        MouseMacroEvent { Transition: InputTransition.Down } or
        ScanCodeMacroEvent { Transition: InputTransition.Down } => "↓",
        KeyMacroEvent { Transition: InputTransition.Up } or
        MouseMacroEvent { Transition: InputTransition.Up } or
        ScanCodeMacroEvent { Transition: InputTransition.Up } => "↑",
        _ => string.Empty,
    };

    public string EditorLabel => Event switch
    {
        DelayMacroEvent delay => FormatSeconds(delay.Milliseconds),
        RandomDelayMacroEvent delay => $"{FormatSeconds(delay.MinimumMilliseconds)}–{FormatSeconds(delay.MaximumMilliseconds)}",
        KeyMacroEvent key => InputEventDisplayFormatter.FormatVirtualKey(key.VirtualKey, key.DisplayName),
        MouseMacroEvent mouse => FormatMouseEditorLabel(mouse.Button),
        ScanCodeMacroEvent scanCode => $"扫描码 {scanCode.ScanCode}",
        XmbcCommandMacroEvent command => command.Category,
        MacroReferenceEvent => "嵌套宏",
        UnknownMacroEvent => "未知事件",
        _ => Type,
    };

    public string TechnicalSummary => Event switch
    {
        DelayMacroEvent delay => $"固定延时 · {delay.Milliseconds} ms",
        RandomDelayMacroEvent delay => $"随机延时 · {delay.MinimumMilliseconds}–{delay.MaximumMilliseconds} ms",
        KeyMacroEvent key => $"{Type} · VK {key.VirtualKey}{(key.IsExtended ? " · 扩展键" : string.Empty)}",
        MouseMacroEvent mouse => $"{Type} · {mouse.Button} · {mouse.Transition}",
        ScanCodeMacroEvent scanCode => $"{Type} · 扫描码 {scanCode.ScanCode}{(scanCode.IsExtended ? " · 扩展键" : string.Empty)}",
        XmbcCommandMacroEvent command => $"XMBC 命令 · {command.Category}",
        MacroReferenceEvent reference => $"嵌套宏 · {reference.TargetName ?? "引用目标"}",
        UnknownMacroEvent unknown => $"未知事件 · {unknown.SourceType}",
        _ => Type,
    };

    public string AutomationSummary => Event switch
    {
        KeyMacroEvent key => $"事件 {Sequence}：键盘 {EditorLabel} {InputEventDisplayFormatter.FormatTransition(key.Transition)}",
        MouseMacroEvent mouse => $"事件 {Sequence}：鼠标 {EditorLabel} {InputEventDisplayFormatter.FormatTransition(mouse.Transition)}",
        ScanCodeMacroEvent scanCode => $"事件 {Sequence}：{EditorLabel} {InputEventDisplayFormatter.FormatTransition(scanCode.Transition)}",
        _ => $"事件 {Sequence}：{EditorLabel}",
    };

    private static string FormatSeconds(long milliseconds) =>
        $"{(milliseconds / 1000m).ToString("0.000", CultureInfo.InvariantCulture)}s";

    private static string FormatMouseEditorLabel(MouseButton button) => button switch
    {
        MouseButton.WheelUp or MouseButton.WheelDown or MouseButton.TiltLeft or MouseButton.TiltRight =>
            InputEventDisplayFormatter.FormatMouseButton(button),
        MouseButton.XButton1 or MouseButton.XButton2 =>
            $"{InputEventDisplayFormatter.FormatMouseButton(button)} 单击",
        _ => $"{InputEventDisplayFormatter.FormatMouseButton(button)}单击",
    };
}

public enum MacroEventVisualKind
{
    Info,
    Keyboard,
    Mouse,
    Delay,
    Warning,
    Error,
}
