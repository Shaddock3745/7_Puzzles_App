using System.Collections.Generic;
using NUnit.Framework;

namespace Tangram.Data.Tests
{
    /// <summary>
    /// ⭐ <b>從外面校準難度那把尺</b>（2026-09-20）：用人手拼的 38 題釘住幾個「改壞了要響」的數字。
    ///
    /// <b>為什麼需要這一支</b>：13 個凸多邊形的分數、它們的難易順序、具象題落在哪個區間，
    /// 原本只寫在 <c>開發進度.md</c> 裡。文件不會響——權重檔改一個數字、或是動到追邊那段程式，
    /// 這些數字會<b>安靜地全部變掉</b>，要等到某天覺得「這題怎麼變簡單了」才發現，而那時已經查不回是哪一次改的。
    ///
    /// <b>五條各自在看什麼</b>（刻意分成五條，而不是一條大的）：
    /// <list type="number">
    /// <item><b>指紋</b>——儲存契約那一級。追邊行為一改，38 個 key 全部變號</item>
    /// <item><b>幾何</b>——不依賴權重。它紅了代表壞的是幾何那層，不是難度那層</item>
    /// <item><b>順序</b>——主力。調權重時分數本來就會變，<b>順序亂跳</b>才是真的出事</item>
    /// <item><b>單一錨點</b>——尺有沒有動</item>
    /// <item><b>兩群題目在同一把尺上</b>——精選題不需要另外手標難度</item>
    /// </list>
    ///
    /// ⚠️ <b>第 4 條刻意只釘一個數字</b>（2026-09-20 使用者定）。13 個全釘絕對分數的話，
    /// 跑一次重算工具就紅 13 條——一次紅十幾條跟沒有警報差不多，訊息會被淹掉。
    /// 只釘一個，紅的時候訊息很乾淨：「尺動了，去確認是不是故意的」。
    ///
    /// 💡 素材與分組規則在 <see cref="CalibrationEntries"/>。整批約 0.4 秒，<b>不掛 <c>[Explicit]</c></b>。
    /// </summary>
    public class ConvexPolygonCalibrationTests
    {
        /// <summary>古典大正方形的難度。2026-09-12 第一次量到，9-14 搬夾住範圍、9-20 重量都還是它。</summary>
        private const int ClassicSquareScore = 2261;

        /// <summary>
        /// 13 個凸多邊形照難度<b>由難到易</b>的順序（2026-09-20 量的）。
        ///
        /// ⚠️ <b>裡面有兩組同分</b>：梯形1 ＝ 平行四邊形1（2286）、六邊形1 ＝ 六邊形3（2214）——
        /// 那兩對的六個特徵<b>完全一樣</b>，難度函數分不出它們。所以下面比的是「不遞增」，不是「嚴格遞減」；
        /// 用嚴格比的話，同分的先後會變成<b>由排序演算法決定的巧合</b>，權重動一點點就左右翻面而紅得莫名其妙。
        /// </summary>
        private static readonly string[] HardestToEasiest =
        {
            "長方形", "梯形2", "五邊形1", "梯形1", "平行四邊形1", "正方形", "六邊形1",
            "六邊形3", "五邊形2", "梯形3", "六邊形2", "三角形", "六邊形4"
        };

        /// <summary>
        /// 一、把七片重新追邊算出來的 <c>puzzleKey</c>，要跟存檔裡的那個一樣。
        ///
        /// <b>這是五條裡最嚴重的一條</b>：<c>puzzleKey</c> 是從 <c>BoundaryTracer</c> 的輸出算的，
        /// 追邊行為一改，<b>資料庫裡所有題目的 key 會同時變號</b>，而 <c>puzzleKey</c> 的指向整批斷掉，不報錯。
        /// 素材被手改過（改了 <c>links</c> 或姿態而沒改 key）也會在這裡響。
        /// </summary>
        [Test]
        public void 指紋_每一題重算的puzzleKey都跟存檔一致()
        {
            IReadOnlyList<PuzzleLibrary.Entry> entries = CalibrationEntries.All();

            Assert.AreEqual(CalibrationEntries.ExpectedEntryCount, entries.Count,
                "素材檔的筆數不對——有人動過 calibration_entries.jsonl，下面每一條都在量別的東西");

            foreach (PuzzleLibrary.Entry entry in entries)
                Assert.AreEqual(entry.Arrangement.PuzzleKey, entry.Puzzle.Key,
                    $"「{entry.Name}」重算的 puzzleKey 跟存檔不一樣。" +
                    "先確認是不是動到了 BoundaryTracer 的追邊行為——那會讓所有題目的 key 一起變號");
        }

        /// <summary>
        /// 二、13 個凸多邊形的轉角數分布是 3 ／ 4×6 ／ 5×2 ／ 6×4。
        ///
        /// ⭐ <b>這是 1942 年就證完的數學事實</b>（Wang &amp; Hsiung：七巧板能拼出的凸多邊形恰好 13 種，
        /// 1 個三角形、6 個四邊形、2 個五邊形、4 個六邊形）。⚠️ <b>它跟權重一點關係都沒有</b>——
        /// 權重怎麼調都不該動到它，所以這一條紅了，要去看的是幾何那層（追邊、共線邊合併、轉角數），不是難度。
        ///
        /// 💡 分組不看名字，看「沒有凹角也沒有洞」，理由在 <see cref="PuzzleLibrary.Entry.IsConvex"/>。
        /// </summary>
        [Test]
        public void 幾何_十三個凸多邊形的轉角數分布對得上文獻()
        {
            IReadOnlyList<PuzzleLibrary.Entry> convexPolygons = CalibrationEntries.ConvexPolygons();

            Assert.AreEqual(CalibrationEntries.ExpectedConvexCount, convexPolygons.Count,
                "凸多邊形的個數不是 13。要嘛素材被動過，要嘛凹角或洞的判定改了");

            var countByCornerCount = new Dictionary<int, int>();
            foreach (PuzzleLibrary.Entry entry in convexPolygons)
            {
                int corners = entry.ShapeFeatures.CornerCount;
                countByCornerCount.TryGetValue(corners, out int soFar);
                countByCornerCount[corners] = soFar + 1;
            }

            Assert.AreEqual(4, countByCornerCount.Count, "轉角數只該出現 3、4、5、6 四種");
            Assert.AreEqual(1, countByCornerCount[3], "三角形應該只有 1 個");
            Assert.AreEqual(6, countByCornerCount[4], "四邊形應該有 6 個");
            Assert.AreEqual(2, countByCornerCount[5], "五邊形應該有 2 個");
            Assert.AreEqual(4, countByCornerCount[6], "六邊形應該有 4 個");
        }

        /// <summary>
        /// 三、13 個凸多邊形的難易順序沒有變。
        ///
        /// <b>調權重時分數本來就會全部變</b>，那不算出事；<b>順序亂跳才算</b>——
        /// 那表示這次改的不只是尺的刻度，而是改變了尺在量什麼。
        /// 同分的兩對見 <see cref="HardestToEasiest"/> 的註解，所以這裡比的是「不遞增」。
        /// </summary>
        [Test]
        public void 尺_十三個凸多邊形的難易順序沒有變()
        {
            DifficultyWeights weights = RepositoryFiles.LoadDifficultyWeights();
            IReadOnlyList<PuzzleLibrary.Entry> convexPolygons = CalibrationEntries.ConvexPolygons();

            Assert.AreEqual(HardestToEasiest.Length, convexPolygons.Count, "期待的順序表跟實際個數對不上");

            int previousScore = int.MaxValue;
            string previousName = "（最難的那個之前）";
            foreach (string name in HardestToEasiest)
            {
                PuzzleLibrary.Entry entry = FindByName(convexPolygons, name);
                int score = entry.Score(weights);

                Assert.LessOrEqual(score, previousScore,
                    $"「{name}」{score} 分，比前一個「{previousName}」{previousScore} 分還難——順序翻過來了。" +
                    "調權重會讓分數全部變，但順序不該亂跳");

                previousScore = score;
                previousName = name;
            }
        }

        /// <summary>
        /// 四、古典大正方形仍然是 2261 分。<b>整支裡唯一釘絕對分數的一條。</b>
        ///
        /// 它是 13 個凸多邊形之一，也是回饋收集的練習題，所以這個數字同時出現在三個地方
        /// （文件、練習題的存檔、這裡）。改權重之後它會紅，<b>那時候要做的是確認尺是故意動的，再把數字更新</b>——
        /// 順便去看 <c>開發進度.md</c> 裡引用它的那幾處。
        ///
        /// ⚠️ <b>2261 是外插出來的，本身沒有「這題有多難」的意義</b>：周長與轉角數都離基準批次五、六個標準差。
        /// 它在這裡的用途是<b>定點</b>，不是難度。
        /// </summary>
        [Test]
        public void 尺_古典大正方形仍然是2261分()
        {
            DifficultyWeights weights = RepositoryFiles.LoadDifficultyWeights();
            PuzzleLibrary.Entry classicSquare = FindByName(CalibrationEntries.ConvexPolygons(), "正方形");

            Assert.AreEqual(ClassicSquareScore, classicSquare.Score(weights),
                "古典大正方形的難度變了＝尺動了。確認是故意的之後，記得同步 開發進度.md 裡引用 2261 的地方");
        }

        /// <summary>
        /// 五、具象題跟生成題用的是<b>同一把尺</b>。
        ///
        /// <b>這一條防的是什麼</b>：精選題如果另外手標難度，或是特徵函數對「有腿有尾巴」的形狀失準，
        /// 症狀會是<b>玩家在生成題一路贏、碰到精選題突然爆難</b>，而且查不出原因
        /// （<c>待辦清單.md</c> E 區〈兩件坑〉第 2 條）。
        ///
        /// 釘兩件事：
        /// <list type="bullet">
        /// <item><b>兩群不重疊</b>：每一個具象題都比最簡單的凸多邊形容易。凸多邊形那一群是尺的外插區，
        /// 具象題不該跑進去</item>
        /// <item><b>大多數落在出題器真的會給的範圍裡</b>（權重檔的 <c>TargetDifficultyRange</c>，現在是 744 ～ 1660）。
        /// ⚠️ 現況是 <b>25 個裡 20 個</b>在範圍內，另外 5 個（房子2、魚2、房子1、數字1、船 1956）比上限高——
        /// 那是可以接受的，它們只是比系統自己出得到的最難題再難一點，<b>不是另一個量級</b></item>
        /// </list>
        /// </summary>
        [Test]
        public void 尺_具象題跟生成題用的是同一把尺()
        {
            DifficultyWeights weights = RepositoryFiles.LoadDifficultyWeights();
            IReadOnlyList<PuzzleLibrary.Entry> figurativeShapes = CalibrationEntries.FigurativeShapes();

            Assert.AreEqual(
                CalibrationEntries.ExpectedEntryCount - CalibrationEntries.ExpectedConvexCount,
                figurativeShapes.Count, "具象題的個數不對");

            int easiestConvexScore = int.MaxValue;
            foreach (PuzzleLibrary.Entry entry in CalibrationEntries.ConvexPolygons())
                easiestConvexScore = System.Math.Min(easiestConvexScore, entry.Score(weights));

            int insideTargetRange = 0;
            foreach (PuzzleLibrary.Entry entry in figurativeShapes)
            {
                int score = entry.Score(weights);

                Assert.Less(score, easiestConvexScore,
                    $"具象題「{entry.Name}」{score} 分，跑進凸多邊形那一群（最低 {easiestConvexScore}）了。" +
                    "那兩群本來是分開的——尺的外插區不該有具象題");

                if (score >= weights.minimumTargetDifficulty && score <= weights.maximumTargetDifficulty)
                    insideTargetRange++;
            }

            Assert.GreaterOrEqual(insideTargetRange, 20,
                $"25 個具象題只有 {insideTargetRange} 個落在出題器的目標範圍 " +
                $"[{weights.minimumTargetDifficulty}, {weights.maximumTargetDifficulty}] 裡（原本 20 個）。" +
                "兩群題目的尺正在分家");
        }

        /// <param name="entries">要找的範圍。</param>
        /// <param name="name">題目的名字。</param>
        /// <returns>那一題。找不到就讓測試失敗（素材被改名了）。</returns>
        private static PuzzleLibrary.Entry FindByName(
            IReadOnlyList<PuzzleLibrary.Entry> entries, string name)
        {
            foreach (PuzzleLibrary.Entry entry in entries)
                if (entry.Name == name) return entry;

            Assert.Fail($"素材裡找不到「{name}」——是不是改名了？");
            return null;
        }
    }
}
