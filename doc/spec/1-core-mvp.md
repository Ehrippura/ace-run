# 第一階段：核心功能 (MVP) ✓ 已完成

目標：完成最小可行性產品，能存、能看、能跑。

## 1. 新增啟動項目 (Add Item)

- [x] 實作 FileOpenPicker 讓使用者選擇 .exe 檔案。工具列的「新增」現為 SplitButton，主按鈕即此檔案選擇器，下拉另有「新增網址」（第七階段）。
- [x] 自動從選擇的檔案路徑截取檔名作為「預設標題」。

## 2. 項目列表 (List View)

- [x] 顯示已加入的應用程式。最初為 ListView，現為磁磚式 `AppGridView`（`GridView`），左側另有資料夾側欄（第四階段）。
- [x] 每個項目顯示顯示名稱 (Display Name)，檔案路徑以 ToolTip 方式於滑鼠懸停時呈現。

## 3. 執行程式 (Execute)

- [x] 點擊「啟動」按鈕，依據儲存的路徑啟動外部程式。

## 4. 資料持久化 (Persistence)

- [x] 定義資料模型 (AppItem)，包含 Id、DisplayName、FilePath 等屬性。後續階段擴充了 `TagIds`（第六階段）、`Kind`（第七階段）與 `SortKey`（第九階段）。
- [x] 應用程式關閉時，將列表序列化為 JSON 儲存至 `%LOCALAPPDATA%\AceRun\`。最初是單一檔案 `apps.json`；第五階段起改為 `config.json`（工作區清單與設定）加上每個工作區一份的 `workspaces/<guid>.json`，`apps.json` 只以 `.bak` 形式保留為遷移備份。
- [x] 應用程式開啟時，讀取 JSON 並還原列表。首次啟動由 `DataService.MigrateOrInitialize()` 判斷要讀 `config.json` 還是遷移舊的 `apps.json`。
