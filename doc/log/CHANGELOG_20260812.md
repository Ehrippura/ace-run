# Changelog — 2026-08-12

本文件記錄 2026-08-10 之後（CHANGELOG_20260810.md 之後）的所有新功能、改善項目與 Bug 修正。

本次沒有新功能。整輪工作是把邏輯與 WinUI 拆開、建立單元測試，以及修正拆解過程中查證出來的缺陷。詳見 [doc/spec/11-testability.md](../spec/11-testability.md)。

---

## 可測試性（第十一階段）

在此之前專案是單一 WinExe，邏輯與 WinUI 混在一起，**沒有任何自動化測試**，CI 只做 build。問題不是「還沒寫測試」，而是結構上寫不了：搜尋排名、Organize 排序、返回歷程、DPI 換算這些有明確正確性要求的邏輯，全埋在 `MainWindow` 的 partial 裡，只能靠手動開 app、在特定 DPI 的第二螢幕上才驗證得到。

現在是三個專案：`core/AceRun.Core`（無 WinUI）、`win/`（app）、`test/AceRun.Core.Tests`。**238 個測試**，`main` 的 push 與 PR 都會在 CI 跑一次。

### 兩條約束規則撐開這道分界

- **測試專案只參考 Core，絕不參考 `win/ace-run.csproj`。** 這讓 `dotnet test` 不必帶 `-p:Platform`、不需要安裝 WindowsAppRuntime——已在 GitHub runner 上驗證，那台機器確實沒有。哪天測試步驟需要 app 那套平台參數，就代表有 WinUI 的東西漏進邏輯層了。
- **`AceRun.Core` 沒有任何 `PackageReference`。** 它用 Windows TFM 只為了 `HotkeyBinding` 的 `VirtualKey`，那是純 metadata 的 WinRT enum，由 TFM 隱含的 projection 提供，不需要 COM 啟動也不需要 UI 執行緒。

`core/` 與 `test/` 放在 `win/` 同層而非底下：`ace-run.csproj` 沒有任何 `<Compile>` 設定，完全靠 SDK 預設 globbing，放進 `win/` 會被自動編進 app。

### 持久化層拆出注入接縫

`DataService` 原本的靜態建構式把 `%LOCALAPPDATA%` 烘進 `static readonly` 欄位並建立目錄——光是讀 `JsonOptions` 就會在磁碟留痕，且無法指向別處。拆成 `AceRunJson`（共用的序列化選項）、`AceRunPaths`（由建構式接收 root）、`DataStore`（實際讀寫），`DataService` 留為 static facade，**所有呼叫端一行未改**。

測試因此能在暫存目錄跑完整的 `apps.json` → workspaces 遷移流程——那段程式碼每個使用者一生只跑一次且不可逆，先前零覆蓋。

### 移進 Core 的邏輯

`SearchRanking`、`ItemOrdering`、`FolderHistory`、`TagOrdering`、`RecentLaunchList`、`WindowPlacement` / `TitleBarMetrics` / `DropGeometry`、`ItemFactory` / `AppDataQuery`，以及後續的 `IconCache` / `IconExtractionPolicy` / `ColorKeys` / `WorkspaceImport`。

`IAppItemView` / `ITagRef` 是讓搜尋與排序能吃 view model 卻不把 `Visibility`、`Brush`、`BitmapImage` 拖過邊界的接縫。`Tags` 宣告為 `IEnumerable<ITagRef>`，靠共變讓 `ObservableCollection<TagViewModel>` 直接滿足，不需投影也無每次呼叫的配置。

留在 `MainWindow` 的是它真正擁有的：UI 狀態、事件接線、儲存時機——`ItemOrdering.ApplyOrder` 回傳「是否移動過」，由呼叫端決定要不要存檔。

### 拆解 WinUI 耦合服務

- **`IconService`** 拆成三塊：`IconCache`（路徑、無過濾掃除、圖示來源選擇）與 `IconExtractionPolicy`（退避排程、`E_PENDING` 判別）進 Core，`BitmapImage`、`StorageFile` 縮圖擷取、`SemaphoreSlim` 閘門與 in-flight 字典留在 `win/`。路徑改由新增的 `AceRunPaths.IconsDir` 提供。最該有測試的是 `IsRetryable`（曾經寫錯的判斷）與 `ClearAll`（無過濾地刪除目錄下每個檔案，也是 `.tmp` 殘骸與改名前 `.png` 的唯一遷移路徑）。
- **`ColorTags`** 拆出 `ColorKeys`。原本 `SolidColorBrush` 靜態欄位使整個型別在無 XAML runtime 時初始化失敗，連純資料的顏色鍵清單都讀不到。價值不在測六個字串，而在**顏色鍵會寫進 JSON 且永遠不得重新命名**——`CLAUDE.md` 早載明這條規則卻無任何東西守著。

---

## Bug 修正

- **`config.json` 損毀會讓 app 靜默失效。** `LoadConfig` 對讀不出來的檔案回傳一個 `Workspaces` 為空的 `WorkspaceConfig`，而每個消費端都對它 `.First()`。`config.json` 存在但壞掉時不會走遷移，也就無從自癒。失敗形態尤其糟：啟動跑在 fire-and-forget 的 task 上，例外未被觀察，行程不會崩潰——使用者拿到的是一個開得起來、工作區選單全空、沒有任何錯誤訊息的視窗，而磁碟上的 workspace 檔案全在、只是索引不到了。`MigrateOrInitialize` 現在保證回傳可用的 config（至少一個工作區、`ActiveWorkspaceId` 指向其中之一），並把修復結果寫回磁碟，讓其他 `LoadConfig` 呼叫端也看得到。修復而非拒絕啟動，且不刪任何東西：既有的 workspace 檔案原封不動，使用者可以再匯入回來。
- **資料檔沒有原子寫入。** `SaveConfig` 與 `SaveWorkspace` 直接 `File.WriteAllText`——先截斷再填入，該窗口內崩潰、斷電或磁碟滿就留下截斷檔，對 `config.json` 而言就是上一條的成因。改為寫 `.tmp` 再 rename，與 `IconService` 同一手法。諷刺的是圖示快取這種隨時可重新擷取的拋棄式資料一直有這層保護，真正無法重建的資料檔反而沒有。
- **新建工作區的預設名稱未本地化。** `ConfirmNewWorkspace_Click` 寫死英文字面值 `"New Workspace"`，中文與日文使用者不命名就得到英文名稱。新增專用的 `Workspace_DefaultName`（比照 `DefaultFolderName`，與按鈕標題分開）。標籤同理新增 `Tag_DefaultName`——先前用的 `Tag_New` 是「新增標籤」按鈕的標題，未命名的標籤會被叫做一個按鈕。
- **工作區重命名遇空白名稱留下不一致畫面。** `WorkspaceName_LostFocus` 直接 `return`，輸入框仍顯示空白而模型維持舊名，兩者不一致直到某個東西觸發重繪。比照 `ManageTagsDialog` 還原輸入框。
- **`.acerun` 匯入驗證形同虛設。** 原本唯一的檢查是 `export?.AppData is null`，但 `WorkspaceExport.AppData` 帶有屬性初始值 `= new()`，`System.Text.Json` 對沒提到該鍵的檔案會保留那個空實例——**只有 JSON 明寫 `"AppData": null` 才會命中**，任何語法正確的 JSON 改名成 `.acerun` 都會匯入成一個空白工作區。改為 `WorkspaceImport.TryParse`：驗證原始 JSON document 確實有 `AppData` 物件鍵，並拒絕 `AceRunVersion` 高於本版的檔案（先前這種檔案靜默匯入，較新版本新增的欄位被丟棄而使用者毫無所覺）。
- **屬性 setter 會刪磁碟檔。** `AppItemViewModel` 的 `FilePath` 與 `CustomIconPath` setter 呼叫 `IconService.InvalidateCache`，設一個字串屬性就刪檔——在呼叫端完全看不出來，也讓 view model 無法在沒有磁碟的情況下建構。`EditItemDialog.ApplyTo` 是唯一寫入者，改由它比較新舊值後呼叫一次，順帶修掉 `CustomIconPath` 缺少 `FilePath` 那道 `Length > 0` 守衛所造成的多餘刪除。
- **`mailto:` 的顯示名稱說明有誤。** `UrlUtil.SuggestDisplayName` 的註解宣稱 `mailto:` 是「沒有 host 的 scheme」而會回傳原字串，但 `Uri` 把 `@` 之後視為 host，所以會被命名為郵件網域。行為本身合理，註解是錯的——已修正並加測試釘住實際行為。

---

## 改善項目

### 消除重複

- `AppCount` 計算（4 處）→ `AppData.ItemCount`
- 走訪工作區所有項目（4 處）→ `AppDataQuery.AllItems` / `ItemIds`
- 標籤依 workspace 順序投影（4 處）→ `TagOrdering.InWorkspaceOrder`
- 建立 App / URL 項目（2 處）→ `ItemFactory`
- 手刻確認 flyout（2 份，各約 28 行）→ `ConfirmFlyout.Show`
- `GetDpiForWindow` 的 `DllImport` 宣告（2 份）→ `DisplayScale.ForWindow`
- 寫死的 `"Blue"`（2 處）→ `ColorKeys.Default`

### 兩處刻意的等價改寫

- `SearchRanking.Rank` 回傳 `(項目, 資料夾名稱)` 配對，不再於排名過程中寫回 `FolderLabel`。排名本身成為純函式。
- `PrimaryTagRank` 改以 `Id` 比對（原為參考相等）。兩者等價，因為標籤是共用實例且 `TagOrdering.Normalize` 保證每個項目的 `Tags` 維持 workspace 順序。

---

## 建置與發佈

- `win/ace-run.slnx` 納入 `AceRun.Core` 與 `AceRun.Core.Tests`，兩者建置 AnyCPU（app 沒有 AnyCPU 設定，但邏輯層沒有理由在意架構）。
- `.github/workflows/ci.yml` 在 build 之後執行 `dotnet test`，刻意不帶 `-p:Platform`。
- `core/` 與 `test/` 各補一份 `.editorconfig`，比照 `win/` 使用 CRLF。

---

## 文件

- 新增 `doc/spec/11-testability.md`，`doc/spec.md` 表格加一列。
- `CLAUDE.md` 加入 Build & Run 的測試指令、Core / test 佈局與那兩條約束規則，以及圖示快取層與擷取層的分界。
- **修正導軌摺疊閾值的文件不符。** 實際常數是 800 DIP（`MainWindow.Motion.cs`），但 `MainWindow.Accelerators.cs` 的註解與 `CLAUDE.md` 都寫 900。以程式碼為準修正兩處。

### 三項排除的誤判

探索階段曾回報下列問題，實際查證後不成立，記錄在 `11-testability.md` 第 10 節以免日後重複調查：

- **`TrackAsModal` 的 handler 洩漏。** 兩個持久 flyout 只在建構式經 `InstallCodeAccelerators()` 訂閱一次；`ShowTrackedFlyout` 的呼叫端每次都傳入新建的 flyout，handler 隨物件消滅。無累積。
- **`PerformDelete` 刪除最後一個工作區會爆。** `DeleteWorkspace_Click` 的 `Count <= 1` 守衛在對話框內成立，期間集合不會變動。是脆弱寫法而非現行缺陷。
- **「空白名稱在 6 個呼叫點有 4 種行為」。** 過度概括，實際只有兩個是缺陷（見上方 Bug 修正），`RenameFolderAsync` 空白時靜默關閉對話框不會留下不一致狀態。
