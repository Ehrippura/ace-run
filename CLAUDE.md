# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## Project Overview

Ace Run is a lightweight Windows application launcher built with WinUI 3 and C#. Users can manage a list of .exe files and launch them with custom parameters. The spec is in `doc/spec.md` (written in Traditional Chinese).

## Tech Stack

- **Framework:** WinUI 3 (Windows App SDK 1.8)
- **Language:** C# / .NET 8.0 (net8.0-windows10.0.19041.0)
- **Data Storage:** JSON
- **Process Launch:** System.Diagnostics.Process / ShellExecute
- **Target Platforms:** x86, x64, ARM64

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

## Project Structure

```
doc/spec.md              # Feature specification (Traditional Chinese)
win/
  ace-run.csproj         # Project file
  ace-run.slnx           # Solution file
  App.xaml / App.xaml.cs  # Application entry point
  MainWindow.xaml / .cs   # Main window (Mica backdrop)
  Package.appxmanifest    # MSIX packaging manifest
  app.manifest            # DPI awareness (PerMonitorV2), OS compat
  Assets/                 # App icons and logos
  Properties/
    launchSettings.json          # Debug profiles (MSIX & Unpackaged)
    PublishProfiles/*.pubxml     # Per-architecture publish configs
```

## Architecture Notes

- The project is in early stage — only the UI skeleton exists (empty MainWindow with Mica backdrop).
- Feature implementation should follow the three phases defined in `doc/spec.md`: MVP (add/list/execute/persist), Advanced Config (arguments, working dir, admin mode, CRUD), UX Polish (icon extraction, drag & drop, search, i18n, system tray).
- Nullable reference types are enabled.
- The app has `runFullTrust` capability for launching external processes.
- Minimum supported OS: Windows 10 1809 (build 17763).
