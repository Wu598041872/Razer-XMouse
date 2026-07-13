using XMacroBridge.Core.Models;

namespace XMacroBridge.Presentation.Workspace;

public static class InputEventDisplayFormatter
{
    public static string FormatEventType(MacroEvent macroEvent) => macroEvent switch
    {
        DelayMacroEvent => "延时",
        KeyMacroEvent key => FormatKeyType(key),
        MouseMacroEvent mouse => FormatMouseType(mouse),
        ScanCodeMacroEvent scanCode => FormatScanCodeType(scanCode),
        _ => "事件",
    };

    public static string FormatKeyType(KeyMacroEvent key) =>
        $"键盘 {FormatVirtualKey(key.VirtualKey, key.DisplayName)} {FormatTransition(key.Transition)}";

    public static string FormatMouseType(MouseMacroEvent mouse) =>
        $"鼠标{FormatMouseButton(mouse.Button)} {FormatTransition(mouse.Transition)}";

    public static string FormatScanCodeType(ScanCodeMacroEvent scanCode) =>
        $"扫描码 {scanCode.ScanCode} {FormatTransition(scanCode.Transition)}";

    public static string FormatTransition(InputTransition transition) => transition switch
    {
        InputTransition.Down => "按下",
        InputTransition.Up => "松开",
        _ => transition.ToString(),
    };

    public static string FormatVirtualKey(int virtualKey, string? displayName = null)
    {
        if (virtualKey is >= 0x41 and <= 0x5A or >= 0x30 and <= 0x39)
        {
            return ((char)virtualKey).ToString();
        }

        if (virtualKey is >= 0x70 and <= 0x87)
        {
            return $"F{virtualKey - 0x70 + 1}";
        }

        if (virtualKey is >= 0x60 and <= 0x69)
        {
            return $"数字键盘 {virtualKey - 0x60}";
        }

        var knownName = virtualKey switch
        {
            0x03 => "取消键",
            0x0C => "清除键",
            0x08 => "退格键",
            0x09 => "Tab",
            0x0D => "Enter",
            0x10 => "Shift",
            0x11 => "Ctrl",
            0x12 => "Alt",
            0x13 => "Pause",
            0x14 => "Caps Lock",
            0x15 => "输入法模式键",
            0x17 => "Junja",
            0x18 => "Final",
            0x19 => "Hanja / Kanji",
            0x1B => "Esc",
            0x1C => "输入法转换",
            0x1D => "输入法不转换",
            0x1E => "输入法接受",
            0x1F => "输入法模式切换",
            0x20 => "空格键",
            0x21 => "Page Up",
            0x22 => "Page Down",
            0x23 => "End",
            0x24 => "Home",
            0x25 => "左方向键",
            0x26 => "上方向键",
            0x27 => "右方向键",
            0x28 => "下方向键",
            0x2C => "Print Screen",
            0x2D => "Insert",
            0x2E => "Delete",
            0x2F => "帮助键",
            0x5B => "左 Windows",
            0x5C => "右 Windows",
            0x5D => "菜单键",
            0x5F => "睡眠键",
            0x6A => "数字键盘 *",
            0x6B => "数字键盘 +",
            0x6C => "数字键盘分隔符",
            0x6D => "数字键盘 -",
            0x6E => "数字键盘 .",
            0x6F => "数字键盘 /",
            0x90 => "Num Lock",
            0x91 => "Scroll Lock",
            0x92 => "OEM 特殊键",
            0x93 => "OEM 特殊键",
            0xA0 => "左 Shift",
            0xA1 => "右 Shift",
            0xA2 => "左 Ctrl",
            0xA3 => "右 Ctrl",
            0xA4 => "左 Alt",
            0xA5 => "右 Alt",
            0xA6 => "浏览器后退",
            0xA7 => "浏览器前进",
            0xA8 => "浏览器刷新",
            0xA9 => "浏览器停止",
            0xAA => "浏览器搜索",
            0xAB => "浏览器收藏",
            0xAC => "浏览器主页",
            0xAD => "静音",
            0xAE => "音量减小",
            0xAF => "音量增大",
            0xB0 => "下一曲",
            0xB1 => "上一曲",
            0xB2 => "停止播放",
            0xB3 => "播放/暂停",
            0xB4 => "启动邮件",
            0xB5 => "启动媒体选择",
            0xB6 => "启动应用 1",
            0xB7 => "启动应用 2",
            0xBA => "; 键",
            0xBB => "= 键",
            0xBC => ", 键",
            0xBD => "- 键",
            0xBE => ". 键",
            0xBF => "/ 键",
            0xC0 => "` 键",
            0xDB => "[ 键",
            0xDC => "\\ 键",
            0xDD => "] 键",
            0xDE => "' 键",
            0xDF => "OEM 8 键",
            0xE2 => "OEM 102 键",
            0xE5 => "输入法处理键",
            0xE7 => "Unicode 输入键",
            0xF6 => "Attn",
            0xF7 => "CrSel",
            0xF8 => "ExSel",
            0xF9 => "Erase EOF",
            0xFA => "Play",
            0xFB => "Zoom",
            0xFC => "保留键",
            0xFD => "PA1",
            0xFE => "清除键",
            _ => null,
        };
        return knownName ?? (!string.IsNullOrWhiteSpace(displayName) ? displayName : "特殊按键");
    }

    public static string FormatMouseButton(MouseButton button) => button switch
    {
        MouseButton.Left => "左键",
        MouseButton.Right => "右键",
        MouseButton.Middle => "中键",
        MouseButton.XButton1 => "侧键 1",
        MouseButton.XButton2 => "侧键 2",
        MouseButton.WheelUp => "滚轮向上",
        MouseButton.WheelDown => "滚轮向下",
        MouseButton.TiltLeft => "滚轮左倾",
        MouseButton.TiltRight => "滚轮右倾",
        _ => button.ToString(),
    };
}
