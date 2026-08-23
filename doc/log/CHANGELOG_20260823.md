# Changelog — 2026-08-23

本文件記錄 2026-08-15 之後（CHANGELOG_20260815.md 之後）的所有新功能、改善項目與 Bug 修正。

本次沒有新功能，是一輪相依套件升級。動機不是想要新 API，而是支援期限：Windows App SDK 1.8 於 **2026-09-09** 停止服務，`1.8.260804001` 已是該線最後一版，2.x 是唯一還在 Current 的線。

---

## 建置與相依

- **Windows App SDK 1.8.260209005 → 2.4.0。零程式碼變更**，只有 `win/ace-run.csproj` 的版本號一行。x86 / x64 / ARM64 三平台建置皆 0 警告 0 錯誤，既有測試全數通過。
- 2.0 的主版號跳躍是改採 Semantic Versioning、讓 SDK 版本號與 NuGet 版本號對齊，不是重新架構：WinUI 仍是 WinUI 3，沒有 namespace 改名。`TargetFramework`（`net10.0-windows10.0.22000.0`）與 `TargetPlatformMinVersion`（`10.0.17763.0`）都不必動——兩版 `Microsoft.WindowsAppSDK.Foundation` 的 lib TFM 同樣是 `net6.0-windows10.0.17763.0`。
- 2.3.1 起 `DISABLE_XAML_GENERATED_MAIN` 的語意從「移除」生成的 `main()` 改為「改名」。本專案定義了這個常數且 `win/Program.cs` 自己實作 `Main`，所以特別驗證過：改名後是 `internal static class XamlGeneratedProgram` 的 `XamlGeneratedMain()`，不會產生第二個進入點。
- 2.3.1 引進的大批 XAML 效能最佳化走 `XamlOptionalChanges` 機制、**預設關閉**（唯一的 ID `Perf2026` 需明確 opt-in），所以既有 UI 不受影響。
- **H.NotifyIcon.WinUI 2.1.3 → 2.4.1。** `TaskbarIcon` 的公開介面在這兩版之間完全沒有變化，升級換到的是修復：主要是 `PrepareContextMenuWindow` 建立 `TaskbarIcon` 時可能拋出的 `COMException 0x8001010E`（.NET 9 起回報）——`App.InitializeTrayIcon` 的 try/catch 會把它降級成托盤圖示直接消失。套件同時改為原生 net10 目標。

## 發佈

- **`environment-deps` 的目標機器需求從 WindowsAppRuntime 1.8 變成 2.x。** 1.x 線每個小版本一個 framework package（`Microsoft.WindowsAppRuntime.1.6`、`.1.7`、`.1.8`，並存且互不替代），2.x 收斂成單一個 `Microsoft.WindowsAppRuntime.2` 原地服務更新。1.8 的機器跑不了 2.4 的建置。
- 這項需求原本只寫在 `CLAUDE.md`，`environment-deps.pubxml` 本身一行註解都沒有——最容易漏掉的地方。已在該檔補上。
- 體積重新量測（x64 Release）：

  | 設定 | 1.8 | 2.4 |
  |---|---|---|
  | `self-contained` | 264.8 MB / 518 檔 | **275.3 MB / 526 檔** |
  | 只打包 WASDK（`SelfContained=false`） | 154 MB | **168 MB** |
  | `environment-deps` | 42.3 MB / 54 檔 | **82 MB / 59 檔** |

- `environment-deps` 幾乎翻倍，而且沒有一位元組是這個 app 的：2.x 把 Windows ML 從 framework package 搬到 app-local，於是 `onnxruntime.dll`（20.7 MB）與 `DirectML.dll`（17.8 MB）出現在每一次 framework-dependent 發佈裡。已比對 1.8 的同設定輸出確認這兩個檔案原本不存在。本專案完全不碰 Windows ML，這 38.5 MB 純屬死重。`Microsoft.WindowsAppSDK.ML.targets` 有 `WindowsAppSDKMLPassthroughOnnxRuntime=true` 可抑制該複製，**尚未驗證，也刻意未設定**。

## 文件

- `CLAUDE.md` 的 Tech Stack 與發佈設定檔表格更新；順帶修正 `H.NotifyIcon.WinUI` 版本（表上仍寫 2.1.3，實際已是 2.4.1）。
- `README.md` 的 self-contained 體積更新；順帶修正發佈指令——原本寫的 `-p:PublishProfile=win/Properties/PublishProfiles/FolderProfile.pubxml` 指向不存在的檔案，`PublishProfile` 吃的是設定檔名而非路徑（`CLAUDE.md` 的版本是對的，兩處不一致）。

## 已評估但未採用

- **2.5.0 的深色托盤選單。** H.NotifyIcon 2.5.0-beta.1 / dev 新增 `TaskbarIcon.ContextMenuThemeMode`，讓原生 `PopupMenu` 路徑跟隨深淺主題。本專案用的正是 `ContextMenuMode.PopupMenu`，托盤選單在深色模式下仍是白底、與其他表面不一致——但那兩版都是 prerelease，作者明說要先回報 regression 才會轉正式版。
- **SDK 的 `TitleBar` 控制項。** 2.1.3 新增 `IsDragRegion` / `AutoRefreshDragRegions` / `RecomputeDragRegions()`，會自動排除互動控制項。本專案當初因 `RightInset` 的物理像素／DIP bug 而改走自繪路線，官方 release note 未提及該 bug 是否一併修正，需另行驗證後才談得上回歸官方控制項。
