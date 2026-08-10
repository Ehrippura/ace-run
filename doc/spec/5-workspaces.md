# 第五階段：Workspace 多工作區管理 — 已實作

目標：支援多個獨立的工作區（Workspace），讓使用者可以依照不同情境（如工作、遊戲、開發）組織應用程式，並快速切換。

## 1. Workspace 概念與資料模型

- [x] 每個 Workspace 包含獨立的：
    - 應用程式列表（含資料夾分組結構）
    - 最近啟動記錄
- [x] 視窗大小設計為全域共用，儲存於 WorkspaceConfig（非各 Workspace 獨立）
- [x] Workspace 屬性：
    - Id (GUID)
    - 名稱 (Name)
    - 建立時間 (CreatedAt)
    - 最後修改時間 (LastModifiedAt)
    - 圖示顏色標記 (ColorTag) - 可選，用於視覺區分
    - 項目數量 (AppCount) - 反正規化欄位，儲存時更新
- [x] 新增 WorkspaceConfig 模型，儲存：
    - Workspace 列表
    - 當前選中的 Workspace Id
    - 預設 Workspace Id（可選）
    - 視窗狀態 WindowState（全域共用）

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

- [x] 「管理工作區」對話框，包含：
    - 新建 Workspace（展開式行內表單）
        - 輸入名稱
        - 選擇顏色標記（無、藍、綠、紅、黃、紫）
        - 選項：「建立空白 Workspace」或「複製當前 Workspace」
    - 行內重新命名（TextBox 直接編輯，失焦時儲存）
    - 刪除 Workspace（Flyout 確認，至少保留一個）
    - 設定預設 Workspace（星形切換按鈕）
- [x] Workspace 列表顯示：
    - 顏色標記圓點（有設定時才顯示）
    - 名稱（可直接編輯）
    - 項目數量
- [x] 拖曳排序（影響 ComboBox 順序）

## 5. 匯入/匯出功能

- [x] 匯出 Workspace：
    - 管理對話框中每個項目提供「匯出」按鈕
    - 匯出為 .acerun 檔案（JSON 格式，含完整結構，不含圖示快取）
    - FileSavePicker 讓使用者選擇儲存位置
- [x] 匯入 Workspace（匯入為新 Workspace）：
    - FileOpenPicker 選擇 .acerun 檔案
    - 驗證檔案格式，格式錯誤時以 InfoBar 提示
    - 匯入後作為新 Workspace 加入列表
- [ ] 「合併到當前 Workspace」選項（未實作）
- [ ] 同名資料夾衝突處理（未實作）
- [ ] 匯入後重新提取圖示快取（未實作，圖示於首次啟動時自動重建）
