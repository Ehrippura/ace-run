# 第六階段：改進

## 1. Tag 標籤管理

目標：讓使用者可以建立帶有顏色與名稱的 tag 標籤，並將 tag 指派到 app 項目上，提升辨識與分類效率。

### 1. 資料模型與持久化

- [x] 新增 Tag 資料模型，包含 Id、名稱與顏色。
- [x] 在 Workspace 資料中儲存 tag 清單，讓每個 Workspace 可擁有獨立的 tag 設定。
- [x] 在 AppItem 中儲存已指派的 TagId（資料面採 TagIds 清單以保留多標籤擴充性，V1 UI 為單一標籤）。
- [x] 儲存與載入 Workspace 時保留 tag 清單、顏色與 app 的 tag 指派關係。

### 2. Tag 管理功能

- [x] 提供新增 tag 功能，可輸入 tag 名稱並選擇顏色。
- [x] 提供編輯 tag 功能，可修改 tag 名稱與顏色。
- [x] 提供刪除 tag 功能，刪除前顯示確認提示。
- [x] 刪除 tag 後，已套用該 tag 的 app 需自動移除對應 TagId。

### 3. App Tag 指派

- [x] 在新增/編輯 app 對話框中加入 tag 選擇欄位。
- [x] 支援從既有 tag 清單中選擇 tag 並套用到 app。
- [x] 支援在 app 右鍵選單中快速設定或移除 tag。
- [x] 修改 app 的 tag 後即時更新 UI 並持久化。

### 4. App Grid 顯示

- [x] 被設定 tag 的 app，在 app 名稱左側顯示對應 tag 顏色的圓點。
- [x] 未設定 tag 的 app 不顯示圓點，維持原本版面簡潔。
- [x] 圓點顏色需與 tag 管理中設定的顏色一致。
- [x] 搜尋結果列表中同樣顯示 app 的 tag 顏色圓點。
