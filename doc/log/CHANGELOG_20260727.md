# Changelog — 2026-07-27

本文件記錄 2026-07-26 之後（CHANGELOG_20260726.md 之後）的所有新功能、改善項目與 Bug 修正。

---

## 新功能

### URL 項目支援（第七階段）

網址可與應用程式並列管理，共用同一套 workspace／資料夾／標籤／搜尋／最近啟動機制。允許任何 absolute URI（scheme 為 `file` 除外），因此 `steam://`、`obsidian://`、`mailto:`、`ms-settings:` 等自訂協定同樣可用。

**資料模型**
- 新增 `ItemKind` 列舉（`App` / `Url`），`AppItem.Kind` 預設 `App`；`AppData` 預設版本升至 **5**（僅為文件慣例，程式不讀取此欄位，既有檔案的 `Version` 會維持原值不動）。
- JSON 改以字串儲存列舉（`JsonStringEnumConverter`），延續前一版「JSON 用可讀文字」的方向。
- 舊 workspace 檔案缺少 `Kind` 屬性時自動落回 `App`，**不需 migration**；下次儲存時所有既有項目會補上 `"Kind": "App"`。
- `AppItemViewModel.Kind` 為唯讀（比照 `Id`）：項目型別於建立時決定，不可事後在編輯對話框中切換。

**新增入口**
- 工具列「新增」改為 **SplitButton**：主按鈕維持原本的 `.exe` 檔案選擇器，下拉提供「新增應用程式」與「新增網址」。
- 拖曳支援 `StandardDataFormats.WebLink` 與 `Text`（從瀏覽器分頁或網址列拖曳連結），以及桌面上的 `.url` 網際網路捷徑檔。多格式同時存在時依 StorageItems → WebLink → Text 的優先序只取一次，避免同一個連結被重複新增。

**編輯對話框**
- URL 模式改用「網址」欄位標題與 `https://example.com` 提示文字，並隱藏 瀏覽 / 啟動參數 / 工作目錄 / 以管理員身分執行 這些對 URL 無意義的欄位。
- 新增專案第一個表單驗證：URL 無法正規化時於欄位下方顯示紅色錯誤訊息並阻止對話框關閉（而非讓儲存按鈕默默變灰）。
- 顯示名稱留空時自動以網域（去掉 `www.`）補上。
- 新增 `UrlUtil` 服務集中 URL 邏輯：輸入僅有主機名時自動補 `https://`（可以只打 `github.com`），並把 `example.com:8080` 一類的輸入正確視為 host:port 而非 scheme。

**啟動與其他 UI**
- `LaunchApp` 對 URL 僅傳 `FileName` + `UseShellExecute`：不帶啟動參數（ShellExecute 會把它交給協定 handler 而非 URL）、不帶工作目錄，也不使用 `runas`。
- URL 項目右鍵選單以「複製連結」取代「開啟檔案位置」。
- 搜尋除顯示名稱外一併比對路徑／網址，可用網域關鍵字搜到 URL 項目。
- 系統匣「最近啟動」原本就相容 URL，無需改動。

---

## 改善項目

### 圖示 fallback
- 無法載入圖示時改為顯示 Segoe MDL2 fallback 字符，取代原本的空白格：URL 顯示地球（`E774`），應用程式顯示預設 app 圖示（`ECAA`）。這順帶修掉了 **exe 路徑失效時格子全空白、沒有任何提示**的舊行為。
- URL 項目仍可透過「自訂圖示」指定 `.ico`；`IconService` 原本的 `CustomIconPath` 優先邏輯已能正確處理，未做修改。

### 序列化設定共用
- `DataService` 與 `ManageWorkspacesDialog` 原本各有一份重複的 `JsonSerializerOptions`。改為公開 `DataService.JsonOptions` 並由兩邊共用，避免 `.acerun` 匯入匯出與主資料的設定日後走鐘。

### Tooltip 即時更新
- App grid 與搜尋結果的路徑 tooltip 原為 one-time binding，編輯後會停留在舊值；改為 `Mode=OneWay`。

---

## 本地化

- `en-US` / `zh-TW` / `ja-JP` 三個 `.resw` 各新增 8 個字串（`AddMenuAppItem.Text`、`AddMenuUrlItem.Text`、`AddUrlTitle`、`EditUrlTitle`、`CopyUrlMenuItem`、`UrlFieldHeader`、`UrlFieldPlaceholder`、`Validation_InvalidUrl`），共 87 個 key 且三個 locale 完全一致。
- `Empty_NoApps` 空狀態文字改為同時提到可拖曳 `.exe` 或連結。
