using System;
using ace_run.Models;
using ace_run.Services;
using Microsoft.UI;
using Microsoft.UI.Input;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Windows.Graphics;
using Windows.System;
using Windows.UI.Core;
using WinRT.Interop;

namespace ace_run;

/// <summary>
/// The settings surface. A window rather than a <see cref="ContentDialog"/> like the two
/// manage dialogs: seven cards is taller than a dialog wants to be, and the interaction is
/// apply-on-change, which has no use for the OK/Cancel pair a dialog is built around.
///
/// It never loads its own <see cref="WorkspaceConfig"/>. MainWindow owns the one live copy
/// and writes the whole thing back when it closes, so a second copy edited here would be
/// silently overwritten. Everything below mutates <c>_owner.Config.Settings</c> in place.
/// </summary>
public sealed partial class SettingsWindow : Window
{
    private readonly MainWindow _owner;
    private AppSettings Settings => _owner.Config.Settings;

    /// <summary>Suppresses the change handlers while the controls are being populated.</summary>
    private bool _loading = true;

    private bool _recording;

    private const int WidthDip = 560;
    private const int HeightDip = 680;

    /// <summary>
    /// The window may be shortened but not narrowed: the width is what the widest setting row
    /// needs, so the floor and the opening width are the same number.
    /// </summary>
    private const int MinHeightDip = 420;

    public SettingsWindow(MainWindow owner)
    {
        _owner = owner;
        InitializeComponent();

        ApplyInitialWindowPlacement();
        WindowIconService.Apply(this);

        ExtendsContentIntoTitleBar = true;
        AppWindow.TitleBar.PreferredHeightOption = TitleBarHeightOption.Tall;
        SetTitleBar(AppTitleBar);

        if (AppWindow.Presenter is OverlappedPresenter presenter)
            presenter.IsMaximizable = false;

        ThemeService.Apply(RootGrid, Settings.Theme);
        ThemeService.Attach(this);

        // Arming the recorder and then alt-tabbing away should not leave it waiting to
        // swallow the next keystroke. This replaces the button's LostFocus cancel, which
        // could not tell "the user moved on" from "Alt moved focus mid-chord".
        Activated += (_, args) =>
        {
            if (args.WindowActivationState == WindowActivationState.Deactivated)
                CancelRecording();
        };

        ApplyStrings();
        LoadValues();
        _loading = false;
    }

    /// <summary>
    /// This window's DIP-to-pixel factor, for the display it is on right now. Null XamlRoot in
    /// the constructor — measured on MainWindow, same lifecycle here — so that one call falls
    /// through to the OS.
    /// </summary>
    private double CurrentScale =>
        RootGrid.XamlRoot?.RasterizationScale ?? DisplayScale.ForWindow(AppWindow);

    /// <summary>
    /// Sets the resize floor for a given display scale.
    /// </summary>
    /// <remarks>
    /// Re-derivable rather than set once, for the same reason as <c>MainWindow</c>'s:
    /// <c>PreferredMinimum*</c> is physical pixels and Windows never rescales it. This window
    /// reaches the broken state without being dragged anywhere, though. The OS creates it on
    /// whichever display it likes and the constructor moves it onto the owner's, so the floor
    /// has to be computed for the *destination* up front — measured, an owner on a 100% display
    /// used to get a 840×630 DIP floor left over from a 150% creation display, against a window
    /// that wants to be 560 wide, and the width clamped half again too large on open.
    ///
    /// The <see cref="DpiChangeWatcher"/> subscription below does in fact cover the constructor's
    /// own move — <c>WM_DPICHANGED</c> is sent synchronously from inside <c>AppWindow.Move</c>,
    /// message loop or no. Setting it from the owner's scale up front is still worth the line: it
    /// is what makes the destination explicit rather than something the reader has to infer from
    /// a message arriving mid-constructor, and it is already correct when both displays share a
    /// scale and no message is sent at all.
    /// </remarks>
    private void ApplyMinimumSize(double scale)
    {
        if (AppWindow.Presenter is not OverlappedPresenter presenter || scale <= 0) return;

        presenter.PreferredMinimumWidth = WindowPlacement.ToPixels(WidthDip, scale);
        presenter.PreferredMinimumHeight = WindowPlacement.ToPixels(MinHeightDip, scale);
    }

    private void ApplyInitialWindowPlacement()
    {
        // The display this window belongs on is the owner's, which is not necessarily the one
        // the OS created it on. Everything describing the destination is derived from this.
        var ownerScale = DisplayScale.ForWindow(_owner.AppWindow);

        ApplyMinimumSize(ownerScale);

        // Detached on Closed, unlike MainWindow's: this window really is destroyed, and HWNDs
        // are recycled.
        var hwnd = WindowNative.GetWindowHandle(this);
        DpiChangeWatcher.Attach(hwnd, ApplyMinimumSize);
        Closed += (_, _) => DpiChangeWatcher.Detach(hwnd);

        // The size, by contrast, is computed in *this* window's current scale, deliberately not
        // the owner's. When the two differ the Move below crosses a DPI boundary and Windows
        // rescales the window by the ratio on arrival, landing it at exactly WidthDip ×
        // HeightDip there. Converting to the owner's scale here would apply that ratio twice.
        //
        // AppWindow.Resize takes physical pixels; XamlRoot.RasterizationScale is not available
        // this early, hence the P/Invoke behind CurrentScale — same as MainWindow.
        var scale = CurrentScale;
        AppWindow.Resize(new SizeInt32(
            WindowPlacement.ToPixels(WidthDip, scale),
            WindowPlacement.ToPixels(HeightDip, scale)));

        CenterOverOwner(ownerScale);
    }

    /// <summary>
    /// Puts the window over the middle of the main window. Without this the OS picks the
    /// spot, and a cascade from the last top-level window can land it a screen away from
    /// the window it belongs to.
    ///
    /// The owner's own monitor is what the result is clamped to, not the settings window's:
    /// its <c>DisplayArea</c> is resolved from where the OS happened to put it, which is the
    /// placement being replaced. A minimised owner reports a position off-screen
    /// (-32000), so that case centres on the monitor instead.
    /// </summary>
    private void CenterOverOwner(double ownerScale)
    {
        var owner = _owner.AppWindow;
        var work = DisplayArea.GetFromWindowId(owner.Id, DisplayAreaFallback.Primary).WorkArea;

        var minimized = owner.Presenter is OverlappedPresenter
            { State: OverlappedPresenterState.Minimized };

        var anchor = minimized
            ? new PixelRect(work.X, work.Y, work.Width, work.Height)
            : new PixelRect(owner.Position.X, owner.Position.Y, owner.Size.Width, owner.Size.Height);

        // The size this window will *land* at, which is not the size it has while this runs.
        // The Move can cross a DPI boundary, and Windows rescales the window on arrival; the
        // owner's scale is therefore the one the arithmetic has to be done in. Centring on the
        // pre-move size leaves the window off by the ratio between the two displays.
        var landed = new PixelSize(
            WindowPlacement.ToPixels(WidthDip, ownerScale),
            WindowPlacement.ToPixels(HeightDip, ownerScale));

        var position = WindowPlacement.CenterIn(
            anchor, landed, new PixelRect(work.X, work.Y, work.Width, work.Height));

        AppWindow.Move(new PointInt32(position.X, position.Y));
    }

    #region Strings

    private void ApplyStrings()
    {
        Title = Loc.GetString("Settings_Title");
        WindowTitleText.Text = Title;

        GeneralGroupLabel.Text = Loc.GetString("Settings_Group_General");
        AppearanceGroupLabel.Text = Loc.GetString("Settings_Group_Appearance");
        StorageGroupLabel.Text = Loc.GetString("Settings_Group_Storage");

        HotkeyHeader.Text = Loc.GetString("Settings_Hotkey_Header");
        HotkeyDesc.Text = Loc.GetString("Settings_Hotkey_Desc");
        ToolTipService.SetToolTip(HotkeyClearButton, Loc.GetString("Settings_Hotkey_Clear"));
        Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(
            HotkeyClearButton, Loc.GetString("Settings_Hotkey_Clear"));

        StartupHeader.Text = Loc.GetString("Settings_Startup_Header");
        StartupDesc.Text = Loc.GetString("Settings_Startup_Desc");

        CloseToTrayHeader.Text = Loc.GetString("Settings_CloseToTray_Header");
        CloseToTrayDesc.Text = Loc.GetString("Settings_CloseToTray_Desc");

        HideOnLaunchHeader.Text = Loc.GetString("Settings_HideOnLaunch_Header");
        HideOnLaunchDesc.Text = Loc.GetString("Settings_HideOnLaunch_Desc");

        ThemeHeader.Text = Loc.GetString("Settings_Theme_Header");
        ThemeDesc.Text = Loc.GetString("Settings_Theme_Desc");

        LanguageHeader.Text = Loc.GetString("Settings_Language_Header");
        LanguageDesc.Text = Loc.GetString("Settings_Language_Desc");

        ResetCacheHeader.Text = Loc.GetString("Settings_ResetCache_Header");
        ResetCacheDesc.Text = Loc.GetString("Settings_ResetCache_Desc");
        ResetCacheButton.Content = Loc.GetString("Settings_ResetCache_Button");

        RestartInfoBar.Message = Loc.GetString("Settings_Language_RestartRequired");
    }

    #endregion

    #region Load

    private void LoadValues()
    {
        // The registry is the truth for this one — the user may have removed the Run entry
        // by hand, or restored a config.json from a machine where it was on.
        var startupOn = StartupService.IsEnabled;
        Settings.StartWithWindows = startupOn;
        StartupToggle.IsOn = startupOn;

        CloseToTrayToggle.IsOn = Settings.CloseToTray;
        HideOnLaunchToggle.IsOn = Settings.HideOnLaunch;

        AddComboItem(ThemeCombo, Loc.GetString("Settings_Theme_System"), AppTheme.System);
        AddComboItem(ThemeCombo, Loc.GetString("Settings_Theme_Light"), AppTheme.Light);
        AddComboItem(ThemeCombo, Loc.GetString("Settings_Theme_Dark"), AppTheme.Dark);
        ThemeCombo.SelectedIndex = (int)Settings.Theme;

        // The tag in Tag, the display text in Content: the tags are persisted to JSON and
        // must survive a language change. Same split as the color pickers.
        AddComboItem(LanguageCombo, Loc.GetString("Settings_Language_System"), string.Empty);
        AddComboItem(LanguageCombo, "English", "en-US");
        AddComboItem(LanguageCombo, "繁體中文", "zh-TW");
        AddComboItem(LanguageCombo, "日本語", "ja-JP");
        LanguageCombo.SelectedIndex = Settings.Language switch
        {
            "en-US" => 1,
            "zh-TW" => 2,
            "ja-JP" => 3,
            _ => 0
        };

        UpdateHotkeyDisplay();
    }

    private static void AddComboItem(ComboBox combo, string text, object tag) =>
        combo.Items.Add(new ComboBoxItem { Content = text, Tag = tag });

    private void Persist() => _owner.PersistSettings();

    #endregion

    #region Hotkey

    private void UpdateHotkeyDisplay()
    {
        HotkeyRecordButton.Content = Settings.Hotkey is { } binding
            ? binding.ToDisplayString()
            : Loc.GetString("Settings_Hotkey_None");
        HotkeyClearButton.IsEnabled = Settings.Hotkey is not null;
    }

    private void HotkeyRecordButton_Click(object sender, RoutedEventArgs e)
    {
        _recording = true;
        HotkeyInfoBar.IsOpen = false;
        HotkeyRecordButton.Content = Loc.GetString("Settings_Hotkey_Recording");
    }

    private void CancelRecording()
    {
        if (!_recording) return;
        _recording = false;
        UpdateHotkeyDisplay();
    }

    /// <summary>
    /// The recorder, on the window root rather than on the button that armed it.
    ///
    /// Two reasons, both found by testing. PreviewKeyDown rather than KeyDown: Space and
    /// Enter are the button's own invoke keys and never reach a bubbling handler, and Tab
    /// would move focus mid-capture. And the *root* rather than the button: a chord
    /// containing Alt can take focus off the button before the chord completes — Alt+Space
    /// is Windows' window-menu shortcut — so a handler on the button captured Ctrl+Alt+K
    /// happily and dropped Ctrl+Alt+Space, which is the one chord a launcher user is most
    /// likely to reach for. Anchoring at the root makes focus movement inside the window
    /// irrelevant; recording is instead cancelled when the window itself is deactivated.
    /// </summary>
    private void RootGrid_PreviewKeyDown(object sender, KeyRoutedEventArgs e)
    {
        if (!_recording) return;
        e.Handled = true;

        var key = e.Key;
        if (IsModifierKey(key)) return; // wait for the chord to be completed

        if (key == VirtualKey.Escape)
        {
            CancelRecording();
            return;
        }

        if (key is VirtualKey.Back or VirtualKey.Delete)
        {
            _recording = false;
            ApplyHotkey(null);
            return;
        }

        var binding = new HotkeyBinding { Modifiers = CurrentModifiers(), Key = key };
        if (!binding.IsValid)
        {
            // Stay in recording mode: the user is mid-chord, not finished and wrong.
            ShowHotkeyError(Loc.GetString("Settings_Hotkey_NeedModifier"));
            return;
        }

        _recording = false;
        ApplyHotkey(binding);
    }

    private void HotkeyClearButton_Click(object sender, RoutedEventArgs e) => ApplyHotkey(null);

    private void ApplyHotkey(HotkeyBinding? binding)
    {
        var previous = Settings.Hotkey;

        if (!_owner.TryApplyHotkey(binding))
        {
            // Registration failed, so nothing is registered right now — put the old chord
            // back rather than leaving the user with neither.
            _owner.TryApplyHotkey(previous);
            ShowHotkeyError(Loc.GetString("Settings_Hotkey_Conflict"));
            UpdateHotkeyDisplay();
            return;
        }

        HotkeyInfoBar.IsOpen = false;
        UpdateHotkeyDisplay();
        Persist();
    }

    private void ShowHotkeyError(string message)
    {
        HotkeyInfoBar.Message = message;
        HotkeyInfoBar.IsOpen = true;
    }

    private static bool IsModifierKey(VirtualKey key) => key
        is VirtualKey.Control or VirtualKey.LeftControl or VirtualKey.RightControl
        or VirtualKey.Shift or VirtualKey.LeftShift or VirtualKey.RightShift
        or VirtualKey.Menu or VirtualKey.LeftMenu or VirtualKey.RightMenu
        or VirtualKey.LeftWindows or VirtualKey.RightWindows;

    /// <summary>
    /// KeyRoutedEventArgs carries no modifier state, so the modifiers are read from the
    /// thread's keyboard state at the moment the non-modifier key arrived.
    /// </summary>
    private static VirtualKeyModifiers CurrentModifiers()
    {
        var mods = VirtualKeyModifiers.None;
        if (IsDown(VirtualKey.Control)) mods |= VirtualKeyModifiers.Control;
        if (IsDown(VirtualKey.Menu)) mods |= VirtualKeyModifiers.Menu;
        if (IsDown(VirtualKey.Shift)) mods |= VirtualKeyModifiers.Shift;
        if (IsDown(VirtualKey.LeftWindows) || IsDown(VirtualKey.RightWindows))
            mods |= VirtualKeyModifiers.Windows;
        return mods;
    }

    /// <summary>
    /// This used to be Win32 <c>GetKeyState</c>, on the belief that
    /// <c>InputKeyboardSource.GetKeyStateForCurrentThread</c> could not see the modifiers —
    /// it "read Ctrl+Alt+K correctly but reported none for Ctrl+Alt+Space". That was a bad
    /// diagnosis. Ctrl+Alt+Space is registered as another application's global hotkey on the
    /// machine it was tested on, so the OS consumed it: instrumenting this method to log both
    /// APIs for the *same* keystroke showed Ctrl and Alt arriving and **both APIs agreeing**,
    /// with no Space event at all. Across every chord that actually reaches the window the two
    /// never disagreed, so the P/Invoke bought nothing. Don't reintroduce it on the strength of
    /// a chord that some other process owns — check first that the key even arrives.
    /// </summary>
    private static bool IsDown(VirtualKey key) =>
        InputKeyboardSource.GetKeyStateForCurrentThread(key).HasFlag(CoreVirtualKeyStates.Down);

    #endregion

    #region Toggles and pickers

    private void StartupToggle_Toggled(object sender, RoutedEventArgs e)
    {
        if (_loading) return;

        var enabled = StartupToggle.IsOn;
        if (!StartupService.SetEnabled(enabled))
        {
            _loading = true;
            StartupToggle.IsOn = !enabled;
            _loading = false;
            return;
        }

        Settings.StartWithWindows = enabled;
        Persist();
    }

    private void CloseToTrayToggle_Toggled(object sender, RoutedEventArgs e)
    {
        if (_loading) return;
        Settings.CloseToTray = CloseToTrayToggle.IsOn;
        Persist();
    }

    private void HideOnLaunchToggle_Toggled(object sender, RoutedEventArgs e)
    {
        if (_loading) return;
        Settings.HideOnLaunch = HideOnLaunchToggle.IsOn;
        Persist();
    }

    private void ThemeCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_loading) return;
        if (ThemeCombo.SelectedItem is not ComboBoxItem { Tag: AppTheme theme }) return;

        Settings.Theme = theme;
        Persist();
        ((App)Application.Current).ApplyTheme(theme);
    }

    private void LanguageCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_loading) return;
        if (LanguageCombo.SelectedItem is not ComboBoxItem { Tag: string tag }) return;

        Settings.Language = tag;
        Persist();
        RestartInfoBar.IsOpen = true;
    }

    #endregion

    #region Storage

    /// <summary>
    /// Clears the icon cache. Deliberately unconfirmed: nothing is lost that the next paint
    /// does not extract again, so a confirmation would guard against no consequence. The
    /// InfoBar reports the count instead, which is the only way the user can tell it ran —
    /// a cache that was already correct repaints identically.
    /// </summary>
    private void ResetCacheButton_Click(object sender, RoutedEventArgs e)
    {
        ResetCacheButton.IsEnabled = false;
        try
        {
            var cleared = _owner.ResetIconCache();
            ResetCacheInfoBar.Message = string.Format(
                Loc.GetString("Settings_ResetCache_Done"), cleared);
            ResetCacheInfoBar.IsOpen = true;
        }
        finally
        {
            ResetCacheButton.IsEnabled = true;
        }
    }

    #endregion
}
