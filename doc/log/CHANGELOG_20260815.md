# Changelog — 2026-08-15

本文件記錄 2026-08-12 之後（CHANGELOG_20260812.md 之後）的所有新功能、改善項目與 Bug 修正。

本次沒有新功能。整輪工作是一類 bug 的清查與修正：**凡是在建構期算一次、之後就凍結的 DPI 換算值**。起點是一個提問——視窗在兩台縮放不同的螢幕之間移動時，`PreferredMinimumWidth` / `Height` 並不會重新計算——順著查下去發現同源的問題有三處。

全程以雙螢幕實測驗證（DISPLAY1 1920×1200 @ 100%、DISPLAY2 3840×2160 @ 150%），下列每個數字都是量到的，不是推導的。

---

## 1. 主視窗的最小尺寸凍結在啟動時的螢幕

`OverlappedPresenter.PreferredMinimum*` 存的是實體像素，而 **Windows 在 DPI 變更時不會重新換算它**。`ApplyInitialWindowSize()` 在建構子裡設一次就不再更新，於是那組像素永遠代表「啟動當下那台螢幕」的縮放。

實測（以要求一個極小尺寸、讀回被 `WM_GETMINMAXINFO` 夾到哪裡的方式量測）：

| | 修正前 | 修正後 |
|---|---|---|
| 1.5 螢幕（啟動處） | 720×480 DIP ✓ | 720×480 DIP |
| 搬到 1.0 螢幕 | **1080×720 DIP** ✗ | 720×480 DIP |
| 搬回 1.5 螢幕 | 720×480 DIP ✓ | 720×480 DIP |

在 1920×1200 的那台上，1080×720 的下限是螢幕寬的 56%、工作區高的 62%——視窗根本縮不下來。反方向則掉到 480×320 DIP，低於標題列版面設計的寬度。

修法是把那三行抽成 `ApplyMinimumSize(scale)`，在 DPI 變更時重跑。**不補 `Resize`**：下限是約束不是尺寸，把視窗從使用者擺好的大小拉走更糟。

### 下限過期不只是數字不對，它會弄壞跨螢幕本身

第一版用 `XamlRoot.Changed` 掛這件事，程式化測試（`SetWindowPos` 跨螢幕）兩個方向都通過，看起來就修好了。實際用滑鼠拖曳卻不是——這是使用者回報後才查出來的。

`WM_DPICHANGED` 會把視窗按 DPI 比例縮放，而**那次縮放會被當下的下限夾住**。所以往低 DPI 螢幕縮小時會落錯位置。以合成拖曳實測：933×600 DIP 的視窗從 150% 拖到 100%，出來是 **1080×720 DIP**——被它離開的那台螢幕的下限釘住了。放大方向不受影響（太小的下限夾不到任何東西），這正是這個 bug 呈現出的不對稱。

`XamlRoot.Changed` 跑在 dispatcher 上，**在縮放之後**才到。程式化的 `SetWindowPos` 測不出來，因為那條路上 dispatcher 有機會先跑完；只有真正的拖曳會失敗，因為訊息是在 modal move loop 裡送達的。

改用 `WM_DPICHANGED`：它在縮放**之前**送出，wParam 帶新 DPI、lParam 帶建議矩形，在處理函式裡改掉的約束，`DefSubclassProc` 套用縮放時就已經生效。新增 `Services/DpiChangeWatcher.cs`，以 `SetWindowSubclass` 掛在 HWND 上（subclass id 與 `HotkeyService` 錯開，兩者都掛在主視窗）。它是 per-window 的並提供 `Detach`——設定視窗會真的被銷毀，而 HWND 會被回收。

合成拖曳的驗收（三種情境，皆守住 DIP）：

| 情境 | 拖曳前 | 拖曳後 |
|---|---|---|
| 中等尺寸 1.5 → 1.0 | 1400×900 DIP | 1400×900 DIP |
| 再拖回 1.5 | 1400×900 DIP | 1400×900 DIP |
| 貼著下限 1.5 → 1.0 | 720×480 DIP | 720×480 DIP |
| 貼著下限 1.0 → 1.5 | 720×480 DIP | 720×480 DIP |

## 2. 設定視窗，同源但不用拖曳就會發生

設定視窗由 OS 生在它挑的螢幕上，再由 `CenterOverOwner` 移到主視窗那台。主視窗若在 1.0 螢幕，這扇窗生在 1.5 主螢幕（實測 `createdOnScale=1.5 ownerScale=1`），於是建構期算出的每一個值都用錯了縮放。

| | 修正前 | 修正後 |
|---|---|---|
| 尺寸 | **840×680 DIP**（設計 560×680） | 560×680 DIP |
| 最小尺寸 | **840×630 DIP** | 560×420 DIP |
| 位置 | (4120,444)，垂直被工作區頂端夾住 | (4260,540)，精確置中 |

值得記下的是**尺寸本身的算法原本是對的**：`560×1.5=840` 到站後被 Windows 乘 `2/3`，正好落回 560。壞掉的純粹是最小尺寸不隨之縮放，把寬度夾在 840 出不來。所以三個值各有各的正確 scale：

- **尺寸**用自己當下的（改用擁有者的會把比例套兩次）
- **最小尺寸**與**置中**用擁有者的（那是目的地）

之後使用者自己拖動設定視窗，同樣由 `DpiChangeWatcher` 負責。`WM_DPICHANGED` 由 `AppWindow.Move` 內部同步送出，所以它連建構期那次搬移也涵蓋得到；用擁有者 scale 預設一次仍然保留，因為那讓「目的地」寫在程式碼裡而不是靠讀者從「訊息會在建構子中途抵達」推論出來，而且兩台螢幕同縮放、根本不送訊息時它是唯一正確的來源。

## 3. `WindowState` 改存 DIP

`SaveWindowSize` 原本直接存 `AppWindow.Size`（實體像素），檔案裡沒有任何欄位記得那是在哪個縮放下量到的。在 1.0 螢幕調好的大小，下次於 1.5 主螢幕啟動就還原成 2/3 的邏輯尺寸。

改存 `WidthDip` / `HeightDip`。單位改變靠**鍵名**而非版本號區分——這點是查證後改的設計：`SaveConfig` 每次寫入都會蓋上 `CurrentVersion`，而啟動修復（`MigrateOrInitialize` → `EnsureUsable`）與每一次即時套用的設定變更都會觸發它，版本閘門會在尺寸重存之前就把像素值標記成 DIP，且無法復原。把換算提前到資料層也不行：那一層沒有螢幕可問，而 `LoadConfig` 的其他呼叫端不該知道 DPI。

舊檔的 `Width` / `Height` 保留為唯讀的遷移輸入（`JsonIgnoreCondition.WhenWritingDefault`，新檔不再寫出），以當前 scale 換算一次。**升級後第一次啟動的視窗實體大小完全不變**，這是遷移的驗收條件，有測試守著。

## 4. `ToPixels` / `ToDip`：唯一的換算入口

實作前查證過 Windows 是否有內建的 DIP↔像素換算可用，**沒有**：

- Win32 的 27 個 DPI 函式全是查詢（`GetDpiForWindow`）、awareness 類、或 per-struct 調整（`AdjustWindowRectExForDpi`）。唯二像換算的 `LogicalToPhysicalPointForPerMonitorDPI` 與其反向吃的是 `POINT`——錨在螢幕原點的座標，換寬高在數學上就不對。
- WinUI（反射列舉 `Microsoft.WinUI.dll` 全部成員）只有 `RasterizationScale` 這個係數，沒有任何套用它的方法，也沒有 `DisplayInformation`。

既然只能自己寫，兩個方向就收斂到 `WindowPlacement` 上，讓**取整規則只定義一次**：尺寸存檔轉 DIP、還原轉回像素，兩邊各自截斷會每輪掉一個像素。用 `Math.Round` 而非截斷，測試以 320–1400 DIP × 五種縮放窮舉 round-trip。

## 其他

- 新增 `win/Services/DpiChangeWatcher.cs`——`WM_DPICHANGED` 的 per-window 訂閱點。
- 移除 `MainWindow.xaml.cs` 中四個零引用的 `private const`（`DefaultWidthDip` / `DefaultHeightDip` / `MinWidthDip` / `MinHeightDip`）——邏輯搬進 `WindowPlacement` 之後的殘留，數值與 Core 那份一致但沒有東西保證同步。
- `SettingsWindow` 的最小高度 420 由字面值改為具名的 `MinHeightDip`。
- 測試 **286 → 296**。

## 相關規格

- [doc/spec/5-workspaces.md](../spec/5-workspaces.md) — `WindowState` 的單位與遷移
- [doc/spec/10-settings.md](../spec/10-settings.md) — 設定視窗建構期的兩個 scale
- [doc/spec/11-testability.md](../spec/11-testability.md) — `ToPixels` / `ToDip` 的位置與理由
