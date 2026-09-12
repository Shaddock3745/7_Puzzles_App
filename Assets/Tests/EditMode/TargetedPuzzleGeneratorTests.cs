using System;
using System.Diagnostics;
using NUnit.Framework;
using Tangram.Data;

namespace Tangram.Data.Tests
{
    /// <summary>
    /// 出題器 第二版：照難度生（P2-7，issue #28）。
    ///
    /// <b>這裡釘住的是三條路都走得通</b>——正中、將就、失敗。
    /// 前兩條靠固定種子（同一個種子必須生出同一串題目），
    /// 第三條靠一組<b>沒有任何形狀過得了</b>的審美門檻，不必等機率。
    /// </summary>
    public class TargetedPuzzleGeneratorTests
    {
        /// <summary>一組不可能過的門檻：周長下限是 25.66，上限設成 1 的話一題都收不到。</summary>
        private static AestheticLimits ImpossibleLimits =>
            new AestheticLimits(maximumHoleCount: 0, maximumPinchPointCount: 0,
                                maximumConcaveAngleCount: 0, maximumPerimeter: Scalar.FromInteger(1));

        private static TargetedPuzzleGenerator MakeGenerator(int seed, AestheticLimits? limits = null) =>
            new TargetedPuzzleGenerator(new PuzzleGenerator(seed, limits));

        // ─────────────────────────────────────────────────────────────
        // 一、卡片的完成定義
        // ─────────────────────────────────────────────────────────────

        /// <summary>
        /// <b>issue #28 的完成定義之一</b>：給目標難度與容許範圍，回傳的題落在範圍內。
        /// 三個目標各驗一次，因為快的那端與慢的那端走的是同一條路、但次數差很多。
        /// </summary>
        [TestCase(1000)]
        [TestCase(1200)]
        [TestCase(1500)]
        public void 照難度生_正中時難度落在容許範圍裡(int target)
        {
            TargetedGenerationResult result = MakeGenerator(20260912 + target).Generate(target);

            Assert.AreEqual(TargetedGenerationOutcome.OnTarget, result.Outcome,
                $"目標 {target} 在預設預算內沒生到，花了 {result.AttemptCount} 次");
            Assert.IsTrue(Math.Abs(result.Difficulty - target) <= TargetedPuzzleGenerator.DefaultTolerance,
                $"難度 {result.Difficulty} 離目標 {target} 超過 {TargetedPuzzleGenerator.DefaultTolerance}");
            Assert.IsNotNull(result.Puzzle);
            Assert.IsNotNull(result.ReferenceSolution);
            Assert.AreEqual(7, result.ReferenceSolution.Length);
        }

        /// <summary>
        /// <b>issue #28 的完成定義之一</b>：預算用完時回「最接近的」並標明，而且帶著實際難度值。
        ///
        /// 用預算 1 逼出這條路——一次呼叫不可能又生得出來又正中，
        /// 而這個種子的第一次呼叫確實生得出題目，所以結果一定是「將就」。
        /// </summary>
        [Test]
        public void 照難度生_預算用完時回最接近的那一題()
        {
            TargetedGenerationResult result = MakeGenerator(20260912).Generate(1500, attemptBudget: 1);

            Assert.AreEqual(TargetedGenerationOutcome.SettledForNearest, result.Outcome);
            Assert.IsNotNull(result.Puzzle, "將就也要有題目——沒有題目那是失敗，不是將就");
            Assert.IsNotNull(result.ReferenceSolution);
            Assert.AreNotEqual(0, result.Difficulty, "將就時必須帶著實際難度值，ELO 要用它");
            Assert.AreEqual(1, result.AttemptCount);
        }

        /// <summary>
        /// 一題都沒生出來才叫失敗，而且<b>失敗與將就不可以合併</b>：
        /// 將就手上有一題好題目，失敗連題目都沒有，呼叫端對兩者該做的事完全不同。
        /// </summary>
        [Test]
        public void 照難度生_一題都沒生出來時是失敗()
        {
            TargetedGenerationResult result =
                MakeGenerator(20260912, ImpossibleLimits).Generate(1200, attemptBudget: 20);

            Assert.AreEqual(TargetedGenerationOutcome.Failed, result.Outcome);
            Assert.IsNull(result.Puzzle);
            Assert.IsNull(result.ReferenceSolution);
            Assert.AreEqual(0, result.Difficulty);
        }

        // ─────────────────────────────────────────────────────────────
        // 二、六條定案裡容易被之後的人改掉的那幾條
        // ─────────────────────────────────────────────────────────────

        /// <summary>
        /// <b>預算算的是「叫了幾次出題器」，生失敗的那幾次也算。</b>
        /// 只算成功的那幾次，「預算」在慢的那一端就會失控——
        /// 目標 1800 每題要叫 339 次，其中大半是被擋掉的。
        /// 用永遠生不出來的門檻量，次數剛好等於預算才代表每一次都算到了。
        /// </summary>
        [Test]
        public void 照難度生_預算算的是呼叫次數_生失敗也算()
        {
            TargetedGenerationResult result =
                MakeGenerator(20260912, ImpossibleLimits).Generate(1200, attemptBudget: 37);

            Assert.AreEqual(37, result.AttemptCount);
        }

        /// <summary>
        /// 目標值會被夾進生成器實際到得了的範圍，而且<b>夾過的值要回報出來</b>——
        /// 面板要能顯示「你要 1900，實際上是照 1710 找的」。
        /// </summary>
        [Test]
        public void 照難度生_目標值會被夾進範圍()
        {
            Assert.AreEqual(TargetedPuzzleGenerator.MinimumTargetDifficulty,
                            TargetedPuzzleGenerator.ClampTarget(100));
            Assert.AreEqual(TargetedPuzzleGenerator.MaximumTargetDifficulty,
                            TargetedPuzzleGenerator.ClampTarget(9999));
            Assert.AreEqual(1200, TargetedPuzzleGenerator.ClampTarget(1200));

            TargetedGenerationResult result = MakeGenerator(20260912).Generate(9999, attemptBudget: 1);
            Assert.AreEqual(TargetedPuzzleGenerator.MaximumTargetDifficulty, result.TargetDifficulty);
        }

        /// <summary>
        /// ⚠️ <b>回報的難度是「這一題的難度」，不是目標值。</b>
        /// 這條是 ELO 的地基：拿目標值去算的話，實力 1900 的玩家
        /// （目標被夾到 1710）每一場都在對一個不存在的對手算分，而且不報錯。
        /// 驗法是拿回傳的題目與參考解<b>獨立再算一次</b>，兩邊必須一樣。
        /// </summary>
        [Test]
        public void 照難度生_回報的難度重算一次會一樣()
        {
            TargetedGenerationResult result = MakeGenerator(20260912).Generate(1300);

            Assert.AreNotEqual(TargetedGenerationOutcome.Failed, result.Outcome);
            Assert.AreEqual(DifficultyEvaluator.Score(result.Puzzle, result.ReferenceSolution),
                            result.Difficulty);
        }

        /// <summary>
        /// 同一個種子、同一個要求，必須拿到同一題。
        /// 影片要排練，而排練的前提是重現得了。
        /// </summary>
        [Test]
        public void 照難度生_同一個種子拿到同一題()
        {
            TargetedGenerationResult first = MakeGenerator(20260912).Generate(1400);
            TargetedGenerationResult second = MakeGenerator(20260912).Generate(1400);

            Assert.AreEqual(first.Puzzle.Key, second.Puzzle.Key);
            Assert.AreEqual(first.Difficulty, second.Difficulty);
            Assert.AreEqual(first.AttemptCount, second.AttemptCount);
        }

        /// <summary>
        /// 去重是跨呼叫的，所以<b>同一個 <see cref="TargetedPuzzleGenerator"/> 連續要兩題不會給同一題</b>。
        /// Demo 連按兩次「生 1500」要得到兩題不同的。
        /// </summary>
        [Test]
        public void 照難度生_同一個實例連續兩次不會給同一題()
        {
            var generator = MakeGenerator(20260912);

            TargetedGenerationResult first = generator.Generate(1200);
            TargetedGenerationResult second = generator.Generate(1200);

            Assert.AreEqual(TargetedGenerationOutcome.OnTarget, first.Outcome);
            Assert.AreEqual(TargetedGenerationOutcome.OnTarget, second.Outcome);
            Assert.AreNotEqual(first.Puzzle.Key, second.Puzzle.Key);
        }

        // ─────────────────────────────────────────────────────────────
        // 三、量生成時間（完成定義的第三條，掛 [Explicit]）
        // ─────────────────────────────────────────────────────────────

        /// <summary>
        /// <b>issue #28 的完成定義之一</b>：量生成時間，幾個目標難度各生 100 題。
        ///
        /// ⚠️ <b>掛 [Explicit] 是刻意的</b>：十個目標各一百題，最慢那幾格要跑好幾分鐘，
        /// 不進日常的 Run All（跟 <c>PuzzleBatchStatisticsTests</c> 同一個處理）。
        /// 要跑：<c>dotnet test Tools/DotnetTests --filter "FullyQualifiedName~生成時間"</c>
        ///
        /// ⚠️ <b>用 <c>dotnet test</c> 跑出來的是 Debug 建置的數字</b>，那是 Unity Editor 的<b>下界</b>
        /// ——Editor 跑 Mono，通常還要再慢 2 ～ 3 倍。實機接近 Release 建置。
        /// </summary>
        [Test, Explicit("P2-7 生成時間：十個目標各一百題要跑好幾分鐘，不進日常的 Run All")]
        public void 生成時間_幾個目標各生一百題()
        {
            const int PuzzlesPerTarget = 100;
            int[] targets = { 900, 1000, 1100, 1200, 1300, 1400, 1500, 1600, 1700 };

            TestContext.WriteLine("══════ P2-7 生成時間 ══════");
            TestContext.WriteLine($"容許範圍 ±{TargetedPuzzleGenerator.DefaultTolerance}　"
                                  + $"預算 {TargetedPuzzleGenerator.DefaultAttemptBudget} 次　"
                                  + $"每個目標 {PuzzlesPerTarget} 題");
            TestContext.WriteLine("⚠️ 這是 Debug 建置的數字，是 Unity Editor 的下界（Editor 再慢 2～3 倍）");
            TestContext.WriteLine("");
            TestContext.WriteLine("目標　　正中　將就　失敗　每題呼叫數　每題毫秒");

            foreach (int target in targets)
            {
                var generator = MakeGenerator(20260912 + target);
                var stopwatch = Stopwatch.StartNew();
                int onTarget = 0, settled = 0, failed = 0, attempts = 0;

                for (int index = 0; index < PuzzlesPerTarget; index++)
                {
                    TargetedGenerationResult result = generator.Generate(target);
                    attempts += result.AttemptCount;
                    switch (result.Outcome)
                    {
                        case TargetedGenerationOutcome.OnTarget: onTarget++; break;
                        case TargetedGenerationOutcome.SettledForNearest: settled++; break;
                        default: failed++; break;
                    }
                }
                stopwatch.Stop();

                TestContext.WriteLine(
                    $"{target,6}{onTarget,6}{settled,6}{failed,6}"
                    + $"{attempts / (double)PuzzlesPerTarget,12:F1}"
                    + $"{stopwatch.ElapsedMilliseconds / (double)PuzzlesPerTarget,11:F0}");

                // 預設預算下每一格都該生得出來。整格失敗代表預算或夾住的範圍訂錯了，
                // 那不是「慢」而是「做不到」，必須紅
                Assert.AreEqual(0, failed, $"目標 {target} 有 {failed} 題連一個形狀都沒生出來");
            }
        }

        // ─────────────────────────────────────────────────────────────
        // 四、參數
        // ─────────────────────────────────────────────────────────────

        /// <summary>參數壞掉要響，不要默默跑出一個看起來正常的結果。</summary>
        [Test]
        public void 照難度生_參數不合法會丟例外()
        {
            var generator = MakeGenerator(20260912);

            Assert.Throws<ArgumentOutOfRangeException>(() => generator.Generate(1200, tolerance: -1));
            Assert.Throws<ArgumentOutOfRangeException>(() => generator.Generate(1200, attemptBudget: 0));
            Assert.Throws<ArgumentNullException>(() => new TargetedPuzzleGenerator(null));
        }
    }
}
