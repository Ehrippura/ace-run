# Changelog — 2026-08-10

本文件記錄 2026-08-05 之後（CHANGELOG_20260805.md 之後）的所有新功能、改善項目與 Bug 修正。

---

## 新功能

### 設定畫面（第十階段）

在此之前應用程式層級的行為全部寫死：關閉視窗一律縮到系統匣、主題與語言完全跟隨系統、沒有全域快捷鍵。本次補上一個獨立的設定視窗，把四組行為開關化。詳見 [doc/spec/10-settings.md](../spec/10-settings.md)。

- **全域快捷鍵**：`RegisterHotKey` + `SetWindowSubclass` 掛在主視窗 HWND 上攔 `WM_HOTKEY`，一律加 `MOD_NOREPEAT` 避免按住連發。預設停用——開箱就搶走一組全域鍵是敵意行為。組合鍵被其他程式占用時會提示衝突並還原舊綁定，不靜默失敗。
- 快捷鍵喚出視窗後焦點直接落在搜尋欄並選取既有查詢，下一個按鍵就是新的搜尋。焦點設定必須排進 dispatcher 佇列，視窗還沒真的到前景時設焦點不會生效。只有快捷鍵這條路徑會這樣做，點托盤圖示不會。
- **開機自動啟動**：寫 HKCU 的 Run 機碼，附 `--tray` 參數；該參數啟動時不呼叫 `Activate()`，直接進系統匣。
- **關閉行為**：`CloseToTray` 開關（預設開啟，維持原行為）。順帶修正關閉即結束這條路徑原本只讓視窗關掉就 return、托盤圖示仍活著導致行程不會退出的缺陷。
- **啟動後隱藏**：`HideOnLaunch`，批次啟動時只在最後一項之後隱藏一次。
- **主題與語言**：主題即時生效；語言需重新啟動，設定視窗以 `InfoBar` 說明。
- `WorkspaceConfig` 新增 `Settings`（`CurrentVersion` 1 → 2），舊檔缺鍵時保留屬性初始值，不需 migration 程式碼。設定放在 `config.json` 而非各 workspace——這些是跟著使用者而非工作區的偏好。
- 設定視窗開啟時於主視窗上置中。不指定位置時由 OS 的層疊規則決定，實測會落在離主視窗很遠、甚至另一個螢幕的地方。

### 整理與排序鍵（第九階段）

項目順序在此之前只有一種來源——使用者拖曳。側邊欄右鍵新增「整理」，可依名稱／路徑／標籤／排序鍵一次性重排，並可反轉順序。詳見 [doc/spec/9-organize.md](../spec/9-organize.md)。

- **一次性重排**，不是持續排序模式：排完的結果就是新的手動順序，因此不需要在 `FolderItem` 上新增排序狀態，拖曳排序也不必停用。
- `AppItem` 新增 `SortKey`——名稱、路徑、標籤都表達不了的自訂排序字串，只有整理會讀，不進搜尋比對也不上圖磚。`AppData` 版本升至 6。
- 套用改用 `ObservableCollection.Move` 而非 `Clear()` + `Add()`：後者的 `Reset` 會回收所有 GridView 容器並重載每個圖示，畫面會閃。
- 右鍵未分組列在此之前完全無反應（它是 `ListView.Header` 而非集合項目，`ItemFromContainer` 對它回 null），本次一併修正。

### 上一頁

- 新增回到前一個資料夾的功能，`Alt+Left` 與滑鼠側鍵皆可觸發。兩者都需要 `handledEventsToo: true`——`ListViewBase` 會把方向鍵與 `PointerPressed` 標記為已處理。
- 資料夾切換收斂到單一入口 `NavigateToFolder`，避免新的進入點漏掉記錄堆疊。歷史為 session-only，彈出時重新解析 id，已刪除的資料夾自動跳過。

### 多選與拖曳到資料夾

- app 項目支援多選（`SelectionMode="Extended"`），所有命令都以批次處理，單選視為一個項目的批次。批次操作只在最後存檔一次。
- 可將選取的項目拖曳到左側資料夾列或「未分類」列，目標列會亮起提示。拖曳期間必須暫時關閉側邊欄的 `CanReorderItems`，否則 WinUI 會把它當成跨清單搬移、撐開插入間隙，該間隙裡沒有可命中的列。

### 視窗圖示

- Alt+Tab 與工作列改用 app 圖示。圖示以 `EmbeddedResource` 內嵌，不走 `ms-appx`——後者在 Release 建置下會消失（本專案為 unpackaged）。

---

## 改善項目

### 搜尋

- 比對條件從名稱／路徑擴充到**啟動參數與標籤名稱**。標籤逐個比對 `Tag.Name` 而非 `TagsSummary`——後者是以分隔符 join 的無障礙字串，查詢會跨過接縫誤中兩個標籤名的組合。
- 打字改走 180ms debounce。模式切換與清空維持同步，只有結果清單延後：`_searchText` 同時是存檔保護的旗標，延後它會出現空窗。打字期間不清空舊結果，否則每個按鍵之間會閃一下「找不到符合項目」。
- **結果依相關性排序**。原本完全沒有排序，命中項目照走訪順序 append，而 `SelectedIndex = 0` 讓 Enter 啟動第一筆，等於 Enter 的目標與相關性無關。現在排序鍵為「最近啟動索引 → 命中分級（名稱前綴／名稱子字串／標籤／路徑）→ 走訪序」，最近使用絕對優先。
- 搜尋結果的圖示改為逐列延遲載入。原本對每一筆命中都載入，命中 200 筆就是 200 次磁碟讀取加解碼，畫面上卻只有約 10 列。
- 每輪搜尋後預選第一項（只設選取、不動焦點）；Enter、Down 與視窗關閉前都先沖掉待處理的 debounce，否則會讀到上一輪或空的清單。
- 從側邊欄切換資料夾時清除搜尋。

### 標題列

- 標題列改為自繪 `Grid`，收掉右側約 120 DIP 的死區。WASDK 的 `TitleBar` 控制項把實體像素的 `RightInset` 直接塞進 DIP 的 `ColumnDefinition`，在 150% 縮放下留下一段沒有任何公開屬性搆得到的空白；自繪版本是同一套算術但有做除法。
- 標題列控制項改個別註冊為 `NonClientRegionKind.Passthrough`（不是註冊父面板，那樣會讓間隙不能拖曳），修正搜尋框沒有文字游標的問題。
- 搜尋欄左右留白加寬至 30 DIP，放大鏡圖示移到左側。

### 工作區色識別

- 工作區色從左側色脊改為整圈視窗外框：`RootGrid` 的最後一個子元素、`Grid.RowSpan="2"`，連標題列一起框成不間斷的矩形，並依視窗狀態重算圓角。沒有設定顏色的工作區不顯示外框，做法是淡出整個外框的 `Opacity`，而不是把透明色推進共用筆刷——那個筆刷同時也畫側邊欄指示條與選取圖磚的外框。

### 其他

- 側邊欄收窄的斷點從 900 DIP 調整為 800 DIP。
- 盤點全專案 8 個 Win32 P/Invoke，以 WinUI API 取代其中三個（`IsIconic` + `ShowWindow` → `OverlappedPresenter.State` / `Restore()`，`GetKeyState` → `InputKeyboardSource.GetKeyStateForCurrentThread`），其餘五個經官方文件確認確實沒有對應品。
- 統一行尾：`win/` 資料夾用 CRLF，其餘用 LF，並補齊檔案結尾換行。

---

## Bug 修正

- **資料檔版本號永遠不會更新**。`Version` 原本只是屬性初始值，載入時被檔案裡的舊值覆蓋、存檔時又原封寫回，實機上的檔案停在 3 而常數已經是 5。改為在寫入時蓋章——版本號描述的是寫入者產生的形狀，不是讀到的形狀。`.acerun` 匯出原本繞過 `DataService` 直接序列化，會把舊版本號帶進匯出檔，一併改走同一條路徑。
- **語言設定無法套用**。MRT 解析資源時只看自己的 `ResourceContext`，**從不讀 `CultureInfo`**；原本只設 `CurrentUICulture`，那只影響 .NET 格式化與 `.resw` fallback 字典，於是設定看起來接好了、每個可見字串卻仍是系統語言。改設 `ApplicationLanguages.PrimaryLanguageOverride`，它才同時驅動 `ResourceLoader` 與 `x:Uid`。
- 修正清單項目的無障礙名稱。
- 修正搜尋框沒有文字游標（見上方標題列 Passthrough）。

---

## 建置與發佈

- 發佈改為 **self-contained**：同時打包 .NET 與 Windows App SDK。兩個開關（`SelfContained` / `WindowsAppSDKSelfContained`）互不蘊含且缺一不可，因為一般使用者不會裝 WindowsAppRuntime，且它綁版本帶。
- **停用 trimming**。WinUI 3 以反射解析 XAML 型別，裁切後的建置可以正常發佈、然後在啟動時死在 `Microsoft.UI.Xaml.dll` 裡。裁切只在 Release publish 時啟用，所以一般建置不會警告。
- 新增 framework-dependent 的發佈設定檔（約 41 MB，目標機器需預裝 .NET 10 Desktop Runtime 與 WindowsAppRuntime 1.8）。
- 新增 GitHub Actions 的 CI 與 Release workflow，並更新所用 action 至最新 major 版本。
- 更新 app 圖示；README 補齊未記載的功能與圖片。

---

## 文件

### spec 狀態欄有了定義

狀態欄原本混用「✓ 已完成」與「已實作」，但兩者沒有定義，差別純粹是文件拆分時留下的痕跡。現在明確定義：**✓ 已完成** = 規格範圍內項目全數實作（檔案末「不在本階段範圍」的延伸想法不計入）；**已實作** = 主要功能可用但規格內仍有缺，括號註明。依此重判，第五階段降為已實作（匯入的合併選項、同名資料夾衝突處理確實未做），其餘階段升為已完成。

### 維護規則：改到既有項目就修正該階段，不要開新階段

階段檔案原本是編年史而非規格——後期階段大量修改前期階段定義的東西，前期檔案卻從未回頭更新，導致只讀單一檔案會被誤導。規則寫進 `doc/spec.md` 與 `CLAUDE.md`，並回頭處理既有的失真：

- 原第九階段「多標籤支援」整篇都在解除第六階段的單標籤限制，已併入 `6-tags.md`；第六階段原本寫著「V1 UI 為單一標籤」，正是這條規則要防的實害。
- 修正 9 處已失真的敘述：第一階段的 ListView／`apps.json`／`AppItem` 欄位／新增入口，第三階段的圖示 fallback／拖放網址／搜尋比對路徑／缺日文，第四階段的關閉至系統匣已成可關閉偏好，第七階段的 `AppData` 版本號。
- 合併後的編號缺口以重新編號補齊：`10-organize.md` → `9-organize.md`，`11-settings.md` → `10-settings.md`，所有交叉引用同步更新。

### changelog 移入 `doc/log/`

`doc/` 下的六份 changelog 全部移入 `doc/log/`，`doc/` 只留 `spec.md` 與 `spec/`。移動後檔案內指向 spec 的相對連結一併改為 `../spec/`。
