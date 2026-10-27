# 巧級 Tangrade

**可驗證難度的空間邏輯訓練系統**
*Tangrade — A Verifiable-Difficulty Training System for Spatial Logic*

一款七巧板拼圖 App。題目由程式當場生成，難度由題目形狀算出來，玩家實力用西洋棋的等級分（ELO）換算，
下一題再照實力生——出題、難度、實力三者自己轉成一個循環，不需要人工標難度。

2026 大專校院資訊應用服務創新競賽參賽作品（資訊應用組）。**不使用生成式 AI 出題，全程離線，不需要帳號。**

---

## 玩起來是什麼樣子

畫面上有一個題目外框（「盒子」），把下方抽屜裡的七片拖進去填滿就過關。

- 片靠近正確位置會**自動吸附**；可以旋轉、翻面、復原上一步、清空
- 卡住時可以要**提示**：系統在參考解的位置閃示一片
- 過關或放棄後結算實力，只告訴玩家「**下一題會難一點／差不多／簡單一點**」，不顯示分數

主畫面有三種模式：

| 模式 | 內容 |
|---|---|
| **挑戰模式** | 照玩家目前的實力當場生一題，難度落在實力附近。實力會存在裝置上，下次打開接著算 |
| **圖形模式** | 從手拼的具象題（貓、船之類）裡隨機抽，一輪之內不重複 |
| **掃描模式** | 拍一張照片（或從相簿選），辨識出物體的剪影，再用七片拼出最像的幾種給玩家挑 |

玩到一半按「回主畫面」等於暫停：回到同一個模式會接著玩，換別的模式則放棄那一局、不結算。

---

## 跟一般七巧板 App 不一樣的地方

這個專案的重點不在遊戲本身，而是**每一項宣稱都能被驗證**：

1. **判定不會錯。** 七巧板的邊長含有 √2，用小數必然有誤差。這裡所有座標都寫成「整數 ＋ 整數 × √2」的精確形式，
   兩個點重不重合是整數比較，沒有容忍值——不會「拼對了不給過」，也不會「歪了卻算過」。
2. **每一題保證拼得出來。** 出題器先把七片拼好、再描下外框當題目，答案在出題那一刻就存在。
3. **難度是算出來的。** 從題目形狀抽出六個特徵、加權換算成難度分數；同一題旋轉或翻面後重算，難度完全相同（由自動化測試把關）。
4. **難度跟真人表現對得上。** 找了 11 位受試者解 56 題：難度等級越高，平均提示次數一路增加；
   而權重**沒有**拿這批資料調過，所以這是驗證，不是事後擬合。
5. **幾何是對的。** 用本系統手拼出的 13 個凸多邊形，與 1942 年 Wang 與 Hsiung 證明的結果一致。

> 展示版為了讓評審在幾局內就看得到實力變化，實力的變化量設為正式設定的 **10 倍**
> （`Assets/Resources/SkillRatingSettings.json` 的 `demonstrationMultiplier`）。

---

## 技術概要

| | |
|---|---|
| 引擎 | Unity **6000.3.7f1**（C#），URP |
| 平台 | Android 手機／平板、Windows；iPad 打包測試中 |
| 精確幾何 | ℤ[√2] 算術（`Scalar` 型別）；浮點只出現在繪圖與手指拖曳 |
| 重疊判斷 | 分離軸定理（SAT），貼邊與碰角不算重疊 |
| 出題 | 隨機化深度優先回溯搜尋；邊界追蹤取外框；標準形雜湊去除重複題 |
| 難度 | 六個形狀特徵加權，權重放在設定檔 `Assets/Resources/DifficultyWeights.json` |
| 實力 | ELO，依解題時間、提示次數、是否放棄結算 |
| 掃描 | U²-Net（`u2netp`）在裝置上辨識前景剪影，再用 beam search 找最接近的七片拼法 |
| 測試 | NUnit，約 590 個自動化測試；規則層完全不依賴 Unity，可以在 Unity 外用 `dotnet test` 跑 |

規則層（幾何、出題、難度、實力）與畫面層是分開的：

```
Assets/Scripts/
├── Data/          模型層：精確座標、七片、盤面、出題器、難度、ELO、求解器、逼近（不依賴 Unity）
├── Core/          遊戲流程：一關的狀態、復原、計時、存檔、背景工作（不依賴 Unity 場景）
├── GameObject/    畫面層：MonoBehaviour、輸入、UI、相機
├── Recognition/   照片 → 剪影的辨識模組（U²-Net）
└── Editor/        Unity Editor 用的監控面板與工具選單
```

---

## 怎麼開起來

1. 安裝 **Unity 6000.3.7f1**（版本要一模一樣）與 **Git LFS**
2. Clone 之前先跑一次 `git lfs install`，否則圖片與字型會是指標檔
3. 用 Unity Hub 開專案資料夾，開啟 `Assets/Scenes/GameScene.unity`，按 Play

| 場景 | 用途 |
|---|---|
| `GameScene` | 遊戲本體 |
| `FeedBackScene` | 收集受試者資料用的版本（固定題單、不存進度） |
| `ToolScene` | 出題工具：手拼具象題並算出難度（不進正式版） |

在 Editor 裡可以從 **Window → Tangram → 監控面板** 即時看每一題的難度拆解與實力計算過程。
正式 build 要在 Scripting Define Symbols 加上 `TANGRADE_MONITOR`，遊戲畫面左上角才會出現同一份監控面板。

### 跑測試

Unity 內：**Window → General → Test Runner → EditMode → Run All**。

不開 Unity 也可以跑規則層的測試（需要 .NET SDK）：

```bash
dotnet test Tools/DotnetTests
```

---

## 文件

| 位置 | 內容 |
|---|---|
| `繳交文件/` | 競賽報名交出去的系統概述文件與敏捷佐證說明書 |
| `文件/規格/` | 七巧板儲存規格、互動設計、程式說明書 |
| `文件/進度/` | 開發進度、待辦清單、除錯記錄 |
| `文件/對外/` | 企劃書與給協作者的交接文件 |
| `CLAUDE.md` | 開發者交接筆記（給接手的人與 AI 助理，內容很細） |

---

## 使用的第三方元件

- [NativeGallery](https://github.com/yasirkula/UnityNativeGallery)（MIT）——掃描模式從相簿選照片
- [Unity Inference Engine](https://docs.unity3d.com/Packages/com.unity.ai.inference@latest)（`com.unity.ai.inference`）——在裝置上執行辨識模型
- [U²-Net](https://github.com/xuebinqin/U-2-Net)（Apache-2.0）的輕量版 `u2netp`——前景剪影辨識
- [Noto Sans TC](https://fonts.google.com/noto/specimen/Noto+Sans+TC)（SIL Open Font License 1.1，授權全文在 `Assets/Font/OFL.txt`）——介面字型
