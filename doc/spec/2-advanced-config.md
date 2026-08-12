# 第二階段：進階設定 (Advanced Config) ✓ 已完成

目標：解決特殊程式（如遊戲、開發工具）的啟動需求。

## 1. 啟動參數 (Arguments)

- [x] 在新增/編輯對話框 (EditItemDialog) 增加「參數」輸入框。
- [x] 支援傳遞參數給執行檔 (例如：chrome.exe --incognito)。

## 2. 自訂工作目錄 (Working Directory)

- [x] 新增項目時，預設將「工作目錄」設為該 .exe 所在的資料夾。
- [x] 允許使用者透過「瀏覽」按鈕手動修改工作目錄。
- [x] 該選擇對話框以**目前工作目錄**為起始位置；欄位為空時退回執行檔所在資料夾（空白工作目錄對被啟動的行程而言即是此值）。

## 3. 管理員模式 (Run as Administrator)

- [x] 在編輯對話框中加入 ToggleSwitch，啟用時以 runas verb 啟動程式。

## 4. 編輯與刪除 (CRUD)

- [x] 實作右鍵選單 (MenuFlyout Context Menu)，包含「Edit」與「Delete」選項。
- [x] 編輯：開啟 ContentDialog，可修改所有欄位（名稱、路徑、參數、工作目錄、管理員模式）。
- [x] 刪除：彈出確認對話框後從列表中移除，變更即時儲存。
- [x] 新增項目時同樣使用編輯對話框，讓使用者在加入前預覽與調整設定。
