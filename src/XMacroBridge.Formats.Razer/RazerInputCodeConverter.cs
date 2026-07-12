using System.Runtime.InteropServices;

namespace XMacroBridge.Formats.Razer;

internal static class RazerInputCodeConverter
{
    public static bool TryVirtualKeyToMakeCode(int virtualKey, out int makeCode)
    {
        makeCode = 0;
        if (!OperatingSystem.IsWindows() || virtualKey is < 1 or > 255)
        {
            return false;
        }

        var scanCode = MapVirtualKey((uint)virtualKey, MapVirtualKeyType.VirtualKeyToScanCode);
        if (scanCode == 0)
        {
            return false;
        }

        makeCode = checked((int)scanCode);
        return true;
    }

    public static bool TryMakeCodeToVirtualKey(int makeCode, out int virtualKey)
    {
        virtualKey = 0;
        if (!OperatingSystem.IsWindows() || makeCode is < 1 or > 255)
        {
            return false;
        }

        var keyCode = MapVirtualKey((uint)makeCode, MapVirtualKeyType.ScanCodeToVirtualKey);
        if (keyCode == 0)
        {
            return false;
        }

        virtualKey = checked((int)keyCode);
        return true;
    }

    private enum MapVirtualKeyType : uint
    {
        VirtualKeyToScanCode = 0,
        ScanCodeToVirtualKey = 1,
    }

    [DllImport("user32.dll", EntryPoint = "MapVirtualKeyW")]
    private static extern uint MapVirtualKey(uint code, MapVirtualKeyType mapType);
}
