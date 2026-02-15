using System.Collections.Generic;

namespace ace_run.Services;

internal static class Loc
{
    private static Microsoft.Windows.ApplicationModel.Resources.ResourceLoader? _loader;

    private static readonly Dictionary<string, string> Fallbacks = new()
    {
        ["AddItemTitle"] = "Add Item",
        ["DeleteItemTitle"] = "Delete Item",
        ["DeleteItemContent"] = "Are you sure you want to delete \"{0}\"?",
        ["DeleteButton"] = "Delete",
        ["CancelButton"] = "Cancel",
        ["SaveButton"] = "Save",
        ["DragDropCaption"] = "Add to Ace Run",
        ["EditMenuItem.Text"] = "Edit",
        ["DeleteMenuItem.Text"] = "Delete",
        ["NewFolderTitle"] = "New Folder",
        ["FolderNamePlaceholder"] = "Folder name",
        ["DefaultFolderName"] = "New Folder",
        ["RenameFolder"] = "Rename",
        ["DeleteFolder"] = "Delete Folder",
        ["DeleteFolderContent"] = "Are you sure you want to delete folder \"{0}\" and all its contents?",
        ["TrayShow"] = "Show",
        ["TrayExit"] = "Exit",
    };

    static Loc()
    {
        try
        {
            _loader = new Microsoft.Windows.ApplicationModel.Resources.ResourceLoader();
        }
        catch
        {
            _loader = null;
        }
    }

    public static string GetString(string key)
    {
        try
        {
            var value = _loader?.GetString(key);
            if (!string.IsNullOrEmpty(value))
                return value;
        }
        catch { }

        return Fallbacks.TryGetValue(key, out var fallback) ? fallback : key;
    }
}
