using ace_run.Models;
using ace_run.Services;
using Microsoft.UI.Xaml;
using WinRT.Interop;

namespace ace_run;

/// <summary>
/// The bridge between <see cref="SettingsWindow"/> and everything the settings actually
/// change. MainWindow owns the one live <see cref="WorkspaceConfig"/> — it rebuilds and
/// writes the whole thing on close — so the settings window edits that object through here
/// instead of loading a copy that would be silently overwritten.
/// </summary>
public sealed partial class MainWindow
{
    public WorkspaceConfig Config => _workspaceConfig;
    private AppSettings Settings => _workspaceConfig.Settings;

    /// <summary>
    /// Installs the hotkey message hook. Runs from the constructor rather than after the
    /// config load: the HWND exists from the moment the Window does, and attaching later
    /// would race the first <see cref="ApplySettings"/>.
    /// </summary>
    private void InitializeSettings()
    {
        HotkeyService.Attach(WindowNative.GetWindowHandle(this));
        HotkeyService.Pressed += () => ((App)Application.Current).ToggleWindow();
    }

    /// <summary>
    /// Puts the loaded settings into effect. Called once the config is available, and again
    /// whenever the settings window changes something that is not applied at its own site.
    /// </summary>
    private void ApplySettings()
    {
        ((App)Application.Current).ApplyTheme(Settings.Theme);

        // ApplyTheme cannot reach this window on the first pass: it runs from
        // InitializeWorkspacesAsync before the first await, which is still inside our own
        // constructor, so App has not been handed the window reference yet. Applying it
        // here as well is idempotent and is what makes the saved theme visible at startup
        // rather than only after the user next opens the settings.
        ThemeService.Apply(Content as FrameworkElement, Settings.Theme);

        // A chord that was free when it was saved may be taken by something else today.
        // Nothing to report at this point — there is no settings window open to report it
        // in — so the binding is kept and the user sees it not working until they revisit
        // the settings, where re-recording it will surface the conflict.
        HotkeyService.Register(Settings.Hotkey);
    }

    /// <summary>
    /// Registers <paramref name="binding"/> and, if Windows accepted it, makes it the saved
    /// one. Returns false when the chord belongs to another process; the settings are left
    /// untouched in that case so the caller can restore what was there before.
    /// </summary>
    public bool TryApplyHotkey(HotkeyBinding? binding)
    {
        if (!HotkeyService.Register(binding))
            return false;

        Settings.Hotkey = binding;
        return true;
    }

    public void PersistSettings() => DataService.SaveConfig(_workspaceConfig);

    private void SettingsMenuItem_Click(object sender, RoutedEventArgs e) =>
        ((App)Application.Current).ShowSettings(this);

    /// <summary>
    /// The tail both launch paths share. Hiding belongs here rather than in
    /// <c>LaunchCore</c> so a batch launch hides once, after the last item.
    /// </summary>
    private void AfterLaunch()
    {
        PersistAfterEdit();
        ((App)Application.Current).UpdateTrayContextMenu();

        if (Settings.HideOnLaunch)
            AppWindow.Hide();
    }
}
