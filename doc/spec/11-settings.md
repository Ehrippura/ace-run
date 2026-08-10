# 第十一階段：設定畫面

目標：在此之前應用程式層級的行為全部寫死——關閉視窗一律縮到系統匣、主題與語言完全跟隨系統、沒有全域快捷鍵。`MainWindow.Accelerators.cs` 的 Esc 處理註解直接寫明它之所以不隱藏視窗，就是因為沒有全域快捷鍵可以喚回來，兩個缺口互相卡住。本階段補上一個獨立的設定視窗，把四組行為開關化，並以全域快捷鍵為主軸。

## 1. 資料模型

- [x] 新增 `Models/AppSettings.cs`，含三個型別：
  - `AppSettings`：`Hotkey`（`HotkeyBinding?`，null = 停用）、`StartWithWindows`、`CloseToTray`（預設 `true`，維持現行行為）、`Theme`、`Language`（`""` = 跟隨系統）、`HideOnLaunch`。
  - `AppTheme`：`System` / `Light` / `Dark`。
  - `HotkeyBinding`：`VirtualKeyModifiers Modifiers` + `VirtualKey Key`。兩者都是 WinRT enum，靠 `DataService.JsonOptions` 既有的 `JsonStringEnumConverter` 存成 `"Control, Menu"` / `"Space"` 這種人看得懂的字串，不另寫轉換器。
- [x] `WorkspaceConfig` 新增 `Settings`，`CurrentVersion` 由 1 提升至 2。舊檔缺 `Settings` 鍵時 `System.Text.Json` 保留屬性初始值，**不需要 migration 程式碼**（與第十階段 `SortKey` 同一個理由）。
- [x] 設定放在 `config.json` 而非各 workspace：這些是應用程式層級的偏好，跟著使用者不跟著工作區。
- [x] **單一擁有者**：`MainWindow` 持有 `_workspaceConfig` 並在關閉時整份寫回。設定視窗若自行 `LoadConfig()` 一份來改，主視窗關閉時那份舊的會把設定蓋掉。因此 `SettingsWindow` 由建構子接收**主視窗手上那個實例**，改的是同一個 `AppSettings` 物件。

## 2. 全域快捷鍵（`Services/HotkeyService.cs`）

- [x] `RegisterHotKey` / `UnregisterHotKey`（user32），modifier 對應 `MOD_ALT/CONTROL/SHIFT/WIN`，並一律加上 `MOD_NOREPEAT (0x4000)`——否則按住不放會連發 `WM_HOTKEY`。
- [x] `WM_HOTKEY (0x0312)` 的攔截用 `SetWindowSubclass`（comctl32）掛在主視窗的 HWND 上，不另建 message-only window：主視窗的 HWND 在整個生命週期穩定，縮到系統匣走的是 `args.Handled = true` + `AppWindow.Hide()`，視窗並未被銷毀。
- [x] `SUBCLASSPROC` delegate 以 static 欄位釘住。它被交給非受管碼保存，區域變數會被 GC 回收，之後每一則訊息都會踩到已釋放的 callback。
- [x] 綁定被其他程式占用時 `RegisterHotKey` 回傳 false；`Register` 據此回傳 bool，由呼叫端還原舊綁定並提示，不靜默失敗。
- [x] 觸發行為是 toggle：視窗在前景 → 隱藏；否則喚回。喚回沿用既有的 `App.BringToForeground()`，它已處理最小化還原與 `SetForegroundWindow`。
- [x] 喚回後焦點直接落在搜尋欄，並選取既有查詢，讓下一個按鍵就是新的搜尋——不然還要先按 Ctrl+F，快捷鍵就沒有意義了。焦點設定必須排進 dispatcher 佇列（`BringToForeground` 自己也在佇列裡做事，視窗還沒真的到前景時設焦點不會生效）。只有快捷鍵這條路徑會這樣做；點托盤圖示是手已經在滑鼠上的行為，把游標塞進輸入框反而礙事。
- [x] 選取而非清空既有查詢：只是想再看一次上次結果的喚回，畫面仍然是那些結果。`AutoSuggestBox` 沒有自己的選取 API，`Focus()` 也不會選取（不論給哪個 `FocusState`），因此要走視覺樹找到樣板裡的 `TextBox` 再 `SelectAll()`。
- [x] 預設**停用**，欄位顯示「未設定」。開箱就搶走一組全域鍵是敵意行為（Ctrl+Alt+Space 與部分輸入法衝突），且啟動階段註冊失敗時沒有 UI 可以回報。
- [x] 啟動時若儲存的組合已被別的程式占走，只是註冊不成功，不彈任何東西——此時沒有設定視窗可以承載錯誤訊息。使用者回到設定重錄時才會看到衝突提示。

## 3. 開機自動啟動（`Services/StartupService.cs`）

- [x] 寫 `HKCU\Software\Microsoft\Windows\CurrentVersion\Run`，值名 `AceRun`。本專案為 unpackaged（`WindowsPackageType=None`），不能用 `StartupTask`；HKCU 不需提權。
- [x] 執行檔路徑取 `Environment.ProcessPath`，**不是** `Assembly.Location`（single-file 發佈下後者是空字串）。路徑加引號，並附 `--tray` 參數。
- [x] `--tray` 啟動時不呼叫 `Activate()`，直接進系統匣。開機時把視窗糊到使用者臉上，等於讓這個選項不能用。
- [x] 讀取狀態時只看值是否存在；若指向其他路徑（例如搬移過執行檔），下次切換時直接覆寫成目前路徑。

## 4. 外觀

- [x] 主題（`Services/ThemeService.cs`）：把 `AppTheme` 轉成 `ElementTheme` 套到根 `FrameworkElement.RequestedTheme`。主視窗、設定視窗、三個 `ContentDialog` 都要套——dialog 掛在 popup 層，明確指定比依賴繼承可靠。即時生效。
- [x] 語言：`Loc` 的靜態建構子內容抽成 `Loc.Initialize(string? languageTag)`，於 `App.OnLaunched` 最開頭、`new MainWindow()` 之前呼叫；有覆寫時同時設 `CultureInfo.DefaultThreadCurrentUICulture`，讓 MSIX 路徑的 `ResourceLoader` 也跟著走。靜態建構子保留為無覆寫的保險。
- [x] 語言**需要重新啟動**才生效，設定視窗以 `InfoBar` 說明。即時切換得讓每個 `Loc.GetString` 呼叫點都能重跑，成本遠高於價值。

## 5. 關閉行為與啟動後隱藏

- [x] `MainWindow_Closed` 的 `if (App.TrayEnabled)` 加上 `&& CloseToTray`。`App.TrayEnabled` 的語意維持「托盤圖示確實建立成功」，不與使用者偏好混用。
- [x] 順帶修正一個既有缺陷：關閉即結束這條路徑原本只是讓視窗關掉就 return，托盤圖示仍活著、行程不會退出。改為走 `App.ExitApp()`（dispose 托盤 + `Environment.Exit(0)`）。此路徑在本階段之前不可達（`TrayEnabled` 只有在托盤初始化拋例外時才是 false），設定開關讓它變成日常路徑。
- [x] `HideOnLaunch`：`LaunchApp` 與 `LaunchApps` 的共用收尾（`PersistAfterEdit()` + `UpdateTrayContextMenu()`）之後隱藏視窗。兩者共用一個 `AfterLaunch()` 私有方法，避免只改到其中一條路徑。
- [x] 兩者都不動 Esc 的行為。Esc 隱藏視窗雖然在有快捷鍵後就成立，但那是行為變更而非設定項，另行評估。

## 6. 設定視窗（`SettingsWindow.xaml`）

- [x] 獨立 `Window` 而非 `ContentDialog`：項目分成六組，塞進對話框會過高，且即時套用的互動與另外兩個對話框的確定／取消流程不同調。
- [x] `MicaBackdrop` + `ExtendsContentIntoTitleBar`，標題列只有一個靠左的標題文字，`SetTitleBar` 整條。**不需要** `UpdateTitleBarInsets` 那套實體像素÷scale 的算術——標題列右端沒有任何控制項，caption strip 底下是空的。
- [x] 尺寸於建構子決定（560×680 DIP），沿用 `MainWindow.ApplyInitialWindowSize()` 的 DPI 手法（`GetDpiForWindow`，`AppWindow.Resize` 吃實體像素）；`IsMaximizable = false`。
- [x] 位置也在建構子決定，於主視窗上置中（`ApplyInitialWindowPlacement` → `CenterOverOwner`）。不指定位置時由 OS 的層疊規則決定，實測會落在離主視窗很遠、甚至另一個螢幕的地方。夾住結果用的是**主視窗所在螢幕**的 `WorkArea`，不是設定視窗自己的——後者反映的正是要取代的那個隨手擺放位置。主視窗最小化時位置回報為離屏值（-32000），該情況改在螢幕工作區置中。
- [x] 單一實例：`App` 持有欄位，已開啟就 `Activate()` 而非開第二扇。
- [x] 版面為 `ScrollViewer` + 手刻的設定卡（`Border` 吃 `CardBackgroundFillColorDefaultBrush` 與 `Styles/Tokens.xaml` 的圓角）。不引入 `CommunityToolkit.WinUI.Controls.SettingsControls`：為六張卡片增加專案第二個第三方相依不划算。
- [x] **即時套用，沒有確定／取消**，每次變更立刻 `DataService.SaveConfig` 並回呼主視窗重新套用。
- [x] 快捷鍵錄製欄位：按下按鈕進入錄製，於 `PreviewKeyDown` 收下一個組合鍵。忽略單獨的修飾鍵；**必須至少含一個 Ctrl/Alt/Win**（純字母當全域鍵會吃掉整個系統的輸入）；Esc 取消錄製，Delete/Backspace 清除綁定。

## 7. 觸發點與在地化

- [x] ⚙ 選單加入第三項「設定」（Segoe MDL2 `E713`）。`Ctrl+,` 維持開啟該選單不變——選單本身就是設定的入口集合。
- [x] 托盤選單也加一項「設定」：視窗隱藏時它是唯一的入口。
- [x] 三份 `.resw` 同步新增設定視窗所需字串，沿用 `Domain_Thing` 的命名慣例。

## 8. 不在本階段範圍

- [ ] Esc 隱藏視窗。
- [ ] 語言即時切換。
- [ ] 快捷鍵綁定多組（例如另一組直接開搜尋）。
