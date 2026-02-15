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
        [x] 自動解析路徑並彈出「新增項目」對話框。

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
        [x] 支援建立資料夾，將程式項目組織為群組。
        [x] 資料夾可展開/收合，提供更清晰的列表管理。
        [x] 支援巢狀資料夾結構（資料夾內可再建立子資料夾）。
        [x] 可透過拖放方式移動項目至資料夾中，或在資料夾間移動。
        [x] 資料夾狀態（展開/收合）會記憶並持久化。

