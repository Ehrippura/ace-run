# Windows App Launcher - 需求規格書 (Feature Spec)

## 1. 專案概觀

開發一個輕量級的 Windows 應用程式啟動器。允許使用者自定義應用程式 (.exe) 的路徑與啟動參數，並透過簡潔的介面快速啟動這些程式。

專案名稱：
    Ace Run

技術堆疊：
    - UI 框架： WinUI 3 (Windows App SDK)
    - 語言： C#
    - 資料儲存： JSON
    - 核心 API： System.Diagnostics.Process / ShellExecute

## 2. 功能列表 (Feature List)

各階段規格拆分於 `doc/spec/` 目錄下。

### 維護規則：改到既有項目時，修正該階段的文件，不要開新階段

階段編號**只代表歷史順序，不代表獨立的功能範圍**。新功能若動到既有階段已定義的東西（資料模型、既有畫面、既有行為），就回頭修正那一階段的檔案，讓每個檔案單獨讀都是現況；只有真正的新範圍才值得成為新階段。

這條規則是有代價才學到的：原第九階段「多標籤支援」整篇都在解除第六階段的單標籤限制，而第六階段檔案卻始終寫著「V1 UI 為單一標籤」，只讀該檔的人會被誤導。該階段已併回第六階段，其後的階段依序遞補編號。

合併階段之後**要重新編號把缺口補起來**，但必須同時更新所有交叉引用：`doc/spec.md` 的表格、各階段檔案內文提到的「第 N 階段」、以及程式碼註解中的檔名（例如 `Models/AppSettings.cs` 指向 `10-settings.md`）。`doc/log/CHANGELOG_*.md` 是歷史記錄，措辭不改，但指向已改名檔案的連結要修。

歷史（某件事何時做的）由 `doc/log/CHANGELOG_*.md` 與 git 承擔，不需要由階段檔案保存。

狀態欄的兩個值有明確定義，不可混用：

- **✓ 已完成** — 該階段規格範圍內的項目全數實作。各檔案末尾「不在本階段範圍」區塊列出的延伸想法不影響此標記，那些是刻意留到日後的，不算欠帳。
- **已實作** — 主要功能可用，但**規格範圍內**仍有項目未實作，括號中註明是哪些。

| 階段 | 文件 | 狀態 |
|---|---|---|
| 第一階段：核心功能 (MVP) | [1-core-mvp.md](spec/1-core-mvp.md) | ✓ 已完成 |
| 第二階段：進階設定 (Advanced Config) | [2-advanced-config.md](spec/2-advanced-config.md) | ✓ 已完成 |
| 第三階段：使用者體驗優化 (UX Polish) | [3-ux-polish.md](spec/3-ux-polish.md) | ✓ 已完成 |
| 第四階段：額外功能實作（System Tray、資料夾分組） | [4-tray-and-folders.md](spec/4-tray-and-folders.md) | ✓ 已完成 |
| 第五階段：Workspace 多工作區管理 | [5-workspaces.md](spec/5-workspaces.md) | 已實作（匯入後重新提取圖示快取未實作） |
| 第六階段：Tag 標籤管理（含多標籤支援，原獨立為第九階段） | [6-tags.md](spec/6-tags.md) | ✓ 已完成 |
| 第七階段：URL 項目支援 | [7-url-items.md](spec/7-url-items.md) | 已實作（favicon 未實作） |
| 第八階段：鍵盤快捷鍵 | [8-keyboard-shortcuts.md](spec/8-keyboard-shortcuts.md) | ✓ 已完成 |
| 第九階段：整理與排序鍵 | [9-organize.md](spec/9-organize.md) | ✓ 已完成 |
| 第十階段：設定畫面 | [10-settings.md](spec/10-settings.md) | ✓ 已完成 |
| 第十一階段：可測試性與單元測試 | [11-testability.md](spec/11-testability.md) | ✓ 已完成 |
