# 第十二階段：MCP 自動化介面 — 已實作

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

**目前狀態：兩期都已實作並驗證完畢。A 期由真實 client 流程驗證過；B 期先以一支自備 app 端的冒煙腳本對真實 `AceRun.Mcp.exe` 驗過全部 20 個工具與錯誤路徑，再接上真實的 app 跑過三個檔位、十四個動作工具與畫面／托盤／最近清單的同步（見 §B7）。**

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

### 已知落差，必須寫進工具說明（B 期已消除）

磁碟不等於畫面。唯一會明顯落後的情況是**使用者在搜尋框留著字**：`SaveItems()` 在 `_searchText` 非空時直接 return，那段時間的修改要等下一次 `CommitSave()` 才落地。A 期在工具描述裡誠實交代；B 期的 `ReadState` 讓查詢優先問 app，落差消失（見 §B2），工具描述一併改掉。

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
  - **項目解析留到 B 期。** A 期沒有任何工具需要它（查詢回傳的是清單，不是單一項目），照本專案「當時無人使用就是死碼」的慣例（第十一階段 §5 的 `IconsDir`）不先寫。B 期補上 `ItemLookup.Item`，走同一個 `Resolve`，扁平掃過未分組與每個資料夾——這也是歧義判定最值得存在的地方：解析完的下一件事就是啟動它。
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
                           └── 連不上時：唯讀工具退回磁碟，其餘回報為何連不上
```

## B1. 管線

- [x] 新增 `win/Services/AutomationServer.cs`：命名管線接聽，名稱 `AceRun.Automation.<使用者 SID>`。
- [x] 名稱與框架解析放 `core/AceRun.Core/Automation/AutomationPipe.cs`，兩端共用。各算一次的名稱就是有一天會不一樣的名稱。
- [x] SID 入名是為了避免多使用者同時登入時撞名；另以 `NamedPipeServerStreamAcl.Create` 明確只授權目前使用者的 SID（`ReadWrite | CreateNewInstance`），不依賴預設 ACL。預設 ACL 幾乎一樣，而「幾乎」就是問題：它同時給 Everyone 讀取權限，這不是能在「可以啟動任意程式」的通道上留著不管的事。`CreateNewInstance` 不能漏——一旦給了明確的安全描述元，建立者需要這個權限才能開下一個同名實例。
  - `NamedPipeServerStreamAcl` 在 net10.0 的參考組件裡（`System.IO.Pipes.AccessControl.dll`），不需要額外 `PackageReference`。
- [x] 接受多個併發連線（編輯器與終端機可以各開一個 MCP session），每條連線各自一個 task；app 結束時一併收掉，縮到系統匣時**不收**——使用者沒有關掉 app，只是把它放到旁邊。
- [x] 框架用 NDJSON：一行一個請求／回應。**不能重用 `AceRunJson.Options`**——它 `WriteIndented = true`，縮排的 JSON 一行放不下。`core/AceRun.Core/Automation/AutomationJson.cs` 另備一份不縮排、同樣帶 `JsonStringEnumConverter` 與 `UnsafeRelaxedJsonEscaping` 的設定，並加上 `DefaultIgnoreCondition = WhenWritingNull`（請求是一份涵蓋所有命令的扁平 record，任一行上大多數欄位都是 null）。
- [x] **讀取端大小寫不敏感。** 兩端都用同一份 options 寫出，照理對得上；但這條管線也會被手寫的冒煙腳本打，而拼錯大小寫的欄位不會失敗，它會安靜地變成 null——「缺少必要參數」是一個很難懂的方式來告訴你某個字母的大小寫錯了。
- [x] 請求帶 id，回應帶同一個 id 與成功或錯誤。錯誤以列舉表示而非字串訊息——`core` 不碰在地化，這條與 `WorkspaceImport.TryParse` 的作法一致；訊息由 MCP 伺服器那側組裝，因為只有它知道呼叫端還能改用哪些工具的名字。
- [x] 命令 DTO 放 `core/AceRun.Core/Automation/AutomationCommand.cs`，app 與 MCP 伺服器共用同一份型別。
- [x] 讀不懂的那一行也要回答。解析失敗回 `null` 而不是丟例外（`AutomationPipe.TryDeserialize`），伺服器照樣送出一個 `Internal` 回應——讓呼叫端永遠等不到回覆比回一個錯誤更糟。

### 命令名稱在線上是字串，不是列舉

`AutomationRequest.Command` 是 `string`，由 `Action` 屬性解析成 `AutomationAction`。理由是版本不一致時的行為：`JsonStringEnumConverter` 碰到沒見過的成員會對**整行**丟例外，那時連可以用來回覆的 id 都還沒拿到。改成字串之後，新伺服器對舊 app 下一個不存在的命令，收到的是 `UnknownCommand`，而不是斷掉的連線。

解析時多一個「首字必須是字母」的檢查：`Enum.TryParse` 連底層數字也收，否則 `"4"` 會變成今天排在第四個的那個命令。

### 請求是一個扁平 record，不是十五個型別

每個命令一個型別、以 `[JsonDerivedType]` 做多型是另一條路，換得的是「序列化器會拒絕這個命令用不到的欄位」——而真正需要檢查的事（它**需要**的欄位在不在）還是得靠 `AutomationRequest.Validate()`。所以檢查集中在一處，型別保持平的。十五個大多只帶一兩個字串的型別不值得那層機制。

- [x] null 表示「沒給」。對 `UpdateItem` 能改的欄位來說，這正是「不要動」與「清掉」的差別：null 不動，空字串清掉。這一條有測試釘住 round-trip 之後兩者仍然分得開。

## B2. 轉接層：`win/MainWindow.Automation.cs`

- [x] 一個 partial 檔案承擔全部轉接，不讓管線程式碼散進既有的 partial 裡。那兩條讓它安全的規則（跑在 UI 執行緒、不開對話框）必須處處成立，散開就沒人看得出它們成不成立。
- [x] 管線 handler 在背景執行緒上，所有 ViewModel 操作以 `DispatcherQueue.TryEnqueue` 搭配 `TaskCompletionSource` marshal 回 UI 執行緒。`TryEnqueue` 回 false（視窗正在收掉）也要回一個答案。
- [x] **存檔一律走 `CommitSave()`。** `SaveItems()` 在搜尋字串非空時直接 return——使用者搜尋框裡留著字時，MCP 的修改會被接受但靜默不存。`PersistAfterEdit()` 兩條路最後都進 `CommitSave()`，所以共用的核心方法用它，轉接層自己寫的路徑直接用 `CommitSave()`。
- [x] 每個命令完成後照既有路徑收尾：`AfterLaunch()`（含 `HideOnLaunch`）、`UpdateTrayContextMenu()`、`PurgeStaleRecentLaunches()`。不另寫一套，否則托盤與最近清單會跟畫面不一致。
- [x] 轉接層**絕不開對話框**：沒有人在看畫面，而且 `_modalDepth` 的守衛會跟它打架。

### 放在哪裡是請求說的，不是畫面說的

- [x] `add_item` 與 `move_item` 把項目放到請求指名的資料夾，省略時放未分組——**不是**放進使用者當下開著的資料夾。UI 的 `AddDirectly` 用 `_selectedFolder` 是對的，那是手在鍵盤上的人的意圖；隔著管線來的呼叫端跟那件事無關。

### 對話框開著就不收寫入

- [x] `request.ChangesSomething && IsModal` → `Busy`。自動化自己不開對話框，但使用者可能開著一個，而那個對話框正在編輯的就是寫入會從它底下搬走的那些集合：編輯對話框開在某個項目上、自動化同時把它刪掉，`ApplyTo` 之後會寫進一個已經不在任何集合裡的 view model，那筆編輯就憑空消失。讀取不受限制，它只複製、不動任何東西。

### 寫入與啟動只作用在使用中的工作區

視窗手上只有一個工作區，因此：

- [x] 動作類工具的 `workspace` 參數是**斷言**而不是選擇器。指名的不是開著的那一個 → `WorkspaceNotActive`，訊息說出現在開著的是哪一個、要先呼叫 `switch_workspace`。
- [x] 不採「暗中切過去再切回來」：那會把使用者從他正在看的畫面上拉走，去服務一個他沒問的問題。多一次 round trip 是呼叫端的成本，暗中切換的成本在使用者身上。
- [x] 讀取**不受**這條限制——它照舊可以跨所有工作區（`workspace: "all"`）。所以「用 all 找到一個項目、接著啟動它」這條路需要中間插一次 `switch_workspace`，伺服器 instructions 與工具描述都寫明了。

### `ReadState`：只有使用中的工作區是活的

- [x] 唯讀工具改為**優先問 app**：`WorkspaceAccess.Open()` 每次呼叫先發一個 `ReadState`，連不上就退回磁碟。
- [x] `ReadState` 只回傳設定與**使用中工作區**的內容。其他工作區不是活的，它們是 app 根本沒拿在手上的檔案，由 MCP 伺服器自己讀既正確又少一跳。
- [x] 這剛好把 A3 那個必須寫進工具說明的落差補掉了：搜尋框有字時 app 把修改壓在記憶體裡（`SaveItems()` 提早 return），而處於那個狀態的工作區，定義上就是使用者正在用的那一個——也就是 `ReadState` 回傳的那一個。
- [x] `ReadState` 回傳的 config 只帶工作區清單，`Settings` 與 `WindowState` 留在預設值。透過 MCP 讀寫應用程式設定不在本階段範圍，把使用者的快捷鍵與偏好送上一條用不到它們的線沒有好處。
- [x] 順帶把使用中工作區的 `AppCount` 用活的 `ItemCount` 蓋掉：那個欄位是存檔時才更新的反正規化值，而這一個工作區的真實內容就在手上。
- [x] `ReadState` **永不**啟動 app。一個問題不構成把使用者機器上的程式載進記憶體的理由，而同一個問題從磁碟就有正確答案。
- [x] 投影是同一份：回應裡的項目一律經 `AutomationQuery.Describe`（A 期的 `Summarize` 改為 public），讓呼叫端不必調和同一列資料的兩種描述。

## B3. 需要先拆的既有程式碼

- [x] `DeleteAppsAsync` / `DeleteFolderAsync` / `RenameFolderAsync` / `NewFolderAsync` 把 `ContentDialog` 與實際動作綁在同一個方法裡。抽出 `DeleteAppsCore` / `DeleteFolderCore` / `RenameFolderCore` / `CreateFolderCore`，讓 UI 路徑與自動化路徑共用後半段。
- [x] 自動化路徑**沒有二次確認**，這是刻意的：確認對話框守的是「使用者點錯」，而這裡的呼叫端是 client 自己的確認流程（工具標 `destructiveHint`）。這一點在工具描述裡講明。
- [x] `LaunchCore` 改為回傳 `Exception?`。兩個 UI 呼叫端照舊忽略它——程式沒出現就是回報——但自動化呼叫端沒有這種回饋，必須被告知（`LaunchFailed` 帶上 OS 說的話）。
- [x] `SwitchWorkspaceAsync` **不需要**改存取層級：轉接層是同一個 class 的 partial，private 就看得到。原本的規畫寫錯了。但它一直只被 picker 的 `SelectionChanged` 呼叫，所以從來不必自己移動 picker；因此新增 `SwitchWorkspaceFromCodeAsync`（設 `_suppressWorkspaceSwitch`、移 combo、再呼叫原方法），放在 `MainWindow.Workspace.cs` 原方法旁邊——會去找「picker 怎麼保持同步」的讀者會在那裡找。切換本身仍然一律走 `SwitchWorkspaceAsync`（第五階段 §3 的先存再重置不能有第二條路徑）。

### 抽核心時抓到的三個既有缺陷

`SaveItems()` 在搜尋字串非空時 early-return，這個陷阱 `PersistAfterEdit` 的註解記錄過一次。抽共用核心時又在三個地方發現它：

- [x] `NewFolderAsync` 建立資料夾 → 改用 `PersistAfterEdit()`。
- [x] `RenameFolderAsync` 重新命名資料夾 → 同上。
- [x] `ClearRecentLaunches`（托盤選單也會呼叫）→ 同上，並改為回傳清掉的筆數供自動化回報。

三者都可以在搜尋框有字時被觸發（側欄與托盤都還在），而結果是改動要等下一次 `CommitSave()` 才落地。這是既有缺陷而不是本階段造成的，但自動化路徑每次都會踩到，所以一併修掉而非只在自動化那側繞過。

## B4. B 期工具清單

| 工具 | 註記 |
|---|---|
| `launch_item` | 核心用途。回傳實際解析到的項目，讓 client 能確認啟動的是哪一個 |
| `add_item` | `kind` 必填 |
| `update_item` | 省略的欄位不動，空字串清掉 |
| `delete_item` | `destructiveHint` |
| `move_item` | 省略 `folder` 等於移出資料夾 |
| `create_folder` / `rename_folder` | |
| `delete_folder` | `destructiveHint`，且連裡面的項目一起刪 |
| `set_item_tags` | 取代而非追加；空清單等於清空 |
| `create_tag` | |
| `switch_workspace` | 其餘動作類工具作用的目標由它決定 |
| `show_window` / `hide_window` | |
| `clear_recent_launches` | `destructiveHint`：這份清單也是托盤選單與 `search_items` 的排序輸入 |

- [x] 共 20 個工具（A 期六個查詢 + B 期十四個動作），放在 `mcp/AceRun.Mcp/Tools/ActionTools.cs`，與 `QueryTools.cs` 分開：那一份整份都是 `ReadOnly` + `Idempotent`，這一份整份都不是。
- [x] `add_item` 的驗證放 Core（`core/AceRun.Core/Automation/AutomationEdit.cs`）：App 項目檢查路徑存在，URL 走既有的 `UrlUtil.TryNormalize`，建立一律經 `ItemFactory`，不另造 `AppItem`。驗證全部在動手之前跑完，不做半套。
- [x] **`kind` 必填，不從目標推論。** 項目型別建立時決定且不可事後切換（第七階段 §4），所以猜錯是永久的。一個單字的參數不值得拿這個換。
- [x] **路徑必須存在。** app 自己的路徑來自 picker 或拖放，一定存在；這一條來自呼叫端的信念，而猜錯的代價落在使用者身上——他會發現一個按了沒反應的項目，而且不知道它從哪來。工作目錄同理（`ProcessStartInfo` 會在啟動時才丟例外）。讀取那側**不**檢查存在性：照實回報已經存好的東西是另一回事。
- [x] 路徑前後成對的引號會被剝掉。引號在 Windows 檔名裡不合法，所以不會誤殺任何東西，而從命令列複製來的路徑常常帶著它們。
- [x] URL 項目不接受 `arguments` / `workingDirectory` / `runAsAdmin`（`FieldNotApplicable`）。第七階段 §5 說它們不參與啟動，存一個永遠不會生效的值等於描述一個不可能發生的啟動。拒絕而非忽略：忽略會讓呼叫端以為設成功了。
- [x] `update_item` 不接受改 `kind`（`KindNotEditable`）。工具本身沒有這個參數，這條只對直接打管線的呼叫端生效。
- [x] 標籤一律以 id 指派（標籤是共用實例，第八階段）。`set_item_tags` 收 id 或名稱，解析完後經 `TagOrdering.InWorkspaceOrder` 排序再寫入，維持「同樣標籤的兩個磁磚上的點會對齊」這條不變量。
- [x] `create_tag` 允許重複名稱，因為管理對話框允許。自動化不是發明 UI 沒有的規則的地方；代價落在之後用那個名字下指令的人身上，他會收到「這個名稱不唯一」並被要求給 id。
- [x] `hide_window` 在既沒有托盤圖示也沒有全域快捷鍵時拒絕（`NoWayToRestore`）：那樣藏起來就沒有任何東西能把它叫回來。close-to-tray 為同一個理由有同一個守衛。
- [x] `show_window` / `hide_window` 算「有改東西」，因此唯讀檔位下一併拒絕。唯讀的語意是「可以看，不要動」，而視窗出現在使用者螢幕上就是動。

## B5. app 未執行時

- [x] 唯讀工具照 A 期走磁碟，照常回答。
- [x] 需要 app 的工具：以 `--tray`（`AutomationLayout.TrayArgument`）啟動 `ace-run.exe`，之後在 20 秒內反覆嘗試連線。`--tray` 不會把視窗丟到使用者臉上，`AppInstance.FindOrRegisterForKey("AceRun-Main")` 保證不會起第二個實例。
- [x] exe 位置從 MCP 伺服器自己的目錄解析（`AutomationLayout.Sibling`，兩者同資料夾發佈），不猜測安裝路徑、不寫登錄檔。開發建置下兩個 exe 各在自己的 `bin`，此時直接回報「找不到」而不是去搜尋。
- [x] 自動啟動可用環境變數 `ACERUN_MCP_NO_AUTOSTART` 關掉（預設開啟）。
- [x] 連不上時的訊息要能讓 agent 轉達給使用者：它分得出三種情況——「app 在跑但介面沒在聽」、「啟動了但沒等到」、「根本找不到 app」——並點名設定裡那個檔位的名字。
- [x] **app 已經在跑就不要再啟動它。** 管線不在而 app 在，唯一的解釋就是檔位關著；此時去啟動它比什麼都不做更糟：單一實例的轉送會把使用者的視窗無故拉到前景，而呼叫端還要把 20 秒的啟動等待整個等完，最後拿到的還是一句錯的「啟動了但介面沒上來」。接上真實 app 實測 21 秒。改成先用執行檔名查行程、命中就直接回答「在跑，但介面關著」之後是 0 秒。
  - 用行程名判斷是刻意的粗糙：它只決定措辭與要不要啟動。提權或別的 session 裡的實例也會被算成「在跑」，但兩種情況要請使用者做的事一樣（去開那個檔位），因為管線本來就只授權目前這個使用者。

## B6. 安全與設定

`launch_item` 加上 `add_item` 等於「LLM 可以執行任意程式」。A 期沒有這個語意，B 期才需要閘門：

- [x] `AppSettings.Automation` 新增三段式開關，**預設關閉**。這是少數不以「維持現行行為」為預設理由的設定——現行行為是「沒有這個介面」，預設關閉才是它的延續。
- [x] 三段式而非布林：`Off` / `ReadOnly` / `Full`。唯讀檔位讓使用者能給 agent 查詢能力而不給執行能力。
- [x] 檔位在**兩個層次**上生效：`Off` 時管線根本不聽（連得上就代表至少是唯讀），`ReadOnly` 時管線聽但只答 `ReadState`。所以「關掉」不只是拒絕，是沒有東西在那裡。
- [x] `AppSettings.AutomationAllowAdminLaunch`，預設 false：`RunAsAdmin` 的項目從自動化啟動會冒出使用者完全沒預期的 UAC 視窗，而畫面上沒有任何東西說明是誰要求的。獨立開關而非併入 `Full`，因為後果的性質不同。
- [x] 設定頁加「複製 MCP 設定」按鈕，輸出含絕對路徑的 `mcpServers` JSON 片段，省掉手填。路徑是使用者唯一沒辦法自己生出來的東西——伺服器躺在一個他從來不需要打開的發佈資料夾裡。
  - 片段由序列化器產生而非字串拼接：值是 Windows 路徑，每個反斜線都要變成兩個。這正是手寫 JSON 會寫錯的地方。
  - 只輸出 `mcpServers` 這一種形狀（Claude Desktop / Claude Code）。剪貼簿只能放一個答案，VS Code 的 `servers` + `type: "stdio"` 形狀留在 README。
  - 開發建置下 `AceRun.Mcp.exe` 不在旁邊，此時以 `InfoBar` 說明，而不是給出一份指向不存在檔案的設定。
- [x] 開關切換即生效：開啟時起管線，關閉時收掉，不需重啟。要重啟才生效的設定，使用者沒辦法確認自己到底改了沒有。
- [x] 啟動時的那一次**不在** `ApplySettings()` 裡，而在 `InitializeWorkspacesAsync` 載完第一個工作區之後。前者跑在工作區載入之前，落在那個縫隙裡的請求會拿著空集合被回答——「你沒有任何項目」比等一下更糟。
- [x] 管理員開關與「複製設定」按鈕在檔位不足時 disable（前者需要 `Full`，後者只要不是 `Off`）：一個按了沒作用的活開關比一個灰掉的開關更糟。
- [x] 三份 `.resw` 同步，沿用 `Domain_Thing` 命名慣例（`Settings_Automation_*`）。
- [x] 第十階段 §1 的 `AppSettings` 欄位清單與設定畫面分組已一併補上（該檔 §1 與新增的 §7）。

## B7. B 期測試

- [x] Core 新增 `AutomationCommandTests`（26 項）與 `AutomationEditTests`（30 項），另在 `ItemLookupTests` / `AutomationQueryTests` 補上項目解析與 `FolderOf`。全套 435 項通過。
- [x] 框架的單行性有測試，而且是用一個值裡面含換行的請求去測——顯示名稱是從別處貼進來的，一個換行就會讓它變成兩則都解析不了的訊息。
- [x] 列舉完整性有測試：`Validate()` 對每一個 `AutomationAction` 都不會落到 `Internal`（落到那裡代表有人加了命令卻沒決定它需要什麼參數），而 `ReadState` 是唯一 `ChangesSomething == false` 的那一個。
- [x] 「空字串清掉、null 不動」在 round-trip 之後仍然分得開，有測試釘住。
- [x] 管線與轉接層留在 `win/`，以一支打管線的冒煙腳本驗證：腳本自己當 app（用 `NamedPipeServerStream` 占住同一個名字、一行一答），對真實的 `AceRun.Mcp.exe` 跑 `tools/list` 與六次 `tools/call`。驗到的東西：20 個工具與它們的 annotation（三個 `destructiveHint`、六個 `readOnlyHint`）、`ReadState` 被拒時查詢確實退回磁碟、`launch_item` 的項目投影（含日文原樣往返）、以及 `NoWayToRestore` / `ItemAmbiguous`（附候選 id）/ `InvalidTarget` / `WorkspaceNotActive` 四種訊息都完整送到 client。
  - 這支腳本的第一版讀出來是亂碼：PowerShell 5.1 把沒有 BOM 的腳本當 ANSI 讀，日文字串在進管線之前就已經壞了。是測試工具的問題，不是傳輸的問題——兩端都明確用無 BOM 的 UTF-8。加上 BOM 之後往返正確。
  - 另一個踩到的坑：函式參數不能叫 `$args`（PowerShell 的自動變數），拼出來的 JSON 會是空的。與之前 `Measure` 被別名蓋掉是同一類問題。
- [x] 以真實的 app 跑完整的端到端：三個檔位、會動到 app 的那十四個工具、以及畫面／托盤／最近清單的同步。`Environment.GetFolderPath` 不吃 `LOCALAPPDATA` 環境變數，沒辦法把 app 指到暫存資料目錄，所以測的是使用者真正的資料目錄：事前把整個 `%LOCALAPPDATA%\AceRun` 快照起來，測試資料全部建在空的 `Links` 工作區裡，事後還原並 diff 確認位元相同。
  - 檔位各自驗過：`Off` 時管線根本不存在、動作被拒而查詢照答；`ReadOnly` 時管線在、查詢答、動作一律回「唯讀」；`Full` 時全通。三段都是從設定視窗的 ComboBox 切的，切完立刻生效（管線隨之出現／消失），並且確實寫進 `config.json`；管理員開關與「複製設定」按鈕的 disable 條件也跟著檔位走。
  - 「活的狀態」有獨立證據：app 開著的時候直接把一個假項目寫進使用中工作區的 JSON，`list_items` 與 `search_items`（連 `workspace: "all"`）都看不到它——答案來自視窗裡的集合，不是磁碟。
  - 動作：`create_folder`、`add_item`（App 與 Url，含裸網域補 `https://`、名稱留給 app 決定）、`update_item`、`set_item_tags`、`create_tag`、`move_item`（含省略 `folder` 等於移出資料夾）、`rename_folder`、`launch_item`、`delete_item`、`delete_folder`、`switch_workspace`、`hide_window`、`show_window`、`clear_recent_launches`。日文與中文的名稱全程原樣往返。
  - 拒絕的那一側也都收到了正確的句子：`InvalidTarget`、`InvalidKind`、`FieldNotApplicable`（URL 項目沒有參數）、`MissingArgument`（什麼都沒改的 `update_item`）、`InvalidColor`、`WorkspaceNotActive`、`AdminLaunchNotAllowed`、`Busy`——最後這個是開著「標籤管理」對話框時下 `add_item`，動作被拒而查詢照答，關掉對話框之後同一個改動就通了。
  - 同步：側邊欄與格線即時跟上（`未分類 2` / `改名テスト 1`），視窗標題跟著 `switch_workspace` 改，`icons/<id>` 在新增後出現、刪除後消失，托盤選單裡也真的出現了被自動化啟動的那個項目。
  - 發佈版的佈局另外跑了一次：同資料夾的路徑解析、app 關著時由 `AceRun.Mcp.exe` 把它叫起來（1 秒，`--tray`，沒有第二個實例），以及「複製 MCP 設定」在兩個 exe 真的相鄰時給出正確的 JSON（開發建置下則是那句警告）。
  - 托盤選單讀不到不代表它壞了：`ContextMenuMode.PopupMenu` 做出來的是真的 Win32 選單，UI Automation 看它是一個空的 `#32768`。要驗內容就對那個視窗發 `MN_GETHMENU`（0x01E1）拿 `HMENU`，再用 `GetMenuItemCount` / `GetMenuStringW` 逐項讀；鍵盤也通，右鍵之後 `{UP}{ENTER}` 就是「結束」。
- [x] 回歸重點驗過：搜尋框裡留著字（搜尋模式開著、結果列著）的時候下 `update_item`，改動確實落到磁碟——§B2 的「存檔一律走 `CommitSave()`」與 §B3 換掉的那三處，守的就是這條。

---

## 套件版本

- [x] `ModelContextProtocol` **2.3.0**（Apache-2.0，`csharp.sdk.modelcontextprotocol.io`），加上 `Microsoft.Extensions.Hosting` **10.0.13**——主套件只相依 `Hosting.Abstractions`，而 `Host.CreateApplicationBuilder` 在實作套件裡。版本跟著 SDK 釘的那一組 Extensions 走，不混版。
- 2026-10-31 查證：`api.nuget.org/v3-flatcontainer/modelcontextprotocol/index.json` 的版本清單結尾為 `2.0.0, 2.1.0, 2.2.0, 2.3.0`，含 prerelease 在內沒有更新的版本；registration index 的 `published` 為 `2026-10-20T23:46Z`，與套件內 `lib/net10.0/*.dll` 的時間一致。
- 2.3.0 的 nuspec 有明確的 `net10.0` 相依群組，該群組只帶 `ModelContextProtocol.Core [2.3.0]`、`Microsoft.Extensions.Caching.Abstractions 10.0.13`、`Microsoft.Extensions.Hosting.Abstractions 10.0.13`；`Core` 在 net10.0 下只帶 `Microsoft.Extensions.AI.Abstractions 10.11.0` 與 `Microsoft.Extensions.Logging.Abstractions 10.0.13`。`System.IO.Pipelines`、`System.Net.ServerSentEvents`、`Microsoft.Bcl.*` 那一串只出現在 net8.0 與 netstandard2.0 群組，這個 TFM 用不到。
- **要驗證版本就查 flat-container index，不要靠搜尋引擎**：撰寫這份規格時第一次查到的「最新版 2.0.0」來自過期的快取頁面，差了三個小版。

## 發佈

- [x] `AceRun.Mcp.exe` 落在 `ace-run.exe` 旁邊（§B5 的路徑解析與 §B6 的「複製設定」都依賴這件事）。做法是 `ace-run.csproj` 的 `PublishAutomationServer` target（`AfterTargets="Publish"`）呼叫 MSBuild 去發佈那個專案到同一個 `PublishDir`。
- [x] **不是 ProjectReference。** 加參考會把 `ModelContextProtocol` 與 `Microsoft.Extensions.Hosting` 拉進 app 的輸出，正是 §A1 要關在 `mcp/` 裡的東西；而 app 根本不呼叫伺服器（兩者走管線），沒有編譯期相依可以表達。
- [x] 幾個覆寫不能漏：`Platform=AnyCPU`（app 是 x86/x64/ARM64，伺服器是 AnyCPU，繼承過去會直接被拒）、`PublishProfile=`（命令列傳的設定檔名是 global property，會讓 MSBuild 去 `mcp/` 底下找同名檔案）、`WindowsAppSDKSelfContained=false`（伺服器沒有 WinAppSDK 可以打包）。`SelfContained` 與 RID 刻意繼承，讓兩個 exe 一起 framework-dependent 或一起 self-contained。
- [x] 伺服器專案要宣告 `RuntimeIdentifiers`，否則 restore 不會在 `project.assets.json` 裡產生 RID 專屬 target，發佈會以 `NETSDK1047` 失敗。同理 target 裡要先單獨呼叫一次 `Restore`——它不是 ProjectReference，外層發佈不會替它還原。
- [x] self-contained 下兩個 exe 共用同一份 runtime 檔案（同 RID、同 TFM，內容相同、互相覆蓋）。實測體積增量：

  | 設定檔 | 只有 app | 加上伺服器 | 增量 |
  |---|---|---|---|
  | `environment-deps` | 82.0 MB | 86.3 MB | +4.3 MB |
  | `self-contained` | 275.6 MB（526 檔） | 283.4 MB（564 檔） | +7.8 MB |

  self-contained 增量較大是因為 R2R 也套用在伺服器自己的組件上。要量只有 app 的那一側，傳 `-p:PublishAutomationServer=false`。

## 不在本階段範圍

- [ ] MCP resources 與 prompts：只做 tools。資源在各 client 的支援度不一致，而這個 app 的操作全部是動作語意。
- [ ] 回傳項目圖示（base64 影像）。
- [ ] 透過 MCP 讀寫應用程式設定（主題、語言、快捷鍵、開機啟動）。
- [ ] `.acerun` 匯入匯出、工作區的新增、刪除與重新命名。
- [ ] 項目順序調整（拖曳與 Organize 的等價操作）。
- [ ] 遠端存取：伺服器只走 stdio，管線只在本機、只授權目前使用者。沒有 HTTP 傳輸、沒有 TCP。
