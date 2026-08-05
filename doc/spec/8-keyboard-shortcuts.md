# 第八階段：鍵盤快捷鍵

目標：讓 launcher 能完全以鍵盤驅動。快捷鍵為**固定**不可自訂，不新增設定 UI 或持久化欄位，也不含全域熱鍵。

## 1. 快捷鍵配置

- [x] `Ctrl+F` / `Ctrl+E` 聚焦搜尋框。
- [x] `Ctrl+N` 新增應用程式、`Ctrl+Shift+N` 新增網址、`Ctrl+Alt+N` 新增資料夾。
- [x] `Ctrl+B` 切換側欄、`Ctrl+,` 展開齒輪選單、`Alt+Enter` 編輯選取項目。
- [x] `Ctrl+1`～`Ctrl+9` 切換 workspace；超出數量時靜默無作用（不 clamp 至最後一個）。
- [x] `Esc` 情境式：先清除搜尋，其次關閉 Overlay 模式的側欄；不隱藏視窗（無全域熱鍵可叫回）。
- [x] 側欄焦點時 `F2` 重新命名、`Delete` 刪除資料夾；「未分組」為 `ListView.Header` 故兩者皆不作用。
- [x] 搜尋框 `Enter` 啟動第一筆結果、`Down` 將焦點移至結果清單。

## 2. 宣告機制

- [x] Ctrl 系組合鍵為 `KeyboardAccelerator`，統一掛在 `RootGrid`——它是所有可聚焦元素的祖先，且永不隱藏或停用。實測確認在 bare `Window`（非 `Page`）下可正常觸發，焦點位於標題列的 `SearchBox` 時亦然。
- [x] 無修飾單鍵（Esc / F2 / Delete / Down）改用特定控制項的 `KeyDown`：全域的無修飾 accelerator 會在搜尋框有焦點時觸發，按 Delete 就會跳出刪除確認。
- [x] `Ctrl+,`（逗號無具名 `VirtualKey`）與 `Ctrl+1..9`（九段重複標記）於程式碼中宣告，其餘寫在 XAML。
- [x] 不引入 `ICommand`／`XamlUICommand` 層，維持既有的事件處理器風格。

## 3. 實測後修正的三項 WinUI 行為

- [x] **Alt 修飾的 accelerator 不會觸發**：`Alt+Enter` 以 `WM_SYSKEYDOWN` 送達，accelerator 引擎不路由。改在兩個清單既有的 `PreviewKeyDown` 中依 `e.KeyStatus.IsMenuKeyDown` 分支（`LaunchOrEditAsync`）。
- [x] **`AutoSuggestBox` 會吃掉 Down 與 Esc**：兩者供其建議清單使用，bubbling 的 `KeyDown` 收不到，故 `SearchBox` 改用 `PreviewKeyDown`。
- [x] **Flyout 不會抑制 `RootGrid` 的 accelerator**：管理選單開啟時按 Ctrl+2 仍會切換 workspace，留下浮在新 workspace 內容上的舊選單。改以 `TrackAsModal` 將 flyout 的 `Opened`／`Closed` 併入同一個 `_modalDepth`。

## 4. 配套修正

- [x] 新增 modal 重入防護（`_modalDepth` / `ShowModalAsync` / `RunModalAsync` / `TrackAsModal`）：WinUI 同時只允許一個 `ContentDialog`，先前所有入口皆為滑鼠觸發故未暴露此問題。
- [x] 抽出 `EditAppAsync` / `NewFolderAsync` / `ToggleRail`，讓滑鼠與鍵盤共用同一路徑。
- [x] `AppGridView` 的 `PreviewKeyDown` 由 `AttachContextMenus()` 移至 XAML，與 `SearchResultsView` 對稱，且在 `Activate()` 前即生效。

## 5. 可發現性

- [x] 所有 accelerator 設 `KeyboardAcceleratorPlacementMode="Hidden"`，改以 `MenuFlyoutItem.KeyboardAcceleratorTextOverride` 與 `AutomationProperties.AcceleratorKey` 提供提示。
- [x] 修飾鍵名稱（Ctrl / Shift / Alt）不在地化，與 Windows 各語系一致；僅新增 `Shortcut_Format` 一個在地化字串。
- [ ] `TitleBar` 內建側欄切換按鈕位於控制項範本內，無法設 `AcceleratorKey`，`Ctrl+B` 不會被朗讀程式念出（已知限制）。
