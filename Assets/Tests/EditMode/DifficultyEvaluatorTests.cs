using System;
using System.Collections.Generic;
using System.IO;
using NUnit.Framework;
using Tangram.Data;

namespace Tangram.Data.Tests
{
    /// <summary>
    /// 權重與尺度（P2-6，issue #27）：原始特徵 → 以 1200 為中心的難度值。
    ///
    /// <b>這裡驗的是 issue 的完成定義</b>：
    /// ① 同題同值 ② 難度有高有低 ③（排序與拼法數量同方向移到 P2-11，要有求解器才算得出來）
    /// ④ 不宣稱等於玩家感受——所以這份測試<b>刻意沒有</b>「難度值應該是多少」這種斷言。
    ///
    /// ⚠️ 最關鍵的仍然是不變性：<see cref="SolutionFeatures"/> 的三個數字要對
    /// 「整組解一起做同一個變換」不變，否則同一題會因為存的是哪個代表而拿到不同分數，ELO 直接失去意義。
    /// 形狀那半的不變性在 <c>DifficultyFeatureTests</c>。
    /// </summary>
    public class DifficultyEvaluatorTests
    {
        /// <summary>權重檔在 repo 裡的相對路徑。</summary>
        private const string WeightsRelativePath = "Assets/Resources/DifficultyWeights.json";

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

        /// <summary>
        /// 從目前目錄與執行檔目錄往上找 repo 裡的檔案。
        /// Unity 的 Test Runner 從專案根目錄跑，<c>dotnet test</c> 從 <c>Tools/DotnetTests/bin/...</c> 跑。
        /// </summary>
        /// <param name="relativePath">相對於專案根目錄的路徑。</param>
        /// <returns>完整路徑。</returns>
        private static string FindRepoFile(string relativePath)
        {
            foreach (string start in new[] { Directory.GetCurrentDirectory(), AppContext.BaseDirectory })
                for (var directory = new DirectoryInfo(start); directory != null; directory = directory.Parent)
                {
                    string candidate = Path.Combine(directory.FullName, relativePath);
                    if (File.Exists(candidate)) return candidate;
                }

            Assert.Fail($"從 {Directory.GetCurrentDirectory()} 往上找不到 {relativePath}");
            return null;
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
            foreach (GenerationResult result in Collect(seed: 31337, wanted: 5))
            {
                int first = DifficultyEvaluator.Score(result.Puzzle, result.ReferenceSolution);
                int second = DifficultyEvaluator.Score(result.Puzzle, result.ReferenceSolution);
                Assert.AreEqual(first, second, "同一題算兩次不一樣");

                foreach ((int rotation, bool flipped) in Symmetry.GlobalTransforms)
                {
                    var transformedSolution = new Placement[result.ReferenceSolution.Length];
                    for (int index = 0; index < result.ReferenceSolution.Length; index++)
                        transformedSolution[index] = result.ReferenceSolution[index].ApplyGlobal(rotation, flipped);

                    var transformedPuzzle = Puzzle.FromPlacements(transformedSolution);
                    Assert.AreEqual(first,
                        DifficultyEvaluator.Score(transformedPuzzle, transformedSolution),
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
        /// 而 P2-7「照難度生」與 P2-14 的 ELO 都是建在它有鑑別度這個假設上。
        ///
        /// 門檻抓得很鬆（跨度 200 分 = 一個標準差），因為要的是「沒垮掉」的守衛，不是重現某個數字。
        /// </summary>
        [Test]
        public void 難度_一批題目的分數要有高有低()
        {
            var scores = new List<int>();
            foreach (GenerationResult result in Collect(seed: 20260910, wanted: 60))
                scores.Add(DifficultyEvaluator.Score(result.Puzzle, result.ReferenceSolution));

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
        /// ⭐ <b><c>Assets/Resources/DifficultyWeights.json</c> 與 <see cref="DifficultyWeights.Default"/>
        /// 必須是同一份數字。</b>
        ///
        /// 兩份的理由：<c>Data</c> 層不認識 Unity 的 <c>Resources</c>，<c>dotnet</c> 測試也沒有 Unity，
        /// 所以程式裡留一份；而「換權重是改檔案不是改程式」要成立，檔案又得是給人改的那一份。
        /// ⚠️ 沒有這條測試的話，改了檔案忘了改程式（或反過來）<b>不會有任何症狀</b>——
        /// 遊戲讀檔、測試讀常數，兩邊各自算出不同的難度值。
        /// </summary>
        [Test]
        public void 權重_檔案與程式裡的預設值一致()
        {
            DifficultyWeights fromFile = DifficultyWeights.FromJson(
                File.ReadAllText(FindRepoFile(WeightsRelativePath)));
            DifficultyWeights fromCode = DifficultyWeights.Default;

            Assert.AreEqual(fromCode.center, fromFile.center, 1e-9, "center");
            Assert.AreEqual(fromCode.spread, fromFile.spread, 1e-9, "spread");
            Assert.AreEqual(fromCode.weightedMean, fromFile.weightedMean, 1e-9, "weightedMean");
            Assert.AreEqual(fromCode.weightedDeviation, fromFile.weightedDeviation, 1e-9, "weightedDeviation");
            Assert.AreEqual(fromCode.terms.Length, fromFile.terms.Length, "特徵數量");

            foreach (DifficultyTermWeight expected in fromCode.terms)
            {
                DifficultyTermWeight actual = null;
                foreach (DifficultyTermWeight candidate in fromFile.terms)
                    if (candidate.key == expected.key) actual = candidate;

                Assert.IsNotNull(actual, $"檔案裡沒有特徵 {expected.key}");
                Assert.AreEqual(expected.weight, actual.weight, 1e-9, $"{expected.key} 的權重");
                Assert.AreEqual(expected.direction, actual.direction, $"{expected.key} 的方向");
                Assert.AreEqual(expected.mean, actual.mean, 1e-9, $"{expected.key} 的平均");
                Assert.AreEqual(expected.deviation, actual.deviation, 1e-9, $"{expected.key} 的標準差");
            }
        }

        /// <summary>
        /// 鍵打錯字要丟例外，不能默默當成 0。
        /// 默默當成 0 的話症狀是「某個特徵完全不影響難度」，而且不會有人發現。
        /// </summary>
        [Test]
        public void 權重_不認得的鍵要丟例外()
        {
            DifficultyWeights weights = DifficultyWeights.Default;
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

            DifficultyWeights withZero = DifficultyWeights.Default;
            var terms = new List<DifficultyTermWeight>(withZero.terms);
            terms.Add(new DifficultyTermWeight
            {
                key = DifficultyTermKeys.ForcedPieceCount,
                weight = 0.0, direction = 1, mean = 0.0, deviation = 1.0,
            });
            withZero.terms = terms.ToArray();

            Assert.AreEqual(DifficultyEvaluator.Score(result.Puzzle, result.ReferenceSolution),
                            DifficultyEvaluator.Score(result.Puzzle, result.ReferenceSolution, withZero));
        }

        // ─────────────────────────────────────────────────────────────
        // 四、[Explicit] 產生權重檔
        // ─────────────────────────────────────────────────────────────

        /// <summary>
        /// <b>把 <see cref="DifficultyWeights.Default"/> 寫成 <c>Assets/Resources/DifficultyWeights.json</c>。</b>
        /// 改了程式裡的預設值之後跑這一支，不要手抄——
        /// 〈權重_檔案與程式裡的預設值一致〉會在兩邊分家時變紅，跑這支就是它的修法。
        /// </summary>
        [Test]
        [Explicit("產生權重檔，不是驗證")]
        public void 匯出_把預設權重寫成檔案()
        {
            string path = FindRepoFile(WeightsRelativePath);
            File.WriteAllText(path, DifficultyWeights.Default.ToJson(pretty: true));

            TestContext.WriteLine($"寫好了：{path}");
            Assert.IsTrue(new FileInfo(path).Length > 0, "寫出來是空的");
        }
    }
}
