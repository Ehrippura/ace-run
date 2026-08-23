using System;
using System.Diagnostics;
using System.Threading.Tasks;
using Microsoft.UI;
using Microsoft.Windows.Storage.Pickers;

namespace ace_run.Services;

/// <summary>
/// The shell's Open / Choose-folder dialogs, via the Windows App SDK pickers.
/// </summary>
/// <remarks>
/// <para>
/// This replaces a hand-written <c>IFileDialog</c> wrapper that existed for exactly one
/// property: <b>the folder to open at</b>. Through Windows App SDK 1.8 neither projection had
/// it — <c>Windows.Storage.Pickers</c> and <c>Microsoft.Windows.Storage.Pickers</c> both gave
/// the open and folder pickers only <c>SuggestedStartLocation</c>, a closed enum of known
/// folders, and <c>SuggestedFolder</c> existed on <c>FileSavePicker</c> alone. Windows App SDK
/// 2.x adds it to all three, which is what makes "start where the value already points"
/// expressible without COM interop.
/// </para>
/// <para>
/// <b><c>SuggestedFolder</c>, not <c>SuggestedStartFolder</c>.</b> 2.x added both and the names
/// do not say which is which: <c>SuggestedFolder</c> is <c>IFileDialog::SetFolder</c> and always
/// wins, while <c>SuggestedStartFolder</c> is <c>SetDefaultFolder</c> — a suggestion for the
/// first ever use that yields to the folder remembered under
/// <see cref="FileOpenPicker.SettingsIdentifier"/> on every later open. Overriding that memory
/// is the whole point here, so the wrong one of the pair degrades into "wherever you were last
/// time" from the second press onwards. Measured both ways against the same item: with
/// <c>SuggestedStartFolder</c> the dialog opened at <c>C:\Windows</c> (the previously accepted
/// folder) while the item's path pointed elsewhere; with <c>SuggestedFolder</c> it opened at the
/// item's own folder. A test that only ever runs against a fresh settings identifier passes
/// either way.
/// </para>
/// <para>
/// These run asynchronously; the interop they replace pumped its own modal loop. That is not a
/// detail callers can ignore — the button that opened the dialog stays live, so every call site
/// needs its own guard against opening a second one. See <c>EditItemDialog._pickerOpen</c>.
/// </para>
/// </remarks>
internal static class FilePicker
{
    /// <summary>
    /// Picks one existing file. Returns null when the user cancels or the dialog fails.
    /// </summary>
    /// <param name="settingsId">
    /// Identifies this call site to the shell, which keeps its last-used folder under that key.
    /// It is what the dialog falls back to whenever <paramref name="startFolder"/> is null, so
    /// call sites must not share one.
    /// </param>
    /// <param name="extensions">Filter entries, each a leading-dot extension such as ".exe".</param>
    public static async Task<string?> PickFileAsync(
        IntPtr owner, string settingsId, string? startFolder, params string[] extensions)
    {
        try
        {
            var picker = new FileOpenPicker(Win32Interop.GetWindowIdFromWindow(owner))
            {
                SettingsIdentifier = settingsId,
            };

            foreach (var extension in extensions)
                picker.FileTypeFilter.Add(extension);

            if (startFolder is not null)
                picker.SuggestedFolder = startFolder;

            var result = await picker.PickSingleFileAsync();
            return result?.Path;
        }
        catch (Exception ex)
        {
            // Same swallow the interop did for a start folder that no longer resolves, widened
            // to the whole call: these run from async void handlers, where letting anything out
            // takes the process down. Cancel is not an exception here — it returns null.
            Debug.WriteLine($"File picker failed: {ex.Message}");
            return null;
        }
    }

    /// <summary>
    /// Picks one existing folder. Returns null when the user cancels or the dialog fails.
    /// </summary>
    public static async Task<string?> PickFolderAsync(IntPtr owner, string settingsId, string? startFolder)
    {
        try
        {
            var picker = new FolderPicker(Win32Interop.GetWindowIdFromWindow(owner))
            {
                SettingsIdentifier = settingsId,
            };

            if (startFolder is not null)
                picker.SuggestedFolder = startFolder;

            var result = await picker.PickSingleFolderAsync();
            return result?.Path;
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"Folder picker failed: {ex.Message}");
            return null;
        }
    }
}
