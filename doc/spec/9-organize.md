# 第九階段：整理與排序鍵 ✓ 已完成

目標：項目順序在此之前只有一種來源——使用者拖曳。項目一多，手動排列就變得吃力，也沒有「依名稱排一排」這種一次性的補救手段。本階段在側邊欄加入「整理」，並補上一個名稱／路徑／標籤都表達不了的自訂排序欄位。

## 1. 資料模型

- [x] `AppItem` 新增 `SortKey`（`string`，預設空字串）：使用者自訂的排序字串，只有整理會讀，不進搜尋比對、不上 tile。
- [x] `AppData.Version` 由 5 提升至 6。舊檔缺 `SortKey` 鍵時 `System.Text.Json` 保留欄位初始值，**不需要 migration 程式碼**。
- [x] 順帶修正版本號永遠不會更新的問題：`Version` 原本只是屬性初始值，載入時被檔案裡的舊值覆蓋、存檔時又原封寫回，因此實機上的檔案停在 3 而常數已經是 5。版本號描述的是**寫入者產生的形狀**，不是讀到的形狀，故改為在寫入時蓋章——`AppData`／`WorkspaceConfig`／`WorkspaceExport` 各自提供 `CurrentVersion` 常數，由 `DataService.SaveWorkspace`／`SaveConfig`／`SerializeExport` 統一蓋上。`Version` 屬性本身保留可讀，未來要做真正的 migration 時仍拿得到來源版本。
- [x] `.acerun` 匯出原本繞過 `DataService` 直接 `JsonSerializer.Serialize`，其中的 `AppData` 直接來自 `LoadWorkspace`，會把該檔的舊版本號帶進匯出檔；改走新增的 `DataService.SerializeExport`，與一般存檔同一條路徑。
- [x] `.acerun` 匯出／匯入走同一個 `AppData` 型別與 `DataService.JsonOptions`，`SortKey` 自動 round-trip，`ManageWorkspacesDialog` 未動。
- [x] `AppItemViewModel` 依既有三點契約同步：backing field、通知屬性、建構子、`ToModel()`。

## 2. 整理語意（`MainWindow.Organize.cs`）

- [x] 新增 partial 而非 `Services/` 類別：邏輯需要 `AppItemViewModel` 與 `_tags`，放進 Services 會造成 Services → ViewModel 的層級反轉。
- [x] **一次性重排**，不是持續排序模式。排完的結果就是新的手動順序，因此 `FolderItem` 不需要新增排序狀態，`CanReorderItems` 也不必停用，之後新增的項目仍照舊落在最後。
- [x] 四個條件皆為遞增，各以 `DisplayName` 作次要鍵：
  - 名稱 → `CurrentCultureIgnoreCase`（顯示文字，中日文需要 culture collation）
  - 路徑 → `OrdinalIgnoreCase`（機器字串，Windows 路徑慣例；URL 項目比 URL 本身）
  - 標籤 → 第一個 tag 在 `_tags` 中的索引，無 tag 者 `int.MaxValue` 排最後。`NormalizeAppTags()` 已維持 tag 在 workspace 順序，故「第一個 tag」即使用者在管理標籤對話框排定的主要標籤；改用 tag 名稱字母序會與該原則打架。
  - 排序鍵 → 空值先分到後段再比字串，未分類的項目不該領頭。
- [x] 一律 `OrderBy`/`ThenBy`（LINQ 為 stable sort，`List.Sort` 不是），與 `RunSearch` 同一理由：全鍵相同的項目保留原本的拖曳順序。
- [x] 「反轉順序」對目前順序整個倒轉，降冪需求由此涵蓋，選單維持一層。
- [x] 套用改用 `ObservableCollection.Move` 而非 `Clear()` + `Add()`：`Clear` 發出 `Reset` 會回收所有 GridView 容器，`AppGridView_ContainerContentChanging` 隨即釋放並重載每個圖示，畫面會閃；`Move` 只讓 GridView 位移，容器不回收。`IndexOf` 造成的 O(n²) 在數十個項目下可忽略。
- [x] 順序沒有實際變動就不寫檔。
- [x] 存檔走 `CommitSave()` 而非 `SaveItems()`：側邊欄在搜尋進行中仍可右鍵，而 `SaveItems()` 在搜尋時 early-return。與兩個 `DragItemsCompleted` 直接呼叫 `CommitSave()` 的理由相同。集合實例未更換，故不需 `RefreshContentArea()`。

## 3. 觸發點

- [x] `SidebarListView_RightTapped` 改為同時服務資料夾列與「未分組」表頭列。`UngroupedItem` 是 `ListView.Header` 而非集合項目，`ItemFromContainer` 對它回 null，因此**右鍵未分組列在此之前完全無反應**；現在以 `ReferenceEquals(lvi, UngroupedItem)` 認出它。
- [x] 資料夾選單：重新命名 → 整理 → 分隔線 → 刪除資料夾。未分組選單：只有整理（沒有這種資料夾可以重新命名或刪除）。
- [x] 「整理」為 `MenuFlyoutSubItem`（Segoe MDL2 `E8CB`），項目少於兩個時停用。
- [x] 不需改 XAML：`SidebarListView.RightTapped` 已由 `AttachContextMenus()` 掛好，表頭的 right-tap 本就冒泡到它。
- [x] 不新增鍵盤快捷鍵：整理屬低頻批次操作，且未分組列不是 `SidebarListView` 的可選取項目，`SidebarListView_KeyDown` 對它沒有著力點。

## 4. 編輯對話框

- [x] 新增 `SortKeyBox`，置於標籤選擇器與「以系統管理員身分執行」之間。
- [x] `ApplyUrlMode()` **不收合**此欄位：URL 項目一樣住在資料夾裡、一樣需要整理。
- [x] Header／PlaceholderText 以 `Loc.GetString` 於程式碼設定而非 `x:Uid`。本專案為 unpackaged（`WindowsPackageType=None`），`x:Uid` 只會解析成 XAML 裡的英文字面值，而本功能的另一半（右鍵選單）會正確在地化——同一功能一半中文一半英文並不合理。`TagFieldLabel` 已有此先例。
- [x] `ApplyTo()` 對此欄位刻意 `Trim()`（該檔案其他欄位皆不 Trim）：尾端空白會靜默改變整理結果，而使用者在輸入框裡看不見它。

## 5. 在地化

- [x] 三份 `.resw` 同步新增 8 個鍵：`Organize_Menu`、`Organize_ByName`、`Organize_ByPath`、`Organize_ByTag`、`Organize_BySortKey`、`Organize_Reverse`、`SortKey_Field`、`SortKey_Placeholder`，全走 `Domain_Thing` 的 code-behind 命名慣例。

## 6. 不在本階段範圍

- [ ] 持續排序模式（資料夾記住排序方式、新項目自動歸位）：需在 `FolderItem` 上持久化排序狀態，並在該模式下停用拖曳排序，與目前「拖曳順序即真實順序」的資料模型衝突，另行評估。
- [ ] 跨資料夾整理／依條件自動分組（例如依 tag 自動建立資料夾）。
- [ ] `SortKey` 參與搜尋比對。
