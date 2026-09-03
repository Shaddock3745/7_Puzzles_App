# CLAUDE.md

> 給 Claude 看的專案交接文件。新 session／換裝置時先讀這份。
> 最後更新：2026-09-03

---

## 這是什麼專案

Unity 6000.3.7f1 的**七巧板拼圖遊戲**，參加 2026 大專校院資訊應用服務創新競賽（資訊應用組 IP + 鈦坦敏捷開發特別獎），**報名截止 10/5**，單人開發。

核心賣點不是遊戲本身，是「把難度與玩家實力量化成可驗證的系統」：演算法即時生成題目、特徵函數算難度、ELO 換算玩家實力，三者互相驅動。**不用生成式 AI、不連網。**

## 文件讀哪些、什麼順序

| 檔案 | 是什麼 | 權威性 |
|---|---|---|
| `七巧板儲存規格.md` | **模型層的權威來源**（使用者寫的，已用程式驗證過） | 最高 |
| `互動設計.md` | **互動層的權威來源**（盒子模式，2026-09-03 review 定案） | 最高 |
| `企劃書_七巧板拼圖遊戲.md` | 競賽企劃書 | 商業面權威，技術細節已過時（見下） |
| `程式說明書.md` | 現有程式逐支說明、API、陷阱、效能備忘 | 描述現況，不是規格 |
| `開發進度.md` | P0～P4 工作細項、完成定義、待決事項 | 工作清單 |

**趕時間的話**：`互動設計.md` + `開發進度.md` 就夠開始工作。

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
| 階段 | **P0 完成，P1 未開始** |
| 程式 | `Assets/Scripts/Data` 14 支、`Assets/Scripts/Core` 4 支 |
| 測試 | 32 個全過（`Assets/Tests/EditMode`） |
| 看板 | GitHub Projects <https://github.com/users/Shaddock3745/projects/1>，repo `Shaddock3745/7_Puzzles_App` |

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

在 scratchpad 建一個 `tests.csproj`：

```xml
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <TargetFramework>net10.0</TargetFramework>
    <Nullable>disable</Nullable>
    <IsPackable>false</IsPackable>
    <EnableDefaultCompileItems>false</EnableDefaultCompileItems>
  </PropertyGroup>
  <ItemGroup>
    <PackageReference Include="Microsoft.NET.Test.Sdk" Version="17.11.1" />
    <PackageReference Include="NUnit" Version="3.14.0" />
    <PackageReference Include="NUnit3TestAdapter" Version="4.6.0" />
  </ItemGroup>
  <ItemGroup>
    <Compile Include="D:/Unity Projects/7_Puzzles_App/Assets/Scripts/Data/*.cs" />
    <Compile Include="D:/Unity Projects/7_Puzzles_App/Assets/Scripts/Core/*.cs" />
    <Compile Include="D:/Unity Projects/7_Puzzles_App/Assets/Tests/EditMode/*.cs" />
    <Compile Include="UnityStub.cs" />
  </ItemGroup>
</Project>
```

加一個 `UnityStub.cs` 補上 Unity 的相依（只用到這兩個）：

```csharp
namespace UnityEngine
{
    public struct Vector2
    {
        public float x, y;
        public Vector2(float x, float y) { this.x = x; this.y = y; }
        public override string ToString() => $"({x}, {y})";
    }

    public static class JsonUtility
    {
        private static readonly System.Text.Json.JsonSerializerOptions Opt =
            new System.Text.Json.JsonSerializerOptions { IncludeFields = true };
        public static string ToJson(object o, bool pretty = false)
            => System.Text.Json.JsonSerializer.Serialize(o, o.GetType(), Opt);
        public static T FromJson<T>(string json)
            => System.Text.Json.JsonSerializer.Deserialize<T>(json, Opt);
    }
}
```

**NUnit 一定要 3.x**（Unity 用 3.x；NUnit 4 拿掉了 `Assert.AreEqual` / `Assert.IsTrue`，會編不過）。

**動到 Data 層一定要重跑全部測試再往下走。**

---

## 容易踩的坑

| 坑 | 說明 |
|---|---|
| **`Placement.Equals` 是姿態相等，不是形狀相等** | 正方形 `Rotation = 0` 與 `= 2` 佔位一模一樣但不相等。要問「是不是同一件事」用 `Canonical`；要比幾何位置比頂點集合 |
| **`PiecePrototype.Get()` 回傳共用單例** | 裡面的陣列改下去會污染全域 |
| **`Placement.Vertices()` 在 flip 時反轉頂點順序** | 所以翻面的片 `Vertices()[0] != ToWorld(Prototype.Vertices[0])`。`Nodes()` 刻意不反轉（節點索引是儲存契約） |
| **`Scalar.MultiplyByHalfRoot2()` 的偶數 assert 不要拿掉** | 45° 旋轉的封閉性檢查 |
| **SAT 中距離 0 算「分離」** | 只有內部交集才算重疊，否則邊貼合與點接觸全誤判 |
| **共線邊只在邊界度數 = 2 時合併** | 捏點度數是 4，合併會毀掉拓樸 |
| **`Canonical.PuzzleKey` 要跑 16 次輪廓追蹤** | 不便宜，算過就存起來。盒子模式下遊玩迴圈已經不用它了 |
| **`Serialization.FromDto` 不重算 key** | 相信檔案裡存的 |
| **`BoardState.Clear()` 會發七次 `Changed`** | P1 加 undo 時一併處理 |

---

## 環境限制

- **`gh` CLI 沒安裝** → 不能幫使用者開 issue／PR，只能給文字讓他自己貼
- **Git Bash 裡沒有 `python`** → 要寫腳本用 PowerShell 或 Node
- **PowerShell 是 5.1** → 沒有 `&&`、`??`、三元運算子；`ConvertFrom-Json` 回 PSCustomObject
- **CJK 檔名在 Git Bash 的 `ls` / `find` 可能查不到** → 用 PowerShell 的 `Get-ChildItem` 確認才準（這個 session 誤判過一次）

---

## 跟使用者合作的方式

- **文件與溝通用繁體中文。**
- 他**會把程式讀到懂才往下走**（原話：「我習慣是把程式都讀到精再繼續」）。解釋要給實作細節、控制流程、邊界情況，不要只給結論。
- 他會**從使用者情境回頭 review 架構**，而且會逐點修正。提出設計時要主動把連鎖反應推完整、把風險講白，不要報喜不報憂。
- **commit / push 要先問**。這個 session 到目前為止程式都還沒進版控（P0-7）。
- 文件更新要同步 repo 的 `.md` 與已發布的 artifact（網址不變，重新 publish 同一個檔案路徑即可）。

### 已發布的 artifact

| 文件 | 網址 |
|---|---|
| 程式說明書 | <https://claude.ai/code/artifact/04d7059d-d155-40a5-ac22-dafb3b166f00> |
| 開發進度 | <https://claude.ai/code/artifact/94f5f82c-868a-4149-af9b-35701eab6eac> |
| 互動設計 | <https://claude.ai/code/artifact/e32f7dae-886a-4ad2-bc95-33761bc180ce> |

---

## 下一步

1. **P0-7**：把程式 commit + push（`origin/main` 還停在「空專案」）。commit 訊息草稿在對話紀錄裡，可用 `Closes #4 #5 #6` 自動關掉看板卡片
2. **P0-8**：Unity 內跑一次 Test Runner
3. **P1-4**：`Geometry` 新增 `PointInRegion` 與 `SegmentsProperlyCross`（盒內硬限制靠它，約半天）
4. **P1-1 ～ P1-7**：做到「一片能拖、能吸進盒子、能轉」

P1 完整 13 項見 `開發進度.md`。
