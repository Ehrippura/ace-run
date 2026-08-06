# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## Working Rules

- After finishing edits, do not automatically commit changes unless the user explicitly asks for a commit.

## Project Overview

Ace Run is a lightweight Windows launcher built with WinUI 3 and C#. Users manage a hierarchical list of launch targets — .exe files (with custom parameters) and URLs / custom protocols — with optional folder grouping, across multiple workspaces. The spec is in `doc/spec.md` (Traditional Chinese) — an overview plus an index into the per-phase files under `doc/spec/`.

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
  AppData                # v6: Tags + UngroupedItems + Folders + RecentLaunches
  AppItem / FolderItem   # Leaf and group nodes (AppItem.SortKey = user-defined Organize key)
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
MainWindow.xaml/.cs      # Primary UI + all orchestration (split across .Actions/.Data/.Events/.Motion/.Workspace/.Organize partials)
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

**Folder navigation goes through one door.** `NavigateToFolder(target, record)` in `MainWindow.History.cs` is the *only* thing that changes which folder the content area shows — rail click, ungrouped row, "Go to folder" from a search result, deleting the open folder, and restoring the saved folder on load all route through it. It sets `_selectedFolder`, the rail selection and `UngroupedItem.IsSelected`, exits search, and refreshes, in that order. Adding a sixth entry point that does those steps by hand will silently skip the back stack. `_suppressFolderNavigation` is the reentrancy guard, playing the same role as `_suppressWorkspaceSwitch`: `NavigateToFolder` assigns `SidebarListView.SelectedItem` itself, so `SidebarListView_SelectionChanged` must ignore the echo. `record: false` marks moves the user did not ask for (workspace load, being evicted from a deleted folder). Back history is `List<Guid?>` (null = ungrouped), session-only, cleared per workspace in `ResetContentState()` and pruned on folder delete; `GoBack` re-resolves each id against `_folders` as it pops, so a stale entry is skipped rather than landing nowhere.

**Save flow (`CommitSave`):** Rebuilds `AppData` from `_ungroupedApps` + `_folders`, updates `WorkspaceInfo.AppCount` (denormalized), calls `DataService.SaveWorkspace` + `DataService.SaveConfig`, then `App.UpdateTrayContextMenu()`.

**Item order is collection order, and Organize does not change that.** There is no comparer, `SortMode` field, or `ICollectionView` anywhere: `CommitSave` serializes `_ungroupedApps` and each `FolderViewModel.Apps` in their current order, so whatever the `ObservableCollection` holds *is* the persisted order. `MainWindow.Organize.cs` is a **one-shot** reorder — it computes a target order and applies it with `ObservableCollection.Move`, after which the result is simply the new manual order. That is why nothing had to be added to `FolderItem` and why `CanReorderItems` stays on. Two details are load-bearing: it moves rather than `Clear()`+`Add()`, because a `Reset` recycles every `GridView` container and `AppGridView_ContainerContentChanging` would then release and reload every icon; and it calls `CommitSave()` directly, because the rail is right-clickable during a search and `SaveItems()` early-returns there — the same reason both `DragItemsCompleted` handlers commit directly. Sorting is stable (`OrderBy`, not `List.Sort`) so equal items keep their dragged order, and "by tag" ranks on the *first* tag's index in `_tags`, which `NormalizeAppTags` already keeps in workspace order — sorting on tag name instead would fight that.

**Item kinds:** `AppItem.Kind` (`ItemKind.App` / `ItemKind.Url`) decides whether `FilePath` is an exe path or a URL. It is serialized as a string via `JsonStringEnumConverter` in `DataService.JsonOptions` (shared with the `.acerun` import/export in `ManageWorkspacesDialog`), and is absent from pre-v5 files — which correctly falls back to `App`, so no migration exists. `AppItemViewModel.Kind` is **read-only**: an item's kind is fixed at construction and never switches. Anything kind-specific branches on `AppItemViewModel.IsUrl`: `LaunchApp` (URLs get only `FileName` + `UseShellExecute` — no arguments, working directory, or `runas`), the `EditItemDialog` field layout, and the right-click menu ("Copy Link" instead of "Open File Location"). URL parsing lives in `UrlUtil`, which accepts any absolute URI except `file:`.

**Icon loading:** `IconService.GetIconAsync()` checks disk cache first. On miss, uses `StorageFile.GetThumbnailAsync()` to extract the icon and writes it to disk. Cache is invalidated (file deleted) when `AppItemViewModel.FilePath` or `CustomIconPath` is set to a new value. A non-file path (i.e. a URL without a custom icon) returns `null` and the templates show a Segoe MDL2 fallback glyph instead — `FallbackGlyph` / `IconVisibility` / `FallbackIconVisibility` on the view model.

**Tags:** `AppData.Tags` holds the workspace's `TagItem` list; each `AppItem` stores `TagIds`, and an item may carry any number of them. `TagIds` is the only persisted state — `AppItemViewModel.Tags` holds the **same `TagViewModel` instances** as `MainWindow._tags`, so a rename or recolor in `ManageTagsDialog` reaches every tile through the tags' own `INotifyPropertyChanged`. Do not reintroduce denormalized `TagColorKey` / `TagName` fields on the item view model: that is what the old single-tag code did, and it required remembering to call a refresh pass after every mutation of the tag list. Deleting a tag still needs `NormalizeAppTags()`, which drops stale tags, dedupes, and re-sorts each item into workspace tag order — assignment order is deliberately not user state, so the dots on two items with the same tags line up. Derived display members (`VisibleTags` capped at 3, `OverflowLabel`, `TagsSummary`) are recomputed from `SetTags`.

The two assignment surfaces differ by necessity: the right-click submenu uses `ToggleMenuFlyoutItem`s (one per click — a flyout closes on invoke), while `EditItemDialog` uses a `DropDownButton` whose flyout is a plain `Flyout` around a multi-select `ListView`, which stays open for consecutive ticks. Its selection is restored in the ListView's `Loaded` handler, not right after `ItemsSource` is set: `SelectedItems` written before the containers exist does not stick, and the flyout realizes the list lazily.

**Design layer:** `Styles/Tokens.xaml` and `Styles/Brushes.xaml` are merged in `App.xaml`. Tokens carry spacing / radii / type only — **no colors**; every color lives in `Brushes.xaml` under `ThemeDictionaries` with `Light`, `Dark`, and `HighContrast` all declared explicitly (never `Default`). Use `{StaticResource}` inside a theme dictionary and `{ThemeResource}` at the usage site. The main window uses two type sizes, 14 and 12; don't reintroduce loose `FontSize` literals.

**Color = context.** The only hues in the UI are workspace color (the shell) and tag color (items); everything else is achromatic, and the system accent is left to interactive controls. `ColorTags.GetBrush` returns the **shared** brush instance out of `Application.Current.Resources` (`AceTagBrush{Key}`), so it is allocation-free but resolves against the theme *at call time* — it does not track later theme switches. The color keys are persisted to JSON and must never be renamed; `ManageWorkspacesDialog`/`ManageTagsDialog` therefore carry the key in `ComboBoxItem.Tag` and use `Content` for display text only.

**Workspace identity brush:** `MainWindow.Motion.cs → InitializeWorkspaceBrush()` creates one `SolidColorBrush` and registers that same instance into the control-level `Resources` of `SidebarListView` and `AppGridView` under the built-in keys (`ListViewItemSelectionIndicator*Brush`, `GridViewItemSelected*BorderBrush`) as well as onto the `BorderBrush` of `WorkspaceEdge`. Overriding theme resource keys keeps the platform's own geometry, states and animations — **do not author a custom `ControlTemplate`** for these. It must run **before** `ItemsSource` is assigned, because `ListViewItemPresenter` resolves these keys when the template is applied. One shared instance means all three surfaces crossfade from a single `ColorAnimation`.

`WorkspaceEdge` is a 2px frame around the *whole* window, not a left spine. It is the **last** child of `RootGrid` with `Grid.RowSpan="2"` so it draws over the title bar and the content surface as one unbroken rectangle, and `IsHitTestVisible="False"` because it lies on top of the caption buttons and the resize grip. Its `CornerRadius` is not fixed in XAML — `UpdateWindowEdgeCorners()` recomputes it off `RootGrid.SizeChanged`, because a rounded stroke inside a square window leaves four visible notches. **No OS call returns a window's corner radius.** `DWMWA_WINDOW_CORNER_PREFERENCE` reads back as `DWMWCP_DEFAULT` ("system decides") — a preference, not a measurement — so the radius comes from the design system instead: `WindowCornerRadius` reads WinUI's `OverlayCornerRadius` (8), the same value it gives flyouts and dialogs. *Whether* the window is rounded is a separate test, `WindowIsRounded`: build ≥ 22000 (Windows 10 never rounds, and 1809 is still supported) **and** `OverlappedPresenter.State == Restored`, asserted positively so maximised and full-screen both fall out. `Environment.OSVersion` is trustworthy for this — since .NET 5 it goes through `RtlGetVersion` rather than the manifest.

A workspace with **no** colour shows no edge at all — a grey ring on every uncoloured workspace reads as window decoration rather than as state. That is done by fading `WorkspaceEdge.Opacity` (0 in XAML, so the first paint can't flash grey), **not** by pushing a transparent colour into `_edgeBrush`: the same brush instance also paints the rail's selection indicator and the selected tile's border, and those must stay visible on the `AceEdgeInactiveColor` fallback. So `UpdateWorkspaceEdge` runs two animations — `ColorAnimation` on the shared brush, `DoubleAnimation` on this one Border's opacity.

**Motion budget — two moments only:** launch pulse (180ms scale on the tile) and workspace switch (edge crossfade 220ms + content fade 120ms). Everything else is instant by design. All of it is gated on `_animationsEnabled` (`UISettings.AnimationsEnabled`). Tile hover/press are deliberately *not* animated: `GridViewItem` uses `ListViewItemPresenter`, which has no VisualStateManager, so a press scale could only target the template `Border` *inside* the presenter's fill — the fill would stay put while the content shrank. `ColorAnimation` needs `EnableDependentAnimation = true`, and `Storyboard.SetTarget` needs an *element* plus a full property path (`"(UIElement.RenderTransform).(ScaleTransform.ScaleX)"`); targeting a transform object directly compiles and silently animates nothing.

**Keyboard — two mechanisms, chosen by key shape.** Ctrl-modified keys are `KeyboardAccelerator`s in `<Grid.KeyboardAccelerators>` on `RootGrid` (plus `Ctrl+,` and `Ctrl+1..9`, built in `InstallCodeAccelerators()` because comma has no named `VirtualKey` and nine XAML blocks is where declarative loses). Unmodified keys — Esc, F2, Delete, Down, Enter — are `KeyDown`/`PreviewKeyDown` handlers on the specific control. **Do not "unify" these into one mechanism:** a global unmodified accelerator also fires while `SearchBox` has focus, so a global `Delete` would open the delete-apps prompt while the user is editing a query. Three WinUI behaviors here were established by testing, not documentation, and each is load-bearing:
- **Alt-modified accelerators never fire.** `Alt+Enter` arrives as `WM_SYSKEYDOWN`, which the accelerator engine doesn't route. It is handled in `LaunchOrEditAsync`, branching on `e.KeyStatus.IsMenuKeyDown` inside the two lists' existing `PreviewKeyDown`. Moving it back to XAML silently breaks it. `Alt+Left` (back) is the same story with a second reason on top: `ListView`/`GridView` mark the arrow keys handled for focus movement, and `ListViewBase` does the same to `PointerPressed` for its own selection — so **both** back gestures are registered in `InitializeNavigationHistory()` via `AddHandler(..., handledEventsToo: true)`, the only form that still fires. Neither works as a plain XAML handler.
- **`AutoSuggestBox` swallows Down and Esc** for its suggestion list, so `SearchBox` uses `PreviewKeyDown` (tunneling). A bubbling `KeyDown` there is dead code.
- **Flyouts do not suppress `RootGrid`'s accelerators.** Popups live on `PopupRoot`, off the focus chain, so `Ctrl+2` switched workspace with the manage menu open. `TrackAsModal` folds flyout `Opened`/`Closed` into `_modalDepth`.

`_modalDepth` (`MainWindow.Accelerators.cs`) is the reentrancy guard: WinUI allows one `ContentDialog` at a time, and every dialog entry point used to be mouse-only so nothing could race. Route dialogs through `ShowModalAsync`, whole flows through `RunModalAsync`, transient menus through `ShowTrackedFlyout`, and open every accelerator handler with `if (IsModal) return;`.

**Localization:** `Loc.GetString(key)` tries `ResourceLoader` first (MSIX), then falls back to embedded `.resw` files parsed via `XDocument`. Language is auto-detected from `CultureInfo.CurrentUICulture` (en-US, zh-TW, or ja-JP; anything starting with `zh`→Chinese, `ja`→Japanese, else English). String files: `win/Strings/en-US/Resources.resw`, `win/Strings/zh-TW/Resources.resw`, and `win/Strings/ja-JP/Resources.resw` — all three must be updated together when adding a new string. Adding a *string* needs no `.csproj` change; only adding a new *language file* does, as an `EmbeddedResource` with a matching `LogicalName`.

**Single instance:** `Program.cs` uses `AppInstance.FindOrRegisterForKey("AceRun-Main")`. If a second instance starts, it redirects activation to the first and exits. The first instance calls `App.BringToForeground()` via P/Invoke on receiving the redirect.

**System tray:** Initialized in `App.xaml.cs` via H.NotifyIcon. Closing the window hides it (`args.Handled = true`) when `App.TrayEnabled` is true. Exiting via tray calls `Environment.Exit(0)` after disposing the icon. `UpdateTrayContextMenu()` is public — called from `MainWindow` after saves to refresh recent launches.

### Notable Capabilities

- `runFullTrust` — required for launching external processes
- `ExtendsContentIntoTitleBar = true` + `TitleBarHeightOption.Tall` — seamless Mica backdrop into title bar
- Chrome is a single row: `Microsoft.UI.Xaml.Controls.TitleBar` (WASDK 1.8) holds the workspace picker (`LeftHeader`), search (`Content`), and Add / ⚙ (`RightHeader`). It carries no `Title` or `IconSource` — the window's own `Title` covers the taskbar. Its template column order is `BackButton → PaneToggle → LeftHeader → Icon → Title → Content → RightHeader`, so `LeftHeader` sits at the far left, *before* the app identity slots. The back button is the control's own `PART_BackButton` (`IsBackButtonVisible` / `IsBackButtonEnabled` / `BackRequested`), not a button of ours — don't add one to `LeftHeader`. It stays visible and toggles `IsBackButtonEnabled` so the row never shifts sideways, and its tooltip is hardcoded `"Back"` in the SDK template, so it is deliberately the one string in the UI that `Loc` does not reach. `Content` is centered and sized to content, so `HorizontalAlignment="Stretch"` does nothing there — `MinWidth` is what holds the search box open.
- Rail is a `SplitView` (not `NavigationView` — reordering needs `CanReorderItems`), toggled from the title bar and switched Inline↔Overlay from `RootGrid.SizeChanged` at 900 DIP. Overlay needs an opaque `PaneBackground`; Inline lets the Mica through. A `VisualStateManager` on the root of a bare `Window` is unreliable, hence the code-behind.
- Window is sized in the constructor before it is shown (1120×760 DIP default, 720×480 minimum), clamped to the current monitor's work area. `AppWindow.Resize` takes **physical pixels**, so DPI comes from a `GetDpiForWindow` P/Invoke — `XamlRoot.RasterizationScale` is null that early.
- `.lnk` shortcut resolution on drag-and-drop via `WScript.Shell` COM; `.url` Internet Shortcuts parsed by `UrlUtil.ReadInternetShortcut`
- Drag-and-drop accepts `StorageItems`, `WebLink`, and `Text` (in that priority order, so a browser offering several formats adds the link once)
- Admin launch via `ProcessStartInfo.Verb = "runas"` (App items only)
- URL / custom-protocol launch via `UseShellExecute` — `steam://`, `mailto:`, `ms-settings:` all work
- App icon embedded as `EmbeddedResource` (`ace_run.Assets.app-icon.ico`) for tray icon use at runtime
