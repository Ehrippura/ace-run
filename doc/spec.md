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

各階段規格拆分於 `doc/spec/` 目錄下：

| 階段 | 文件 | 狀態 |
|---|---|---|
| 第一階段：核心功能 (MVP) | [1-core-mvp.md](spec/1-core-mvp.md) | ✓ 已完成 |
| 第二階段：進階設定 (Advanced Config) | [2-advanced-config.md](spec/2-advanced-config.md) | ✓ 已完成 |
| 第三階段：使用者體驗優化 (UX Polish) | [3-ux-polish.md](spec/3-ux-polish.md) | ✓ 已完成 |
| 第四階段：額外功能實作（System Tray、資料夾分組） | [4-tray-and-folders.md](spec/4-tray-and-folders.md) | ✓ 已完成 |
| 第五階段：Workspace 多工作區管理 | [5-workspaces.md](spec/5-workspaces.md) | ✓ 已完成 |
| 第六階段：改進（Tag 標籤管理） | [6-tags.md](spec/6-tags.md) | 已實作 |
| 第七階段：URL 項目支援 | [7-url-items.md](spec/7-url-items.md) | 已實作（favicon 未實作） |
| 第八階段：鍵盤快捷鍵 | [8-keyboard-shortcuts.md](spec/8-keyboard-shortcuts.md) | 已實作 |
| 第九階段：多標籤支援 | [9-multi-tag.md](spec/9-multi-tag.md) | 已實作（tag 篩選未實作） |
| 第十階段：整理與排序鍵 | [10-organize.md](spec/10-organize.md) | 已實作 |
