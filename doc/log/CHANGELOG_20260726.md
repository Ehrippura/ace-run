# Changelog — 2026-07-26

本文件記錄 2026-02-27 之後（CHANGELOG_20260227.md 之後）的所有新功能、改善項目與 Bug 修正。

---

## 新功能

### 標籤（Tag）管理（第六階段）
- 資料模型新增 `TagItem`；`AppData.Tags` 與 `AppItem.TagIds`（資料面採清單保留多標籤擴充性，V1 UI 為單一標籤），`AppData` 版本升至 **4**。
- 新增共用顏色 helper `ColorTags`（6 色）。
- `EditItemDialog` 新增標籤下拉；右鍵選單新增「設定標籤」子選單。
- 新增 **管理標籤對話框（ManageTagsDialog）**：新增／改名／改色／刪除（含確認），並在工具列加入標籤按鈕。
- App grid 與搜尋結果於名稱左側顯示標籤顏色圓點。

### 日文 (ja-JP) 顯示支援
- 新增 `win/Strings/ja-JP/Resources.resw`（全部 79 個字串的日文翻譯）並加入 `.csproj` 的 `EmbeddedResource`。
- `Loc` 服務文化偵測改為三向：`zh`→中文、`ja`→日文、其餘→英文。

### 搜尋結果資料夾標示與快速跳轉
- 搜尋結果每列名稱後方靠右顯示所屬資料夾（未分類的 app 顯示「未分類」標籤）。
- 新增右鍵選單「切換到所在資料夾」：清除搜尋並切換到該 app 所在資料夾（或未分類頁），在 grid 中選取並捲動到該項目。

### System Tray 清除最近啟動記錄
- 系統匣選單新增清除「最近啟動」記錄的功能，並修正啟動後未即時更新 tray 選單的問題。

---

## 改善項目

### Icon 載入效能
- 移除載入 workspace 時對所有 app 的 eager icon 載入，改為 **lazy 載入**：`AppItemViewModel.ReleaseIcon()` 釋放隱藏頁的 `BitmapImage`，切換頁面時才載入可見頁 icon。
- 改用 `ContainerContentChanging` 事件驅動 viewport-level 載入／釋放，取代原本輪詢式的 `LoadVisibleIcons()`。
- 搜尋時動態載入搜尋結果 icon，清除搜尋後恢復當前頁 icon。

### Workspace 與互動體驗
- 記住每個 workspace 上次選中的資料夾（`WorkspaceInfo.SelectedFolderId`），資料夾已刪除則 fallback 到未分類。
- 支援按 **Enter** 鍵啟動選中的應用程式（改用 `PreviewKeyDown` 避免 GridView/ListView 攔截事件）。
- 搜尋結果清單選取模式改為單選。

### UI 版面與無障礙
- App grid 磚塊寬度 96→108、名稱可用寬度 76→92，減少文字過早截斷；有標籤時圓點群組仍在磚塊內不裁切。
- 為純圖示按鈕（管理工作區／標籤、匯出、刪除）補上 `AutomationProperties.Name`；標籤顏色圓點綁定名稱作為可存取名稱，對話框中與名稱重複的圓點標記為 `AccessibilityView=Raw`。
- 內容區新增置中空狀態提示（無應用程式／搜尋無結果），隨集合變更即時更新。
- 明確呼叫 `SetTitleBar` 定義視窗拖曳區，取代原本靠 padding 頂開 caption 的做法。

### 程式碼整理
- `MainWindow.xaml.cs` 拆分為 5 個 partial class 檔案（`MainWindow.Actions/Data/Events/Workspace.cs`），降低單一檔案臃腫。
- 提取 `CreateAppItemFromPath`、`ResetContentState` 消除跨 partial class 的重複邏輯；移除多餘的冗餘 null 檢查。
- 移除殭屍功能：`IsDefault` / `DefaultWorkspaceId`、`AppData.WindowState`（皆已無讀取端）。

### 資料儲存編碼
- JSON 序列化加入 `Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping`（`DataService.cs`、`ManageWorkspacesDialog.xaml.cs`），中日文等非 ASCII 字元不再以 `\uXXXX` 跳脫存放，直接以文字寫入檔案，提升可讀性；不影響既有檔案的讀取相容性。（尚未提交）

---

## Bug 修正

| 修正項目 | 說明 |
|---|---|
| 視窗啟動未帶到前景 | 補上啟動時將視窗帶到前景的呼叫 |
| EditItemDialog 底部欄位被裁切 | 視窗過小時新增／編輯對話框底部欄位被裁切，加入 `ScrollViewer` |
| System tray 選單未即時更新 | 啟動後「最近啟動」清單變動未同步反映到 tray 選單 |
| 切換 folder 後開啟管理工作區會跳回舊 folder | Sidebar 切換 folder 不會即時儲存 `SelectedFolderId`；開啟管理工作區對話框前先 `CommitSave` 持久化目前選取 |

---

## 資料格式變更

### AppData 升級至 Version 4：新增 Tags

```json
{
  "Version": 4,
  "Tags": [ /* TagItem：Id, Name, ColorKey */ ],
  "UngroupedItems": [ /* AppItem，含 TagIds */ ],
  "Folders": [ /* FolderItem，含 Children: AppItem[] */ ],
  "RecentLaunches": [ /* 最近啟動紀錄 */ ]
}
```

載入時會 normalize 失效的 `TagId`（引用到不存在的標籤時自動清除）。
