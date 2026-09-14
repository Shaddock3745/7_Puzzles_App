using System;
using System.Collections.Generic;

namespace Tangram.Data
{
    /// <summary>
    /// 結果檔的一行 = 一位受試者做完的一題（回饋收集，2026-09-13）。
    ///
    /// ⭐ <b>存題目本身（<see cref="arrangement"/>），不只存難度值</b>：權重還沒定案，校正之後舊的難度值全部過期，
    /// 只要題目還在就能重算。<b>特徵值刻意不存</b>——有題目就算得出來，而且特徵函數之後被修正時，
    /// 重算拿到的反而是新定義的值（鐵則三：只存相對編碼，需要時重算）。
    ///
    /// ⚠️ <b>欄位名就是檔案裡的鍵</b>，開始收資料之後就不要改名——改了舊的那幾行會被讀成預設值，不報錯
    /// （跟 CLAUDE.md〈儲存契約〉第 2 條同一件事）。
    /// </summary>
    [Serializable]
    public class FeedbackRecord
    {
        /// <summary>受試者編號（從 1 起）。不收姓名。</summary>
        public int participantNumber;

        /// <summary><see cref="AgeGroup"/> 的名字。存字串是為了打開檔案就讀得懂。</summary>
        public string ageGroup;

        /// <summary><see cref="TangramExperience"/> 的名字。</summary>
        public string tangramExperience;

        /// <summary>
        /// ⭐ 受試者代號：<b>小朋友自己打的英文名字</b>，大小寫照原樣（見 <see cref="FeedbackParticipantCode"/>，2026-09-14）。
        /// <b>同一個小朋友不同天再來時，靠它把兩次連起來</b>——<see cref="participantNumber"/> 每次都是新的。
        /// ⚠️ 分析時比對要<b>不分大小寫</b>；⚠️ 這個欄位讓結果檔含有名字，不能放進對外文件。
        /// </summary>
        public string participantCode;

        /// <summary>這是這個代號第幾次來（從 1 起，程式從結果檔算的）。分析時用它分開學習效應。</summary>
        public int visitNumber;

        /// <summary><see cref="WatchedOthers"/> 的名字：輪到他之前有沒有在旁邊看別人玩。</summary>
        public string watchedOthers;

        /// <summary>
        /// 是不是練習題。⚠️ <b>分析正式資料時要排除</b>——練習題可以教、不問難不難、題目也不在題單上。
        /// 記下來是因為它的時間等於「這個人的滑鼠操作速度」，可以當基準。
        /// </summary>
        public bool isPractice;

        /// <summary>這是他的第幾題：練習題是 0，正式題 1 ～ 5。看學習效應用。</summary>
        public int sequenceNumber;

        /// <summary>題單種子。同一個種子生出同一批題（前提是權重指紋也一樣——權重決定哪幾題會被挑中）。每個人一批不同的題（基準種子 ＋ 編號 − 1）。</summary>
        public int planSeed;

        /// <summary>這一題在題單上的目標難度，練習題是 0。⚠️ 分析時用 <see cref="difficulty"/>，這個只是標籤。</summary>
        public int targetDifficulty;

        /// <summary>題目的 key（輪廓的 canonical hash）。</summary>
        public string puzzleKey;

        /// <summary>⭐ 參考解的相對編碼。權重校正之後，拿它重算難度。</summary>
        public ArrangementDto arrangement;

        /// <summary>收資料當時系統算的難度值（當時那份權重，是哪一份看 <see cref="weightsFingerprint"/>）。</summary>
        public int difficulty;

        /// <summary>
        /// ⭐ 算 <see cref="difficulty"/> 用的那份權重的指紋（<c>DifficultyWeights.Fingerprint</c>，2026-09-14）。
        /// 收資料會分好幾輪、中間可能換權重——<b>不同指紋的難度值不能直接放在一起比</b>，要先用同一份權重從 <see cref="arrangement"/> 重算。
        /// </summary>
        public string weightsFingerprint;

        /// <summary><see cref="LevelOutcome"/> 的名字：Solved 或 Abandoned。</summary>
        public string outcome;

        /// <summary>前景秒數。⚠️ 這一批是<b>筆電、滑鼠操作</b>，跟觸控不一樣，不能拿來校正 T。</summary>
        public double foregroundSeconds;

        /// <summary>拖曳秒數（已套單次上限）。</summary>
        public double dragSeconds;

        /// <summary>拖曳次數。</summary>
        public int dragCount;

        /// <summary>清空次數——之後有資料時可能是最有鑑別力的挫折指標。</summary>
        public int clearCount;

        /// <summary>提示次數（現在恆為 0）。</summary>
        public int hintsUsed;

        /// <summary>主觀難度 1（很簡單）～ 5（很難）。<b>只有過關的正式題才問</b>；練習題與放棄的題是 0。</summary>
        public int subjectiveDifficulty;

        /// <summary>
        /// ⭐ <see cref="AbandonReason"/> 的名字（2026-09-14）。<b>只有放棄的正式題才問</b>；過關與練習題是空字串。
        /// ⚠️ 這個欄位加上之前收的那幾行讀進來是空的——那幾行放棄時問的是 1 ～ 5，分析時看 <see cref="appVersion"/> 分開。
        /// </summary>
        public string abandonReason;

        /// <summary>寫下這一行的時間，ISO-8601 UTC。</summary>
        public string recordedAtUtc;

        /// <summary>App 版本，之後改了程式才分得出是哪一版收的。</summary>
        public string appVersion;

        /// <summary>
        /// 從一堆行裡找出最大的受試者編號，新的受試者就用它 ＋ 1。
        ///
        /// <b>讀不懂的行跳過並計數</b>，不丟例外——結果檔是逐行追加的，當掉時最後一行可能只寫了一半，
        /// 那不該讓整個場景開不起來。但數量要回報，讓呼叫端講出來。
        /// </summary>
        /// <param name="lines">結果檔的每一行。</param>
        /// <param name="parse">把一行讀成紀錄的函數（Unity 裡是 <c>JsonUtility.FromJson</c>）；讀不懂時丟例外或回 null。</param>
        /// <param name="brokenLineCount">讀不懂的行數（空行不算）。</param>
        /// <returns>最大的編號；沒有任何紀錄時是 0。</returns>
        public static int HighestParticipantNumber(IEnumerable<string> lines, Func<string, FeedbackRecord> parse,
                                                   out int brokenLineCount)
        {
            int highest = 0;
            brokenLineCount = 0;
            foreach (string line in lines)
            {
                if (string.IsNullOrWhiteSpace(line)) continue;

                FeedbackRecord record = TryParse(line, parse);
                if (record == null)
                {
                    brokenLineCount++;
                    continue;
                }
                if (record.participantNumber > highest) highest = record.participantNumber;
            }
            return highest;
        }

        /// <summary>
        /// 每個代號在這些行裡<b>來過幾次</b>（2026-09-14）：數的是<b>不同的受試者編號</b>，不是行數——
        /// 一次來有練習加五題共 6 行，數行數的話來一次就變成來六次。
        /// 讀不懂的行、沒有代號的行（代號欄位加上之前的舊資料）跳過。
        /// </summary>
        /// <param name="lines">結果檔的每一行。</param>
        /// <param name="parse">把一行讀成紀錄的函數；讀不懂時丟例外或回 null。</param>
        /// <returns>代號 → 來過幾次。</returns>
        public static Dictionary<string, int> CountVisitsByCode(IEnumerable<string> lines, Func<string, FeedbackRecord> parse)
        {
            // ⚠️ 不分大小寫：「Amy」與「amy」是同一個小朋友（見 FeedbackParticipantCode.Comparer）
            var participantsByCode = new Dictionary<string, HashSet<int>>(FeedbackParticipantCode.Comparer);
            foreach (string line in lines)
            {
                if (string.IsNullOrWhiteSpace(line)) continue;

                FeedbackRecord record = TryParse(line, parse);
                string code = record == null ? null : FeedbackParticipantCode.Normalize(record.participantCode);
                if (code == null) continue;

                if (!participantsByCode.TryGetValue(code, out HashSet<int> participants))
                    participantsByCode[code] = participants = new HashSet<int>();
                participants.Add(record.participantNumber);
            }

            var visits = new Dictionary<string, int>(FeedbackParticipantCode.Comparer);
            foreach (KeyValuePair<string, HashSet<int>> entry in participantsByCode) visits[entry.Key] = entry.Value.Count;
            return visits;
        }

        /// <param name="line">一行。</param>
        /// <param name="parse">把一行讀成紀錄的函數。</param>
        /// <returns>讀得懂而且編號合法的紀錄；否則 null。</returns>
        private static FeedbackRecord TryParse(string line, Func<string, FeedbackRecord> parse)
        {
            FeedbackRecord record;
            try { record = parse(line); }
            catch (Exception) { record = null; }   // Unity 丟 ArgumentException、System.Text.Json 丟 JsonException，一律當成讀不懂

            return record != null && record.participantNumber > 0 ? record : null;
        }
    }
}
