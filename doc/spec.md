Windows App Launcher - 需求規格書 (Feature Spec)

1. 專案概觀

開發一個輕量級的 Windows 應用程式啟動器。允許使用者自定義應用程式 (.exe) 的路徑與啟動參數，並透過簡潔的介面快速啟動這些程式。

專案名稱：
    Ace Run

技術堆疊：
    - UI 框架： WinUI 3 (Windows App SDK)
    - 語言： C#
    - 資料儲存： JSON
    - 核心 API： System.Diagnostics.Process / ShellExecute

2. 功能列表 (Feature List)

第一階段：核心功能 (MVP) ✓ 已完成

目標：完成最小可行性產品，能存、能看、能跑。
    1. 新增啟動項目 (Add Item)
        [x] 實作 FileOpenPicker 讓使用者選擇 .exe 檔案。
        [x] 自動從選擇的檔案路徑截取檔名作為「預設標題」。

    2. 項目列表 (List View)
        [x] 使用 ListView 顯示已加入的應用程式。
        [x] 每個項目顯示顯示名稱 (Display Name)，檔案路徑以 ToolTip 方式於滑鼠懸停時呈現。

    3. 執行程式 (Execute)
        [x] 點擊「啟動」按鈕，依據儲存的路徑啟動外部程式。

    4. 資料持久化 (Persistence)
        [x] 定義資料模型 (AppItem)，包含 Id、DisplayName、FilePath 等屬性。
        [x] 應用程式關閉時，將列表序列化為 JSON 儲存至 LocalAppData\AceRun\apps.json。
        [x] 應用程式開啟時，讀取 JSON 並還原列表。

第二階段：進階設定 (Advanced Config) ✓ 已完成

目標：解決特殊程式（如遊戲、開發工具）的啟動需求。

    1. 啟動參數 (Arguments)
        [x] 在新增/編輯對話框 (EditItemDialog) 增加「參數」輸入框。
        [x] 支援傳遞參數給執行檔 (例如：chrome.exe --incognito)。

    2. 自訂工作目錄 (Working Directory)
        [x] 新增項目時，預設將「工作目錄」設為該 .exe 所在的資料夾。
        [x] 允許使用者透過 FolderPicker 手動修改工作目錄。

    3. 管理員模式 (Run as Administrator)
        [x] 在編輯對話框中加入 ToggleSwitch，啟用時以 runas verb 啟動程式。

    4. 編輯與刪除 (CRUD)
        [x] 實作右鍵選單 (MenuFlyout Context Menu)，包含「Edit」與「Delete」選項。
        [x] 編輯：開啟 ContentDialog，可修改所有欄位（名稱、路徑、參數、工作目錄、管理員模式）。
        [x] 刪除：彈出確認對話框後從列表中移除，變更即時儲存。
        [x] 新增項目時同樣使用編輯對話框，讓使用者在加入前預覽與調整設定。

第三階段：使用者體驗優化 (UX Polish) ✓ 已完成

目標：提升視覺質感與操作便利性。

    1. 圖示擷取 (Icon Extraction)
        [x] 從 .exe 檔案中動態讀取圖示 (Icon)。
        [x] 圖示必須使用磁碟快取，避免每次開啟 app 都需要從檔案中重新讀取圖示

    2. 拖放支援 (Drag & Drop)
        [x] 允許使用者將桌面上的 .exe 或 .lnk (捷徑) 直接拖入視窗。
        [x] 自動解析路徑並直接加入目前選取的資料夾（或「未分類」），不開啟對話框。

    3. 快速搜尋 (Search/Filter)
        [x] 頂部增加搜尋框 (AutoSuggestBox)。
        [x] 輸入關鍵字時即時過濾列表內容。

    4. 多語言支援
        [x] 英文
        [x] 繁體中文

第四階段：額外功能實作 ✓ 已完成

    1. 使用 H.NotifyIcon 提供 System Tray 支援
        [x] 支援將應用程式最小化至 System Tray。
        [x] 雙擊 Tray Icon 恢復視窗顯示。
        [x] Tray Icon 右鍵選單包含「最近開啟的程式」與「結束」選項。

    2. 資料夾分組管理 (Folder Grouping)
        [x] 支援建立、重新命名、刪除資料夾，將程式項目組織為群組。
        [x] 左側側邊欄以 ListView 呈現「未分類」與各資料夾，點選切換主內容區域。
        [x] 選取中的側邊欄項目顯示 accent 左側選取指示條，視覺一致。
        [x] 「未分類」固定置頂，無法移動；其他資料夾支援拖曳重新排序，順序持久化。
        [x] 可透過右鍵選單「移動至」子選單，在「未分類」與各資料夾之間移動項目。

第五階段：Workspace 多工作區管理

目標：支援多個獨立的工作區（Workspace），讓使用者可以依照不同情境（如工作、遊戲、開發）組織應用程式，並快速切換。

    1. Workspace 概念與資料模型
        [ ] 每個 Workspace 包含獨立的：
            - 應用程式列表（含資料夾分組結構）
            - 最近啟動記錄
            - 視窗狀態
        [ ] Workspace 屬性：
            - Id (GUID)
            - 名稱 (Name)
            - 建立時間 (CreatedAt)
            - 最後修改時間 (LastModifiedAt)
            - 圖示顏色標記 (ColorTag) - 可選，用於視覺區分
        [ ] 新增 WorkspaceConfig 模型，儲存：
            - Workspace 列表
            - 當前選中的 Workspace Id
            - 預設 Workspace Id（可選）

    2. 資料儲存架構調整
        [ ] 改為多檔案儲存結構：
            - %LOCALAPPDATA%\AceRun\config.json - 儲存 WorkspaceConfig
            - %LOCALAPPDATA%\AceRun\workspaces\<workspace-id>.json - 各 Workspace 資料
            - 圖示快取維持共用：%LOCALAPPDATA%\AceRun\icons\
        [ ] 向下相容處理：
            - 首次升級時，將現有 apps.json 轉換為「預設」Workspace
            - 保留 apps.json 作為備份
        [ ] 自動儲存機制：
            - 切換 Workspace 時自動儲存當前 Workspace
            - 修改資料時更新 LastModifiedAt 時間戳

    3. Workspace 切換器 UI
        [ ] 頂部工具列新增 Workspace 選擇器（ComboBox 或 SplitButton）
            - 顯示當前 Workspace 名稱
            - 下拉選單列出所有 Workspace
            - 每個項目顯示名稱與顏色標記（若有設定）
        [ ] 切換行為：
            - 儲存當前 Workspace 的完整狀態
            - 清空目前的 UI 樹狀結構與列表
            - 載入選中 Workspace 的資料
            - 保持視窗大小與位置（但各 Workspace 的內部狀態如展開/選中項目獨立）

    4. Workspace 管理功能
        [ ] 新增「管理 Workspace」對話框，包含：
            - 新建 Workspace
                - 輸入名稱（必填）
                - 選擇顏色標記（可選：藍、綠、紅、黃、紫等預設色）
                - 選項：「複製當前 Workspace」或「建立空白 Workspace」
            - 重新命名 Workspace
            - 刪除 Workspace
                - 需確認對話框，警告將永久刪除所有資料
                - 至少保留一個 Workspace（無法刪除最後一個）
            - 設定預設 Workspace
                - 標記為「啟動時自動載入」
            - 修改顏色標記
        [ ] Workspace 列表顯示：
            - 名稱、項目數量（應用程式總數）、最後修改時間
            - 拖曳排序（影響下拉選單順序）
            - 當前選中的 Workspace 顯示標記

    5. 匯入/匯出功能
        [ ] 匯出 Workspace：
            - 右鍵選單或管理介面提供「匯出」選項
            - 將單個 Workspace 資料匯出為 .acerun 檔案（實為 JSON）
            - 檔案包含 Workspace 完整結構，但不包含圖示快取
            - FileSavePicker 讓使用者選擇儲存位置
        [ ] 匯入 Workspace：
            - FileOpenPicker 選擇 .acerun 檔案
            - 驗證檔案格式與版本相容性
            - 選項：
                - 「匯入為新 Workspace」（保留原名稱或重新命名）
                - 「合併到當前 Workspace」（將應用加到現有結構）
            - 處理衝突（若有同名資料夾）：由使用者選擇合併或重新命名
            - 匯入後重新提取並快取所有應用程式的圖示

    6. UI/UX 細節
        [ ] 視覺識別：
            - Workspace 選擇器顯示彩色標記小圓點或色塊
            - 視窗標題列顯示當前 Workspace 名稱（例如：「Ace Run - 工作環境」）
        [ ] 空狀態處理：
            - 新建空白 Workspace 時，顯示歡迎訊息與快速操作提示
            - 建議透過拖放或新增按鈕加入第一個應用程式
        [ ] 效能考量：
            - 惰性載入：僅在切換時才載入 Workspace 資料
            - 大型 Workspace（100+ 項目）的列表虛擬化已在前期實作

