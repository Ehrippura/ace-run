# Ace Run

A lightweight Windows application launcher built with WinUI 3 and C#.

Manage a list of `.exe` files and launch them quickly with custom parameters — all from a clean, modern interface.

## Features

- **Quick Launch** — Add executables and launch them with one click
- **Custom Parameters** — Set arguments, working directory, and admin mode per app
- **Drag & Drop** — Drop `.exe` or `.lnk` files directly into the window
- **Folder Grouping** — Organize apps into nested folders
- **Search** — Filter your app list instantly
- **System Tray** — Minimize to tray with quick-launch from the context menu
- **Icon Extraction** — Automatically displays each app's icon
- **i18n** — English and Traditional Chinese

## Requirements

- Windows 10 1809 (build 17763) or later
- .NET 8.0

## Build

```bash
dotnet build win/ace-run.csproj -p:Platform=x64
```

## Run

```bash
dotnet run --project win/ace-run.csproj
```
