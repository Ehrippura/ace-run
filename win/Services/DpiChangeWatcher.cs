using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;

namespace ace_run.Services;

/// <summary>
/// Reports the new scale when a window crosses to a display at another DPI, early enough to
/// change what the window is about to be resized to.
/// </summary>
/// <remarks>
/// <c>XamlRoot.Changed</c> reports the same thing for a great deal less machinery, and it is
/// what this started as — but it reports it too late. Measured with a synthetic drag: a
/// 933×600 DIP window dragged from a 150% display to a 100% one came out 1080×720 DIP, because
/// Windows had already resized it, and clamped that resize against the minimum still set for
/// the display it left. Only the shrinking direction is affected — growing cannot be clamped by
/// a floor that is too small — which is what makes the bug look intermittent. A programmatic
/// <c>SetWindowPos</c> across the boundary does *not* reproduce it: there the dispatcher gets to
/// run the Changed handler first, so the seam only opens during a real drag, where the message
/// arrives inside a modal move loop.
///
/// <c>WM_DPICHANGED</c> has no such gap. It is sent *before* the resize, carrying the new DPI in
/// wParam and the suggested rect in lParam, so a constraint updated in the handler is already in
/// force when <c>DefSubclassProc</c> applies it.
///
/// Subclassing rather than a message-only window, for the reason <see cref="HotkeyService"/>
/// gives. Unlike that one this is per-window — the settings window comes and goes — so the
/// callbacks are keyed by HWND, and the subclass id differs from HotkeyService's because both
/// attach to the same main HWND.
/// </remarks>
internal static class DpiChangeWatcher
{
    private const uint WmDpiChanged = 0x02E0;

    /// <summary>Distinct from <see cref="HotkeyService"/>'s, which subclasses the same HWND.</summary>
    private static readonly IntPtr SubclassId = new(1);

    /// <summary>
    /// Static for the same reason as <see cref="HotkeyService"/>'s: unmanaged code holds no
    /// managed reference, so a delegate that went out of scope would be collected and every
    /// later message would call into freed memory. One instance serves every window — the
    /// callback is chosen by HWND inside it.
    /// </summary>
    private static SubclassProc? _subclassProc;

    private static readonly Dictionary<IntPtr, Action<double>> Handlers = new();

    /// <param name="onScaleChanged">
    /// Runs on the window's own thread, inside the message, before the resize.
    /// </param>
    public static void Attach(IntPtr hwnd, Action<double> onScaleChanged)
    {
        if (hwnd == IntPtr.Zero) return;

        Handlers[hwnd] = onScaleChanged;
        _subclassProc ??= SubclassCallback;
        SetWindowSubclass(hwnd, _subclassProc, SubclassId, IntPtr.Zero);
    }

    /// <summary>
    /// Required of any window that is really destroyed, not tidiness: HWND values are recycled,
    /// and a stale entry would fire a dead window's callback for whatever inherits the handle.
    /// The main window never needs it — closing to the tray hides rather than destroys, so that
    /// HWND is stable for the life of the process.
    /// </summary>
    public static void Detach(IntPtr hwnd)
    {
        if (!Handlers.Remove(hwnd) || _subclassProc is null) return;
        RemoveWindowSubclass(hwnd, _subclassProc, SubclassId);
    }

    private static IntPtr SubclassCallback(
        IntPtr hwnd, uint msg, IntPtr wParam, IntPtr lParam, IntPtr id, IntPtr data)
    {
        // Ahead of DefSubclassProc on purpose — that is where the resize this is racing happens.
        // LOWORD of wParam is the X-axis DPI; the Y is the same on every shipping configuration.
        if (msg == WmDpiChanged && Handlers.TryGetValue(hwnd, out var handler))
            handler((ushort)(wParam.ToInt64() & 0xFFFF) / 96.0);

        return DefSubclassProc(hwnd, msg, wParam, lParam);
    }

    private delegate IntPtr SubclassProc(
        IntPtr hWnd, uint uMsg, IntPtr wParam, IntPtr lParam, IntPtr uIdSubclass, IntPtr dwRefData);

    [DllImport("comctl32.dll", SetLastError = true)]
    private static extern bool SetWindowSubclass(
        IntPtr hWnd, SubclassProc pfnSubclass, IntPtr uIdSubclass, IntPtr dwRefData);

    [DllImport("comctl32.dll", SetLastError = true)]
    private static extern bool RemoveWindowSubclass(
        IntPtr hWnd, SubclassProc pfnSubclass, IntPtr uIdSubclass);

    [DllImport("comctl32.dll")]
    private static extern IntPtr DefSubclassProc(IntPtr hWnd, uint uMsg, IntPtr wParam, IntPtr lParam);
}
