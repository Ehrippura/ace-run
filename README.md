# Ace Run

A lightweight Windows application launcher built with WinUI 3 and C#.

Manage a hierarchical list of `.exe` files and URLs across multiple workspaces and launch them quickly with custom parameters — all from a clean, modern interface.

## Features

- **Quick Launch** — Add executables and launch them with one click
- **URLs & Protocols** — Add web links alongside your apps; custom protocols like `steam://`, `obsidian://` and `ms-settings:` work too
- **Custom Parameters** — Set arguments, working directory, and admin mode per app
- **Drag & Drop** — Drop `.exe`, `.lnk` or `.url` files into the window, or drag a link straight from your browser; silently added with sensible defaults
- **Folder Grouping** — Organize apps into folders with drag-to-reorder; drag tiles onto a sidebar folder to move them there, and go back to the previous folder with `Alt+Left` or the mouse back button
- **Organize** — Right-click a folder in the sidebar to sort it in one pass by name, path, tag, or a custom sort key — or reverse it. The result becomes the new manual order, so drag-to-reorder keeps working afterwards
- **Tags** — Give an app any number of color-coded tags; tag names are searchable, and a folder can be sorted by tag
- **Multiple Workspaces** — Switch between independent app lists, each with its own folders, tags, and accent color; export/import as `.acerun` files
- **Multi-Select** — Select several items and launch, tag, move, delete, or drag them onto a folder in one go
- **Search** — Instantly filter by name, path/URL, launch arguments, or tag name, ranked with recently launched items first; every result shows its folder and can jump to it
- **System Tray** — Minimize to tray, quick-launch recent apps from the context menu
- **Single Instance** — Re-launching brings the existing window to the foreground instead of opening a second copy
- **Icon Extraction** — Automatically displays and caches each app's icon
- **Keyboard Shortcuts** — Drive the whole app from the keyboard; see below
- **Global Shortcut** — Bind a key combination that summons the window from anywhere and puts it away again
- **Settings** — Global shortcut, start with Windows, close-to-tray, hide-after-launch, theme, and language, in their own window
- **Fluent Design** — Mica backdrop with the workspace color framing the window; light, dark, and high-contrast themes
- **i18n** — English, Traditional Chinese, and Japanese

## Keyboard Shortcuts

| Shortcut | Action |
|---|---|
| `Ctrl+F` / `Ctrl+E` | Focus the search box |
| `Enter` (in search) | Launch the top result |
| `Down` (in search) | Move focus to the results list |
| `Esc` | Clear the search, then close the overlay rail |
| `Ctrl+N` | Add an app |
| `Ctrl+Shift+N` | Add a URL |
| `Ctrl+Alt+N` | New folder |
| `Ctrl+B` | Toggle the sidebar |
| `Alt+Left` / mouse back button | Go back to the previous folder |
| `Ctrl+,` | Open the Manage menu (workspaces / tags / settings) |
| `Ctrl+1`…`Ctrl+9` | Switch to workspace 1–9 |
| `Enter` | Launch the selected item(s) |
| `Alt+Enter` | Edit the selected item |
| `Ctrl+A` (in the item list) | Select every item in the current view |
| `Delete` | Delete the selected items — or the selected folder, in the sidebar |
| `F2` | Rename the selected folder (sidebar) |
| Your global shortcut | Summon the window from any app; press again to hide it (off by default — set it in Settings) |

## Requirements

- Windows 10 1809 (build 17763) or later
- .NET 10.0
- Supported platforms: x86, x64, ARM64

## Build

```bash
dotnet build win/ace-run.csproj
```

## Run

```bash
dotnet run --project win/ace-run.csproj
```

## Publish

Self-contained publish (x64) — the .NET runtime ships with the app, so the target machine needs no separate install:

```bash
dotnet publish win/ace-run.csproj -p:PublishProfile=win/Properties/PublishProfiles/FolderProfile.pubxml
```
