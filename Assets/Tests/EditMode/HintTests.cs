using System;
using NUnit.Framework;
using Tangram.Data;

namespace Tangram.Core.Tests
{
    /// <summary>
    /// 提示的模型層（2026-09-15，互動設計 §9）：幾次能按、挑哪一片、算不算擺對。
    /// 畫面那半（覆蓋片怎麼閃）在 <c>HintOverlayView</c>，這裡測不到。
    /// </summary>
    public class HintTests
    {
        /// <summary>測試用的間隔，跟正式版預設一樣。</summary>
        private const double Interval = 45.0;

        /// <returns>載入經典大正方形、前景從 0 秒開始計的一關。</returns>
        private static LevelData ClassicLevel()
        {
            Placement[] solution = Fixtures.ClassicSquare();
            var session = new LevelSession();
            session.Load(Puzzle.FromPlacements(solution), solution, difficulty: 1200, allowRoll: true);
            session.Data.StartForegroundTiming(0.0);
            return session.Data;
        }

        /// <summary>照規矩把一片放上盤面：先離開待用區再上盤。</summary>
        /// <param name="level">這一關。</param>
        /// <param name="slotIndex">哪一槽的片。</param>
        /// <param name="placement">放在哪。</param>
        private static void Put(LevelData level, int slotIndex, Placement placement)
        {
            level.SetInTray(slotIndex, false);
            level.Board.Place(slotIndex, placement);
        }

        // ─────────────────────────────────────────────────────────────
        // 一、幾次能按
        // ─────────────────────────────────────────────────────────────

        /// <summary>⌊前景 ÷ 間隔⌋，上限 3；沒按的會累積。</summary>
        [TestCase(0.0, 0)]
        [TestCase(44.9, 0)]
        [TestCase(45.0, 1)]
        [TestCase(89.9, 1)]
        [TestCase(135.0, 3)]
        [TestCase(1000.0, 3)]
        public void 次數_照間隔累積而且上限三次(double nowSeconds, int expected)
        {
            Assert.AreEqual(expected, ClassicLevel().HintsAvailableAt(nowSeconds, Interval));
        }

        [Test]
        public void 次數_用掉的要扣掉()
        {
            LevelData level = ClassicLevel();
            Assert.IsTrue(level.TryUseHint(100.0, Interval, out _));
            Assert.AreEqual(1, level.HintsAvailableAt(100.0, Interval), "90 秒開放兩次、用了一次");
            Assert.AreEqual(2, level.HintsAvailableAt(140.0, Interval), "135 秒開放三次、用了一次");

            Assert.IsTrue(level.TryUseHint(140.0, Interval, out _));
            Assert.IsTrue(level.TryUseHint(140.0, Interval, out _));
            Assert.IsFalse(level.TryUseHint(10000.0, Interval, out int slot), "三次用完就沒了");
            Assert.AreEqual(-1, slot);
            Assert.AreEqual(3, level.HintsUsed);
        }

        [Test]
        public void 次數_還沒到就按不了而且不記任何東西()
        {
            LevelData level = ClassicLevel();
            Assert.IsFalse(level.CanUseHintAt(30.0, Interval));
            Assert.IsTrue(level.CanUseHintAt(45.0, Interval), "對照：到了就能按");
            Assert.IsFalse(level.TryUseHint(30.0, Interval, out _));
            Assert.AreEqual(0, level.HintsUsed);
            Assert.AreEqual(0.0, level.FirstHintForegroundSeconds);
        }

        /// <summary>結束了（放棄或過關）就是 0——放棄那一幀彈窗還沒蓋上來，玩家還按得到。</summary>
        [Test]
        public void 次數_這一局結束之後是零()
        {
            LevelData abandoned = ClassicLevel();
            abandoned.MarkAbandoned(200.0);
            Assert.AreEqual(0, abandoned.HintsAvailableAt(200.0, Interval));
            Assert.IsFalse(abandoned.TryUseHint(200.0, Interval, out _));

            LevelData solved = ClassicLevel();
            solved.MarkSolved();
            Assert.AreEqual(0, solved.HintsAvailableAt(200.0, Interval));
        }

        /// <summary>出題工具的空方框沒有參考解，不能有提示。</summary>
        [Test]
        public void 次數_沒有參考解是零()
        {
            var session = new LevelSession();
            session.LoadBoxOnly(Region.FromPuzzle(Puzzle.FromPlacements(Fixtures.ClassicSquare()), ExactVector2.Zero));
            session.Data.StartForegroundTiming(0.0);

            Assert.AreEqual(0, session.Data.HintsAvailableAt(1000.0, Interval));
            Assert.IsFalse(session.Data.IsReferenceSlotSatisfied(0));
        }

        [TestCase(0.0)]
        [TestCase(-5.0)]
        [TestCase(double.NaN)]
        public void 次數_間隔不是正數要大聲(double interval)
        {
            Assert.Throws<ArgumentOutOfRangeException>(() => ClassicLevel().HintsAvailableAt(100.0, interval));
        }

        // ─────────────────────────────────────────────────────────────
        // 二、挑哪一片
        // ─────────────────────────────────────────────────────────────

        /// <summary>空盤面：大三角兩片（槽號小的先），再來是面積 4 裡槽號最小的中三角。</summary>
        [Test]
        public void 挑片_空盤面照面積大小與槽號()
        {
            LevelData level = ClassicLevel();

            Assert.IsTrue(level.TryUseHint(200.0, Interval, out int first));
            Assert.IsTrue(level.TryUseHint(200.0, Interval, out int second));
            Assert.IsTrue(level.TryUseHint(200.0, Interval, out int third));

            Assert.AreEqual(0, first);
            Assert.AreEqual(1, second, "提示過的不能再挑一次");
            Assert.AreEqual(2, third);
            CollectionAssert.AreEqual(new[] { 0, 1, 2 }, level.HintedReferenceSlots, "照按的順序記");
        }

        [Test]
        public void 挑片_已經擺對的跳過()
        {
            LevelData level = ClassicLevel();
            Put(level, 0, level.ReferenceSolution[0]);
            Put(level, 1, level.ReferenceSolution[1]);

            Assert.IsTrue(level.TryUseHint(50.0, Interval, out int slot));
            Assert.AreEqual(2, slot, "兩片大三角都擺對了，要往下挑");
        }

        /// <summary>
        /// ⭐ <b>位置空的優先，比面積優先</b>：中三角被推去壓住大三角 0 的位置，
        /// 所以先挑空著的大三角 1、再挑空著的中三角位置，被壓住的大三角 0 排最後。
        /// </summary>
        [Test]
        public void 挑片_位置空的比面積大的優先()
        {
            LevelData level = ClassicLevel();

            // 中三角 (2,4)(4,2)(4,4) 平移 (−1,−3)（方格單位 ×√2）→ (1,1)(3,−1)(3,1)：
            // 跟大三角 0 的內部重疊，跟大三角 1、平行四邊形、小三角 4 只有點或邊接觸
            var shift = new ExactVector2(Scalar.FromRoot2Multiple(-1), Scalar.FromRoot2Multiple(-3));
            Put(level, 2, level.ReferenceSolution[2].Translated(shift));

            Assert.IsTrue(level.TryUseHint(200.0, Interval, out int first));
            Assert.IsTrue(level.TryUseHint(200.0, Interval, out int second));
            Assert.IsTrue(level.TryUseHint(200.0, Interval, out int third));

            Assert.AreEqual(1, first, "空著的大三角");
            Assert.AreEqual(2, second, "空著的面積 4 贏過被佔住的面積 8");
            Assert.AreEqual(5, third, "空位還有正方形、平行四邊形、兩片小三角，被佔住的大三角 0 排在它們後面");
        }

        /// <summary>所有位置都被佔住時照樣挑——蓋上去本身就是「這裡擺錯了」的訊號。</summary>
        [Test]
        public void 挑片_沒有空位也照樣挑()
        {
            LevelData level = ClassicLevel();

            // 七片整組往右推半格：每一個參考解的位置都跟某一片重疊，但沒有一片擺對
            var shift = new ExactVector2(Scalar.FromRoot2Multiple(1), Scalar.Zero);
            for (int slotIndex = 0; slotIndex < 7; slotIndex++)
                Put(level, slotIndex, level.ReferenceSolution[slotIndex].Translated(shift));

            Assert.IsTrue(level.TryUseHint(50.0, Interval, out int slot));
            Assert.AreEqual(0, slot, "都被佔住就只剩面積與槽號");
        }

        /// <summary>一個候選都沒有時不扣次數（按了沒反應還被算一次最糟）。</summary>
        [Test]
        public void 挑片_沒有候選不扣次數()
        {
            LevelData level = ClassicLevel();
            for (int slotIndex = 0; slotIndex < 7; slotIndex++)
                Put(level, slotIndex, level.ReferenceSolution[slotIndex]);

            Assert.IsFalse(level.CanUseHintAt(200.0, Interval), "次數還有，但沒東西可提示——鈕要退場");
            Assert.IsFalse(level.TryUseHint(200.0, Interval, out int slot));
            Assert.AreEqual(-1, slot);
            Assert.AreEqual(0, level.HintsUsed);
            Assert.AreEqual(0.0, level.FirstHintForegroundSeconds);
        }

        [Test]
        public void 挑片_第一次的前景秒數只記第一次()
        {
            LevelData level = ClassicLevel();
            Assert.IsTrue(level.TryUseHint(50.0, Interval, out _));
            Assert.IsTrue(level.TryUseHint(100.0, Interval, out _));
            Assert.AreEqual(50.0, level.FirstHintForegroundSeconds, 1e-9);
        }

        /// <summary>清空與 undo 都不退提示：已經用掉了（2026-09-14 定）。</summary>
        [Test]
        public void 挑片_清空與undo之後提示留著()
        {
            LevelData level = ClassicLevel();
            level.PushUndoStep(level.Board[5]);
            Put(level, 5, level.ReferenceSolution[5]);
            Assert.IsTrue(level.TryUseHint(50.0, Interval, out _));

            Assert.IsTrue(level.TryUndo(out _));
            Assert.AreEqual(1, level.HintsUsed, "undo 不退提示");

            Put(level, 5, level.ReferenceSolution[5]);
            Assert.IsTrue(level.TryClearBoardToTray(out _));
            Assert.AreEqual(1, level.HintsUsed, "清空不退提示");
            CollectionAssert.AreEqual(new[] { 0 }, level.HintedReferenceSlots);
        }

        // ─────────────────────────────────────────────────────────────
        // 三、算不算擺對
        // ─────────────────────────────────────────────────────────────

        [Test]
        public void 擺對_同片種的另一片放上去也算()
        {
            LevelData level = ClassicLevel();
            Put(level, 1, level.ReferenceSolution[0]);     // 大三角 1 放在大三角 0 的位置
            Put(level, 4, level.ReferenceSolution[3]);     // 小三角同理

            Assert.IsTrue(level.IsReferenceSlotSatisfied(0));
            Assert.IsTrue(level.IsReferenceSlotSatisfied(3));
            Assert.IsFalse(level.IsReferenceSlotSatisfied(1), "大三角 1 的位置沒人放");
        }

        /// <summary>⚠️ 正方形換一個旋轉值、佔一模一樣的位置——比 <c>Placement.Equals</c> 的話這裡會錯。</summary>
        [Test]
        public void 擺對_姿態欄位不同但佔位一樣也算()
        {
            LevelData level = ClassicLevel();
            Placement reference = level.ReferenceSolution[5];
            Placement samePlace = SameVerticesDifferentRotation(reference);

            Assert.AreNotEqual(reference, samePlace, "前置：姿態欄位要真的不一樣，否則這個測試沒有意義");
            Put(level, 5, samePlace);
            Assert.IsTrue(level.IsReferenceSlotSatisfied(5));
        }

        [Test]
        public void 擺對_不同片種或位置差一點都不算()
        {
            LevelData level = ClassicLevel();
            var shift = new ExactVector2(Scalar.FromRoot2Multiple(1), Scalar.Zero);
            Put(level, 0, level.ReferenceSolution[0].Translated(shift));

            Assert.IsFalse(level.IsReferenceSlotSatisfied(0), "位置不一樣");
            Assert.IsFalse(level.IsReferenceSlotSatisfied(2), "空的");
        }

        /// <summary>還拖在手上（盤面還沒登記）不算——提示要等鬆手才消失。</summary>
        [Test]
        public void 擺對_不在盤上不算()
        {
            LevelData level = ClassicLevel();
            level.SetInTray(0, false);
            Assert.IsFalse(level.IsReferenceSlotSatisfied(0));
        }

        /// <summary>
        /// 提示片跟七片同一層：<b>蓋在所有沒在拖的片上面、拖著的片下面</b>（2026-09-15 起拖曳中的片要壓得過提示）。
        /// 有人改了 <c>TierStride</c> 或面積階數而擠壞這個夾縫時會紅。
        /// </summary>
        [Test]
        public void 排序_提示夾在未吸附與拖曳中之間()
        {
            int highestNotDragged = int.MinValue;
            int lowestDragged = int.MaxValue;
            foreach (PieceKind kind in (PieceKind[])Enum.GetValues(typeof(PieceKind)))
            {
                for (int slotIndex = 0; slotIndex < BoardState.PieceCount; slotIndex++)
                {
                    highestNotDragged = Math.Max(highestNotDragged, Math.Max(
                        PieceSorting.SortingOrder(kind, slotIndex, isSnapped: false, isBeingDragged: false),
                        PieceSorting.SortingOrder(kind, slotIndex, isSnapped: true, isBeingDragged: false)));
                    lowestDragged = Math.Min(lowestDragged, Math.Min(
                        PieceSorting.SortingOrder(kind, slotIndex, isSnapped: false, isBeingDragged: true),
                        PieceSorting.SortingOrder(kind, slotIndex, isSnapped: true, isBeingDragged: true)));
                }
            }

            Assert.Greater(PieceSorting.HintSortingOrder, highestNotDragged, "提示要蓋在沒在拖的片上面");
            Assert.Less(PieceSorting.HintSortingOrder + LevelData.MaximumHints - 1, lowestDragged, "拖著的片要蓋在提示上面");
        }

        [Test]
        public void 幾何_頂點集合要連位置一起比()
        {
            Placement reference = Fixtures.ClassicSquare()[0];
            var shift = new ExactVector2(Scalar.FromRoot2Multiple(1), Scalar.Zero);

            Assert.IsTrue(Geometry.SameVertexSet(reference.Vertices(), reference.Vertices()));
            Assert.IsFalse(Geometry.SameVertexSet(reference.Vertices(), reference.Translated(shift).Vertices()));
            Assert.IsTrue(Geometry.SameVerticesIgnoringPosition(reference.Vertices(), reference.Translated(shift).Vertices()),
                          "對照：不看位置的那一支會說一樣");
        }

        /// <param name="reference">要找替身的擺放。</param>
        /// <returns>旋轉值不一樣、頂點集合一模一樣的擺放。</returns>
        private static Placement SameVerticesDifferentRotation(Placement reference)
        {
            ExactVector2[] target = reference.Vertices();
            for (int rotation = 0; rotation < 8; rotation++)
            {
                if (rotation == reference.Rotation) continue;

                Placement atOrigin;
                ExactVector2[] vertices;
                try
                {
                    atOrigin = new Placement(reference.Kind, rotation, reference.IsFlipped, ExactVector2.Zero);
                    vertices = atOrigin.Vertices();
                }
                catch (InvalidOperationException)
                {
                    continue;       // 這個姿態離開了 ℤ[√2]
                }

                foreach (ExactVector2 anchor in target)
                {
                    Placement moved = atOrigin.Translated(anchor - vertices[0]);
                    if (Geometry.SameVertexSet(moved.Vertices(), target)) return moved;
                }
            }
            throw new InvalidOperationException($"{reference.Kind} 找不到佔位一樣的其他旋轉值");
        }
    }
}
