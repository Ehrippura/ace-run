# 第十二階段：MCP 自動化介面 — 已實作（階段 A）

目標：讓外部的 AI agent（Claude Code、Claude Desktop 等 MCP client）能查詢與操作這個啟動器——列出工作區與項目、以自然語言搜尋、啟動指定項目、增刪改項目與資料夾。在此之前這個 app 只有三個入口：視窗、系統匣、全域快捷鍵，三者都要人在鍵盤前面。

這一階段最大的約束不是協定，而是**狀態的所有權**：活的資料在 `MainWindow` 手上的 ViewModel 集合裡，`CommitSave()` 每次都把整份 workspace 從那些集合重寫回 JSON。外部程序直接改 `workspaces/<guid>.json`，下一次存檔就會被整份蓋掉。另一條約束是啟動的正確性綁在項目上：`Arguments`、`WorkingDirectory`、`runas` 缺一個行為就不同，`RecentLaunch` 的註解已經記錄過「只拿路徑自己啟動」這個錯誤的代價。

因此規則是：**寫入與啟動一律交給執行中的 app，MCP 伺服器不自己動磁碟。**

## 0. 實作分期

兩期各自可獨立交付、獨立驗收：

| 期 | 範圍 | 對 app 的改動 | 風險 |
|---|---|---|---|
| **A** | 唯讀查詢工具，資料直接讀磁碟 | **零**（不改 `win/`，不加設定，不開管線） | 讀自己的設定檔，無副作用 |
| **B** | 命名管線、啟動、增刪改、工作區切換、視窗控制 | 新增管線伺服器、轉接層、設定開關 | 會動資料、會執行程式 |

這樣切的理由：A 期能驗證掉這個功能最不確定的一半（MCP client 認不認、工具 schema 好不好用、`AceRun.Core` 能不能被另一個專案乾淨地重用），而且**全程不具破壞性**——它做的事情跟使用者自己讀一次 `config.json` 等價。B 期才引入「LLM 可以執行任意程式」這個真正需要開關與確認的語意。

A 期完工後 MCP 伺服器就已經可用，不是半成品。

**目前狀態：A 期已實作並驗證（`tools/list` 與六個工具都由真實 client 流程跑過），B 期未開始。**

---

# 階段 A：唯讀查詢

## A1. 架構與專案結構

```
MCP client ──stdio──> AceRun.Mcp.exe ──讀取──> %LOCALAPPDATA%\AceRun\*.json
```

- [x] 新增 `mcp/AceRun.Mcp/`：console 專案，參考 `AceRun.Core`，託管 stdio MCP 伺服器。
- [x] **放在 `win/` 同層而非底下**，理由與 `core/`、`test/` 相同：`ace-run.csproj` 沒有任何 `<Compile>` 設定，完全依賴 SDK 預設 globbing，放進 `win/` 的任何資料夾都會被編進 app。
- [x] TFM 必須是 `net10.0-windows10.0.22000.0`（搭配 `TargetPlatformMinVersion 10.0.17763.0`）：`AceRun.Core` 用的是 Windows TFM，參考端不能是純 `net10.0`。這個伺服器本來就只在 Windows 上有意義。
- [x] `Platforms` 用 `AnyCPU`，與 `core/`、`test/` 一致；發佈時才按 RID 指定。
- [x] **所有 MCP 相依只能出現在這個專案。** `AceRun.Core` 維持零 `PackageReference`（見第十一階段 §1），測試專案才能在沒有 WindowsAppRuntime 的機器上跑。
- [x] **log 一律導到 stderr。** stdout 就是協定通道，`Host.CreateApplicationBuilder` 預設掛上的 console logger 會把 log 寫進去、把 JSON-RPC 訊息攪爛；因此清掉所有 provider 再以 `LogToStandardErrorThreshold = Trace` 重新掛一個。導到 stderr 而非全部關掉，是為了讓 client 的伺服器輸出面板還看得到東西。

## A2. 伺服器型態：厚伺服器，不是瘦橋接

協定與工具 schema 寫在 `AceRun.Mcp.exe`，app 不認識 MCP。三個方案比較過：

- **伺服器直接讀寫 JSON** — A 期的讀取採用它，但寫入不行（見開頭的覆蓋問題）。
- **app 內嵌 HTTP MCP 伺服器** — 要把 ASP.NET Core 拉進 WinUI 程序，而且要開 port。否決。
- **瘦橋接**（MCP 協定寫在 app 裡，exe 只做管線轉送） — app 沒開時連 `tools/list` 都回不出東西，client 一開機就看到零工具。否決。

厚伺服器的代價是「工具 schema」與「管線命令」兩層要對齊，但命令 DTO 放在 `core/` 由兩邊共用，漂移由編譯器擋住。

## A3. 讀取路徑：不能重用 `DataStore` 的寬容行為

- [x] 新增 `core/AceRun.Core/Automation/AutomationRead.cs`，提供會**明確回報失敗**的讀取（`ReadOutcome`：`Ok` / `Missing` / `Unreadable` / `Malformed`）。
- [x] `DataStore.LoadWorkspace` 走 `ReadOrDefault`，任何例外都回傳空的 `AppData`。那個行為對 app 啟動是對的（「啟動空的勝過不啟動」，第十一階段 §2），對查詢工具是**錯的**：檔案正在被原子替換而短暫讀不到時，client 會收到一個語意完全不同的答案——「你沒有任何項目」。測試直接把這組對比釘住：同一個壞檔，`DataStore` 回空清單，`AutomationReader` 回 `Malformed`。
- [x] 因此 `AutomationRead` 自己讀檔：短暫重試（3 次、間隔 50ms，涵蓋存檔持有檔案的瞬間），仍失敗則回傳錯誤，由工具轉成 MCP 的 error 而非空清單。解析**也不能**借用 `DataStore.ParseAppData` / `ParseConfig`——它們同樣把失敗吞成空物件、無從回報——所以直接以 `AceRunJson.Options` 反序列化。
- [x] 每次呼叫都重新讀檔，不做快取：app 在任何時刻都可能存檔。
- [x] 開檔用 `FileShare.ReadWrite | FileShare.Delete`，讓 app 能在我們讀的同時開啟並寫入同一個檔案（`File.ReadAllText` 的 `FileShare.Read` 會把寫入者鎖在外面）。但**沒有任何 share mode 能讓 rename 蓋過開啟中的檔案**：`DataStore.WriteAtomic` 結尾的 `File.Move(overwrite: true)` 只要目的檔還有任何 handle 開著就是 access denied——`FILE_SHARE_DELETE` 只允許刪除處於「待處理」狀態，而待刪除的檔案仍占著 rename 需要的目錄項。這條原本在註解裡寫反了，是測試糾正的；能做的就是把讀取視窗壓成一次 open-read-close，中間不夾任何事，並以測試釘住這個 Windows 行為以免哪天又被寫反。

### 已知落差，必須寫進工具說明

磁碟不等於畫面。唯一會明顯落後的情況是**使用者在搜尋框留著字**：`SaveItems()` 在 `_searchText` 非空時直接 return，那段時間的修改要等下一次 `CommitSave()` 才落地。B 期改為優先問 app 之後這個落差會消失，A 期則在工具描述裡誠實交代。

## A4. A 期工具清單

| 工具 | 回傳 |
|---|---|
| `list_workspaces` | id、名稱、顏色鍵、項目數、是否為使用中 |
| `list_folders(workspace?)` | id、名稱、項目數、所屬工作區 |
| `list_items(workspace?, folder?, tag?, limit?, offset?)` | 一頁項目加上總數：id、名稱、kind、路徑/網址、參數、工作目錄、是否 admin、標籤、資料夾、工作區 |
| `search_items(query, workspace?, limit?, offset?)` | 同上，順序與 UI 一致 |
| `list_tags(workspace?)` | id、名稱、顏色鍵、所屬工作區 |
| `get_recent_launches(workspace?)` | 項目 id、顯示名稱、工作區 |

- [x] 工具名不加 `ace_run_` 前綴：client 本來就以伺服器名稱區隔。
- [x] `workspace` 參數省略時用 `ActiveWorkspaceId`；可給 id 或名稱。
- [x] 全部標 `readOnly` 與 `idempotent`：這六個工具改不了任何東西，client 不必為它們問使用者。
- [x] `search_items` 直接呼叫既有的 `SearchRanking`，結果順序與視窗內的搜尋一致，不另寫一套比對規則。
- [x] 不回傳圖示（見最後一節）。路徑照原樣回傳，不展開環境變數、不檢查存在性——那是 B 期 `launch_item` 的事。
- [x] `list_items` 省略 `folder` 時回傳整個工作區（扁平、未分組在前，每筆自己報所屬資料夾）。app 一次只顯示一個資料夾是因為人一次只讀一個畫面；隔著協定問的呼叫端要的是一次問完。
- [x] **錯誤訊息只有 `McpException` 的內容送得到 client**，其他例外一律被 SDK 收斂成 `An error occurred invoking '<tool>'`。因此失敗路徑都丟 `McpException`，訊息除了講哪裡錯，還要講接下來該呼叫什麼（「Call list_workspaces to see them.」）；`search_items` 的空查詢在工具層多擋一次，Core 的 `ArgumentException` 守衛照留——那是函式的契約，這是協定的訊息。這一條是冒煙測試發現的：空查詢原本只回「發生錯誤」。

### 篩選與回應大小：接上真實 agent 之後補的

第一版工具只有 `list_items(workspace?, folder?)`，沒有標籤篩選也沒有分頁。接到 VS Code 的 agent 之後，「tag 是 Done 的項目有哪些」這一個問題就把它打穿，而且是兩個獨立的缺口同時發作：

- **只能整個工作區列出來**，而該工作區有 349 個項目。回應過大，client 把完整 JSON 存進暫存檔而沒有在對話裡顯示內容，於是 agent 放棄工具、自己去 `%LOCALAPPDATA%\AceRun` 讀 JSON 解析（中途還踩到 PowerShell 的編碼問題）。**工具存在但不好用，等於不存在。**
- **`Done` 標籤在 Games，而當時使用中的工作區是 Work。** `list_tags` 不帶 workspace 只回使用中工作區的標籤，agent 根本看不到那個標籤在哪個工作區。

因此補上：

- [x] `tag` 參數（id 或名稱，比對規則與 workspace 相同）。這是「tag 是 X 的項目有哪些」的直接答案，不必列出全部再自己濾。
- [x] `limit` / `offset`，且回傳 `{ total, offset, items, nextOffset }` 而非裸清單。`total` 讓呼叫端分得出「這就是全部」和「還有更多」；工具描述明說總數大時該用 tag / folder 收斂，而不是一頁一頁翻。
- [x] **`nextOffset` 由伺服器算，不留給呼叫端。** 這是實際用出來的：agent 分頁走完 110 筆之後，回報某兩個資料夾的數量各多一個——那正是第二頁起點早一列、兩頁重疊的樣子。核對過資料與伺服器回應都沒有問題（110 筆、110 個不重複 id，兩個資料夾確實是 11 與 5），錯在呼叫端自己算下一個 offset。伺服器算得出精確答案的算術，沒有道理叫語言模型重算一次；最後一頁不帶這個欄位，所以「還有沒有下一頁」也不必靠比較大小判斷。
- [x] **預設 50、上限 100，而且這兩個數字是量出來的。** 以真實的日文遊戲庫實測（每列約 220 bytes）：

  | 回傳列數 | 回應大小 |
  |---|---|
  | 10 | 2.4 KB |
  | 50 | 11 KB |
  | 100 | 23 KB |
  | 200 | 47 KB |
  | 349（整個工作區） | 81 KB |

  client 認為過大的工具回應會被轉存成檔案、只給模型一小段預覽——正是逼 agent 放棄工具的那件事。**預設值才是真正的防線**：會需要自己傳 `limit` 的呼叫端，就是已經出事的那一個。上限只開到預設的兩倍，再多就該用 tag / folder 收斂，而不是去要一頁沒人讀得完的資料。
- [x] 工具序列化改用 `UnsafeRelaxedJsonEscaping`（以 `McpJsonUtilities.DefaultOptions` 為基礎覆寫 `Encoder`）。SDK 預設把非 ASCII 寫成 `\uXXXX`，一個字六個 bytes，整個日文路徑等於兩倍大小；實測 100 列由 30 KB 降到 23 KB。app 自己的檔案（`AceRunJson`）早就為同一個理由這樣寫。
- [x] `workspace: "all"` 掃過所有工作區，每一列都報自己來自哪個工作區。名稱解析先跑，所以真的叫 All 的工作區仍然用名稱找得到。標籤在某個工作區不存在時，那個工作區是**整個跳過**而不是不篩選——後者會回傳它的全部項目；一個工作區都對不上才是錯誤，訊息並告知用 `workspace: "all"` 去找。
- [x] 投影瘦身：null 欄位不序列化、`RunAsAdmin` 只在 true 時出現、沒有標籤就不帶 `tags`，而 `WorkingDirectory` 等於 exe 所在資料夾時省略——那是 `ItemFactory.FromPath` 的預設值，幾乎每一列都只是 `Target` 的複製品。使用者自己選過的工作目錄是另一回事，照實回報。實測 100 列約 30 KB。
- [x] 伺服器 instructions 直接寫明「不要自己去開 `%LOCALAPPDATA%\AceRun` 的檔案」，並說明標籤屬於工作區、列表會分頁。agent 會讀 instructions，而 `CLAUDE.md` 的 Data Storage 表格剛好告訴它檔案在哪——不講清楚等於邀請它繞過工具。

## A5. 放進 Core 的東西（因此測得到）

- [x] `AutomationRead`：帶重試與明確失敗的讀取（§A3）。
- [x] `ItemLookup`：以 id 或名稱解析工作區、資料夾與標籤。名稱比對規則釘死——忽略大小寫、完全相符優先（`Work` 不會被 `Work (old)` 變成歧義）、多筆相符時回報歧義並列出候選 id，而不是隨便挑第一筆。這是 agent 最容易踩的地方，而「隨便挑一筆」在 B 期會變成啟動錯的程式。解析得出是 `Guid` 的 key 只比對 id，絕不退回名稱比對：把精確的請求變成模糊的請求沒有道理。
  - **項目解析留到 B 期。** A 期沒有任何工具需要它（查詢回傳的是清單，不是單一項目），照本專案「當時無人使用就是死碼」的慣例（第十一階段 §5 的 `IconsDir`）不先寫。
  - `ActiveWorkspaceId` 指向不存在的工作區時退回第一個而不是報錯：修復那個索引是 `MigrateOrInitialize` 的事，而它會**寫磁碟**——這一側絕對不能做。把答得出來的問題變成錯誤沒有好處。
- [x] `AutomationQuery` 與 `AutomationSummaries`：投影型別與查詢。`AppItem` 不直接外洩，`SortKey`、`CustomIconPath` 這些內部欄位不出現在 MCP 介面上；URL 項目不回報 `Arguments` / `WorkingDirectory` / admin（第七階段 §5 說它們不參與啟動，照檔案內容回報等於描述一個不可能發生的啟動）；空字串一律回報成 null。
- [x] 搜尋要跑 `SearchRanking` 就得有 `IAppItemView`，而存檔的 `AppItem` 存的是 tag id、本身滿足不了那個介面；以 `TagOrdering.InWorkspaceOrder` 解析成共用的 `TagItem` 後用一個私有 adapter 包起來，順帶讓「workspace 順序、丟掉已刪標籤」這條既有不變量自動成立。
- [x] `AutomationQuery.Page`：切頁並帶上總數。夾制而非驗證——越界的 offset 回空頁、超出上限的 limit 收到上限，呼叫端是 agent，為算錯的頁碼回一個錯誤沒有意義。
- [x] 測試 52 項：`ItemLookup` 的歧義與大小寫、`AutomationRead` 對損毀／空檔／`null` 字面／鎖住的檔案各自回報哪一種失敗、share mode 的兩面、標籤篩選、分頁邊界、`WorkingDirectory` 的省略規則（含路徑帶無效字元時不得讓整份列表失敗）、投影與搜尋順序（含「最近啟動壓過比對品質」這條 UI 行為）。

## A6. A 期的建置與文件

- [x] `win/ace-run.slnx` 納入 `mcp/AceRun.Mcp`，平台對應寫 `AnyCPU`。
- [x] CI 既有的 build 會自然涵蓋；測試步驟不變（仍不帶 `-p:Platform`）。
- [x] 第十一階段 §1 的「三個專案」改為四個，並說明 `mcp/` 為何同樣在 `win/` 外。
- [x] `CLAUDE.md` 的 Architecture 與 Build & Run 補上 `mcp/`。
- [x] README 補一段註冊方式（`claude mcp add` 與 Claude Desktop 的 JSON 片段）。

---

# 階段 B：寫入與啟動

```
MCP client ──stdio──> AceRun.Mcp.exe ──命名管線──> ace-run.exe (MainWindow)
                           │
                           └── app 未執行時：唯讀工具退回 A 期的磁碟路徑
```

## B1. 管線

- [ ] 新增 `win/Services/AutomationServer.cs`：命名管線接聽，名稱 `AceRun.Automation.<使用者 SID>`。
- [ ] SID 入名是為了避免多使用者同時登入時撞名；另以 `NamedPipeServerStreamAcl.Create` 明確只授權目前使用者，不依賴預設 ACL。
- [ ] 接受多個併發連線（不只一個 client 會接上來），app 結束時一併收掉。
- [ ] 框架用 NDJSON：一行一個請求／回應。**不能重用 `AceRunJson.Options`**——它 `WriteIndented = true`，縮排的 JSON 一行放不下。`core/AceRun.Core/Automation/AutomationJson.cs` 另備一份不縮排、同樣帶 `JsonStringEnumConverter` 的設定。
- [ ] 請求帶 id，回應帶同一個 id 與成功或錯誤。錯誤以列舉表示而非字串訊息——`core` 不碰在地化，這條與 `WorkspaceImport.TryParse` 的作法一致。
- [ ] 命令 DTO 放 `core/AceRun.Core/Automation/AutomationCommand.cs`，app 與 MCP 伺服器共用同一份型別。

## B2. 轉接層：`win/MainWindow.Automation.cs`

- [ ] 一個 partial 檔案承擔全部轉接，不讓管線程式碼散進既有的 partial 裡。
- [ ] 管線 handler 在背景執行緒上，所有 ViewModel 操作必須 `DispatcherQueue.TryEnqueue` 搭配 `TaskCompletionSource` marshal 回 UI 執行緒。`ObservableCollection` 不能從別的執行緒改。
- [ ] **存檔只能走 `PersistAfterEdit()` 或 `CommitSave()`，不可用 `SaveItems()`。** 後者在搜尋字串非空時直接 return——使用者搜尋框裡留著字時，MCP 的修改會靜默不存。這個陷阱 `PersistAfterEdit` 的註解已經記錄過一次。
- [ ] 每個命令完成後照既有路徑收尾：`AfterLaunch()`（含 `HideOnLaunch`）、`UpdateTrayContextMenu()`、`PurgeStaleRecentLaunches()`。不另寫一套，否則托盤與最近清單會跟畫面不一致。
- [ ] 轉接層**絕不開對話框**：沒有人在看畫面，而且 `_modalDepth` 的守衛會跟它打架。

## B3. 需要先拆的既有程式碼（不改行為）

- [ ] `DeleteAppsAsync` / `DeleteFolderAsync` / `RenameFolderAsync` 目前把 `ContentDialog` 與實際動作綁在同一個方法裡。抽出無對話框的核心方法，讓 UI 路徑與自動化路徑共用後半段。
- [ ] 自動化路徑**沒有二次確認**，這是刻意的：確認對話框守的是「使用者點錯」，而這裡的呼叫端是 client 自己的確認流程（工具標 `destructiveHint`）。這一點要在工具描述裡講明。
- [ ] `SwitchWorkspaceAsync` 目前是 private；轉接層需要它，改為可內部存取，但切換一律走這個方法——第五階段 §3 的切換行為（先存當前工作區、再重置內容狀態）不能有第二條路徑繞過。

## B4. B 期工具清單

| 工具 | 註記 |
|---|---|
| `launch_item` | 核心用途。回傳實際解析到的項目，讓 client 能確認啟動的是哪一個 |
| `add_item` / `update_item` / `delete_item` / `move_item` | `delete_item` 標 `destructiveHint` |
| `create_folder` / `rename_folder` / `delete_folder` | 同上 |
| `set_item_tags` / `create_tag` | 標籤是共用實例，一律以 id 指派 |
| `switch_workspace` | |
| `show_window` / `hide_window` | |
| `clear_recent_launches` | |

- [ ] `add_item` 的驗證放 Core：App 項目檢查路徑存在，URL 走既有的 `UrlUtil.TryNormalize`，建立一律經 `ItemFactory`，不另造 `AppItem`。
- [ ] 項目型別建立時決定、不可事後切換（第七階段 §4），`update_item` 不接受改 `kind`。
- [ ] A 期的唯讀工具在 B 期改為**優先問 app**：連得上就問（拿到活的狀態），連不上退回磁碟。

## B5. app 未執行時

- [ ] 唯讀工具照 A 期走磁碟，照常回答。
- [ ] 需要 app 的工具：以 `--tray`（`StartupService.TrayArgument`）啟動 `ace-run.exe`，等管線出現後重試一次，逾時則回報錯誤。`--tray` 不會把視窗丟到使用者臉上，`AppInstance.FindOrRegisterForKey("AceRun-Main")` 保證不會起第二個實例。
- [ ] exe 位置從 MCP 伺服器自己的目錄解析（兩者同資料夾發佈），不猜測安裝路徑、不寫登錄檔。
- [ ] 自動啟動可用環境變數關掉（預設開啟）。

## B6. 安全與設定

`launch_item` 加上 `add_item` 等於「LLM 可以執行任意程式」。A 期沒有這個語意，B 期才需要閘門：

- [ ] `AppSettings` 新增自動化開關，**預設關閉**。這是少數不以「維持現行行為」為預設理由的設定——現行行為是「沒有這個介面」，預設關閉才是它的延續。
- [ ] 三段式而非布林：關閉 / 唯讀 / 完整。唯讀檔位讓使用者能給 agent 查詢能力而不給執行能力。
- [ ] `RunAsAdmin` 的項目在自動化路徑另設獨立開關，預設拒絕啟動：從 MCP 呼叫會冒出使用者完全沒預期的 UAC 視窗。
- [ ] 設定頁加「複製 MCP 設定」按鈕，輸出含絕對路徑的 JSON 片段，省掉手填。
- [ ] 開關切換即生效：開啟時起管線，關閉時收掉，不需重啟。
- [ ] 三份 `.resw` 同步，沿用 `Domain_Thing` 命名慣例。
- [ ] 第十階段 §1 的 `AppSettings` 欄位清單與設定畫面分組要一併補上這些項目（實作到這裡時再改那份文件，在此之前它仍然正確）。

## B7. B 期測試

- [ ] Core：命令 DTO round-trip（含 NDJSON 的單行性）、`add_item` / `update_item` 的欄位驗證、錯誤列舉的完整性。
- [ ] 管線與轉接層留在 `win/`，以一支打管線的 smoke script 驗證，並用 UI 測試腳本確認畫面、托盤、最近清單在自動化改動後同步更新。
- [ ] 端到端：真的用 MCP client 跑一次 `tools/list` 與 `launch_item`。
- [ ] 回歸重點：搜尋框留著字時下 `update_item`，確認改動有落地（§B2 的陷阱）。

---

## 套件版本

- [x] `ModelContextProtocol` **2.3.0**（Apache-2.0，`csharp.sdk.modelcontextprotocol.io`），加上 `Microsoft.Extensions.Hosting` **10.0.13**——主套件只相依 `Hosting.Abstractions`，而 `Host.CreateApplicationBuilder` 在實作套件裡。版本跟著 SDK 釘的那一組 Extensions 走，不混版。
- 2026-10-31 查證：`api.nuget.org/v3-flatcontainer/modelcontextprotocol/index.json` 的版本清單結尾為 `2.0.0, 2.1.0, 2.2.0, 2.3.0`，含 prerelease 在內沒有更新的版本；registration index 的 `published` 為 `2026-10-20T23:46Z`，與套件內 `lib/net10.0/*.dll` 的時間一致。
- 2.3.0 的 nuspec 有明確的 `net10.0` 相依群組，該群組只帶 `ModelContextProtocol.Core [2.3.0]`、`Microsoft.Extensions.Caching.Abstractions 10.0.13`、`Microsoft.Extensions.Hosting.Abstractions 10.0.13`；`Core` 在 net10.0 下只帶 `Microsoft.Extensions.AI.Abstractions 10.11.0` 與 `Microsoft.Extensions.Logging.Abstractions 10.0.13`。`System.IO.Pipelines`、`System.Net.ServerSentEvents`、`Microsoft.Bcl.*` 那一串只出現在 net8.0 與 netstandard2.0 群組，這個 TFM 用不到。
- **要驗證版本就查 flat-container index，不要靠搜尋引擎**：撰寫這份規格時第一次查到的「最新版 2.0.0」來自過期的快取頁面，差了三個小版。

## 發佈

- [ ] 發佈設定檔要讓 `AceRun.Mcp.exe` 落在 `ace-run.exe` 旁邊（§B5 的路徑解析與 §B6 的「複製設定」都依賴這件事）。
- [ ] self-contained 設定檔下兩個 exe 能共用同一份 runtime 檔案（同 RID、同 TFM），但實際體積增量要實測後再寫進 `CLAUDE.md` 的設定檔表格。

## 不在本階段範圍

- [ ] MCP resources 與 prompts：只做 tools。資源在各 client 的支援度不一致，而這個 app 的操作全部是動作語意。
- [ ] 回傳項目圖示（base64 影像）。
- [ ] 透過 MCP 讀寫應用程式設定（主題、語言、快捷鍵、開機啟動）。
- [ ] `.acerun` 匯入匯出、工作區的新增、刪除與重新命名。
- [ ] 項目順序調整（拖曳與 Organize 的等價操作）。
- [ ] 遠端存取：伺服器只走 stdio，管線只在本機、只授權目前使用者。沒有 HTTP 傳輸、沒有 TCP。
