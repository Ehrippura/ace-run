# 第七階段：URL 項目支援 — 已實作

目標：讓網址（以及 `steam://`、`obsidian://`、`ms-settings:` 等自訂協定）能與應用程式並列管理，共用同一套 workspace／資料夾／標籤／搜尋／最近啟動機制。

## 1. 資料模型與持久化

- [x] 新增 `ItemKind` 列舉（`App` / `Url`），`AppItem.Kind` 預設 `App`。
- [x] JSON 以字串形式儲存（`JsonStringEnumConverter`），舊 workspace 檔案缺少 `Kind` 時自動視為 `App`，不需 migration。
- [x] 本階段將 `AppData` 版本推進至 5，第九階段再推進至 **6**（現值）。
- [x] `DataService.JsonOptions` 對外公開，`.acerun` 匯入匯出改為共用同一份序列化設定。

## 2. 判斷與正規化

- [x] 新增 `UrlUtil` 服務：`TryNormalize`（可接受任何 absolute URI，scheme 為 `file` 則拒絕；輸入僅有主機名時自動補 `https://`）、`SuggestDisplayName`（取 host 並去掉 `www.`）、`ReadInternetShortcut`（讀取 `.url` 檔的 `URL=`）。
- [x] `example.com:8080` 一類的輸入視為 host:port 而非 scheme。

## 3. 新增入口

- [x] 工具列「新增」改為 SplitButton：主按鈕維持 `.exe` 檔案選擇器，下拉提供「新增應用程式」與「新增網址」。
- [x] 拖曳支援 `StandardDataFormats.WebLink` 與 `Text`（從瀏覽器分頁或網址列拖曳），以及桌面上的 `.url` 網際網路捷徑檔。
- [x] 多格式同時存在時依 StorageItems → WebLink → Text 的優先序只取一次，避免重複新增。

## 4. 編輯與驗證

- [x] `EditItemDialog` 於 URL 模式改用「網址」欄位標題，並隱藏 瀏覽 / 啟動參數 / 工作目錄 / 以管理員身分執行。
- [x] 新增第一個表單驗證：URL 無法正規化時於欄位下方顯示錯誤訊息並阻止對話框關閉。
- [x] 顯示名稱留空時自動以網域補上。
- [x] 項目型別於建立時決定且不可事後切換（`AppItemViewModel.Kind` 為唯讀）。

## 5. 啟動與其他 UI

- [x] `LaunchApp` 對 URL 僅傳 `FileName` + `UseShellExecute`，不帶啟動參數／工作目錄，也不使用 `runas`。
- [x] 無圖示時顯示 Segoe MDL2 fallback 字符：URL 為地球（`E774`），應用程式為預設 app 圖示（`ECAA`）— 同時解決了 exe 路徑失效時格子全空白的問題。
- [x] URL 項目右鍵選單以「複製連結」取代「開啟檔案位置」。
- [x] 搜尋除顯示名稱外一併比對路徑／網址，可用網域搜尋。
- [ ] 抓取網站 favicon 作為圖示（未實作，目前可用「自訂圖示」指定 .ico）。
