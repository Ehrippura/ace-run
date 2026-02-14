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

第一階段：核心功能 (MVP)

目標：完成最小可行性產品，能存、能看、能跑。
    1. 新增啟動項目 (Add Item)
        [ ] 實作 FileOpenPicker 讓使用者選擇 .exe 檔案。
        [ ] 自動從選擇的檔案路徑截取檔名作為「預設標題」。

    2. 項目列表 (List View)
        [ ] 使用 ListView 或 GridView 顯示已加入的應用程式。
        [ ] 每個項目需顯示：顯示名稱 (Display Name) 與 檔案路徑 (File Path)。
        [ ] 項目顯示的名稱可以任意重新命名。

    3. 執行程式 (Execute)
        [ ] 點擊列表項目或「啟動」按鈕。
        [ ] 依據儲存的路徑啟動外部程式。

    4. 資料持久化 (Persistence)
        [ ] 定義資料模型 (Data Model)。
        [ ] 應用程式關閉時，將列表序列化為 JSON 儲存。
        [ ] 應用程式開啟時，讀取 JSON 並還原列表。

第二階段：進階設定 (Advanced Config)

目標：解決特殊程式（如遊戲、開發工具）的啟動需求。

    1. 啟動參數 (Arguments)
        [ ] 在新增/編輯頁面增加「參數」輸入框 (TextBox)。
        [ ] 支援傳遞參數給執行檔 (例如：chrome.exe --incognito)。

    2. 自訂工作目錄 (Working Directory)
        [ ] 預設將「工作目錄」設為該 .exe 所在的資料夾。
        [ ] 允許使用者手動修改（解決舊遊戲找不到資源檔的問題）。

    3. 管理員模式 (Run as Administrator)
        [ ] 在項目設定中加入 ToggleSwitch。

    4. 編輯與刪除 (CRUD)
        [ ] 實作右鍵選單 (Context Menu) 或項目內按鈕。
        [ ] 功能：編輯 (修改路徑/參數)、刪除 (從列表中移除)。

第三階段：使用者體驗優化 (UX Polish)

目標：提升視覺質感與操作便利性。

    1. 圖示擷取 (Icon Extraction)
        [ ] 從 .exe 檔案中動態讀取圖示 (Icon)。
        [ ] 圖示必須使用磁碟快取，避免每次開啟 app 都需要從檔案中重新讀取圖示

    2. 拖放支援 (Drag & Drop)
        [ ] 允許使用者將桌面上的 .exe 或 .lnk (捷徑) 直接拖入視窗。
        [ ] 自動解析路徑並彈出「新增項目」對話框。

    3. 快速搜尋 (Search/Filter)
        [ ] 頂部增加搜尋框 (AutoSuggestBox)。
        [ ] 輸入關鍵字時即時過濾列表內容。

