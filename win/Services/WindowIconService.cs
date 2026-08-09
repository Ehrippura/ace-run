using System;
using System.Diagnostics;
using System.IO;
using Microsoft.UI.Xaml;

namespace ace_run.Services;

/// <summary>
/// Gives a window its Alt+Tab / taskbar / task-manager icon.
///
/// This has to be done by hand, and that is not a WinUI quirk: a window's icon has always
/// come from its class's <c>hIcon</c> or from <c>WM_SETICON</c>, never from the .exe's
/// resources. <c>&lt;ApplicationIcon&gt;</c> stamps the icon into the .exe so Explorer can
/// show it for the *file*; WinUI 3 then registers its window class with no <c>hIcon</c> at
/// all, so with nothing set the switcher falls back to the generic application icon. WPF and
/// WinForms hide this by setting it for you — WinUI 3 never added that convenience, which is
/// what WindowsAppSDK issue #4028 asked for and did not get.
///
/// Packaged apps get it free: the shell reads the package manifest. This app is
/// <c>WindowsPackageType=None</c>, so that path does not exist here.
///
/// <c>SetIcon</c> takes a *path to an .ico*. It also accepts an .exe path without complaint
/// and then silently applies the default Windows icon and leaves ICON_SMALL unset, so do not
/// "simplify" this to <c>Environment.ProcessPath</c> — it looks like it works. The loose .ico
/// beside the .exe is therefore load-bearing; it is a plain <c>Content</c> item so it lands in
/// both build and publish output. (The same file is *also* an EmbeddedResource, which is what
/// the tray icon reads — the tray needs a stream, this needs a path.)
/// </summary>
internal static class WindowIconService
{
    public static void Apply(Window window)
    {
        try
        {
            // Absolute, not "Assets\app-icon.ico": launching from the Run key at sign-in
            // gives the process an arbitrary working directory.
            var ico = Path.Combine(AppContext.BaseDirectory, "Assets", "app-icon.ico");
            if (File.Exists(ico))
                window.AppWindow.SetIcon(ico);
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"Window icon failed: {ex.Message}");
        }
    }
}
