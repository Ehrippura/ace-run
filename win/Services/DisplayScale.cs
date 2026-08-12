using System;
using System.Runtime.InteropServices;
using Microsoft.UI;
using Microsoft.UI.Windowing;

namespace ace_run.Services;

/// <summary>
/// The DIP-to-physical-pixel factor for a window.
/// </summary>
/// <remarks>
/// Needed in both windows' constructors, where <c>XamlRoot.RasterizationScale</c> is not
/// available yet — the XamlRoot does not exist until the content is loaded, and the window has
/// to be sized before it is shown or the user sees it resize itself.
/// </remarks>
internal static class DisplayScale
{
    [DllImport("user32.dll")]
    private static extern uint GetDpiForWindow(IntPtr hWnd);

    /// <summary>96 DPI is the definition of 1.0 — one DIP to one pixel.</summary>
    public static double ForWindow(AppWindow window)
        => GetDpiForWindow(Win32Interop.GetWindowFromWindowId(window.Id)) / 96.0;
}
