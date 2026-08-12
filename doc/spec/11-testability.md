# 第十一階段：可測試性與單元測試 ✓ 已完成

目標：在此之前整個專案是單一 `WinExe`，邏輯與 WinUI 混在一起，**沒有任何自動化測試**，CI 只做 build。問題不是「還沒寫測試」而是結構上寫不了——搜尋排名、Organize 排序、返回歷程、DPI 換算這些有明確正確性要求的邏輯全埋在 `MainWindow` 的 partial 裡，只能靠手動開 app、在特定 DPI 的第二螢幕上才驗證得到；`DataService` 更是光讀一個屬性就會在磁碟建目錄。本階段把不依賴 UI 的邏輯集中到獨立組件、建立單元測試與 CI 檢查，並修正拆解過程中查證出來的缺陷。

## 1. 專案結構

- [x] 三個專案：`core/AceRun.Core/`（無 WinUI 的邏輯層）、`win/`（app）、`test/AceRun.Core.Tests/`（xUnit）。
- [x] 兩個新專案放在 `win/` **同層而非底下**：`ace-run.csproj` 沒有任何 `<Compile>` 設定，完全依賴 SDK 預設 globbing，放進 `win/` 的任何資料夾都會被自動編進 app。
- [x] 命名空間沿用 `ace_run.Models` 與 `ace_run.Services`，搬移不影響任何呼叫端的 `using`。
- [x] **測試專案只參考 `AceRun.Core`，絕不參考 `win/ace-run.csproj`。** 這條規則有兩個作用：測試不需要安裝 WindowsAppRuntime 就能跑（`dotnet test` 因此不必帶 `-p:Platform`），以及強制「想測的東西就必須放進 Core」——否則邏輯可以留在 `MainWindow` 裡卻看起來像被測過了。
- [x] **`AceRun.Core` 沒有任何 `PackageReference`。** 它用 Windows TFM（`net10.0-windows10.0.22000.0`）只為了 `HotkeyBinding` 的 `Windows.System.VirtualKey`，那是純 metadata 的 WinRT enum，由 TFM 隱含的 Windows SDK projection 提供，不需要 COM 啟動也不需要 UI 執行緒。
- [x] `core/` 與 `test/` 各補一份 `.editorconfig`，比照 `win/` 使用 CRLF（根目錄設定為 LF，`doc/` 沿用之）。

## 2. 持久化層

- [x] `DataService` 原本是全靜態類別，靜態建構式把 `%LOCALAPPDATA%` 烘進 `static readonly` 欄位並 `Directory.CreateDirectory`——光是讀 `JsonOptions` 就會在磁碟留痕，且無法指向暫存目錄、無法在測試間重設。拆成四塊：
  - `AceRunJson`：共用的 `JsonSerializerOptions`。獨立出來後讀它不再觸發任何路徑解析。
  - `AceRunPaths`：由建構式接收 root 算出各檔案路徑，不碰檔案系統。提供 `Default`（`%LOCALAPPDATA%\AceRun`）。
  - `DataStore`：實際的讀寫與遷移，建立目錄移到真正需要的方法內。
  - `DataService`：靜態 facade，逐一委派給 `DataStore.Default`。**所有呼叫端一行未改。**
- [x] 另提供 `DataStore.ParseAppData` / `ParseConfig` 兩個純解析進入點，讓「檔案損毀就回預設值」這條重複三次的 try/catch 行為可以脫離檔案系統測試。catch 維持不過濾，與被取代的程式碼一致：app 得在任何檔案狀態下啟動，把它收窄會讓一個讀不出來的檔案變成啟動時崩潰。
- [x] 測試因此能在暫存目錄跑完整的 `apps.json` → workspaces + `.bak` 遷移流程。那段程式碼每個使用者一生只跑一次且不可逆，先前零覆蓋。
- [x] 順帶統一 `DataStore.Load()` 的例外處理。它原本把 `ReadAllText` 放在 try 之外（其餘兩個載入方法則有），遷移路徑會因檔案被鎖而讓啟動失敗。

## 3. 邏輯層抽象

- [x] `AppItemViewModel` 與 `TagViewModel` 帶有 `Visibility`、`Brush`、`BitmapImage`，直接吃 view model 的邏輯會把整個框架拉進 Core。以兩個最小介面切開：`ITagRef`（`Id`、`Name`）與 `IAppItemView`（`Id`、`DisplayName`、`FilePath`、`Arguments`、`SortKey`、`Tags`）。
- [x] `Tags` 宣告為 `IEnumerable<ITagRef>` 而非清單，靠共變讓 `ObservableCollection<TagViewModel>` 直接滿足，不需投影也沒有每次呼叫的配置。view model 端只需加基底型別與一個顯式實作成員。
- [x] 搜尋與排序必須跑在 view model 上而非 `AppItem` 上：集合順序**就是**持久化順序，而集合裡放的是 view model。

## 4. 移入 Core 的邏輯

- [x] `SearchRanking`（自 `MainWindow.Events.cs`）：比對排名與結果排序。
- [x] `ItemOrdering`（自 `MainWindow.Organize.cs`）：四種排序鍵與 `ApplyOrder`。
- [x] `FolderHistory`（自 `MainWindow.History.cs`）：返回歷程狀態機。
- [x] `TagOrdering`：標籤依 workspace 順序投影與正規化。
- [x] `RecentLaunchList`：最近啟動清單的記錄與清理，`MaxRecent` 取代原本出現兩次的無名魔術數字 10。
- [x] `WindowPlacement` / `TitleBarMetrics` / `DropGeometry`：DPI 換算、視窗夾制與置中、拖放落點判定。Core 內定義最小的像素值型別（`PixelRect` / `PixelSize` / `PixelPoint`），避免把 `Windows.Graphics.*` 拖進來；呼叫端做轉接。
- [x] `ItemFactory` / `AppDataQuery`：建立 App / URL 項目、走訪整個 workspace。
- [x] 留在 `MainWindow` 的是它真正擁有的：UI 狀態、事件接線、**儲存時機**。`ItemOrdering.ApplyOrder` 回傳「是否真的移動過」，由呼叫端決定要不要 `CommitSave()`。
- [x] **排名不再有副作用**：`RunSearch` 原本在排名過程中就把 `FolderLabel` 寫回項目。改為 `SearchRanking.Rank` 回傳 `(項目, 資料夾名稱)` 配對、由呼叫端指派，排名本身成為純函式。
- [x] **一處刻意的等價改寫**：`PrimaryTagRank` 原本用 `_tags.IndexOf(app.Tags[0])`，是參考相等比對；抽出後改為以 `Id` 比對。兩者等價，因為標籤是共用實例且 `TagOrdering.Normalize` 保證每個項目的 `Tags` 維持 workspace 順序。

## 5. 拆解 WinUI 耦合服務

- [x] **`IconService`** 拆成三塊。`IconCache`（`PathFor` / `Invalidate` / `ClearAll` / `ChooseSource`，純 `System.IO`）與 `IconExtractionPolicy`（`BackoffMs` / `DelayForAttempt` / `IsRetryable`，純判斷）進 Core；`BitmapImage`、`StorageFile` 縮圖擷取、`SemaphoreSlim` 閘門與 in-flight 字典留在 `win/`。
- [x] 路徑改由新增的 `AceRunPaths.IconsDir` 提供，磁碟配置回到單一型別描述。第一輪刻意沒加這個屬性——當時無人使用，會是死碼。
- [x] `IsRetryable` 是最該有測試的一個：它正是第三階段修過的判斷，把 `E_PENDING`（稍後再試）當成永久失敗會讓圖示永久空白，因為沒有任何東西會重跑擷取。
- [x] `ClearAll` 是次要目標：它**無過濾地刪除目錄下每個檔案**，也是 `.tmp` 殘骸與改名前 `.png` 的唯一遷移路徑（見第三階段 §1），行為必須釘死。
- [x] **`AppItemViewModel` 的 setter 不再動磁碟。** `FilePath` 與 `CustomIconPath` 的 setter 原本呼叫 `IconService.InvalidateCache`——設一個字串屬性就刪檔，在呼叫端完全看不出來，也讓 view model 無法在沒有磁碟的情況下建構。`EditItemDialog.ApplyTo` 是唯一寫入者，因此失效動作移到那裡，比較新舊值後呼叫一次；不需要任何依賴注入。順帶修掉 `CustomIconPath` 缺少 `FilePath` 那道 `Length > 0` 守衛所造成的多餘刪除。
- [x] `LoadIconAsync` / `ReleaseIcon` 仍留在 view model：那是明確的非同步呼叫，不是隱藏在屬性賦值裡的副作用。
- [x] **`ColorTags` 拆出 `ColorKeys`。** 原本顏色鍵清單與 `NoColorBrush = new SolidColorBrush(...)` 是同一型別的靜態欄位初始化，因此連讀純資料都會在無 XAML runtime 時拋 `TypeInitializationException`。價值不在測六個字串，而在**顏色鍵會寫進 JSON 且永遠不得重新命名**——這條規則早已載明卻無任何東西守著。`ColorKeys.Default` 取代兩處寫死的 `"Blue"`。

## 6. 消除的重複

- [x] `AppCount` 計算（4 處）→ `AppData.ItemCount`。
- [x] 走訪工作區所有項目（4 處）→ `AppDataQuery.AllItems` / `ItemIds`。
- [x] 標籤依 workspace 順序投影（4 處）→ `TagOrdering.InWorkspaceOrder`。
- [x] 建立 App / URL 項目（2 處）→ `ItemFactory`。
- [x] 手刻確認 flyout（2 份，各約 28 行，除訊息與確認動作外完全相同）→ `ConfirmFlyout.Show`。留在 `win/`：這是 UI 結構，不是邏輯。
- [x] `GetDpiForWindow` 的 `DllImport` 宣告（2 份）→ `DisplayScale.ForWindow`。同樣留在 `win/`：這是 OS 呼叫。

## 7. 修正的缺陷

- [x] **新建工作區的預設名稱未本地化。** `ConfirmNewWorkspace_Click` 寫死英文字面值 `"New Workspace"`，中文與日文使用者不命名就得到英文名稱。新增專用的 `Workspace_DefaultName`——資料夾的 `DefaultFolderName` 才是對的樣板，與按鈕標題分開。標籤同理新增 `Tag_DefaultName`：先前用的 `Tag_New` 是「新增標籤」按鈕的標題，未命名的標籤會被叫做一個按鈕。
- [x] **工作區重命名遇空白名稱留下不一致畫面。** `WorkspaceName_LostFocus` 直接 `return`，輸入框仍顯示空白而模型維持舊名，兩者不一致直到某個東西觸發重繪。`ManageTagsDialog` 的同位置做法（還原輸入框）才是對的，比照修正。
- [x] **`.acerun` 匯入驗證形同虛設。** 原本唯一的檢查是 `export?.AppData is null`，但 `WorkspaceExport.AppData` 帶有屬性初始值 `= new()`，`System.Text.Json` 對沒提到該鍵的檔案會保留那個空實例——**只有 JSON 明寫 `"AppData": null` 才會命中**，任何語法正確的 JSON 改名成 `.acerun` 都會匯入成一個空白工作區。改為 `WorkspaceImport.TryParse`：驗證原始 JSON document 確實有 `AppData` 物件鍵，並拒絕 `AceRunVersion` 高於本版的檔案（先前這種檔案靜默匯入，較新版本新增的欄位被 `System.Text.Json` 丟棄而使用者毫無所覺）。回傳列舉而非字串鍵，Core 不碰任何本地化；名稱空白仍不視為拒絕，由呼叫端套用預設名。
- [x] **導軌摺疊閾值的文件不符。** 實際常數是 `RailCollapseWidthDip = 800`，但 `MainWindow.Accelerators.cs` 的註解與 `CLAUDE.md` 都寫 900。以程式碼為準修正兩處。
- [x] **`mailto:` 的顯示名稱說明有誤。** `UrlUtil.SuggestDisplayName` 的註解宣稱 `mailto:` 是「沒有 host 的 scheme」而會回傳原字串，但 `Uri` 把 `@` 之後視為 host，所以會被命名為郵件網域。行為本身合理，註解是錯的——修正註解並加測試釘住實際行為。

## 8. 建置與 CI

- [x] `win/ace-run.slnx` 納入兩個新專案，平台對應寫成 `AnyCPU`（app 沒有 AnyCPU 設定，但邏輯層沒有理由在意架構），Visual Studio 仍可正常開啟。
- [x] `.github/workflows/ci.yml` 在 build 之後執行 `dotnet test test/AceRun.Core.Tests/AceRun.Core.Tests.csproj -c Release`。
- [x] 刻意**不帶** `-p:Platform`：Core 與測試專案都是 AnyCPU。哪天這一步需要 app 那套平台參數，就代表有 WinUI 的東西漏進邏輯層了。
- [x] 已在 GitHub runner 上驗證通過——那台機器沒有安裝 WindowsAppRuntime，正是這條規則為之設計的環境。

## 9. 已查證排除的疑慮

盤點時曾列為疑似缺陷，實際查證後不成立。記錄於此以免日後重複調查。

- [x] **`TrackAsModal` 的 handler 從未取消訂閱**：不成立。兩個持久 flyout 只在建構式經 `InstallCodeAccelerators()` 訂閱一次；`ShowTrackedFlyout` 的兩個呼叫端每次都傳入新建的 flyout，handler 隨物件消滅。無累積。
- [x] **`PerformDelete` 刪除最後一個工作區會爆**：不是現行缺陷。`DeleteWorkspace_Click` 的 `Count <= 1` 守衛在對話框內成立，期間集合不會變動。是脆弱寫法而非 live bug。
- [x] **「空白名稱在 6 個呼叫點有 4 種行為」**：過度概括。實際只有兩個是缺陷（見第 7 節），`RenameFolderAsync` 空白時靜默關閉對話框不會留下不一致狀態。

## 10. 不在本階段範圍

- [ ] `Loc` 的拆解：語言標籤解析與 `.resw` 剖析可移進 Core，MRT 部分留在 app。注意它會改寫行程層級的 `CultureInfo`。
- [ ] `StartupService`：HKCU 讀寫無注入接縫。可抽的只有 `FormatRunValue` 一行，為它新增 Core 型別不划算；登錄檔注入接縫對 70 行的類別是過度設計。
- [ ] `ThemeService.ToElementTheme`：是純的三分支 switch，但 `ElementTheme` 來自 `Microsoft.UI.Xaml`，無法移出。
- [ ] 名稱重複檢查：工作區、資料夾、標籤名稱皆可重複。這是產品決策而非缺陷——一切以 Guid 識別，重複不會損壞資料，只是視覺上容易混淆。標籤名稱會被搜尋比對，同名標籤會讓一個查詢同時命中兩者。
- [ ] 標籤溢位「顯示 3 個 + N more」的兩份實作與兩個獨立的 `= 3` 常數（`ViewModels.cs` 的 `MaxVisibleTags` 與 `EditItemDialog.xaml.cs` 的 `MaxSummaryDots`）。行為相同，屬可合併的重複。
