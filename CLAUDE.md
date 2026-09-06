# CLAUDE.md

> 給 Claude 看的專案交接文件。新 session／換裝置時先讀這份。
> 最後更新：2026-09-04

---

## 這是什麼專案

Unity 6000.3.7f1 的**七巧板拼圖遊戲**，參加 2026 大專校院資訊應用服務創新競賽（資訊應用組 IP + 鈦坦敏捷開發特別獎），**報名截止 10/5**，單人開發。

核心賣點不是遊戲本身，是「把難度與玩家實力量化成可驗證的系統」：演算法即時生成題目、特徵函數算難度、ELO 換算玩家實力，三者互相驅動。**不用生成式 AI、不連網。**

## 文件讀哪些、什麼順序

| 檔案 | 是什麼 | 權威性 |
|---|---|---|
| `七巧板儲存規格.md` | **模型層的權威來源**（使用者寫的，已用程式驗證過） | 最高 |
| `互動設計.md` | **互動層的權威來源**（盒子模式；§2 座標與版面層、§13 片的顯示層、§14 盒子與這一關的狀態，2026-09-04 定案） | 最高 |
| `企劃書_七巧板拼圖遊戲.md` | 競賽企劃書 | 商業面權威，技術細節已過時（見下） |
| `程式說明書.md` | 現有程式逐支說明、API、陷阱、效能備忘 | 描述現況，不是規格 |
| `開發進度.md` | P0～P4 工作細項、完成定義、待決事項 | 工作清單 |
| `待辦清單.md` | **還沒做完的事的單一清單**（阻塞項、P1 剩餘、待決、技術債） | 每天的工作起點 |
| `除錯記錄.md` | 查過的「不報錯但行為不對」的問題。每則四段：預期／實際／原因（含**怎麼發現的**）／處理（含**失敗的嘗試**） | 遇到怪症狀先翻這份 |

**趕時間的話**：`待辦清單.md` 開始做事，`互動設計.md` 查規格。

### 已知的文件過時處

- **企劃書 5.1 的 `PieceState` 是浮點版本**（`Vector2 position` / `float rotationDegrees`）。實際實作是精確座標，見鐵則一。使用者知道，還沒改。
- **企劃書通篇假設「自由拼」**。已改成盒子模式。

---

## 三條鐵則（破了會出現最難查的 bug）

### 一、浮點永遠不進模型

座標型別是 `Scalar`（`n + m√2`，兩個 `long`），不是 `float`。手指位置這類浮點值一律先過 `SnapSolver` 轉成精確座標才能進 `BoardState`。

「兩個點是不是同一點」是整數比較，不是容差判斷。七巧板大量出現點接觸與邊貼合，用容差會很痛。

### 二、節點索引不可更動

每片的節點順序由 `PiecePrototype` 沿 CCW 邊界走訪產生，`Arrangement.Links` 存的就是這個索引。**改了原型頂點或節點產生順序 = 資料庫裡所有的解全部失效，而且不會報錯。**

### 三、只存相對編碼，座標需要時重算

`Arrangement` 只存七片的 `Rotation`/`IsFlipped` 加六條連接，不存座標。頂點、輪廓、絕對座標一律不入庫。

---

## 命名規則

**識別字一律用完整單字，不縮寫**（`triangle` 不寫成 `tri`、`direction` 不寫成 `dir`、`length` 不寫成 `len`），也不用 `temp`/`tmp` 這類名稱。型別、方法、參數、區域變數都適用。

**例外**：`for` 迴圈的 `i` / `j` / `k` 可以。

專案已經全面改過一輪，新程式照這個規則寫。

---

## 目前狀態

| | |
|---|---|
| 階段 | **P0 完成；P1 進行中（13 項完成 5 項：P1-1、P1-2、P1-3、P1-4、P1-7）** |
| 程式 | `Assets/Scripts/Data` 15 支、`Assets/Scripts/Core` 8 支、`Assets/Scripts/GameObject` 7 支 |
| 測試 | 106 個全過（`Assets/Tests/EditMode`）。⚠️ `dotnet` 只編 Data/Core/Tests，`GameObject/` 那幾支只有 Unity 驗得到 |
| 看板 | GitHub Projects：`P0_Skeleton` 與 `P1_Prototype`（<https://github.com/users/Shaddock3745/projects/1>、`/2`），repo `Shaddock3745/7_Puzzles_App`。P1-1 ～ P1-9 = issue #9 ～ #17 |

### 2026-09-03：P1-4 完成（區域判定）

`Assets/Scripts/Data/Region.cs` 是新的一支，加上 `Geometry` 的四個判斷式
（`Cross` / `IsOnSegment` / `SegmentsProperlyCross` / `StrictlyInsideConvexPolygon`）。

`Region` 是「有洞的封閉二維區域」：盒子是它、片是它、之後 P1-11 的剩餘連通塊也是它。
建構時凍結攤平的環頂點、×2 座標副本、`Bounds`、每個洞的一個內部點，之後唯讀。
`Puzzle.Region` 是 lazy 的（題目自身座標系）；盤面上的盒子用 `Region.FromPuzzle(puzzle, boxOrigin)`。

`ContainsPolygon` 是**五條規則**，由便宜到貴。兩條最容易被誤認為冗餘的：

- **規則 5（每條片邊的區間中點都在區域內）**：擋「片跨過捏點」。一片可以三個頂點都停在
  輪廓的角上、每條邊都只在端點碰到輪廓（不算真交叉），卻有一整塊內部戳在捏點的另一側。
  漏掉它會破壞「七片都合法 ⟹ 填滿」的證明，而且不會報錯
- **規則 4（洞的內部點不在片內）**：擋「片整個蓋住一個洞」。規則 3 抓不到，因為片剛好等於
  洞時，洞的頂點都落在片的邊上。也**不能**改用「洞的頂點在片內」來測，會誤殺正常擺法

細節見 `程式說明書.md` 第 13 章。

### 2026-09-03：P1-7 完成（盒子接進吸附層）

`Region.BoundaryNodes`（盒子輪廓上的節點，規則跟片的節點一樣）成為吸附錨點之一，
**所以第一片就有磁性**——會吸到盒子的角落或邊上。

`SnapSolver.Candidates` / `Resolve` 多兩個選用參數（都加在最後，既有呼叫點不用改）：

```csharp
Resolve(..., currentlySnapped, boxRegion.BoundaryNodes,
             placement => boxRegion.Contains(placement));
```

**分工要記住**：`Candidates` 只做距離篩選；`isAllowed` 與重疊都在 `Resolve` 的
「由近到遠、通過就 return」迴圈裡懶惰檢查——第一個候選通常就通過，只付一次成本；
先過濾整份清單要付 N 次（`ContainsPolygon` 實測 7.3 µs／次）。
有測試 `盒子_Candidates只做距離篩選_不管盒內限制` 專門釘住這件事。

另外 `SnapToOrigin` 改成 `TrySnapToOrigin`（多收 `isAllowed`、回 `bool` + `out`），
並新增 `SnapSolver.RotationCycleLength(kind)`（三角 8 / 正方形 2 / 平行四邊形 4）。

### 2026-09-04：P1-1 完成（座標與版面層）

三個角色都實作完了，`互動設計.md` §2.8 的四個待決也全部拍板。
完整 API 與設計理由見 `程式說明書.md` 第 18 章。

| 角色 | 檔案 | 可離線測 |
|---|---|---|
| 一：純數學 | `Core/ViewportMath.cs` | ✅ |
| 二：座標狀態 | `Core/BoardLayout.cs` | ✅ |
| 三：Unity 驅動 | `GameObject/BoardView.cs` | ❌（所以只剩搬運）|

角色一與角色二**必須**放在 `Assets/Scripts/Core/`，因為 `tests.csproj` 是靠
`../../Assets/Scripts/Core/*.cs` 把原始碼拉進去的。放進 `GameObject/` 就測不了。

**四個待決的定案**：roll 限定 0/90/180/270（型別上就是 `int quarterTurns`）／
自己實作 `ScreenToWorld`（順序契約要求「相機這幀還沒更新答案也要對」，用 `Camera` 做不到）／
覆蓋框走 world-space（但 `WorldToScreen` 還是寫了，理由是 round-trip 測試）／
重算時取消拖曳（P1-1 只發事件，取消是 P1-6 的事）。

**兩個實作時多加的保險**：

- `ViewportSolution` 自己帶著算它的那組螢幕尺寸 → 型別上不可能「拿 A 螢幕的解換算 B 螢幕的座標」
- `LastScreenWidth/Height` 的語意是「**已經成功推送過**」不是「上次看到」。
  `Solve()` 是純函式，`RecordScreenSize()` 是另一個方法，推送成功後才呼叫。
  中途放棄的那一幀不記錄——記了的話永遠不再重算，畫面卡住且不報錯

其他定案：模型:世界 **1:1**（模型→世界只是浮點轉型，沒有係數）、置中靠相機不平移模型
（−寬/2 在 ℤ[√2] 裡除不盡）、係數**重算不快取**、題目旋轉**轉相機**、精選題不 roll、
未放置的片半透明、自適應版面不進 P1。

⚠️ **`BoardView.LayoutRecomputed` 目前沒有訂閱者，這是刻意留給 P1-6 的洞。**
沒接的話症狀是「轉螢幕或拉 Game 視窗時，正在拖的片會突然黏歪」，不會有錯誤訊息。

（場景接線的現況見下面 P1-3 那一節。）

### 2026-09-04：P1-2 完成（片的顯示）

完整規格在 `互動設計.md` §13。

| 檔案 | 在哪 | 可離線測 |
|---|---|---|
| `PieceVertexCache.cs` | `Core` | ✅ 十份浮點頂點；唯一讀 `PiecePrototype` 共用單例的地方 |
| `PieceSorting.cs` | `Core` | ✅ 三層 + 面積 + 槽索引 |
| `PieceSpriteLibrary.cs` | `GameObject` | ❌ 用到 `Texture2D`/`Sprite` |
| `PieceView.cs` | `GameObject` | ❌ `MonoBehaviour` |
| `PiecePreview.cs` | `GameObject` | ❌ **驗證鷹架，P1-5 做好就刪** |

**圖是執行時生成的，不是匯入的美術資源。** 這讓原本 §13.7 那兩個「做錯不會報錯」的
匯入陷阱（Pixels Per Unit、Pivot）整個消失——都由程式算。作法是**先侵蝕「內縮 + 圓角半徑」、
再膨脹回圓角半徑**（形態學），比多邊形偏移好寫太多。⚠️ 距離要用**真正的歐氏距離**，
用「半平面裡最大值」會把角推成尖的，方向剛好相反。

**片記住**：片種、槽索引、旋轉格數(int)、翻面、吸住了沒、正在被拖。
**片刻意不記**：`Placement`（`BoardState` 已經存了）、位置（就是 `transform.position`）。

分界的理由：**旋轉不能放 transform，位置可以**。正方形 rotation 0 與 2 的 transform
完全相同，資訊被銷毀反推不回來；位置則是浮點 round-trip 無損（前提：不掛在有旋轉或縮放的父物件下）。

四個狀態欄位的 setter 都會呼叫刷新，**位置不用**——位置忘了寫是大聲的 bug（片不動一眼看到），
其他四個忘了刷新是安靜的。

⚠️ 翻面用 `SpriteRenderer.flipY`（模型的翻面是 y 取負 = 垂直翻），**不要用負縮放**。

### 2026-09-04：P1-3 完成（盒子 + 「這一關」的擁有者）

完整規格在 `互動設計.md` §14。範圍比原本的「畫個盒子」大——第一次有東西負責「這一關」。

| 檔案 | 在哪 | 可離線測 |
|---|---|---|
| `LevelSession.cs`（含 `LevelData`） | `Core` | ✅ 7 個測試，其中一個是重置測試 |
| `LevelController.cs` | `GameObject` | ❌ |
| `BoxView.cs` | `GameObject` | ❌ |

✅ **場景已接線**（2026-09-04）：`GameScene.unity` 上有 `BoardView` / `BoxView` / `LevelController` / `PiecePreview` 四個物件，相機會自己 fit 到盒子。
⚠️ **`BoxView` 物件的 transform 必須是原點**——mesh 建在世界座標，偏移會讓盒子畫到別的地方，而且不報錯。`BoxView.Awake` 現在會自己拉回來並警告。詳見 `除錯記錄.md` 001。

| 角色 | 是什麼 |
|---|---|
| `LevelSession` | 純 C#、**長生**、可離線測。持有 `Puzzle` / `Region` / 參考解 / **`BoardState`** / 提示次數 |
| `LevelController` | `MonoBehaviour`，只驅動「進來／出去」，照順序叫人 |

**兩者是平行的，不是上下關係。** `LevelSession` 的消費者有一大票（`BoxView`、拖曳、回饋、overlay、過關、提示），
`LevelController` 只是其中一個。掛在它底下的話所有人都要穿過一個 `MonoBehaviour` 拿資料。
**分界線是消費者數量**——對照 `BoardView` 持有 `BoardLayout` 就沒問題，因為後者只有前者自己讀。

取得方式是 `LevelSession.Current` 靜態存取，但**型別本身要保持可以 `new`**（測試要）。

**換題是原地重置不是重建。** 重建的話所有把 `board` 存成欄位的人都會寫到死掉的那一份——
片吸得好好的但盤面永遠空的、過關不觸發、`Changed` 訂閱安靜停掉，**完全不報錯**。
代價是「清乾淨」變成人的責任，所以兩層防呆都做了：per-關 狀態集中成 `LevelData` 一包、重置就換掉整包，
外加一個「載 A → 全部動一遍 → 載 B → 斷言回到初始」的測試（`關卡_換關之後所有狀態都回到初始`）。

**換題的通知照順序直接叫，不發事件**——第 4 步（擺七片）依賴第 2 步（相機 fit），而事件沒有順序保證。

**盒子畫成程序生成 mesh**：參考解七片各自扇形三角化（每片都是凸的），約 14 個三角形，
**洞自動出現**——那些地方本來就沒有片蓋住，所以完全不用寫「有洞多邊形的三角化」（橋接）。
代價是盒子的畫面依賴參考解存在，P2-6a 本來就規劃要一起存。

✅ **`BoardLayout.BoxOrigin` 已經拿掉**（它是唯寫欄位，`Solve()` 從沒用過），`SetBox` 少收一個參數。
盒子原點的家改成 `LevelSession.BoxOriginFor`——無狀態靜態方法算出來，目前值是 `ExactVector2.Zero`。

### 2026-09-03：全專案函式文件化

18 支程式檔全部有 XML doc（`<param>` / `<returns>`）。IDE 提示視窗會顯示，
所以「這個參數是什麼」直接看程式碼就好，`程式說明書.md` 講的是「為什麼這樣設計」。

### 2026-09-03 架構 review：自由拼 → 盒子模式

原本「玩家在空白處拼，形狀對就過關」，改成「**盒子**：題目是盤面上固定位置的容器，玩家填滿它」。

因為吸附有硬限制（只能吸在盒內、不能重疊），**過關判定退化成 `board.AllOnBoard`**——七片都合法放置就必然填滿（面積 32 = 盒子面積）。連帶：

- `Canonical` **退出遊玩迴圈**，只剩出題去重與解數統計兩個後端用途
- `Geometry.AllConnected` 在驗證裡不需要了
- 鏡射誤判問題消失

完整規格見 `互動設計.md`。

### ⚠️ `PuzzleValidator.cs` 是過時的

它還是自由拼版本（用 `Canonical.PuzzleKey` 比對）。檔案裡有註解標記，**不要照它的行為推論設計**。改寫排在 P1-10。

---

## 怎麼跑測試

Unity 內：**Window → General → Test Runner → EditMode → Run All**。

**但更快的方法是用 `dotnet` 在 Unity 外面跑**——這個 session 一直是這樣驗證的，秒級回饋，不用開 Unity：

測試專案就在 repo 裡：`Tools/DotnetTests/`（路徑全是相對的，換電腦不用改）。

```bash
dotnet test Tools/DotnetTests
```

裡面兩個檔案：

- `tests.csproj` —— 用 `../../Assets/Scripts/Data/*.cs`、`../../Assets/Scripts/Core/*.cs`、`../../Assets/Tests/EditMode/*.cs` 三行把原始碼拉進來
- `UnityStub.cs` —— 補上 Unity 的相依（只用到 `Vector2` 與 `JsonUtility`）

**`UnityStub.cs` 一定要放在 `Assets/` 外面。** 放進 `Assets/` 的話 Unity 會一起編，`UnityEngine.Vector2` 就會有兩份定義。`Tools/` 在 `Assets/` 外，Unity 完全不會看它。

`bin/`、`obj/` 已經在 `.gitignore` 裡。

**NUnit 一定要 3.x**（Unity 用 3.x；NUnit 4 拿掉了 `Assert.AreEqual` / `Assert.IsTrue`，會編不過）。

**動到 Data 層一定要重跑全部測試再往下走。**

---

## 容易踩的坑

| 坑 | 說明 |
|---|---|
| **`Placement.Equals` 是姿態相等，不是形狀相等** | 正方形 `Rotation = 0` 與 `= 2` 佔位一模一樣但不相等。要問「是不是同一件事」用 `Canonical`；要比幾何位置比頂點集合 |
| **`PiecePrototype.Get()` 回傳共用單例** | 裡面的陣列改下去會污染全域 |
| **`Placement.Vertices()` 在 flip 時反轉寫入順序** | 目的是**把繞向正規化回 CCW**，所以輸出**永遠是 CCW**（測試 `翻轉後繞向重新正規化` 釘住，五種片 × 8 旋轉 × 翻面全驗）。代價是索引對應會變：翻面時 `Vertices()[i] == ToWorld(Prototype.Vertices[n−1−i])`，所以 `Vertices()[0] != ToWorld(Prototype.Vertices[0])`。`Nodes()` 刻意不做這個反轉（節點索引是儲存契約） |
| **背面剔除吃掉翻面的平行四邊形** | ⚠️ 這個坑**跟 `Vertices()` 無關**（它永遠 CCW，三角化不用吃兩種繞向）。真正的來源是用 `transform.localScale.y = -1` 做翻面——負縮放會反轉繞向，背面剔除就整片消失。要嘛不用負縮放（重新產生 mesh），要嘛材質設 `Cull Off` |
| **`Scalar.MultiplyByHalfRoot2()` 的偶數 assert 不要拿掉** | 45° 旋轉的封閉性檢查 |
| **SAT 中距離 0 算「分離」** | 只有內部交集才算重疊，否則邊貼合與點接觸全誤判 |
| **共線邊只在邊界度數 = 2 時合併** | 捏點度數是 4，合併會毀掉拓樸 |
| **`Canonical.PuzzleKey` 要跑 16 次輪廓追蹤** | 不便宜，算過就存起來。盒子模式下遊玩迴圈已經不用它了 |
| **`Serialization.FromDto` 不重算 key** | 相信檔案裡存的 |
| **`BoardState.Clear()` 會發七次 `Changed`** | P1 加 undo 時一併處理 |
| **`Region.ContainsPolygon` 只管出界，不管重疊** | 合法擺放要它與 `Geometry.ConvexOverlap` 兩個都過 |
| **`Region` 內部有兩套座標系** | 原座標與 ×2。中點只在 ×2 那邊算，混用會差一個 2 倍而且不報錯 |
| **專案只開了新 Input System** | `ProjectSettings` 的 `activeInputHandler: 1`。`Input.mousePosition`、`Input.GetMouseButton` 這些舊 API 在 runtime 會直接丟例外。P1-6 要用 `UnityEngine.InputSystem` 的 `Pointer.current` / `Touchscreen.current` |
| **`transform` 不能存旋轉** | 正方形 `rotation = 0` 與 `= 2` 的 transform **完全相同**，反推不回來。旋轉一定要有自己的 int 欄位，transform 只是輸出。位置反過來可以放 transform（浮點 round-trip 無損），前提是不掛在有旋轉或縮放的父物件下 |
| **模型的翻面是垂直翻，不是水平** | `Symmetry.MirrorAcrossXAxis` 是 (x, y) → (x, −y)。所以 Sprite 要用 `flipY` 不是 `flipX` |
| **兩個 pixels per unit 撞名** | Sprite 匯入設定的 **Pixels Per Unit** = 紋理像素 → 世界單位（固定的資產設定）；`SnapSolver.Settings.ScreenPixelsPerModelUnit` = 模型單位 → 螢幕像素（相機 fit 算出來，**每題都變**）。搞混不會報錯，只會手感很怪 |

---

## 環境限制

- **`gh` CLI 沒安裝** → 不能幫使用者開 issue／PR，只能給文字讓他自己貼
- **Git Bash 裡沒有 `python`，也沒有 `node`** → 要寫腳本只能用 PowerShell；改檔案優先用 Edit/Write 工具（CJK 內容用 heredoc 有編碼風險）
- **PowerShell 是 5.1** → 沒有 `&&`、`??`、三元運算子；`ConvertFrom-Json` 回 PSCustomObject
- **CJK 檔名在 Git Bash 的 `ls` / `find` 可能查不到** → 用 PowerShell 的 `Get-ChildItem` 確認才準（這個 session 誤判過一次）

---

## 跟使用者合作的方式

### ⚠️ 協作模式（2026-09-03 起，最重要的一條）

> **他定義資料流與介面，你只做 review。**

流程是：**他提出設計 → 你指出問題與可以改進的地方 → 反覆修到好 → 他自己寫程式**。

討論**離開程式碼本身，停在邏輯層**：需要什麼功能、輸入資料要怎麼變成正確的輸出、
這個介面之後會不會卡住、漏了哪個情況。

**原因（他的原話）**：「不然我沒寫過的東西我會依賴你丟程式碼給我，沒學到東西我感覺好像跟騙人一樣。」
這是學習與作品歸屬的問題，不是效率問題。**不要為了快就直接丟實作。**

具體行為：

| 該做 | 不該做 |
|---|---|
| 指出這樣設計會導致什麼後果、哪裡會卡住 | 未經要求就寫出完整實作丟給他 |
| 舉具體的反例、邊界情況、之後會咬人的地方 | 只說「這樣很好」或只給結論 |
| 陳述事實（某個 API 存在／不存在、某個成本是多少） | 幫他把設計「補完」到不用思考 |
| 他明確要求時才寫程式，而且註解要寫好 | 因為他沒想到就順手替他決定 |

**他還是會讀程式碼**，所以你真的動手寫的時候，
XML doc（`<param>` / `<returns>`）與「為什麼這樣寫」的註解一樣要到位。

之前那次 `PieceView` 的討論（他提「用 transform 存姿態、記種類、吸附交給單例 manager、
不做待用區」，我逐點回應同意／修正）就是這個模式的樣板。

### 「虛擬碼」是指**中文邏輯**，不是 C# 語法

他說「列虛擬碼」的時候，要的是**用中文寫「誰記住什麼、能被要求做什麼、做的時候發生什麼」**，
不是帶大括號與型別的 C# 骨架。

原話（2026-09-04，P1-2）：「你的『虛擬碼』還是太雜了，別用 C# 直接寫中文邏輯就好」。

寫法：用小標題 + 表格 + 條列。型別名（`BoardState`、`Placement`）照寫沒關係，那是專有名詞；
但不要出現 `public`、`=>`、`{ }`、`get; set;`。

### ⚠️ 討論的順序：**先邏輯，美術往後放**（2026-09-04 起）

他講過兩次，第二次是明確要求記下來：

> 「這兩個偏美術，其實你不該現在問我」（2026-09-04，P1-2）
> 「美術的先跳過，先整理邏輯」（2026-09-04，P1-3）

**規劃一個工作項目時，把問題先分兩堆**：

| 先問 | 往後放 |
|---|---|
| 誰持有什麼、誰通知誰、資料往哪流 | 顏色、粗細、圓角、間距 |
| 這個介面之後會不會卡住 | 「好不好看」 |
| 邊界情況、之後會咬人的地方 | 手感的具體數字（那要玩過才準） |

美術的決定**不要拿到邏輯討論裡問**——那會打斷他的思路，而且答案通常是「之後再說」。
等邏輯定案、東西跑得動了，他自己會回頭調。

判準：**這個決定改起來會不會動到資料流？** 不會動到的就是可以往後放的。
（例：P1-2 的「描邊怎麼畫」不動資料流 → 往後放；「片要不要記 `Placement`」會動 → 先問。）

### 其他

- **文件與溝通用繁體中文。**
- 他**會把程式讀到懂才往下走**（原話：「我習慣是把程式都讀到精再繼續」）。解釋要給實作細節、控制流程、邊界情況，不要只給結論。
- 他會**從使用者情境回頭 review 架構**，而且會逐點修正。提出設計時要主動把連鎖反應推完整、把風險講白，不要報喜不報憂。
- **commit / push 要先問**。
- 文件更新只要改 repo 的 `.md`。**artifact 不用管，也不要問要不要同步**——
  他會在換裝置或需要的時候自己更新，主動要求時才做（2026-09-04）。
  真的要更新時：先 `action: "read"` 把線上版讀回來再合併，**不能直接用本地檔案覆蓋**，
  線上版常常比本地新。網址不變，重新 publish 同一個檔案路徑即可。

### 已發布的 artifact

| 文件 | 網址 |
|---|---|
| 程式說明書 | <https://claude.ai/code/artifact/04d7059d-d155-40a5-ac22-dafb3b166f00> |
| 開發進度 | <https://claude.ai/code/artifact/94f5f82c-868a-4149-af9b-35701eab6eac> |
| 互動設計 | <https://claude.ai/code/artifact/e32f7dae-886a-4ad2-bc95-33761bc180ce> |

---

## 下一步

**完整清單看 `待辦清單.md`**，這裡只列頭三件：

1. **在 `GameScene.unity` 掛上 `BoardView`**（空物件 + 指定 `targetCamera`），否則後面全部看不到東西
2. **驗一次 P1-2**：另建一個空物件掛 `PiecePreview`，按 Play 應該看到七片排一列、五種形狀、七個顏色。
   ⚠️ `GameObject/` 那幾支 `dotnet` 編不到，這是它們第一次進 Unity 編譯器
3. **接線**：`GameScene.unity` 上建物件掛 `BoardView` / `BoxView` / `LevelController`
4. **P1-6**：拖曳控制。⚠️ 要訂閱 `BoardView.LayoutRecomputed` 取消進行中的拖曳，
   座標換算走 `BoardView.ScreenToWorld` 不要用 `Camera.ScreenToWorldPoint`

**純 C# 的部分（P1-1、P1-2 的可測部分、P1-4、P1-7）全部做完了**，剩下的都在 Unity 那一側。
P0-8 也結案了（Unity 內編譯通過、Test Runner 全綠）。
