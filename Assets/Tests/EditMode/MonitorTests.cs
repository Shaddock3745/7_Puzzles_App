using System;
using System.Collections.Generic;
using NUnit.Framework;
using Tangram.Data;
using Tangram.Data.Tests;

namespace Tangram.Core.Tests
{
    /// <summary>
    /// 監控面板的資料那半（P2-15，issue #32）：難度拆解、出題紀錄、結算明細的輸入、走勢、監控報告。
    ///
    /// ⚠️ <b>視窗本身不在這裡</b>——它在 Editor 資料夾，<c>dotnet</c> 編不到。
    /// 所以「會算錯的東西」全放在資料那半，這裡驗完，視窗只剩排版。
    /// </summary>
    public class MonitorTests
    {
        /// <summary>跟 <c>SkillRatingTests</c> 同一組（r 0.5、T 90、P 0、K 20 × 10）。</summary>
        /// <returns>一份新的設定。</returns>
        private static SkillRatingSettings AgreedSettings() => new SkillRatingSettings
        {
            initialRating = 900.0,
            ratingScale = 400.0,
            dragWeightRatio = 0.5,
            referenceSeconds = 90.0,
            hintPenaltySeconds = 0.0,
            baseKFactor = 20.0,
            demonstrationMultiplier = 10.0,
        };

        /// <summary>跟 <c>SkillRatingTests</c> 同一個範圍，刻意不讀權重檔（理由見那邊）。</summary>
        /// <returns>744 ～ 1660。</returns>
        private static TargetDifficultyRange TestRange() => new TargetDifficultyRange(744, 1660);

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

        /// <param name="session">要換關的那一場。</param>
        /// <param name="difficulty">這一題的難度。</param>
        /// <param name="generation">出題紀錄。</param>
        private static void LoadClassicSquare(LevelSession session, int difficulty = 1200,
                                              GenerationRecord? generation = null)
        {
            Placement[] solution = Fixtures.ClassicSquare();
            session.Load(Puzzle.FromPlacements(solution), solution, difficulty, allowRoll: true, generation);
        }

        /// <summary>七片照參考解擺上去，不停計時。</summary>
        /// <param name="level">這一關。</param>
        private static void PlaceAllPieces(LevelData level)
        {
            for (int slotIndex = 0; slotIndex < BoardState.PieceCount; slotIndex++)
                level.Board.Place(slotIndex, level.ReferenceSolution[slotIndex]);
        }

        /// <summary>照正常流程過關：開始計時、擺滿、判過關、停計時。</summary>
        /// <param name="level">這一關。</param>
        /// <param name="foregroundSeconds">花了幾秒。</param>
        private static void SolveIn(LevelData level, double foregroundSeconds)
        {
            level.StartForegroundTiming(0.0);
            PlaceAllPieces(level);
            Assert.IsTrue(level.MarkSolvedIfAllOnBoard());
            level.StopAllTiming(foregroundSeconds);
        }

        // ─────────────────────────────────────────────────────────────
        // 一、難度拆解
        // ─────────────────────────────────────────────────────────────

        /// <summary>
        /// ⭐ <b>拆解加起來跟原本的加權和完全相等</b>——不是差不多。
        ///
        /// 拆解刻意沒有動 <see cref="DifficultyEvaluator.WeightedSum"/>（尺已經凍結），而是另外算每一項。
        /// 兩邊的算式只要有一邊改了先後順序，浮點數最後一位就可能不同，面板上的「各項加總」就會跟真的分數差一點點。
        /// 經典大正方形之外再拿三十題生成的，一定要有不對稱的。
        /// </summary>
        [Test]
        public void 拆解_各項加總跟原本的加權和完全相等()
        {
            DifficultyWeights weights = RepositoryFiles.LoadDifficultyWeights();

            var solutions = new List<Placement[]> { Fixtures.ClassicSquare() };
            foreach (GenerationResult result in Collect(seed: 20260914, wanted: 30))
                solutions.Add(result.ReferenceSolution);

            foreach (Placement[] solution in solutions)
            {
                Puzzle puzzle = Puzzle.FromPlacements(solution);
                DifficultyBreakdown breakdown = DifficultyBreakdown.Measure(puzzle, solution, weights);

                double total = 0.0;
                foreach (DifficultyTermContribution term in breakdown.Terms)
                    total += term.WeightedTerm;

                double expected = DifficultyEvaluator.WeightedSum(
                    DifficultyFeatures.Measure(puzzle.Walks), DifficultyFeatures.ForcedPieceCountOf(puzzle.Walks),
                    SolutionFeatures.Measure(solution), weights);

                Assert.IsTrue(total == expected, $"各項加總 {total:R} ≠ 加權和 {expected:R}");
                Assert.AreEqual(expected, breakdown.WeightedSum);
                Assert.AreEqual(DifficultyEvaluator.Score(puzzle, solution, weights), breakdown.Score);
            }
        }

        /// <summary>
        /// ⭐ <b>重量的特徵跟出題器帶回來的一樣</b>。出題器用的是 <see cref="GenerationResult.Features"/>，
        /// 拆解從題目重量。這條破了，就是面板上的對帳會亮的那種情況——在這裡先抓到。
        /// </summary>
        [Test]
        public void 拆解_從題目重量跟出題器帶回來的特徵算出同一個分數()
        {
            DifficultyWeights weights = RepositoryFiles.LoadDifficultyWeights();

            foreach (GenerationResult result in Collect(seed: 20260915, wanted: 30))
            {
                int generatorScore = DifficultyEvaluator.Score(
                    result.Features, DifficultyFeatures.ForcedPieceCountOf(result.Puzzle.Walks),
                    SolutionFeatures.Measure(result.ReferenceSolution), weights);

                DifficultyBreakdown breakdown = DifficultyBreakdown.Measure(result.Puzzle, result.ReferenceSolution, weights);
                Assert.IsTrue(breakdown.Matches(generatorScore), $"出題器 {generatorScore}、拆解 {breakdown.Score}");
            }
        }

        /// <summary>中心 ＋ 校正量 ＋ 每一列的分數 ＝ 還沒四捨五入的難度。這是面板上「1200 ＋ 66 − 40 …」那一行。</summary>
        [Test]
        public void 拆解_中心加校正加各項分數等於難度()
        {
            DifficultyWeights weights = RepositoryFiles.LoadDifficultyWeights();

            foreach (GenerationResult result in Collect(seed: 20260916, wanted: 10))
            {
                DifficultyBreakdown breakdown = DifficultyBreakdown.Measure(result.Puzzle, result.ReferenceSolution, weights);

                double unrounded = breakdown.Center + breakdown.CenterOffsetPoints;
                foreach (DifficultyTermContribution term in breakdown.Terms) unrounded += term.Points;

                double expected = weights.center
                    + weights.spread * (breakdown.WeightedSum - weights.weightedMean) / weights.weightedDeviation;
                Assert.AreEqual(expected, unrounded, 1e-9);
                Assert.AreEqual(breakdown.Score, (int)Math.Round(unrounded, MidpointRounding.AwayFromZero));
            }
        }

        /// <summary>權重 0 的特徵照樣列出來、分數 0——「有量但不算分」本身就是評審可能問的事。</summary>
        [Test]
        public void 拆解_權重0的特徵照樣列出來而且不算分()
        {
            DifficultyWeights weights = RepositoryFiles.LoadDifficultyWeights();
            int zeroedIndex = Array.FindIndex(weights.terms, term => term.weight != 0.0);
            Assert.GreaterOrEqual(zeroedIndex, 0, "權重檔裡應該至少有一個算分的特徵");
            weights.terms[zeroedIndex].weight = 0.0;

            Placement[] solution = Fixtures.ClassicSquare();
            DifficultyBreakdown breakdown = DifficultyBreakdown.Measure(Puzzle.FromPlacements(solution), solution, weights);

            Assert.AreEqual(weights.terms.Length, breakdown.Terms.Count, "每個特徵都要有一列");
            DifficultyTermContribution zeroed = breakdown.Terms[zeroedIndex];
            Assert.AreEqual(weights.terms[zeroedIndex].key, zeroed.Key, "順序要跟權重檔一樣");
            Assert.AreEqual(0.0, zeroed.WeightedTerm);
            Assert.AreEqual(0.0, zeroed.Points);
        }

        /// <summary>對帳比整數，差 1 分就是對不上。</summary>
        [Test]
        public void 拆解_對帳差一分就算對不上()
        {
            DifficultyWeights weights = RepositoryFiles.LoadDifficultyWeights();
            Placement[] solution = Fixtures.ClassicSquare();
            DifficultyBreakdown breakdown = DifficultyBreakdown.Measure(Puzzle.FromPlacements(solution), solution, weights);

            Assert.IsTrue(breakdown.Matches(breakdown.Score));
            Assert.IsFalse(breakdown.Matches(breakdown.Score + 1));
            Assert.AreEqual(weights.Fingerprint(), breakdown.WeightsFingerprint);
        }

        /// <summary>
        /// ⭐ <b>整條路</b>：照難度生 → 參考解平移到盤面座標 → 進關 → 監控報告的對帳過得了。
        /// 跟遊戲裡 <c>LevelController.EnterNextLevel</c> 走的是同一條。
        /// </summary>
        [Test]
        public void 拆解_照難度生進關之後對帳過得了()
        {
            DifficultyWeights weights = RepositoryFiles.LoadDifficultyWeights();
            var generator = new TargetedPuzzleGenerator(new PuzzleGenerator(20260910, AestheticLimits.Default), weights);
            var session = new LevelSession();
            var monitor = new LevelMonitor();

            foreach (int target in new[] { 900, 1200, 1400 })
            {
                TargetedGenerationResult result = generator.Generate(target);
                Assert.AreNotEqual(TargetedGenerationOutcome.Failed, result.Outcome);

                session.Load(result.Puzzle, result.ReferenceSolution, result.Difficulty, allowRoll: false,
                             GenerationRecord.From(result));
                LevelMonitorReport report = monitor.Build(session, weights, 0.0);

                Assert.AreEqual(true, report.BreakdownMatchesDifficulty,
                                $"目標 {target}：存著 {result.Difficulty}、拆解 {report.Breakdown?.Score}");
            }
        }

        // ─────────────────────────────────────────────────────────────
        // 二、出題紀錄
        // ─────────────────────────────────────────────────────────────

        /// <summary>三種下場都照抄；失敗時拿到的難度是 0。</summary>
        [TestCase(TargetedGenerationOutcome.OnTarget, 1230, 17)]
        [TestCase(TargetedGenerationOutcome.SettledForNearest, 1610, 200)]
        [TestCase(TargetedGenerationOutcome.Failed, 0, 200)]
        public void 出題紀錄_三種下場都抄得對(TargetedGenerationOutcome outcome, int difficulty, int attempts)
        {
            var result = new TargetedGenerationResult(outcome, null, null, difficulty, 1200, attempts);
            GenerationRecord record = GenerationRecord.From(result);

            Assert.AreEqual(outcome, record.Outcome);
            Assert.AreEqual(1200, record.TargetDifficulty);
            Assert.AreEqual(difficulty, record.GeneratedDifficulty);
            Assert.AreEqual(attempts, record.AttemptCount);
        }

        /// <summary>給了就有、沒給就沒有；換關之後上一題的不會留下來。</summary>
        [Test]
        public void 出題紀錄_給了有沒給沒有換關不殘留()
        {
            var session = new LevelSession();
            var record = new GenerationRecord(TargetedGenerationOutcome.OnTarget, 1200, 1215, 9);

            LoadClassicSquare(session, generation: record);
            Assert.IsTrue(session.Data.Generation.HasValue);
            Assert.AreEqual(1215, session.Data.Generation.Value.GeneratedDifficulty);

            LoadClassicSquare(session);
            Assert.IsFalse(session.Data.Generation.HasValue, "換成沒給紀錄的一關，上一題的紀錄不該留下來");
        }

        // ─────────────────────────────────────────────────────────────
        // 三、結算明細帶著輸入
        // ─────────────────────────────────────────────────────────────

        /// <summary>
        /// ⭐ <b>只用明細裡的數字就重算得出 C</b>——走勢裡的舊局 <c>LevelData</c> 已經換掉了，
        /// 「那一局的 C 為什麼是這個數」只能靠明細自己回答。
        /// </summary>
        [Test]
        public void 明細_只用明細裡的數字就重算得出時間代價()
        {
            SkillRatingSettings settings = AgreedSettings();
            settings.hintPenaltySeconds = 30.0;

            RatingUpdate update = SkillRating.Settle(1100.0, 1250, LevelOutcome.Solved, 95.0, 41.0, 2, settings, TestRange());

            Assert.AreEqual(95.0, update.ForegroundSeconds);
            Assert.AreEqual(41.0, update.DragSeconds);
            Assert.AreEqual(2, update.HintsUsed);

            double recomputed = update.ForegroundSeconds
                                - (1.0 - settings.dragWeightRatio) * update.DragSeconds
                                + update.HintsUsed * settings.hintPenaltySeconds;
            Assert.AreEqual(update.TimeCostSeconds, recomputed, 1e-12);
        }

        // ─────────────────────────────────────────────────────────────
        // 四、走勢
        // ─────────────────────────────────────────────────────────────

        /// <summary>還在玩、沒題目、計時還在跑——守衛擋下的都不加。</summary>
        [Test]
        public void 走勢_沒結算的不加()
        {
            var session = new LevelSession { Rating = new SkillRatingTracker(AgreedSettings(), 1200.0) };
            LoadClassicSquare(session);
            LevelData level = session.Data;

            level.StartForegroundTiming(0.0);
            session.Rating.TrySettle(level, TestRange(), out _);
            Assert.AreEqual(0, session.Rating.History.Count, "還在玩");

            PlaceAllPieces(level);
            level.MarkSolvedIfAllOnBoard();
            session.Rating.TrySettle(level, TestRange(), out _);
            Assert.AreEqual(0, session.Rating.History.Count, "計時還在跑");
        }

        /// <summary>每幀都在叫，同一局只能有一筆。</summary>
        [Test]
        public void 走勢_同一局叫很多次只加一筆()
        {
            var session = new LevelSession { Rating = new SkillRatingTracker(AgreedSettings(), 1200.0) };
            LoadClassicSquare(session);
            session.Data.StartForegroundTiming(0.0);
            session.Data.MarkAbandoned(40.0);

            for (int frame = 0; frame < 10; frame++) session.Rating.TrySettle(session.Data, TestRange(), out _);

            Assert.AreEqual(1, session.Rating.History.Count);
        }

        /// <summary>
        /// ⭐ <b>兩條不變式</b>：過關與放棄交錯玩五局，
        /// ① 每一筆的「結算前」＝ 前一筆的「結算後」，第一筆的 ＝ 起始實力
        /// ② 最後一筆的「結算後」＝ 目前實力
        /// 比的是 double 完全相等——走勢跟實力是同一刻寫的，不該有任何誤差。
        /// </summary>
        [Test]
        public void 走勢_五局前後接得起來而且接到目前實力()
        {
            var session = new LevelSession { Rating = new SkillRatingTracker(AgreedSettings(), 900.0) };
            int[] difficulties = { 900, 1050, 980, 1200, 1130 };

            for (int i = 0; i < difficulties.Length; i++)
            {
                LoadClassicSquare(session, difficulties[i]);
                if (i % 2 == 0) SolveIn(session.Data, 30.0 + 20.0 * i);
                else
                {
                    session.Data.StartForegroundTiming(0.0);
                    session.Data.MarkAbandoned(60.0);
                }
                Assert.IsTrue(session.Rating.TrySettle(session.Data, TestRange(), out _), $"第 {i + 1} 局應該要結算");
            }

            IReadOnlyList<SettlementRecord> history = session.Rating.History;
            Assert.AreEqual(difficulties.Length, history.Count);

            double previousAfter = session.Rating.StartingRating;
            for (int i = 0; i < history.Count; i++)
            {
                Assert.AreEqual(i + 1, history[i].SequenceNumber);
                Assert.AreEqual(difficulties[i], history[i].Update.Difficulty);
                Assert.IsTrue(history[i].Update.RatingBefore == previousAfter, $"第 {i + 1} 筆接不上前一筆");
                previousAfter = history[i].Update.RatingAfter;
            }
            Assert.IsTrue(previousAfter == session.Rating.CurrentRating, "最後一筆接不到目前實力");
        }

        /// <summary>走勢帶著那一局的出題紀錄——換關之後 <c>LevelData</c> 沒了，紀錄還在。</summary>
        [Test]
        public void 走勢_帶著那一局的出題紀錄()
        {
            var session = new LevelSession { Rating = new SkillRatingTracker(AgreedSettings(), 1200.0) };
            var record = new GenerationRecord(TargetedGenerationOutcome.SettledForNearest, 1700, 1610, 200);

            LoadClassicSquare(session, 1610, record);
            SolveIn(session.Data, 50.0);
            session.Rating.TrySettle(session.Data, TestRange(), out _);

            LoadClassicSquare(session);   // 換關，而且新的一關沒給紀錄
            SolveIn(session.Data, 50.0);
            session.Rating.TrySettle(session.Data, TestRange(), out _);

            Assert.AreEqual(TargetedGenerationOutcome.SettledForNearest, session.Rating.History[0].Generation.Value.Outcome);
            Assert.AreEqual(1610, session.Rating.History[0].Generation.Value.GeneratedDifficulty);
            Assert.IsFalse(session.Rating.History[1].Generation.HasValue);
        }

        /// <summary>外面拿到的走勢改不動，轉型成可寫的清單也不行。</summary>
        [Test]
        public void 走勢_外面改不動()
        {
            var tracker = new SkillRatingTracker(AgreedSettings(), 1200.0);
            var asList = tracker.History as IList<SettlementRecord>;

            Assert.IsNotNull(asList);
            Assert.IsTrue(asList.IsReadOnly);
            Assert.Throws<NotSupportedException>(() =>
                asList.Add(new SettlementRecord(1, null, default)));
        }

        // ─────────────────────────────────────────────────────────────
        // 五、監控報告
        // ─────────────────────────────────────────────────────────────

        /// <summary>⭐ 六種狀態各走一次，尤其「結束了還沒結算」要跟「結算完」分得開。</summary>
        [Test]
        public void 報告_六種狀態()
        {
            Assert.AreEqual(MonitorState.NoSession, LevelMonitorReport.Build(null, null, null, 0.0).State);

            var session = new LevelSession { Rating = new SkillRatingTracker(AgreedSettings(), 1200.0) };
            Assert.AreEqual(MonitorState.NoLevel, LevelMonitorReport.Build(session, null, null, 0.0).State);

            session.LoadBoxOnly(Region.FromPolygon(new[]
            {
                new ExactVector2(0, 0), new ExactVector2(12, 0), new ExactVector2(12, 12), new ExactVector2(0, 12),
            }));
            Assert.AreEqual(MonitorState.NoPuzzle, LevelMonitorReport.Build(session, null, null, 0.0).State);

            LoadClassicSquare(session);
            LevelData level = session.Data;
            level.StartForegroundTiming(0.0);
            Assert.AreEqual(MonitorState.Playing, LevelMonitorReport.Build(session, null, null, 1.0).State);

            // 過關但計時沒停 → 守衛擋下，停在「結算中」
            PlaceAllPieces(level);
            level.MarkSolvedIfAllOnBoard();
            session.Rating.TrySettle(level, TestRange(), out _);
            Assert.AreEqual(MonitorState.AwaitingSettlement, LevelMonitorReport.Build(session, null, null, 2.0).State);

            level.StopAllTiming(2.0);
            session.Rating.TrySettle(level, TestRange(), out _);
            LevelMonitorReport settled = LevelMonitorReport.Build(session, null, null, 3.0);
            Assert.AreEqual(MonitorState.Settled, settled.State);
            Assert.IsTrue(settled.Settlement.HasValue);
        }

        /// <summary>
        /// 預覽只在還在玩、而且有實力紀錄時有值。
        /// ⚠️ 結算之後刻意沒有——那時實力已經更新了，拿新實力算的 E 會跟明細裡的 E 不一樣。
        /// </summary>
        [Test]
        public void 報告_預覽只在還在玩的時候有()
        {
            var session = new LevelSession { Rating = new SkillRatingTracker(AgreedSettings(), 1100.0) };
            LoadClassicSquare(session, 1300);
            session.Data.StartForegroundTiming(0.0);

            LevelMonitorReport playing = LevelMonitorReport.Build(session, null, null, 5.0);
            Assert.AreEqual(SkillRating.ExpectedPerformance(1100.0, 1300, 400.0), playing.PreviewExpectedPerformance.Value, 1e-12);

            session.Data.MarkAbandoned(10.0);
            session.Rating.TrySettle(session.Data, TestRange(), out _);
            Assert.IsFalse(LevelMonitorReport.Build(session, null, null, 11.0).PreviewExpectedPerformance.HasValue, "結算之後");

            var withoutRating = new LevelSession();
            LoadClassicSquare(withoutRating);
            Assert.IsFalse(LevelMonitorReport.Build(withoutRating, null, null, 0.0).PreviewExpectedPerformance.HasValue, "沒有實力紀錄");
            Assert.AreEqual(0, LevelMonitorReport.Build(withoutRating, null, null, 0.0).History.Count, "沒有實力紀錄時走勢是空的，不是 null");
        }

        /// <summary>前景要含還在跑的那一段——用 <c>ForegroundSeconds</c> 的話玩的時候一直是 0，看了會以為計時壞了。</summary>
        [Test]
        public void 報告_前景含還在跑的那一段()
        {
            var session = new LevelSession();
            LoadClassicSquare(session);
            session.Data.StartForegroundTiming(10.0);

            Assert.AreEqual(25.0, LevelMonitorReport.Build(session, null, null, 35.0).ForegroundSecondsSoFar, 1e-12);
        }

        /// <summary>還沒結算時，下一題目標是用報告拿到的範圍夾目前的實力；沒有範圍就是 null。</summary>
        [Test]
        public void 報告_還沒結算時下一題目標用範圍夾()
        {
            var session = new LevelSession { Rating = new SkillRatingTracker(AgreedSettings(), 2000.0) };
            Assert.AreEqual(1660, LevelMonitorReport.Build(session, null, TestRange(), 0.0).NextTargetDifficulty);
            Assert.IsNull(LevelMonitorReport.Build(session, null, null, 0.0).NextTargetDifficulty, "沒有範圍");
        }

        /// <summary>
        /// ⭐ <b>結算過了就讀明細裡記下的那個</b>——跟彈窗上的方向比的是同一個欄位。
        /// 故意傳一個不一樣的範圍進報告：如果報告偷偷又自己夾一次，這條會紅。
        /// </summary>
        [Test]
        public void 報告_結算過了下一題目標讀明細()
        {
            var session = new LevelSession { Rating = new SkillRatingTracker(AgreedSettings(), 2000.0) };
            LoadClassicSquare(session, 1600);
            session.Data.StartForegroundTiming(0.0);
            session.Data.MarkAbandoned(10.0);
            Assert.IsTrue(session.Rating.TrySettle(session.Data, TestRange(), out RatingUpdate update));

            var differentRange = new TargetDifficultyRange(500, 1000);
            LevelMonitorReport report = LevelMonitorReport.Build(session, null, differentRange, 11.0);

            Assert.AreEqual(1660, update.NextTargetDifficulty, "放棄之後實力仍然高於上限，明細應該記上限");
            Assert.AreEqual(update.NextTargetDifficulty, report.NextTargetDifficulty);
        }

        /// <summary>展示倍率不是 1 時旗標要亮；是 1 時不亮；沒有設定時不亮。</summary>
        [Test]
        public void 報告_展示倍率不是1時旗標要亮()
        {
            var demonstration = new LevelSession { Rating = new SkillRatingTracker(AgreedSettings(), 1200.0) };
            Assert.IsTrue(LevelMonitorReport.Build(demonstration, null, null, 0.0).IsDemonstrationMultiplierActive);

            SkillRatingSettings formal = AgreedSettings();
            formal.demonstrationMultiplier = 1.0;
            var production = new LevelSession { Rating = new SkillRatingTracker(formal, 1200.0) };
            Assert.IsFalse(LevelMonitorReport.Build(production, null, null, 0.0).IsDemonstrationMultiplierActive);

            Assert.IsFalse(LevelMonitorReport.Build(new LevelSession(), null, null, 0.0).IsDemonstrationMultiplierActive);
        }

        /// <summary>
        /// ⭐ <b>拆解換關才重算</b>：同一關刷新很多次只算一次；換關、換權重實例才重算；
        /// 沒有權重時沒有拆解，對帳結果是 null（沒對帳 ≠ 對不上）。
        /// </summary>
        [Test]
        public void 報告_拆解換關或換權重才重算()
        {
            DifficultyWeights weights = RepositoryFiles.LoadDifficultyWeights();
            var session = new LevelSession();
            var monitor = new LevelMonitor();

            LoadClassicSquare(session);
            for (int refresh = 0; refresh < 5; refresh++) monitor.Build(session, weights, refresh);
            Assert.AreEqual(1, monitor.BreakdownComputationCount, "同一關刷新五次");

            LoadClassicSquare(session);
            monitor.Build(session, weights, 0.0);
            Assert.AreEqual(2, monitor.BreakdownComputationCount, "換關（LevelData 換了一個）");

            monitor.Build(session, RepositoryFiles.LoadDifficultyWeights(), 0.0);
            Assert.AreEqual(3, monitor.BreakdownComputationCount, "換了權重實例");

            LevelMonitorReport withoutWeights = monitor.Build(session, null, 0.0);
            Assert.IsNull(withoutWeights.Breakdown);
            Assert.IsNull(withoutWeights.BreakdownMatchesDifficulty);
        }

        /// <summary>存著的難度跟拆解算出來的不一樣時，報告要說對不上——這就是面板上會亮 ⚠️ 的情況。</summary>
        [Test]
        public void 報告_存著的難度不對時對帳失敗()
        {
            DifficultyWeights weights = RepositoryFiles.LoadDifficultyWeights();
            Placement[] solution = Fixtures.ClassicSquare();
            int correct = DifficultyEvaluator.Score(Puzzle.FromPlacements(solution), solution, weights);

            var session = new LevelSession();
            LoadClassicSquare(session, correct);
            Assert.AreEqual(true, new LevelMonitor().Build(session, weights, 0.0).BreakdownMatchesDifficulty);

            LoadClassicSquare(session, correct + 5);
            Assert.AreEqual(false, new LevelMonitor().Build(session, weights, 0.0).BreakdownMatchesDifficulty);
        }
    }
}
