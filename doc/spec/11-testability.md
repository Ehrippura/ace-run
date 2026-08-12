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

## 8. 第二輪：拆解 WinUI 耦合服務

第一輪刻意把與 WinUI 焊在一起的服務留到後面。這一輪處理其中三個。

### 8.1 `IconService` 拆成快取層與擷取策略

| 型別 | 位置 | 內容 |
|---|---|---|
| `IconCache` | Core | `PathFor` / `Invalidate` / `ClearAll` / `ChooseSource`，純 `System.IO` |
| `IconExtractionPolicy` | Core | `BackoffMs` / `DelayForAttempt` / `IsRetryable`，純判斷 |
| `IconService` | win | `BitmapImage`、`StorageFile` 縮圖擷取、`SemaphoreSlim` 閘門、in-flight 字典 |

路徑改由 `AceRunPaths.IconsDir` 提供，磁碟配置回到單一型別描述。

兩個最該有測試的：`IsRetryable` 是曾經寫錯的判斷（把「稍後再試」當永久失敗，代價是圖示永久空白）；`ClearAll` 是**無過濾的整目錄刪除**，也是 `.tmp` 殘骸與改名前 `.png` 的唯一遷移路徑。

### 8.2 `AppItemViewModel` 的 setter 不再動磁碟

`FilePath` 與 `CustomIconPath` 的 setter 原本呼叫 `IconService.InvalidateCache` —— 設一個字串屬性就會刪檔。`EditItemDialog.ApplyTo` 是唯一寫入者，因此失效動作移到那裡，比較新舊值後呼叫一次；不需要任何依賴注入。

### 8.3 `ColorTags` 拆出 `ColorKeys`

顏色鍵清單移進 Core，`ColorTags` 只留 `GetBrush`。價值在於**顏色鍵會寫進 JSON、永遠不得重新命名**，測試把這個資料格式不變量釘死。`Default`（`"Blue"`）取代兩處寫死值。

## 9. 已修正的缺陷

| 缺陷 | 修正 |
|---|---|
| 新建工作區的預設名稱寫死英文 `"New Workspace"` | 新增 `Workspace_DefaultName`（三語言）。標籤同理新增 `Tag_DefaultName`，不再沿用按鈕標題當預設名 |
| 工作區重命名遇空白名稱留下不一致畫面 | 比照 `ManageTagsDialog` 還原輸入框 |
| 導軌閾值文件寫 900、實際是 800 | 以程式碼為準修正兩處文件 |
| `.acerun` 匯入驗證形同虛設 | 改為 `WorkspaceImport.TryParse`，見下 |

### 匯入驗證：原本的檢查幾乎是死碼

原本唯一的檢查是 `export?.AppData is null`。但 `WorkspaceExport.AppData` 帶有屬性初始值 `= new()`，`System.Text.Json` 對沒提到該鍵的檔案會保留那個空實例 —— **只有 JSON 明寫 `"AppData": null` 才會命中**。任何語法正確的 JSON 改名成 `.acerun` 都會匯入成一個空白工作區。

現在改為驗證原始 JSON document 確實有 `AppData` 物件鍵，並拒絕 `AceRunVersion` 高於本版的檔案（先前這種檔案會靜默匯入，較新版本新增的欄位被丟棄而使用者毫無所覺）。名稱空白仍不視為拒絕，由呼叫端套用預設名。

## 10. 已排除的誤判

探索階段曾回報下列三項，實際查證後不成立，記錄於此以免日後重複調查：

- **`TrackAsModal` 的 handler 洩漏** — 兩個持久 flyout 只在建構式經 `InstallCodeAccelerators()` 訂閱一次；`ShowTrackedFlyout` 的呼叫端每次都傳入新建的 flyout，handler 隨物件消滅。無累積
- **`PerformDelete` 刪除最後一個工作區會爆** — `DeleteWorkspace_Click` 的 `Count <= 1` 守衛在對話框內成立，期間集合不會變動。是脆弱寫法而非現行缺陷
- **「空白名稱在 6 個呼叫點有 4 種行為」** — 過度概括。實際只有兩個是缺陷（見第 9 節），`RenameFolderAsync` 空白時靜默關閉對話框不會留下不一致狀態

## 11. 仍不在範圍

- **`Loc`** — 可抽出語言標籤解析與 `.resw` 解析；它會改寫行程層級的 `CultureInfo`
- **`StartupService`** — HKCU 讀寫無注入接縫。可抽的只有 `FormatRunValue` 一行，為它新增 Core 型別不划算
- **`ThemeService.ToElementTheme`** — 是純的三分支 switch，但 `ElementTheme` 來自 `Microsoft.UI.Xaml`，無法移出
- **名稱重複檢查** — 工作區、資料夾、標籤名稱皆可重複。這是產品決策而非缺陷：一切以 Guid 識別，重複不會損壞資料，只是視覺上容易混淆。標籤名稱會被搜尋比對，同名標籤會讓一個查詢同時命中兩者
- **標籤溢位「顯示 3 個 + N more」** — 兩份實作、兩個獨立 `= 3` 常數（`ViewModels.cs` 與 `EditItemDialog.xaml.cs`）
