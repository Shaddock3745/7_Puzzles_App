using System;
using System.Collections.Generic;
using Tangram.Data;

namespace Tangram.Core
{
    /// <summary>監控面板現在處在哪個狀態。照順序判斷，第一個成立的就是答案。</summary>
    public enum MonitorState
    {
        /// <summary>沒有這一場：沒在 Play，或場景裡沒有 <c>LevelController</c>。</summary>
        NoSession,

        /// <summary>有這一場，但還沒進任何一關。</summary>
        NoLevel,

        /// <summary>有關卡但沒有題目——出題工具那個空方框。</summary>
        NoPuzzle,

        /// <summary>
        /// 這一關<b>不結算實力</b>（<see cref="LevelData.CountsTowardRating"/> 是 false，目前只有掃描題，2026-10-14）。
        /// <b>玩完沒有都停在這裡</b>——不分這一條的話，玩完會掉進 <see cref="AwaitingSettlement"/> 而且一直停著，
        /// 看起來就是「被守衛擋下了」那個 bug 訊號，其實是正常的。
        /// </summary>
        NotRated,

        /// <summary>還在玩。</summary>
        Playing,

        /// <summary>
        /// 結束了但還沒結算。正常情況只有一瞬間（晚一幀）。
        /// ⚠️ <b>一直停在這裡</b>就代表被 <see cref="SkillRatingTracker.TrySettle"/> 的守衛擋下了（例如計時沒停），
        /// 跟彈窗上方向一直空白是同一個症狀。這一層不另外判斷，看得出來就夠了。
        /// </summary>
        AwaitingSettlement,

        /// <summary>結算完了。</summary>
        Settled,
    }

    /// <summary>
    /// 監控面板<b>某一刻</b>要顯示的全部數字（P2-15，issue #32）。只是一份快照，不持有任何狀態。
    ///
    /// <b>這一層只給數字，不格式化</b>：小數點幾位、顏色、排版是視窗的事（也是美術，往後放）。
    /// 反過來說，會算錯的東西——狀態怎麼判、公式代進去的是哪幾個數、對帳過了沒——全在這裡，
    /// 因為視窗在 Editor 資料夾裡，<c>dotnet</c> 編不到。決賽真的要進 APP 的話，換的也只有視窗那層。
    /// </summary>
    public sealed class LevelMonitorReport
    {
        /// <summary>現在的狀態。</summary>
        public readonly MonitorState State;

        /// <summary>
        /// 這一關。<see cref="MonitorState.NoSession"/> 與 <see cref="MonitorState.NoLevel"/> 時是 null。
        /// ⚠️ 跟 <c>LevelSession.Data</c> 的規矩一樣：<b>不要存起來</b>，每次跟報告拿。
        /// </summary>
        public readonly LevelData Level;

        /// <summary>難度拆解。沒有題目、或沒有權重時是 null。</summary>
        public readonly DifficultyBreakdown Breakdown;

        /// <summary>
        /// 拆解跟 <c>LevelData.Difficulty</c> 對不對得上。<b>沒有拆解時是 null</b>（不是 false——沒對帳不等於對不上）。
        /// </summary>
        public readonly bool? BreakdownMatchesDifficulty;

        /// <summary>實力紀錄。出題工具場景、或 ELO 設定讀不到時是 null。</summary>
        public readonly SkillRatingTracker Rating;

        /// <summary>
        /// ⭐ <b>預覽</b>：照<b>現在的實力</b>，這一題的預期表現 E。只在 <see cref="MonitorState.Playing"/> 且有實力紀錄時有值。
        /// 讓評審在玩家還沒解完之前就看到「系統認為你有 52% 的表現」。
        /// <see cref="MonitorState.NotRated"/> 時也沒有值：這一局根本不會拿它來比。
        /// ⚠️ 結算之後刻意沒有值：那時實力已經更新了，拿新的實力算這一題的 E 會跟結算明細裡的 E 不一樣，兩個數字並排會讓人以為算錯。
        /// </summary>
        public readonly double? PreviewExpectedPerformance;

        /// <summary>
        /// 到這一刻為止的前景秒數，<b>含還在跑的那一段</b>（<c>ForegroundSecondsAt</c>）。
        /// 沒有關卡時是 0。
        /// </summary>
        public readonly double ForegroundSecondsSoFar;

        /// <summary>
        /// 下一題的目標難度，<b>已經夾進出題器的範圍</b>。
        ///
        /// <b>兩個來源，照順序</b>（2026-09-14 改）：
        /// ① 這一關<b>結算過了</b> → 直接讀明細的 <see cref="RatingUpdate.NextTargetDifficulty"/>，
        ///    跟 <see cref="RatingUpdate.Trend"/> 比的是<b>同一個欄位</b>，不是另外算一個碰巧相等的數字
        /// ② 還沒結算 → 用 <see cref="TargetRange"/> 夾目前的實力
        ///
        /// 沒有實力紀錄時是 null；還沒結算而且沒有範圍（沒有權重）時也是 null。
        /// </summary>
        public readonly int? NextTargetDifficulty;

        /// <summary>這一場的目標難度範圍（跟出題用的同一份權重）。沒有權重時是 null。</summary>
        public readonly TargetDifficultyRange TargetRange;

        /// <summary>
        /// ⭐ 這一關暫停中（中途離開，2026-10-23）：主畫面蓋著、不計時。暫停的真相在 <c>LevelController</c>，這裡只是轉述給面板看。
        /// <b>不另外開一個狀態</b>：暫停的關照樣是「還在玩」，預期表現的預覽照樣成立，只是時間不走。
        /// </summary>
        public readonly bool IsPaused;

        private LevelMonitorReport(MonitorState state, LevelData level, DifficultyBreakdown breakdown,
                                   SkillRatingTracker rating, double? previewExpectedPerformance,
                                   double foregroundSecondsSoFar, int? nextTargetDifficulty,
                                   TargetDifficultyRange targetRange, bool isPaused)
        {
            IsPaused = isPaused;
            TargetRange = targetRange;
            State = state;
            Level = level;
            Breakdown = breakdown;
            BreakdownMatchesDifficulty = breakdown != null && level != null ? breakdown.Matches(level.Difficulty) : (bool?)null;
            Rating = rating;
            PreviewExpectedPerformance = previewExpectedPerformance;
            ForegroundSecondsSoFar = foregroundSecondsSoFar;
            NextTargetDifficulty = nextTargetDifficulty;
        }

        /// <summary>這一局的結算明細。還沒結算是 null。</summary>
        public RatingUpdate? Settlement => Level?.Settlement;

        /// <summary>這一題的出題紀錄。不是出題器生的、或沒有關卡時是 null。</summary>
        public GenerationRecord? Generation => Level?.Generation;

        /// <summary>ELO 參數。沒有實力紀錄時是 null。</summary>
        public SkillRatingSettings Settings => Rating?.Settings;

        /// <summary>這一場的走勢。沒有實力紀錄時是空的，<b>不是 null</b>——視窗不必每次判斷。</summary>
        public IReadOnlyList<SettlementRecord> History =>
            Rating != null ? Rating.History : Array.Empty<SettlementRecord>();

        /// <summary>
        /// ⚠️ <b>展示倍率不是 1</b>。系統概述文件要寫明展示版 K 是正式設定的 10 倍，
        /// 面板上也要看得出來，不能讓影片裡的大幅跳動被誤會成正式設定。
        /// </summary>
        public bool IsDemonstrationMultiplierActive => Settings != null && Settings.demonstrationMultiplier != 1.0;

        /// <summary>
        /// 組一份報告。
        /// </summary>
        /// <param name="session">這一場；沒在 Play 就傳 null。</param>
        /// <param name="breakdown">這一關的難度拆解；沒有就傳 null。由 <see cref="LevelMonitor"/> 快取，換關才重算。</param>
        /// <param name="targetRange">這一場的目標難度範圍；沒有權重就傳 null。</param>
        /// <param name="nowSeconds">現在的時刻（秒），跟局內統計用同一個時鐘。</param>
        /// <param name="isPaused">這一關是不是暫停中（2026-10-23，中途離開）；殼跟 <c>LevelController.IsLevelPaused</c> 拿。</param>
        /// <returns>報告。</returns>
        public static LevelMonitorReport Build(LevelSession session, DifficultyBreakdown breakdown,
                                               TargetDifficultyRange targetRange, double nowSeconds, bool isPaused = false)
        {
            if (session == null)
                return new LevelMonitorReport(MonitorState.NoSession, null, null, null, null, 0.0, null, targetRange, false);

            SkillRatingTracker rating = session.Rating;
            LevelData level = session.Data;
            int? nextTarget = NextTargetOf(rating, level, targetRange);

            if (level == null)
                return new LevelMonitorReport(MonitorState.NoLevel, null, null, rating, null, 0.0, nextTarget, targetRange, false);

            double foreground = level.ForegroundSecondsAt(nowSeconds);

            // 沒有題目就沒有難度可以拆（出題工具的空方框難度是 0），傳進來的拆解也不收
            if (level.Puzzle == null)
                return new LevelMonitorReport(MonitorState.NoPuzzle, level, null, rating, null, foreground, nextTarget, targetRange, false);

            // ⚠️ 不算實力的要排在最前面：它玩完也不會有明細，往下判就會變成「結算中」
            MonitorState state = !level.CountsTowardRating ? MonitorState.NotRated
                : !level.IsFinished ? MonitorState.Playing
                : !level.Settlement.HasValue ? MonitorState.AwaitingSettlement
                : MonitorState.Settled;

            double? preview = state == MonitorState.Playing && rating != null
                ? SkillRating.ExpectedPerformance(rating.CurrentRating, level.Difficulty, rating.Settings.ratingScale)
                : (double?)null;

            // 暫停只對還沒結束的關有意義（結束了的關不會被暫停，這一行是保險）
            return new LevelMonitorReport(state, level, breakdown, rating, preview, foreground, nextTarget, targetRange,
                                          isPaused && !level.IsFinished);
        }

        /// <summary>下一題目標的兩個來源，見 <see cref="NextTargetDifficulty"/>。</summary>
        /// <param name="rating">實力紀錄，可以是 null。</param>
        /// <param name="level">這一關，可以是 null。</param>
        /// <param name="targetRange">範圍，可以是 null。</param>
        /// <returns>夾過的下一題目標；算不出來時是 null。</returns>
        private static int? NextTargetOf(SkillRatingTracker rating, LevelData level, TargetDifficultyRange targetRange)
        {
            if (rating == null) return null;
            if (level != null && level.Settlement.HasValue) return level.Settlement.Value.NextTargetDifficulty;
            return targetRange?.Clamp(rating.NextTargetDifficulty);
        }
    }

    /// <summary>
    /// 監控面板的資料那半（P2-15，issue #32）：<b>持有難度拆解的快取</b>，每次要就組一份新的報告。
    ///
    /// <b>為什麼要快取</b>：拆解要重量特徵，「被外框逼出來的片數」每題約 0.2 ms。視窗一秒刷新 10 次，
    /// 每次都算就是每秒 2 ms 白花。<b>換關（<c>LevelData</c> 換了一個）或換了權重才重算</b>。
    ///
    /// ⚠️ <b>比的是參照不是內容</b>：<c>LevelData</c> 換關時整包換掉，新的一定是另一個物件；
    /// 權重也是同一個實例整場共用。所以比參照剛好就是「是不是同一題、同一把尺」。
    ///
    /// <b>它只讀，絕對不寫</b>——跟 <c>DebugOverlay</c> 同一條規矩。除錯工具一旦會改東西，它自己就變成 bug 來源。
    /// </summary>
    public sealed class LevelMonitor
    {
        private LevelData cachedLevel;
        private DifficultyWeights cachedWeights;
        private DifficultyBreakdown cachedBreakdown;

        /// <summary>拆解實際算了幾次。只給測試驗快取用。</summary>
        public int BreakdownComputationCount { get; private set; }

        /// <summary>組一份報告。</summary>
        /// <param name="session">這一場；沒在 Play 就傳 null。</param>
        /// <param name="weights">
        /// 這一場用的權重，<b>要跟出題用的是同一個實例</b>（遊戲裡跟 <c>LevelController.CurrentDifficultyWeights</c> 拿）。
        /// 自己再讀一次檔的話，Play 中改了 JSON 就會變成兩份，對帳會亮，而原因很難想到。沒有就傳 null。
        /// </param>
        /// <param name="nowSeconds">現在的時刻（秒）。</param>
        /// <param name="isPaused">這一關是不是暫停中（2026-10-23），見 <see cref="LevelMonitorReport.IsPaused"/>。</param>
        /// <returns>報告。</returns>
        public LevelMonitorReport Build(LevelSession session, DifficultyWeights weights, double nowSeconds, bool isPaused = false)
        {
            LevelData level = session?.Data;
            return LevelMonitorReport.Build(session, BreakdownFor(level, weights), weights?.TargetRange, nowSeconds, isPaused);
        }

        /// <param name="level">這一關。</param>
        /// <param name="weights">權重。</param>
        /// <returns>拆解；沒有題目、沒有參考解或沒有權重時是 null。</returns>
        private DifficultyBreakdown BreakdownFor(LevelData level, DifficultyWeights weights)
        {
            if (level == null || level.Puzzle == null || level.ReferenceSolution == null || weights == null)
            {
                // 不清快取也行，但清掉比較不會留著一份指向舊關卡的東西（LevelData 不准被存起來，見它的說明）
                cachedLevel = null;
                cachedWeights = null;
                cachedBreakdown = null;
                return null;
            }

            if (ReferenceEquals(level, cachedLevel) && ReferenceEquals(weights, cachedWeights))
                return cachedBreakdown;

            cachedBreakdown = DifficultyBreakdown.Measure(level.Puzzle, level.ReferenceSolution, weights);
            cachedLevel = level;
            cachedWeights = weights;
            BreakdownComputationCount++;
            return cachedBreakdown;
        }
    }
}
