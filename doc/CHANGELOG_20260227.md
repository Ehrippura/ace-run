# Changelog — 2026-02-27

本文件記錄 2026-02-19 之後（CHANGELOG_20260219.md 之後）的所有新功能、改善項目與 Bug 修正。

---

## 新功能

### Workspace 多工作區管理（第五階段）
- 工具列新增 **Workspace ComboBox**，可在不同工作區間快速切換，視窗標題顯示目前工作區名稱。
- 新增 **管理工作區對話框（ManageWorkspacesDialog）**，功能包含：
  - 行內新增工作區（即時輸入名稱）
  - 為工作區指定顏色標記（Blue／Green／Red／Yellow／Purple）
  - 拖曳排序工作區列表
  - 設定預設工作區
  - 匯出 / 匯入工作區（`.acerun` 格式）
  - 刪除工作區（含確認 Flyout，禁止刪除最後一個工作區）
- 每個工作區的項目清單獨立儲存；視窗大小由所有工作區共用。

### 單一實例（Single Instance）機制
- 使用 `AppInstance.FindOrRegisterForKey` 確保只有一個 ace-run 進程執行。
- 第二次啟動時自動通知既有實例，並以 `SetForegroundWindow` 將視窗帶至前景（包含縮小至系統匣的情境）。

### 多選操作
- 主清單（GridView）與搜尋結果均切換為 **Multiple 選取模式**。
- 右鍵選單：多選時顯示「刪除 N 個項目」批次刪除選項。
- 鍵盤 **Delete 鍵**：在主清單與搜尋結果中均可觸發批次刪除（含確認對話框）。
- 拖曳排序支援多選項目同時移動。

### 拖曳 exe/lnk 靜默加入
- 外部拖曳 `.exe` / `.lnk` 放入時**不再開啟編輯對話框**，改為直接以預設設定加入清單（名稱取自檔名、工作目錄預設為執行檔所在資料夾）。
- 拖曳時偵測游標下的資料夾節點，放開後項目直接加入該資料夾。

### 側邊欄資料夾拖曳排序
- 側邊欄資料夾清單啟用 `CanReorderItems`，可拖曳重新排列資料夾順序並自動持久化。

---

## 改善項目

### UI 版面重構：TreeView → Sidebar + GridView
- 主界面由 **TreeView** 改為 **Sidebar（左欄 ListView）+ 主區域 GridView** 雙欄版面。
  - 左側 Sidebar 列出「未分類」及所有資料夾，點選後切換主區域內容。
  - 主區域以 **GridView（圖示格狀視圖）** 顯示選定分組的程式項目。
- 「未分類」固定為 `ListView.Header`，不參與拖曳排序。
- `TreeItemTemplateSelector` 及相關 TreeView 邏輯隨重構移除，大幅簡化程式碼。

### 刪除項目後同步清理 RecentLaunches
- 刪除程式項目或資料夾（含子項目）時，同步移除對應的 `RecentLaunch` 紀錄，並立即更新 System Tray 右鍵選單。
- 應用程式啟動時呼叫 `PurgeStaleRecentLaunches()`，清除已不存在的過期紀錄。

### 圖示快取尺寸
- `IconService` 縮圖擷取尺寸從 32px 調整為 **48px**，提升圖示顯示清晰度。

### Sidebar 視覺細節
- Sidebar 項目字體從 12 調整為 **13**，與整體介面比例更協調。
- 縮小 Sidebar 項目列高，版面更緊湊。
- 移除資料夾項目多餘的 `Padding`。

### .NET 版本升級
- 目標框架由 `net8.0-windows10.0.19041.0` 升級至 **`net10.0-windows10.0.22000.0`**。
- 發佈設定改為 **framework-dependent**（移除 self-contained），並啟用 `ReadyToRun`。

---

## Bug 修正

| 修正項目 | 說明 |
|---|---|
| 右鍵點擊空白處顯示空白選單 | 修正右鍵點擊清單空白區域時出現無項目的空白 context menu |
| GridView 項目文字頂緣不一致 | 單行與多行名稱的文字頂緣現一致對齊（Row 1 + `VerticalAlignment="Top"`） |

---

## 資料格式變更

### 儲存架構改版（v3）

原本的單一 `apps.json` 拆分為多檔案結構：

```
%LOCALAPPDATA%\AceRun\
  config.json              # WorkspaceConfig（工作區清單 + 視窗狀態）
  workspaces/<guid>.json   # 各工作區的 AppData（UngroupedItems + Folders）
  apps.json.bak            # 遷移備份（原 apps.json）
```

`AppData` 升版至 **Version 3**，將原 `Items: List<TreeItem>` 拆為兩個獨立集合：

```json
{
  "Version": 3,
  "UngroupedItems": [ /* AppItem */ ],
  "Folders": [ /* FolderItem，含 Children: AppItem[] */ ],
  "RecentLaunches": [ /* 最近啟動紀錄 */ ]
}
```

首次啟動時 `DataService.MigrateOrInitialize()` 自動將舊版 `apps.json` 遷移為新格式，原檔案備份為 `apps.json.bak`。
