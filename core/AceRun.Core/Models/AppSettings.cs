using System.Collections.Generic;
using System.Text;
using System.Text.Json.Serialization;
using Windows.System;

namespace ace_run.Models;

/// <summary>
/// Application-level preferences. These live in <c>config.json</c> rather than in a
/// workspace file because they follow the user, not the workspace.
///
/// Every default reproduces the behaviour this app had before there were settings at all,
/// so an existing install that gains a <c>Settings</c> block changes nothing until the user
/// touches something. That is also why <see cref="CloseToTray"/> defaults to true.
/// </summary>
public class AppSettings
{
    /// <summary>null = no global hotkey registered. Off by default — see 10-settings.md §2.</summary>
    public HotkeyBinding? Hotkey { get; set; }

    public bool StartWithWindows { get; set; }
    public bool CloseToTray { get; set; } = true;
    public AppTheme Theme { get; set; } = AppTheme.System;

    /// <summary>BCP-47 tag, or empty for "follow the system". Applied at startup only.</summary>
    public string Language { get; set; } = string.Empty;

    public bool HideOnLaunch { get; set; }
}

public enum AppTheme
{
    System,
    Light,
    Dark
}

/// <summary>
/// A global hotkey, held as the WinRT key enums rather than a packed int or a parsed
/// string: <see cref="Services.DataService.JsonOptions"/> already carries a
/// <c>JsonStringEnumConverter</c>, so the file reads <c>"Control, Menu"</c> / <c>"Space"</c>
/// with no converter of our own and no parser to keep in sync with the formatter.
/// </summary>
public class HotkeyBinding
{
    public VirtualKeyModifiers Modifiers { get; set; }
    public VirtualKey Key { get; set; }

    // Win32 fsModifiers. The numbering happens to match VirtualKeyModifiers for Shift and
    // Win but not for Control/Alt, so it is mapped rather than cast.
    private const uint ModAlt = 0x0001;
    private const uint ModControl = 0x0002;
    private const uint ModShift = 0x0004;
    private const uint ModWin = 0x0008;

    /// <summary>
    /// A binding with no non-modifier key, or with only Shift, is refused. A bare letter
    /// registered globally swallows that key everywhere in Windows.
    ///
    /// JsonIgnore because System.Text.Json serializes get-only properties: without it the
    /// file grows an <c>"IsValid": true</c> line that is written on every save and thrown
    /// away on every load.
    /// </summary>
    [JsonIgnore]
    public bool IsValid =>
        Key != VirtualKey.None
        && (Modifiers & (VirtualKeyModifiers.Control | VirtualKeyModifiers.Menu | VirtualKeyModifiers.Windows)) != 0;

    public uint ToWin32Modifiers()
    {
        uint value = 0;
        if (Modifiers.HasFlag(VirtualKeyModifiers.Control)) value |= ModControl;
        if (Modifiers.HasFlag(VirtualKeyModifiers.Menu)) value |= ModAlt;
        if (Modifiers.HasFlag(VirtualKeyModifiers.Shift)) value |= ModShift;
        if (Modifiers.HasFlag(VirtualKeyModifiers.Windows)) value |= ModWin;
        return value;
    }

    /// <summary>Windows' own order: Win, Ctrl, Alt, Shift, key.</summary>
    public string ToDisplayString()
    {
        var parts = new StringBuilder();
        if (Modifiers.HasFlag(VirtualKeyModifiers.Windows)) parts.Append("Win + ");
        if (Modifiers.HasFlag(VirtualKeyModifiers.Control)) parts.Append("Ctrl + ");
        if (Modifiers.HasFlag(VirtualKeyModifiers.Menu)) parts.Append("Alt + ");
        if (Modifiers.HasFlag(VirtualKeyModifiers.Shift)) parts.Append("Shift + ");
        parts.Append(KeyName(Key));
        return parts.ToString();
    }

    /// <summary>
    /// The OEM punctuation keys have no names in <see cref="VirtualKey"/>, so an unmapped
    /// cast would print a bare number ("188" for comma). Everything else — letters, digits,
    /// F-keys, arrows — either prints a single character or already has a usable name.
    /// </summary>
    private static readonly Dictionary<int, string> _oemNames = new()
    {
        [0xBA] = ";", [0xBB] = "+", [0xBC] = ",", [0xBD] = "-", [0xBE] = ".", [0xBF] = "/",
        [0xC0] = "`", [0xDB] = "[", [0xDC] = "\\", [0xDD] = "]", [0xDE] = "'"
    };

    public static string KeyName(VirtualKey key)
    {
        var code = (int)key;

        if (key is >= VirtualKey.A and <= VirtualKey.Z) return ((char)code).ToString();
        if (key is >= VirtualKey.Number0 and <= VirtualKey.Number9) return ((char)code).ToString();
        if (key is >= VirtualKey.NumberPad0 and <= VirtualKey.NumberPad9)
            return "Num " + (code - (int)VirtualKey.NumberPad0);
        if (_oemNames.TryGetValue(code, out var oem)) return oem;

        return key switch
        {
            VirtualKey.Escape => "Esc",
            VirtualKey.Back => "Backspace",
            VirtualKey.Enter => "Enter",
            VirtualKey.PageUp => "Page Up",
            VirtualKey.PageDown => "Page Down",
            VirtualKey.Left => "←",
            VirtualKey.Up => "↑",
            VirtualKey.Right => "→",
            VirtualKey.Down => "↓",
            _ => key.ToString()
        };
    }
}
