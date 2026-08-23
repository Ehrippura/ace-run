using System;
using System.Runtime.InteropServices;
using Windows.Win32;
using Windows.Win32.Foundation;
using Windows.Win32.UI.Shell;
using Windows.Win32.UI.Shell.Common;

namespace ace_run.Services;

/// <summary>
/// The shell's own Open / Choose-folder dialog, driven through <c>IFileDialog</c> directly.
/// </summary>
/// <remarks>
/// <para>
/// This exists for one property neither picker projection has: <b>the folder to open at</b>.
/// <c>Windows.Storage.Pickers.FileOpenPicker</c> and <c>FolderPicker</c> expose only
/// <c>SuggestedStartLocation</c>, a closed enum of known folders, and the Windows App SDK's
/// <c>Microsoft.Windows.Storage.Pickers</c> replacements add <c>SuggestedFolder</c> to
/// <c>FileSavePicker</c> <i>alone</i> — the open and folder pickers there have the same closed
/// enum and nothing else. Both projections are <c>IFileDialog</c> underneath, so going straight
/// to it is what makes "start where the value already points" possible at all.
/// </para>
/// <para>
/// <see cref="Show"/> runs on the calling (UI) thread and pumps its own modal loop, which is
/// what the picker it replaces did too — the dialog disables the owner window and the caller
/// resumes when it closes.
/// </para>
/// <para>
/// The <c>IFileDialog</c>/<c>IShellItem</c> bindings themselves — vtable order, GUIDs, struct
/// layout — come from <c>Microsoft.Windows.CsWin32</c> (see <c>NativeMethods.txt</c>) rather
/// than being hand-written here, so a mismatched signature is a generator bug against
/// Microsoft's own metadata, not a typo this file could silently make.
/// </para>
/// </remarks>
internal static class ShellFileDialog
{
    /// <summary><c>HRESULT_FROM_WIN32(ERROR_CANCELLED)</c> — the user backed out.</summary>
    private const int ErrorCancelled = unchecked((int)0x800704C7);

    /// <summary>
    /// Picks one existing file. Returns null when the user cancels.
    /// </summary>
    /// <param name="clientId">
    /// Identifies this call site to the shell, which keeps its last-used folder under that key.
    /// The replacement for the old <c>SettingsIdentifier</c>, and the fallback whenever
    /// <paramref name="startFolder"/> is null.
    /// </param>
    /// <param name="fileTypes">Filter entries as (label, semicolon-separated spec) pairs.</param>
    public static string? PickFile(
        IntPtr owner, Guid clientId, string? startFolder, params (string Label, string Spec)[] fileTypes)
        => Show(owner, clientId, startFolder, FILEOPENDIALOGOPTIONS.FOS_FILEMUSTEXIST, fileTypes);

    /// <summary>
    /// Picks one existing folder. Returns null when the user cancels.
    /// </summary>
    public static string? PickFolder(IntPtr owner, Guid clientId, string? startFolder)
        => Show(owner, clientId, startFolder, FILEOPENDIALOGOPTIONS.FOS_PICKFOLDERS, []);

    private static string? Show(
        IntPtr owner, Guid clientId, string? startFolder, FILEOPENDIALOGOPTIONS extraOptions,
        (string Label, string Spec)[] fileTypes)
    {
        var dialog = (IFileDialog)new FileOpenDialog();

        try
        {
            dialog.SetClientGuid(clientId);

            dialog.GetOptions(out var options);
            dialog.SetOptions(options
                | FILEOPENDIALOGOPTIONS.FOS_FORCEFILESYSTEM
                | FILEOPENDIALOGOPTIONS.FOS_NOCHANGEDIR
                | FILEOPENDIALOGOPTIONS.FOS_PATHMUSTEXIST
                | extraOptions);

            if (fileTypes.Length > 0)
                SetFileTypes(dialog, fileTypes);

            // SetFolder, not SetDefaultFolder: the latter is only a suggestion for the first
            // ever use and yields to the folder remembered under the client GUID afterwards,
            // which is exactly the behaviour being overridden here.
            if (TryCreateShellItem(startFolder, out var folder))
            {
                dialog.SetFolder(folder!);
                Marshal.FinalReleaseComObject(folder!);
            }

            try
            {
                dialog.Show((HWND)owner);
            }
            catch (COMException ex) when (ex.HResult == ErrorCancelled)
            {
                return null;
            }

            dialog.GetResult(out var item);
            return DisplayName(item);
        }
        finally
        {
            Marshal.FinalReleaseComObject(dialog);
        }
    }

    /// <summary>
    /// <c>COMDLG_FILTERSPEC</c> holds raw <c>PCWSTR</c>s, not marshalled strings — the
    /// generated struct has no <c>[MarshalAs(LPWStr)]</c> to lean on the way the old
    /// hand-written one did. Each label/spec is pinned instead of copied, because a plain
    /// <c>fixed</c> block's pointers die at the end of that block and this needs them alive
    /// through the <see cref="IFileDialog.SetFileTypes"/> call that follows.
    /// </summary>
    private static void SetFileTypes(IFileDialog dialog, (string Label, string Spec)[] fileTypes)
    {
        var specs = new COMDLG_FILTERSPEC[fileTypes.Length];
        var handles = new GCHandle[fileTypes.Length * 2];

        try
        {
            for (var i = 0; i < fileTypes.Length; i++)
            {
                var nameHandle = GCHandle.Alloc(fileTypes[i].Label, GCHandleType.Pinned);
                var specHandle = GCHandle.Alloc(fileTypes[i].Spec, GCHandleType.Pinned);
                handles[i * 2] = nameHandle;
                handles[i * 2 + 1] = specHandle;

                unsafe
                {
                    specs[i] = new COMDLG_FILTERSPEC
                    {
                        pszName = (char*)nameHandle.AddrOfPinnedObject(),
                        pszSpec = (char*)specHandle.AddrOfPinnedObject(),
                    };
                }
            }

            dialog.SetFileTypes(specs);
        }
        finally
        {
            foreach (var handle in handles)
                if (handle.IsAllocated) handle.Free();
        }
    }

    /// <summary>
    /// Wraps a path as an <c>IShellItem</c>. A path that has gone missing between the check and
    /// here is not worth failing a click over — the dialog just opens wherever it would have.
    /// </summary>
    private static bool TryCreateShellItem(string? path, out IShellItem? item)
    {
        item = null;
        if (string.IsNullOrEmpty(path)) return false;

        try
        {
            PInvoke.SHCreateItemFromParsingName(path, null, out IShellItem created).ThrowOnFailure();
            item = created;
            return true;
        }
        catch (COMException)
        {
            return false;
        }
        catch (ArgumentException)
        {
            return false;
        }
    }

    private static string? DisplayName(IShellItem item)
    {
        try
        {
            item.GetDisplayName(SIGDN.SIGDN_FILESYSPATH, out var name);
            try
            {
                return name.ToString();
            }
            finally
            {
                unsafe { if (name.Value is not null) Marshal.FreeCoTaskMem((IntPtr)name.Value); }
            }
        }
        finally
        {
            Marshal.FinalReleaseComObject(item);
        }
    }
}
