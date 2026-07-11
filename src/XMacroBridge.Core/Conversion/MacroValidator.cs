using XMacroBridge.Core.Abstractions;
using XMacroBridge.Core.Diagnostics;
using XMacroBridge.Core.Models;

namespace XMacroBridge.Core.Conversion;

public sealed class MacroValidator : IMacroValidator
{
    public MacroValidationResult Validate(MacroDocument document, MacroLimits? limits = null)
    {
        ArgumentNullException.ThrowIfNull(document);
        limits ??= new MacroLimits();

        var diagnostics = new List<ConversionDiagnostic>();
        if (document.Events.Count > limits.MaximumEventsPerMacro)
        {
            diagnostics.Add(new ConversionDiagnostic(
                "LIMIT_EVENT_COUNT",
                DiagnosticSeverity.Error,
                $"宏包含 {document.Events.Count} 个事件，超过上限 {limits.MaximumEventsPerMacro}。"));
        }

        var pressedKeys = new HashSet<int>();
        var pressedButtons = new HashSet<MouseButton>();
        var pressedScanCodes = new HashSet<(int ScanCode, bool IsExtended)>();

        foreach (var macroEvent in document.Events.OrderBy(item => item.Sequence))
        {
            switch (macroEvent)
            {
                case DelayMacroEvent delay:
                    ValidateDelay(delay, limits, diagnostics);
                    break;
                case RandomDelayMacroEvent randomDelay:
                    ValidateRandomDelay(randomDelay, limits, diagnostics);
                    break;
                case KeyMacroEvent key:
                    ValidateKey(key, pressedKeys, diagnostics);
                    break;
                case MouseMacroEvent mouse:
                    ValidateMouse(mouse, pressedButtons, diagnostics);
                    break;
                case ScanCodeMacroEvent scanCode:
                    ValidateScanCode(scanCode, pressedScanCodes, diagnostics);
                    break;
                case XmbcCommandMacroEvent:
                    break;
                case MacroReferenceEvent reference:
                    diagnostics.Add(new ConversionDiagnostic(
                        "REFERENCE_NOT_RESOLVED",
                        DiagnosticSeverity.Error,
                        $"宏引用尚未展开：{reference.TargetName ?? reference.TargetGuid?.ToString() ?? reference.TargetIndex?.ToString() ?? "未知引用"}。",
                        reference.Sequence));
                    break;
                case UnknownMacroEvent unknown:
                    diagnostics.Add(new ConversionDiagnostic(
                        "UNKNOWN_EVENT",
                        DiagnosticSeverity.Error,
                        $"无法识别来源事件类型：{unknown.SourceType}。",
                        unknown.Sequence));
                    break;
            }
        }

        foreach (var key in pressedKeys.Order())
        {
            diagnostics.Add(new ConversionDiagnostic(
                "KEY_NOT_RELEASED",
                DiagnosticSeverity.Error,
                $"宏结束时虚拟键码 {key} 仍处于按下状态。"));
        }

        foreach (var button in pressedButtons.Order())
        {
            diagnostics.Add(new ConversionDiagnostic(
                "MOUSE_NOT_RELEASED",
                DiagnosticSeverity.Error,
                $"宏结束时鼠标按钮 {button} 仍处于按下状态。"));
        }

        foreach (var scanCode in pressedScanCodes.OrderBy(item => item.ScanCode).ThenBy(item => item.IsExtended))
        {
            diagnostics.Add(new ConversionDiagnostic(
                "SCAN_CODE_NOT_RELEASED",
                DiagnosticSeverity.Error,
                $"宏结束时扫描码 {scanCode.ScanCode}（扩展={scanCode.IsExtended}）仍处于按下状态。"));
        }

        return new MacroValidationResult(diagnostics);
    }

    private static void ValidateRandomDelay(
        RandomDelayMacroEvent delay,
        MacroLimits limits,
        ICollection<ConversionDiagnostic> diagnostics)
    {
        if (delay.MinimumMilliseconds < 0 || delay.MaximumMilliseconds < 0 ||
            delay.MinimumMilliseconds > delay.MaximumMilliseconds)
        {
            diagnostics.Add(new ConversionDiagnostic(
                "RANDOM_DELAY_RANGE_INVALID",
                DiagnosticSeverity.Error,
                $"随机延时范围无效：{delay.MinimumMilliseconds}-{delay.MaximumMilliseconds} ms。",
                delay.Sequence));
        }
        else if (delay.MaximumMilliseconds > limits.MaximumDelayMilliseconds)
        {
            diagnostics.Add(new ConversionDiagnostic(
                "RANDOM_DELAY_TOO_LONG",
                DiagnosticSeverity.Error,
                $"随机延时最大值 {delay.MaximumMilliseconds} ms 超过上限 {limits.MaximumDelayMilliseconds} ms。",
                delay.Sequence));
        }
    }

    private static void ValidateDelay(
        DelayMacroEvent delay,
        MacroLimits limits,
        ICollection<ConversionDiagnostic> diagnostics)
    {
        if (delay.Milliseconds < 0)
        {
            diagnostics.Add(new ConversionDiagnostic(
                "DELAY_NEGATIVE",
                DiagnosticSeverity.Error,
                "延时不能为负数。",
                delay.Sequence));
        }
        else if (delay.Milliseconds > limits.MaximumDelayMilliseconds)
        {
            diagnostics.Add(new ConversionDiagnostic(
                "DELAY_TOO_LONG",
                DiagnosticSeverity.Error,
                $"单次延时 {delay.Milliseconds} ms 超过上限 {limits.MaximumDelayMilliseconds} ms。",
                delay.Sequence));
        }
    }

    private static void ValidateKey(
        KeyMacroEvent key,
        ISet<int> pressedKeys,
        ICollection<ConversionDiagnostic> diagnostics)
    {
        if (key.VirtualKey is < 0 or > 255)
        {
            diagnostics.Add(new ConversionDiagnostic(
                "KEY_CODE_OUT_OF_RANGE",
                DiagnosticSeverity.Error,
                $"虚拟键码 {key.VirtualKey} 不在 0–255 范围内。",
                key.Sequence));
            return;
        }

        var stateChanged = key.Transition == InputTransition.Down
            ? pressedKeys.Add(key.VirtualKey)
            : pressedKeys.Remove(key.VirtualKey);

        if (!stateChanged)
        {
            var code = key.Transition == InputTransition.Down ? "KEY_DUPLICATE_DOWN" : "KEY_UP_WITHOUT_DOWN";
            var action = key.Transition == InputTransition.Down ? "重复按下" : "未按下就释放";
            diagnostics.Add(new ConversionDiagnostic(
                code,
                DiagnosticSeverity.Error,
                $"虚拟键码 {key.VirtualKey} 出现{action}。",
                key.Sequence));
        }
    }

    private static void ValidateMouse(
        MouseMacroEvent mouse,
        ISet<MouseButton> pressedButtons,
        ICollection<ConversionDiagnostic> diagnostics)
    {
        var stateChanged = mouse.Transition == InputTransition.Down
            ? pressedButtons.Add(mouse.Button)
            : pressedButtons.Remove(mouse.Button);

        if (!stateChanged)
        {
            var code = mouse.Transition == InputTransition.Down ? "MOUSE_DUPLICATE_DOWN" : "MOUSE_UP_WITHOUT_DOWN";
            var action = mouse.Transition == InputTransition.Down ? "重复按下" : "未按下就释放";
            diagnostics.Add(new ConversionDiagnostic(
                code,
                DiagnosticSeverity.Error,
                $"鼠标按钮 {mouse.Button} 出现{action}。",
                mouse.Sequence));
        }
    }

    private static void ValidateScanCode(
        ScanCodeMacroEvent scanCode,
        ISet<(int ScanCode, bool IsExtended)> pressedScanCodes,
        ICollection<ConversionDiagnostic> diagnostics)
    {
        if (scanCode.ScanCode is < 0 or > 65535)
        {
            diagnostics.Add(new ConversionDiagnostic(
                "SCAN_CODE_OUT_OF_RANGE",
                DiagnosticSeverity.Error,
                $"扫描码 {scanCode.ScanCode} 不在 0–65535 范围内。",
                scanCode.Sequence));
            return;
        }

        var key = (scanCode.ScanCode, scanCode.IsExtended);
        var stateChanged = scanCode.Transition == InputTransition.Down
            ? pressedScanCodes.Add(key)
            : pressedScanCodes.Remove(key);
        if (!stateChanged)
        {
            var code = scanCode.Transition == InputTransition.Down ? "SCAN_CODE_DUPLICATE_DOWN" : "SCAN_CODE_UP_WITHOUT_DOWN";
            diagnostics.Add(new ConversionDiagnostic(
                code,
                DiagnosticSeverity.Error,
                $"扫描码 {scanCode.ScanCode} 出现无效的 {scanCode.Transition} 状态。",
                scanCode.Sequence));
        }
    }
}
