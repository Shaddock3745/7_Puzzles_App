using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;
using NUnit.Framework;
using Tangram.Data;

namespace Tangram.Data.Tests
{
    /// <summary>
    /// 權重與尺度（issue #27）：原始特徵 → 以 1200 為中心的難度值。
    ///
    /// <b>這裡驗的是 issue 的完成定義</b>：
    /// ① 同題同值 ② 難度有高有低 ③（排序與拼法數量同方向延後，要有求解器才算得出來）
    /// ④ 不宣稱等於玩家感受——所以這份測試<b>刻意沒有</b>「難度值應該是多少」這種斷言。
    ///
    /// ⚠️ 最關鍵的仍然是不變性：<see cref="SolutionFeatures"/> 的三個數字要對
    /// 「整組解一起做同一個變換」不變，否則同一題會因為存的是哪個代表而拿到不同分數，ELO 直接失去意義。
    /// 形狀那半的不變性在 <c>DifficultyFeatureTests</c>。
    /// </summary>
    public class DifficultyEvaluatorTests
    {
        /// <summary>把生成器跑到拿滿指定題數為止。</summary>
        /// <param name="seed">種子。</param>
        /// <param name="wanted">要幾題。</param>
        /// <returns>成功的結果。</returns>
        private static List<GenerationResult> Collect(int seed, int wanted)
        {
            var generator = new PuzzleGenerator(seed);
            var puzzles = new List<GenerationResult>();
            for (int attempt = 0; attempt < 20000 && puzzles.Count < wanted; attempt++)
            {
                GenerationResult result = generator.TryGenerate();
                if (result.Outcome == GenerationOutcome.Generated) puzzles.Add(result);
            }
            Assert.AreEqual(wanted, puzzles.Count, "沒能在上限內生滿");
            return puzzles;
        }

        // ─────────────────────────────────────────────────────────────
        // 一、⭐ ELO 的前提：同一題永遠同一個數字
        // ─────────────────────────────────────────────────────────────

        /// <summary>
        /// ⭐ <b>解的三個特徵對 16 種整體變換全部不變。</b>
        ///
        /// <see cref="Canonical"/> 把互為旋轉／鏡射的題目當成同一題，題庫只存其中一個代表，
        /// 而參考解會跟著題目一起被變換。特徵要是跟著變，同一題就會拿到不同難度。
        ///
        /// ⚠️ 一定要有不對稱的解：古典大正方形太整齊，鏡射寫錯也可能矇混過去。
        /// 所以另外拿三組生成出來的解一起驗。
        /// </summary>
        [Test]
        public void 解的特徵_對十六種整體變換全部不變()
        {
            var solutions = new List<Placement[]> { Fixtures.ClassicSquare() };
            foreach (GenerationResult result in Collect(seed: 20260912, wanted: 3))
                solutions.Add(result.ReferenceSolution);

            foreach (Placement[] solution in solutions)
            {
                SolutionFeatures original = SolutionFeatures.Measure(solution);

                foreach ((int rotation, bool flipped) in Symmetry.GlobalTransforms)
                {
                    var transformed = new Placement[solution.Length];
                    for (int index = 0; index < solution.Length; index++)
                        transformed[index] = solution[index].ApplyGlobal(rotation, flipped);

                    SolutionFeatures actual = SolutionFeatures.Measure(transformed);
                    string label = $"rotation={rotation} flipped={flipped}";

                    Assert.AreEqual(original.KeyPiecesExposingOneVertex,
                                    actual.KeyPiecesExposingOneVertex, $"{label} 只露一個頂點的片數");
                    Assert.AreEqual(original.FullEdgeContactCount,
                                    actual.FullEdgeContactCount, $"{label} 完全接觸");
                    Assert.AreEqual(original.StaggeredEdgeContactCount,
                                    actual.StaggeredEdgeContactCount, $"{label} 錯開的接觸");
                    Assert.AreEqual(original.LargeTriangleUnion,
                                    actual.LargeTriangleUnion, $"{label} 大三角合體");
                }
            }
        }

        /// <summary>
        /// ⭐ <b>同題同值</b>（issue #27 完成定義之一）：同一題算兩次必須完全一樣，
        /// 而且題目轉過去之後也一樣。
        ///
        /// 後半才是真正有內容的：難度值是用<b>存下來的</b>平均與標準差算的，
        /// 哪天有人改成「拿當下這一池現算」，這條就會紅。
        /// </summary>
        [Test]
        public void 難度_同一題永遠得到同一個值()
        {
            DifficultyWeights weights = RepositoryFiles.LoadDifficultyWeights();
            foreach (GenerationResult result in Collect(seed: 31337, wanted: 5))
            {
                int first = DifficultyEvaluator.Score(result.Puzzle, result.ReferenceSolution, weights);
                int second = DifficultyEvaluator.Score(result.Puzzle, result.ReferenceSolution, weights);
                Assert.AreEqual(first, second, "同一題算兩次不一樣");

                foreach ((int rotation, bool flipped) in Symmetry.GlobalTransforms)
                {
                    var transformedSolution = new Placement[result.ReferenceSolution.Length];
                    for (int index = 0; index < result.ReferenceSolution.Length; index++)
                        transformedSolution[index] = result.ReferenceSolution[index].ApplyGlobal(rotation, flipped);

                    var transformedPuzzle = Puzzle.FromPlacements(transformedSolution);
                    Assert.AreEqual(first,
                        DifficultyEvaluator.Score(transformedPuzzle, transformedSolution, weights),
                        $"轉過去之後分數變了：rotation={rotation} flipped={flipped}");
                }
            }
        }

        // ─────────────────────────────────────────────────────────────
        // 二、難度有高有低
        // ─────────────────────────────────────────────────────────────

        /// <summary>
        /// ⭐ <b>難度有高有低</b>（issue #27 完成定義之一）。
        ///
        /// ⚠️ <b>刻意不斷言「應該多難」</b>——那要有真人玩家資料才說得準（完成定義第 ④ 條）。
        /// 這裡只驗尺度有沒有張開：全部擠在一起的話，難度值等於沒有資訊，
        /// 而出題器的「照難度生」與 ELO 都是建在它有鑑別度這個假設上。
        ///
        /// 門檻抓得很鬆（跨度 200 分 = 一個標準差），因為要的是「沒垮掉」的守衛，不是重現某個數字。
        /// </summary>
        [Test]
        public void 難度_一批題目的分數要有高有低()
        {
            DifficultyWeights weights = RepositoryFiles.LoadDifficultyWeights();
            var scores = new List<int>();
            foreach (GenerationResult result in Collect(seed: 20260910, wanted: 60))
                scores.Add(DifficultyEvaluator.Score(result.Puzzle, result.ReferenceSolution, weights));

            scores.Sort();
            int lowest = scores[0], highest = scores[scores.Count - 1];
            var distinct = new HashSet<int>(scores);

            TestContext.WriteLine($"60 題：最低 {lowest}、中位 {scores[scores.Count / 2]}、最高 {highest}、" +
                                  $"相異值 {distinct.Count} 個");

            Assert.Greater(highest - lowest, 200, "60 題的難度跨度不到一個標準差，尺度等於沒張開");
            Assert.Greater(distinct.Count, scores.Count / 2, "太多題拿到同一個分數，分不出先後");
        }

        // ─────────────────────────────────────────────────────────────
        // 三、權重這份資料
        // ─────────────────────────────────────────────────────────────

        /// <summary>
        /// ⭐ <b>權重檔讀得出來，而且通過讀檔時的檢查。</b>
        ///
        /// 2026-09-13 起權重檔是唯一的源頭（程式裡沒有預設值），遊戲讀不到就不進關。
        /// 所以檔案被改壞要在日常的 Run All 就紅，不要等到按 Play 才發現。
        /// 順便釘住尺的中心與間距——那兩個數字是凍結的，改了等於換一把尺。
        /// </summary>
        [Test]
        public void 權重檔_讀得出來而且通過檢查()
        {
            DifficultyWeights weights = RepositoryFiles.LoadDifficultyWeights();

            Assert.AreEqual(1200.0, weights.center, "尺的中心被改了——那是凍結的");
            Assert.AreEqual(200.0, weights.spread, "尺的間距被改了——那是凍結的");
            Assert.Greater(weights.terms.Length, 0);
        }

        /// <summary>
        /// ⭐ <b>權重檔的鍵名跟類別的欄位一一對上。</b>
        ///
        /// ⚠️ <c>JsonUtility</c> 碰到拼錯的欄位名<b>不會吭聲</b>：檔案裡的 <c>"weigth"</c> 被忽略，
        /// 類別裡的 <c>weight</c> 讀成 0——那個特徵就安靜地不算分了。
        /// 讀檔時的檢查攔得住「0 一定是錯」的欄位（倍率、間距、有權重的標準差），
        /// 但<b>權重本身的 0 是合法的</b>，只有比對鍵名攔得住。
        /// </summary>
        [Test]
        public void 權重檔_鍵名跟類別的欄位一一對上()
        {
            string text = File.ReadAllText(RepositoryFiles.Find(RepositoryFiles.DifficultyWeightsRelativePath));
            DifficultyWeights weights = DifficultyWeights.FromJson(text);
            const string label = "權重檔";

            JsonKeyAssertions.EveryKeyIsKnown(text, label, typeof(DifficultyWeights), typeof(DifficultyTermWeight));

            // 反過來：每個欄位都要出現，而且每一筆特徵都要有自己的一份
            JsonKeyAssertions.EveryFieldAppears(text, label, typeof(DifficultyWeights), 1);
            JsonKeyAssertions.EveryFieldAppears(text, label, typeof(DifficultyTermWeight), weights.terms.Length);
        }

        /// <summary>
        /// 讀檔時的檢查：每一種壞法都要丟例外，訊息要說是哪一項。
        /// 從真的權重檔出發，一次只弄壞一個地方——這樣證明的是「就是這一處讓它失敗」。
        /// </summary>
        [Test]
        public void 權重檔_壞掉的內容讀檔時就丟例外()
        {
            string text = File.ReadAllText(RepositoryFiles.Find(RepositoryFiles.DifficultyWeightsRelativePath));

            AssertRejected("", "空的", "空字串");
            AssertRejected(JsonKeyAssertions.ReplaceFirstNumber(text, "weightedDeviation", "0"), "weightedDeviation", "換算倍率是 0");
            AssertRejected(JsonKeyAssertions.ReplaceFirstNumber(text, "spread", "0"), "spread", "間距是 0");

            // 欄位名拼錯 → JsonUtility 讀成 0 → 被「必須大於 0」攔下來。這就是那條檢查存在的理由
            AssertRejected(Regex.Replace(text, "\"spread\"", "\"spreadd\""), "spread", "間距的鍵名拼錯");

            AssertRejected(ReplaceFirstKeyValue(text, "perimeter", "perimeterr"), "perimeterr", "不認得的特徵");
            AssertRejected(ReplaceFirstKeyValue(text, "cornerCount", "perimeter"), "兩次", "同一個特徵出現兩次");
            AssertRejected(JsonKeyAssertions.ReplaceFirstNumber(text, "direction", "2"), "方向", "方向不是 ±1");
            AssertRejected(JsonKeyAssertions.ReplaceFirstNumber(text, "weight", "-0.1"), "負的", "權重是負的");
            AssertRejected(JsonKeyAssertions.ReplaceFirstNumber(text, "deviation", "0"), "標準差", "有權重的特徵標準差是 0");

            // 目標難度範圍（2026-09-14 搬進來）。0 就是「欄位名拼錯被讀成 0」的樣子
            AssertRejected(JsonKeyAssertions.ReplaceFirstNumber(text, "minimumTargetDifficulty", "0"),
                           "minimumTargetDifficulty", "範圍下限是 0");
            AssertRejected(JsonKeyAssertions.ReplaceFirstNumber(text, "maximumTargetDifficulty", "0"),
                           "maximumTargetDifficulty", "範圍上限是 0");
            AssertRejected(JsonKeyAssertions.ReplaceFirstNumber(
                               JsonKeyAssertions.ReplaceFirstNumber(text, "minimumTargetDifficulty", "1660"),
                               "maximumTargetDifficulty", "744"),
                           "maximumTargetDifficulty", "範圍上下限寫反");
        }

        /// <param name="json">要讀的內容。</param>
        /// <param name="expectedInMessage">例外訊息裡必須提到的字（證明訊息有說是哪一項）。</param>
        /// <param name="label">這一種壞法的名字。</param>
        private static void AssertRejected(string json, string expectedInMessage, string label)
        {
            var exception = Assert.Throws<ArgumentException>(() => DifficultyWeights.FromJson(json), label);
            StringAssert.Contains(expectedInMessage, exception.Message, $"{label}：訊息沒有說是哪一項");
        }

        /// <summary>把第一個 <c>"key": "oldKey"</c> 換成 <c>"key": "newKey"</c>。</summary>
        /// <param name="text">JSON 文字。</param>
        /// <param name="oldKey">原本的特徵鍵。</param>
        /// <param name="newKey">換成什麼。</param>
        /// <returns>換過的文字。</returns>
        private static string ReplaceFirstKeyValue(string text, string oldKey, string newKey)
        {
            var pattern = new Regex($"(\"key\"\\s*:\\s*)\"{Regex.Escape(oldKey)}\"");
            Assert.IsTrue(pattern.IsMatch(text), $"權重檔裡找不到特徵「{oldKey}」");
            return pattern.Replace(text, match => match.Groups[1].Value + $"\"{newKey}\"", 1);
        }

        /// <summary>
        /// 權重與題目都是必填（2026-09-13 起）。傳 null 要響——
        /// 原本權重傳 null 會安靜地退回程式裡的預設值，那正是這次拿掉的東西。
        /// </summary>
        [Test]
        public void 難度_權重與題目都是必填()
        {
            DifficultyWeights weights = RepositoryFiles.LoadDifficultyWeights();
            GenerationResult result = Collect(seed: 5, wanted: 1)[0];

            Assert.Throws<ArgumentNullException>(() =>
                DifficultyEvaluator.Score(result.Puzzle, result.ReferenceSolution, null));
            Assert.Throws<ArgumentNullException>(() =>
                DifficultyEvaluator.Score(null, result.ReferenceSolution, weights));
            Assert.Throws<ArgumentNullException>(() =>
                DifficultyEvaluator.Score(result.Puzzle, null, weights));
        }

        /// <summary>
        /// 鍵打錯字要丟例外，不能默默當成 0。
        /// 默默當成 0 的話症狀是「某個特徵完全不影響難度」，而且不會有人發現。
        /// 這一條走的是「在記憶體裡改過的權重」——讀檔那條路已經在讀檔時攔下了。
        /// </summary>
        [Test]
        public void 權重_不認得的鍵要丟例外()
        {
            DifficultyWeights weights = RepositoryFiles.LoadDifficultyWeights();
            weights.terms[0].key = "perimeterr";

            GenerationResult result = Collect(seed: 5, wanted: 1)[0];
            Assert.Throws<InvalidOperationException>(() =>
                DifficultyEvaluator.Score(result.Puzzle, result.ReferenceSolution, weights));
        }

        /// <summary>權重 0 的特徵完全不影響分數——公式裡留著 0 分的欄位是為了之後方便調回來。</summary>
        [Test]
        public void 權重_零權重的特徵不影響分數()
        {
            GenerationResult result = Collect(seed: 808, wanted: 1)[0];

            DifficultyWeights original = RepositoryFiles.LoadDifficultyWeights();
            DifficultyWeights withZero = RepositoryFiles.LoadDifficultyWeights();
            var terms = new List<DifficultyTermWeight>(withZero.terms);
            terms.Add(new DifficultyTermWeight
            {
                key = DifficultyTermKeys.ForcedPieceCount,
                weight = 0.0, direction = 1, mean = 0.0, deviation = 1.0,
            });
            withZero.terms = terms.ToArray();

            Assert.AreEqual(DifficultyEvaluator.Score(result.Puzzle, result.ReferenceSolution, original),
                            DifficultyEvaluator.Score(result.Puzzle, result.ReferenceSolution, withZero));
        }

        // ─────────────────────────────────────────────────────────────
        // 四、[Explicit] 重算工具：權重改過之後重算換算倍率
        // ─────────────────────────────────────────────────────────────

        /// <summary>
        /// 「同一批」的比對容許這麼多的差。<b>是推出來的，不是試出來的</b>：
        ///
        /// ⚠️ 檔案裡的平均與標準差是<b>對照牆的 JS</b> 算的，而縮圖牆匯出把周長<b>四捨五入到小數四位</b>
        /// 才寫進資料——每個值最多差 5e-5，所以平均與標準差最多也差 5e-5。
        /// C# 這邊用的是精確的周長，第一次跑就撞到了：周長的平均差 1.1e-5（2026-09-13）。
        /// 其他特徵都是整數，只剩存到小數第六位的四捨五入。
        ///
        /// <b>這個容許值抓不抓得到「真的換了一批」</b>：845 個裡只要換掉一個形狀，
        /// 整數特徵差 1 就讓平均移動 1 ÷ 845 ≈ 1.2e-3，比容許值大十倍以上。
        /// </summary>
        private const double StoredStatisticTolerance = 1e-4;

        /// <summary>
        /// 新算出來的換算倍率跟檔案差不到這麼多就不寫檔——那只是四捨五入的雜訊。
        /// 權重沒改的時候跑這個工具，應該走到「一致，不寫」那條路。
        ///
        /// 雜訊的來源跟 <see cref="StoredStatisticTolerance"/> 同一個（周長在對照牆資料裡只有小數四位），
        /// 傳到倍率上最多約 6e-6。差 2e-5 對難度的影響不到 0.03 分，四捨五入成整數之後完全看不到。
        /// </summary>
        private const double WeightedDeviationUnchangedTolerance = 2e-5;

        /// <summary>
        /// ⭐ <b>重算工具：改了權重檔的權重之後跑這一支。</b>
        ///
        /// <b>白話</b>：難度 = 1200 + 原始分 × 換算倍率。換算倍率讓基準批次那 845 題攤開在
        /// 大約 800 ～ 1600。權重一改，原始分整體變大或變小，<b>倍率不跟著改的話尺就被拉長或壓扁</b>，
        /// 同一題的難度會變，出題器的夾住範圍、±50、ELO 全部跟著歪，而且不報錯。
        /// 這支拿同一批 845 題、用新權重重算，把倍率調到讓它們又落回原本的範圍。
        ///
        /// <b>五步</b>：
        /// ① 讀權重檔 ② 生<see cref="BaselineBatch">基準批次</see>、篩完必須剛好 845 個
        /// ③ ⚠️ <b>確認是同一批題目</b>：每個特徵在這一批的平均與標準差要跟檔案裡的一樣。
        ///    對不上代表出題器或特徵函數被改過——拿不一樣的題目校正尺，等於默默把整把尺搬走，
        ///    所以<b>直接失敗，什麼都不寫</b>。平均與標準差本身是凍結的，這支只驗、不改
        /// ④ 用新權重算這一批的原始分，量出新的換算倍率（<c>weightedDeviation</c>）
        /// ⑤ ⭐ 在新尺上量<b>出題器的目標難度範圍</b>：這一批 845 題的第 1 與第 99 百分位
        ///    （<c>minimumTargetDifficulty</c> ／ <c>maximumTargetDifficulty</c>，2026-09-14 從程式常數搬進權重檔）
        /// ⑥ <b>只把有變的數字寫回檔案</b>（最多三個），其他文字一個字都不動（用文字取代，不整份重新序列化）
        ///
        /// 「全部兩千個」那一群的百分位照印，但<b>只當參考、不比</b>——出題器只交出篩過的形狀。
        /// 原本兩群都拿去比、差太多就警告，範圍改成量篩過的那一群之後，另一群會永遠亮警告，等於沒有警告。
        ///
        /// ⭐ <b>權重沒改的時候跑，應該算回檔案裡那個值</b>。那個值原本是對照牆的 JS 算的，
        /// C# 算出一樣的數字就證明兩邊的定義一致。
        ///
        /// 要跑：<c>dotnet test Tools/DotnetTests -- "NUnit.Where=name == RescaleDifficultyWeights"</c>（⚠️ 不能用 <c>--filter</c>，它選不到 <c>[Explicit]</c>，見 <c>文件/開發須知.md</c>〈怎麼跑測試〉）
        /// </summary>
        [Test, Explicit("重算工具：生基準批次要數十秒，而且會寫權重檔")]
        public void 尺度_權重改過之後重算換算倍率()
        {
            // ① 讀
            string path = RepositoryFiles.Find(RepositoryFiles.DifficultyWeightsRelativePath);
            string fileText = File.ReadAllText(path, new UTF8Encoding(false));
            DifficultyWeights oldWeights = DifficultyWeights.FromJson(fileText);

            // ② 基準批次
            BaselineBatch batch = BaselineBatch.Generate();
            List<GenerationResult> accepted = batch.AcceptedByDefault();
            Assert.AreEqual(BaselineBatch.ExpectedAcceptedCount, accepted.Count,
                "基準批次篩完的數量不對——出題器或審美門檻被改過，這已經不是當初定尺的那一批。什麼都沒寫。");

            List<MeasuredShape> acceptedShapes = Measure(accepted);

            // ③ 確認是同一批
            var mismatches = new List<string>();
            foreach (DifficultyTermWeight term in oldWeights.terms)
            {
                var values = new List<double>(acceptedShapes.Count);
                foreach (MeasuredShape shape in acceptedShapes)
                    values.Add(DifficultyEvaluator.RawValueOf(term.key, shape.ShapeFeatures,
                                                              shape.ForcedPieceCount, shape.SolutionFeatures));

                (double mean, double deviation) = MeanAndPopulationDeviation(values);
                if (Math.Abs(mean - term.mean) > StoredStatisticTolerance
                    || Math.Abs(deviation - term.deviation) > StoredStatisticTolerance)
                    mismatches.Add($"  {term.key}：檔案 平均 {term.mean}／標準差 {term.deviation}，" +
                                   $"這一批算出來 {mean:F6}／{deviation:F6}");
            }
            if (mismatches.Count > 0)
                Assert.Fail("各特徵的平均與標準差跟檔案對不上——出題器或特徵函數被改過，" +
                            "拿這一批校正會把整把尺搬走。什麼都沒寫。\n" + string.Join("\n", mismatches));

            // ④ 新的換算倍率。平均與標準差用檔案裡凍結的那份（WeightedSum 就是這樣算的）
            var weightedSums = new List<double>(acceptedShapes.Count);
            foreach (MeasuredShape shape in acceptedShapes)
                weightedSums.Add(DifficultyEvaluator.WeightedSum(shape.ShapeFeatures, shape.ForcedPieceCount,
                                                                 shape.SolutionFeatures, oldWeights));
            (double weightedMean, double measuredDeviation) = MeanAndPopulationDeviation(weightedSums);
            double newDeviation = Math.Round(measuredDeviation, 6, MidpointRounding.AwayFromZero);
            bool deviationChanged = Math.Abs(newDeviation - oldWeights.weightedDeviation) > WeightedDeviationUnchangedTolerance;

            // ⚠️ 倍率沒變就沿用檔案裡那個，不換成剛算的：兩者差的是四捨五入的雜訊，
            // 但那一點點可能讓某一題的難度跨過整數，第 ⑤ 步量到的範圍就會被寫動 1 分——權重明明沒改
            DifficultyWeights newWeights = DifficultyWeights.FromJson(fileText);
            if (deviationChanged) newWeights.weightedDeviation = newDeviation;

            // ⑤ 新尺上的目標難度範圍
            (int newMinimum, int newMaximum) = PercentileRange(acceptedShapes, newWeights);
            newWeights.minimumTargetDifficulty = newMinimum;
            newWeights.maximumTargetDifficulty = newMaximum;

            ReportRescale(oldWeights, newWeights, newDeviation, weightedMean, acceptedShapes, Measure(batch.Shapes));

            // ⑥ 寫回。只動有變的數字
            var changes = new List<(string key, string value)>();
            if (deviationChanged)
                changes.Add(("weightedDeviation", newDeviation.ToString("0.######", CultureInfo.InvariantCulture)));
            if (newMinimum != oldWeights.minimumTargetDifficulty)
                changes.Add(("minimumTargetDifficulty", newMinimum.ToString(CultureInfo.InvariantCulture)));
            if (newMaximum != oldWeights.maximumTargetDifficulty)
                changes.Add(("maximumTargetDifficulty", newMaximum.ToString(CultureInfo.InvariantCulture)));

            if (changes.Count == 0)
            {
                TestContext.WriteLine("");
                TestContext.WriteLine("✅ 換算倍率與目標難度範圍都跟檔案一致，不寫檔。");
                return;
            }

            string newText = fileText;
            foreach ((string key, string value) in changes) newText = ReplaceSingleNumber(newText, key, value);

            File.WriteAllText(path, newText, new UTF8Encoding(false));

            // 寫完回頭讀一次，證明真的寫進去了、而且還讀得回來（讀檔的檢查也會再跑一次）
            DifficultyWeights reread = DifficultyWeights.FromJson(File.ReadAllText(path));
            Assert.AreEqual(newWeights.weightedDeviation, reread.weightedDeviation, 1e-12, "換算倍率讀回來不一樣");
            Assert.AreEqual(newMinimum, reread.minimumTargetDifficulty, "範圍下限讀回來不一樣");
            Assert.AreEqual(newMaximum, reread.maximumTargetDifficulty, "範圍上限讀回來不一樣");

            TestContext.WriteLine("");
            TestContext.WriteLine($"✏️ 寫好了：{path}");
            foreach ((string key, string value) in changes) TestContext.WriteLine($"   {key} → {value}");
            TestContext.WriteLine("   接下來：① 切回 Unity 讓它重新匯入 ② 重跑縮圖牆匯出、重發布對照牆 " +
                                  "③ 範圍有變的話，重跑 TargetedPuzzleGeneratorTests.生成時間_幾個目標各生一百題");
        }

        /// <summary>把檔案裡 <c>"key": 數字</c> 的數字換掉。<b>那個鍵必須剛好出現一次</b>，否則不敢改。</summary>
        /// <param name="text">權重檔的文字。</param>
        /// <param name="key">鍵名。</param>
        /// <param name="value">新的值（原樣寫進去）。</param>
        /// <returns>換過的文字。</returns>
        private static string ReplaceSingleNumber(string text, string key, string value)
        {
            var pattern = new Regex($"(\"{Regex.Escape(key)}\"\\s*:\\s*)-?[0-9.eE+-]+");
            Assert.AreEqual(1, pattern.Matches(text).Count, $"權重檔裡的 {key} 不是剛好一個，不敢改");
            return pattern.Replace(text, match => match.Groups[1].Value + value, 1);
        }

        /// <summary>一個形狀算難度要的三樣東西，量一次就好（被外框逼出來的片數不便宜）。</summary>
        private readonly struct MeasuredShape
        {
            public readonly DifficultyFeatures ShapeFeatures;
            public readonly int ForcedPieceCount;
            public readonly SolutionFeatures SolutionFeatures;

            public MeasuredShape(GenerationResult shape)
            {
                ShapeFeatures = shape.Features;
                ForcedPieceCount = DifficultyFeatures.ForcedPieceCountOf(shape.Puzzle.Walks);
                SolutionFeatures = SolutionFeatures.Measure(shape.ReferenceSolution);
            }
        }

        /// <param name="shapes">生成器的結果。</param>
        /// <returns>每個形狀量好的特徵，順序不變。</returns>
        private static List<MeasuredShape> Measure(List<GenerationResult> shapes)
        {
            var measured = new List<MeasuredShape>(shapes.Count);
            foreach (GenerationResult shape in shapes) measured.Add(new MeasuredShape(shape));
            return measured;
        }

        /// <summary>
        /// 平均與<b>母體</b>標準差（除以 n）——跟對照牆 JS 的 <c>meanAndDeviation</c> 同一個定義，
        /// 檔案裡的數字就是那樣算出來的。改成樣本標準差（除以 n − 1）的話，第 ③ 步永遠對不上。
        /// </summary>
        /// <param name="values">數字。</param>
        /// <returns>平均與標準差。</returns>
        private static (double mean, double deviation) MeanAndPopulationDeviation(List<double> values)
        {
            double sum = 0.0;
            foreach (double value in values) sum += value;
            double mean = sum / values.Count;

            double squaredSum = 0.0;
            foreach (double value in values) squaredSum += (value - mean) * (value - mean);
            return (mean, Math.Sqrt(squaredSum / values.Count));
        }

        /// <summary>印出重算的摘要：倍率變多少、尺被拉了多少、五題的新舊難度、目標難度範圍變多少。</summary>
        /// <param name="oldWeights">檔案裡原本的。</param>
        /// <param name="newWeights">換上新換算倍率與新範圍的（倍率沒變時沿用檔案的）。</param>
        /// <param name="measuredDeviation">剛算出來的換算倍率（四捨五入到小數第六位）。</param>
        /// <param name="weightedMean">這一批原始分的平均（理論上是 0）。</param>
        /// <param name="acceptedShapes">基準批次篩過的那 845 個。</param>
        /// <param name="allShapes">基準批次全部 2000 個。</param>
        private static void ReportRescale(DifficultyWeights oldWeights, DifficultyWeights newWeights,
                                          double measuredDeviation, double weightedMean,
                                          List<MeasuredShape> acceptedShapes, List<MeasuredShape> allShapes)
        {
            TestContext.WriteLine("══════ 重算換算倍率 ══════");
            TestContext.WriteLine($"基準批次：種子 {BaselineBatch.Seed}，{allShapes.Count} 個形狀，預設門檻收 {acceptedShapes.Count} 個");
            TestContext.WriteLine("✅ 各特徵的平均與標準差跟檔案一致（是同一批題目）");
            TestContext.WriteLine($"原始分的平均 {weightedMean:F6}（理論上是 0，檔案裡存 {oldWeights.weightedMean}）");
            TestContext.WriteLine($"換算倍率 weightedDeviation：檔案 {oldWeights.weightedDeviation} → 算出來 {measuredDeviation}");
            TestContext.WriteLine($"沒重算的話，每題離 1200 的距離會是正確值的 " +
                                  $"{newWeights.weightedDeviation / oldWeights.weightedDeviation:F3} 倍");

            // 五題：照新難度排，挑最低、四分之一、中位、四分之三、最高
            var byNewDifficulty = new List<(int oldDifficulty, int newDifficulty)>(acceptedShapes.Count);
            foreach (MeasuredShape shape in acceptedShapes)
                byNewDifficulty.Add((ScoreOf(shape, oldWeights), ScoreOf(shape, newWeights)));
            byNewDifficulty.Sort((left, right) => left.newDifficulty.CompareTo(right.newDifficulty));

            TestContext.WriteLine("五題的難度（舊倍率 → 新倍率）：");
            foreach (double fraction in new[] { 0.0, 0.25, 0.5, 0.75, 1.0 })
            {
                (int oldDifficulty, int newDifficulty) = byNewDifficulty[(int)Math.Round(fraction * (byNewDifficulty.Count - 1))];
                TestContext.WriteLine($"  第 {fraction * 100,3:F0} 百分位：{oldDifficulty} → {newDifficulty}");
            }

            // 目標難度範圍
            bool rangeUnchanged = newWeights.minimumTargetDifficulty == oldWeights.minimumTargetDifficulty
                               && newWeights.maximumTargetDifficulty == oldWeights.maximumTargetDifficulty;
            TestContext.WriteLine($"目標難度範圍（出題器夾目標用）：檔案 {oldWeights.minimumTargetDifficulty} ～ " +
                                  $"{oldWeights.maximumTargetDifficulty} → 新尺上 {newWeights.minimumTargetDifficulty} ～ " +
                                  $"{newWeights.maximumTargetDifficulty}" + (rangeUnchanged ? "　✅ 一致" : "　✏️ 會寫回"));
            TestContext.WriteLine($"  取法：預設門檻收的 {acceptedShapes.Count} 個，第 1 與第 99 百分位（最近排名法）");

            (int allMinimum, int allMaximum) = PercentileRange(allShapes, newWeights);
            TestContext.WriteLine($"  參考，不比：全部 {allShapes.Count} 個是 {allMinimum} ～ {allMaximum}" +
                                  "（出題器只交出篩過的形狀，所以範圍不用這一群）");
        }

        /// <summary>一群形狀在某把尺上的第 1 與第 99 百分位。</summary>
        /// <param name="shapes">形狀。</param>
        /// <param name="weights">用哪一份權重算。</param>
        /// <returns>第 1 與第 99 百分位的難度。</returns>
        private static (int lowest, int highest) PercentileRange(List<MeasuredShape> shapes, DifficultyWeights weights)
        {
            var difficulties = new List<int>(shapes.Count);
            foreach (MeasuredShape shape in shapes) difficulties.Add(ScoreOf(shape, weights));
            difficulties.Sort();

            // 最近排名法：第 p 百分位 = 排序後第 round(p × (n − 1)) 個
            return (difficulties[(int)Math.Round(0.01 * (difficulties.Count - 1))],
                    difficulties[(int)Math.Round(0.99 * (difficulties.Count - 1))]);
        }

        /// <param name="shape">量好的形狀。</param>
        /// <param name="weights">權重。</param>
        /// <returns>難度值。</returns>
        private static int ScoreOf(in MeasuredShape shape, DifficultyWeights weights) =>
            DifficultyEvaluator.Score(shape.ShapeFeatures, shape.ForcedPieceCount, shape.SolutionFeatures, weights);
    }
}
