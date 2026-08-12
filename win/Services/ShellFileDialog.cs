using System;
using System.Runtime.InteropServices;

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
/// </remarks>
internal static class ShellFileDialog
{
    /// <summary>Only filesystem items — no search results, no virtual shell locations.</summary>
    private const uint FosForceFilesystem = 0x00000040;

    /// <summary>Leave the process's current directory alone.</summary>
    private const uint FosNoChangeDir = 0x00000008;

    private const uint FosPathMustExist = 0x00000800;
    private const uint FosFileMustExist = 0x00001000;
    private const uint FosPickFolders = 0x00000020;

    /// <summary>The full filesystem path, which is all any caller here wants.</summary>
    private const uint SigdnFileSysPath = 0x80058000;

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
        => Show(owner, clientId, startFolder, FosFileMustExist, fileTypes);

    /// <summary>
    /// Picks one existing folder. Returns null when the user cancels.
    /// </summary>
    public static string? PickFolder(IntPtr owner, Guid clientId, string? startFolder)
        => Show(owner, clientId, startFolder, FosPickFolders, []);

    private static string? Show(
        IntPtr owner, Guid clientId, string? startFolder, uint extraOptions, (string Label, string Spec)[] fileTypes)
    {
        var dialog = (IFileDialog)new FileOpenDialogClass();

        try
        {
            dialog.SetClientGuid(ref clientId);

            dialog.GetOptions(out var options);
            dialog.SetOptions(options | FosForceFilesystem | FosNoChangeDir | FosPathMustExist | extraOptions);

            if (fileTypes.Length > 0)
            {
                var specs = new FilterSpec[fileTypes.Length];
                for (var i = 0; i < fileTypes.Length; i++)
                    specs[i] = new FilterSpec { Name = fileTypes[i].Label, Spec = fileTypes[i].Spec };

                dialog.SetFileTypes((uint)specs.Length, specs);
            }

            // SetFolder, not SetDefaultFolder: the latter is only a suggestion for the first
            // ever use and yields to the folder remembered under the client GUID afterwards,
            // which is exactly the behaviour being overridden here.
            if (TryCreateShellItem(startFolder, out var folder))
            {
                dialog.SetFolder(folder!);
                Marshal.FinalReleaseComObject(folder!);
            }

            var hr = dialog.Show(owner);
            if (hr == ErrorCancelled) return null;
            Marshal.ThrowExceptionForHR(hr);

            dialog.GetResult(out var item);
            return DisplayName(item);
        }
        finally
        {
            Marshal.FinalReleaseComObject(dialog);
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
            var riid = typeof(IShellItem).GUID;
            SHCreateItemFromParsingName(path, IntPtr.Zero, in riid, out var created);
            item = (IShellItem)created;
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
        var buffer = IntPtr.Zero;

        try
        {
            item.GetDisplayName(SigdnFileSysPath, out buffer);
            return Marshal.PtrToStringUni(buffer);
        }
        finally
        {
            if (buffer != IntPtr.Zero) Marshal.FreeCoTaskMem(buffer);
            Marshal.FinalReleaseComObject(item);
        }
    }

    [DllImport("shell32.dll", CharSet = CharSet.Unicode, PreserveSig = false)]
    private static extern void SHCreateItemFromParsingName(
        [MarshalAs(UnmanagedType.LPWStr)] string path,
        IntPtr bindContext,
        in Guid riid,
        [MarshalAs(UnmanagedType.Interface)] out object item);

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct FilterSpec
    {
        [MarshalAs(UnmanagedType.LPWStr)] public string Name;
        [MarshalAs(UnmanagedType.LPWStr)] public string Spec;
    }

    [ComImport, Guid("DC1C5A9C-E88A-4DDE-A5A1-60F82A20AEF7")]
    private class FileOpenDialogClass
    {
    }

    // The vtable order below is the contract — these are hand-written declarations of shell
    // interfaces, so a reordered, removed or wrongly-signed member silently calls the wrong
    // slot. Members this file never calls are still declared, for that reason alone.
    [ComImport, Guid("42F85136-DB7E-439C-85F1-E4075D135FC8")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IFileDialog
    {
        // IModalWindow. PreserveSig because "the user cancelled" arrives as a failure HRESULT
        // and is not an error.
        [PreserveSig] int Show(IntPtr parent);

        void SetFileTypes(uint fileTypeCount, [MarshalAs(UnmanagedType.LPArray)] FilterSpec[] filterSpecs);
        void SetFileTypeIndex(uint fileType);
        void GetFileTypeIndex(out uint fileType);
        void Advise(IntPtr events, out uint cookie);
        void Unadvise(uint cookie);
        void SetOptions(uint options);
        void GetOptions(out uint options);
        void SetDefaultFolder(IShellItem folder);
        void SetFolder(IShellItem folder);
        void GetFolder(out IShellItem folder);
        void GetCurrentSelection(out IShellItem item);
        void SetFileName([MarshalAs(UnmanagedType.LPWStr)] string name);
        void GetFileName([MarshalAs(UnmanagedType.LPWStr)] out string name);
        void SetTitle([MarshalAs(UnmanagedType.LPWStr)] string title);
        void SetOkButtonLabel([MarshalAs(UnmanagedType.LPWStr)] string text);
        void SetFileNameLabel([MarshalAs(UnmanagedType.LPWStr)] string label);
        void GetResult(out IShellItem item);
        void AddPlace(IShellItem place, int order);
        void SetDefaultExtension([MarshalAs(UnmanagedType.LPWStr)] string extension);
        void Close([MarshalAs(UnmanagedType.Error)] int result);
        void SetClientGuid(ref Guid client);
        void ClearClientData();
        void SetFilter(IntPtr filter);
    }

    [ComImport, Guid("43826D1E-E718-42EE-BC55-A1E261C37BFE")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IShellItem
    {
        void BindToHandler(IntPtr bindContext, in Guid handler, in Guid riid, out IntPtr result);
        void GetParent(out IShellItem parent);
        void GetDisplayName(uint kind, out IntPtr name);
        void GetAttributes(uint mask, out uint attributes);
        void Compare(IShellItem other, uint hint, out int order);
    }
}
