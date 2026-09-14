using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using Tangram.Data;

namespace Tangram.Core
{
    /// <summary>
    /// 走勢裡的一筆：<b>一局結算了什麼</b>（P2-15，issue #32）。
    /// </summary>
    public readonly struct SettlementRecord
    {
        /// <summary>
        /// 第幾筆，從 1 開始。
        /// ⚠️ <b>刻意不用 <c>LevelController</c> 的關卡編號</b>：被守衛擋下、沒結算的關會讓關卡編號跳號，
        /// 筆數不會。兩個都顯示的話會讓人以為少了一局。
        /// </summary>
        public readonly int SequenceNumber;

        /// <summary>那一局的出題紀錄；不是出題器生的題目時是 null。</summary>
        public readonly GenerationRecord? Generation;

        /// <summary>結算明細，原封不動複製（唯讀 struct，不會有兩邊互相改到的問題）。</summary>
        public readonly RatingUpdate Update;

        /// <param name="sequenceNumber">第幾筆。</param>
        /// <param name="generation">出題紀錄。</param>
        /// <param name="update">結算明細。</param>
        public SettlementRecord(int sequenceNumber, GenerationRecord? generation, RatingUpdate update)
        {
            SequenceNumber = sequenceNumber;
            Generation = generation;
            Update = update;
        }
    }

    /// <summary>
    /// <b>持有玩家目前的實力</b>，並負責「這一關要不要結算、結算完記下來」（P2-14，issue #31）。
    ///
    /// <b>為什麼不放在 <c>LevelController</c></b>（2026-09-13 定）：
    /// ① 目前實力的讀者不只一個——<c>LevelController</c> 拿它出下一題，之後的監控面板與除錯 overlay 要顯示它。
    ///    照 <c>SharedUiState</c> 那四道題的第三道，讀者不只一個就不該住在其中一個讀者身上；
    ///    而 <c>LevelController</c> 自己的 XML doc 就寫著「別人不要跟這支要資料」。
    /// ② ⭐ <b>可測</b>：「結束了嗎、結算過了嗎、有題目嗎、計時停了嗎」這幾道守衛加上更新實力，
    ///    是整張卡最容易寫錯的地方。放在 <c>MonoBehaviour</c> 裡只能靠實機玩，放這裡離線就能驗。
    /// <b>時機仍然由 <c>LevelController</c> 決定</b>（每幀叫一次、換關前再叫一次），它只負責「叫」。
    ///
    /// <b>住在 <see cref="LevelSession"/> 的外殼上</b>：它是整場共用的，不是每關一份——
    /// 每關一份的東西才放 <see cref="LevelData"/>（換關時整包換掉）。
    /// </summary>
    public sealed class SkillRatingTracker
    {
        /// <summary>參數。</summary>
        public readonly SkillRatingSettings Settings;

        /// <summary>
        /// 目前的實力。⚠️ <b>小數，不化整</b>——每局化整一次的話，+12.5 這種變化的誤差會一局一局累積。
        /// 只有「拿去出題」與「寫進存檔」時才四捨五入（<see cref="SkillRating.RoundRating"/>）。
        /// </summary>
        public double CurrentRating { get; private set; }

        /// <summary>下一題的目標難度 = 目前實力四捨五入。夾進出題器範圍是出題器自己的事。</summary>
        public int NextTargetDifficulty => SkillRating.RoundRating(CurrentRating);

        /// <param name="settings">參數。建構時會再檢查一次，不合法的設定進不了結算。</param>
        /// <param name="startingRating">
        /// 起始實力：讀得到存檔就用存檔裡的值，讀不到就傳 <see cref="SkillRatingSettings.initialRating"/>。
        /// 決定用哪個是呼叫端的事——這一層不知道存檔的存在。
        /// </param>
        /// <exception cref="ArgumentNullException">沒有給參數。</exception>
        /// <exception cref="ArgumentException">參數不合法。</exception>
        public SkillRatingTracker(SkillRatingSettings settings, double startingRating)
        {
            Settings = settings ?? throw new ArgumentNullException(nameof(settings));
            settings.ThrowIfInvalid();
            StartingRating = startingRating;
            CurrentRating = startingRating;
            History = new ReadOnlyCollection<SettlementRecord>(history);
        }

        /// <summary>這一場開始時的實力。走勢第一筆的「結算前」必須等於它。</summary>
        public readonly double StartingRating;

        /// <summary>
        /// ⭐ <b>這一場的走勢</b>：每結算一局加一筆，只往後加（P2-15）。
        ///
        /// <b>為什麼放這裡，不讓監控面板自己記</b>（2026-09-14 定）：面板只能每隔一段時間去<b>看</b>
        /// <c>LevelData.Settlement</c>，它不在結算的現場——晚開視窗、中途關掉再開、換關太快
        /// （<c>EnterNextLevel</c> 補結算與換掉 <c>LevelData</c> 在同一格），三種都會漏。
        /// <b>記錄要放在事情發生的地方，不能放在可能在看的人身上。</b>
        ///
        /// ⭐ 跟實力在<b>同一刻</b>更新，所以兩條不變式結構上成立（測試釘住）：
        /// ① 最後一筆的「結算後」＝ <see cref="CurrentRating"/>
        /// ② 每一筆的「結算前」＝ 前一筆的「結算後」，第一筆的 ＝ <see cref="StartingRating"/>
        /// 第二條順便是防線：之後有人在 <see cref="TrySettle"/> 之外改了實力，走勢就接不起來。
        ///
        /// <b>不設上限</b>：一筆十幾個數字，玩一千局才一百 KB 左右。只存在記憶體裡，重開 App 就沒了——
        /// 要不要寫進 <c>PlayerProgress.json</c> 是 P3 的決定。
        /// ⚠️ 回饋收集場景也會照記，沒人讀、無害。
        /// </summary>
        public readonly IReadOnlyList<SettlementRecord> History;

        /// <summary><see cref="History"/> 底下那一份。外面拿到的是唯讀包裝，轉型也改不動。</summary>
        private readonly List<SettlementRecord> history = new List<SettlementRecord>();

        /// <summary>
        /// 看看這一關要不要結算；要的話就結算、記進 <see cref="LevelData.Settlement"/>、更新實力。
        ///
        /// <b>每幀叫也沒關係</b>——結算過一次之後就只剩幾個欄位的判斷。
        ///
        /// <b>不結算的情況</b>（全部回 false、實力不動）：
        /// <list type="bullet">
        /// <item>沒有這一關，或<b>沒有題目</b>（出題工具那個空方框，難度是 0——拿 0 去算預期表現會出事）</item>
        /// <item><b>還沒結束</b>。關掉 App 走人永遠走到這裡，所以那種不算輸（2026-09-10 定）</item>
        /// <item><b>已經結算過</b>。不擋的話同一局會被算兩遍</item>
        /// <item>⚠️ <b>計時還在跑</b>。前景秒數只在「停止」時才累加，還在跑就讀它，拿到的是少算的時間、
        ///   實際表現偏高。正常流程不會發生（過關與放棄都會停計時），這一條擋的是之後新加的結束路徑——
        ///   <b>寧可不結算（彈窗上的方向一直空白，看得出來），也不要安靜地算錯</b></item>
        /// </list>
        /// </summary>
        /// <param name="level">這一關。</param>
        /// <param name="update">結算了的話是明細；沒結算時是預設值，不要讀。</param>
        /// <returns>這一次有沒有真的結算。</returns>
        public bool TrySettle(LevelData level, out RatingUpdate update)
        {
            update = default;
            if (level == null || level.Puzzle == null) return false;
            if (!level.IsFinished || level.Settlement.HasValue || level.IsTiming) return false;

            update = SkillRating.Settle(
                CurrentRating,
                level.Difficulty,
                level.IsSolved ? LevelOutcome.Solved : LevelOutcome.Abandoned,
                level.ForegroundSeconds,
                level.DragSeconds,
                level.HintsUsed,
                Settings);

            // 真相的守衛在 LevelData（它才是那一格的擁有者），這裡照它的回傳值走
            if (!level.RecordSettlement(update)) return false;

            // ⚠️ 這兩行不要拆開：走勢的兩條不變式靠的就是「實力與紀錄在同一刻變」
            CurrentRating = update.RatingAfter;
            history.Add(new SettlementRecord(history.Count + 1, level.Generation, update));
            return true;
        }
    }
}
