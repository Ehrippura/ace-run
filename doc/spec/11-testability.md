# 第十一階段：可測試性與單元測試

## 1. 目標

把不依賴 UI 的邏輯集中到一個獨立的組件，並為它建立單元測試與 CI 檢查。

在此之前專案是單一 `WinExe` 專案，所有邏輯與 WinUI 混在一起，**沒有任何自動化測試**，CI 只做 build。搜尋排名、排序、返回歷程、DPI 換算這些有明確正確性要求的邏輯，只能靠手動開 app 驗證。

本階段不新增任何使用者可見的功能，也不改變任何既有行為。

## 2. 專案結構

| 專案 | 位置 | 內容 |
|---|---|---|
| `AceRun.Core` | `core/AceRun.Core/` | Models、純邏輯服務、JSON 持久化 |
| `AceRun.Core.Tests` | `test/AceRun.Core.Tests/` | xUnit 測試 |
| `ace-run` | `win/` | WinUI 應用程式，參考 `AceRun.Core` |

兩個新專案**放在 `win/` 同層而非底下**：`ace-run.csproj` 沒有任何 `<Compile>` 設定，完全依賴 SDK 預設 globbing，放在 `win/` 底下的任何資料夾都會被自動編進 app。

命名空間沿用 `ace_run.Models` 與 `ace_run.Services`，搬移不影響任何呼叫端的 `using`。

### 2.1 測試專案只參考 Core

**`AceRun.Core.Tests` 不得參考 `win/ace-run.csproj`。**

這條規則有兩個作用：測試不需要安裝 WindowsAppRuntime 就能執行（CI 的 `dotnet test` 因此不必帶 `-p:Platform`），以及強制「想測的東西就必須放進 Core」——否則邏輯可以留在 `MainWindow` 裡卻看起來像被測過了。

`AceRun.Core` 使用 Windows TFM（`net10.0-windows10.0.22000.0`）但**不參考 `Microsoft.WindowsAppSDK`**。Windows TFM 是因為 `HotkeyBinding` 用 `Windows.System.VirtualKey`，那是純 metadata 的 WinRT enum，由 TFM 隱含的 Windows SDK projection 提供，不需要 COM 啟動也不需要 UI 執行緒。

## 3. 持久化層

`DataService` 原本是全靜態類別，靜態建構式把 `%LOCALAPPDATA%` 烘進 `static readonly` 欄位並建立目錄——光是讀 `JsonOptions` 就會在磁碟留下痕跡，且無法指向別處。拆成四塊：

| 型別 | 職責 |
|---|---|
| `AceRunJson` | 共用的 `JsonSerializerOptions`。讀它不觸發任何路徑解析 |
| `AceRunPaths` | 由建構式接收 root，算出各檔案路徑。不碰檔案系統 |
| `DataStore` | 實際的讀寫與遷移。建立目錄移到真正需要的方法內 |
| `DataService` | 靜態 facade，委派給 `DataStore.Default`。呼叫端一行未改 |

另提供 `DataStore.ParseAppData` / `ParseConfig` 兩個純解析進入點，讓「檔案損毀就回預設值」的行為可以脫離檔案系統測試。

## 4. 邏輯層抽象

`AppItemViewModel` 與 `TagViewModel` 帶有 `Visibility`、`Brush` 等 WinUI 型別，直接吃 view model 的邏輯會把整個框架拉進 Core。以兩個最小介面切開：

- `ITagRef` — `Id`、`Name`
- `IAppItemView` — `Id`、`DisplayName`、`FilePath`、`Arguments`、`SortKey`、`Tags`

`Tags` 宣告為 `IEnumerable<ITagRef>`，靠共變讓 `ObservableCollection<TagViewModel>` 直接滿足，不需要投影也不會有每次呼叫的配置。

## 5. 移入 Core 的邏輯

| 型別 | 原位置 | 內容 |
|---|---|---|
| `SearchRanking` | `MainWindow.Events.cs` | 比對排名與結果排序 |
| `ItemOrdering` | `MainWindow.Organize.cs` | 四種排序鍵與 `ApplyOrder` |
| `FolderHistory` | `MainWindow.History.cs` | 返回歷程狀態機 |
| `TagOrdering` | 四處重複 | 標籤依 workspace 順序投影、正規化 |
| `RecentLaunchList` | `MainWindow.Actions/Data.cs` | 最近啟動清單的記錄與清理 |
| `WindowPlacement` / `TitleBarMetrics` / `DropGeometry` | 四處 | DPI 換算、視窗夾制與置中、落點判定 |
| `ItemFactory` / `AppDataQuery` | 兩處 | 建立項目、走訪整個 workspace |

留在 `MainWindow` 的是它真正擁有的東西：UI 狀態、事件接線、儲存時機。例如 `ItemOrdering.ApplyOrder` 回傳「是否真的移動過」，由呼叫端決定要不要 `CommitSave()`。

### 5.1 排名不再有副作用

原本 `RunSearch` 在排名過程中就把 `FolderLabel` 寫回項目。改為讓 `SearchRanking.Rank` 回傳 `(項目, 資料夾名稱)` 配對，由呼叫端指派，排名本身成為純函式。

### 5.2 一處等價改寫

`PrimaryTagRank` 原本用 `_tags.IndexOf(app.Tags[0])`，是參考相等比對；抽出後改為以 `Id` 比對。兩者等價，因為標籤是共用實例且 `TagOrdering.Normalize` 保證每個項目的 `Tags` 維持 workspace 順序。

## 6. 消除的重複

- `AppCount` 計算（4 處）→ `AppData.ItemCount`
- 走訪所有項目（4 處）→ `AppDataQuery.AllItems` / `ItemIds`
- 標籤依 workspace 順序投影（4 處）→ `TagOrdering.InWorkspaceOrder`
- 建立 App / URL 項目（2 處）→ `ItemFactory`

行為有分歧的重複（空白名稱在 6 個呼叫點有 4 種行為）**不在本階段處理**，統一之前需要先決定哪個才是對的。

## 7. CI

`.github/workflows/ci.yml` 在 build 之後執行 `dotnet test test/AceRun.Core.Tests/AceRun.Core.Tests.csproj -c Release`。

刻意不帶 `-p:Platform`：Core 與測試專案都是 AnyCPU。哪天這一步需要 app 那套平台參數，就代表有 WinUI 的東西漏進邏輯層了。

## 8. 不在本階段範圍

以下已確認可行，留待日後：

- **`ColorTags` 的型別初始化毒化** — `SolidColorBrush` 靜態欄位使純資料的 `Keys` 也無法在沒有 XAML runtime 時讀取。拆出 `Keys` 即可解毒
- **`IconService`** — 退避重試狀態機與 `E_PENDING` 判別值得測試，但與 WinRT 縮圖擷取焊在一起；`ClearCache` 是無過濾的整目錄掃除，尤其該有測試
- **`AppItemViewModel` 的 setter 會動磁碟** — `FilePath` 與 `CustomIconPath` 的 setter 呼叫 `IconService.InvalidateCache`，設一個字串屬性就會嘗試刪檔。需要 `IIconCache` 接縫
- **`Loc`** — 可抽出語言標籤解析與 `.resw` 解析；它會改寫行程層級的 `CultureInfo`
- **`StartupService`** — HKCU 讀寫無注入接縫
- 空白名稱處理的行為統一、確認 flyout 的重複、`GetDpiForWindow` 的 P/Invoke 重複宣告
