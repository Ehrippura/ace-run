# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

It records **rules and the reasons behind them** — the things that look like cruft and are not. Deeper forensics live in the code comments next to the code they explain; don't duplicate them here.

## Working Rules

- After finishing edits, do not automatically commit changes unless the user explicitly asks for a commit.
- Commit messages should describe only the purpose and result of the change, without excessive implementation details.
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

**`-p:Platform` is required** because the project supports `x86`, `x64`, and `ARM64` only. `PublishProfile` takes a profile name from `win/Properties/PublishProfiles/`, not a file path.

Available publish profiles:

| Profile | Bundles | Target needs |
|---|---|---|
| `self-contained` | .NET **and** the Windows App SDK (~263 MB) | nothing — this is the one users get |
| `environment-deps` | neither (~41 MB, no ReadyToRun) | .NET 10 Desktop Runtime **and** WindowsAppRuntime 1.8 preinstalled |

Profiles default to x64 Release publishing. Override the platform, runtime, or output directory with command-line properties when needed. Trimming is disabled because WinUI 3 relies on XAML reflection.

## Data Storage

All data lives under `%LOCALAPPDATA%\AceRun\`:

| File/Dir | Purpose |
|---|---|
| `config.json` | `WorkspaceConfig` — workspace list, active/default workspace ID, window state, `AppSettings` |
| `workspaces/<guid>.json` | Per-workspace `AppData` — ungrouped items, folders, recent launches |
| `icons/<guid>` | Cached app icons (keyed by `AppItem.Id`), extensionless — see "Icon loading" |
| `apps.json.bak` | Migration backup from pre-workspace format |

The first launch creates or migrates the configuration and ensures it contains a usable workspace. New fields should remain backward-compatible through their defaults.

Configuration, workspace data, and import/export files use the same JSON settings. Persisted units must be represented by explicit keys when their meaning changes.

The core data store accepts a configurable root so the full persistence layer can be tested in isolation. Writes use atomic replacement to protect configuration and workspace data from partial files.

## Architecture

### Layers

The repository is split into three projects plus documentation and assets. `core/` holds logic that does not need WinUI, `win/` is the WinUI app, and `test/` covers the core project without referencing the app. `doc/` contains the specification and change logs, while `img/` contains project images.

```
core/                     # Core domain models and services; no WinUI dependencies.
test/                     # xUnit tests for core behavior; never references win/.
win/                      # WinUI 3 application, UI, platform integrations, and resources.
doc/                      # Product specification, implementation phases, and change logs.
img/                      # Project images and other visual assets.
```

### Testability — two rules hold the split open

The test project references only `AceRun.Core`; core logic must not depend on WinUI, Windows App SDK services, or the app project. Shared interfaces keep search and organization logic independent from view-specific types.

The main window owns UI state and persistence timing, while core services own data transformation and domain rules.

### Data flow

Workspaces contain folders, ungrouped items, tags, and recent launches. The UI presents the active workspace through the sidebar, item views, and search results.

Changes are persisted through the core data services. The UI coordinates when to save and keeps the tray and denormalized workspace metadata in sync.

Item order is preserved as part of the workspace data. Organize and drag-and-drop operations update that order without introducing a separate sorting state.

### Invariants — things that look wrong and are not

**Ungrouped items are not a folder.** The ungrouped view is a separate UI collection and must not be serialized as a folder.

**Folder navigation has one owner.** All navigation entry points should use the shared navigation and history flow so selection, search state, and back history remain consistent.

**Settings have one source of truth.** The main window owns the live configuration; the settings window edits that instance rather than loading a separate copy. Defaults must preserve existing behaviour.

**Recycled rows must be data-bound.** Use bindings that update when a container is reused. Do not keep row state in one-time `Loaded` handlers, and build row menus and flyouts when they are opened.

**Edits must target the original item.** Capture the view model when editing starts and verify it is still the row's data before committing changes.

**Tags are shared instances.** Persist tag IDs, while item view models reference the workspace's shared tag objects so renames and colour changes propagate consistently.

**Item kinds are fixed at creation.** An item remains either a file or a URL; kind-specific editing and launching must respect that type.

**Icon cache state is separate from view state.** View model property setters must not perform disk I/O. Cache writes should be atomic, concurrent requests should share work, and deleted items must release their cached icons.

### WinUI behaviours found by testing

Keyboard events that controls consume must be handled at the control level, especially search navigation and Alt-modified commands. Flyouts and dialogs are modal surfaces and must receive the active app theme.

Drag-and-drop handlers must distinguish app moves from folder reordering and use the actual rail item under the pointer. Do not change the existing hit-testing or reorder ownership without testing both paths.

Animations that target dependent properties must explicitly enable dependent animation and target the owning element.

Dialogs and flyouts do not inherit the window theme automatically; apply the active theme explicitly to every popup. Theme changes must update each window root, caption buttons, and theme-specific colour lookups.

Window sizes and title-bar insets use physical pixels, while XAML layout uses DIPs. Keep conversions in the shared geometry helpers and update minimum sizes when the window changes display DPI.

### Subsystems

**Global hotkey and keyboard.** Global hotkeys use the platform integration layer. Keep text-editing keys local to their controls, and guard accelerators while modal surfaces are open.

**Dragging.** App and folder reordering share controls but have different ownership. Preserve the existing separation and keep drop highlights in the content layer rather than overriding container backgrounds.

**Design layer.** Keep spacing, typography, and colours in their designated resource files. Persisted colour keys are stable identifiers and must not be renamed. The UI uses workspace colour for shell identity and tag colour for item identity.

**Workspace identity.** The selection indicator, selected tile border, and window edge share the workspace colour while retaining platform control templates.

The title bar is custom and must keep its interactive controls separate from the draggable caption area. Motion is limited to launch and workspace-switch feedback and respects the user's animation setting.

Localization is initialized before the first window is created. Keep all supported language resources in sync; changing the language requires a restart.

The application runs as a single instance and redirects later activations to the existing window.

The system tray owns close-to-tray behaviour and recent-launch access. Closing must either hide the window when enabled or exit the application completely.

### Notable Capabilities

- Launches executable files, URLs, and custom protocols, with optional administrator mode for applications.
- Supports workspaces, folders, tags, search, recent launches, drag-and-drop, and item reordering.
- Provides a system tray, global hotkey, configurable theme and language, and close-to-tray behaviour.
- Uses a responsive WinUI layout with a custom title bar, Mica backdrop, DPI-aware sizing, and high-contrast support.
- Accepts files, shortcuts, web links, and text drops, and caches application icons for the UI.
