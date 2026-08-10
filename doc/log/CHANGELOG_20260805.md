# Changelog — 2026-08-05

本文件記錄 2026-07-27 之後（CHANGELOG_20260727.md 之後）的所有新功能、改善項目與 Bug 修正。

---

## 新功能

### 多標籤支援（當時的第九階段，現已併入第六階段）

解除「一個項目只能有一個 tag」的 UI 限制。資料格式未變（`AppItem.TagIds` 本即 `List<Guid>`，`AppData` 維持 v5），舊 workspace 直接可讀。詳見 [doc/spec/6-tags.md](../spec/6-tags.md)（原 `9-multi-tag.md`，已併入第六階段）。

- `AppItemViewModel.Tags` 改為持有 `MainWindow._tags` 的同一批 `TagViewModel` 實例，改名／改色透過 tag 自身的 `INotifyPropertyChanged` 傳播到所有卡片；移除原本的 `TagColorKey` / `TagName` 反正規化欄位與 `RefreshAllAppTagColors()`。
- `NormalizeAppTags()` 改為去除失效 tag、去重並依 workspace tag 順序排序——指派順序刻意不作為使用者狀態，讓相同標籤組合在各卡片上排列一致。
- 右鍵選單改用 `ToggleMenuFlyoutItem` 並以置頂的「清除標籤」取代「無標籤」；編輯對話框改用 `DropDownButton` 包多選 `ListView`（一般 `Flyout` 而非 `MenuFlyout`，才能連續勾選不關閉），選取於 `Loaded` 回填。
- 卡片與搜尋結果列改以 `ItemsControl` 呈現多個圓點，超過三個以 `+N` 表示；無障礙名稱設在整條圓點列而非每顆圓點。
- 三語 `.resw` 新增 `Tag_Clear` / `Tag_Overflow` / `Tag_Separator` / `Tag_Field`（取代已移除的 `TagCombo.Header`）。

---

### 鍵盤快捷鍵（第八階段）

讓 launcher 能完全以鍵盤驅動。快捷鍵為**固定**不可自訂，不新增設定 UI 或持久化欄位，也不含全域熱鍵。詳見 [doc/spec/8-keyboard-shortcuts.md](../spec/8-keyboard-shortcuts.md)。

- `Ctrl+F` / `Ctrl+E` 聚焦搜尋框；`Ctrl+N` 新增應用程式、`Ctrl+Shift+N` 新增網址、`Ctrl+Alt+N` 新增資料夾。
- `Ctrl+B` 切換側欄、`Ctrl+,` 展開齒輪選單、`Alt+Enter` 編輯選取項目、`Ctrl+1`～`Ctrl+9` 切換 workspace。
- `Esc` 情境式清除搜尋 / 收合 Overlay 側欄；側欄焦點時 `F2` 重新命名、`Delete` 刪除資料夾；搜尋框 `Enter` 啟動第一筆結果、`Down` 移至結果清單。
- 宣告機制：Ctrl 系組合鍵用 `KeyboardAccelerator`（掛在 `RootGrid`），無修飾單鍵用特定控制項的 `KeyDown`/`PreviewKeyDown`，兩者刻意不統一（原因見 spec）。
- 新增 `_modalDepth` / `ShowModalAsync` / `RunModalAsync` / `TrackAsModal` 作為 modal 重入防護，避免 flyout 開啟時快捷鍵仍能觸發第二個 `ContentDialog` 或切換 workspace。
- 所有 accelerator 設為 `Hidden` placement，改以 `KeyboardAcceleratorTextOverride` 與 `AutomationProperties.AcceleratorKey` 提供可發現性；三語 `.resw` 新增 `Shortcut_Format` 字串。

---

## 外觀重構（Visual Refresh）

一系列 UI 重構（[PR #1](https://github.com/Ehrippura/ace-run/pull/1)），核心規則：**有彩度＝有情境意義**——只有工作區色（外殼識別）與標籤色（項目識別）帶色，其餘一律無彩，系統 accent 保留給互動控制項。

### 設計層

- 專案首次引入設計層：新增 `Styles/Tokens.xaml`（間距刻度、圓角、字級——採 Segoe UI Variable 三個光學尺寸，不引入自訂字體以避免中日文 fallback 不一致與破壞 High Contrast）與 `Styles/Brushes.xaml`（`ThemeDictionaries`，Light/Dark/HighContrast 三套皆顯式宣告）。
- `ColorTags` 改為查詢主題資源（共用 brush 實例），不再每次 property get 就 `new SolidColorBrush`。
- 字級全面收斂至 14／12 兩種，取代原本散落的字面值（11/12/13/14/16/32）。

### 標題列整併

- 改用 WinAppSDK 1.8 的 `TitleBar` 控制項，工作區選單、搜尋、新增、管理全部收進單一列，取代原本整排工具列 Grid 與 32px 透明拖曳疊層。
- 標題列移除 app 名稱與圖示：工作區選單已是視窗唯一需要的識別，重複的名稱/圖示會把搜尋欄擠離中央；`Window.Title` 與工作列標題不受影響。
- 新增 `Assets/AppIcon.png`（由 `img/app-icon.png` 縮製），供 tray icon 等場景使用。

### 工作區色識別統一

- 色脊（`WorkspaceSpine`）、側欄選取指示條、圖磚選取外框三個識別表面，改為共用同一個 `SolidColorBrush` 實例（覆寫 `ListViewItemSelectionIndicatorBrush` / `GridViewItemSelectedBorderBrush` 等內建 theme resource，不自訂 `ControlTemplate`），切換工作區時三者同步淡變，不再與系統 accent 混用打架。

### 側欄重構

- 改用 `SplitView`：`DisplayMode` / `IsPaneOpen` 由 `Window.SizeChanged` 驅動（<900px 轉為 Overlay 並收合），`TitleBar` 的面板開合鈕提供手動切換。
- 資料夾項目數：`FolderViewModel` 新增 `AppCountText`（掛在 `Apps.CollectionChanged`）；「未分類」因是 `ListView.Header` 而非 `FolderViewModel`，數字改由 `_ungroupedApps.CollectionChanged` 在程式碼維護。
- 工作區 ComboBox 移除多餘的 12px 左邊距（面板開合鈕已提供前導間距）。
- 側欄列高由 30 調整為 36。

---

## 改善項目

### 文件整理：拆分 spec.md

- `doc/spec.md` 原本將所有八個階段的規格塞在同一份文件中，隨著功能增加愈來愈難導覽。拆分為每階段一個檔案，放在新的 `doc/spec/` 目錄下：
    - [1-core-mvp.md](../spec/1-core-mvp.md) — 核心功能 (MVP)
    - [2-advanced-config.md](../spec/2-advanced-config.md) — 進階設定
    - [3-ux-polish.md](../spec/3-ux-polish.md) — 使用者體驗優化
    - [4-tray-and-folders.md](../spec/4-tray-and-folders.md) — System Tray + 資料夾分組
    - [5-workspaces.md](../spec/5-workspaces.md) — Workspace 多工作區管理
    - [6-tags.md](../spec/6-tags.md) — Tag 標籤管理
    - [7-url-items.md](../spec/7-url-items.md) — URL 項目支援
    - [8-keyboard-shortcuts.md](../spec/8-keyboard-shortcuts.md) — 鍵盤快捷鍵
- `doc/spec.md` 保留專案概觀與技術堆疊，第 2 節改為連往上述各檔案的索引表格。
- 各階段原本以 4 空白縮排表示的小節，拆分時一併改為正規的 `##` 標題與 Markdown checkbox 清單（原本的縮排在渲染時會被誤判為程式碼區塊）；文字內容逐字保留未改。
- `CLAUDE.md` 對 `doc/spec.md` 的描述同步更新，說明其現為「概觀 + 索引」而非單一大文件。
