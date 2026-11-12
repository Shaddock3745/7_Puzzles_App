using System;
using Tangram.Data;

namespace Tangram.Core
{
    /// <summary>
    /// <b>一位受試者</b>在回饋收集場景裡的進度（2026-09-13）。
    ///
    /// <b>為什麼放 Core 不放控制器裡</b>：跟 <see cref="SkillRatingTracker"/> 同一個理由——
    /// 「這一題能不能答、答過了沒」這幾道守衛寫錯的症狀是<b>資料少一行或多一行</b>，
    /// 而那只有收完資料打開檔案才看得到，那時候受試者早就走了。放這裡離線就能驗。
    ///
    /// <b>記錄分兩步</b>：<see cref="TryCreateRecord"/> 只檢查並組出那一行、不改任何狀態；
    /// 控制器寫檔成功之後才叫 <see cref="MarkAnswered"/> 推進度。
    /// ⚠️ 合成一步的話，寫檔失敗時進度已經往前了——那一題的答案就永遠消失，而受試者已經在做下一題。
    /// 分兩步之後寫檔失敗時停在原題，再點一次就是重試。
    ///
    /// <b>寫檔是控制器的事</b>，這一層不碰檔案；現在幾點與 App 版本也由呼叫端傳進來（跟局內統計的計時同一個模式）。
    ///
    /// <b>順序</b>：練習題（<see cref="TryCreatePracticeRecord"/> → <see cref="MarkPracticeDone"/>）→ 正式五題。
    /// 練習題可以教、不問難不難，但一樣記一行（第 0 題）。
    /// 正式題依結局分兩條（2026-09-14）：過關問難不難（<see cref="TryCreateSolvedRecord"/>），
    /// 放棄問為什麼（<see cref="TryCreateAbandonedRecord"/>）。推進度共用 <see cref="MarkAnswered"/>。
    /// </summary>
    public sealed class FeedbackSession
    {
        /// <summary>受試者編號（從 1 起）。</summary>
        public readonly int ParticipantNumber;

        /// <summary>開始前填好的資料：代號、第幾次來、年齡層、經驗、有沒有看別人玩。</summary>
        public readonly FeedbackParticipantProfile Profile;

        /// <summary>這一位的題單種子（每個人一批不同的題，見 <see cref="FeedbackPlan.PlanSeedFor"/>）。</summary>
        public readonly int PlanSeed;

        /// <summary>出題與算難度用的那份權重的指紋，寫進每一行（見 <see cref="DifficultyWeights.Fingerprint"/>）。</summary>
        public readonly string WeightsFingerprint;

        /// <summary>出題順序：第 k 題（從 0 起）是題單上的第幾個。</summary>
        private readonly int[] order;

        /// <summary>
        /// 上一次答完的那一關。⚠️ <b>「同一題不能答兩次」靠比對這個參照</b>——
        /// 換關時 <see cref="LevelData"/> 整包換成新的物件，所以同一個參照就代表還是那一題。
        /// </summary>
        private LevelData lastAnsweredLevel;

        /// <summary>
        /// 練習題做完（而且那一行寫進檔了）沒。⚠️ <b>沒做完之前正式題一律不收</b>——
        /// 流程是「練習 → 準備好了嗎 → 正式五題」，順序反過來的話練習題就失去意義。
        /// </summary>
        public bool IsPracticeDone { get; private set; }

        /// <summary>已經答完幾題（正式題，不含練習）。</summary>
        public int AnsweredCount { get; private set; }

        /// <summary>五題都答完了沒。</summary>
        public bool IsComplete => AnsweredCount >= FeedbackPlan.PuzzleCount;

        /// <summary>現在是第幾題（1 ～ 5）。做完之後是 6，不要拿去用。</summary>
        public int SequenceNumber => AnsweredCount + 1;

        /// <param name="participantNumber">受試者編號，必須 ≥ 1。</param>
        /// <param name="profile">開始前填好的資料（建構時已經檢查過）。</param>
        /// <param name="planSeed">這一位的題單種子。出題順序由它與編號決定（<see cref="FeedbackPlan.OrderFor"/>）。</param>
        /// <param name="weightsFingerprint">
        /// 權重指紋。⚠️ <b>必填</b>——空的話不同輪的難度值混在一起就分不出來了（2026-09-14 加欄位的理由）。
        /// </param>
        /// <exception cref="ArgumentOutOfRangeException">編號小於 1。</exception>
        /// <exception cref="ArgumentNullException">沒有給受試者資料。</exception>
        /// <exception cref="ArgumentException">權重指紋是空的。</exception>
        public FeedbackSession(int participantNumber, FeedbackParticipantProfile profile, int planSeed,
                               string weightsFingerprint)
        {
            if (participantNumber < 1)
                throw new ArgumentOutOfRangeException(nameof(participantNumber), participantNumber, "受試者編號從 1 起");
            if (string.IsNullOrEmpty(weightsFingerprint))
                throw new ArgumentException("權重指紋是空的——不同輪換過權重的話資料會對不上", nameof(weightsFingerprint));

            ParticipantNumber = participantNumber;
            Profile = profile ?? throw new ArgumentNullException(nameof(profile));
            PlanSeed = planSeed;
            WeightsFingerprint = weightsFingerprint;
            order = FeedbackPlan.OrderFor(planSeed, participantNumber);
        }

        /// <summary>現在這一題是題單上的第幾個（0 ～ 4）。</summary>
        /// <exception cref="InvalidOperationException">五題都做完了。</exception>
        public int CurrentPlanIndex
        {
            get
            {
                if (IsComplete) throw new InvalidOperationException("五題都做完了，沒有下一題");
                return order[AnsweredCount];
            }
        }

        /// <summary>
        /// <b>過關的</b>正式題：檢查能不能答，能的話組出要寫進檔案的那一行。<b>不改任何狀態</b>。
        ///
        /// <b>不收的情況</b>（回 false）：
        /// <list type="bullet">
        /// <item>練習題還沒做完、五題已經做完</item>
        /// <item>沒有這一關或沒有題目</item>
        /// <item><b>還沒結束</b>（還在玩就不能答）</item>
        /// <item>⚠️ <b>計時還在跑</b>——前景秒數只在停止時累加，還在跑就讀會少算（跟 ELO 結算同一條理由）</item>
        /// <item><b>這一題已經答過</b>——手指連點兩下只能寫一行</item>
        /// <item>⭐ <b>這一關不是過關的</b>（2026-09-14）——放棄的題要走 <see cref="TryCreateAbandonedRecord"/></item>
        /// <item>主觀難度不在 1 ～ 5</item>
        /// </list>
        ///
        /// ⚠️ <b>為什麼要分結局擋</b>：場景接錯線（例如放棄之後難度面板也出現了）時，
        /// 資料是<b>寫不進去</b>而不是<b>寫錯</b>——後者就是「放棄＋超簡單」那種行，只有收完打開檔案才看得到。
        /// </summary>
        /// <param name="level">這一關。</param>
        /// <param name="subjectiveDifficulty">主觀難度 1 ～ 5。</param>
        /// <param name="recordedAtUtc">現在的 UTC 時間。</param>
        /// <param name="appVersion">App 版本。</param>
        /// <param name="record">收的話是那一行；不收時是 null。</param>
        /// <returns>收不收。</returns>
        public bool TryCreateSolvedRecord(LevelData level, int subjectiveDifficulty, DateTime recordedAtUtc,
                                          string appVersion, out FeedbackRecord record)
        {
            record = null;
            if (!IsPracticeDone || !CanAnswer(level) || !level.IsSolved
                || !FeedbackPlan.IsValidSubjectiveDifficulty(subjectiveDifficulty))
                return false;

            record = BuildRecord(level, isPractice: false, SequenceNumber,
                                 FeedbackPlan.TargetDifficulties[CurrentPlanIndex], subjectiveDifficulty,
                                 abandonReason: null, recordedAtUtc, appVersion);
            return true;
        }

        /// <summary>
        /// <b>放棄的</b>正式題：檢查能不能答，能的話組出那一行。<b>不改任何狀態</b>（2026-09-14）。
        ///
        /// 放棄之後<b>不問難不難，改問為什麼</b>（理由見 <see cref="AbandonReason"/>），所以主觀難度寫 0。
        /// 不收的情況跟 <see cref="TryCreateSolvedRecord"/> 一樣，只是結局那條反過來（這一關要是放棄的），
        /// 而答案要是列舉裡的其中一個。
        /// </summary>
        /// <param name="level">這一關。</param>
        /// <param name="reason">為什麼放棄。</param>
        /// <param name="recordedAtUtc">現在的 UTC 時間。</param>
        /// <param name="appVersion">App 版本。</param>
        /// <param name="record">收的話是那一行；不收時是 null。</param>
        /// <returns>收不收。</returns>
        public bool TryCreateAbandonedRecord(LevelData level, AbandonReason reason, DateTime recordedAtUtc,
                                             string appVersion, out FeedbackRecord record)
        {
            record = null;
            if (!IsPracticeDone || !CanAnswer(level) || !level.IsAbandoned || !FeedbackPlan.IsValidAbandonReason(reason))
                return false;

            record = BuildRecord(level, isPractice: false, SequenceNumber,
                                 FeedbackPlan.TargetDifficulties[CurrentPlanIndex], subjectiveDifficulty: 0,
                                 reason, recordedAtUtc, appVersion);
            return true;
        }

        /// <summary>
        /// 那一行已經寫進檔案了，推進到下一題。<b>寫檔成功之後才叫</b>（理由見類別說明）。
        /// </summary>
        /// <param name="level">剛答完的那一關。</param>
        /// <returns>有沒有真的推進（守衛跟組那一行的兩支一樣，但不看結局也不看答案——那一行已經寫進去了）。</returns>
        public bool MarkAnswered(LevelData level)
        {
            if (!IsPracticeDone || !CanAnswer(level)) return false;

            lastAnsweredLevel = level;
            AnsweredCount++;
            return true;
        }

        // ───────── 練習題（2026-09-13）─────────
        //
        // 跟正式題同一套兩步：組出那一行 → 寫檔 → 寫成功才標記做完。
        // 差別：不問主觀難度（寫 0）、不問放棄原因（空的，放棄了也一樣）、第幾題寫 0、目標難度寫 0、isPractice = true。

        /// <summary>
        /// 檢查練習題能不能收尾，能的話組出那一行。<b>不改任何狀態</b>。
        /// 不收的情況：練習已經做完、沒有題目、還沒結束、計時還在跑、這一關已經記過。
        /// </summary>
        /// <param name="level">練習題那一關。</param>
        /// <param name="recordedAtUtc">現在的 UTC 時間。</param>
        /// <param name="appVersion">App 版本。</param>
        /// <param name="record">收的話是那一行；不收時是 null。</param>
        /// <returns>收不收。</returns>
        public bool TryCreatePracticeRecord(LevelData level, DateTime recordedAtUtc, string appVersion,
                                            out FeedbackRecord record)
        {
            record = null;
            if (IsPracticeDone || !CanAnswer(level)) return false;

            record = BuildRecord(level, isPractice: true, sequenceNumber: 0, targetDifficulty: 0,
                                 subjectiveDifficulty: 0, abandonReason: null, recordedAtUtc, appVersion);
            return true;
        }

        /// <summary>練習題那一行已經寫進檔了，標記練習做完。<b>寫檔成功之後才叫</b>。</summary>
        /// <param name="level">練習題那一關。</param>
        /// <returns>有沒有真的標記。</returns>
        public bool MarkPracticeDone(LevelData level)
        {
            if (IsPracticeDone || !CanAnswer(level)) return false;

            lastAnsweredLevel = level;
            IsPracticeDone = true;
            return true;
        }

        /// <param name="level">這一關。</param>
        /// <returns>這一關現在能不能收尾（不看分數、不看是練習還是正式）。</returns>
        private bool CanAnswer(LevelData level) =>
            !IsComplete
            && level != null && level.Puzzle != null
            && level.IsFinished && !level.IsTiming
            && !ReferenceEquals(level, lastAnsweredLevel);

        /// <summary>組出一行。練習題與正式題共用，差別由參數決定。</summary>
        /// <param name="level">這一關。</param>
        /// <param name="isPractice">是不是練習題。</param>
        /// <param name="sequenceNumber">第幾題（練習是 0）。</param>
        /// <param name="targetDifficulty">題單上的目標（練習是 0）。</param>
        /// <param name="subjectiveDifficulty">主觀難度（練習與放棄是 0）。</param>
        /// <param name="abandonReason">放棄原因（過關與練習是 null，寫成空字串）。</param>
        /// <param name="recordedAtUtc">現在的 UTC 時間。</param>
        /// <param name="appVersion">App 版本。</param>
        /// <returns>那一行。</returns>
        private FeedbackRecord BuildRecord(LevelData level, bool isPractice, int sequenceNumber, int targetDifficulty,
                                           int subjectiveDifficulty, AbandonReason? abandonReason,
                                           DateTime recordedAtUtc, string appVersion)
        {
            // 題目存相對編碼：權重校正之後拿它重算難度。puzzleKey 直接用 LevelData 上那個，
            // 不跑 WithKeys（那要 16 次輪廓追蹤，而且算出來的應該就是同一個——測試釘住）
            ArrangementDto arrangement = Serialization.ToDto(Arrangement.FromPlacements(level.ReferenceSolution), recordedAtUtc);
            arrangement.puzzleKey = level.Puzzle.Key;

            return new FeedbackRecord
            {
                participantNumber = ParticipantNumber,
                ageGroup = Profile.AgeGroup.ToString(),
                tangramExperience = Profile.Experience.ToString(),
                participantCode = Profile.Code,
                visitNumber = Profile.VisitNumber,
                watchedOthers = Profile.WatchedOthers.ToString(),
                isPractice = isPractice,
                sequenceNumber = sequenceNumber,
                planSeed = PlanSeed,
                targetDifficulty = targetDifficulty,
                puzzleKey = level.Puzzle.Key,
                arrangement = arrangement,
                difficulty = level.Difficulty,
                weightsFingerprint = WeightsFingerprint,
                outcome = (level.IsSolved ? LevelOutcome.Solved : LevelOutcome.Abandoned).ToString(),
                foregroundSeconds = level.ForegroundSeconds,
                dragSeconds = level.DragSeconds,
                dragCount = level.DragCount,
                clearCount = level.ClearCount,
                hintsUsed = level.HintsUsed,
                firstHintForegroundSeconds = level.FirstHintForegroundSeconds,
                subjectiveDifficulty = subjectiveDifficulty,
                // 空字串而不是 null：JsonUtility 本來就把 null 寫成 ""，這裡明寫，免得記憶體裡跟讀回來的不一樣
                abandonReason = abandonReason.HasValue ? abandonReason.Value.ToString() : string.Empty,
                recordedAtUtc = recordedAtUtc.ToString("o"),
                appVersion = appVersion,
            };
        }
    }
}
