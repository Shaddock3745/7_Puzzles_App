using System;
using System.IO;
using NUnit.Framework;
using Tangram.Data;
using Tangram.Data.Tests;

namespace Tangram.Core.Tests
{
    /// <summary>
    /// ELO 換算（issue #31）：公式、結算的守衛、設定檔。
    ///
    /// ⚠️ <b>公式的測試用記憶體裡組的設定，不讀設定檔</b>——設定檔是調參旋鈕，
    /// 改 T 或倍率不該讓「公式有沒有算對」的測試變紅。設定檔本身在第四節另外驗。
    ///
    /// <b>全部離線</b>：時刻由呼叫端傳進來，這裡傳的是假時間。
    /// </summary>
    public class SkillRatingTests
    {
        /// <summary>
        /// 2026-09-13 定案的那一組（r 0.5、T 90、基準 K 20 × 展示倍率 10），P 在 2026-09-15 從 0 定成 30。
        /// 「算給你看」那兩張表就是用這組算的。
        /// </summary>
        /// <returns>一份新的設定。</returns>
        private static SkillRatingSettings AgreedSettings() => new SkillRatingSettings
        {
            initialRating = 900.0,
            ratingScale = 400.0,
            dragWeightRatio = 0.5,
            referenceSeconds = 90.0,
            hintPenaltySeconds = 30.0,
            baseKFactor = 20.0,
            demonstrationMultiplier = 10.0,
        };

        /// <summary>
        /// 出題器的目標難度範圍。⚠️ <b>刻意不讀權重檔</b>，理由跟設定一樣：重算工具改了範圍，
        /// 不該讓「ELO 有沒有算對」的測試變紅。數字抄的是 2026-09-14 權重檔裡的值，只是為了讀起來眼熟。
        /// </summary>
        /// <returns>744 ～ 1660。</returns>
        private static TargetDifficultyRange TestRange() => new TargetDifficultyRange(744, 1660);

        /// <param name="rating">實力。</param>
        /// <param name="difficulty">難度。</param>
        /// <param name="outcome">結局。</param>
        /// <param name="foregroundSeconds">前景秒數。</param>
        /// <param name="dragSeconds">拖曳秒數。</param>
        /// <param name="hintsUsed">提示次數。</param>
        /// <param name="settings">設定；null 用 <see cref="AgreedSettings"/>。</param>
        /// <returns>ΔR。</returns>
        private static double ChangeOf(double rating, int difficulty, LevelOutcome outcome,
                                       double foregroundSeconds, double dragSeconds, int hintsUsed = 0,
                                       SkillRatingSettings settings = null) =>
            SkillRating.Settle(rating, difficulty, outcome, foregroundSeconds, dragSeconds, hintsUsed,
                               settings ?? AgreedSettings(), TestRange()).RatingChange;

        // ─────────────────────────────────────────────────────────────
        // 一、公式
        // ─────────────────────────────────────────────────────────────

        /// <summary>
        /// ⭐ <b>「算給你看」那張表</b>：實力 1200、題目 1200、展示 K 200。
        /// 表跟程式不一樣的話，是其中一邊寫錯了——這條就是兩邊的對帳。
        /// </summary>
        [TestCase(35.0, 25.0, LevelOutcome.Solved, 60.0, TestName = "公式_示範表_快（前景35拖曳25）加60")]
        [TestCase(90.0, 40.0, LevelOutcome.Solved, 12.5, TestName = "公式_示範表_普通（前景90拖曳40）加12點5")]
        [TestCase(110.0, 40.0, LevelOutcome.Solved, 0.0, TestName = "公式_示範表_剛好T秒（前景110拖曳40）不加不減")]
        [TestCase(300.0, 60.0, LevelOutcome.Solved, -50.0, TestName = "公式_示範表_慢（前景300拖曳60）減50")]
        [TestCase(300.0, 60.0, LevelOutcome.Abandoned, -100.0, TestName = "公式_示範表_放棄減100")]
        public void 公式_示範表(double foregroundSeconds, double dragSeconds, LevelOutcome outcome, double expectedChange)
        {
            Assert.AreEqual(expectedChange, ChangeOf(1200.0, 1200, outcome, foregroundSeconds, dragSeconds), 1e-9);
        }

        /// <summary>
        /// ⭐ <b>繼續解永遠不比放棄差</b>——這是公式的結構上保證，不是剛好調出來的。
        ///
        /// 反例是「放棄打折」那個被否決的方案：a = 0.5 時，慢慢解完一題簡單題扣得比放棄還多，
        /// 玩家會學到「卡住就放棄」。這條掃過一大片實力、難度、時間、提示次數（含提示有代價的情況），
        /// 哪一格破了就代表公式被改壞了。
        /// </summary>
        [Test]
        public void 公式_繼續解永遠不比放棄差()
        {
            SkillRatingSettings withHintCost = AgreedSettings();
            withHintCost.hintPenaltySeconds = 30.0;

            foreach (SkillRatingSettings settings in new[] { AgreedSettings(), withHintCost })
            foreach (double rating in new[] { 300.0, 900.0, 1200.0, 2000.0 })
            foreach (int difficulty in new[] { 744, 1200, 1660, 2261 })
            foreach (double foreground in new[] { 0.0, 10.0, 90.0, 1000.0, 100000.0 })
            foreach (double dragShare in new[] { 0.0, 0.5, 1.0 })
            foreach (int hints in new[] { 0, 3 })
            {
                double drag = foreground * dragShare;
                double solved = ChangeOf(rating, difficulty, LevelOutcome.Solved, foreground, drag, hints, settings);
                double abandoned = ChangeOf(rating, difficulty, LevelOutcome.Abandoned, foreground, drag, hints, settings);

                Assert.Greater(solved, abandoned,
                    $"實力 {rating}、難度 {difficulty}、前景 {foreground}、拖曳 {drag}、提示 {hints}、" +
                    $"提示代價 {settings.hintPenaltySeconds}：解完 {solved} 不比放棄 {abandoned} 好");
            }
        }

        /// <summary>
        /// <b>r = 1 是「只看前景」</b>——保守退路。
        /// ⚠️ 原本文件寫成「w₁ 設 0 就退回只看前景」，那是反方向（2026-09-13 更正），這條把對的那個釘住。
        /// </summary>
        [Test]
        public void 公式_r為1時拖曳多少都一樣()
        {
            SkillRatingSettings settings = AgreedSettings();
            settings.dragWeightRatio = 1.0;

            double noDrag = ChangeOf(1200.0, 1200, LevelOutcome.Solved, 120.0, 0.0, settings: settings);
            double allDrag = ChangeOf(1200.0, 1200, LevelOutcome.Solved, 120.0, 120.0, settings: settings);

            Assert.AreEqual(noDrag, allDrag, 1e-9);
        }

        /// <summary>展示倍率剛好乘在 ΔR 上——從展示版切回正式版，就是每個數字除以 10。</summary>
        [Test]
        public void 公式_展示倍率剛好乘上去()
        {
            SkillRatingSettings formal = AgreedSettings();
            formal.demonstrationMultiplier = 1.0;

            double demonstration = ChangeOf(1100.0, 1250, LevelOutcome.Solved, 77.0, 20.0);
            double production = ChangeOf(1100.0, 1250, LevelOutcome.Solved, 77.0, 20.0, settings: formal);

            Assert.AreEqual(production * 10.0, demonstration, 1e-9);
        }

        /// <summary>預期表現：實力等於難度時是一半；難 400 分時贏面 1 比 10。</summary>
        [Test]
        public void 公式_預期表現()
        {
            Assert.AreEqual(0.5, SkillRating.ExpectedPerformance(1200.0, 1200, 400.0), 1e-12);
            Assert.AreEqual(1.0 / 11.0, SkillRating.ExpectedPerformance(1200.0, 1600, 400.0), 1e-12);
            Assert.AreEqual(10.0 / 11.0, SkillRating.ExpectedPerformance(1600.0, 1200, 400.0), 1e-12);
        }

        /// <summary>
        /// ⭐ <b>放棄簡單題很痛、放棄難題不太痛</b>——放棄 = −K × E，沒有另外的「扣多少」。
        /// </summary>
        [Test]
        public void 公式_放棄簡單題比放棄難題痛()
        {
            double easy = ChangeOf(1200.0, 1000, LevelOutcome.Abandoned, 200.0, 20.0);
            double hard = ChangeOf(1200.0, 1400, LevelOutcome.Abandoned, 200.0, 20.0);

            Assert.Less(easy, hard, "放棄簡單題應該扣得比較多");
            Assert.AreEqual(-200.0 * SkillRating.ExpectedPerformance(1200.0, 1000, 400.0), easy, 1e-9);
        }

        /// <summary>
        /// ⭐ <b>提示的「算給你看」那張表</b>（2026-09-15 定 P = 30）：實力 1200、題目 1200、展示 K 200。
        /// 重點是第一列：卡住拿一次提示、70 秒解完 ≈ 自己 110 秒慢慢解完（0），不是大加分。
        /// </summary>
        [TestCase(70.0, 25.0, 1, 1.4084507, TestName = "公式_提示示範表_45秒拿一次70秒解完約等於0")]
        [TestCase(110.0, 40.0, 2, -25.0, TestName = "公式_提示示範表_兩次110秒解完減25")]
        [TestCase(150.0, 50.0, 3, -40.9836066, TestName = "公式_提示示範表_三次150秒解完減41")]
        [TestCase(140.0, 100.0, 3, -33.3333333, TestName = "公式_提示示範表_三次而且一直在拖片減33")]
        public void 公式_提示示範表(double foregroundSeconds, double dragSeconds, int hintsUsed, double expectedChange)
        {
            Assert.AreEqual(expectedChange,
                            ChangeOf(1200.0, 1200, LevelOutcome.Solved, foregroundSeconds, dragSeconds, hintsUsed), 1e-6);
        }

        /// <summary>
        /// ⭐ <b>用完三次提示解完，不會加分</b>（2026-09-15 定，理由是提示三片幾乎就能解了）。
        ///
        /// 前提是提示的開放時間表：第三次要等前景 135 秒（間隔 45 × 3，<c>BoardActionButtons</c> 的預設）。
        /// 最有利的情況是整局都在拖片（拖曳 = 前景，打折最多）而且 135 秒一到就解完：C = 135 × 0.5 ＋ 3 × 30 = 157.5 &gt; T，
        /// 實力高過題目 50 分（E 最大的情況）也一樣。
        /// ⚠️ <b>這條紅了代表 T、提示間隔、P 其中一個被改了而另外兩個沒跟上</b>——三個數字綁在一起。
        /// </summary>
        [Test]
        public void 公式_用完三次提示解完不會加分()
        {
            const double thirdHintUnlockSeconds = 135.0;
            foreach (double foreground in new[] { thirdHintUnlockSeconds, 150.0, 200.0, 400.0 })
            foreach (double dragShare in new[] { 0.0, 0.3, 0.7, 1.0 })
            foreach (double rating in new[] { 1150.0, 1200.0, 1250.0 })
            {
                double change = ChangeOf(rating, 1200, LevelOutcome.Solved, foreground, foreground * dragShare, 3);
                Assert.LessOrEqual(change, 0.0,
                    $"實力 {rating}、前景 {foreground}、拖曳 {foreground * dragShare}、提示 3 次：加了 {change} 分");
            }
        }

        /// <summary>提示加在時間代價上：一次提示等於多想 P 秒。</summary>
        [Test]
        public void 公式_提示加在時間代價上()
        {
            SkillRatingSettings settings = AgreedSettings();

            RatingUpdate withoutHints = SkillRating.Settle(1200.0, 1200, LevelOutcome.Solved, 90.0, 40.0, 0, settings, TestRange());
            RatingUpdate withTwoHints = SkillRating.Settle(1200.0, 1200, LevelOutcome.Solved, 90.0, 40.0, 2, settings, TestRange());

            Assert.AreEqual(withoutHints.TimeCostSeconds + 60.0, withTwoHints.TimeCostSeconds, 1e-9);
        }

        /// <summary>
        /// ⚠️ <b>拖曳比前景長要響</b>。那違反局內計時的不變式（背景時不可能拖曳），代表計時的呼叫點接錯了；
        /// 不擋的話時間代價變負、實際表現超過 1，實力安靜地虛漲。
        /// </summary>
        [Test]
        public void 公式_不合理的輸入要丟例外()
        {
            SkillRatingSettings settings = AgreedSettings();

            Assert.Throws<ArgumentException>(() =>
                SkillRating.Settle(1200.0, 1200, LevelOutcome.Solved, 10.0, 20.0, 0, settings, TestRange()), "拖曳比前景長");
            Assert.Throws<ArgumentException>(() =>
                SkillRating.Settle(1200.0, 1200, LevelOutcome.Solved, -1.0, 0.0, 0, settings, TestRange()), "前景是負的");
            Assert.Throws<ArgumentException>(() =>
                SkillRating.Settle(1200.0, 1200, LevelOutcome.Solved, 10.0, 0.0, -1, settings, TestRange()), "提示次數是負的");
            Assert.Throws<ArgumentNullException>(() =>
                SkillRating.Settle(1200.0, 1200, LevelOutcome.Solved, 10.0, 0.0, 0, null, TestRange()), "沒有設定");
            Assert.Throws<ArgumentNullException>(() =>
                SkillRating.Settle(1200.0, 1200, LevelOutcome.Solved, 10.0, 0.0, 0, settings, null), "沒有範圍");
        }

        // ─────────────────────────────────────────────────────────────
        // 二、化整與方向
        // ─────────────────────────────────────────────────────────────

        /// <summary>
        /// ⚠️ <b>四捨五入要遠離零</b>。C# 的 <c>Math.Round</c> 預設是銀行家捨入：12.5 → 12、13.5 → 14。
        /// </summary>
        [TestCase(912.5, 913)]
        [TestCase(913.5, 914)]
        [TestCase(912.4999, 912)]
        [TestCase(1200.0, 1200)]
        public void 化整_四捨五入遠離零(double rating, int expected)
        {
            Assert.AreEqual(expected, SkillRating.RoundRating(rating));
        }

        /// <summary>
        /// 方向比的是明細裡記下的「<b>下一題目標</b> − 這一題的難度」，門檻是出題器的容許範圍 ±50。
        /// ⭐ 2026-09-14 起目標直接給，不再靠「實力 1800 會被夾到哪」繞一圈——夾不夾是下一條測試的事。
        /// </summary>
        [TestCase(1200, 1251, NextPuzzleTrend.Harder)]
        [TestCase(1200, 1250, NextPuzzleTrend.Similar)]
        [TestCase(1200, 1150, NextPuzzleTrend.Similar)]
        [TestCase(1200, 1149, NextPuzzleTrend.Easier)]
        public void 方向_用明細裡的下一題目標跟這一題比(int difficulty, int nextTarget, NextPuzzleTrend expected)
        {
            // ⚠️ 實力變化故意給 +500：方向如果偷偷改回去看 ΔR 或結算後實力，這幾格會全部變成「會難一點」
            var update = new RatingUpdate(LevelOutcome.Solved, difficulty, 1200.0, 0.5, 90.0, 0.5, 200.0, 500.0,
                                          90.0, 0.0, 0, nextTarget);
            Assert.AreEqual(expected, update.Trend);
        }

        /// <summary>
        /// ⭐ <b>結算時就把下一題目標夾好記下來</b>：結算後實力四捨五入、再夾進範圍。
        /// 後兩格是夾住的情況——實力衝過上限，下一題目標停在上限，所以這一題 1655 顯示「差不多」是對的。
        /// </summary>
        [TestCase(1200.0, 1200, 1213, TestName = "結算_下一題目標_範圍內就是實力四捨五入")]
        [TestCase(600.0, 600, 744, TestName = "結算_下一題目標_低於下限夾到下限")]
        [TestCase(1800.0, 1655, 1660, TestName = "結算_下一題目標_高於上限夾到上限")]
        public void 結算_下一題目標是夾過的(double ratingBefore, int difficulty, int expectedNextTarget)
        {
            // 前景 90、拖曳 40 就是示範表「普通」那一列：C = 70、S = 0.5625。
            // 實力 = 難度時 E = 0.5，ΔR = +12.5（1200 → 1212.5 → 四捨五入 1213，600 → 613 再夾到 744）；
            // 1800 對 1655 時 E ≈ 0.70，ΔR ≈ −27，1773 仍然高於上限
            RatingUpdate update = SkillRating.Settle(ratingBefore, difficulty, LevelOutcome.Solved,
                                                     90.0, 40.0, 0, AgreedSettings(), TestRange());

            int unclamped = SkillRating.RoundRating(update.RatingAfter);
            Assert.AreEqual(TestRange().Clamp(unclamped), update.NextTargetDifficulty, "記下來的不是夾過的結算後實力");
            Assert.AreEqual(expectedNextTarget, update.NextTargetDifficulty,
                $"結算後實力 {update.RatingAfter:F1}，這組測資應該讓目標落在 {expectedNextTarget}");
        }

        // ─────────────────────────────────────────────────────────────
        // 三、結算的守衛（SkillRatingTracker 與 LevelData）
        // ─────────────────────────────────────────────────────────────

        /// <param name="difficulty">這一題的難度。</param>
        /// <returns>載好經典大正方形、掛上 tracker 的一場。</returns>
        private static LevelSession FreshSession(int difficulty = 1200)
        {
            var session = new LevelSession { Rating = new SkillRatingTracker(AgreedSettings(), 1200.0) };
            LoadClassicSquare(session, difficulty);
            return session;
        }

        /// <param name="session">要換關的那一場。</param>
        /// <param name="difficulty">這一題的難度。</param>
        private static void LoadClassicSquare(LevelSession session, int difficulty)
        {
            Placement[] solution = Fixtures.ClassicSquare();
            session.Load(Puzzle.FromPlacements(solution), solution, difficulty, allowRoll: true);
        }

        /// <summary>七片照參考解擺上去。<b>不停計時</b>——那是呼叫端的事，跟真的遊戲一樣。</summary>
        /// <param name="level">這一關。</param>
        private static void PlaceAllPieces(LevelData level)
        {
            for (int slotIndex = 0; slotIndex < BoardState.PieceCount; slotIndex++)
                level.Board.Place(slotIndex, level.ReferenceSolution[slotIndex]);
        }

        /// <summary>還在玩的一局不結算——關掉 App 走人就是永遠停在這裡，所以那種不算輸。</summary>
        [Test]
        public void 結算_還沒結束的一局不結算()
        {
            LevelSession session = FreshSession();
            session.Data.StartForegroundTiming(0.0);
            session.Data.StopForegroundTiming(50.0);

            Assert.IsFalse(session.Rating.TrySettle(session.Data, TestRange(), out _));
            Assert.IsFalse(session.Data.Settlement.HasValue);
            Assert.AreEqual(1200.0, session.Rating.CurrentRating);
        }

        /// <summary>
        /// ⚠️ <b>沒給範圍就丟例外，連還不需要結算的一局也一樣</b>：那是接線錯了。
        /// 如果等守衛放行才檢查，症狀會延後到「第一次過關那一刻才當掉」。
        /// </summary>
        [Test]
        public void 結算_沒給範圍就丟例外_不等到真的要結算()
        {
            LevelSession session = FreshSession();
            Assert.Throws<ArgumentNullException>(() => session.Rating.TrySettle(session.Data, null, out _), "還在玩的一局");
            Assert.Throws<ArgumentNullException>(() => session.Rating.TrySettle(null, null, out _), "沒有關卡");
        }

        /// <summary>
        /// 沒有題目的空方框（出題工具）不結算。不擋的話難度是 0，
        /// 預期表現接近 1，一次放棄就扣掉整個 K。
        /// </summary>
        [Test]
        public void 結算_沒有題目的空方框不結算()
        {
            var session = new LevelSession { Rating = new SkillRatingTracker(AgreedSettings(), 1200.0) };
            session.LoadBoxOnly(Region.FromPolygon(new[]
            {
                new ExactVector2(0, 0), new ExactVector2(12, 0), new ExactVector2(12, 12), new ExactVector2(0, 12),
            }));
            session.Data.MarkAbandoned(10.0);

            Assert.IsFalse(session.Rating.TrySettle(session.Data, TestRange(), out _));
            Assert.AreEqual(1200.0, session.Rating.CurrentRating);
        }

        /// <summary>
        /// 說了不算的一關（掃描題，2026-10-14）結束了也不結算，實力不動、也不留明細。
        /// </summary>
        [Test]
        public void 結算_不算實力的一關不結算()
        {
            var session = new LevelSession { Rating = new SkillRatingTracker(AgreedSettings(), 1200.0) };
            Placement[] solution = Fixtures.ClassicSquare();
            session.Load(Puzzle.FromPlacements(solution), solution, 1200, allowRoll: false, countsTowardRating: false);

            session.Data.StartForegroundTiming(0.0);
            session.Data.MarkAbandoned(100.0);

            Assert.IsFalse(session.Data.CountsTowardRating);
            Assert.IsFalse(session.Rating.TrySettle(session.Data, TestRange(), out _));
            Assert.IsFalse(session.Data.Settlement.HasValue);
            Assert.AreEqual(1200.0, session.Rating.CurrentRating);
            Assert.AreEqual(0, session.Rating.History.Count, "走勢裡不該多一筆");
        }

        /// <summary>沒特別講的話照舊要結算——回饋場景、挑戰模式、圖形模式都沒改呼叫端。</summary>
        [Test]
        public void 結算_預設是要算實力的()
        {
            Assert.IsTrue(FreshSession().Data.CountsTowardRating);
        }

        /// <summary>⭐ <b>同一局只結算一次</b>——每幀都會叫，不擋的話實力每幀掉一次。</summary>
        [Test]
        public void 結算_同一局只結算一次()
        {
            LevelSession session = FreshSession();
            session.Data.StartForegroundTiming(0.0);
            session.Data.MarkAbandoned(100.0);

            Assert.IsTrue(session.Rating.TrySettle(session.Data, TestRange(), out RatingUpdate first));
            double afterFirst = session.Rating.CurrentRating;

            Assert.IsFalse(session.Rating.TrySettle(session.Data, TestRange(), out _), "第二次不應該再結算");
            Assert.AreEqual(afterFirst, session.Rating.CurrentRating, "實力被扣了兩次");
            Assert.AreEqual(first.RatingAfter, session.Data.Settlement.Value.RatingAfter);
        }

        /// <summary>
        /// ⚠️ <b>計時還在跑就不結算</b>。前景秒數只在停止時累加，還在跑就讀會少算、實際表現偏高。
        /// 正常流程過關會停計時；這裡直接叫過關、不停計時，模擬「之後有人新加了一條沒停計時的結束路徑」。
        /// </summary>
        [Test]
        public void 結算_計時還在跑就先不結算()
        {
            LevelSession session = FreshSession();
            LevelData level = session.Data;
            level.StartForegroundTiming(0.0);
            PlaceAllPieces(level);
            Assert.IsTrue(level.MarkSolvedIfAllOnBoard());

            Assert.IsFalse(session.Rating.TrySettle(level, TestRange(), out _), "計時還在跑，不應該結算");

            level.StopAllTiming(90.0);
            Assert.IsTrue(session.Rating.TrySettle(level, TestRange(), out RatingUpdate update), "停了之後就該結算");
            Assert.AreEqual(90.0, update.TimeCostSeconds, 1e-9, "用的必須是停完之後的前景秒數");
        }

        /// <summary>結算用的是 <see cref="LevelData"/> 上的數字：難度、前景、拖曳、提示。</summary>
        [Test]
        public void 結算_讀的是這一關的統計()
        {
            LevelSession session = FreshSession(difficulty: 1350);
            LevelData level = session.Data;
            level.StartForegroundTiming(0.0);
            level.StartDragTiming(10.0);
            level.StopDragTiming(12.0);
            Assert.IsTrue(level.TryUseHint(50.0, 45.0, out _));
            PlaceAllPieces(level);
            level.MarkSolvedIfAllOnBoard();
            level.StopAllTiming(55.0);

            Assert.IsTrue(session.Rating.TrySettle(level, TestRange(), out RatingUpdate update));

            RatingUpdate expected = SkillRating.Settle(1200.0, 1350, LevelOutcome.Solved, 55.0, 2.0, 1, AgreedSettings(), TestRange());
            Assert.AreEqual(LevelOutcome.Solved, update.Outcome);
            Assert.AreEqual(1350, update.Difficulty);
            Assert.AreEqual(expected.RatingChange, update.RatingChange, 1e-12);
        }

        /// <summary>
        /// ⭐ <b>連續兩局，實力不化整</b>：第二局的起點就是第一局算出來的小數。
        /// 每局化整一次的話，+12.5 這種變化的誤差會一局一局累積。
        /// </summary>
        [Test]
        public void 結算_連續兩局實力接得上而且不化整()
        {
            var session = new LevelSession { Rating = new SkillRatingTracker(AgreedSettings(), 900.0) };

            LoadClassicSquare(session, 1200);
            session.Data.StartForegroundTiming(0.0);
            session.Data.MarkAbandoned(100.0);
            Assert.IsTrue(session.Rating.TrySettle(session.Data, TestRange(), out RatingUpdate first));
            Assert.AreNotEqual(Math.Round(first.RatingAfter), first.RatingAfter, "這組數字應該算出小數，測試才有意義");

            // 換關：LevelData 整包換掉，「還沒結算」自動重置；tracker 在外殼上，實力延續
            LoadClassicSquare(session, 1000);
            Assert.IsFalse(session.Data.Settlement.HasValue, "換關之後應該是還沒結算");
            session.Data.StartForegroundTiming(0.0);
            session.Data.MarkAbandoned(100.0);
            Assert.IsTrue(session.Rating.TrySettle(session.Data, TestRange(), out RatingUpdate second));

            Assert.AreEqual(first.RatingAfter, second.RatingBefore, "第二局的起點必須是第一局的終點，一點都不差");
            Assert.AreEqual(second.RatingAfter, session.Rating.CurrentRating);
        }

        /// <summary>
        /// <see cref="LevelData.RecordSettlement"/> 自己的兩道守衛：還沒結束不收、已經記過不收。
        /// 真相的擁有者自己擋，不靠呼叫端小心。
        /// </summary>
        [Test]
        public void 結算_LevelData只收已結束而且還沒記過的()
        {
            LevelSession session = FreshSession();
            LevelData level = session.Data;
            RatingUpdate update = SkillRating.Settle(1200.0, 1200, LevelOutcome.Abandoned, 10.0, 0.0, 0, AgreedSettings(), TestRange());

            Assert.IsFalse(level.RecordSettlement(update), "還沒結束就不該收");

            level.MarkAbandoned(10.0);
            Assert.IsTrue(level.RecordSettlement(update));
            Assert.IsFalse(level.RecordSettlement(update), "已經記過就不該再收");
        }

        /// <summary>下一題的目標 = 目前實力四捨五入。</summary>
        [Test]
        public void 結算_下一題目標是實力四捨五入()
        {
            Assert.AreEqual(913, new SkillRatingTracker(AgreedSettings(), 912.5).NextTargetDifficulty);
        }

        /// <summary>在記憶體裡組的不合法設定進不了 tracker——不然 T = 0 會在結算時除以零。</summary>
        [Test]
        public void 結算_不合法的設定進不了tracker()
        {
            SkillRatingSettings broken = AgreedSettings();
            broken.referenceSeconds = 0.0;

            Assert.Throws<ArgumentException>(() => new SkillRatingTracker(broken, 1200.0));
            Assert.Throws<ArgumentNullException>(() => new SkillRatingTracker(null, 1200.0));
        }

        // ─────────────────────────────────────────────────────────────
        // 四、設定檔（Assets/Resources/SkillRatingSettings.json）
        // ─────────────────────────────────────────────────────────────

        /// <summary>
        /// 設定檔讀得出來、通過檢查。遊戲讀不到就不進關，所以壞掉要在日常的 Run All 就紅。
        /// ⚠️ <b>刻意不釘數值</b>：它是調參旋鈕，改 T 或倍率不該讓測試變紅。
        /// </summary>
        [Test]
        public void 設定檔_讀得出來而且通過檢查()
        {
            SkillRatingSettings settings = RepositoryFiles.LoadSkillRatingSettings();
            Assert.DoesNotThrow(() => new SkillRatingTracker(settings, settings.initialRating));

            TestContext.WriteLine($"起始實力 {settings.initialRating}、T {settings.referenceSeconds}、r {settings.dragWeightRatio}、" +
                                  $"P {settings.hintPenaltySeconds}、K {settings.baseKFactor} × {settings.demonstrationMultiplier}");
        }

        /// <summary>
        /// ⭐ <b>鍵名跟欄位一一對上</b>。r 與 P 的 0 是合法值，拼錯鍵名讀成 0 時讀檔的檢查攔不住——
        /// <c>dragWeightRatio</c> 拼錯會安靜地變成「拖曳完全免費」。
        /// </summary>
        [Test]
        public void 設定檔_鍵名跟類別的欄位一一對上()
        {
            string text = File.ReadAllText(RepositoryFiles.Find(RepositoryFiles.SkillRatingSettingsRelativePath));
            const string label = "ELO 設定檔";

            JsonKeyAssertions.EveryKeyIsKnown(text, label, typeof(SkillRatingSettings));
            JsonKeyAssertions.EveryFieldAppears(text, label, typeof(SkillRatingSettings), 1);
        }

        /// <summary>讀檔時的檢查：每一種壞法都要丟例外，訊息要說是哪一項。一次只弄壞一個地方。</summary>
        [Test]
        public void 設定檔_壞掉的內容讀檔時就丟例外()
        {
            string text = File.ReadAllText(RepositoryFiles.Find(RepositoryFiles.SkillRatingSettingsRelativePath));

            AssertRejected("", "空的", "空字串");
            foreach (string mustBePositive in new[]
                     { "initialRating", "ratingScale", "referenceSeconds", "baseKFactor", "demonstrationMultiplier" })
                AssertRejected(JsonKeyAssertions.ReplaceFirstNumber(text, mustBePositive, "0"), mustBePositive, $"{mustBePositive} 是 0");

            AssertRejected(JsonKeyAssertions.ReplaceFirstNumber(text, "dragWeightRatio", "1.5"), "dragWeightRatio", "r 超過 1");
            AssertRejected(JsonKeyAssertions.ReplaceFirstNumber(text, "dragWeightRatio", "-0.1"), "dragWeightRatio", "r 是負的");
            AssertRejected(JsonKeyAssertions.ReplaceFirstNumber(text, "hintPenaltySeconds", "-1"), "hintPenaltySeconds", "P 是負的");

            // 鍵名拼錯 → 讀成 0 → 被「必須大於 0」攔下來
            AssertRejected(text.Replace("\"referenceSeconds\"", "\"referenceSecond\""), "referenceSeconds", "T 的鍵名拼錯");
        }

        /// <param name="json">要讀的內容。</param>
        /// <param name="expectedInMessage">例外訊息裡必須提到的字。</param>
        /// <param name="label">這一種壞法的名字。</param>
        private static void AssertRejected(string json, string expectedInMessage, string label)
        {
            var exception = Assert.Throws<ArgumentException>(() => SkillRatingSettings.FromJson(json), label);
            StringAssert.Contains(expectedInMessage, exception.Message, $"{label}：訊息沒有說是哪一項");
        }
    }
}
