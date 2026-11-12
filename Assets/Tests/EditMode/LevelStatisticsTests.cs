using NUnit.Framework;
using Tangram.Core;
using Tangram.Data;

namespace Tangram.Core.Tests
{
    /// <summary>
    /// 局內統計：ELO 的輸入。
    ///
    /// ⚠️ <b>全部離線</b>——時刻由呼叫端傳進來，所以這裡傳的是假時間，一次 Unity 都不用開。
    /// 而「用哪個時鐘」整個住在 <c>GameObject</c> 層，換時鐘不影響這一整份測試。
    /// </summary>
    public class LevelStatisticsTests
    {
        /// <returns>載好經典大正方形的一場。</returns>
        private static LevelData FreshLevel()
        {
            var session = new LevelSession();
            Placement[] solution = Fixtures.ClassicSquare();
            session.Load(Puzzle.FromPlacements(solution), solution, difficulty: 1200, allowRoll: true);
            return session.Data;
        }

        // ─────────────────────────────────────────────────────────────
        // 一、三條冪等規則
        // ─────────────────────────────────────────────────────────────

        /// <summary>
        /// <b>規則①</b>：已經在計時再叫「開始」→ 什麼都不做。
        ///
        /// ⚠️ 少了它，第二次「開始」會**蓋掉第一段的開始時刻**，
        /// 從 100 到 110 那十秒永遠漏掉，而且不報錯。
        /// </summary>
        [Test]
        public void 冪等_連續兩次開始不會蓋掉第一段的開始時刻()
        {
            LevelData level = FreshLevel();

            level.StartDragTiming(100.0);
            level.StartDragTiming(110.0);     // 這一次必須什麼都不做
            level.StopDragTiming(102.0);

            Assert.AreEqual(2.0, level.DragSeconds, 1e-9, "算的必須是 100→102，不是 110→102");
            Assert.AreEqual(1, level.DragCount, "第二次「開始」不算一次拖曳");
        }

        /// <summary>
        /// <b>規則②</b>：沒在計時叫「停止」→ 什麼都不做。
        /// 💡 這條讓「兩個出口都叫停止」變成安全的，所以呼叫端不用挑一邊。
        /// </summary>
        [Test]
        public void 冪等_沒在計時就停止不會有事()
        {
            LevelData level = FreshLevel();

            level.StopDragTiming(100.0);
            level.StopForegroundTiming(100.0);

            Assert.AreEqual(0.0, level.DragSeconds, 1e-9);
            Assert.AreEqual(0.0, level.ForegroundSeconds, 1e-9);
            Assert.AreEqual(0, level.DragCount);
        }

        /// <summary>
        /// <b>規則②的真正用途</b>：同一段拖曳被停兩次（`EndPress` 與 `CancelInteraction`
        /// 都會叫），第二次不能重複累加。
        /// </summary>
        [Test]
        public void 冪等_同一段拖曳停兩次只算一次()
        {
            LevelData level = FreshLevel();

            level.StartDragTiming(100.0);
            level.StopDragTiming(101.0);
            level.StopDragTiming(105.0);      // 另一個出口也叫了

            Assert.AreEqual(1.0, level.DragSeconds, 1e-9);
        }

        /// <summary>
        /// <b>規則③</b>：這一局結束之後，所有計時請求一律忽略。
        ///
        /// ⚠️ 放棄的那一幀彈窗還沒出現（`Update` 下一幀才跑），**玩家還碰得到螢幕**。
        /// 他剛好按下一片的話，拖曳計時會重新開始，而這一局其實已經結束了。
        /// </summary>
        [Test]
        public void 冪等_放棄之後再操作不會繼續累加()
        {
            LevelData level = FreshLevel();
            level.StartForegroundTiming(0.0);

            level.MarkAbandoned(50.0);

            level.StartDragTiming(51.0);
            level.StopDragTiming(53.0);
            level.StartForegroundTiming(51.0);
            level.StopForegroundTiming(60.0);

            Assert.AreEqual(0.0, level.DragSeconds, 1e-9, "放棄之後的拖曳不能算");
            Assert.AreEqual(0, level.DragCount);
            Assert.AreEqual(50.0, level.ForegroundSeconds, 1e-9, "前景停在放棄那一刻");
        }

        // ─────────────────────────────────────────────────────────────
        // 二、累加與上限
        // ─────────────────────────────────────────────────────────────

        /// <summary>幾段拖曳加起來，而且次數對得上。</summary>
        [Test]
        public void 拖曳_多段累加而且次數對得上()
        {
            LevelData level = FreshLevel();

            level.StartDragTiming(10.0);
            level.StopDragTiming(11.5);
            level.StartDragTiming(20.0);
            level.StopDragTiming(20.5);

            Assert.AreEqual(2.0, level.DragSeconds, 1e-9);
            Assert.AreEqual(2, level.DragCount);
        }

        /// <summary>
        /// ⭐ <b>單次拖曳的上限</b>：超過 3 秒的部分不算拖曳。
        /// 那一段是「拿著片在想」，而思考時間是「前景 − 拖曳」算出來的，
        /// <b>所以超出的部分自動落到思考那邊</b>，不用另外存。
        /// </summary>
        [Test]
        public void 拖曳_單次超過上限的部分算進思考時間()
        {
            LevelData level = FreshLevel();

            level.StartForegroundTiming(0.0);
            level.StartDragTiming(10.0);
            level.StopDragTiming(30.0);        // 拖了 20 秒，只能算 3 秒
            level.StopForegroundTiming(40.0);

            Assert.AreEqual(LevelData.MaximumSingleDragSeconds, level.DragSeconds, 1e-9);
            Assert.AreEqual(40.0, level.ForegroundSeconds, 1e-9);
            Assert.AreEqual(37.0, level.ThinkingSeconds, 1e-9, "那 17 秒要落在思考時間裡");
        }

        /// <summary>剛好等於上限的那一次不該被砍。</summary>
        [Test]
        public void 拖曳_剛好等於上限不砍()
        {
            LevelData level = FreshLevel();

            level.StartDragTiming(10.0);
            level.StopDragTiming(10.0 + LevelData.MaximumSingleDragSeconds);

            Assert.AreEqual(LevelData.MaximumSingleDragSeconds, level.DragSeconds, 1e-9);
        }

        /// <summary>
        /// 背景那段不算前景：進關 → 切背景 → 回來 → 過關。
        /// </summary>
        [Test]
        public void 前景_背景那一段不算進去()
        {
            LevelData level = FreshLevel();

            level.StartForegroundTiming(0.0);
            level.StopAllTiming(10.0);         // 切到背景
            level.StartForegroundTiming(70.0); // 六十秒後回來
            level.StopAllTiming(80.0);

            Assert.AreEqual(20.0, level.ForegroundSeconds, 1e-9, "中間那 60 秒不能算");
        }

        // ─────────────────────────────────────────────────────────────
        // 三、那條免費的不變式
        // ─────────────────────────────────────────────────────────────

        /// <summary>
        /// ⭐ <b>拖曳時間 ≤ 前景時間，永遠</b>——背景時不可能拖曳，而前景涵蓋整關。
        /// 所以<b>思考時間為負在結構上不可能發生</b>，一旦為負就代表某個呼叫點接錯了。
        ///
        /// 這裡照真實的呼叫順序跑一輪（進關 → 拖三次 → 切背景 → 回來 → 再拖 → 過關）。
        /// </summary>
        [Test]
        public void 思考時間_永遠不為負()
        {
            LevelData level = FreshLevel();

            level.StartForegroundTiming(0.0);

            level.StartDragTiming(1.0);
            level.StopDragTiming(2.0);
            level.StartDragTiming(3.0);
            level.StopDragTiming(3.4);
            level.StartDragTiming(5.0);
            level.StopDragTiming(5.2);

            level.StopAllTiming(6.0);            // 切背景
            level.StartForegroundTiming(100.0);  // 回來

            level.StartDragTiming(101.0);
            level.StopDragTiming(101.8);

            level.StopAllTiming(110.0);          // 過關

            Assert.IsTrue(level.DragSeconds <= level.ForegroundSeconds,
                $"拖曳 {level.DragSeconds} 不該大於前景 {level.ForegroundSeconds}");
            Assert.IsTrue(level.ThinkingSeconds >= 0.0);
            Assert.AreEqual(4, level.DragCount);
        }

        /// <summary>時刻倒退時不能讓累積變小——那代表呼叫端傳錯了，不該安靜地污染資料。</summary>
        [Test]
        public void 累積_時刻倒退不會讓數字變小()
        {
            LevelData level = FreshLevel();

            level.StartDragTiming(100.0);
            level.StopDragTiming(90.0);          // 倒退

            Assert.AreEqual(0.0, level.DragSeconds, 1e-9);
        }

        // ─────────────────────────────────────────────────────────────
        // 四、放棄與互斥
        // ─────────────────────────────────────────────────────────────

        /// <summary>放棄會把兩個計時都停掉。</summary>
        [Test]
        public void 放棄_兩個計時都停掉()
        {
            LevelData level = FreshLevel();

            level.StartForegroundTiming(0.0);
            level.StartDragTiming(5.0);

            Assert.IsTrue(level.MarkAbandoned(6.0));

            Assert.IsTrue(level.IsAbandoned);
            Assert.IsTrue(level.IsFinished);
            Assert.AreEqual(1.0, level.DragSeconds, 1e-9);
            Assert.AreEqual(6.0, level.ForegroundSeconds, 1e-9);
        }

        /// <summary>
        /// ⚠️ <b>互斥①</b>：已經過關就不能再放棄。
        /// </summary>
        [Test]
        public void 互斥_過關之後放棄不做事()
        {
            LevelData level = FreshLevel();
            level.MarkSolved();

            Assert.IsFalse(level.MarkAbandoned(10.0));
            Assert.IsFalse(level.IsAbandoned);
        }

        /// <summary>
        /// ⚠️ <b>互斥②</b>：已經放棄就不能再過關。
        /// 不擋的話這一局同時是「放棄」與「過關」，而 ELO 會把它算成兩件事。
        /// </summary>
        [Test]
        public void 互斥_放棄之後填滿盤面也不算過關()
        {
            var session = new LevelSession();
            Placement[] solution = Fixtures.ClassicSquare();
            session.Load(Puzzle.FromPlacements(solution), solution, difficulty: 1200, allowRoll: true);
            LevelData level = session.Data;

            level.MarkAbandoned(1.0);

            // 照參考解把七片都放上去（留一個空位再補，用的是現成的那支）
            Assert.IsTrue(level.TryFillFromPlacements(solution, out _));

            Assert.IsFalse(level.MarkSolvedIfAllOnBoard(), "放棄過的一局不能再過關");
            Assert.IsFalse(level.IsSolved);
        }

        /// <summary>放棄兩次，第二次不做事。</summary>
        [Test]
        public void 放棄_第二次不做事()
        {
            LevelData level = FreshLevel();

            Assert.IsTrue(level.MarkAbandoned(10.0));
            Assert.IsFalse(level.MarkAbandoned(20.0));
        }

        // ─────────────────────────────────────────────────────────────
        // 五、換關
        // ─────────────────────────────────────────────────────────────

        /// <summary>
        /// 換關之後統計歸零。⭐ <b>這是把真相放 `LevelData` 換來的</b>——
        /// <c>LevelSession</c> 整包換掉，統計自動跟著換，<b>沒有人需要記得清它</b>。
        /// </summary>
        [Test]
        public void 換關_統計自動歸零()
        {
            var session = new LevelSession();
            Placement[] solution = Fixtures.ClassicSquare();
            session.Load(Puzzle.FromPlacements(solution), solution, difficulty: 1200, allowRoll: true);

            session.Data.StartForegroundTiming(0.0);
            session.Data.StartDragTiming(1.0);
            session.Data.StopDragTiming(2.0);
            session.Data.RecordClear();
            session.Data.MarkAbandoned(3.0);

            session.Load(Puzzle.FromPlacements(solution), solution, difficulty: 1200, allowRoll: true);

            Assert.AreEqual(0.0, session.Data.DragSeconds, 1e-9);
            Assert.AreEqual(0.0, session.Data.ForegroundSeconds, 1e-9);
            Assert.AreEqual(0, session.Data.DragCount);
            Assert.AreEqual(0, session.Data.ClearCount);
            Assert.IsFalse(session.Data.IsAbandoned);
            Assert.IsFalse(session.Data.IsFinished);
        }

        /// <summary>
        /// ⚠️ <b>ThinkingSeconds 在遊玩途中會是負的</b>——前景只在「停止」時才累加，
        /// 而拖曳已經有值。那不是 bug，是它只給「一局結束後的 ELO」讀的。
        /// 遊玩途中要看就得用 <c>ThinkingSecondsAt</c>。這條測試把兩者的差別釘住，
        /// 免得有人看到負數就去「修」那個減法。
        /// </summary>
        [Test]
        public void 思考時間_遊玩途中要用現算的那一支()
        {
            LevelData level = FreshLevel();

            level.StartForegroundTiming(0.0);
            level.StartDragTiming(1.0);
            level.StopDragTiming(3.0);

            Assert.AreEqual(-2.0, level.ThinkingSeconds, 1e-9,
                "停止累加之前，存下來的那個版本本來就會是負的");
            Assert.AreEqual(8.0, level.ThinkingSecondsAt(10.0), 1e-9,
                "現算的那一支要含還在跑的前景：10 − 2 = 8");
        }

        /// <summary>現算的前景含還在跑的那一段。</summary>
        [Test]
        public void 前景_現算的含還在跑的那一段()
        {
            LevelData level = FreshLevel();

            Assert.AreEqual(0.0, level.ForegroundSecondsAt(5.0), 1e-9, "還沒開始計就是 0");

            level.StartForegroundTiming(10.0);
            Assert.AreEqual(4.0, level.ForegroundSecondsAt(14.0), 1e-9);
            Assert.AreEqual(0.0, level.ForegroundSeconds, 1e-9, "存下來的那個還沒動");

            level.StopForegroundTiming(14.0);
            Assert.AreEqual(4.0, level.ForegroundSeconds, 1e-9);
        }

        /// <summary>清空次數記得起來。</summary>
        [Test]
        public void 清空_次數記得起來()
        {
            LevelData level = FreshLevel();

            level.RecordClear();
            level.RecordClear();

            Assert.AreEqual(2, level.ClearCount);
        }
    }
}
