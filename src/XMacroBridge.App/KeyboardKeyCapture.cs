using System.Windows.Input;
using XMacroBridge.Presentation.Workspace;

namespace XMacroBridge.App;

public readonly record struct CapturedKeyboardKey(
    Key Key,
    int VirtualKey,
    bool IsExtended,
    string DisplayName);

public static class KeyboardKeyCapture
{
    public static bool TryCapture(KeyEventArgs e, out CapturedKeyboardKey captured)
    {
        ArgumentNullException.ThrowIfNull(e);

        var key = ResolveEffectiveKey(e);
        var virtualKey = KeyInterop.VirtualKeyFromKey(key);
        if (key == Key.None || virtualKey is < 1 or > 255)
        {
            captured = default;
            return false;
        }

        captured = new CapturedKeyboardKey(
            key,
            virtualKey,
            IsExtendedKey(key),
            InputEventDisplayFormatter.FormatVirtualKey(virtualKey));
        return true;
    }

    public static Key ResolveEffectiveKey(
        Key eventKey,
        Key systemKey,
        Key imeProcessedKey,
        Key deadCharProcessedKey) => eventKey switch
        {
            Key.System => systemKey,
            Key.ImeProcessed => imeProcessedKey,
            Key.DeadCharProcessed => deadCharProcessedKey,
            _ => eventKey,
        };

    private static Key ResolveEffectiveKey(KeyEventArgs e) => e.Key switch
    {
        Key.System => e.SystemKey,
        Key.ImeProcessed => e.ImeProcessedKey,
        Key.DeadCharProcessed => e.DeadCharProcessedKey,
        _ => e.Key,
    };

    private static bool IsExtendedKey(Key key) => key is
        Key.RightAlt or Key.RightCtrl or
        Key.Insert or Key.Delete or Key.Home or Key.End or
        Key.PageUp or Key.PageDown or
        Key.Left or Key.Up or Key.Right or Key.Down or
        Key.NumLock or Key.PrintScreen or Key.Divide;
}
