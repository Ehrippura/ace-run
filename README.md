# Ace Run

A lightweight Windows application launcher built with WinUI 3 and C#.

Manage a hierarchical list of `.exe` files across multiple workspaces and launch them quickly with custom parameters — all from a clean, modern interface.

## Features

- **Quick Launch** — Add executables and launch them with one click
- **Custom Parameters** — Set arguments, working directory, and admin mode per app
- **Drag & Drop** — Drop `.exe` or `.lnk` files directly into the window; silently added with sensible defaults
- **Folder Grouping** — Organize apps into folders with drag-to-reorder
- **Tags** — Label apps with color-coded tags and filter/organize by them
- **Multiple Workspaces** — Switch between independent app lists, each with its own folders and tags; export/import as `.acerun` files
- **Multi-Select** — Batch delete and reorder multiple items at once
- **Search** — Filter your app list instantly, with folder context and one-click jump to an item's folder
- **System Tray** — Minimize to tray, quick-launch recent apps from the context menu
- **Single Instance** — Re-launching brings the existing window to the foreground instead of opening a second copy
- **Icon Extraction** — Automatically displays and caches each app's icon
- **i18n** — English, Traditional Chinese, and Japanese

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

Framework-dependent publish (x64):

```bash
dotnet publish win/ace-run.csproj -p:PublishProfile=win/Properties/PublishProfiles/FolderProfile.pubxml
```
