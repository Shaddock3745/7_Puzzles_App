using System;
using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using Tangram.Data;
using Tangram.Data.Tests;

namespace Tangram.Core.Tests
{
    /// <summary>
    /// 監控面板上的字（issue #38，2026-10-23）：<see cref="LevelMonitorText"/>。
    ///
    /// ⭐ <b>重點是「公式代進去的是明細裡的數字」</b>：面板是讓人照著按計算機驗算的，代錯一個數字（例如代了結算之後的實力），
    /// 算式看起來照樣通順，只是對不上——不報錯。Editor 的視窗與 App 的 overlay 共用這一份，所以驗一次兩邊都算數。
    /// </summary>
    public class MonitorTextTests
    {
        /// <summary>跟 <c>MonitorTests</c> 同一組（r 0.5、T 90、P 30、K 20 × 10）。</summary>
        private static SkillRatingSettings AgreedSettings(double demonstrationMultiplier = 10.0) => new SkillRatingSettings
        {
            initialRating = 900.0,
            ratingScale = 400.0,
            dragWeightRatio = 0.5,
            referenceSeconds = 90.0,
            hintPenaltySeconds = 30.0,
            baseKFactor = 20.0,
            demonstrationMultiplier = demonstrationMultiplier,
        };

        private static TargetDifficultyRange TestRange() => new TargetDifficultyRange(744, 1660);

        /// <summary>一場、實力 1200、載入經典大正方形（難度照權重算的真值，對帳才過得了）。</summary>
        private static LevelSession SessionWithClassicSquare(DifficultyWeights weights, double demonstrationMultiplier = 10.0)
        {
            var session = new LevelSession { Rating = new SkillRatingTracker(AgreedSettings(demonstrationMultiplier), 1200.0) };
            LoadClassicSquare(session, weights);
            return session;
        }

        private static void LoadClassicSquare(LevelSession session, DifficultyWeights weights)
        {
            Placement[] solution = Fixtures.ClassicSquare();
            Puzzle puzzle = Puzzle.FromPlacements(solution);
            session.Load(puzzle, solution, DifficultyEvaluator.Score(puzzle, solution, weights), allowRoll: true);
        }

        /// <summary>照正常流程過關並結算：開始計時、擺滿、判過關、停計時、結算。</summary>
        private static RatingUpdate SolveAndSettle(LevelSession session, double foregroundSeconds)
        {
            LevelData level = session.Data;
            level.StartForegroundTiming(0.0);
            for (int slotIndex = 0; slotIndex < BoardState.PieceCount; slotIndex++)
                level.Board.Place(slotIndex, level.ReferenceSolution[slotIndex]);
            Assert.IsTrue(level.MarkSolvedIfAllOnBoard());
            level.StopAllTiming(foregroundSeconds);
            Assert.IsTrue(session.Rating.TrySettle(level, TestRange(), out RatingUpdate update));
            return update;
        }

        private static readonly MonitorPage[] AllPages = (MonitorPage[])Enum.GetValues(typeof(MonitorPage));

        [Test]
        public void 文字_每一頁在每一種狀態都組得出來()
        {
            DifficultyWeights weights = RepositoryFiles.LoadDifficultyWeights();
            var monitor = new LevelMonitor();

            var reports = new List<LevelMonitorReport> { monitor.Build(null, weights, 0.0) };

            var session = new LevelSession { Rating = new SkillRatingTracker(AgreedSettings(), 1200.0) };
            reports.Add(monitor.Build(session, weights, 0.0));        // 還沒進關

            LoadClassicSquare(session, weights);
            session.Data.StartForegroundTiming(0.0);
            reports.Add(monitor.Build(session, weights, 5.0));        // 還在玩
            reports.Add(monitor.Build(session, weights, 5.0, isPaused: true));

            session.Data.StopAllTiming(5.0);
            SolveAndSettle(session, 40.0);
            reports.Add(monitor.Build(session, weights, 50.0));       // 結算過了

            foreach (LevelMonitorReport report in reports)
                foreach (MonitorPage page in AllPages)
                    Assert.DoesNotThrow(() => LevelMonitorText.PageText(report, page), $"{report.State} 的 {page}");

            Assert.AreEqual("沒有這一場。", LevelMonitorText.PageText(reports[0], MonitorPage.Puzzle));
            Assert.AreEqual("還沒進關。", LevelMonitorText.StatusText(reports[1]));
            Assert.IsNull(LevelMonitorText.StatusText(reports[2]), "一切正常時狀態列是空的");
        }

        /// <summary>
        /// ⭐ 四條公式代進去的全是<b>明細裡</b>的數字：結算前的實力、這一題的難度、明細的秒數與提示次數——
        /// 不是現在的實力（結算之後已經變了）。
        /// </summary>
        [Test]
        public void 文字_結算公式代進去的是明細裡的數字()
        {
            SkillRatingSettings settings = AgreedSettings();
            RatingUpdate update = SkillRating.Settle(1100.0, 1250, LevelOutcome.Solved, 95.0, 41.0, 2, settings, TestRange());

            IReadOnlyList<MonitorFormula> formulas = LevelMonitorText.SettlementFormulas(update, settings);
            Assert.AreEqual(4, formulas.Count);

            StringAssert.Contains("(1250 − 1100.0) ÷ 400", formulas[0].Substituted, "E 用結算前的實力與這一題的難度");
            Assert.AreEqual(update.ExpectedPerformance.ToString("F3"), formulas[0].Result);

            StringAssert.Contains("95.0 − (1 − 0.5) × 41.0 ＋ 2 × 30", formulas[1].Substituted, "C 用明細的前景、拖曳、提示");
            Assert.AreEqual($"{update.TimeCostSeconds:F1} 秒", formulas[1].Result);

            StringAssert.Contains($"90 ÷ (90 ＋ {update.TimeCostSeconds:F1})", formulas[2].Substituted);
            Assert.AreEqual(update.ActualPerformance.ToString("F3"), formulas[2].Result);

            StringAssert.Contains($"{update.EffectiveKFactor:0.##} × ", formulas[3].Substituted);
            Assert.AreEqual(LevelMonitorText.SignedPoints(update.RatingChange), formulas[3].Result);

            // 一行接起來是「符號　＝　代入　＝　結果」，任何人照著按計算機都驗得出來
            Assert.AreEqual($"{formulas[0].Symbolic}　＝　{formulas[0].Substituted}　＝　{formulas[0].Result}", formulas[0].Equation);
        }

        [Test]
        public void 文字_放棄時實際表現是零()
        {
            SkillRatingSettings settings = AgreedSettings();
            RatingUpdate update = SkillRating.Settle(1100.0, 1250, LevelOutcome.Abandoned, 300.0, 100.0, 3, settings, TestRange());

            MonitorFormula actual = LevelMonitorText.SettlementFormulas(update, settings)[2];
            Assert.AreEqual("放棄 ＝ 0", actual.Symbolic);
            Assert.AreEqual("0", actual.Result);
        }

        /// <summary>合計那一行的最後一個數字就是分數；不算分的特徵不列進合計，但表格裡照樣有一列寫「不算分」。</summary>
        [Test]
        public void 文字_拆解的合計以分數結尾而且每個特徵都有中文名字()
        {
            DifficultyWeights weights = RepositoryFiles.LoadDifficultyWeights();
            Placement[] solution = Fixtures.ClassicSquare();
            DifficultyBreakdown breakdown = DifficultyBreakdown.Measure(Puzzle.FromPlacements(solution), solution, weights);

            string sum = LevelMonitorText.BreakdownSumText(breakdown);
            StringAssert.StartsWith(breakdown.Center.ToString("0"), sum);
            StringAssert.EndsWith($"＝ {breakdown.Score}", sum);

            foreach (DifficultyTermContribution term in breakdown.Terms)
            {
                string[] cells = LevelMonitorText.BreakdownCells(term);
                Assert.AreEqual(LevelMonitorText.BreakdownHeader.Count, cells.Length);
                Assert.AreNotEqual(term.Key, cells[0], $"特徵 {term.Key} 沒有中文名字");
                if (term.Weight == 0.0) Assert.AreEqual("不算分", cells[5]);
                else StringAssert.Contains(LevelMonitorText.SignedPoints(term.Points), sum);
            }
        }

        [Test]
        public void 文字_暫停中的關寫暫停中結束了的關不算暫停()
        {
            DifficultyWeights weights = RepositoryFiles.LoadDifficultyWeights();
            LevelSession session = SessionWithClassicSquare(weights);
            var monitor = new LevelMonitor();

            StringAssert.Contains("暫停中", LevelMonitorText.RoundStateText(monitor.Build(session, weights, 0.0, isPaused: true)));
            Assert.AreEqual("進行中", LevelMonitorText.RoundStateText(monitor.Build(session, weights, 0.0)));

            SolveAndSettle(session, 30.0);
            LevelMonitorReport finished = monitor.Build(session, weights, 40.0, isPaused: true);
            Assert.IsFalse(finished.IsPaused, "結束了的關不會被暫停");
            Assert.AreEqual("已過關", LevelMonitorText.RoundStateText(finished));
        }

        /// <summary>手機畫面放不下太多局：只列最新的幾局，前面的寫「省略」。</summary>
        [Test]
        public void 文字_走勢只列最新的幾局()
        {
            DifficultyWeights weights = RepositoryFiles.LoadDifficultyWeights();
            LevelSession session = SessionWithClassicSquare(weights);
            for (int round = 0; round < 3; round++)
            {
                if (round > 0) LoadClassicSquare(session, weights);
                SolveAndSettle(session, 30.0 + round * 20.0);
            }

            string text = LevelMonitorText.PageText(new LevelMonitor().Build(session, weights, 0.0), MonitorPage.History, maximumHistoryRows: 2);
            StringAssert.Contains("（前 1 局省略）", text);

            string[] rows = text.Split('\n').Where(line => line.Length > 0 && char.IsDigit(line[0])).ToArray();
            Assert.AreEqual(2, rows.Length, "只列兩局");
            StringAssert.StartsWith("2", rows[0]);
            StringAssert.StartsWith("3", rows[1]);
        }

        [Test]
        public void 文字_展示倍率不是一才有警語()
        {
            DifficultyWeights weights = RepositoryFiles.LoadDifficultyWeights();
            var monitor = new LevelMonitor();

            LevelMonitorReport demonstration = monitor.Build(SessionWithClassicSquare(weights, 10.0), weights, 0.0);
            StringAssert.Contains("10 倍", LevelMonitorText.DemonstrationWarning(demonstration));
            StringAssert.Contains("10 倍", LevelMonitorText.PageText(demonstration, MonitorPage.Settings));

            LevelMonitorReport official = monitor.Build(SessionWithClassicSquare(weights, 1.0), weights, 0.0);
            Assert.IsNull(LevelMonitorText.DemonstrationWarning(official));
        }

        [Test]
        public void 文字_出題紀錄三種下場()
        {
            Assert.AreEqual("非生成題", LevelMonitorText.ShortGenerationText(null));
            StringAssert.StartsWith("非生成題", LevelMonitorText.GenerationText(null, 1200));
            Assert.AreEqual("會難一點", LevelMonitorText.TrendTextOf(NextPuzzleTrend.Harder));
            Assert.AreEqual("會簡單一點", LevelMonitorText.TrendTextOf(NextPuzzleTrend.Easier));
            Assert.AreEqual("+1.5", LevelMonitorText.SignedPoints(1.46));
            Assert.AreEqual("-0.4", LevelMonitorText.SignedPoints(-0.4));
        }

        [Test]
        public void 參數檢查()
        {
            LevelMonitorReport report = new LevelMonitor().Build(null, null, 0.0);
            Assert.Throws<ArgumentNullException>(() => LevelMonitorText.PageText(null, MonitorPage.Puzzle));
            Assert.Throws<ArgumentOutOfRangeException>(() => LevelMonitorText.PageText(report, MonitorPage.History, 0));
            Assert.Throws<ArgumentNullException>(() => LevelMonitorText.StatusText(null));
        }
    }
}
