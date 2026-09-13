using System;
using Tangram.Data;

namespace Tangram.Core
{
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
            CurrentRating = startingRating;
        }

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

            CurrentRating = update.RatingAfter;
            return true;
        }
    }
}
