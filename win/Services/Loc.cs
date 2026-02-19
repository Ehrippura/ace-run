using System;
using System.Collections.Generic;
using System.Globalization;

namespace ace_run.Services;

internal static class Loc
{
    private static readonly Microsoft.Windows.ApplicationModel.Resources.ResourceLoader? _loader;
    private static readonly Dictionary<string, string> _fallbacks;

    static Loc()
    {
        try
        {
            _loader = new Microsoft.Windows.ApplicationModel.Resources.ResourceLoader();
        }
        catch
        {
            // ignore
        }

        var isZh = CultureInfo.CurrentUICulture.Name.StartsWith("zh", StringComparison.OrdinalIgnoreCase);
        _fallbacks = isZh ? ZhTwStrings : EnUsStrings;
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

        return _fallbacks.TryGetValue(key, out var fallback) ? fallback : key;
    }

    private static readonly Dictionary<string, string> EnUsStrings = new()
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

    private static readonly Dictionary<string, string> ZhTwStrings = new()
    {
        ["AddItemTitle"] = "新增項目",
        ["DeleteItemTitle"] = "刪除項目",
        ["DeleteItemContent"] = "確定要刪除「{0}」嗎？",
        ["DeleteButton"] = "刪除",
        ["CancelButton"] = "取消",
        ["SaveButton"] = "儲存",
        ["DragDropCaption"] = "新增至 Ace Run",
        ["EditMenuItem.Text"] = "編輯",
        ["DeleteMenuItem.Text"] = "刪除",
        ["NewFolderTitle"] = "新增資料夾",
        ["FolderNamePlaceholder"] = "資料夾名稱",
        ["DefaultFolderName"] = "新增資料夾",
        ["RenameFolder"] = "重新命名",
        ["DeleteFolder"] = "刪除資料夾",
        ["DeleteFolderContent"] = "確定要刪除資料夾「{0}」及其所有內容嗎？",
        ["TrayShow"] = "顯示",
        ["TrayExit"] = "結束",
    };
}
