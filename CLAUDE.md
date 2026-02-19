# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## Project Overview

Ace Run is a lightweight Windows application launcher built with WinUI 3 and C#. Users manage a hierarchical list of .exe files (with optional folder grouping) and launch them with custom parameters. The spec is in `doc/spec.md` (Traditional Chinese) — all four phases are now complete.

## Tech Stack

- **Framework:** WinUI 3 (Windows App SDK 1.8)
- **Language:** C# / .NET 8.0 (net8.0-windows10.0.19041.0)
- **Data Storage:** JSON at `%LOCALAPPDATA%\AceRun\apps.json`
- **Icon Cache:** PNG files at `%LOCALAPPDATA%\AceRun\icons\<guid>.png`
- **System Tray:** H.NotifyIcon.WinUI 2.1.3
- **Target Platforms:** x86, x64, ARM64
- **Minimum OS:** Windows 10 1809 (build 17763)

## Build & Run

```bash
# Build
dotnet build win/ace-run.csproj

# Run (unpackaged mode)
dotnet run --project win/ace-run.csproj

# Publish (self-contained, example for x64)
dotnet publish win/ace-run.csproj -p:PublishProfile=win/Properties/PublishProfiles/win-x64.pubxml
```

The solution file is `win/ace-run.slnx` (can also be opened in Visual Studio).

## Architecture

### Layers

```
Models/          # Plain data classes (TreeItem, AppItem, FolderItem, AppData, ...)
Services/        # Static service classes (DataService, IconService, Loc)
ViewModels.cs    # TreeItemViewModel (abstract), AppItemViewModel, FolderViewModel
MainWindow        # UI + orchestration (all in MainWindow.xaml / .xaml.cs)
EditItemDialog    # ContentDialog for add/edit operations
```

### Key Design Patterns

**Dual-list synchronization:** `_rootItems` (a `List<TreeItemViewModel>`) mirrors `AppTreeView.RootNodes` (WinUI `TreeViewNode` tree). Both must be kept in sync. After drag-and-drop reordering, `RebuildRootItemsFromNodes()` rebuilds `_rootItems` from the node tree. After saves, the node tree drives serialization.

**TreeView item templates:** `TreeItemTemplateSelector` selects the XAML `DataTemplate` (app vs. folder) based on the type of `TreeViewNode.Content`.

**Search vs. tree view:** When the search box is non-empty, `AppTreeView` is hidden and `SearchResultsView` (a flat `ListView`) is shown. Saving is blocked while search is active to avoid clobbering the list.

**Icon loading:** `IconService.GetIconAsync()` checks the disk cache first (`%LOCALAPPDATA%\AceRun\icons\<guid>.png`). On cache miss, it uses `StorageFile.GetThumbnailAsync()` to extract the icon and writes it to disk. The cache is invalidated (file deleted) when `FilePath` or `CustomIconPath` changes.

**Localization:** `Loc.GetString(key)` tries `ResourceLoader` first (MSIX package), then falls back to embedded `.resw` files parsed via XDocument. Language is auto-detected from `CultureInfo.CurrentUICulture` (en-US or zh-TW). String files are embedded resources: `win/Strings/en-US/Resources.resw` and `win/Strings/zh-TW/Resources.resw`.

**Data model:** `AppData` (v2) contains a `List<TreeItem>` (polymorphic — either `AppItem` or `FolderItem` which has nested `Children`), plus `RecentLaunches` and `WindowState`. `DataService` handles backward compat with the old flat `List<AppItem>` format.

**System tray:** Initialized in `App.xaml.cs` via H.NotifyIcon. Closing the window hides it (`args.Handled = true`) when `App.TrayEnabled` is true. Exiting via tray menu calls `Environment.Exit(0)` after disposing the tray icon.

### Notable Capabilities

- `runFullTrust` — required for launching external processes
- `ExtendsContentIntoTitleBar = true` — seamless Mica backdrop into title bar
- `.lnk` shortcut resolution on drag-and-drop via `WScript.Shell` COM
- Admin launch via `ProcessStartInfo.Verb = "runas"`
- App icon embedded as `EmbeddedResource` for tray icon use at runtime
