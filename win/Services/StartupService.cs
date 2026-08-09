using System;
using System.Diagnostics;
using Microsoft.Win32;

namespace ace_run.Services;

/// <summary>
/// "Start with Windows", via the per-user Run key.
///
/// <c>Windows.ApplicationModel.StartupTask</c> is the modern API and is not available here:
/// it requires an identity this app does not have (<c>WindowsPackageType=None</c>). HKCU
/// needs no elevation, and the value is removed as cleanly as it is written.
/// </summary>
internal static class StartupService
{
    private const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string ValueName = "AceRun";

    /// <summary>
    /// Started from the Run key the window stays hidden. Launching at sign-in and throwing
    /// the window in the user's face would make the option unusable for the case it exists
    /// for — being resident behind a hotkey.
    /// </summary>
    public const string TrayArgument = "--tray";

    public static bool IsEnabled
    {
        get
        {
            try
            {
                using var key = Registry.CurrentUser.OpenSubKey(RunKey);
                return key?.GetValue(ValueName) is not null;
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Startup read failed: {ex.Message}");
                return false;
            }
        }
    }

    /// <summary>Returns false if the registry refused the write; the caller reverts the toggle.</summary>
    public static bool SetEnabled(bool enabled)
    {
        try
        {
            using var key = Registry.CurrentUser.CreateSubKey(RunKey, writable: true);
            if (key is null) return false;

            if (enabled)
            {
                // Environment.ProcessPath, not Assembly.Location: the latter is an empty
                // string under single-file publishing, which would write a broken entry.
                var exe = Environment.ProcessPath;
                if (string.IsNullOrEmpty(exe)) return false;

                key.SetValue(ValueName, $"\"{exe}\" {TrayArgument}");
            }
            else
            {
                key.DeleteValue(ValueName, throwOnMissingValue: false);
            }

            return true;
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"Startup write failed: {ex.Message}");
            return false;
        }
    }
}
