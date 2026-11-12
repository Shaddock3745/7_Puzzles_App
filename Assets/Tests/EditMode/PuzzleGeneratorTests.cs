using System.Collections.Generic;
using System.Diagnostics;
using NUnit.Framework;
using Tangram.Data;

namespace Tangram.Data.Tests
{
    /// <summary>
    /// 出題器 第一版：隨便生（issue #24）。
    ///
    /// <b>這一版不管難度</b>，只管「生得出來、不重複、不太醜」。
    /// 照指定難度生是 <c>TargetedPuzzleGenerator</c>。
    /// </summary>
    public class PuzzleGeneratorTests
    {
        /// <summary>把生成器跑到拿滿指定題數為止，順便把四種結果的次數記下來。</summary>
        private static List<GenerationResult> CollectPuzzles(
            PuzzleGenerator generator, int wanted, out Dictionary<GenerationOutcome, int> outcomeCounts,
            int maximumAttempts = 20000)
        {
            var puzzles = new List<GenerationResult>();
            outcomeCounts = new Dictionary<GenerationOutcome, int>();

            for (int attempt = 0; attempt < maximumAttempts && puzzles.Count < wanted; attempt++)
            {
                GenerationResult result = generator.TryGenerate();
                outcomeCounts.TryGetValue(result.Outcome, out int count);
                outcomeCounts[result.Outcome] = count + 1;

                if (result.Outcome == GenerationOutcome.Generated) puzzles.Add(result);
            }
            return puzzles;
        }

        // ─────────────────────────────────────────────────────────────
        // 一、卡片的完成定義
        // ─────────────────────────────────────────────────────────────

        /// <summary>
        /// <b>issue #24 的完成定義之一</b>：生 100 題，每一題的參考解都通過既有的驗證清單
        /// （面積、不重疊、連通、節點數、round-trip）。
        ///
        /// 💡 <b>連通不需要另外保證</b>——每一片都是貼在已放好的片上長出來的。
        /// 這條測試只是把「本來就該成立」釘成會紅的東西。
        /// </summary>
        [Test]
        public void 生成_一百題的參考解全部通過驗證清單()
        {
            var generator = new PuzzleGenerator(seed: 20260910);
            List<GenerationResult> puzzles = CollectPuzzles(generator, 100, out _);

            Assert.AreEqual(100, puzzles.Count, "沒能在上限內生滿 100 題");

            foreach (GenerationResult result in puzzles)
            {
                Validation.Report report = Validation.RunAll(result.ReferenceSolution);
                Assert.IsTrue(report.Ok, report.ToString());
            }
        }

        /// <summary>每題的面積都必須是 32（有向面積 ×2 = 64）。</summary>
        [Test]
        public void 生成_每題的面積都是三十二()
        {
            var generator = new PuzzleGenerator(seed: 7);
            List<GenerationResult> puzzles = CollectPuzzles(generator, 15, out _);

            Assert.AreEqual(15, puzzles.Count);
            foreach (GenerationResult result in puzzles)
                Assert.AreEqual(Scalar.FromInteger(64), result.Puzzle.TotalSignedDoubleArea());
        }

        /// <summary>
        /// <b>完成定義之一</b>：跑一批，重複率與各種失敗原因的比例量得出來。
        ///
        /// ⚠️ 這條**刻意不斷言比例**——那些數字現在還沒有「應該是多少」的根據，
        /// 硬訂一個門檻只會變成之後每次調參都要跟著改的假警報。
        /// 它的工作是<b>把數字印出來</b>，那是批次統計的雛形，也是調審美門檻的依據。
        ///
        /// 唯一斷言的是「四種結果分得出來」——失敗原因合成一個 bool 的話，
        /// 「拼不起來太多」「門檻太嚴」「隨機性不夠散」這三件事就分不出來了。
        /// </summary>
        [Test]
        public void 生成_跑一批之後各種結果的比例量得出來()
        {
            var generator = new PuzzleGenerator(seed: 12345);
            var stopwatch = Stopwatch.StartNew();
            List<GenerationResult> puzzles = CollectPuzzles(generator, 100, out var outcomeCounts);
            stopwatch.Stop();

            int totalAttempts = 0;
            foreach (KeyValuePair<GenerationOutcome, int> entry in outcomeCounts)
                totalAttempts += entry.Value;

            TestContext.WriteLine($"拿到 {puzzles.Count} 題，共叫了 {totalAttempts} 次，" +
                                  $"耗時 {stopwatch.ElapsedMilliseconds} ms" +
                                  $"（平均 {(double)stopwatch.ElapsedMilliseconds / totalAttempts:F1} ms／次）");
            foreach (GenerationOutcome outcome in System.Enum.GetValues(typeof(GenerationOutcome)))
            {
                outcomeCounts.TryGetValue(outcome, out int count);
                TestContext.WriteLine($"  {outcome}：{count} 次（{(double)count / totalAttempts:P1}）");
            }

            Assert.Greater(puzzles.Count, 0, "一題都生不出來");
            Assert.AreEqual(puzzles.Count, generator.DistinctPuzzleCount, "成功的題數應該等於相異形狀數");
        }

        // ─────────────────────────────────────────────────────────────
        // 二、⭐ 題目與參考解的關係
        // ─────────────────────────────────────────────────────────────

        /// <summary>
        /// ⭐ <b>參考解與題目在同一個座標系。</b>
        ///
        /// 拿參考解重新描一次外框，必須跟題目的外框完全對得上（環的 key 含起點座標，
        /// 所以相等代表位置也相等）。
        ///
        /// ⚠️ 這條就是「提示的覆蓋框會不會整組偏移或轉 45°」的守衛。
        /// 存正規化座標的代價就是這件事——題目被轉過去了，參考解沒跟上的話，
        /// 執行時完全不報錯，只是提示畫在錯的地方。
        /// </summary>
        [Test]
        public void 生成_參考解重新描邊會得到同一個外框()
        {
            var generator = new PuzzleGenerator(seed: 99);
            List<GenerationResult> puzzles = CollectPuzzles(generator, 15, out _);

            Assert.AreEqual(15, puzzles.Count);
            foreach (GenerationResult result in puzzles)
            {
                var expected = new List<string>();
                foreach (Walk walk in result.Puzzle.Walks)
                    expected.Add(Canonical.CanonicalStart(walk).ToKey());
                expected.Sort(System.StringComparer.Ordinal);

                var actual = new List<string>();
                foreach (Walk walk in BoundaryTracer.Trace(result.ReferenceSolution))
                    actual.Add(Canonical.CanonicalStart(walk).ToKey());
                actual.Sort(System.StringComparer.Ordinal);

                CollectionAssert.AreEqual(expected, actual, result.Puzzle.Key);
            }
        }

        /// <summary>
        /// ⚠️ <b>參考解的索引必須是槽索引</b>，而且片種要對得上
        /// <see cref="PiecePrototype.TangramSet"/>。
        ///
        /// 放置順序是洗牌過的，所以這條在守「有沒有把片放回正確的槽」。
        /// 放錯的症狀要到載入遊戲才看得出來（片種對不上），在這裡抓比較便宜。
        /// </summary>
        [Test]
        public void 生成_參考解的槽索引與片種對得上()
        {
            var generator = new PuzzleGenerator(seed: 555);
            List<GenerationResult> puzzles = CollectPuzzles(generator, 15, out _);

            Assert.AreEqual(15, puzzles.Count);
            foreach (GenerationResult result in puzzles)
            {
                Assert.AreEqual(PiecePrototype.TangramSet.Length, result.ReferenceSolution.Length);
                for (int slotIndex = 0; slotIndex < PiecePrototype.TangramSet.Length; slotIndex++)
                    Assert.AreEqual(PiecePrototype.TangramSet[slotIndex],
                                    result.ReferenceSolution[slotIndex].Kind,
                                    $"第 {slotIndex} 槽的片種不對");
            }
        }

        /// <summary>題目的 key 必須真的是它自己外框的 key（存的是正規化過的樣子）。</summary>
        [Test]
        public void 生成_題目的key與參考解算出來的一致()
        {
            var generator = new PuzzleGenerator(seed: 4242);
            List<GenerationResult> puzzles = CollectPuzzles(generator, 10, out _);

            Assert.AreEqual(10, puzzles.Count);
            foreach (GenerationResult result in puzzles)
                Assert.AreEqual(result.Puzzle.Key, Canonical.PuzzleKey(result.ReferenceSolution));
        }

        // ─────────────────────────────────────────────────────────────
        // 三、去重、決定性、審美
        // ─────────────────────────────────────────────────────────────

        /// <summary>同一個種子必須生出同一串題目，不然批次統計不可重現。</summary>
        [Test]
        public void 生成_同一個種子生出同一串題目()
        {
            List<GenerationResult> first = CollectPuzzles(new PuzzleGenerator(seed: 2024), 10, out _);
            List<GenerationResult> second = CollectPuzzles(new PuzzleGenerator(seed: 2024), 10, out _);

            Assert.AreEqual(first.Count, second.Count);
            for (int index = 0; index < first.Count; index++)
                Assert.AreEqual(first[index].Puzzle.Key, second[index].Puzzle.Key, $"第 {index} 題");
        }

        /// <summary>回報成功的題目彼此不重複——去重是靠正規化之後的 key。</summary>
        [Test]
        public void 生成_回報成功的題目彼此不重複()
        {
            var generator = new PuzzleGenerator(seed: 31337);
            List<GenerationResult> puzzles = CollectPuzzles(generator, 100, out _);

            var keys = new HashSet<string>();
            foreach (GenerationResult result in puzzles)
                Assert.IsTrue(keys.Add(result.Puzzle.Key), $"重複的 key：{result.Puzzle.Key}");
        }

        /// <summary>回報成功的題目全部通過審美門檻。</summary>
        [Test]
        public void 生成_成功的題目全部通過審美門檻()
        {
            AestheticLimits limits = AestheticLimits.Default;
            var generator = new PuzzleGenerator(seed: 808, limits);
            List<GenerationResult> puzzles = CollectPuzzles(generator, 20, out _);

            Assert.AreEqual(20, puzzles.Count);
            foreach (GenerationResult result in puzzles)
            {
                DifficultyFeatures features = result.Features;
                Assert.LessOrEqual(features.HoleCount, limits.MaximumHoleCount);
                Assert.LessOrEqual(features.PinchPointCount, limits.MaximumPinchPointCount);
                Assert.LessOrEqual(features.ConcaveAngleCount, limits.MaximumConcaveAngleCount);

                Scalar perimeter = DifficultyFeatures.TotalPiecePerimeter - features.DoubledHiddenEdgeLength;
                Assert.IsTrue(perimeter <= limits.MaximumPerimeter, $"周長 {perimeter} 超過上限");
            }
        }

        /// <summary>
        /// 門檻收到極緊就應該什麼都生不出來——證明審美篩選真的有在把關，
        /// 而不是「剛好每一題都過」。
        /// </summary>
        [Test]
        public void 生成_門檻收到極緊時全部被篩掉()
        {
            var impossible = new AestheticLimits(
                maximumHoleCount: 0, maximumPinchPointCount: 0,
                maximumConcaveAngleCount: 0, maximumPerimeter: Scalar.FromInteger(1));

            var generator = new PuzzleGenerator(seed: 616, impossible);

            for (int attempt = 0; attempt < 50; attempt++)
                Assert.AreNotEqual(GenerationOutcome.Generated, generator.TryGenerate().Outcome);
        }

        /// <summary>被審美篩掉時也要帶回特徵——那正是調門檻要看的東西。</summary>
        [Test]
        public void 生成_被審美篩掉時仍然帶回特徵()
        {
            var impossible = new AestheticLimits(
                maximumHoleCount: 0, maximumPinchPointCount: 0,
                maximumConcaveAngleCount: 0, maximumPerimeter: Scalar.FromInteger(1));

            var generator = new PuzzleGenerator(seed: 24, impossible);
            bool sawRejection = false;

            for (int attempt = 0; attempt < 50 && !sawRejection; attempt++)
            {
                GenerationResult result = generator.TryGenerate();
                if (result.Outcome != GenerationOutcome.RejectedByAesthetics) continue;

                sawRejection = true;
                // 周長一定是正的，代表特徵真的算過而不是 default
                Scalar perimeter = DifficultyFeatures.TotalPiecePerimeter - result.Features.DoubledHiddenEdgeLength;
                Assert.Greater(perimeter.Sign, 0);
            }

            Assert.IsTrue(sawRejection, "門檻極緊卻沒有任何一次是被審美篩掉的");
        }

        // ─────────────────────────────────────────────────────────────
        // 三、門檻的判斷（2026-09-11 從生成器搬進 AestheticLimits）
        // ─────────────────────────────────────────────────────────────

        /// <summary>
        /// 周長剛好 44 的特徵。周長 = 七片周長總和（40 + 20√2）− 藏起來的長度 ×2，
        /// 所以藏起來的長度 ×2 要是 −4 + 20√2。
        /// </summary>
        private static readonly Scalar DoubledHiddenForPerimeterFortyFour = new Scalar(-4, 20);

        /// <summary>
        /// 只填門檻會看的四個數字，其餘特徵填「一定過」的值。
        ///
        /// 轉角不參與門檻判斷，填 0 就好；凸包面積 ×2 給 100
        /// （填滿率 = 64 ÷ 100 = 0.64，過得了預設的 0.6 下限）。
        /// ⚠️ 這些測試要驗的是<b>門檻的判斷</b>，不是特徵怎麼量出來的，所以直接組特徵、不跑 Measure。
        /// </summary>
        /// <param name="concaveAngleCount">凹角數。</param>
        /// <param name="doubledHiddenEdgeLength">藏起來的邊長 ×2。</param>
        /// <param name="pinchPointCount">捏點數。</param>
        /// <param name="holeCount">洞數。</param>
        /// <param name="convexHullDoubleArea">
        /// 凸包面積 ×2，預設 100（填滿率 = 64 ÷ 100 = 0.64，過得了預設的 0.6 下限）。
        /// </param>
        /// <returns>可以直接餵給 <c>Violations</c> 的特徵。</returns>
        private static DifficultyFeatures FeaturesForLimits(int concaveAngleCount,
                                                            Scalar doubledHiddenEdgeLength,
                                                            int pinchPointCount, int holeCount,
                                                            long convexHullDoubleArea = 100)
            => new DifficultyFeatures(concaveAngleCount, 8, doubledHiddenEdgeLength,
                                      pinchPointCount, holeCount,
                                      cornerCount: 0,
                                      convexHullDoubleArea: Scalar.FromInteger(convexHullDoubleArea));

        /// <summary>上限是「最多幾個」：<b>剛好等於上限算過</b>，四條都一樣。</summary>
        [Test]
        public void 門檻_剛好等於上限算過()
        {
            var limits = new AestheticLimits(1, 1, 10, Scalar.FromInteger(44));
            DifficultyFeatures features = FeaturesForLimits(10, DoubledHiddenForPerimeterFortyFour, 1, 1);

            Assert.AreEqual(AestheticRules.None, limits.Violations(features));
            Assert.IsTrue(limits.Accepts(features));
        }

        /// <summary>
        /// 每一條各自超過一點點，就只回報那一條。
        /// 縮圖牆要標「被哪一條擋」，所以不能只回答過或不過。
        /// </summary>
        [Test]
        public void 門檻_只超過一條就只回報那一條()
        {
            var limits = new AestheticLimits(1, 1, 10, Scalar.FromInteger(44));
            Scalar hidden = DoubledHiddenForPerimeterFortyFour;
            Scalar oneLonger = hidden - Scalar.One;     // 藏起來的少一點 → 周長多 1

            Assert.AreEqual(AestheticRules.HoleCount,
                limits.Violations(FeaturesForLimits(10, hidden, 1, 2)));
            Assert.AreEqual(AestheticRules.PinchPointCount,
                limits.Violations(FeaturesForLimits(10, hidden, 2, 1)));
            Assert.AreEqual(AestheticRules.ConcaveAngleCount,
                limits.Violations(FeaturesForLimits(11, hidden, 1, 1)));
            Assert.AreEqual(AestheticRules.Perimeter,
                limits.Violations(FeaturesForLimits(10, oneLonger, 1, 1)));
        }

        /// <summary>
        /// ⭐ <b>填滿率門檻在界線上剛好對。</b>（2026-09-12）
        ///
        /// 門檻存的是分數 3/5，比較時兩邊乘開：面積 ×2（64）× 分母 ≥ 分子 × 凸包面積 ×2。
        /// 所以 64 × 5 = 320 ≥ 3 × 凸包 ⇔ <b>凸包 ≤ 106.67</b>，也就是 106 過、107 不過。
        /// ⚠️ 這條在守「不要改成除法」：改成 <c>64.0 / 凸包 >= 0.6</c> 的話界線會飄，
        /// 而且飄的方向取決於浮點誤差——同一個形狀在不同機器上可能一個收一個不收。
        /// </summary>
        [Test]
        public void 門檻_填滿率剛好在界線上()
        {
            var limits = new AestheticLimits(1, 1, 10, Scalar.FromInteger(45),
                                             minimumFillRatioNumerator: 3, minimumFillRatioDenominator: 5);

            DifficultyFeatures justInside = FeaturesForLimits(
                10, DoubledHiddenForPerimeterFortyFour, 1, 1, convexHullDoubleArea: 106);
            DifficultyFeatures justOutside = FeaturesForLimits(
                10, DoubledHiddenForPerimeterFortyFour, 1, 1, convexHullDoubleArea: 107);

            Assert.AreEqual(AestheticRules.None, limits.Violations(justInside), "凸包 106 應該過");
            Assert.AreEqual(AestheticRules.ConvexHullFillRatio, limits.Violations(justOutside),
                "凸包 107 應該只沒過填滿率這一條");
        }

        /// <summary>
        /// 分子 0 = 不設填滿率門檻。縮圖牆與審視鷹架都會開「全開」的門檻，
        /// 那時候這一條必須完全不作用——預設值如果不是 0，它們會安靜地少收一批形狀。
        /// </summary>
        [Test]
        public void 門檻_沒設填滿率時再散的形狀也收()
        {
            var limits = new AestheticLimits(1, 1, 10, Scalar.FromInteger(45));
            DifficultyFeatures verySpread = FeaturesForLimits(
                10, DoubledHiddenForPerimeterFortyFour, 1, 1, convexHullDoubleArea: 10000);

            Assert.AreEqual(AestheticRules.None, limits.Violations(verySpread));
        }

        /// <summary>同時超過好幾條，每一條都要回報，不能只回報第一條就停。</summary>
        [Test]
        public void 門檻_同時超過好幾條全部回報()
        {
            var limits = new AestheticLimits(0, 0, 3, Scalar.FromInteger(30));
            DifficultyFeatures features = FeaturesForLimits(4, DoubledHiddenForPerimeterFortyFour, 1, 1);

            Assert.AreEqual(
                AestheticRules.HoleCount | AestheticRules.PinchPointCount
                    | AestheticRules.ConcaveAngleCount | AestheticRules.Perimeter,
                limits.Violations(features));
            Assert.IsFalse(limits.Accepts(features));
        }

        /// <summary>
        /// ⭐ <b>生成器的判定跟 <see cref="AestheticLimits.Violations"/> 永遠一致。</b>
        ///
        /// 縮圖牆用全開的門檻生形狀、再拿預設門檻問一次「收不收」——那個答案必須跟
        /// 「用預設門檻直接生」時生成器的判定一模一樣，否則牆上標的「被擋掉」跟遊戲裡實際發生的對不上。
        /// 判斷只住在 AestheticLimits 就是為了這件事；這條把它釘住。
        /// </summary>
        [Test]
        public void 門檻_生成器的判定跟AestheticLimits一致()
        {
            AestheticLimits limits = AestheticLimits.Default;
            var generator = new PuzzleGenerator(seed: 11, limits);
            int checkedCount = 0;

            for (int attempt = 0; attempt < 300; attempt++)
            {
                GenerationResult result = generator.TryGenerate();
                if (result.Outcome == GenerationOutcome.PackingFailed) continue;

                checkedCount++;
                bool rejected = result.Outcome == GenerationOutcome.RejectedByAesthetics;
                Assert.AreEqual(rejected, !limits.Accepts(result.Features), $"第 {attempt} 次：{result.Features}");
            }

            Assert.Greater(checkedCount, 0);
        }
    }
}
