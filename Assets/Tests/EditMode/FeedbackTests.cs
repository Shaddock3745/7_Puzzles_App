using System;
using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using Tangram.Data;
using Tangram.Data.Tests;
using UnityEngine;

namespace Tangram.Core.Tests
{
    /// <summary>
    /// 回饋收集（2026-09-13）：出題順序、一位受試者的進度守衛、寫進檔案的那一行。
    ///
    /// ⚠️ <b>這裡寫錯的症狀是資料少一行或多一行、欄位對不上</b>——只有收完資料打開檔案才看得到，
    /// 那時候受試者早就走了。所以控制器只負責開關面板與寫檔，判斷全放在這裡驗。
    /// </summary>
    public class FeedbackTests
    {
        private const int PlanSeed = 20260914;

        /// <param name="participantNumber">受試者編號。</param>
        /// <returns>一位新的受試者。</returns>
        private static FeedbackSession NewSession(int participantNumber = 1) =>
            new FeedbackSession(participantNumber, TestProfile(), PlanSeed, TestFingerprint);

        /// <returns>一份合法的受試者資料。</returns>
        private static FeedbackParticipantProfile TestProfile() =>
            new FeedbackParticipantProfile("A01", 1, AgeGroup.NineToTwelve, TangramExperience.Never, WatchedOthers.DidNotWatch);

        /// <summary>測試用的權重指紋。內容無所謂，只要不是空的。</summary>
        private const string TestFingerprint = "0123456789ab";

        /// <param name="solution">參考解。</param>
        /// <param name="difficulty">這一題的難度。</param>
        /// <returns>載好這一題的一場。</returns>
        private static LevelSession LoadedSession(Placement[] solution, int difficulty = 1234)
        {
            var session = new LevelSession();
            session.Load(Puzzle.FromPlacements(solution), solution, difficulty, allowRoll: false);
            return session;
        }

        /// <summary>照參考解擺滿、過關、停計時——跟真的遊戲一樣的順序。</summary>
        /// <param name="level">這一關。</param>
        /// <param name="foregroundSeconds">玩了幾秒。</param>
        private static void Solve(LevelData level, double foregroundSeconds)
        {
            level.StartForegroundTiming(0.0);
            for (int slotIndex = 0; slotIndex < BoardState.PieceCount; slotIndex++)
                level.Board.Place(slotIndex, level.ReferenceSolution[slotIndex]);
            Assert.IsTrue(level.MarkSolvedIfAllOnBoard());
            level.StopAllTiming(foregroundSeconds);
        }

        /// <returns>一場已經過關的經典大正方形。</returns>
        private static LevelData SolvedClassicSquare()
        {
            LevelData level = LoadedSession(Fixtures.ClassicSquare()).Data;
            Solve(level, 42.0);
            return level;
        }

        /// <summary>
        /// 一位<b>已經做完練習題</b>的受試者——正式題的測試都從這裡開始，
        /// 因為沒做練習之前正式題一律不收（那條另外驗）。
        /// </summary>
        /// <param name="participantNumber">受試者編號。</param>
        /// <returns>練習做完的受試者。</returns>
        private static FeedbackSession SessionPastPractice(int participantNumber = 1)
        {
            FeedbackSession session = NewSession(participantNumber);
            LevelData practice = SolvedClassicSquare();
            Assert.IsTrue(session.TryCreatePracticeRecord(practice, DateTime.UtcNow, "test", out _));
            Assert.IsTrue(session.MarkPracticeDone(practice));
            return session;
        }

        // ─────────────────────────────────────────────────────────────
        // 一、出題順序
        // ─────────────────────────────────────────────────────────────

        /// <summary>每一位受試者的順序都是 0 ～ 4 的排列：不重複、不漏題。</summary>
        [Test]
        public void 順序_永遠是題單索引的排列()
        {
            foreach (int seed in new[] { PlanSeed, 0, -7, int.MaxValue })
                for (int participant = 1; participant <= 50; participant++)
                {
                    int[] order = FeedbackPlan.OrderFor(seed, participant);
                    CollectionAssert.AreEquivalent(Enumerable.Range(0, FeedbackPlan.PuzzleCount), order,
                        $"種子 {seed}、第 {participant} 位的順序不是排列：{string.Join(",", order)}");
                }
        }

        /// <summary>⭐ <b>可重現</b>：同一個種子、同一個編號永遠是同一個順序，事後查得出他當時的題序。</summary>
        [Test]
        public void 順序_同一個編號永遠一樣()
        {
            CollectionAssert.AreEqual(FeedbackPlan.OrderFor(PlanSeed, 3), FeedbackPlan.OrderFor(PlanSeed, 3));
        }

        /// <summary>
        /// 真的有打散：30 位受試者裡要有很多種順序。
        /// 全部一樣的話等於固定順序，學習效應會系統性地壓低後面那幾題的難度感受。
        /// </summary>
        [Test]
        public void 順序_不同編號會拿到不同順序()
        {
            var distinct = new HashSet<string>();
            for (int participant = 1; participant <= 30; participant++)
                distinct.Add(string.Join(",", FeedbackPlan.OrderFor(PlanSeed, participant)));

            Assert.GreaterOrEqual(distinct.Count, 15, $"30 位只有 {distinct.Count} 種順序，打散得不夠");
        }

        /// <summary>
        /// 每一格被排到第一題的次數不能太偏。一百位裡某一題從來不是第一題的話，
        /// 那一題永遠吃不到「剛開始、還不熟」的狀態。
        /// </summary>
        [Test]
        public void 順序_每一題都有機會排第一()
        {
            var firstCounts = new int[FeedbackPlan.PuzzleCount];
            for (int participant = 1; participant <= 100; participant++)
                firstCounts[FeedbackPlan.OrderFor(PlanSeed, participant)[0]]++;

            foreach (int count in firstCounts)
                Assert.Greater(count, 5, $"排第一的次數分布太偏：{string.Join(",", firstCounts)}");
        }

        /// <summary>
        /// ⭐ <b>每個人一批不同的題</b>：第 1 位用基準種子、第 N 位用基準 ＋ N − 1。
        /// 會一次帶一組小朋友來測，在旁邊看的人不能看過同一題。
        /// </summary>
        [TestCase(1, 20260914)]
        [TestCase(2, 20260915)]
        [TestCase(9, 20260922)]
        public void 換批_每個人一個種子(int participantNumber, int expectedSeed)
        {
            Assert.AreEqual(expectedSeed, FeedbackPlan.PlanSeedFor(20260914, participantNumber));
        }

        /// <summary>
        /// 同一組連續的小朋友<b>一定拿到不同的種子</b>——這是改成每人一批的全部理由。
        /// 20 位連續受試者，種子兩兩不同。
        /// </summary>
        [Test]
        public void 換批_連續的受試者種子都不同()
        {
            var seeds = new HashSet<int>();
            for (int participant = 1; participant <= 20; participant++)
                Assert.IsTrue(seeds.Add(FeedbackPlan.PlanSeedFor(PlanSeed, participant)), $"第 {participant} 位跟前面的人撞種子");
        }

        /// <summary>編號從 1 起。</summary>
        [Test]
        public void 換批_編號小於一要丟例外()
        {
            Assert.Throws<ArgumentOutOfRangeException>(() => FeedbackPlan.PlanSeedFor(1, 0));
        }

        // ─────────────────────────────────────────────────────────────
        // 一之二、權重指紋
        // ─────────────────────────────────────────────────────────────

        /// <summary>
        /// ⭐ <b>權重指紋</b>：同一份權重同一個指紋；任何一個會影響難度的數字改了，指紋就變。
        /// 回饋收集分好幾輪、中間可能換權重，靠它分辨每一行的難度值是哪一把尺量的。
        /// </summary>
        [Test]
        public void 指紋_同一份權重相同而且改了數字就變()
        {
            DifficultyWeights original = RepositoryFiles.LoadDifficultyWeights();
            string fingerprint = original.Fingerprint();

            Assert.AreEqual(12, fingerprint.Length);
            Assert.AreEqual(fingerprint, RepositoryFiles.LoadDifficultyWeights().Fingerprint(), "同一份權重讀兩次指紋不一樣");

            DifficultyWeights changedWeight = RepositoryFiles.LoadDifficultyWeights();
            changedWeight.terms[0].weight += 0.01;
            Assert.AreNotEqual(fingerprint, changedWeight.Fingerprint(), "改了權重指紋沒變");

            DifficultyWeights changedScale = RepositoryFiles.LoadDifficultyWeights();
            changedScale.weightedDeviation += 0.000001;
            Assert.AreNotEqual(fingerprint, changedScale.Fingerprint(), "改了換算倍率指紋沒變");
        }

        /// <summary>
        /// ⚠️ <b>目標難度範圍不算進指紋</b>（2026-09-14）：它不改變任何一題的難度值。
        /// 算進去的話，加欄位那一天指紋就變了，之前收的回饋資料會被當成另一把尺量的。
        /// </summary>
        [Test]
        public void 指紋_目標難度範圍不影響()
        {
            DifficultyWeights original = RepositoryFiles.LoadDifficultyWeights();

            DifficultyWeights changedRange = RepositoryFiles.LoadDifficultyWeights();
            changedRange.minimumTargetDifficulty -= 100;
            changedRange.maximumTargetDifficulty += 100;

            Assert.AreEqual(original.Fingerprint(), changedRange.Fingerprint(), "改了夾住範圍指紋就變了");
        }

        /// <summary>
        /// 指紋只看數字：特徵的先後順序、檔案的排版都不影響（公式是加總，順序本來就不影響難度）。
        /// 否則重新排版一次權重檔，就會被當成換了一把尺。
        /// </summary>
        [Test]
        public void 指紋_特徵順序與排版不影響()
        {
            string text = System.IO.File.ReadAllText(RepositoryFiles.Find(RepositoryFiles.DifficultyWeightsRelativePath));
            DifficultyWeights original = DifficultyWeights.FromJson(text);

            DifficultyWeights reordered = DifficultyWeights.FromJson(text);
            Array.Reverse(reordered.terms);
            Assert.AreEqual(original.Fingerprint(), reordered.Fingerprint(), "換了特徵順序指紋就變了");

            DifficultyWeights compact = DifficultyWeights.FromJson(
                System.Text.RegularExpressions.Regex.Replace(text, @"\s+", ""));
            Assert.AreEqual(original.Fingerprint(), compact.Fingerprint(), "拿掉排版指紋就變了");
        }

        /// <summary>沒有權重指紋不能建受試者——空的話換過權重的資料混在一起就分不出來。</summary>
        [Test]
        public void 指紋_受試者一定要帶著指紋()
        {
            Assert.Throws<ArgumentException>(() =>
                new FeedbackSession(1, TestProfile(), PlanSeed, ""));
            Assert.Throws<ArgumentException>(() =>
                new FeedbackSession(1, TestProfile(), PlanSeed, null));
            Assert.Throws<ArgumentNullException>(() =>
                new FeedbackSession(1, null, PlanSeed, TestFingerprint));
        }

        // ─────────────────────────────────────────────────────────────
        // 一之三、受試者代號與「第幾次來」
        // ─────────────────────────────────────────────────────────────

        /// <summary>
        /// 英文名字整理：去前後空白、中間連續空白縮成一個，<b>大小寫照打的樣子</b>（刻意不全轉大寫）。
        /// </summary>
        [TestCase("Amy", "Amy")]
        [TestCase("  amy ", "amy")]
        [TestCase("Mary   Ann", "Mary Ann")]
        [TestCase("O'Neil", "O'Neil")]
        [TestCase("Mary-Jane", "Mary-Jane")]
        [TestCase("Amy2", "Amy2")]
        [TestCase("Maximilian Johnson", "Maximilian Johnson")]
        public void 代號_整理成標準形式而且保留大小寫(string raw, string expected)
        {
            Assert.AreEqual(expected, FeedbackParticipantCode.Normalize(raw));
        }

        /// <summary>
        /// 不合法的回 null：空的、太長、有符號、⚠️ <b>全形字</b>（中文輸入法開著打出來的「Ａｍｙ」
        /// 跟半形看起來一樣，混進去的話同一個人會變兩個名字）、中文名字（這裡要英文名字）。
        /// </summary>
        [TestCase(null)]
        [TestCase("")]
        [TestCase("   ")]
        [TestCase("Bartholomew Montgomery")]
        [TestCase("Amy!")]
        [TestCase("Amy_2")]
        [TestCase("Ａｍｙ")]
        [TestCase("小明")]
        // ⚠️ 下面四個是 2026-09-14 發現「說明跟程式不一致」之後補的：
        // 原本用 string.Trim()，頭尾的全形空白、換行會被刪掉而通過；Tab 被當成空白收進去
        [TestCase("Amy　")]
        [TestCase("　Amy")]
        [TestCase("Amy\n")]
        [TestCase("Mary\tAnn")]
        public void 代號_不合法的回null(string raw)
        {
            Assert.IsNull(FeedbackParticipantCode.Normalize(raw));
        }

        /// <summary>⚠️ 比對不分大小寫：小朋友第一次打「Amy」、第二次打「amy」是同一個人。</summary>
        [Test]
        public void 代號_比對不分大小寫()
        {
            Assert.IsTrue(FeedbackParticipantCode.Comparer.Equals("Amy", "amy"));
            Assert.IsTrue(FeedbackParticipantCode.Comparer.Equals("MARY ANN", "mary ann"));
            Assert.IsFalse(FeedbackParticipantCode.Comparer.Equals("Amy", "Amy2"));
        }

        /// <summary>受試者資料建構時就檢查：代號不合法、第幾次小於 1 都進不了 session。</summary>
        [Test]
        public void 代號_不合法的資料建不起來()
        {
            Assert.Throws<ArgumentException>(() =>
                new FeedbackParticipantProfile("Ａｍｙ", 1, AgeGroup.NineToTwelve, TangramExperience.Never, WatchedOthers.DidNotWatch));
            Assert.Throws<ArgumentOutOfRangeException>(() =>
                new FeedbackParticipantProfile("Amy", 0, AgeGroup.NineToTwelve, TangramExperience.Never, WatchedOthers.DidNotWatch));
            Assert.AreEqual("amy",
                new FeedbackParticipantProfile(" amy ", 1, AgeGroup.NineToTwelve, TangramExperience.Never, WatchedOthers.DidNotWatch).Code,
                "大小寫要照打的樣子");
        }

        /// <summary>
        /// ⭐ <b>「來過幾次」數的是不同的受試者編號，不是行數</b>——一次來有練習加五題共 6 行，數行數的話來一次就變成來六次。
        /// 大小寫不同照樣算同一個人；讀不懂的行、沒有名字的舊資料跳過。
        /// </summary>
        [Test]
        public void 代號_來過幾次數的是不同編號而且不分大小寫()
        {
            string Line(int number, string code) =>
                JsonUtility.ToJson(new FeedbackRecord { participantNumber = number, participantCode = code });

            var lines = new List<string>();
            for (int i = 0; i < 6; i++) lines.Add(Line(1, "Amy"));   // 第 1 位：練習 ＋ 五題
            for (int i = 0; i < 3; i++) lines.Add(Line(4, "amy"));   // 第 4 位：同一個小朋友第二次來，這次打小寫（提早結束，三行）
            lines.Add(Line(2, "Ben"));
            lines.Add(Line(3, null));                                  // 沒有名字（加欄位之前的舊資料）
            lines.Add("{\"participantNumber\": 9, \"partic");          // 當掉時只寫一半
            lines.Add("");

            Dictionary<string, int> visits = FeedbackRecord.CountVisitsByCode(lines, line => JsonUtility.FromJson<FeedbackRecord>(line));

            Assert.AreEqual(2, visits["AMY"], "Amy 來過兩次（編號 1 與 4），不是 9 次；而且查的時候大小寫無所謂");
            Assert.AreEqual(1, visits["ben"]);
            Assert.AreEqual(2, visits.Count, "沒有名字的行不該算成一個名字");
        }

        // ─────────────────────────────────────────────────────────────
        // 二、一位受試者的進度守衛
        // ─────────────────────────────────────────────────────────────

        /// <summary>還在玩的一題不能答。</summary>
        [Test]
        public void 進度_還沒結束不能答()
        {
            LevelData level = LoadedSession(Fixtures.ClassicSquare()).Data;
            FeedbackSession session = SessionPastPractice();

            Assert.IsFalse(session.TryCreateSolvedRecord(level, 3, DateTime.UtcNow, "test", out _));
            Assert.IsFalse(session.MarkAnswered(level));
            Assert.AreEqual(0, session.AnsweredCount);
        }

        /// <summary>⚠️ 計時還在跑不能答——前景秒數只在停止時累加，還在跑就讀會少算。</summary>
        [Test]
        public void 進度_計時還在跑不能答()
        {
            LevelData level = LoadedSession(Fixtures.ClassicSquare()).Data;
            level.StartForegroundTiming(0.0);
            for (int slotIndex = 0; slotIndex < BoardState.PieceCount; slotIndex++)
                level.Board.Place(slotIndex, level.ReferenceSolution[slotIndex]);
            level.MarkSolvedIfAllOnBoard();

            FeedbackSession session = SessionPastPractice();
            Assert.IsFalse(session.TryCreateSolvedRecord(level, 3, DateTime.UtcNow, "test", out _), "計時還在跑");

            level.StopAllTiming(30.0);
            Assert.IsTrue(session.TryCreateSolvedRecord(level, 3, DateTime.UtcNow, "test", out _), "停了之後就能答");
        }

        /// <summary>⭐ <b>同一題只能答一次</b>——手指連點兩下只能寫一行。</summary>
        [Test]
        public void 進度_同一題不能答兩次()
        {
            LevelData level = SolvedClassicSquare();
            FeedbackSession session = SessionPastPractice();

            Assert.IsTrue(session.TryCreateSolvedRecord(level, 2, DateTime.UtcNow, "test", out _));
            Assert.IsTrue(session.MarkAnswered(level));

            Assert.IsFalse(session.TryCreateSolvedRecord(level, 4, DateTime.UtcNow, "test", out _), "同一關第二次不能再組一行");
            Assert.IsFalse(session.MarkAnswered(level), "同一關第二次不能再推進度");
            Assert.AreEqual(1, session.AnsweredCount);
        }

        /// <summary>主觀難度只收 1 ～ 5。</summary>
        [TestCase(0, false)]
        [TestCase(1, true)]
        [TestCase(5, true)]
        [TestCase(6, false)]
        public void 進度_主觀難度只收一到五(int subjectiveDifficulty, bool accepted)
        {
            Assert.AreEqual(accepted,
                SessionPastPractice().TryCreateSolvedRecord(SolvedClassicSquare(), subjectiveDifficulty, DateTime.UtcNow, "test", out _));
        }

        /// <summary>
        /// ⚠️ <b>組出那一行不推進度</b>——寫檔成功之後才推。
        /// 合成一步的話寫檔失敗時進度已經往前，那一題的答案就永遠消失。
        /// </summary>
        [Test]
        public void 進度_組出那一行不會推進度()
        {
            LevelData level = SolvedClassicSquare();
            FeedbackSession session = SessionPastPractice();

            Assert.IsTrue(session.TryCreateSolvedRecord(level, 3, DateTime.UtcNow, "test", out FeedbackRecord first));
            Assert.IsTrue(session.TryCreateSolvedRecord(level, 3, DateTime.UtcNow, "test", out FeedbackRecord retry),
                "寫檔失敗時要能重試");
            Assert.AreEqual(0, session.AnsweredCount);
            Assert.AreEqual(first.sequenceNumber, retry.sequenceNumber);
        }

        /// <summary>沒有題目的空方框（出題工具那種）不能答。</summary>
        [Test]
        public void 進度_沒有題目的空方框不能答()
        {
            var levelSession = new LevelSession();
            levelSession.LoadBoxOnly(Region.FromPolygon(new[]
            {
                new ExactVector2(0, 0), new ExactVector2(12, 0), new ExactVector2(12, 12), new ExactVector2(0, 12),
            }));
            levelSession.Data.MarkAbandoned(10.0);

            Assert.IsFalse(SessionPastPractice().TryCreateAbandonedRecord(levelSession.Data, AbandonReason.TooHard,
                                                                          DateTime.UtcNow, "test", out _));
        }

        // ─────────────────────────────────────────────────────────────
        // 二之二、依結局分兩條（2026-09-14）
        // ─────────────────────────────────────────────────────────────

        /// <returns>一場已經放棄、計時也停了的經典大正方形。</returns>
        private static LevelData AbandonedClassicSquare()
        {
            LevelData level = LoadedSession(Fixtures.ClassicSquare()).Data;
            level.StartForegroundTiming(0.0);
            level.MarkAbandoned(135.0);
            return level;
        }

        /// <summary>
        /// ⭐ <b>過關的題不收放棄原因、放棄的題不收難不難</b>。
        /// 場景接錯線的時候資料要「寫不進去」而不是「寫錯」——「放棄＋超簡單」那種行只有收完打開檔案才看得到。
        /// </summary>
        [Test]
        public void 結局_答案要跟結局對得上()
        {
            FeedbackSession session = SessionPastPractice();

            Assert.IsFalse(session.TryCreateAbandonedRecord(SolvedClassicSquare(), AbandonReason.TooHard,
                                                            DateTime.UtcNow, "test", out _), "過關的題收了放棄原因");
            Assert.IsFalse(session.TryCreateSolvedRecord(AbandonedClassicSquare(), 1, DateTime.UtcNow, "test", out _),
                           "放棄的題收了難不難");
        }

        /// <summary>還在玩的題兩條都不收（結局那條不能讓「還沒結束」溜過去）。</summary>
        [Test]
        public void 結局_還沒結束兩條都不收()
        {
            LevelData level = LoadedSession(Fixtures.ClassicSquare()).Data;
            FeedbackSession session = SessionPastPractice();

            Assert.IsFalse(session.TryCreateSolvedRecord(level, 3, DateTime.UtcNow, "test", out _));
            Assert.IsFalse(session.TryCreateAbandonedRecord(level, AbandonReason.TooHard, DateTime.UtcNow, "test", out _));
        }

        /// <summary>放棄原因只收列舉裡的三種——按鈕的 onClick 參數打錯時不能寫進去。</summary>
        [TestCase(0, true)]
        [TestCase(2, true)]
        [TestCase(3, false)]
        [TestCase(-1, false)]
        public void 結局_放棄原因只收三種(int reasonIndex, bool accepted)
        {
            Assert.AreEqual(accepted, SessionPastPractice().TryCreateAbandonedRecord(
                AbandonedClassicSquare(), (AbandonReason)reasonIndex, DateTime.UtcNow, "test", out _));
        }

        /// <summary>
        /// 放棄那一行：<b>主觀難度 0</b>、原因照填、結局 Abandoned。過關那一行：原因是空字串。
        /// 經過一次 JSON 來回，因為真正要讀的是檔案裡那一行。
        /// </summary>
        [Test]
        public void 結局_放棄那一行難度是零而且有原因()
        {
            Assert.IsTrue(SessionPastPractice().TryCreateAbandonedRecord(
                AbandonedClassicSquare(), AbandonReason.DidNotKnowHow, DateTime.UtcNow, "test", out FeedbackRecord abandoned));
            FeedbackRecord abandonedReread = JsonUtility.FromJson<FeedbackRecord>(JsonUtility.ToJson(abandoned));
            Assert.AreEqual(0, abandonedReread.subjectiveDifficulty);
            Assert.AreEqual("DidNotKnowHow", abandonedReread.abandonReason);
            Assert.AreEqual("Abandoned", abandonedReread.outcome);

            Assert.IsTrue(SessionPastPractice().TryCreateSolvedRecord(
                SolvedClassicSquare(), 4, DateTime.UtcNow, "test", out FeedbackRecord solved));
            FeedbackRecord solvedReread = JsonUtility.FromJson<FeedbackRecord>(JsonUtility.ToJson(solved));
            Assert.AreEqual(4, solvedReread.subjectiveDifficulty);
            Assert.AreEqual("", solvedReread.abandonReason);
            Assert.AreEqual("Solved", solvedReread.outcome);
        }

        /// <summary>放棄的題一樣只能答一次、答完一樣推進度（推進度兩條共用）。</summary>
        [Test]
        public void 結局_放棄的題也只能答一次()
        {
            LevelData level = AbandonedClassicSquare();
            FeedbackSession session = SessionPastPractice();

            Assert.IsTrue(session.TryCreateAbandonedRecord(level, AbandonReason.TooHard, DateTime.UtcNow, "test", out _));
            Assert.IsTrue(session.MarkAnswered(level));
            Assert.IsFalse(session.TryCreateAbandonedRecord(level, AbandonReason.NotInterested, DateTime.UtcNow, "test", out _),
                           "同一關第二次不能再組一行");
            Assert.AreEqual(1, session.AnsweredCount);
        }

        /// <summary>練習題放棄了也不問原因：那一行原因是空的。</summary>
        [Test]
        public void 結局_練習題放棄了也不問原因()
        {
            Assert.IsTrue(NewSession().TryCreatePracticeRecord(AbandonedClassicSquare(), DateTime.UtcNow, "test",
                                                                out FeedbackRecord record));
            Assert.AreEqual("", record.abandonReason);
            Assert.AreEqual(0, record.subjectiveDifficulty);
        }

        /// <summary>
        /// 五題照出題順序走，答完五題才算完成；第幾題與題單索引一路對得上。
        /// 每一題都是換過關的新 <see cref="LevelData"/>——跟真的流程一樣。
        /// </summary>
        [Test]
        public void 進度_照順序走完五題才算完成()
        {
            FeedbackSession session = SessionPastPractice(participantNumber: 4);
            int[] expectedOrder = FeedbackPlan.OrderFor(PlanSeed, 4);
            var levelSession = new LevelSession();

            for (int k = 0; k < FeedbackPlan.PuzzleCount; k++)
            {
                Assert.IsFalse(session.IsComplete, $"第 {k + 1} 題之前就算完成了");
                Assert.AreEqual(k + 1, session.SequenceNumber);
                Assert.AreEqual(expectedOrder[k], session.CurrentPlanIndex);

                Placement[] solution = Fixtures.ClassicSquare();
                levelSession.Load(Puzzle.FromPlacements(solution), solution, 1000 + k, allowRoll: false);
                Solve(levelSession.Data, 10.0 * (k + 1));

                Assert.IsTrue(session.TryCreateSolvedRecord(levelSession.Data, 3, DateTime.UtcNow, "test", out FeedbackRecord record));
                Assert.AreEqual(FeedbackPlan.TargetDifficulties[expectedOrder[k]], record.targetDifficulty);
                Assert.IsTrue(session.MarkAnswered(levelSession.Data));
            }

            Assert.IsTrue(session.IsComplete);
            Assert.Throws<InvalidOperationException>(() => { int _ = session.CurrentPlanIndex; });
        }

        /// <summary>受試者編號從 1 起。</summary>
        [Test]
        public void 進度_編號小於一要丟例外()
        {
            Assert.Throws<ArgumentOutOfRangeException>(() => NewSession(participantNumber: 0));
        }

        // ─────────────────────────────────────────────────────────────
        // 三、練習題
        // ─────────────────────────────────────────────────────────────

        /// <summary>
        /// ⭐ <b>沒做練習題之前，正式題一律不收</b>。
        /// 流程是「練習 → 準備好了嗎 → 正式五題」，順序反過來的話練習題就失去意義。
        /// </summary>
        [Test]
        public void 練習_沒做練習題之前正式題不收()
        {
            FeedbackSession session = NewSession();
            LevelData level = SolvedClassicSquare();

            Assert.IsFalse(session.TryCreateSolvedRecord(level, 3, DateTime.UtcNow, "test", out _));
            Assert.IsFalse(session.MarkAnswered(level));
            Assert.AreEqual(0, session.AnsweredCount);
        }

        /// <summary>練習題那一行：第 0 題、不問難不難（0）、目標 0、標成練習。分析時靠 isPractice 排除。</summary>
        [Test]
        public void 練習_那一行是第零題而且不問難不難()
        {
            FeedbackSession session = NewSession();
            LevelData practice = SolvedClassicSquare();

            Assert.IsTrue(session.TryCreatePracticeRecord(practice, DateTime.UtcNow, "test", out FeedbackRecord record));
            Assert.IsTrue(record.isPractice);
            Assert.AreEqual(0, record.sequenceNumber);
            Assert.AreEqual(0, record.subjectiveDifficulty);
            Assert.AreEqual(0, record.targetDifficulty);
            Assert.AreEqual(42.0, record.foregroundSeconds, 1e-9, "練習題的時間照樣記——那是這個人的操作速度基準");
            Assert.IsFalse(session.IsPracticeDone, "組出那一行不會標記做完");
        }

        /// <summary>
        /// 練習題只做一次；而且<b>練習那一關不能拿去答正式第一題</b>——
        /// 控制器按完「開始正式題目」之前，盤面上還是練習那一關（已結束），連點的話不能被當成正式題。
        /// </summary>
        [Test]
        public void 練習_只做一次而且那一關不能拿去答正式題()
        {
            FeedbackSession session = NewSession();
            LevelData practice = SolvedClassicSquare();
            Assert.IsTrue(session.MarkPracticeDone(practice));

            Assert.IsFalse(session.TryCreatePracticeRecord(SolvedClassicSquare(), DateTime.UtcNow, "test", out _), "練習做兩次");
            Assert.IsFalse(session.MarkPracticeDone(SolvedClassicSquare()));
            Assert.IsFalse(session.TryCreateSolvedRecord(practice, 3, DateTime.UtcNow, "test", out _), "練習那一關被當成正式題");
        }

        /// <summary>
        /// ⭐ <b>練習題的平行四邊形一定要翻面</b>：不翻面的八個角度沒有一個對得上。
        /// 否則從抽屜拿出來直接放就對了，受試者練不到翻面鈕——而那是最難自己發現的操作。
        /// </summary>
        [Test]
        public void 練習題_平行四邊形一定要翻面才放得進去()
        {
            Placement[] solution = FeedbackPractice.BuildSolution();
            Placement parallelogram = solution.Single(piece => piece.Kind == PieceKind.Parallelogram);

            Assert.IsTrue(parallelogram.IsFlipped);
            for (int rotation = 0; rotation < 8; rotation++)
                Assert.IsFalse(FeedbackPractice.SameShapeIgnoringPosition(
                    new Placement(PieceKind.Parallelogram, rotation, false, ExactVector2.Zero), parallelogram),
                    $"不翻面、轉 {rotation} 格就對得上——練不到翻面");
        }

        /// <summary>留空的兩槽：一個平行四邊形、一片<b>姿勢跟抽屜預設不同</b>的三角形。</summary>
        [Test]
        public void 練習題_留空的是平行四邊形與一片要轉的三角形()
        {
            Placement[] solution = FeedbackPractice.BuildSolution();
            int[] slots = FeedbackPractice.SlotsToLeaveEmpty(solution);

            Assert.AreEqual(2, slots.Length);
            Assert.AreEqual(PieceKind.Parallelogram, solution[slots[0]].Kind);
            // ⚠️ 不要寫成 Is.AnyOf——Unity 的 NUnit 是 3.5，沒有那個 API（2026-09-13 在 Unity 編不過）
            CollectionAssert.Contains(new[] { PieceKind.SmallTriangle, PieceKind.MediumTriangle, PieceKind.LargeTriangle },
                                      solution[slots[1]].Kind);
            Assert.IsFalse(FeedbackPractice.LooksLikeTrayDefault(solution[slots[1]]), "那片三角形不轉就放得進去");
        }

        /// <summary>
        /// 練習題還是經典大正方形（鏡射過的也算同一個形狀），而且落在正象限，跟生成題一樣。
        /// </summary>
        [Test]
        public void 練習題_還是大正方形而且在正象限()
        {
            Placement[] solution = FeedbackPractice.BuildSolution();

            Assert.AreEqual(Canonical.PuzzleKey(Fixtures.ClassicSquare()), Canonical.PuzzleKey(solution));
            foreach (Placement piece in solution)
                foreach (ExactVector2 vertex in piece.Vertices())
                    Assert.IsFalse(vertex.Y < Scalar.Zero, "鏡射之後沒有移回正象限");
        }

        /// <summary>
        /// 「形狀方向一樣」比的是頂點集合，不是姿態欄位：正方形轉 0 與轉 2 佔位一樣，
        /// 平行四邊形翻不翻面不一樣（文件/開發須知.md〈容易踩的坑〉的「Placement.Equals 是姿態相等，不是形狀相等」）。
        /// </summary>
        [Test]
        public void 練習題_形狀比較不看姿態欄位只看佔位()
        {
            var somewhere = new ExactVector2(3, 5);
            Assert.IsTrue(FeedbackPractice.SameShapeIgnoringPosition(
                new Placement(PieceKind.Square, 0, false, ExactVector2.Zero), new Placement(PieceKind.Square, 2, false, somewhere)));
            Assert.IsFalse(FeedbackPractice.SameShapeIgnoringPosition(
                new Placement(PieceKind.Parallelogram, 0, false, ExactVector2.Zero), new Placement(PieceKind.Parallelogram, 0, true, somewhere)));
        }

        /// <summary>
        /// 開局先擺好：只擺指定以外的槽、<b>不壓 undo</b>（不然受試者按 undo，擺好的五片會整組飛回抽屜）、
        /// 盤面不是空的就不擺（不然會蓋掉玩家放的片，而且拿不回來）。
        /// </summary>
        [Test]
        public void 練習題_開局先擺好五片而且不壓undo()
        {
            Placement[] solution = FeedbackPractice.BuildSolution();
            int[] leaveEmpty = FeedbackPractice.SlotsToLeaveEmpty(solution);
            LevelData level = LoadedSession(solution).Data;

            Assert.IsTrue(level.TryPrefillAtStart(leaveEmpty));
            for (int slotIndex = 0; slotIndex < BoardState.PieceCount; slotIndex++)
            {
                bool shouldBeEmpty = leaveEmpty.Contains(slotIndex);
                Assert.AreEqual(!shouldBeEmpty, level.Board[slotIndex].OnBoard, $"第 {slotIndex} 槽");
                Assert.AreEqual(shouldBeEmpty, level.IsInTray(slotIndex), $"第 {slotIndex} 槽的待用區狀態");
            }
            Assert.IsFalse(level.CanUndo, "開局擺好的片不是玩家的一步，不該能 undo");
            Assert.IsFalse(level.TryPrefillAtStart(leaveEmpty), "盤面已經有片了，不該再擺");
        }

        // ─────────────────────────────────────────────────────────────
        // 四、寫進檔案的那一行
        // ─────────────────────────────────────────────────────────────

        /// <summary>那一行的數字都來自這一關的 <see cref="LevelData"/> 與這位受試者。</summary>
        [Test]
        public void 那一行_數字來自這一關與這位受試者()
        {
            LevelData level = LoadedSession(Fixtures.ClassicSquare(), difficulty: 1111).Data;
            level.StartForegroundTiming(0.0);
            level.StartDragTiming(5.0);
            level.StopDragTiming(7.0);
            level.RecordClear();
            Assert.IsTrue(level.TryUseHint(50.0, 45.0, out _), "前置：50 秒時能按一次提示");
            level.MarkAbandoned(60.0);

            var profile = new FeedbackParticipantProfile("c-3", 2, AgeGroup.EighteenOrOver,
                                                         TangramExperience.MoreThanTenTimes, WatchedOthers.WatchedLong);
            var session = new FeedbackSession(9, profile, PlanSeed, "fedcba987654");
            LevelData practice = SolvedClassicSquare();
            Assert.IsTrue(session.TryCreatePracticeRecord(practice, DateTime.UtcNow, "0.1", out _));
            Assert.IsTrue(session.MarkPracticeDone(practice));
            var time = new DateTime(2026, 9, 14, 3, 4, 5, DateTimeKind.Utc);
            Assert.IsTrue(session.TryCreateAbandonedRecord(level, AbandonReason.TooHard, time, "0.1", out FeedbackRecord record));

            Assert.AreEqual(9, record.participantNumber);
            Assert.AreEqual("EighteenOrOver", record.ageGroup);
            Assert.AreEqual("MoreThanTenTimes", record.tangramExperience);
            Assert.AreEqual("c-3", record.participantCode, "名字照打的樣子存，不轉大寫");
            Assert.AreEqual(2, record.visitNumber);
            Assert.AreEqual("WatchedLong", record.watchedOthers);
            Assert.IsFalse(record.isPractice);
            Assert.AreEqual(1, record.sequenceNumber, "練習題是第 0 題，正式第一題是 1");
            Assert.AreEqual(PlanSeed, record.planSeed);
            Assert.AreEqual(1111, record.difficulty);
            Assert.AreEqual("fedcba987654", record.weightsFingerprint, "每一行都要記是哪一份權重算的難度");
            Assert.AreEqual("Abandoned", record.outcome);
            Assert.AreEqual(60.0, record.foregroundSeconds, 1e-9);
            Assert.AreEqual(2.0, record.dragSeconds, 1e-9);
            Assert.AreEqual(1, record.dragCount);
            Assert.AreEqual(1, record.clearCount);
            Assert.AreEqual(1, record.hintsUsed);
            Assert.AreEqual(50.0, record.firstHintForegroundSeconds, 1e-9, "第一次按提示時的前景秒數");
            Assert.AreEqual(0, record.subjectiveDifficulty, "放棄的題不問難不難");
            Assert.AreEqual("TooHard", record.abandonReason);
            Assert.AreEqual(level.Puzzle.Key, record.puzzleKey);
            Assert.AreEqual(time.ToString("o"), record.recordedAtUtc);
            Assert.AreEqual("0.1", record.appVersion);
        }

        /// <summary>
        /// ⭐ <b>存下來的題目還原得回來，而且還原後重算的難度一樣</b>——這就是「權重改了之後能重算」的保證。
        /// 經過一次 JSON 來回，因為真正要還原的是檔案裡那一行。用生成題驗，大正方形太對稱，錯了也可能矇混過去。
        /// </summary>
        [Test]
        public void 那一行_題目還原得回來而且難度重算一樣()
        {
            DifficultyWeights weights = RepositoryFiles.LoadDifficultyWeights();
            TargetedGenerationResult generated =
                new TargetedPuzzleGenerator(new PuzzleGenerator(PlanSeed), weights).Generate(1300);
            Assert.AreNotEqual(TargetedGenerationOutcome.Failed, generated.Outcome);

            var levelSession = new LevelSession();
            levelSession.Load(generated.Puzzle, generated.ReferenceSolution, generated.Difficulty, allowRoll: false);
            Solve(levelSession.Data, 80.0);

            Assert.IsTrue(SessionPastPractice().TryCreateSolvedRecord(levelSession.Data, 3, DateTime.UtcNow, "test", out FeedbackRecord record));
            FeedbackRecord reread = JsonUtility.FromJson<FeedbackRecord>(JsonUtility.ToJson(record));

            Placement[] decoded = Serialization.FromDto(reread.arrangement).Decode();
            Assert.AreEqual(reread.puzzleKey, Canonical.PuzzleKey(decoded), "還原出來的不是同一個形狀");
            Assert.AreEqual(reread.difficulty,
                DifficultyEvaluator.Score(Puzzle.FromPlacements(decoded), decoded, weights), "還原之後重算的難度不一樣");
        }

        /// <summary>
        /// 暫存檔名：編號寫進去讀得回來（含補零、完整路徑），不是暫存檔的名字讀不到。
        /// ⚠️ 讀錯的症狀是「下一位拿到跟遺留暫存檔一樣的編號」，兩份資料被當成同一個人，不報錯。
        /// </summary>
        [Test]
        public void 暫存檔_檔名與編號互轉()
        {
            foreach (int number in new[] { 1, 7, 42, 9999, 12345 })
            {
                string fileName = FeedbackFiles.PartialFileName(number);
                Assert.IsTrue(FeedbackFiles.TryParsePartialFileName(fileName, out int parsed), fileName);
                Assert.AreEqual(number, parsed);
                Assert.IsTrue(FeedbackFiles.TryParsePartialFileName(
                    System.IO.Path.Combine("Tools", "Feedback", "unfinished", fileName), out int fromPath), "完整路徑也要讀得到");
                Assert.AreEqual(number, fromPath);
            }
            Assert.AreEqual("participant_0007.partial.jsonl", FeedbackFiles.PartialFileName(7), "補零讓檔案總管排得對");

            foreach (string notPartial in new[]
                     {
                         "feedback_results.jsonl", "participant_abc.partial.jsonl", "participant_0000.partial.jsonl",
                         "participant_-3.partial.jsonl", "participant_0007.jsonl", "", null,
                     })
                Assert.IsFalse(FeedbackFiles.TryParsePartialFileName(notPartial, out _), $"「{notPartial}」不該被當成暫存檔");
        }

        /// <summary>
        /// 找最大受試者編號：空檔是 0、取最大的那個、讀不懂的行跳過並計數、空行不算壞掉。
        /// 當掉時最後一行可能只寫了一半，那不該讓整個場景開不起來。
        /// </summary>
        [Test]
        public void 那一行_找出最大的受試者編號()
        {
            Func<string, FeedbackRecord> parse = line => JsonUtility.FromJson<FeedbackRecord>(line);

            Assert.AreEqual(0, FeedbackRecord.HighestParticipantNumber(new string[0], parse, out int noneBroken));
            Assert.AreEqual(0, noneBroken);

            string Line(int number) => JsonUtility.ToJson(new FeedbackRecord { participantNumber = number });
            var lines = new[] { Line(3), "", Line(7), "{\"participantNumber\": 5, \"ageGr", Line(2), "   " };

            Assert.AreEqual(7, FeedbackRecord.HighestParticipantNumber(lines, parse, out int brokenLineCount));
            Assert.AreEqual(1, brokenLineCount, "只寫了一半的那一行要算壞掉，空行不算");
        }
    }
}
