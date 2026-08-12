# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

It records **rules and the reasons behind them** — the things that look like cruft and are not. Deeper forensics live in the code comments next to the code they explain; don't duplicate them here.

## Working Rules

- After finishing edits, do not automatically commit changes unless the user explicitly asks for a commit.
- **When a change touches something an existing phase already specified, amend that phase's file under `doc/spec/` — do not add a new phase.** Phase numbers are historical order, not scope boundaries, and a reader of one file must not be misled by it. Only genuinely new scope earns a new phase. Merging a phase away (phase 9 went back into 6) means renumbering the ones after it to close the gap — and updating every cross-reference: the `doc/spec.md` table, the "第 N 階段" mentions inside other phase files, and filenames cited in code comments.

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

# Test — no -p:Platform, and that is the point (see "Testability" below)
dotnet test test/AceRun.Core.Tests/AceRun.Core.Tests.csproj

# Run (unpackaged mode)
dotnet run --project win/ace-run.csproj

# Publish (self-contained, x64). Empty the output folder first — publish does not clear it.
dotnet publish win/ace-run.csproj -c Release -p:Platform=x64 -r win-x64 -p:PublishProfile=self-contained
```

The solution file is `win/ace-run.slnx` (can also be opened in Visual Studio).

**`-p:Platform` is not optional.** The csproj declares `x86;x64;ARM64` and no AnyCPU, so any build or publish that omits it resolves no configuration. `PublishProfile` takes the *name*, not a path — MSBuild looks under `win/Properties/PublishProfiles/`.

There are two profiles, and each pins `Configuration=Release`, `Platform=x64` and `RuntimeIdentifier=win-x64` so a bare `-p:PublishProfile=<name>` works on its own:

| Profile | Bundles | Target needs |
|---|---|---|
| `self-contained` | .NET **and** the Windows App SDK (~263 MB) | nothing — this is the one users get |
| `environment-deps` | neither (~41 MB, no ReadyToRun) | .NET 10 Desktop Runtime **and** WindowsAppRuntime 1.8 preinstalled |

Both carry a `PublishDir` pointing at a local Desktop folder, which is a personal convenience and not something to depend on. Override it — and the platform/RID for a non-x64 build — with command-line properties: those are MSBuild *global* properties and win over anything the profile sets, which is exactly how `.github/workflows/release.yml` retargets the same profile at `win-arm64` and at its own artifacts folder.

**Two self-contained switches, and no trimming.** `SelfContained` bundles .NET, `WindowsAppSDKSelfContained` bundles the Windows App SDK; neither implies the other and both are needed, because no ordinary user installs the WindowsAppRuntime and it is version-band specific. `PublishTrimmed` is pinned **false**: WinUI 3 resolves XAML types by reflection, so a trimmed build publishes cleanly and then crashes on startup inside `Microsoft.UI.Xaml.dll`. Trimming engages only on a Release *publish*, so an ordinary build never warns you.

## Data Storage

All data lives under `%LOCALAPPDATA%\AceRun\`:

| File/Dir | Purpose |
|---|---|
| `config.json` | `WorkspaceConfig` — workspace list, active/default workspace ID, window state, `AppSettings` |
| `workspaces/<guid>.json` | Per-workspace `AppData` — ungrouped items, folders, recent launches |
| `icons/<guid>` | Cached app icons (keyed by `AppItem.Id`), extensionless — see "Icon loading" |
| `apps.json.bak` | Migration backup from pre-workspace format |

On first launch, `DataService.MigrateOrInitialize()` either reads `config.json` or migrates the legacy `apps.json` into the new format.

Version numbers are stamped **on write** (`AppData` v6, `WorkspaceConfig` v2). New fields need no migration code: an older file simply lacks the key and `System.Text.Json` leaves the property initializer in place.

Every file goes through the one shared `AceRunJson.Options` (reachable as `DataService.JsonOptions`), including the `.acerun` import/export — that is what keeps them from drifting, and its `JsonStringEnumConverter` is why enums (`ItemKind`, `AppTheme`, the hotkey's `VirtualKey` / `VirtualKeyModifiers`) persist as readable names rather than numbers.

**`DataService` is a facade, not the implementation.** `DataStore` does the work and takes an `AceRunPaths` whose root is a constructor argument, which is how a test runs the whole layer — migration included — in a temp directory. The options object lives apart from both so that reading it cannot trigger path resolution: the old static class resolved `%LOCALAPPDATA%` and created the data directory in its type initializer, so merely touching it wrote to the profile.

**`MigrateOrInitialize` promises a *usable* config; `LoadConfig` does not.** An unreadable file gives `LoadConfig` nothing to return but a fresh `WorkspaceConfig`, whose workspace list is empty — and every consumer does `.First()` on it. Startup runs on a fire-and-forget task, so that exception went unobserved: the user got a window with an empty workspace picker, no error, and no way back, because `config.json` existed and migration never re-ran. `EnsureUsable` is the floor (≥1 workspace, `ActiveWorkspaceId` names one of them) and the repair is written back so later `LoadConfig` callers see it. It repairs rather than refuses, and deletes nothing: the workspace files survive a damaged config, so the user can import them back.

**Writes go through `WriteAtomic` — temp file, then rename.** `File.WriteAllText` truncates before it fills, and a crash in that window leaves `config.json` truncated, which is the index to every workspace. The icon cache has had this since it was written; the data that cannot be regenerated did not. A stray `.tmp` is inert — only `icons/` is ever enumerated (by `IconCache.ClearAll`), and every other lookup is by exact path.

## Architecture

### Layers

Three projects. `core/AceRun.Core` holds everything that does not need WinUI, `win/` is the app, `test/AceRun.Core.Tests` covers the first and never sees the second.

```
core/AceRun.Core/        # No WinUI. Namespaces stay ace_run.Models / ace_run.Services
  Models/                # Plain data classes
    AppData              # v6: Tags + UngroupedItems + Folders + RecentLaunches; ItemCount
    AppItem / FolderItem # Leaf and group nodes (AppItem.SortKey = user-defined Organize key)
    TagItem              # User-defined tag (id, name, color key)
    ItemKind             # App | Url — what AppItem.FilePath points at
    WorkspaceConfig      # v2: Top-level config (workspaces list + window state + Settings)
    WorkspaceInfo        # Workspace metadata (id, name, color tag, app count)
    WorkspaceExport      # Import/export container
    AppSettings          # App-level prefs; AppTheme + HotkeyBinding live in the same file
    IAppItemView         # + ITagRef — the seam that keeps WinUI out of the logic below
  Services/
    AceRunJson           # The one shared JsonSerializerOptions
    AceRunPaths          # Where the data lives, rooted at a constructor argument
    DataStore            # JSON persistence, workspace CRUD, migration
    DataService          # Static facade over DataStore.Default — what the app calls
    UrlUtil              # URL normalization / display name / .url shortcut parsing
    SearchRanking        # MatchRank + result ordering
    ItemOrdering         # OrganizeBy + the four sorts + ApplyOrder
    FolderHistory        # The back stack
    TagOrdering          # Tag id set -> workspace-ordered tags; normalization
    RecentLaunchList     # Track / Purge, capped at MaxRecent
    WindowGeometry.cs    # WindowPlacement, TitleBarMetrics, DropGeometry + pixel records
    ItemFactory          # AppItem from a path or a URL; AppDataQuery walks a workspace
    IconCache            # Cache paths, the unfiltered sweep, icon-source selection
    IconExtractionPolicy # Backoff schedule + the E_PENDING retry test
    ColorKeys            # The persisted colour keys and the default
    WorkspaceImport      # Vets an .acerun file: TryParse -> ImportRejection
test/AceRun.Core.Tests/  # xUnit. References Core only — never win/ace-run.csproj
win/
Services/                # Static service classes — the ones that need WinUI or the OS
  IconService            # BitmapImage + StorageFile thumbnail extraction, and its two gates
  Loc                    # Localization (ResourceLoader with embedded .resw fallback)
  ColorTags              # Colour key -> the shared theme brush (the keys are ColorKeys')
  HotkeyService          # RegisterHotKey + WM_HOTKEY via SetWindowSubclass on the main HWND
  StartupService         # "Start with Windows" via the HKCU Run key
  ThemeService           # AppTheme -> ElementTheme, applied per root element
  DisplayScale           # GetDpiForWindow, for the two constructors with no XamlRoot yet
  ConfirmFlyout          # The shared yes/no popup both manage dialogs use
Styles/                  # Design layer, merged in App.xaml
  Tokens.xaml            # Spacing scale, corner radii, type ramp (no colors)
  Brushes.xaml           # ThemeDictionaries: Light / Dark / HighContrast
ViewModels.cs            # AppItemViewModel, FolderViewModel, WorkspaceViewModel, TagViewModel (all INotifyPropertyChanged)
MainWindow.xaml/.cs      # Primary UI + all orchestration (split across .Actions/.Data/.Events/.Motion/.TitleBar/.Workspace/.Organize/.ItemMenu/.Settings/.History partials)
EditItemDialog.xaml/.cs  # ContentDialog for add/edit app or URL (folders use ad-hoc dialogs in MainWindow.Actions.cs)
ManageWorkspacesDialog.xaml/.cs  # ContentDialog for workspace CRUD
ManageTagsDialog.xaml/.cs        # ContentDialog for tag CRUD
SettingsWindow.xaml/.cs  # Standalone window (not a dialog) for the app-level settings
App.xaml.cs              # App lifecycle, single-instance, tray icon, theme fan-out
Program.cs               # Entry point — single-instance via AppInstance.FindOrRegisterForKey
```

### Testability — two rules hold the split open

**The test project references `AceRun.Core` and nothing else.** Never `win/ace-run.csproj`. That is what lets `dotnet test` run with no `-p:Platform` and no WindowsAppRuntime installed, and it is the only thing stopping logic from staying in `MainWindow` while looking tested. If a test step ever needs the app's platform incantation, something WinUI-shaped has leaked into the logic layer.

**`AceRun.Core` has no `PackageReference` at all.** Its Windows TFM is there for exactly one reason: `HotkeyBinding` stores `Windows.System.VirtualKey` / `VirtualKeyModifiers`, which are metadata-only WinRT enums supplied by the projection the TFM implies. No COM activation, no UI thread, no Windows App SDK.

`core/` and `test/` sit beside `win/`, not inside it, because `ace-run.csproj` has no `<Compile>` items and relies on default globbing — anything under `win/` would be compiled into the app as well.

`IAppItemView` / `ITagRef` (`Models/IAppItemView.cs`) are what let search and Organize run over the live view models without dragging `Visibility`, `Brush` and `BitmapImage` across the boundary. `Tags` is `IEnumerable<ITagRef>` so `ObservableCollection<TagViewModel>` satisfies it by covariance — no projection, no per-call allocation.

What stays in `MainWindow` is what it genuinely owns: UI state, event wiring, and *when* to save. `ItemOrdering.ApplyOrder` returns whether anything moved and lets the caller decide about `CommitSave()`; `SearchRanking.Rank` returns `(item, folderLabel)` pairs rather than writing `FolderLabel` during the ranking pass.

### Data flow

**Workspaces.** `InitializeWorkspacesAsync()` calls `DataService.MigrateOrInitialize()`, applies settings, selects the active workspace, then `LoadWorkspaceDataAsync()`. Switching calls `CommitSave()` then `LoadWorkspaceDataAsync()`. `_suppressWorkspaceSwitch` stops `ComboBox.SelectionChanged` firing during programmatic selection.

**Collections.** `_folders` drives `SidebarListView`; `_ungroupedApps` or the selected folder's `Apps` drive `AppGridView`; `_searchResults` drives `SearchResultsView`, shown instead while a query is active.

**`CommitSave`** rebuilds `AppData` from `_ungroupedApps` + `_folders`, updates the denormalized `WorkspaceInfo.AppCount`, writes workspace + config, then rebuilds the tray menu.

**`SaveItems()` early-returns during a search.** `PersistAfterEdit()` (`MainWindow.Data.cs`) is the shared tail that picks `CommitSave()` or `SaveItems()` correctly — every flow reachable from the search results must use it. Anything triggered from the rail (Organize, both `DragItemsCompleted` handlers) calls `CommitSave()` directly, since the rail stays live during a search.

**Item order is collection order.** There is no comparer, sort mode, or `ICollectionView` anywhere — whatever the `ObservableCollection` holds *is* the persisted order. `MainWindow.Organize.cs` is a **one-shot** reorder that applies its result with `ObservableCollection.Move` and then gets out of the way, which is why `FolderItem` needs no sort state and `CanReorderItems` stays on. Move rather than `Clear()`+`Add()`: a `Reset` recycles every `GridView` container and reloads every icon. Sorting is stable (`OrderBy`, never `List.Sort`) so equal items keep their dragged order; "by tag" ranks on the first tag's index in `_tags`, matching the workspace order `NormalizeAppTags` maintains.

### Invariants — things that look wrong and are not

**"Ungrouped" is not a folder.** It is a `ListView.Header`, not a member of `_folders`, and "ungrouped is selected" means `_selectedFolder == null`. Do not promote it to a real `FolderViewModel`: `CommitSave` serializes `_folders` wholesale, so a phantom folder would be persisted on the first drag-reorder. Dozens of call sites assume `_folders` holds only real folders.

**Folder navigation goes through one door.** `NavigateToFolder(target, record)` (`MainWindow.History.cs`) is the *only* thing that changes which folder is shown; every entry point routes through it. It sets `_selectedFolder`, the rail selection, exits search and refreshes, in that order — a new entry point that does those steps by hand silently skips the back stack. `_suppressFolderNavigation` is its reentrancy guard, same role as `_suppressWorkspaceSwitch`. `record: false` marks moves the user did not ask for. Back history is session-only `List<Guid?>` (null = ungrouped), and `GoBack` re-resolves each id as it pops so stale entries are skipped.

**Settings have exactly one owner, and it is not the settings window.** `MainWindow` holds the one live `WorkspaceConfig` and writes the **whole** thing back. `SettingsWindow` is handed the `MainWindow` and mutates `owner.Config.Settings` in place — a copy loaded with `LoadConfig()` would be overwritten the moment the main window closed. Every default reproduces the pre-settings behaviour, so an existing install gains a `Settings` block and behaves identically until the user touches something.

`MainWindow.Settings.cs` is the seam: `InitializeSettings()` from the constructor (the HWND exists as soon as the `Window` does), `ApplySettings()` once the config is loaded, and `TryApplyHotkey` separately because it is the one setting that can *fail* — it returns false when Windows refuses the chord, leaving `Settings` untouched so the caller can restore the old binding. `ResetIconCache()` is the odd one out and sits in its own Storage group: it writes nothing to `AppSettings`, it performs an action.

**`App.TrayEnabled` is not the close-to-tray preference.** It means the tray icon was created successfully; the preference is `AppSettings.CloseToTray`, and `MainWindow_Closed` needs both. The else branch must call `App.ExitApp()` — letting the window close is not quitting, because the tray icon keeps a message loop alive.

**`Loc.Initialize(tag)` must run before the first `GetString`.** Every UI string is read once at construction and nothing re-reads them, so the language override is applied in `App.OnLaunched` before `new MainWindow()`, and changing it needs a restart. The static constructor still resolves the system language on its own as a fallback.

**Selection is a batch, always.** Both item views are `SelectionMode="Extended"` and every command reads `SelectedAppsInOrder(list)` rather than `SelectedItem` — a one-item selection is a batch of one. That helper re-sorts on `list.Items.IndexOf` because `SelectedItems` is in *selection* order, which would otherwise make batch Move To and Launch All run in Ctrl+click order. The mirror rule is `SelectOnly(list, item)`: under Extended, assigning `SelectedItem` is not a reliable *replace*, so every programmatic "select just this one" goes through it. Ctrl+A comes free from `ListViewBase` — do **not** add it as a `RootGrid` accelerator, which would steal select-all-text from `SearchBox`.

Batch helpers (`MoveAppsTo`, `LaunchApps`, `SetTagOnApps`, `ClearTagsOnApps`, `DeleteAppsAsync`) save **once** at the end; `LaunchApp` is a one-item wrapper around `LaunchCore` for that reason.

**One right-click menu, two views.** `BuildAppMenu(list, apps)` (`MainWindow.ItemMenu.cs`) serves both `AppGridView` and `SearchResultsView`; `ShowAppMenu` is the single `RightTapped` body. `FindParent<SelectorItem>` covers both container types. Only one entry branches on view ("Go to Folder"); the rest branch on selection size, hiding only what genuinely cannot be batched. Do not let the two views drift apart again.

`ToggleMenuFlyoutItem` has no indeterminate state, so the tag submenu ticks a tag only when **every** selected item carries it — a mixed selection reads as unchecked and the first click gives the tag to all of them. The mixed state is therefore not recoverable through the menu; `EditItemDialog` stays the per-item surface.

**Tags are shared instances.** `TagIds` is the only persisted state; `AppItemViewModel.Tags` holds the **same `TagViewModel` objects** as `MainWindow._tags`, so a rename or recolor propagates through their own `INotifyPropertyChanged`. Do not reintroduce denormalized `TagColorKey` / `TagName` on the item view model — that was the old single-tag design and it needed a manual refresh pass after every tag mutation. Deleting a tag still needs `NormalizeAppTags()`, which drops stale ids, dedupes, and re-sorts each item into workspace tag order so the dots line up between items.

**Item kinds are fixed at construction.** `AppItem.Kind` decides whether `FilePath` is an exe path or a URL; `AppItemViewModel.Kind` is read-only and never switches. Kind-specific behaviour branches on `IsUrl`: launch (URLs get only `FileName` + `UseShellExecute`), the `EditItemDialog` field layout, and "Copy Link" vs "Open File Location". URL parsing lives in `UrlUtil`, which accepts any absolute URI except `file:`.

**Icon loading, and where it is split.** `IconService.GetIconAsync()` checks the disk cache, then extracts via `StorageFile.GetThumbnailAsync()`. A non-file path returns `null` and the templates fall back to a Segoe MDL2 glyph (`FallbackGlyph` / `IconVisibility` / `FallbackIconVisibility`).

The cache's *rules* are not in `IconService`: `IconCache` owns where an entry goes, what the sweep takes and which source an icon comes from; `IconExtractionPolicy` owns the backoff schedule and the `E_PENDING` test. Both are in Core and tested. What stays in `IconService` is what genuinely needs the framework — `BitmapImage`, `StorageFile`, the `SemaphoreSlim` gate and the in-flight dictionary. The `IsRetryable` half is the piece that shipped wrong once; `ClearAll` deletes every file in the directory, which is worth a test before it is trusted.

**A property setter must not touch the disk.** `AppItemViewModel.FilePath` / `CustomIconPath` used to call `IconService.InvalidateCache` from their setters, so assigning a string deleted a file — invisible at the call site, and enough to make the view model unusable in a test. `EditItemDialog.ApplyTo` is the only writer of either; it captures both, applies the edit, and invalidates once if either moved.

**`E_PENDING` from the shell is a retry request, not a failure.** `GetThumbnailAsync` does not queue overlapping extractions — it serves the first and answers `0x8000000A` to everything else, and on a cold cache a folder's worth of tiles realize together. Measured at four tiles: three were refused before the winner's thumbnail even arrived. Swallowing it left those tiles on the fallback glyph *permanently*, because `LoadIconAsync` only runs on container realization and nothing re-runs an extraction — the icons came back only after a manual cache reset. `ExtractionGate` (a `SemaphoreSlim(1)`) removes the contention at the source and a bounded backoff covers a genuinely cold shell, which can answer `E_PENDING` with nothing else in flight. The gate never touches the warm path: `GetIconAsync` only extracts when the cache file is absent.

**Cache entries carry no extension, and the size request is not arbitrary.** `GetThumbnailAsync` hands back an uncompressed 32bpp BMP, so the `.png` these files used to be named was a format the bytes never had; nothing read it by extension anyway, since `SetSourceAsync` sniffs through WIC. Dropping the suffix also drops the only pattern `ClearCache` could filter on, which is why that sweep is now unfiltered — the directory is ours alone, and taking everything is what collects `.tmp` debris and the pre-rename `.png` entries no lookup can reach. There is no migration beyond that button. The request is `ThumbnailMode.SingleItem, 48, UseCurrentScale`: 48 because it is both the largest place the icon is drawn (the tile; search rows are 20) and a native icon band, so no resampling; `SingleItem` because `ListView` mode is the one scoped to ≤40; `UseCurrentScale` because `requestedSize` is physical pixels. The scale is baked in at extraction and the key does not include it, so changing display scale leaves every entry at the old resolution with nothing to invalidate it — the reset button is the only way out.

**The same icon is routinely asked for twice at once**, so `IconService` keeps a `ConcurrentDictionary` of running extractions and hands every caller the one task. An add loads eagerly *and* the container realizing for that same item loads again; a search row and a grid tile are the same view model. On a cold cache both calls used to reach `ExtractAndCacheIconAsync`, the loser of the write hit a sharing violation, swallowed it and returned null — a freshly dropped app came up iconless about half the time. For the same reason the cache write goes to a `.tmp` and is renamed into place: `File.Exists(cachePath)` *is* the "is it cached?" test, and an in-place write passes that test while the file is still filling.

**Icon teardown has two levels and they are not interchangeable.** Leaving the screen releases the bitmap (`ReleaseIcon`, driven by container recycling and `ReleaseHiddenIcons`); being deleted must also drop the disk cache, because it is keyed by `AppItem.Id` and nothing on disk remembers that id once the item leaves the workspace JSON. `DiscardIcons` is the deletion pair, called from `DeleteAppsAsync` and `DeleteFolderAsync` (a deleted folder takes its items with it), and workspace deletion reads the ids out of the workspace file *before* deleting it. Import and "copy current workspace" preserve item ids, so two workspaces can share cache entries; deleting one then evicts the other's, which costs a re-extraction and nothing else — not worth refcounting across workspaces.

**A recycle notification is not proof the item left the screen.** The icon lives on the view model, which is shared by every container that ever shows it, so `AppGridView_ContainerContentChanging` must not release straight from the `InRecycleQueue` branch — a drag reorder takes the item out of the collection and puts it back, and the new container's realization and the old one's recycling land in the same layout pass in no fixed order. Releasing there blanked a tile that was on screen, and only *sometimes*, because the realization's own `LoadIconAsync` usually finished after the release and quietly put the icon back. `ScheduleIconRelease` defers to the dispatcher and re-checks `ContainerFromItem`; `LoadIconAsync` stamps a generation that `ReleaseIcon` bumps, so an in-flight load cannot undo a release either.

### WinUI behaviours found by testing

These are not in the documentation, and each one on its own is enough to break the feature it belongs to.

**Alt-modified accelerators never fire.** `Alt+Enter` arrives as `WM_SYSKEYDOWN`, which the accelerator engine does not route; it is handled in `LaunchOrEditAsync` via `e.KeyStatus.IsMenuKeyDown` inside the lists' `PreviewKeyDown`. `Alt+Left` has a second problem on top — `ListViewBase` marks arrow keys and `PointerPressed` handled for its own use — so both back gestures are registered in `InitializeNavigationHistory()` with `AddHandler(..., handledEventsToo: true)`.

**`AutoSuggestBox` swallows Down and Esc** for its suggestion list, so `SearchBox` must use `PreviewKeyDown`. A bubbling `KeyDown` there is dead code.

**`AutoSuggestBox.Focus()` does not select the existing query**, whatever `FocusState` it is given — it leaves the caret collapsed after the old text, so typing appends. `FocusSearchBox()` therefore selects on the inner `TextBox`, reached with `FindDescendant<T>` since the part lives in the control template.

**Flyouts do not suppress `RootGrid`'s accelerators.** Popups live on `PopupRoot`, off the focus chain. `TrackAsModal` folds flyout `Opened`/`Closed` into `_modalDepth`.

**`DragEventArgs.OriginalSource` is the ListView, not the row under the pointer** — unlike `Tapped` / `RightTapped`, where `FindParent<ListViewItem>` is correct. Rail drops hit-test instead: `VisualTreeHelper.FindElementsInHostCoordinates(e.GetPosition(null), SidebarListView)`, first `ListViewItem` wins. Do not "simplify" this back to `FindParent`.

**`CanReorderItems` on the rail hijacks an app drag.** WinUI reads it as cross-list item transfer, parts two rows for an insertion line, and the resulting gap contains no `ListViewItem` for the hit test to find. `AppGridView_DragItemsStarting` clears it and `AppGridView_DragItemsCompleted` (which also fires on cancel) restores it. `AllowDrop` stays on throughout, and folder reordering is unaffected — a folder drag never passes through `DragItemsStarting`.

**`ColorAnimation` needs `EnableDependentAnimation = true`**, and `Storyboard.SetTarget` needs an *element* plus a full property path (`"(UIElement.RenderTransform).(ScaleTransform.ScaleX)"`). Targeting a transform object directly compiles and silently animates nothing.

**`ContentDialog` does not inherit the element theme.** It is hosted on the popup layer, outside the tree carrying `RequestedTheme`, so `ShowModalAsync` sets it explicitly — which is why every dialog routes through there rather than calling `ShowAsync()` directly.

**`Application.RequestedTheme` can only be set before the first window exists**, so it cannot serve a toggle. `ThemeService.Apply` writes `FrameworkElement.RequestedTheme` per root and `App.ApplyTheme` fans it out. `MicaBackdrop` and the system caption buttons both follow the root's actual theme, so neither needs its own pass.

**No OS call returns a window's corner radius.** `DWMWA_WINDOW_CORNER_PREFERENCE` reads back as `DWMWCP_DEFAULT` — a preference, not a measurement — so `WindowCornerRadius` takes WinUI's `OverlayCornerRadius` (8) instead.

**`AppWindow.Resize` and every `AppWindow.TitleBar` inset are in physical pixels**, while every XAML dimension is in DIPs. Divide by the scale. `XamlRoot.RasterizationScale` is not available in the constructor, so initial sizing uses a `GetDpiForWindow` P/Invoke.

### Subsystems

**Global hotkey.** `RegisterHotKey` delivers `WM_HOTKEY` to a window's wndproc, which WinUI does not expose, so `HotkeyService` subclasses the main HWND with `SetWindowSubclass` (comctl32) — the HWND is stable for the whole process, since closing to the tray hides rather than destroys. The `SUBCLASSPROC` delegate must be held in a **static** field (unmanaged code keeps no managed reference), and `MOD_NOREPEAT` must be OR'd into the modifiers or holding the chord toggles the window repeatedly. Summoning also focuses the search box, **queued** on the dispatcher — focus set before the window is really foreground does not stick. Only the hotkey does this; the tray icon deliberately does not.

**Keyboard — two mechanisms, chosen by key shape.** Ctrl-modified keys are `KeyboardAccelerator`s on `RootGrid` (`Ctrl+,` and `Ctrl+1..9` are built in `InstallCodeAccelerators()` — comma has no named `VirtualKey`). Unmodified keys — Esc, F2, Delete, Down, Enter — are handlers on the specific control. **Do not unify these:** a global unmodified accelerator also fires while `SearchBox` has focus, so a global `Delete` would prompt to delete apps while the user is editing a query.

`_modalDepth` (`MainWindow.Accelerators.cs`) is the reentrancy guard, since WinUI allows one `ContentDialog` at a time. Route dialogs through `ShowModalAsync`, whole flows through `RunModalAsync`, transient menus through `ShowTrackedFlyout`, and open every accelerator handler with `if (IsModal) return;`.

**Dragging tiles onto the rail.** `AppGridView_DragItemsStarting` stashes the payload in `_draggedApps` — a `ListViewBase`'s internal reorder package has no public reader, and that field is how the rail tells an app drag from its own folder drag. When it is null, `SidebarListView_DragOver` returns **without touching `AcceptedOperation` or `Handled`**, which is what leaves folder reordering working. `_draggedApps` is cleared in `DragItemsCompleted`, which fires *after* `Drop`.

The target row highlight is a `Border` behind the row content, driven by `FolderViewModel.IsDropTarget` (session-only, absent from `ToModel`). The ungrouped row has no view model, so its `Border` is toggled by name from code. **Do not** set `Background` on the container instead — the row's fills belong to `ListViewItemPresenter` and hand-written values fight its rest / hover / selected states. `MainWindow.Events.cs` tracks the lit row in `_railDropFolder` + `_railDropIsUngrouped` (two fields, because null alone cannot distinguish "the ungrouped row" from "nothing"). A drop onto the folder already on screen is refused, since `MoveAppsTo` removes and re-appends and would silently reorder the visible view.

**Design layer.** `Tokens.xaml` carries spacing / radii / type only — **no colors**; every color lives in `Brushes.xaml` under `ThemeDictionaries` with `Light`, `Dark`, and `HighContrast` declared explicitly (never `Default`). `{StaticResource}` inside a theme dictionary, `{ThemeResource}` at the usage site. The main window uses two type sizes, 14 and 12; don't reintroduce loose `FontSize` literals.

**Color = context.** The only hues are workspace color (the shell) and tag color (items); everything else is achromatic and the system accent is left to interactive controls. `ColorTags.GetBrush` returns the **shared** brush instance from application resources, so it is allocation-free but resolves against the theme at call time. Color keys are persisted to JSON and must never be renamed — the dialogs carry the key in `ComboBoxItem.Tag` and use `Content` for display text only.

**Workspace identity brush.** `InitializeWorkspaceBrush()` (`MainWindow.Motion.cs`) creates one `SolidColorBrush` and registers that same instance into the control-level `Resources` of both lists under the platform's own keys (`ListViewItemSelectionIndicator*Brush`, `GridViewItemSelected*BorderBrush`) and onto `WorkspaceEdge.BorderBrush`. Overriding theme keys keeps the platform's geometry, states and animations — **do not author a custom `ControlTemplate`**. It must run **before** `ItemsSource` is assigned, because `ListViewItemPresenter` resolves those keys when its template is applied. One shared instance means all three surfaces crossfade from a single `ColorAnimation`.

`WorkspaceEdge` is a 2px frame around the *whole* window: the last child of `RootGrid` with `Grid.RowSpan="2"` so it draws over the title bar as one unbroken rectangle, and `IsHitTestVisible="False"` because it covers the caption buttons and resize grip. `UpdateWindowEdgeCorners()` recomputes its radius on resize, gated on `WindowIsRounded` (build ≥ 22000 **and** `OverlappedPresenter.State == Restored`). A workspace with **no** colour shows no edge at all, done by fading `WorkspaceEdge.Opacity` — **not** by pushing a transparent colour into the shared brush, which also paints the rail indicator and the selected tile border.

**The chrome row is our own `Grid`**, not WASDK's `Microsoft.UI.Xaml.Controls.TitleBar`. That control assigns the physical-pixel `RightInset` straight into a DIP `ColumnDefinition`, which at 150% scaling left ~120 DIP of dead title bar no public property could reach. `UpdateTitleBarInsets()` is the same arithmetic with the division performed; it drives **both** inset columns, because RTL and left-handed caption buttons move the strip to `LeftInset`. It writes nothing when nothing moved, so running inside `RootGrid.SizeChanged` cannot feed itself.

`SetTitleBar(AppTitleBar)` registers the whole row as one Caption rect, which leaves its children in non-client space — input still routes to them, but the cursor does not, so `UpdateTitleBarPassthrough()` registers each control individually as `NonClientRegionKind.Passthrough`. Individually, not the parent panel, so the gaps stay draggable. The back and pane-toggle buttons are ordinary `Button`s on `NavigationBackButtonNormalStyle`; back stays *visible* and toggles `IsEnabled` so the row never shifts sideways. Inactive-window dimming is a `Window.Activated` handler setting `AppTitleBar.Opacity`.

**Motion budget — two moments only:** launch pulse (180ms scale on the tile) and workspace switch (edge crossfade 220ms + content fade 120ms). Everything else is instant by design, and all of it is gated on `_animationsEnabled` (`UISettings.AnimationsEnabled`). Tile hover/press are deliberately not animated: `ListViewItemPresenter` has no VisualStateManager, so a press scale could only shrink the content inside a fill that stayed put.

**Localization — MRT answers, and only `PrimaryLanguageOverride` steers it.** The build emits `ace-run.pri` next to the exe and MRT loads it even unpackaged, so `ResourceLoader` *does* resolve here and `x:Uid` *does* work (`SearchBox`, the Add menu, `NewFolderButtonLabel` and the empty-state button are bound that way and never touch `Loc`). `Loc.GetString(key)` therefore returns the `ResourceLoader` value in practice; the embedded `.resw` files parsed via `XDocument` are the fallback for when the `.pri` is missing.

That is why the language override has exactly one lever. MRT resolves against its own `ResourceContext` and **never reads `CultureInfo`** — setting `CurrentUICulture` steers .NET formatting and the `.resw` fallback dictionary and nothing else, which is how the setting could look wired up while every visible string still came back in the system language. `Loc.Resolve` sets `Microsoft.Windows.Globalization.ApplicationLanguages.PrimaryLanguageOverride`, which steers both the `ResourceLoader` and `x:Uid`, and it must be set before either resolves anything — hence `App.OnLaunched` calling `Loc.Initialize` before `new MainWindow()`. Changing it still needs a restart: every string is read once at construction.

All three of `win/Strings/{en-US,zh-TW,ja-JP}/Resources.resw` must be updated together. Adding a *string* needs no `.csproj` change; adding a new *language file* does, as an `EmbeddedResource` with a matching `LogicalName` — and the `.resw` tree is also what `makepri` compiles into the `.pri`, so a new language needs a matching `Strings\<tag>\` folder for the MRT half to see it.

**Single instance.** `Program.cs` uses `AppInstance.FindOrRegisterForKey("AceRun-Main")`; a second instance redirects activation to the first and exits, and the first calls `App.BringToForeground()`.

**System tray.** Initialized in `App.xaml.cs` via H.NotifyIcon. Closing hides the window when the tray icon exists **and** `CloseToTray` is on; otherwise `App.ExitApp()` disposes the icon and exits the process. `UpdateTrayContextMenu()` is public — `MainWindow` calls it after saves to refresh recent launches. The menu carries a Settings entry because it is the only way in while the window is hidden.

### Notable Capabilities

- `runFullTrust` — required for launching external processes
- `ExtendsContentIntoTitleBar = true` + `TitleBarHeightOption.Tall` — seamless Mica backdrop into the title bar
- Chrome is a single row with seven columns: `LeftInset → Back → PaneToggle → WorkspacePicker → Search(*) → Add/⚙ → RightInset`. Search is the one star column, which is what centres it in the *window*; it has a `MaxWidth` (720) and deliberately **no** `MinWidth`, which would beat the column and push the field under the workspace picker at the 720 DIP minimum window size.
- Rail is a `SplitView` (not `NavigationView` — reordering needs `CanReorderItems`), switched Inline↔Overlay from `RootGrid.SizeChanged` at 800 DIP (`RailCollapseWidthDip`). Overlay needs an opaque `PaneBackground`; Inline lets the Mica through. A `VisualStateManager` on the root of a bare `Window` is unreliable, hence the code-behind.
- Window is sized in the constructor before it is shown (1120×760 DIP default, 720×480 minimum), clamped to the current monitor's work area
- `.lnk` resolution on drag-and-drop via `WScript.Shell` COM; `.url` Internet Shortcuts via `UrlUtil.ReadInternetShortcut`
- Drag-and-drop accepts `StorageItems`, `WebLink`, and `Text` — in that priority order, so a browser offering several formats adds the link once
- Admin launch via `ProcessStartInfo.Verb = "runas"` (App items only)
- URL / custom-protocol launch via `UseShellExecute` — `steam://`, `mailto:`, `ms-settings:` all work
- App icon embedded as `EmbeddedResource` (`ace_run.Assets.app-icon.ico`) for tray use at runtime
