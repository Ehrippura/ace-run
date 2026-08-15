# 第五階段：Workspace 多工作區管理 — 已實作

目標：支援多個獨立的工作區（Workspace），讓使用者可以依照不同情境（如工作、遊戲、開發）組織應用程式，並快速切換。

## 1. Workspace 概念與資料模型

- [x] 每個 Workspace 包含獨立的：
    - 應用程式列表（含資料夾分組結構）
    - 最近啟動記錄
- [x] 視窗大小設計為全域共用，儲存於 WorkspaceConfig（非各 Workspace 獨立）
- [x] `WindowState` 存的是 **DIP**（`WidthDip` / `HeightDip`），不是實體像素。原本直接存 `AppWindow.Size`，而檔案裡沒有任何欄位記得那是在哪個縮放下量到的——在 100% 螢幕調好的大小，下次於 150% 螢幕啟動就還原成 2/3 的邏輯尺寸。單位改變靠**鍵名**而非 `WorkspaceConfig.Version` 區分：`SaveConfig` 每次寫入都會蓋上當前版本號，而啟動修復（`MigrateOrInitialize` → `EnsureUsable`）可能在視窗尺寸重存之前就觸發它，版本閘門會把還是像素的值標記成 DIP。舊檔的 `Width` / `Height` 只作為遷移輸入讀取，用當前 scale 換算一次（在使用者用同一台螢幕啟動時正確，否則也不比舊行為差），之後只寫 DIP 那組。
- [x] Workspace 屬性：
    - Id (GUID)
    - 名稱 (Name)
    - 建立時間 (CreatedAt)
    - 最後修改時間 (LastModifiedAt)
    - 圖示顏色標記 (ColorTag) - 可選，用於視覺區分
    - 項目數量 (AppCount) - 反正規化欄位，儲存時更新
- [x] 新增 WorkspaceConfig 模型，儲存：
    - Workspace 列表
    - 當前選中的 Workspace Id（`ActiveWorkspaceId`）
    - 視窗狀態 WindowState（全域共用）
    - 「預設 Workspace Id」不做——啟動本來就回到上次使用的工作區，在單人使用的桌面工具上「預設」與「使用中」是同一件事的兩個名字。原先此處與 §4 都列為已完成，但模型裡從來沒有這個欄位、UI 上也從來沒有那顆星形按鈕，只留下一個沒有任何引用的 `Workspace_Default` 字串（已一併刪除）。

## 2. 資料儲存架構調整

- [x] 改為多檔案儲存結構：
    - %LOCALAPPDATA%\AceRun\config.json - 儲存 WorkspaceConfig（含全域視窗狀態）
    - %LOCALAPPDATA%\AceRun\workspaces\<workspace-id>.json - 各 Workspace 資料
    - 圖示快取維持共用：%LOCALAPPDATA%\AceRun\icons\
- [x] 向下相容處理：
    - 首次升級時，將現有 apps.json 轉換為「預設」Workspace
    - 原 apps.json 重新命名為 apps.json.bak 保留備份
- [x] 自動儲存機制：
    - 切換 Workspace 時自動儲存當前 Workspace
    - 修改資料時更新 LastModifiedAt 時間戳

## 3. Workspace 切換器 UI

- [x] 頂部工具列新增 Workspace ComboBox
    - 顯示當前 Workspace 名稱與顏色標記圓點
    - 下拉選單列出所有 Workspace
- [x] 工具列新增「管理工作區」按鈕（齒輪圖示）
- [x] 切換行為：
    - 儲存當前 Workspace 的完整狀態
    - 清空目前的 UI 列表與搜尋狀態
    - 載入選中 Workspace 的資料
    - 視窗大小為全域共用，切換時不重置
- [x] 視窗標題列顯示當前 Workspace 名稱（格式：「Ace Run — <名稱>」）

## 4. Workspace 管理功能

- [x] 「管理工作區」對話框與「管理標籤」共用同一個列型：`⠿ 握把 / 顏色按鈕 / 名稱 TextBox / 中繼資訊 / ⋯ 溢位選單`。共用的是樣式（`Styles/ManageList.xaml`）與行為（`Services/ColorSwatchFlyout.cs`、`Services/ManageRowMenu.cs`）；兩個 `DataTemplate` 各自留在自己的檔案裡，因為沒有 `x:Class` 的 `ResourceDictionary` 不能承載 `x:Bind`，而且兩者的中繼欄位本來就不同。
- [x] 新建 Workspace 為 `DropDownButton` 兩個選項：「建立空白工作區」/「複製目前工作區」。選完**立即建列並讓名稱欄取得焦點**，不再有展開式行內表單。移除的表單同時是三個缺陷的來源：其中按 Enter 會觸發對話框的 `DefaultButton`（＝關閉）把整個對話框關掉、名稱欄唯一的標籤是 placeholder、以及按鈕在頂端而表單長在最高 360 DIP 的清單底下。代價是未輸入就關閉會留下一個叫「新工作區」的列——可見且一個選單點擊即可刪除。
- [x] 新列的預設名稱若已被占用會自動加序號（新工作區 2、3…），否則連按兩次「新增」會撞到自己的重名檢查。
- [x] 行內重新命名（TextBox 直接編輯）。欄位改為**正常帶邊框的 TextBox**：原本是 `BorderThickness=0` + 透明底，看起來就是純文字，沒有任何線索說它可以改。
    - Enter 提交、Esc 還原，兩者都 `e.Handled = true`——不攔的話它們會冒泡到 ContentDialog 的預設鈕與取消路徑。
    - 提交對象是 `GotFocus` 時捕獲的 view model，並在寫入前比對 `ReferenceEquals(tb.DataContext, vm)`。讀 `LostFocus` 當下的 `DataContext` 是資料毀損路徑：ListView 的拖曳重排用 `RemoveAt` + `Insert` 變更來源、刪除會把下方每一列往上推，容器都可能在編輯與提交之間被回收到另一個工作區上。
    - 重名（忽略大小寫）以 InfoBar 提示並還原欄位。
- [x] 顏色可在建立後隨時修改——列上的顏色按鈕開出色票面板。在此之前 `WorkspaceViewModel.ColorTag` 的 setter 全專案沒有任何呼叫端，顏色是建立當下寫死一次的，儘管它同時驅動視窗外框、rail 選取指示與選取磚邊框。改色即時寫入 config；畫面上的外框在對話框關閉、`ReloadAfterWorkspaceManagement` 重新套用識別時才重繪。
- [x] 色票面板（`ColorSwatchFlyout`）：兩欄格狀，來源是 `ColorKeys.All`（藍、綠、紅、黃、紫、灰，工作區另有「無」共 7 項），目前選中者加勾。
    - 每格都顯示**本地化的顏色名稱**。列上只有一顆點、不出現顏色文字，但面板上必須有：High Contrast 字典刻意把六個 `AceTagBrush*` 全部塌成 `SystemColorWindowTextColor`，沒有文字就是六個一模一樣的圓。勾選記號同理，彩色圈在那裡看不見。
    - 筆刷經 `ColorTags.ResolveBrush(key, theme)` 而非 app 主題查找。`Application.Current.Resources` 答的是 app 主題（生命週期內固定為系統主題），OS 深色 + app 設淺色時實測面板畫 `#0F6CBD`、列上的點畫 `#62ABF5`——使用者在面板選了一個藍，列上出現另一個藍。`ColorTags.GetBrush` 也一併改走同一條路徑，所以磚上的 tag 點、工作區選擇器的點都跟著對齊。
    - 面板與 ⋯ 選單都經 `ThemeService.ApplyTo(FlyoutBase)` 明確套用主題。Flyout 掛在 popup root 上，跟 `ContentDialog` 一樣不繼承元素主題。
- [x] ⋯ 溢位選單取代每列常駐的匯出／刪除圖示鈕：匯出 / 上移 / 下移 / ── / 刪除。三顆常駐控制項在六列的清單裡是視覺噪音，也讓每列吃掉四個 tab stop。
    - 刪除仍走 `ConfirmFlyout`，但**必須延後一拍 dispatcher**：`MenuFlyoutItem.Click` 在承載它的選單還在關閉時觸發，關閉中的 light-dismiss 層會吃掉新開的彈出層，刪除會時靈時不靈。錨點用仍存活的 ⋯ 按鈕，絕不用 `MenuFlyoutItem`（它的視覺父層正在被拆掉）。
    - `ConfirmFlyout` 排成 `ContentDialog` 的形狀：標題、內文、分隔線、下方兩顆等寬按鈕，並沿用 `ContentDialogBackground` / `ContentDialogTopOverlay` / `ContentDialogSeparatorBorderBrush`（這些鍵名不是跨版本的契約，每一個都以確定存在的 Fluent 筆刷作為 fallback）。**是複製外觀而非借用控制項**——它從 `ContentDialog` 內部發出，巢狀的那個會靜默地開不起來。兩顆按鈕都不上 accent，比照 WinUI `DefaultButton="None"` 的呈現，破壞性動作也不該是視線第一個落點。
    - presenter 的 `Padding` 歸零、內容不設固定 `Width`：內容若剛好等於 presenter 寬度，左右各 1px 的框線會讓它溢出，實測會在按鈕下方長出一條水平捲軸。
    - 標題另立字串（`Workspace_DeleteTitle` / `Tag_DeleteTitle`），比照 `DeleteFolder` + `DeleteFolderContent` 的既有慣例；原本的單句確認文字改當內文。
    - 上移／下移是**唯一的鍵盤重排路徑**。列內有 TextBox，會搶在容器之前拿到焦點，所以 `ListView` 自己的 Ctrl+Shift+方向鍵重排永遠碰不到。用 `ItemOrdering.MoveBy`（`ObservableCollection.Move`）而非 Remove+Insert，容器才會跟著項目移動、焦點留在 ⋯ 上，連按才有效。
- [x] Workspace 列表顯示：
    - 顏色按鈕（無顏色時是空心圈，本身就讀作「未設定」）
    - 名稱（可直接編輯）
    - 「使用中」標示與項目數量。中繼欄位寬度**固定**而非 `Auto`：ListView 每一列各自量測，Auto 會讓帶「使用中」的那列把星號欄擠窄，名稱欄右緣參差不齊。
    - 列容器經 `ItemContainers.BindAutomationName` 取得可讀名稱，否則螢幕報讀器念的是 `ace_run.WorkspaceViewModel`。
- [x] 刪除 Workspace（Flyout 確認，至少保留一個）
- [x] 重名以 InfoBar 擋除
- [x] 拖曳排序（影響 ComboBox 順序，也重新對應 Ctrl+1..9）
    - `⠿` 握把是純 `FontIcon`，不可包進 `Button`：`TextBox` 與 `Button` 會在按下時捕獲指標，事件到不了 `ListViewItem` 的拖曳偵測。在握把出現之前，工作區列可拖曳的區域只剩那顆 16 DIP 的色點與欄間隙，這就是拖曳排序「時靈時不靈」的原因。它靠冒泡到容器才能啟動拖曳——WinUI 沒有從程式碼發動 ListView 重排的公開 API。

## 5. 匯入/匯出功能

- [x] 匯出 Workspace：
    - 管理對話框中每列的 ⋯ 選單提供「匯出」
    - 匯出為 .acerun 檔案（JSON 格式，含完整結構，不含圖示快取）
    - FileSavePicker 讓使用者選擇儲存位置
- [x] 匯入 Workspace：「匯入」改為 `DropDownButton`，兩個選項「匯入為新工作區」/「合併到目前工作區」。目的地在開檔案挑選器**之前**就選定，不是選完檔案再問——合併會寫進一個已經有內容的工作區，該考慮的時間點是在選檔案之前，而不是在一個本來就是 modal 的對話框上再疊一層確認。
    - FileOpenPicker 選擇 .acerun 檔案
    - 驗證檔案格式，格式錯誤時以 InfoBar 提示
    - 匯入為新工作區：作為新 Workspace 加入列表；名稱比照手動新增去重，兩個同名項目在切換器裡無從分辨
- [x] 「合併到目前工作區」：把 `.acerun` 折進對話框背後那個工作區（`WorkspaceMerge.Merge`，core 層純函式）。
    - **合併只增不改**：目的地既有的任何東西都不會被改名、改色、重排或刪除。
    - **每個匯入的項目與資料夾都重新配一個新 Id，來源的 Id 一律丟棄。** Id 在跨檔案時並不唯一：匯出後再合併回同一個工作區、或合併一份「複製目前工作區」建立出來的匯出檔，拿到的就是目的地已經在用的 Id。圖示快取以 `AppItem.Id` 為鍵、而磁碟上沒有任何東西記得某個 Id 屬於哪個工作區，撞號會讓兩個項目共用一份快取，刪掉其中一個另一個的圖示就跟著空掉。代價是匯入的項目沒有現成快取，於載入時重新擷取。
    - **同名資料夾合併，不重複建立**：名稱去空白、忽略大小寫比對，命中就把子項目接到既有資料夾後面，資料夾本身保留原 Id（側邊欄、返回堆疊與 `SelectedFolderId` 指的都是它）。比對對象是**合併後的結果清單**，所以兩個同名的來源資料夾也會落進同一個——它們對使用者本來就無從分辨，另一種做法反而會留下正要避免的重複。
    - **同名標籤沿用既有的，顏色不動**：目的地的調色盤是使用者自己的，匯入不構成重新上色的理由。新標籤帶著自己的顏色進來，但同樣不留 Id。項目的 `TagIds` 經 `TagOrdering.InWorkspaceOrder` 重新對應，順帶落回工作區順序、丟掉對應不到的 Id——磚上標籤點對齊靠的就是這個不變條件。（`TagItem` 為此實作 `ITagRef`：合併時還沒有 view model。）
    - **項目一律不比對**：兩個指向同一個 .exe 的項目是合理的（不同參數、不同工作目錄），合併掉會毀掉使用者刻意留下的資料。
    - 最近啟動記錄接在目的地自己的後面再截到上限。目的地那幾筆是這台機器上真實的啟動歷史，匯入的則是別處發生的事。項目沒跟著過來的那幾筆直接丟棄——這份清單以項目 Id 為鍵、搜尋把位置當排名讀，懸空的一筆什麼也排不到。
    - 寫入使用中工作區的檔案之所以安全，靠的是 `_config` 註解描述的那組括號：`CommitSave()` 已把 MainWindow 的副本刷到磁碟，`ReloadAfterWorkspaceManagement()` 在對話框關閉時把檔案（含合併進來的項目）重新讀回來。
    - 項目數經 view model 的 setter 寫回，直接改 `WorkspaceInfo.AppCount` 會讓那一列的數字停在舊值直到重開對話框。
    - `InfoBar` 從只報錯改為 `MessageBar`，成功時以 `Severity="Success"` 顯示 `Workspace_MergeDone`：合併落在對話框**背後**的工作區，畫面上唯一的痕跡只有某一列的項目數，不說就等於沒有回饋。
- [x] 兩顆 `DropDownButton` 在 XAML 宣告的 `MenuFlyout` 也走 `ThemeService.ApplyTo`。Flyout 掛在 popup root 上，不在承載對話框 `RequestedTheme` 的元素樹裡——和色票面板、⋯ 選單是同一個問題。
- [ ] 匯入後重新提取圖示快取（未實作，圖示於首次啟動時自動重建）

## 6. 在地化

- [x] 三份 `.resw` 同步新增：`Workspace_InUse`、`Workspace_DuplicateName`、`Workspace_DeleteTitle`、`Color_Choose`、`Color_None`、`Color_{Blue,Green,Red,Yellow,Purple,Gray}`、`Row_More`、`Row_MoveUp`、`Row_MoveDown`、`Row_Reorder`、`Workspace_MergeDone`。
- [x] `Workspace_ImportAsNew` / `Workspace_ImportMerge` 原本是為了未實作的合併功能保留、全專案無引用的兩個字串，現在是匯入下拉選單的兩個項目。
- [x] `Color_*` 的鍵名後綴就是持久化的 `ColorKeys` 值，兩者必須同進退。
- [x] 刪除：`Workspace_Default`（功能不做，見 §1）、`Workspace_NewTitle`（表單移除）、`Workspace_ColorLabel`（由 `Color_Choose` 取代）、`Workspace_Rename` 與 `Workspace_Delete`（重新命名是列內行為、刪除改用通用的 `DeleteButton`）。
- [x] 對話框內的字串一律 `Loc.GetString`，不用 `x:Uid`——不是因為 `x:Uid` 在此不可用（見第九階段 §4 的更正），而是 ⋯ 選單項與色票全是程式碼建構的元素，那裡沒有 `x:Uid` 可用，混用兩套沒有好處。
