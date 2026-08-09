using System;
using System.Runtime.InteropServices;
using ace_run.Models;

namespace ace_run.Services;

/// <summary>
/// The single global hotkey that summons (and dismisses) the window.
///
/// <c>RegisterHotKey</c> delivers <c>WM_HOTKEY</c> to a window's message loop, and WinUI 3
/// gives us no hook into that loop. The message is intercepted by subclassing the main
/// window's HWND with <c>SetWindowSubclass</c> rather than by standing up a message-only
/// window: the main HWND is stable for the whole process — closing to the tray runs
/// <c>args.Handled = true</c> + <c>AppWindow.Hide()</c>, so the window is hidden, never
/// destroyed — and a second window would be a second thing to keep alive for no gain.
/// </summary>
internal static class HotkeyService
{
    // Any id works as long as it is unique within the window; 1 is ours because we register
    // exactly one hotkey.
    private const int HotkeyId = 1;
    private const int WmHotkey = 0x0312;

    /// <summary>
    /// Suppresses the auto-repeat storm from holding the chord down. Without it a held
    /// key toggles the window dozens of times a second.
    /// </summary>
    private const uint ModNoRepeat = 0x4000;

    private static IntPtr _hwnd;
    private static bool _registered;

    /// <summary>
    /// Held in a static field on purpose. The delegate is handed to unmanaged code, which
    /// keeps no managed reference to it; as a local it would be collected and every
    /// subsequent message would call into freed memory.
    /// </summary>
    private static SubclassProc? _subclassProc;

    /// <summary>Raised on the UI thread — <c>WM_HOTKEY</c> arrives on the window's own thread.</summary>
    public static event Action? Pressed;

    /// <summary>Installs the message hook. Safe to call more than once.</summary>
    public static void Attach(IntPtr hwnd)
    {
        if (_subclassProc is not null) return;

        _hwnd = hwnd;
        _subclassProc = SubclassCallback;
        SetWindowSubclass(hwnd, _subclassProc, IntPtr.Zero, IntPtr.Zero);
    }

    /// <summary>
    /// Replaces the current registration. Returns false when the chord is already owned by
    /// another process, in which case nothing is registered and the caller is expected to
    /// put the previous binding back.
    /// </summary>
    public static bool Register(HotkeyBinding? binding)
    {
        Unregister();

        if (_hwnd == IntPtr.Zero || binding is null || !binding.IsValid)
            return true; // "no hotkey" is a successful outcome, not a failure

        _registered = RegisterHotKey(
            _hwnd, HotkeyId, binding.ToWin32Modifiers() | ModNoRepeat, (uint)binding.Key);

        return _registered;
    }

    public static void Unregister()
    {
        if (!_registered || _hwnd == IntPtr.Zero) return;
        UnregisterHotKey(_hwnd, HotkeyId);
        _registered = false;
    }

    private static IntPtr SubclassCallback(
        IntPtr hwnd, uint msg, IntPtr wParam, IntPtr lParam, IntPtr id, IntPtr data)
    {
        if (msg == WmHotkey && wParam == HotkeyId)
        {
            Pressed?.Invoke();
            return IntPtr.Zero;
        }

        return DefSubclassProc(hwnd, msg, wParam, lParam);
    }

    private delegate IntPtr SubclassProc(
        IntPtr hWnd, uint uMsg, IntPtr wParam, IntPtr lParam, IntPtr uIdSubclass, IntPtr dwRefData);

    [DllImport("comctl32.dll", SetLastError = true)]
    private static extern bool SetWindowSubclass(
        IntPtr hWnd, SubclassProc pfnSubclass, IntPtr uIdSubclass, IntPtr dwRefData);

    [DllImport("comctl32.dll")]
    private static extern IntPtr DefSubclassProc(IntPtr hWnd, uint uMsg, IntPtr wParam, IntPtr lParam);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool RegisterHotKey(IntPtr hWnd, int id, uint fsModifiers, uint vk);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool UnregisterHotKey(IntPtr hWnd, int id);
}
