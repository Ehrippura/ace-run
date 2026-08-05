Windows App Launcher - 需求規格書 (Feature Spec)

1. 專案概觀

開發一個輕量級的 Windows 應用程式啟動器。允許使用者自定義應用程式 (.exe) 的路徑與啟動參數，並透過簡潔的介面快速啟動這些程式。

專案名稱：
    Ace Run

技術堆疊：
    - UI 框架： WinUI 3 (Windows App SDK)
    - 語言： C#
    - 資料儲存： JSON
    - 核心 API： System.Diagnostics.Process / ShellExecute

2. 功能列表 (Feature List)

## 第一階段：核心功能 (MVP) ✓ 已完成

目標：完成最小可行性產品，能存、能看、能跑。
    1. 新增啟動項目 (Add Item)
        [x] 實作 FileOpenPicker 讓使用者選擇 .exe 檔案。
        [x] 自動從選擇的檔案路徑截取檔名作為「預設標題」。

    2. 項目列表 (List View)
        [x] 使用 ListView 顯示已加入的應用程式。
        [x] 每個項目顯示顯示名稱 (Display Name)，檔案路徑以 ToolTip 方式於滑鼠懸停時呈現。

    3. 執行程式 (Execute)
        [x] 點擊「啟動」按鈕，依據儲存的路徑啟動外部程式。

    4. 資料持久化 (Persistence)
        [x] 定義資料模型 (AppItem)，包含 Id、DisplayName、FilePath 等屬性。
        [x] 應用程式關閉時，將列表序列化為 JSON 儲存至 LocalAppData\AceRun\apps.json。
        [x] 應用程式開啟時，讀取 JSON 並還原列表。

## 第二階段：進階設定 (Advanced Config) ✓ 已完成

目標：解決特殊程式（如遊戲、開發工具）的啟動需求。

    1. 啟動參數 (Arguments)
        [x] 在新增/編輯對話框 (EditItemDialog) 增加「參數」輸入框。
        [x] 支援傳遞參數給執行檔 (例如：chrome.exe --incognito)。

    2. 自訂工作目錄 (Working Directory)
        [x] 新增項目時，預設將「工作目錄」設為該 .exe 所在的資料夾。
        [x] 允許使用者透過 FolderPicker 手動修改工作目錄。

    3. 管理員模式 (Run as Administrator)
        [x] 在編輯對話框中加入 ToggleSwitch，啟用時以 runas verb 啟動程式。

    4. 編輯與刪除 (CRUD)
        [x] 實作右鍵選單 (MenuFlyout Context Menu)，包含「Edit」與「Delete」選項。
        [x] 編輯：開啟 ContentDialog，可修改所有欄位（名稱、路徑、參數、工作目錄、管理員模式）。
        [x] 刪除：彈出確認對話框後從列表中移除，變更即時儲存。
        [x] 新增項目時同樣使用編輯對話框，讓使用者在加入前預覽與調整設定。

## 第三階段：使用者體驗優化 (UX Polish) ✓ 已完成

目標：提升視覺質感與操作便利性。

    1. 圖示擷取 (Icon Extraction)
        [x] 從 .exe 檔案中動態讀取圖示 (Icon)。
        [x] 圖示必須使用磁碟快取，避免每次開啟 app 都需要從檔案中重新讀取圖示

    2. 拖放支援 (Drag & Drop)
        [x] 允許使用者將桌面上的 .exe 或 .lnk (捷徑) 直接拖入視窗。
        [x] 自動解析路徑並直接加入目前選取的資料夾（或「未分類」），不開啟對話框。

    3. 快速搜尋 (Search/Filter)
        [x] 頂部增加搜尋框 (AutoSuggestBox)。
        [x] 輸入關鍵字時即時過濾列表內容。

    4. 多語言支援
        [x] 英文
        [x] 繁體中文

## 第四階段：額外功能實作 ✓ 已完成

    1. 使用 H.NotifyIcon 提供 System Tray 支援
        [x] 支援將應用程式最小化至 System Tray。
        [x] 雙擊 Tray Icon 恢復視窗顯示。
        [x] Tray Icon 右鍵選單包含「最近開啟的程式」與「結束」選項。

    2. 資料夾分組管理 (Folder Grouping)
        [x] 支援建立、重新命名、刪除資料夾，將程式項目組織為群組。
        [x] 左側側邊欄以 ListView 呈現「未分類」與各資料夾，點選切換主內容區域。
        [x] 選取中的側邊欄項目顯示 accent 左側選取指示條，視覺一致。
        [x] 「未分類」固定置頂，無法移動；其他資料夾支援拖曳重新排序，順序持久化。
        [x] 可透過右鍵選單「移動至」子選單，在「未分類」與各資料夾之間移動項目。

## 第五階段：Workspace 多工作區管理 ✓ 已完成

目標：支援多個獨立的工作區（Workspace），讓使用者可以依照不同情境（如工作、遊戲、開發）組織應用程式，並快速切換。

    1. Workspace 概念與資料模型
        [x] 每個 Workspace 包含獨立的：
            - 應用程式列表（含資料夾分組結構）
            - 最近啟動記錄
        [x] 視窗大小設計為全域共用，儲存於 WorkspaceConfig（非各 Workspace 獨立）
        [x] Workspace 屬性：
            - Id (GUID)
            - 名稱 (Name)
            - 建立時間 (CreatedAt)
            - 最後修改時間 (LastModifiedAt)
            - 圖示顏色標記 (ColorTag) - 可選，用於視覺區分
            - 項目數量 (AppCount) - 反正規化欄位，儲存時更新
        [x] 新增 WorkspaceConfig 模型，儲存：
            - Workspace 列表
            - 當前選中的 Workspace Id
            - 預設 Workspace Id（可選）
            - 視窗狀態 WindowState（全域共用）

    2. 資料儲存架構調整
        [x] 改為多檔案儲存結構：
            - %LOCALAPPDATA%\AceRun\config.json - 儲存 WorkspaceConfig（含全域視窗狀態）
            - %LOCALAPPDATA%\AceRun\workspaces\<workspace-id>.json - 各 Workspace 資料
            - 圖示快取維持共用：%LOCALAPPDATA%\AceRun\icons\
        [x] 向下相容處理：
            - 首次升級時，將現有 apps.json 轉換為「預設」Workspace
            - 原 apps.json 重新命名為 apps.json.bak 保留備份
        [x] 自動儲存機制：
            - 切換 Workspace 時自動儲存當前 Workspace
            - 修改資料時更新 LastModifiedAt 時間戳

    3. Workspace 切換器 UI
        [x] 頂部工具列新增 Workspace ComboBox
            - 顯示當前 Workspace 名稱與顏色標記圓點
            - 下拉選單列出所有 Workspace
        [x] 工具列新增「管理工作區」按鈕（齒輪圖示）
        [x] 切換行為：
            - 儲存當前 Workspace 的完整狀態
            - 清空目前的 UI 列表與搜尋狀態
            - 載入選中 Workspace 的資料
            - 視窗大小為全域共用，切換時不重置
        [x] 視窗標題列顯示當前 Workspace 名稱（格式：「Ace Run — <名稱>」）

    4. Workspace 管理功能
        [x] 「管理工作區」對話框，包含：
            - 新建 Workspace（展開式行內表單）
                - 輸入名稱
                - 選擇顏色標記（無、藍、綠、紅、黃、紫）
                - 選項：「建立空白 Workspace」或「複製當前 Workspace」
            - 行內重新命名（TextBox 直接編輯，失焦時儲存）
            - 刪除 Workspace（Flyout 確認，至少保留一個）
            - 設定預設 Workspace（星形切換按鈕）
        [x] Workspace 列表顯示：
            - 顏色標記圓點（有設定時才顯示）
            - 名稱（可直接編輯）
            - 項目數量
        [x] 拖曳排序（影響 ComboBox 順序）

    5. 匯入/匯出功能
        [x] 匯出 Workspace：
            - 管理對話框中每個項目提供「匯出」按鈕
            - 匯出為 .acerun 檔案（JSON 格式，含完整結構，不含圖示快取）
            - FileSavePicker 讓使用者選擇儲存位置
        [x] 匯入 Workspace（匯入為新 Workspace）：
            - FileOpenPicker 選擇 .acerun 檔案
            - 驗證檔案格式，格式錯誤時以 InfoBar 提示
            - 匯入後作為新 Workspace 加入列表
        [ ] 「合併到當前 Workspace」選項（未實作）
        [ ] 同名資料夾衝突處理（未實作）
        [ ] 匯入後重新提取圖示快取（未實作，圖示於首次啟動時自動重建）

## 第六階段：改進

1. Tag 標籤管理

目標：讓使用者可以建立帶有顏色與名稱的 tag 標籤，並將 tag 指派到 app 項目上，提升辨識與分類效率。

    1. 資料模型與持久化
        [x] 新增 Tag 資料模型，包含 Id、名稱與顏色。
        [x] 在 Workspace 資料中儲存 tag 清單，讓每個 Workspace 可擁有獨立的 tag 設定。
        [x] 在 AppItem 中儲存已指派的 TagId（資料面採 TagIds 清單以保留多標籤擴充性，V1 UI 為單一標籤）。
        [x] 儲存與載入 Workspace 時保留 tag 清單、顏色與 app 的 tag 指派關係。

    2. Tag 管理功能
        [x] 提供新增 tag 功能，可輸入 tag 名稱並選擇顏色。
        [x] 提供編輯 tag 功能，可修改 tag 名稱與顏色。
        [x] 提供刪除 tag 功能，刪除前顯示確認提示。
        [x] 刪除 tag 後，已套用該 tag 的 app 需自動移除對應 TagId。

    3. App Tag 指派
        [x] 在新增/編輯 app 對話框中加入 tag 選擇欄位。
        [x] 支援從既有 tag 清單中選擇 tag 並套用到 app。
        [x] 支援在 app 右鍵選單中快速設定或移除 tag。
        [x] 修改 app 的 tag 後即時更新 UI 並持久化。

    4. App Grid 顯示
        [x] 被設定 tag 的 app，在 app 名稱左側顯示對應 tag 顏色的圓點。
        [x] 未設定 tag 的 app 不顯示圓點，維持原本版面簡潔。
        [x] 圓點顏色需與 tag 管理中設定的顏色一致。
        [x] 搜尋結果列表中同樣顯示 app 的 tag 顏色圓點。

## 第七階段：URL 項目支援

目標：讓網址（以及 `steam://`、`obsidian://`、`ms-settings:` 等自訂協定）能與應用程式並列管理，共用同一套 workspace／資料夾／標籤／搜尋／最近啟動機制。

    1. 資料模型與持久化
        [x] 新增 `ItemKind` 列舉（`App` / `Url`），`AppItem.Kind` 預設 `App`。
        [x] JSON 以字串形式儲存（`JsonStringEnumConverter`），舊 workspace 檔案缺少 `Kind` 時自動視為 `App`，不需 migration。
        [x] `AppData` 版本升至 **5**。
        [x] `DataService.JsonOptions` 對外公開，`.acerun` 匯入匯出改為共用同一份序列化設定。

    2. 判斷與正規化
        [x] 新增 `UrlUtil` 服務：`TryNormalize`（可接受任何 absolute URI，scheme 為 `file` 則拒絕；輸入僅有主機名時自動補 `https://`）、`SuggestDisplayName`（取 host 並去掉 `www.`）、`ReadInternetShortcut`（讀取 `.url` 檔的 `URL=`）。
        [x] `example.com:8080` 一類的輸入視為 host:port 而非 scheme。

    3. 新增入口
        [x] 工具列「新增」改為 SplitButton：主按鈕維持 `.exe` 檔案選擇器，下拉提供「新增應用程式」與「新增網址」。
        [x] 拖曳支援 `StandardDataFormats.WebLink` 與 `Text`（從瀏覽器分頁或網址列拖曳），以及桌面上的 `.url` 網際網路捷徑檔。
        [x] 多格式同時存在時依 StorageItems → WebLink → Text 的優先序只取一次，避免重複新增。

    4. 編輯與驗證
        [x] `EditItemDialog` 於 URL 模式改用「網址」欄位標題，並隱藏 瀏覽 / 啟動參數 / 工作目錄 / 以管理員身分執行。
        [x] 新增第一個表單驗證：URL 無法正規化時於欄位下方顯示錯誤訊息並阻止對話框關閉。
        [x] 顯示名稱留空時自動以網域補上。
        [x] 項目型別於建立時決定且不可事後切換（`AppItemViewModel.Kind` 為唯讀）。

    5. 啟動與其他 UI
        [x] `LaunchApp` 對 URL 僅傳 `FileName` + `UseShellExecute`，不帶啟動參數／工作目錄，也不使用 `runas`。
        [x] 無圖示時顯示 Segoe MDL2 fallback 字符：URL 為地球（`E774`），應用程式為預設 app 圖示（`ECAA`）— 同時解決了 exe 路徑失效時格子全空白的問題。
        [x] URL 項目右鍵選單以「複製連結」取代「開啟檔案位置」。
        [x] 搜尋除顯示名稱外一併比對路徑／網址，可用網域搜尋。
        [ ] 抓取網站 favicon 作為圖示（未實作，目前可用「自訂圖示」指定 .ico）。

## 第八階段：鍵盤快捷鍵

目標：讓 launcher 能完全以鍵盤驅動。快捷鍵為**固定**不可自訂，不新增設定 UI 或持久化欄位，也不含全域熱鍵。

    1. 快捷鍵配置
        [x] `Ctrl+F` / `Ctrl+E` 聚焦搜尋框。
        [x] `Ctrl+N` 新增應用程式、`Ctrl+Shift+N` 新增網址、`Ctrl+Alt+N` 新增資料夾。
        [x] `Ctrl+B` 切換側欄、`Ctrl+,` 展開齒輪選單、`Alt+Enter` 編輯選取項目。
        [x] `Ctrl+1`～`Ctrl+9` 切換 workspace；超出數量時靜默無作用（不 clamp 至最後一個）。
        [x] `Esc` 情境式：先清除搜尋，其次關閉 Overlay 模式的側欄；不隱藏視窗（無全域熱鍵可叫回）。
        [x] 側欄焦點時 `F2` 重新命名、`Delete` 刪除資料夾；「未分組」為 `ListView.Header` 故兩者皆不作用。
        [x] 搜尋框 `Enter` 啟動第一筆結果、`Down` 將焦點移至結果清單。

    2. 宣告機制
        [x] Ctrl 系組合鍵為 `KeyboardAccelerator`，統一掛在 `RootGrid`——它是所有可聚焦元素的祖先，且永不隱藏或停用。實測確認在 bare `Window`（非 `Page`）下可正常觸發，焦點位於標題列的 `SearchBox` 時亦然。
        [x] 無修飾單鍵（Esc / F2 / Delete / Down）改用特定控制項的 `KeyDown`：全域的無修飾 accelerator 會在搜尋框有焦點時觸發，按 Delete 就會跳出刪除確認。
        [x] `Ctrl+,`（逗號無具名 `VirtualKey`）與 `Ctrl+1..9`（九段重複標記）於程式碼中宣告，其餘寫在 XAML。
        [x] 不引入 `ICommand`／`XamlUICommand` 層，維持既有的事件處理器風格。

    3. 實測後修正的三項 WinUI 行為
        [x] **Alt 修飾的 accelerator 不會觸發**：`Alt+Enter` 以 `WM_SYSKEYDOWN` 送達，accelerator 引擎不路由。改在兩個清單既有的 `PreviewKeyDown` 中依 `e.KeyStatus.IsMenuKeyDown` 分支（`LaunchOrEditAsync`）。
        [x] **`AutoSuggestBox` 會吃掉 Down 與 Esc**：兩者供其建議清單使用，bubbling 的 `KeyDown` 收不到，故 `SearchBox` 改用 `PreviewKeyDown`。
        [x] **Flyout 不會抑制 `RootGrid` 的 accelerator**：管理選單開啟時按 Ctrl+2 仍會切換 workspace，留下浮在新 workspace 內容上的舊選單。改以 `TrackAsModal` 將 flyout 的 `Opened`／`Closed` 併入同一個 `_modalDepth`。

    4. 配套修正
        [x] 新增 modal 重入防護（`_modalDepth` / `ShowModalAsync` / `RunModalAsync` / `TrackAsModal`）：WinUI 同時只允許一個 `ContentDialog`，先前所有入口皆為滑鼠觸發故未暴露此問題。
        [x] 抽出 `EditAppAsync` / `NewFolderAsync` / `ToggleRail`，讓滑鼠與鍵盤共用同一路徑。
        [x] `AppGridView` 的 `PreviewKeyDown` 由 `AttachContextMenus()` 移至 XAML，與 `SearchResultsView` 對稱，且在 `Activate()` 前即生效。

    5. 可發現性
        [x] 所有 accelerator 設 `KeyboardAcceleratorPlacementMode="Hidden"`，改以 `MenuFlyoutItem.KeyboardAcceleratorTextOverride` 與 `AutomationProperties.AcceleratorKey` 提供提示。
        [x] 修飾鍵名稱（Ctrl / Shift / Alt）不在地化，與 Windows 各語系一致；僅新增 `Shortcut_Format` 一個在地化字串。
        [ ] `TitleBar` 內建側欄切換按鈕位於控制項範本內，無法設 `AcceleratorKey`，`Ctrl+B` 不會被朗讀程式念出（已知限制）。

## 第九階段：多標籤支援

目標：解除第六階段「一個 app 只能有一個 tag」的 UI 限制，讓單一項目可同時掛上多個標籤。資料面早已為此保留擴充性，本階段補齊 ViewModel 與 UI。

    1. 資料模型
        [ ] `AppItem.TagIds` 本即 `List<Guid>`，格式不變、`AppData` 維持 v5、不需 migration，`.acerun` 匯入匯出自動相容。
        [ ] 新檔被舊版本讀取時僅保留第一個 tag（舊版 `NormalizeAppTags` 的既有行為），屬可接受的降級而非資料損毀。

    2. ViewModel：以實例取代反正規化欄位
        [ ] `AppItemViewModel.Tags` 改為 `ObservableCollection<TagViewModel>`，直接持有 `_tags` 中的同一批實例。
        [ ] 移除 `SetSingleTag` / `TagColorKey` / `TagName` / `TagBrush` / `TagVisibility`；`ToModel()` 由 `Tags` 推導 `TagIds`，不再保留獨立狀態。
        [ ] `TagViewModel.ColorKey` 的 setter 已通知 `ColorBrush`，改色／改名即時反映到所有卡片，故 `RefreshAllAppTagColors()` 與 `ResolveAppTagDisplay()` 一併刪除。
        [ ] 新增 `VisibleTags`（前 3 個，卡片僅 124px 寬）、`OverflowLabel`（`+N`）、`TagsSummary`（名稱串接，供無障礙與下拉按鈕共用），由 `Tags.CollectionChanged` 觸發通知。
        [ ] 建構子加 `IReadOnlyList<TagViewModel>` 參數解析 id → 實例（四個呼叫點）；`LoadWorkspaceDataAsync` 已是先填 `_tags` 再建 app VM，順序天然正確。

    3. 指派邏輯
        [ ] `NormalizeAppTags()` 改為：移除已刪除的 tag、去重、依 `_tags` 順序排序。固定順序讓同一組 tag 在每張卡片上呈現一致，也省去額外的 tag 排序 UI。
        [ ] `ApplyTagToApp` 拆為 `ToggleTagOnApp(app, tag, on)` 與 `ClearTagsOnApp(app)`，兩者保留既有「搜尋模式下改走 `CommitSave()`」的分支。

    4. 編輯對話框
        [ ] `TagCombo` 改為 `DropDownButton` + `Flyout` 包 `SelectionMode="Multiple"` 的 `ListView`（非 `MenuFlyout`，才能連續勾選不關閉）；按鈕內容顯示圓點與名稱摘要，滿三個改 `+N`。
        [ ] `DropDownButton` 無 `Header` 屬性，需自行補標籤 `TextBlock` 並以 `Loc.GetString` 設定文字。
        [ ] 多選 `ListView` 在 `ItemsSource` 指派後立即寫 `SelectedItems` 不穩（容器尚未實體化），改於 `Loaded` 回填並以旗標擋掉回填期間的 `SelectionChanged`。
        [ ] workspace 尚無任何 tag 時按鈕停用並顯示 `Tag_Empty`。

    5. 顯示
        [ ] 卡片的單一 `Ellipse` 改為固定高度容器包 `ItemsControl`（水平 StackPanel、7px 圓點）加溢位文字；容器高度固定，空清單不改變卡片高度。
        [ ] 搜尋結果列沿用同一組合（8px 圓點），該欄 `Auto` 寬度在無 tag 時自然收合。
        [ ] `AutomationProperties.Name` 設在容器（值為 `TagsSummary`）而非每顆圓點，否則朗讀程式會逐點朗讀。

    6. 右鍵選單
        [ ] `RadioMenuFlyoutItem` + `GroupName` 改為 `ToggleMenuFlyoutItem`；「無標籤」改為置頂的「清除標籤」加分隔線。
        [ ] Flyout 點選後即關閉為 Windows 標準行為，一次指派多個標籤請走編輯對話框——此即對話框選用可連續勾選控制項的原因。

    7. 在地化
        [ ] 新增 `Tag_Clear`、`Tag_Overflow`（`+{0}`）、`Tag_Separator`（zh-TW／ja 用「、」，en 用「, 」）、`Tag_Field`，三份 `.resw` 同步更新。

    8. 不在本階段範圍
        [ ] 依 tag 篩選（搜尋語法或篩選列）另行規劃，避免同時動到搜尋管線與 `_searchResults` 的存檔封鎖邏輯。
