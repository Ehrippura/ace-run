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

# Publish (self-contained, x64)
dotnet publish win/ace-run.csproj -p:PublishProfile=win/Properties/PublishProfiles/FolderProfile.pubxml
```

The solution file is `win/ace-run.slnx` (can also be opened in Visual Studio).

## Data Storage

All data lives under `%LOCALAPPDATA%\AceRun\`:

| File/Dir | Purpose |
|---|---|
| `config.json` | `WorkspaceConfig` — workspace list, active/default workspace ID, window state, `AppSettings` |
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
  WorkspaceConfig        # v2: Top-level config (workspaces list + window state + Settings)
  WorkspaceInfo          # Workspace metadata (id, name, color tag, app count)
  WorkspaceExport        # Import/export container
  AppSettings            # App-level prefs; AppTheme + HotkeyBinding live in the same file
Services/                # Static service classes
  DataService            # JSON persistence, workspace CRUD, migration, shared JsonOptions
  IconService            # Icon cache (disk) + extraction via StorageFile thumbnail
  UrlUtil                # URL normalization / display name / .url shortcut parsing
  Loc                    # Localization (ResourceLoader with embedded .resw fallback)
  ColorTags              # Color-key list + resolution to the shared theme brushes
  HotkeyService          # RegisterHotKey + WM_HOTKEY via SetWindowSubclass on the main HWND
  StartupService         # "Start with Windows" via the HKCU Run key
  ThemeService           # AppTheme -> ElementTheme, applied per root element
Styles/                  # Design layer, merged in App.xaml
  Tokens.xaml            # Spacing scale, corner radii, type ramp (no colors)
  Brushes.xaml           # ThemeDictionaries: Light / Dark / HighContrast
ViewModels.cs            # AppItemViewModel, FolderViewModel, WorkspaceViewModel, TagViewModel (all INotifyPropertyChanged)
MainWindow.xaml/.cs      # Primary UI + all orchestration (split across .Actions/.Data/.Events/.Motion/.TitleBar/.Workspace/.Organize/.ItemMenu partials)
MainWindow.ItemMenu.cs   # The app-item right-click menu, shared by the tile grid and the search results
MainWindow.Settings.cs   # The bridge between SettingsWindow and everything the settings change
EditItemDialog.xaml/.cs  # ContentDialog for add/edit app or URL (folders use ad-hoc dialogs in MainWindow.Actions.cs)
ManageWorkspacesDialog.xaml/.cs  # ContentDialog for workspace CRUD
ManageTagsDialog.xaml/.cs        # ContentDialog for tag CRUD
SettingsWindow.xaml/.cs  # Standalone window (not a dialog) for the six app-level settings
App.xaml.cs              # App lifecycle, single-instance, tray icon
Program.cs               # Entry point — single-instance via AppInstance.FindOrRegisterForKey
```

### Key Design Patterns

**Workspace data flow:** `InitializeWorkspacesAsync()` calls `DataService.MigrateOrInitialize()`, selects the active workspace, then calls `LoadWorkspaceDataAsync()`. Switching workspaces calls `CommitSave()` (saves current) then `LoadWorkspaceDataAsync()` (loads new). `_suppressWorkspaceSwitch` prevents the `ComboBox.SelectionChanged` handler from firing during programmatic selection.

**UI data model:** `_folders` (`ObservableCollection<FolderViewModel>`) drives `SidebarListView`. `_ungroupedApps` and the selected folder's `FolderViewModel.Apps` drive the main `AppGridView` (tile cards). `_searchResults` drives `SearchResultsView` (full-width rows), shown instead when search is active (saves blocked during search).

**"Ungrouped" is not a folder.** It is a `ListView.Header` inside `SidebarListView`, not a member of `_folders`, and "ungrouped is selected" is represented by `_selectedFolder == null` — which the code keeps in sync by hand. Do not "clean this up" by making it a real `FolderViewModel`: `CommitSave` does `_appData.Folders = _folders.Select(f => f.ToModel())` and `SidebarListView_DragItemsCompleted` calls `CommitSave()` directly, so a phantom folder with a fresh Guid would be persisted on the first drag-reorder. Roughly 28 call sites assume `_folders` holds only real folders.

**Folder navigation goes through one door.** `NavigateToFolder(target, record)` in `MainWindow.History.cs` is the *only* thing that changes which folder the content area shows — rail click, ungrouped row, "Go to folder" from a search result, deleting the open folder, and restoring the saved folder on load all route through it. It sets `_selectedFolder`, the rail selection and `UngroupedItem.IsSelected`, exits search, and refreshes, in that order. Adding a sixth entry point that does those steps by hand will silently skip the back stack. `_suppressFolderNavigation` is the reentrancy guard, playing the same role as `_suppressWorkspaceSwitch`: `NavigateToFolder` assigns `SidebarListView.SelectedItem` itself, so `SidebarListView_SelectionChanged` must ignore the echo. `record: false` marks moves the user did not ask for (workspace load, being evicted from a deleted folder). Back history is `List<Guid?>` (null = ungrouped), session-only, cleared per workspace in `ResetContentState()` and pruned on folder delete; `GoBack` re-resolves each id against `_folders` as it pops, so a stale entry is skipped rather than landing nowhere.

**Save flow (`CommitSave`):** Rebuilds `AppData` from `_ungroupedApps` + `_folders`, updates `WorkspaceInfo.AppCount` (denormalized), calls `DataService.SaveWorkspace` + `DataService.SaveConfig`, then `App.UpdateTrayContextMenu()`.

**Settings have exactly one owner, and it is not the settings window.** `AppSettings` hangs off `WorkspaceConfig` (which is why `WorkspaceConfig.CurrentVersion` is 2; a v1 file simply lacks the key and `System.Text.Json` leaves the property initializer alone, so there is no migration). `MainWindow` holds the one live `WorkspaceConfig` and writes the **whole** thing back from `SaveWindowSize`, `CommitSave`, and `PersistSettings`. `SettingsWindow` therefore never calls `LoadConfig()` — it is handed the `MainWindow` and mutates `owner.Config.Settings` in place. A second copy would be silently overwritten the moment the main window closed. Every default reproduces the pre-settings behaviour (`CloseToTray = true`, no hotkey, `Theme = System`), so an existing install that gains a `Settings` block changes nothing until the user touches something.

`MainWindow.Settings.cs` is the seam. `InitializeSettings()` runs from the constructor — the HWND exists as soon as the `Window` does, and attaching the message hook later would race the first `ApplySettings()`. `ApplySettings()` runs once the config is loaded (top of `InitializeWorkspacesAsync`) and is the only place that fans settings out. `TryApplyHotkey` is separate because it is the one setting that can *fail*: it returns false when Windows refuses the chord, leaving `Settings` untouched so the caller can put the old binding back.

**The global hotkey needs a message loop WinUI does not expose.** `RegisterHotKey` delivers `WM_HOTKEY` to a window's wndproc, so `HotkeyService` subclasses the main HWND with `SetWindowSubclass` (comctl32) rather than standing up a message-only window — the main HWND is stable for the whole process, since closing to the tray is `args.Handled = true` + `AppWindow.Hide()` and never destroys it. Two details are load-bearing: the `SUBCLASSPROC` delegate is held in a **static** field, because unmanaged code keeps no managed reference and a local would be collected out from under every later message; and `MOD_NOREPEAT` is OR'd into the modifiers, without which holding the chord toggles the window dozens of times a second.

**Theme is per-element, and each root has to be told separately.** `Application.RequestedTheme` can only be set before the first window exists, so it cannot serve a toggle; `ThemeService.Apply` writes `FrameworkElement.RequestedTheme` instead, and `App.ApplyTheme` fans it out to the main window and the settings window. A `ContentDialog` lives on the popup layer, outside the tree carrying the override, so it gets its own `RequestedTheme` — set in `ShowModalAsync`, which is why the two manage dialogs were moved off their bare `ShowAsync()` calls and now route through it like every other dialog. `MicaBackdrop` and the system caption buttons both follow the root element's actual theme, so neither needs its own pass.

**`App.TrayEnabled` is not the close-to-tray preference.** It means "the tray icon was created successfully"; the preference is `AppSettings.CloseToTray`, and `MainWindow_Closed` needs both. The else branch has to call `App.ExitApp()`: letting the window close is not quitting, because the tray icon keeps a message loop alive and the process would linger with no window. That branch was unreachable before the setting existed (`TrayEnabled` was only false when tray init threw), which is how the bug survived this long.

**`Loc.Initialize(tag)` must run before the first `GetString`.** Every string in the UI is read once, at construction, and nothing re-reads them — so the language override is applied in `App.OnLaunched` before `new MainWindow()`, and changing it needs a restart, which the settings window says out loud. That costs a second synchronous read of `config.json` (`ApplyInitialWindowSize` does the first); both have to happen before the window is shown. The static constructor still resolves the system language on its own, so a call site that somehow runs earlier gets strings rather than keys.

**Item order is collection order, and Organize does not change that.** There is no comparer, `SortMode` field, or `ICollectionView` anywhere: `CommitSave` serializes `_ungroupedApps` and each `FolderViewModel.Apps` in their current order, so whatever the `ObservableCollection` holds *is* the persisted order. `MainWindow.Organize.cs` is a **one-shot** reorder — it computes a target order and applies it with `ObservableCollection.Move`, after which the result is simply the new manual order. That is why nothing had to be added to `FolderItem` and why `CanReorderItems` stays on. Two details are load-bearing: it moves rather than `Clear()`+`Add()`, because a `Reset` recycles every `GridView` container and `AppGridView_ContainerContentChanging` would then release and reload every icon; and it calls `CommitSave()` directly, because the rail is right-clickable during a search and `SaveItems()` early-returns there — the same reason both `DragItemsCompleted` handlers commit directly. Sorting is stable (`OrderBy`, not `List.Sort`) so equal items keep their dragged order, and "by tag" ranks on the *first* tag's index in `_tags`, which `NormalizeAppTags` already keeps in workspace order — sorting on tag name instead would fight that.

**Selection is a batch, always.** Both item views are `SelectionMode="Extended"`, and every command reads `SelectedAppsInOrder(list)` (`MainWindow.Events.cs`) rather than `SelectedItem` — a one-item selection is a batch of one, not a separate code path. `SelectedItems` is in **selection** order, so that helper re-sorts on `list.Items.IndexOf`: without it a batch Move To lands items in whatever sequence the user happened to Ctrl+click, and Launch All fires in an order nothing on screen predicts. The mirror-image rule is `SelectOnly(list, item)` — under Extended, assigning `SelectedItem` / `SelectedIndex` is not a reliable *replace*, so every programmatic "select just this one" (right-click landing outside the selection, `NavigateToAppFolder`, the search's pre-selected top hit, Down out of the search box) goes through it. Ctrl+A comes free from `ListViewBase`; do **not** add it as a `RootGrid` accelerator, since that would steal select-all-text from `SearchBox`.

The batch helpers each save **once** at the end — `MoveAppsTo`, `LaunchApps`, `SetTagOnApps`, `ClearTagsOnApps`, and the pre-existing `DeleteAppsAsync`. `LaunchApp` is now a one-item wrapper around `LaunchCore` for exactly this reason: the save and the tray rebuild had to come out of the loop, or ten selected tiles meant ten workspace writes and ten tray rebuilds. `PersistAfterEdit()` (`MainWindow.Data.cs`) is the shared tail — `CommitSave()` while a query is active, `SaveItems()` otherwise. Every flow reachable from the search results must use it, because `SaveItems()` early-returns there; that trap is now reachable from far more places than before, since the search rows carry the full menu.

**One right-click menu, two views.** `MainWindow.ItemMenu.cs → BuildAppMenu(list, apps)` builds the menu for both `AppGridView` and `SearchResultsView`; `ShowAppMenu` is the single `RightTapped` body, differing only in which list it is handed. It finds the container via `FindParent<SelectorItem>` — the common base of `GridViewItem` and `ListViewItem` — so one lookup serves both. `ReferenceEquals(list, SearchResultsView)` is the only branch on view, and it gates exactly one entry ("Go to Folder"). The other branch is on selection size, and it hides only what genuinely cannot be batched: Edit opens a dialog for one item, Copy Link / Open File Location address one path. Do not let the two views drift apart again — before this the grid had the full menu and the search rows had a single entry, so "delete this" worked on a tile and did nothing on a row showing the same item.

`ToggleMenuFlyoutItem` has no indeterminate state, so the tag submenu ticks a tag only when **every** selected item carries it. A mixed selection therefore reads as unchecked and the first click gives the tag to all of them — deliberate, and the direction someone reaching for a tag on a multi-selection wants. It also means the mixed state is not recoverable through the menu; `EditItemDialog` stays the per-item surface.

**Dragging tiles onto the rail.** `SidebarListView` always had `AllowDrop="True"` for its own folder reordering but no drop handler, so tiles dragged there did nothing. `AppGridView_DragItemsStarting` now stashes the payload in `_draggedApps` — a ListViewBase's internal reorder package has no public reader, and the rail needs to tell an app drag apart from its own folder drag. That field *is* the discriminator: when it is null, `SidebarListView_DragOver` returns **without touching `AcceptedOperation` or `Handled`**, which is what leaves the built-in folder reorder working.

Two WinUI behaviors had to be found by instrumenting a real drag; neither is guessable from the API, and each on its own is enough to break the feature.

**`DragEventArgs.OriginalSource` is the ListView, not the row under the pointer.** It does not match `Tapped` / `RightTapped`, where `FindParent<ListViewItem>(e.OriginalSource)` is the correct and widely used pattern in this file. Logging every `DragOver` of a real drag showed `OriginalSource == SidebarListView` on every single one, so the walk up never found a container and `TryResolveRailDropTarget` refused every drop — silently, with no error. It therefore hit-tests instead: `VisualTreeHelper.FindElementsInHostCoordinates(e.GetPosition(null), SidebarListView)`, first `ListViewItem` wins. `GetPosition(null)` is XamlRoot-relative, which is the coordinate space that API expects. Do not "simplify" this back to the `FindParent` form the neighbouring handlers use.

The target row lights up while the drag is over it. The highlight is a `Border` behind the row content in both rail templates, with `Margin="-12,-4"` cancelling the container's padding so the fill reaches the row edges instead of floating as an inset pill. It is driven from `FolderViewModel.IsDropTarget` (session-only, deliberately absent from `ToModel` — it describes the pointer, not the folder) via a `Visibility`-typed member, matching how `AppItemViewModel` exposes `IconVisibility` and friends. The ungrouped row is the `ListView.Header`, so it has no view model and its `Border` is toggled by name from code — the same split the rest of the rail already lives with. **Do not** implement this by setting `Background` on the container: the row's fills belong to `ListViewItemPresenter`, and writing them by hand fights the platform's own rest / hover / selected states. `MainWindow.Events.cs` tracks the lit row in `_railDropFolder` + `_railDropIsUngrouped` (two fields because null alone cannot tell "the ungrouped row" from "nothing"), and clears it on `DragLeave`, on `Drop`, on `DragItemsCompleted`, and on the refusal branch — so the folder you are already viewing never lights up as a target it would reject.

**The rail must have `CanReorderItems` switched off for the duration of an app drag** — `AppGridView_DragItemsStarting` clears it, `AppGridView_DragItemsCompleted` (which also fires on cancel) puts it back. Both lists are `ListViewBase` with `CanReorderItems`, which switches on WinUI's built-in **cross-list item transfer**: the rail decides the dragged tiles are items it should insert into `_folders`, parts two rows and draws an insertion line. Wrong on its own terms — a folder row is a thing to drop *into*, and an `AppItemViewModel` has no business in `_folders` — but it also breaks the drop, because the gap it opens contains no `ListViewItem`, so the hit test above lands on the `ScrollViewer` and resolves nothing. Logging position-by-position showed a 45 DIP dead band between two rows whose own height is 36 DIP; with reorder suppressed the rows sit at a contiguous 36 DIP pitch and every `DragOver` resolves. The visible symptom of the dead band was the drag caption freezing on whichever row last resolved. `AllowDrop` stays on throughout, so our handlers keep running, and the rail's own folder reordering is unaffected — a folder drag never passes through `AppGridView_DragItemsStarting`. A drop onto the folder already on screen is refused rather than performed, because `MoveAppsTo` removes and re-appends — it would silently reorder the view the user is looking at. `ReferenceEquals(folder, _selectedFolder)` covers ungrouped-onto-ungrouped too, where both sides are null. `_draggedApps` is cleared in `DragItemsCompleted`, which fires *after* `Drop`.

**Item kinds:** `AppItem.Kind` (`ItemKind.App` / `ItemKind.Url`) decides whether `FilePath` is an exe path or a URL. It is serialized as a string via `JsonStringEnumConverter` in `DataService.JsonOptions` (shared with the `.acerun` import/export in `ManageWorkspacesDialog`), and is absent from pre-v5 files — which correctly falls back to `App`, so no migration exists. `AppItemViewModel.Kind` is **read-only**: an item's kind is fixed at construction and never switches. Anything kind-specific branches on `AppItemViewModel.IsUrl`: `LaunchApp` (URLs get only `FileName` + `UseShellExecute` — no arguments, working directory, or `runas`), the `EditItemDialog` field layout, and the right-click menu ("Copy Link" instead of "Open File Location"). URL parsing lives in `UrlUtil`, which accepts any absolute URI except `file:`.

**Icon loading:** `IconService.GetIconAsync()` checks disk cache first. On miss, uses `StorageFile.GetThumbnailAsync()` to extract the icon and writes it to disk. Cache is invalidated (file deleted) when `AppItemViewModel.FilePath` or `CustomIconPath` is set to a new value. A non-file path (i.e. a URL without a custom icon) returns `null` and the templates show a Segoe MDL2 fallback glyph instead — `FallbackGlyph` / `IconVisibility` / `FallbackIconVisibility` on the view model.

**Tags:** `AppData.Tags` holds the workspace's `TagItem` list; each `AppItem` stores `TagIds`, and an item may carry any number of them. `TagIds` is the only persisted state — `AppItemViewModel.Tags` holds the **same `TagViewModel` instances** as `MainWindow._tags`, so a rename or recolor in `ManageTagsDialog` reaches every tile through the tags' own `INotifyPropertyChanged`. Do not reintroduce denormalized `TagColorKey` / `TagName` fields on the item view model: that is what the old single-tag code did, and it required remembering to call a refresh pass after every mutation of the tag list. Deleting a tag still needs `NormalizeAppTags()`, which drops stale tags, dedupes, and re-sorts each item into workspace tag order — assignment order is deliberately not user state, so the dots on two items with the same tags line up. Derived display members (`VisibleTags` capped at 3, `OverflowLabel`, `TagsSummary`) are recomputed from `SetTags`.

The two assignment surfaces differ by necessity: the right-click submenu uses `ToggleMenuFlyoutItem`s (one per click — a flyout closes on invoke), while `EditItemDialog` uses a `DropDownButton` whose flyout is a plain `Flyout` around a multi-select `ListView`, which stays open for consecutive ticks. Its selection is restored in the ListView's `Loaded` handler, not right after `ItemsSource` is set: `SelectedItems` written before the containers exist does not stick, and the flyout realizes the list lazily.

**Design layer:** `Styles/Tokens.xaml` and `Styles/Brushes.xaml` are merged in `App.xaml`. Tokens carry spacing / radii / type only — **no colors**; every color lives in `Brushes.xaml` under `ThemeDictionaries` with `Light`, `Dark`, and `HighContrast` all declared explicitly (never `Default`). Use `{StaticResource}` inside a theme dictionary and `{ThemeResource}` at the usage site. The main window uses two type sizes, 14 and 12; don't reintroduce loose `FontSize` literals.

**Color = context.** The only hues in the UI are workspace color (the shell) and tag color (items); everything else is achromatic, and the system accent is left to interactive controls. `ColorTags.GetBrush` returns the **shared** brush instance out of `Application.Current.Resources` (`AceTagBrush{Key}`), so it is allocation-free but resolves against the theme *at call time* — it does not track later theme switches. The color keys are persisted to JSON and must never be renamed; `ManageWorkspacesDialog`/`ManageTagsDialog` therefore carry the key in `ComboBoxItem.Tag` and use `Content` for display text only.

**Workspace identity brush:** `MainWindow.Motion.cs → InitializeWorkspaceBrush()` creates one `SolidColorBrush` and registers that same instance into the control-level `Resources` of `SidebarListView` and `AppGridView` under the built-in keys (`ListViewItemSelectionIndicator*Brush`, `GridViewItemSelected*BorderBrush`) as well as onto the `BorderBrush` of `WorkspaceEdge`. Overriding theme resource keys keeps the platform's own geometry, states and animations — **do not author a custom `ControlTemplate`** for these. It must run **before** `ItemsSource` is assigned, because `ListViewItemPresenter` resolves these keys when the template is applied. One shared instance means all three surfaces crossfade from a single `ColorAnimation`.

`WorkspaceEdge` is a 2px frame around the *whole* window, not a left spine. It is the **last** child of `RootGrid` with `Grid.RowSpan="2"` so it draws over the title bar and the content surface as one unbroken rectangle, and `IsHitTestVisible="False"` because it lies on top of the caption buttons and the resize grip. Its `CornerRadius` is not fixed in XAML — `UpdateWindowEdgeCorners()` recomputes it off `RootGrid.SizeChanged`, because a rounded stroke inside a square window leaves four visible notches. **No OS call returns a window's corner radius.** `DWMWA_WINDOW_CORNER_PREFERENCE` reads back as `DWMWCP_DEFAULT` ("system decides") — a preference, not a measurement — so the radius comes from the design system instead: `WindowCornerRadius` reads WinUI's `OverlayCornerRadius` (8), the same value it gives flyouts and dialogs. *Whether* the window is rounded is a separate test, `WindowIsRounded`: build ≥ 22000 (Windows 10 never rounds, and 1809 is still supported) **and** `OverlappedPresenter.State == Restored`, asserted positively so maximised and full-screen both fall out. `Environment.OSVersion` is trustworthy for this — since .NET 5 it goes through `RtlGetVersion` rather than the manifest.

A workspace with **no** colour shows no edge at all — a grey ring on every uncoloured workspace reads as window decoration rather than as state. That is done by fading `WorkspaceEdge.Opacity` (0 in XAML, so the first paint can't flash grey), **not** by pushing a transparent colour into `_edgeBrush`: the same brush instance also paints the rail's selection indicator and the selected tile's border, and those must stay visible on the `AceEdgeInactiveColor` fallback. So `UpdateWorkspaceEdge` runs two animations — `ColorAnimation` on the shared brush, `DoubleAnimation` on this one Border's opacity.

**The chrome row is a `Grid`, and it owns the caption reserve.** It used to be WASDK 1.8's `Microsoft.UI.Xaml.Controls.TitleBar`. That control sizes its trailing template column from `AppWindow.TitleBar.RightInset` — which is a count of **physical pixels** — and assigns it straight into a `ColumnDefinition.Width`, which is measured in DIPs. At 100% scaling the two numbers agree and nothing looks wrong; at 150% it reserved 216 DIP for a 144 DIP strip, and its template adds a hard-coded 48 DIP spacer on top, leaving ~120 DIP of dead title bar between the ⚙ button and the minimise box that no public property could reach. `MainWindow.TitleBar.cs → UpdateTitleBarInsets()` is the same arithmetic with the division actually performed. It drives **both** inset columns, not just the right one, because RTL and left-handed caption buttons move the strip to `LeftInset`; it also sets the row height from `AppWindow.TitleBar.Height`, and writes nothing when nothing moved, so running inside `RootGrid.SizeChanged` cannot feed itself. It is re-run on `XamlRoot.Changed` (DPI) and on every size pass (maximise changes the caption height).

Three things the SDK control supplied come back to us. The back and pane-toggle buttons are ordinary `Button`s on `NavigationBackButtonNormalStyle` — the platform's own 40×40 subtle-icon style, already bound to `SymbolThemeFontFamily`, so the toggle only swaps `Content` to a different glyph. Back stays *visible* and toggles `IsEnabled` (`UpdateBackButtonState()`), so the row never shifts sideways; the click handler lives in `MainWindow.TitleBar.cs` while the history it drives lives in `MainWindow.History.cs`. Their tooltips are now localised — under the SDK control the back button's was hardcoded English in the template and `Loc` could not reach it. And the dimming while the window is inactive is a `Window.Activated` handler setting `AppTitleBar.Opacity`, approximating the template's `*Deactivated` visual states; opacity rather than brush swaps because the row is a dozen unrelated controls, not two `TextBlock`s. `SetTitleBar(AppTitleBar)` still marks the whole row as the drag region, and interactive children inside it still receive input — that is how the search box worked under the control too.

**Motion budget — two moments only:** launch pulse (180ms scale on the tile) and workspace switch (edge crossfade 220ms + content fade 120ms). Everything else is instant by design. All of it is gated on `_animationsEnabled` (`UISettings.AnimationsEnabled`). Tile hover/press are deliberately *not* animated: `GridViewItem` uses `ListViewItemPresenter`, which has no VisualStateManager, so a press scale could only target the template `Border` *inside* the presenter's fill — the fill would stay put while the content shrank. `ColorAnimation` needs `EnableDependentAnimation = true`, and `Storyboard.SetTarget` needs an *element* plus a full property path (`"(UIElement.RenderTransform).(ScaleTransform.ScaleX)"`); targeting a transform object directly compiles and silently animates nothing.

**Keyboard — two mechanisms, chosen by key shape.** Ctrl-modified keys are `KeyboardAccelerator`s in `<Grid.KeyboardAccelerators>` on `RootGrid` (plus `Ctrl+,` and `Ctrl+1..9`, built in `InstallCodeAccelerators()` because comma has no named `VirtualKey` and nine XAML blocks is where declarative loses). Unmodified keys — Esc, F2, Delete, Down, Enter — are `KeyDown`/`PreviewKeyDown` handlers on the specific control. **Do not "unify" these into one mechanism:** a global unmodified accelerator also fires while `SearchBox` has focus, so a global `Delete` would open the delete-apps prompt while the user is editing a query. Three WinUI behaviors here were established by testing, not documentation, and each is load-bearing:
- **Alt-modified accelerators never fire.** `Alt+Enter` arrives as `WM_SYSKEYDOWN`, which the accelerator engine doesn't route. It is handled in `LaunchOrEditAsync`, branching on `e.KeyStatus.IsMenuKeyDown` inside the two lists' existing `PreviewKeyDown`. Moving it back to XAML silently breaks it. `Alt+Left` (back) is the same story with a second reason on top: `ListView`/`GridView` mark the arrow keys handled for focus movement, and `ListViewBase` does the same to `PointerPressed` for its own selection — so **both** back gestures are registered in `InitializeNavigationHistory()` via `AddHandler(..., handledEventsToo: true)`, the only form that still fires. Neither works as a plain XAML handler.
- **`AutoSuggestBox` swallows Down and Esc** for its suggestion list, so `SearchBox` uses `PreviewKeyDown` (tunneling). A bubbling `KeyDown` there is dead code.
- **Flyouts do not suppress `RootGrid`'s accelerators.** Popups live on `PopupRoot`, off the focus chain, so `Ctrl+2` switched workspace with the manage menu open. `TrackAsModal` folds flyout `Opened`/`Closed` into `_modalDepth`.

`_modalDepth` (`MainWindow.Accelerators.cs`) is the reentrancy guard: WinUI allows one `ContentDialog` at a time, and every dialog entry point used to be mouse-only so nothing could race. Route dialogs through `ShowModalAsync`, whole flows through `RunModalAsync`, transient menus through `ShowTrackedFlyout`, and open every accelerator handler with `if (IsModal) return;`.

**Localization:** `Loc.GetString(key)` tries `ResourceLoader` first (MSIX), then falls back to embedded `.resw` files parsed via `XDocument`. Language is auto-detected from `CultureInfo.CurrentUICulture` (en-US, zh-TW, or ja-JP; anything starting with `zh`→Chinese, `ja`→Japanese, else English). String files: `win/Strings/en-US/Resources.resw`, `win/Strings/zh-TW/Resources.resw`, and `win/Strings/ja-JP/Resources.resw` — all three must be updated together when adding a new string. Adding a *string* needs no `.csproj` change; only adding a new *language file* does, as an `EmbeddedResource` with a matching `LogicalName`.

**Single instance:** `Program.cs` uses `AppInstance.FindOrRegisterForKey("AceRun-Main")`. If a second instance starts, it redirects activation to the first and exits. The first instance calls `App.BringToForeground()` via P/Invoke on receiving the redirect.

**System tray:** Initialized in `App.xaml.cs` via H.NotifyIcon. Closing the window hides it (`args.Handled = true`) when the tray icon exists **and** `CloseToTray` is on; otherwise `App.ExitApp()` disposes the icon and calls `Environment.Exit(0)`. `UpdateTrayContextMenu()` is public — called from `MainWindow` after saves to refresh recent launches. The menu carries a Settings entry because it is the only way in while the window is hidden.

### Notable Capabilities

- `runFullTrust` — required for launching external processes
- `ExtendsContentIntoTitleBar = true` + `TitleBarHeightOption.Tall` — seamless Mica backdrop into title bar
- Chrome is a single row, and it is **our own `Grid`**, not `Microsoft.UI.Xaml.Controls.TitleBar`. It carries no app name or icon — the window's own `Title` covers the taskbar. Seven columns: `LeftInset → Back → PaneToggle → WorkspacePicker → Search(*) → Add/⚙ → RightInset`. Search is the one star column, which is what centres it in the *window*. It stretches with a `MaxWidth` (720) and deliberately **no** `MinWidth`: a min would beat the column and push the field out under the workspace picker at the window's 720 DIP minimum size.
- Rail is a `SplitView` (not `NavigationView` — reordering needs `CanReorderItems`), toggled from the title bar and switched Inline↔Overlay from `RootGrid.SizeChanged` at 900 DIP. Overlay needs an opaque `PaneBackground`; Inline lets the Mica through. A `VisualStateManager` on the root of a bare `Window` is unreliable, hence the code-behind.
- Window is sized in the constructor before it is shown (1120×760 DIP default, 720×480 minimum), clamped to the current monitor's work area. `AppWindow.Resize` takes **physical pixels**, so DPI comes from a `GetDpiForWindow` P/Invoke — `XamlRoot.RasterizationScale` is null that early.
- `.lnk` shortcut resolution on drag-and-drop via `WScript.Shell` COM; `.url` Internet Shortcuts parsed by `UrlUtil.ReadInternetShortcut`
- Drag-and-drop accepts `StorageItems`, `WebLink`, and `Text` (in that priority order, so a browser offering several formats adds the link once)
- Admin launch via `ProcessStartInfo.Verb = "runas"` (App items only)
- URL / custom-protocol launch via `UseShellExecute` — `steam://`, `mailto:`, `ms-settings:` all work
- App icon embedded as `EmbeddedResource` (`ace_run.Assets.app-icon.ico`) for tray icon use at runtime
