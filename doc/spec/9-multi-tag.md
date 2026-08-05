# 第九階段：多標籤支援

目標：解除第六階段「一個 app 只能有一個 tag」的 UI 限制，讓單一項目可同時掛上多個標籤。資料面早已為此保留擴充性，本階段補齊 ViewModel 與 UI。

## 1. 資料模型

- [x] `AppItem.TagIds` 本即 `List<Guid>`，格式不變、`AppData` 維持 v5、不需 migration，`.acerun` 匯入匯出自動相容。
- [x] 新檔被舊版本讀取時僅保留第一個 tag（舊版 `NormalizeAppTags` 的既有行為），屬可接受的降級而非資料損毀。

## 2. ViewModel：以實例取代反正規化欄位

- [x] `AppItemViewModel.Tags` 改為 `ObservableCollection<TagViewModel>`，直接持有 `_tags` 中的同一批實例。
- [x] 移除 `SetSingleTag` / `TagColorKey` / `TagName` / `TagBrush` / `TagVisibility`；`ToModel()` 由 `Tags` 推導 `TagIds`，不再保留獨立狀態。
- [x] `TagViewModel.ColorKey` 的 setter 已通知 `ColorBrush`，改色／改名即時反映到所有卡片，故 `RefreshAllAppTagColors()` 與 `ResolveAppTagDisplay()` 一併刪除。
- [x] 新增 `VisibleTags`（前 3 個，卡片僅 124px 寬）、`OverflowLabel`（`+N`）、`TagsSummary`（名稱串接，供無障礙與下拉按鈕共用），由 `SetTags` 觸發通知。
- [x] 建構子加 `IReadOnlyList<TagViewModel>` 參數解析 id → 實例（四個呼叫點）；`LoadWorkspaceDataAsync` 已是先填 `_tags` 再建 app VM，順序天然正確。

## 3. 指派邏輯

- [x] `NormalizeAppTags()` 改為：移除已刪除的 tag、去重、依 `_tags` 順序排序。固定順序讓同一組 tag 在每張卡片上呈現一致，也省去額外的 tag 排序 UI。
- [x] `ApplyTagToApp` 拆為 `ToggleTagOnApp(app, tag, on)` 與 `ClearTagsOnApp(app)`，兩者保留既有「搜尋模式下改走 `CommitSave()`」的分支。

## 4. 編輯對話框

- [x] `TagCombo` 改為 `DropDownButton` + `Flyout` 包 `SelectionMode="Multiple"` 的 `ListView`（非 `MenuFlyout`，才能連續勾選不關閉）；按鈕內容顯示圓點與名稱摘要，滿三個改 `+N`。
- [x] `DropDownButton` 無 `Header` 屬性，需自行補標籤 `TextBlock` 並以 `Loc.GetString` 設定文字。
- [x] 多選 `ListView` 在 `ItemsSource` 指派後立即寫 `SelectedItems` 不穩（容器尚未實體化），改於 `Loaded` 回填並以旗標擋掉回填期間的 `SelectionChanged`。
- [x] workspace 尚無任何 tag 時按鈕停用並顯示 `Tag_Empty`。

## 5. 顯示

- [x] 卡片的單一 `Ellipse` 改為固定高度容器包 `ItemsControl`（水平 StackPanel、7px 圓點）加溢位文字；容器高度固定，空清單不改變卡片高度。實作時該列高度由 `7px` 調整為 `16px`（margin 由 8 調為 4），因為溢位的 `+N` 使用 12pt 的 `AceCaptionStyle`；實測卡片總高不變。
- [x] 搜尋結果列沿用同一組合（8px 圓點），該欄 `Auto` 寬度在無 tag 時自然收合。
- [x] `AutomationProperties.Name` 設在容器（值為 `TagsSummary`）而非每顆圓點，否則朗讀程式會逐點朗讀。

## 6. 右鍵選單

- [x] `RadioMenuFlyoutItem` + `GroupName` 改為 `ToggleMenuFlyoutItem`；「無標籤」改為置頂的「清除標籤」加分隔線。
- [x] Flyout 點選後即關閉為 Windows 標準行為，一次指派多個標籤請走編輯對話框——此即對話框選用可連續勾選控制項的原因。

## 7. 在地化

- [x] 新增 `Tag_Clear`、`Tag_Overflow`（`+{0}`）、`Tag_Separator`（zh-TW／ja 用「、」，en 用「, 」）、`Tag_Field`（取代已移除的 `TagCombo.Header`），三份 `.resw` 同步更新。

## 8. 不在本階段範圍

- [ ] 依 tag 篩選（搜尋語法或篩選列）另行規劃，避免同時動到搜尋管線與 `_searchResults` 的存檔封鎖邏輯。
