using Microsoft.UI.Input;
using ModernExplorer.Core.Input;
using Windows.System;
using Windows.UI.Core;

namespace ModernExplorer.App;

public static class VirtualKeyMapper
{
    public static KeyModifiers GetCurrentModifiers()
    {
        var mods = KeyModifiers.None;
        if (IsDown(VirtualKey.Control)) mods |= KeyModifiers.Ctrl;
        if (IsDown(VirtualKey.Shift)) mods |= KeyModifiers.Shift;
        if (IsDown(VirtualKey.Menu)) mods |= KeyModifiers.Alt;
        if (IsDown(VirtualKey.LeftWindows) || IsDown(VirtualKey.RightWindows)) mods |= KeyModifiers.Win;
        return mods;
    }

    public static bool TryMap(VirtualKey vk, out KeyChord chord)
    {
        chord = default;
        if (vk is VirtualKey.Control or VirtualKey.LeftControl or VirtualKey.RightControl
            or VirtualKey.Shift or VirtualKey.LeftShift or VirtualKey.RightShift
            or VirtualKey.Menu or VirtualKey.LeftMenu or VirtualKey.RightMenu
            or VirtualKey.LeftWindows or VirtualKey.RightWindows)
        {
            return false;
        }

        var keyName = MapKeyName(vk);
        if (keyName is null) return false;
        chord = new KeyChord(keyName, GetCurrentModifiers());
        return true;
    }

    private static string? MapKeyName(VirtualKey vk)
    {
        if (vk >= VirtualKey.A && vk <= VirtualKey.Z)
            return ((char)('A' + (vk - VirtualKey.A))).ToString();

        if (vk >= VirtualKey.Number0 && vk <= VirtualKey.Number9)
            return ((char)('0' + (vk - VirtualKey.Number0))).ToString();

        if (vk >= VirtualKey.F1 && vk <= VirtualKey.F24)
            return "F" + (1 + (vk - VirtualKey.F1));

        if (vk >= VirtualKey.NumberPad0 && vk <= VirtualKey.NumberPad9)
            return "NumPad" + (vk - VirtualKey.NumberPad0);

        return vk switch
        {
            VirtualKey.Back => "Back",
            VirtualKey.Tab => "Tab",
            VirtualKey.Enter => "Enter",
            VirtualKey.Escape => "Escape",
            VirtualKey.Space => "Space",
            VirtualKey.PageUp => "PageUp",
            VirtualKey.PageDown => "PageDown",
            VirtualKey.End => "End",
            VirtualKey.Home => "Home",
            VirtualKey.Left => "Left",
            VirtualKey.Up => "Up",
            VirtualKey.Right => "Right",
            VirtualKey.Down => "Down",
            VirtualKey.Insert => "Insert",
            VirtualKey.Delete => "Delete",
            VirtualKey.Add => "Add",
            VirtualKey.Subtract => "Subtract",
            VirtualKey.Multiply => "Multiply",
            VirtualKey.Divide => "Divide",
            VirtualKey.Application => "Apps",
            _ => null,
        };
    }

    private static bool IsDown(VirtualKey key) =>
        InputKeyboardSource.GetKeyStateForCurrentThread(key).HasFlag(CoreVirtualKeyStates.Down);
}

