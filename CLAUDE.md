# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## Project Overview

Ace Run is a lightweight Windows launcher built with WinUI 3 and C#. Users manage a hierarchical list of launch targets — .exe files (with custom parameters) and URLs / custom protocols — with optional folder grouping, across multiple workspaces. The spec is in `doc/spec.md` (Traditional Chinese).

## Tech Stack

- **Framework:** WinUI 3 (Windows App SDK 1.8)
- **Language:** C# / .NET 10.0 (net10.0-windows10.0.22000.0)
- **Minimum OS:** Windows 10 1809 (build 17763)
- **System Tray:** H.NotifyIcon.WinUI 2.1.3
- **Target Platforms:** x86, x64, ARM64

## Build & Run

```bash
# Build
dotnet build win/ace-run.csproj

# Run (unpackaged mode)
dotnet run --project win/ace-run.csproj

# Publish (framework-dependent, x64)
dotnet publish win/ace-run.csproj -p:PublishProfile=win/Properties/PublishProfiles/FolderProfile.pubxml
```

The solution file is `win/ace-run.slnx` (can also be opened in Visual Studio).

## Data Storage

All data lives under `%LOCALAPPDATA%\AceRun\`:

| File/Dir | Purpose |
|---|---|
| `config.json` | `WorkspaceConfig` — workspace list, active/default workspace ID, window state |
| `workspaces/<guid>.json` | Per-workspace `AppData` — ungrouped items, folders, recent launches |
| `icons/<guid>.png` | Cached app icons (keyed by `AppItem.Id`) |
| `apps.json.bak` | Migration backup from pre-workspace format |

On first launch, `DataService.MigrateOrInitialize()` either reads `config.json` or migrates the legacy `apps.json` into the new format.

## Architecture

### Layers

```
Models/                  # Plain data classes
  AppData                # v5: Tags + UngroupedItems + Folders + RecentLaunches
  AppItem / FolderItem   # Leaf and group nodes
  ItemKind               # App | Url — what AppItem.FilePath points at
  WorkspaceConfig        # Top-level config (workspaces list + window state)
  WorkspaceInfo          # Workspace metadata (id, name, color tag, app count)
  WorkspaceExport        # Import/export container
Services/                # Static service classes
  DataService            # JSON persistence, workspace CRUD, migration, shared JsonOptions
  IconService            # Icon cache (disk) + extraction via StorageFile thumbnail
  UrlUtil                # URL normalization / display name / .url shortcut parsing
  Loc                    # Localization (ResourceLoader with embedded .resw fallback)
ViewModels.cs            # AppItemViewModel, FolderViewModel, WorkspaceViewModel (all INotifyPropertyChanged)
MainWindow.xaml/.cs      # Primary UI + all orchestration (split across .Actions/.Data/.Events/.Workspace partials)
EditItemDialog.xaml/.cs  # ContentDialog for add/edit app or URL (folders use ad-hoc dialogs in MainWindow.Actions.cs)
ManageWorkspacesDialog.xaml/.cs  # ContentDialog for workspace CRUD
App.xaml.cs              # App lifecycle, single-instance, tray icon
Program.cs               # Entry point — single-instance via AppInstance.FindOrRegisterForKey
```

### Key Design Patterns

**Workspace data flow:** `InitializeWorkspacesAsync()` calls `DataService.MigrateOrInitialize()`, selects the active workspace, then calls `LoadWorkspaceDataAsync()`. Switching workspaces calls `CommitSave()` (saves current) then `LoadWorkspaceDataAsync()` (loads new). `_suppressWorkspaceSwitch` prevents the `ComboBox.SelectionChanged` handler from firing during programmatic selection.

**UI data model:** `_folders` (`ObservableCollection<FolderViewModel>`) drives `SidebarListView`. `_ungroupedApps` and the selected folder's `FolderViewModel.Apps` drive the main `AppListView`. `_searchResults` is shown instead when search is active (saves blocked during search).

**Save flow (`CommitSave`):** Rebuilds `AppData` from `_ungroupedApps` + `_folders`, updates `WorkspaceInfo.AppCount` (denormalized), calls `DataService.SaveWorkspace` + `DataService.SaveConfig`, then `App.UpdateTrayContextMenu()`.

**Item kinds:** `AppItem.Kind` (`ItemKind.App` / `ItemKind.Url`) decides whether `FilePath` is an exe path or a URL. It is serialized as a string via `JsonStringEnumConverter` in `DataService.JsonOptions` (shared with the `.acerun` import/export in `ManageWorkspacesDialog`), and is absent from pre-v5 files — which correctly falls back to `App`, so no migration exists. `AppItemViewModel.Kind` is **read-only**: an item's kind is fixed at construction and never switches. Anything kind-specific branches on `AppItemViewModel.IsUrl`: `LaunchApp` (URLs get only `FileName` + `UseShellExecute` — no arguments, working directory, or `runas`), the `EditItemDialog` field layout, and the right-click menu ("Copy Link" instead of "Open File Location"). URL parsing lives in `UrlUtil`, which accepts any absolute URI except `file:`.

**Icon loading:** `IconService.GetIconAsync()` checks disk cache first. On miss, uses `StorageFile.GetThumbnailAsync()` to extract the icon and writes it to disk. Cache is invalidated (file deleted) when `AppItemViewModel.FilePath` or `CustomIconPath` is set to a new value. A non-file path (i.e. a URL without a custom icon) returns `null` and the templates show a Segoe MDL2 fallback glyph instead — `FallbackGlyph` / `IconVisibility` / `FallbackIconVisibility` on the view model.

**Localization:** `Loc.GetString(key)` tries `ResourceLoader` first (MSIX), then falls back to embedded `.resw` files parsed via `XDocument`. Language is auto-detected from `CultureInfo.CurrentUICulture` (en-US, zh-TW, or ja-JP; anything starting with `zh`→Chinese, `ja`→Japanese, else English). String files: `win/Strings/en-US/Resources.resw`, `win/Strings/zh-TW/Resources.resw`, and `win/Strings/ja-JP/Resources.resw` — all must be updated when adding new strings. Each new `.resw` must also be added as an `EmbeddedResource` (with matching `LogicalName`) in the `.csproj`.

**Single instance:** `Program.cs` uses `AppInstance.FindOrRegisterForKey("AceRun-Main")`. If a second instance starts, it redirects activation to the first and exits. The first instance calls `App.BringToForeground()` via P/Invoke on receiving the redirect.

**System tray:** Initialized in `App.xaml.cs` via H.NotifyIcon. Closing the window hides it (`args.Handled = true`) when `App.TrayEnabled` is true. Exiting via tray calls `Environment.Exit(0)` after disposing the icon. `UpdateTrayContextMenu()` is public — called from `MainWindow` after saves to refresh recent launches.

### Notable Capabilities

- `runFullTrust` — required for launching external processes
- `ExtendsContentIntoTitleBar = true` — seamless Mica backdrop into title bar
- `.lnk` shortcut resolution on drag-and-drop via `WScript.Shell` COM; `.url` Internet Shortcuts parsed by `UrlUtil.ReadInternetShortcut`
- Drag-and-drop accepts `StorageItems`, `WebLink`, and `Text` (in that priority order, so a browser offering several formats adds the link once)
- Admin launch via `ProcessStartInfo.Verb = "runas"` (App items only)
- URL / custom-protocol launch via `UseShellExecute` — `steam://`, `mailto:`, `ms-settings:` all work
- App icon embedded as `EmbeddedResource` (`ace_run.Assets.app-icon.ico`) for tray icon use at runtime
