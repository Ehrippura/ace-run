# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## Working Rules

- After finishing edits, do not automatically commit changes unless the user explicitly asks for a commit.

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
  TagItem                # User-defined tag (id, name, color key)
  ItemKind               # App | Url — what AppItem.FilePath points at
  WorkspaceConfig        # Top-level config (workspaces list + window state)
  WorkspaceInfo          # Workspace metadata (id, name, color tag, app count)
  WorkspaceExport        # Import/export container
Services/                # Static service classes
  DataService            # JSON persistence, workspace CRUD, migration, shared JsonOptions
  IconService            # Icon cache (disk) + extraction via StorageFile thumbnail
  UrlUtil                # URL normalization / display name / .url shortcut parsing
  Loc                    # Localization (ResourceLoader with embedded .resw fallback)
  ColorTags              # Color-key list + resolution to the shared theme brushes
Styles/                  # Design layer, merged in App.xaml
  Tokens.xaml            # Spacing scale, corner radii, type ramp (no colors)
  Brushes.xaml           # ThemeDictionaries: Light / Dark / HighContrast
ViewModels.cs            # AppItemViewModel, FolderViewModel, WorkspaceViewModel, TagViewModel (all INotifyPropertyChanged)
MainWindow.xaml/.cs      # Primary UI + all orchestration (split across .Actions/.Data/.Events/.Motion/.Workspace partials)
EditItemDialog.xaml/.cs  # ContentDialog for add/edit app or URL (folders use ad-hoc dialogs in MainWindow.Actions.cs)
ManageWorkspacesDialog.xaml/.cs  # ContentDialog for workspace CRUD
ManageTagsDialog.xaml/.cs        # ContentDialog for tag CRUD
App.xaml.cs              # App lifecycle, single-instance, tray icon
Program.cs               # Entry point — single-instance via AppInstance.FindOrRegisterForKey
```

### Key Design Patterns

**Workspace data flow:** `InitializeWorkspacesAsync()` calls `DataService.MigrateOrInitialize()`, selects the active workspace, then calls `LoadWorkspaceDataAsync()`. Switching workspaces calls `CommitSave()` (saves current) then `LoadWorkspaceDataAsync()` (loads new). `_suppressWorkspaceSwitch` prevents the `ComboBox.SelectionChanged` handler from firing during programmatic selection.

**UI data model:** `_folders` (`ObservableCollection<FolderViewModel>`) drives `SidebarListView`. `_ungroupedApps` and the selected folder's `FolderViewModel.Apps` drive the main `AppGridView` (tile cards). `_searchResults` drives `SearchResultsView` (full-width rows), shown instead when search is active (saves blocked during search).

**"Ungrouped" is not a folder.** It is a `ListView.Header` inside `SidebarListView`, not a member of `_folders`, and "ungrouped is selected" is represented by `_selectedFolder == null` — which the code keeps in sync by hand. Do not "clean this up" by making it a real `FolderViewModel`: `CommitSave` does `_appData.Folders = _folders.Select(f => f.ToModel())` and `SidebarListView_DragItemsCompleted` calls `CommitSave()` directly, so a phantom folder with a fresh Guid would be persisted on the first drag-reorder. Roughly 28 call sites assume `_folders` holds only real folders.

**Save flow (`CommitSave`):** Rebuilds `AppData` from `_ungroupedApps` + `_folders`, updates `WorkspaceInfo.AppCount` (denormalized), calls `DataService.SaveWorkspace` + `DataService.SaveConfig`, then `App.UpdateTrayContextMenu()`.

**Item kinds:** `AppItem.Kind` (`ItemKind.App` / `ItemKind.Url`) decides whether `FilePath` is an exe path or a URL. It is serialized as a string via `JsonStringEnumConverter` in `DataService.JsonOptions` (shared with the `.acerun` import/export in `ManageWorkspacesDialog`), and is absent from pre-v5 files — which correctly falls back to `App`, so no migration exists. `AppItemViewModel.Kind` is **read-only**: an item's kind is fixed at construction and never switches. Anything kind-specific branches on `AppItemViewModel.IsUrl`: `LaunchApp` (URLs get only `FileName` + `UseShellExecute` — no arguments, working directory, or `runas`), the `EditItemDialog` field layout, and the right-click menu ("Copy Link" instead of "Open File Location"). URL parsing lives in `UrlUtil`, which accepts any absolute URI except `file:`.

**Icon loading:** `IconService.GetIconAsync()` checks disk cache first. On miss, uses `StorageFile.GetThumbnailAsync()` to extract the icon and writes it to disk. Cache is invalidated (file deleted) when `AppItemViewModel.FilePath` or `CustomIconPath` is set to a new value. A non-file path (i.e. a URL without a custom icon) returns `null` and the templates show a Segoe MDL2 fallback glyph instead — `FallbackGlyph` / `IconVisibility` / `FallbackIconVisibility` on the view model.

**Tags:** `AppData.Tags` holds the workspace's `TagItem` list; each `AppItem` stores `TagIds`. The list exists for future multi-tag support but the V1 UI assigns **at most one** — `AppItemViewModel.SetSingleTag` clears then adds. `TagIds` is the only persisted state; `TagColorKey` / `TagName` on the view model are *denormalized display fields* pushed in by `MainWindow.Data.cs → ResolveAppTagDisplay`, so anything that mutates the tag list must follow with `RefreshAllAppTagColors()` or the dots keep the old color.

**Design layer:** `Styles/Tokens.xaml` and `Styles/Brushes.xaml` are merged in `App.xaml`. Tokens carry spacing / radii / type only — **no colors**; every color lives in `Brushes.xaml` under `ThemeDictionaries` with `Light`, `Dark`, and `HighContrast` all declared explicitly (never `Default`). Use `{StaticResource}` inside a theme dictionary and `{ThemeResource}` at the usage site. The main window uses two type sizes, 14 and 12; don't reintroduce loose `FontSize` literals.

**Color = context.** The only hues in the UI are workspace color (the shell) and tag color (items); everything else is achromatic, and the system accent is left to interactive controls. `ColorTags.GetBrush` returns the **shared** brush instance out of `Application.Current.Resources` (`AceTagBrush{Key}`), so it is allocation-free but resolves against the theme *at call time* — it does not track later theme switches. The color keys are persisted to JSON and must never be renamed; `ManageWorkspacesDialog`/`ManageTagsDialog` therefore carry the key in `ComboBoxItem.Tag` and use `Content` for display text only.

**Workspace identity brush:** `MainWindow.Motion.cs → InitializeWorkspaceBrush()` creates one `SolidColorBrush` and registers that same instance into the control-level `Resources` of `SidebarListView` and `AppGridView` under the built-in keys (`ListViewItemSelectionIndicator*Brush`, `GridViewItemSelected*BorderBrush`) as well as onto the 4px `WorkspaceSpine`. Overriding theme resource keys keeps the platform's own geometry, states and animations — **do not author a custom `ControlTemplate`** for these. It must run **before** `ItemsSource` is assigned, because `ListViewItemPresenter` resolves these keys when the template is applied. One shared instance means all three surfaces crossfade from a single `ColorAnimation`.

**Motion budget — two moments only:** launch pulse (180ms scale on the tile) and workspace switch (spine crossfade 220ms + content fade 120ms). Everything else is instant by design. All of it is gated on `_animationsEnabled` (`UISettings.AnimationsEnabled`). Tile hover/press are deliberately *not* animated: `GridViewItem` uses `ListViewItemPresenter`, which has no VisualStateManager, so a press scale could only target the template `Border` *inside* the presenter's fill — the fill would stay put while the content shrank. `ColorAnimation` needs `EnableDependentAnimation = true`, and `Storyboard.SetTarget` needs an *element* plus a full property path (`"(UIElement.RenderTransform).(ScaleTransform.ScaleX)"`); targeting a transform object directly compiles and silently animates nothing.

**Keyboard — two mechanisms, chosen by key shape.** Ctrl-modified keys are `KeyboardAccelerator`s in `<Grid.KeyboardAccelerators>` on `RootGrid` (plus `Ctrl+,` and `Ctrl+1..9`, built in `InstallCodeAccelerators()` because comma has no named `VirtualKey` and nine XAML blocks is where declarative loses). Unmodified keys — Esc, F2, Delete, Down, Enter — are `KeyDown`/`PreviewKeyDown` handlers on the specific control. **Do not "unify" these into one mechanism:** a global unmodified accelerator also fires while `SearchBox` has focus, so a global `Delete` would open the delete-apps prompt while the user is editing a query. Three WinUI behaviors here were established by testing, not documentation, and each is load-bearing:
- **Alt-modified accelerators never fire.** `Alt+Enter` arrives as `WM_SYSKEYDOWN`, which the accelerator engine doesn't route. It is handled in `LaunchOrEditAsync`, branching on `e.KeyStatus.IsMenuKeyDown` inside the two lists' existing `PreviewKeyDown`. Moving it back to XAML silently breaks it.
- **`AutoSuggestBox` swallows Down and Esc** for its suggestion list, so `SearchBox` uses `PreviewKeyDown` (tunneling). A bubbling `KeyDown` there is dead code.
- **Flyouts do not suppress `RootGrid`'s accelerators.** Popups live on `PopupRoot`, off the focus chain, so `Ctrl+2` switched workspace with the manage menu open. `TrackAsModal` folds flyout `Opened`/`Closed` into `_modalDepth`.

`_modalDepth` (`MainWindow.Accelerators.cs`) is the reentrancy guard: WinUI allows one `ContentDialog` at a time, and every dialog entry point used to be mouse-only so nothing could race. Route dialogs through `ShowModalAsync`, whole flows through `RunModalAsync`, transient menus through `ShowTrackedFlyout`, and open every accelerator handler with `if (IsModal) return;`.

**Localization:** `Loc.GetString(key)` tries `ResourceLoader` first (MSIX), then falls back to embedded `.resw` files parsed via `XDocument`. Language is auto-detected from `CultureInfo.CurrentUICulture` (en-US, zh-TW, or ja-JP; anything starting with `zh`→Chinese, `ja`→Japanese, else English). String files: `win/Strings/en-US/Resources.resw`, `win/Strings/zh-TW/Resources.resw`, and `win/Strings/ja-JP/Resources.resw` — all three must be updated together when adding a new string. Adding a *string* needs no `.csproj` change; only adding a new *language file* does, as an `EmbeddedResource` with a matching `LogicalName`.

**Single instance:** `Program.cs` uses `AppInstance.FindOrRegisterForKey("AceRun-Main")`. If a second instance starts, it redirects activation to the first and exits. The first instance calls `App.BringToForeground()` via P/Invoke on receiving the redirect.

**System tray:** Initialized in `App.xaml.cs` via H.NotifyIcon. Closing the window hides it (`args.Handled = true`) when `App.TrayEnabled` is true. Exiting via tray calls `Environment.Exit(0)` after disposing the icon. `UpdateTrayContextMenu()` is public — called from `MainWindow` after saves to refresh recent launches.

### Notable Capabilities

- `runFullTrust` — required for launching external processes
- `ExtendsContentIntoTitleBar = true` + `TitleBarHeightOption.Tall` — seamless Mica backdrop into title bar
- Chrome is a single row: `Microsoft.UI.Xaml.Controls.TitleBar` (WASDK 1.8) holds the workspace picker (`LeftHeader`), search (`Content`), and Add / ⚙ (`RightHeader`). It carries no `Title` or `IconSource` — the window's own `Title` covers the taskbar. Its template column order is `PaneToggle → LeftHeader → Icon → Title → Content → RightHeader`, so `LeftHeader` sits at the far left, *before* the app identity slots. `Content` is centered and sized to content, so `HorizontalAlignment="Stretch"` does nothing there — `MinWidth` is what holds the search box open.
- Rail is a `SplitView` (not `NavigationView` — reordering needs `CanReorderItems`), toggled from the title bar and switched Inline↔Overlay from `RootGrid.SizeChanged` at 900 DIP. Overlay needs an opaque `PaneBackground`; Inline lets the Mica through. A `VisualStateManager` on the root of a bare `Window` is unreliable, hence the code-behind.
- Window is sized in the constructor before it is shown (1120×760 DIP default, 720×480 minimum), clamped to the current monitor's work area. `AppWindow.Resize` takes **physical pixels**, so DPI comes from a `GetDpiForWindow` P/Invoke — `XamlRoot.RasterizationScale` is null that early.
- `.lnk` shortcut resolution on drag-and-drop via `WScript.Shell` COM; `.url` Internet Shortcuts parsed by `UrlUtil.ReadInternetShortcut`
- Drag-and-drop accepts `StorageItems`, `WebLink`, and `Text` (in that priority order, so a browser offering several formats adds the link once)
- Admin launch via `ProcessStartInfo.Verb = "runas"` (App items only)
- URL / custom-protocol launch via `UseShellExecute` — `steam://`, `mailto:`, `ms-settings:` all work
- App icon embedded as `EmbeddedResource` (`ace_run.Assets.app-icon.ico`) for tray icon use at runtime
