using System.Text.Json;
using ace_run.Models;
using ace_run.Services;
using Windows.System;
using Xunit;

namespace ace_run.Tests;

public class HotkeyBindingTests
{
    // --- IsValid: what Windows may be asked to reserve globally ---

    [Theory]
    [InlineData(VirtualKeyModifiers.Control, VirtualKey.K)]
    [InlineData(VirtualKeyModifiers.Menu, VirtualKey.Space)]
    [InlineData(VirtualKeyModifiers.Windows, VirtualKey.J)]
    [InlineData(VirtualKeyModifiers.Control | VirtualKeyModifiers.Menu, VirtualKey.K)]
    // Shift alongside a real modifier is fine; it is Shift *alone* that is refused.
    [InlineData(VirtualKeyModifiers.Control | VirtualKeyModifiers.Shift, VirtualKey.K)]
    public void Valid_chords(VirtualKeyModifiers modifiers, VirtualKey key)
        => Assert.True(new HotkeyBinding { Modifiers = modifiers, Key = key }.IsValid);

    [Theory]
    // A bare letter registered globally swallows that key everywhere in Windows.
    [InlineData(VirtualKeyModifiers.None, VirtualKey.K)]
    // Shift+letter is just a capital letter — same problem.
    [InlineData(VirtualKeyModifiers.Shift, VirtualKey.K)]
    // Modifiers with nothing to modify.
    [InlineData(VirtualKeyModifiers.Control, VirtualKey.None)]
    [InlineData(VirtualKeyModifiers.None, VirtualKey.None)]
    public void Invalid_chords(VirtualKeyModifiers modifiers, VirtualKey key)
        => Assert.False(new HotkeyBinding { Modifiers = modifiers, Key = key }.IsValid);

    // --- ToWin32Modifiers: the mapping that must not become a cast ---

    [Theory]
    [InlineData(VirtualKeyModifiers.Menu, 0x0001u)]      // MOD_ALT
    [InlineData(VirtualKeyModifiers.Control, 0x0002u)]   // MOD_CONTROL
    [InlineData(VirtualKeyModifiers.Shift, 0x0004u)]     // MOD_SHIFT
    [InlineData(VirtualKeyModifiers.Windows, 0x0008u)]   // MOD_WIN
    [InlineData(VirtualKeyModifiers.None, 0x0000u)]
    public void Each_modifier_maps_to_its_win32_flag(VirtualKeyModifiers modifiers, uint expected)
        => Assert.Equal(expected, new HotkeyBinding { Modifiers = modifiers }.ToWin32Modifiers());

    [Fact]
    public void Control_and_Menu_do_not_survive_a_straight_cast()
    {
        // The reason the mapping is hand-written. VirtualKeyModifiers numbers Control 1 and
        // Menu 2; Win32 numbers them 2 and 1. A cast would silently swap Ctrl and Alt, and
        // the chord would register — just not the one the user pressed.
        Assert.Equal(0x0002u, new HotkeyBinding { Modifiers = VirtualKeyModifiers.Control }.ToWin32Modifiers());
        Assert.NotEqual((uint)VirtualKeyModifiers.Control,
                        new HotkeyBinding { Modifiers = VirtualKeyModifiers.Control }.ToWin32Modifiers());

        Assert.Equal(0x0001u, new HotkeyBinding { Modifiers = VirtualKeyModifiers.Menu }.ToWin32Modifiers());
        Assert.NotEqual((uint)VirtualKeyModifiers.Menu,
                        new HotkeyBinding { Modifiers = VirtualKeyModifiers.Menu }.ToWin32Modifiers());
    }

    [Fact]
    public void Combined_modifiers_or_together()
    {
        var binding = new HotkeyBinding
        {
            Modifiers = VirtualKeyModifiers.Control | VirtualKeyModifiers.Menu
                        | VirtualKeyModifiers.Shift | VirtualKeyModifiers.Windows
        };

        Assert.Equal(0x0001u | 0x0002u | 0x0004u | 0x0008u, binding.ToWin32Modifiers());
    }

    // --- KeyName ---

    [Theory]
    [InlineData(VirtualKey.A, "A")]
    [InlineData(VirtualKey.Z, "Z")]
    [InlineData(VirtualKey.Number0, "0")]
    [InlineData(VirtualKey.Number9, "9")]
    [InlineData(VirtualKey.NumberPad0, "Num 0")]
    [InlineData(VirtualKey.NumberPad7, "Num 7")]
    [InlineData(VirtualKey.Escape, "Esc")]
    [InlineData(VirtualKey.Back, "Backspace")]
    [InlineData(VirtualKey.Enter, "Enter")]
    [InlineData(VirtualKey.PageUp, "Page Up")]
    [InlineData(VirtualKey.Left, "←")]
    [InlineData(VirtualKey.Down, "↓")]
    // Named in VirtualKey already, so the switch falls through to ToString.
    [InlineData(VirtualKey.F5, "F5")]
    [InlineData(VirtualKey.Space, "Space")]
    public void KeyName_names(VirtualKey key, string expected)
        => Assert.Equal(expected, HotkeyBinding.KeyName(key));

    [Theory]
    // OEM punctuation has no VirtualKey member, so without the lookup table these print as
    // bare numbers — "188" instead of ",".
    [InlineData(0xBC, ",")]
    [InlineData(0xBE, ".")]
    [InlineData(0xDB, "[")]
    [InlineData(0xDC, "\\")]
    [InlineData(0xC0, "`")]
    public void KeyName_names_the_oem_punctuation_keys(int code, string expected)
        => Assert.Equal(expected, HotkeyBinding.KeyName((VirtualKey)code));

    // --- ToDisplayString ---

    [Fact]
    public void ToDisplayString_uses_windows_own_modifier_order()
    {
        // Win, Ctrl, Alt, Shift — regardless of the order the flags were set in.
        var binding = new HotkeyBinding
        {
            Modifiers = VirtualKeyModifiers.Shift | VirtualKeyModifiers.Windows
                        | VirtualKeyModifiers.Menu | VirtualKeyModifiers.Control,
            Key = VirtualKey.K
        };

        Assert.Equal("Win + Ctrl + Alt + Shift + K", binding.ToDisplayString());
    }

    [Fact]
    public void ToDisplayString_of_a_bare_key_is_just_the_key()
        => Assert.Equal("Esc", new HotkeyBinding { Key = VirtualKey.Escape }.ToDisplayString());

    // --- Persistence ---

    [Fact]
    public void A_binding_round_trips_as_readable_names()
    {
        var binding = new HotkeyBinding
        {
            Modifiers = VirtualKeyModifiers.Control | VirtualKeyModifiers.Menu,
            Key = VirtualKey.K
        };

        var json = JsonSerializer.Serialize(binding, AceRunJson.Options);

        // Names, not numbers — the file is meant to be legible.
        Assert.Contains("Control", json);
        Assert.Contains("Menu", json);
        Assert.Contains("\"K\"", json);
        // IsValid is [JsonIgnore]: a get-only property would otherwise be written on every
        // save and discarded on every load.
        Assert.DoesNotContain("IsValid", json);

        var loaded = JsonSerializer.Deserialize<HotkeyBinding>(json, AceRunJson.Options);
        Assert.NotNull(loaded);
        Assert.Equal(binding.Modifiers, loaded.Modifiers);
        Assert.Equal(binding.Key, loaded.Key);
    }

    [Fact]
    public void Settings_default_to_the_pre_settings_behaviour()
    {
        // Every default reproduces how the app behaved before it had a settings screen, so an
        // existing install that gains a Settings block behaves identically until touched.
        var settings = new AppSettings();

        Assert.Null(settings.Hotkey);
        Assert.False(settings.StartWithWindows);
        Assert.True(settings.CloseToTray);
        Assert.Equal(AppTheme.System, settings.Theme);
        Assert.Equal(string.Empty, settings.Language);
        Assert.False(settings.HideOnLaunch);
    }
}
