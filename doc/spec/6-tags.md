# 第六階段：Tag 標籤管理 ✓ 已完成

目標：讓使用者可以建立帶有顏色與名稱的 tag 標籤，並將 tag 指派到 app 項目上，提升辨識與分類效率。單一項目可同時掛上多個標籤。

> 原「第九階段：多標籤支援」整篇都在解除本階段最初的單標籤 UI 限制，已依 spec.md 的維護規則併入此檔（見 §5 起）。階段編號因此有缺口，屬正常。

## 1. 資料模型與持久化

- [x] 新增 Tag 資料模型，包含 Id、名稱與顏色。
- [x] 在 Workspace 資料中儲存 tag 清單，讓每個 Workspace 可擁有獨立的 tag 設定。
- [x] 在 AppItem 中儲存已指派的 TagId，資料面採 `TagIds` 清單。**最初的 UI 只讓每個項目掛一個 tag，多標籤 UI 於下方 §5 起補齊；資料格式從頭到尾未變，故不需 migration，`AppData` 版本不受影響。**
- [x] 儲存與載入 Workspace 時保留 tag 清單、顏色與 app 的 tag 指派關係。
- [x] 新檔被舊版本讀取時僅保留第一個 tag（舊版 `NormalizeAppTags` 的既有行為），屬可接受的降級而非資料損毀。

## 2. Tag 管理功能

「管理標籤」對話框與「管理工作區」共用同一個列型與同一套行為，差異只留真正該有差異的（無數量欄、無匯出、有空狀態）。列型、色票面板、⋯ 選單、鍵盤重排與主題解析的完整理由記在第五階段 §4，此處只記本對話框特有的部分。

- [x] 列型：`⠿ 握把 / 顏色按鈕 / 名稱 TextBox / ⋯ 溢位選單`。
- [x] 新增 tag：按下即在尾端建列並讓名稱欄取得焦點，預設名稱被占用時自動加序號。取代原本的展開式行內表單。
- [x] 編輯 tag 名稱與顏色：名稱為列內 TextBox（Enter 提交、Esc 還原、重名以 InfoBar 擋除），顏色為列上的色票按鈕。
    - **原本的每列顏色 `ComboBox` 是這次重做的主因。** 它靠 `Loaded` 事件初始化選取值，而 `Loaded` 每個容器只觸發一次；`ItemsStackPanel` 把容器回收到另一個 tag 上時只換 `DataContext`，`SelectedIndex` 就停在前一個 tag 的值。清單超過約九列即可重現：往下捲會看到別的 tag 的顏色，資料沒壞但畫面在說謊。改成 `{x:Bind ColorBrush, Mode=OneWay}` 之後，沒有任何容器層級的狀態可以走鐘。
    - 一般規則：**列內控制項的狀態必須由 `x:Bind` 承載，不得靠 `Loaded` 初始化。** `DataTemplate` 裡的 `x:Bind`（含 OneTime）在每次 realization 重跑，回收安全；`Loaded` 不是。
- [x] 刪除 tag，刪除前以 `ConfirmFlyout` 確認（經 ⋯ 選單，需延後一拍 dispatcher）。
- [x] 刪除 tag 後，已套用該 tag 的 app 需自動移除對應 TagId。
- [x] 補上 `InfoBar`（工作區對話框一直都有，這邊沒有，所以任何失敗都無處可說）。
- [x] 空清單時整個 `ListView` 一起收掉，只留提示。原本 `MinHeight="120"` 的空清單留在提示底下，讀起來是「一句話加一塊空盒子」。
- [x] 拖曳排序，以及 ⋯ 選單的上移／下移。
    - **tag 順序是有意義的**：第九階段的「依標籤」排序用第一個 tag 在 `_tags` 中的索引，`NormalizeAppTags` 也照同一順序排每個項目上的色點。順序在那兩處看得見，卻在此之前無法編輯。
    - 對話框自己不寫任何檔案：它拿到的是 `MainWindow._tags` 的參考並就地變更，`ManageTagsButton_Click` 收尾的 `NormalizeAppTags(); CommitSave();` 就是全部的持久化，`CommitSave` 寫出的正是這個集合的列舉順序。
    - **絕不可重建 `_tags`**（`Clear()` + 重新 `Add`）：那會換掉每個 `AppItemViewModel.Tags` 持有的同一批實例，下一次 `NormalizeAppTags` 會把所有項目的標籤清空。

## 3. App Tag 指派

- [x] 在新增/編輯 app 對話框中加入 tag 選擇欄位。
- [x] 支援從既有 tag 清單中選擇 tag 並套用到 app。
- [x] 支援在 app 右鍵選單中快速設定或移除 tag。
- [x] 修改 app 的 tag 後即時更新 UI 並持久化。

## 4. App Grid 顯示

- [x] 被設定 tag 的 app，在 app 名稱左側顯示對應 tag 顏色的圓點。
- [x] 未設定 tag 的 app 不顯示圓點，維持原本版面簡潔。
- [x] 圓點顏色需與 tag 管理中設定的顏色一致。
- [x] 搜尋結果列表中同樣顯示 app 的 tag 顏色圓點。

## 5. 多標籤：ViewModel 以實例取代反正規化欄位

- [x] `AppItemViewModel.Tags` 為 `ObservableCollection<TagViewModel>`，直接持有 `_tags` 中的同一批實例。
- [x] 不保留 `SetSingleTag` / `TagColorKey` / `TagName` / `TagBrush` / `TagVisibility` 等反正規化欄位；`ToModel()` 由 `Tags` 推導 `TagIds`，不再保留獨立狀態。
- [x] `TagViewModel.ColorKey` 的 setter 通知 `ColorBrush`，改色／改名即時反映到所有卡片，故不需要 `RefreshAllAppTagColors()` 一類的手動刷新。
- [x] `VisibleTags`（前 3 個，卡片僅 124px 寬）、`OverflowLabel`（`+N`）、`TagsSummary`（名稱串接，供無障礙與下拉按鈕共用），由 `SetTags` 觸發通知。
- [x] 建構子帶 `IReadOnlyList<TagViewModel>` 參數解析 id → 實例（四個呼叫點）；`LoadWorkspaceDataAsync` 先填 `_tags` 再建 app VM，順序天然正確。

## 6. 多標籤：指派邏輯

- [x] `NormalizeAppTags()`：移除已刪除的 tag、去重、依 `_tags` 順序排序。固定順序讓同一組 tag 在每張卡片上呈現一致，也省去額外的 tag 排序 UI。
- [x] `ToggleTagOnApp(app, tag, on)` 與 `ClearTagsOnApp(app)` 兩支，皆保留「搜尋模式下改走 `CommitSave()`」的分支。

## 7. 多標籤：編輯對話框與顯示

- [x] tag 欄位為 `DropDownButton` + `Flyout` 包 `SelectionMode="Multiple"` 的 `ListView`（非 `MenuFlyout`，才能連續勾選不關閉）；按鈕內容顯示圓點與名稱摘要，滿三個改 `+N`。
- [x] `DropDownButton` 無 `Header` 屬性，需自行補標籤 `TextBlock` 並以 `Loc.GetString` 設定文字。
- [x] 多選 `ListView` 在 `ItemsSource` 指派後立即寫 `SelectedItems` 不穩（容器尚未實體化），改於 `Loaded` 回填並以旗標擋掉回填期間的 `SelectionChanged`。
- [x] workspace 尚無任何 tag 時按鈕停用並顯示 `Tag_Empty`。
- [x] 卡片上是固定高度容器包 `ItemsControl`（水平 StackPanel、7px 圓點）加溢位文字；容器高度固定，空清單不改變卡片高度。該列高度為 `16px`（margin 4），因為溢位的 `+N` 使用 12pt 的 `AceCaptionStyle`；卡片總高不變。
- [x] 搜尋結果列沿用同一組合（8px 圓點），該欄 `Auto` 寬度在無 tag 時自然收合。
- [x] `AutomationProperties.Name` 設在容器（值為 `TagsSummary`）而非每顆圓點，否則朗讀程式會逐點朗讀。

## 8. 多標籤：右鍵選單

- [x] 使用 `ToggleMenuFlyoutItem`（非 `RadioMenuFlyoutItem` + `GroupName`）；置頂為「清除標籤」加分隔線。
- [x] Flyout 點選後即關閉為 Windows 標準行為，一次指派多個標籤請走編輯對話框——此即對話框選用可連續勾選控制項的原因。

## 9. 在地化

- [x] `Tag_Clear`、`Tag_Overflow`（`+{0}`）、`Tag_Separator`（zh-TW／ja 用「、」，en 用「, 」）、`Tag_Field`，三份 `.resw` 同步更新。
- [x] 新增 `Tag_DuplicateName` 與 `Tag_DeleteTitle`；`Color_*` 與 `Row_*` 見第五階段 §6，兩個對話框共用。
- [x] 刪除 `Tag_NewTitle`（表單移除）與 `Tag_Delete`（改用通用的 `DeleteButton`）。`Tag_Name` 從表單的 placeholder 改為列內名稱欄的 `AutomationProperties.Name`。

## 10. 不在本階段範圍

- [ ] 依 tag 篩選（搜尋語法或篩選列）另行規劃，避免同時動到搜尋管線與 `_searchResults` 的存檔封鎖邏輯。
