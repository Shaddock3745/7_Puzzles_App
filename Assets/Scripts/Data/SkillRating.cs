using System;

namespace Tangram.Data
{
    /// <summary>一局怎麼結束的。<b>關掉 App 走人不在這裡</b>——那種不結算（見 <see cref="SkillRating"/>）。</summary>
    public enum LevelOutcome
    {
        /// <summary>過關 = 贏，實際表現看花了多少時間。</summary>
        Solved,

        /// <summary>按了放棄 = 輸，實際表現是 0。</summary>
        Abandoned,
    }

    /// <summary>
    /// 下一題大概會比這一題難還是簡單。<b>玩家端只看得到這個，看不到分數</b>（2026-09-13 定，A 案）。
    /// </summary>
    public enum NextPuzzleTrend
    {
        /// <summary>下一題的目標比這一題難超過容許範圍。</summary>
        Harder,

        /// <summary>落在容許範圍內——下一題真的可能跟這一題一樣難。</summary>
        Similar,

        /// <summary>下一題的目標比這一題簡單超過容許範圍。</summary>
        Easier,
    }

    /// <summary>
    /// 一局的結算明細。<b>每一個中間值都留著</b>——P2-15 的監控面板要回答「為什麼 +13」，靠的就是這一份。
    /// </summary>
    public readonly struct RatingUpdate
    {
        /// <summary>這一局怎麼結束的。</summary>
        public readonly LevelOutcome Outcome;

        /// <summary>這一題的難度 D。⚠️ <b>實際拿到那一題的</b>，不是目標值。</summary>
        public readonly int Difficulty;

        /// <summary>結算前的實力 R。</summary>
        public readonly double RatingBefore;

        /// <summary>預期表現 E，0 ～ 1。照實力與難度，這一局「該」表現多好。</summary>
        public readonly double ExpectedPerformance;

        /// <summary>
        /// 時間代價 C（秒）= 前景 − (1 − r) × 拖曳 ＋ 提示次數 × P。
        /// <b>放棄時也照算</b>——實際表現用不到它，但面板可能想顯示「卡了多久」。
        /// </summary>
        public readonly double TimeCostSeconds;

        /// <summary>實際表現 S，0 ～ 1。過關 = T ÷ (T + C)，放棄 = 0。</summary>
        public readonly double ActualPerformance;

        /// <summary>實際用的 K（已經乘過展示倍率）。</summary>
        public readonly double EffectiveKFactor;

        /// <summary>實力變化 ΔR = K × (S − E)。<b>小數，不化整</b>。</summary>
        public readonly double RatingChange;

        // ───────── 結算的輸入（P2-15 加）─────────
        //
        // ⭐ 原本只記了時間代價 C，沒記 C 是怎麼算出來的。正在玩的這一關那三個數字還在 LevelData 上，
        // 但走勢裡的舊局 LevelData 已經換掉了——「第 2 局的 C 為什麼是 42 秒」答不出來。
        // 原樣存下來之後，一份明細自己就能解釋自己。

        /// <summary>結算時的前景秒數。</summary>
        public readonly double ForegroundSeconds;

        /// <summary>結算時的拖曳秒數（已經套過單次上限）。</summary>
        public readonly double DragSeconds;

        /// <summary>結算時用了幾次提示。</summary>
        public readonly int HintsUsed;

        /// <param name="outcome">結局。</param>
        /// <param name="difficulty">題目難度。</param>
        /// <param name="ratingBefore">結算前的實力。</param>
        /// <param name="expectedPerformance">預期表現。</param>
        /// <param name="timeCostSeconds">時間代價。</param>
        /// <param name="actualPerformance">實際表現。</param>
        /// <param name="effectiveKFactor">實際用的 K。</param>
        /// <param name="ratingChange">實力變化。</param>
        /// <param name="foregroundSeconds">前景秒數。⚠️ 三個輸入刻意放最後：前面已經一串 double，插在中間的話舊的呼叫端位置錯開也編得過。</param>
        /// <param name="dragSeconds">拖曳秒數。</param>
        /// <param name="hintsUsed">提示次數。</param>
        public RatingUpdate(LevelOutcome outcome, int difficulty, double ratingBefore,
                            double expectedPerformance, double timeCostSeconds, double actualPerformance,
                            double effectiveKFactor, double ratingChange,
                            double foregroundSeconds, double dragSeconds, int hintsUsed)
        {
            Outcome = outcome;
            Difficulty = difficulty;
            RatingBefore = ratingBefore;
            ExpectedPerformance = expectedPerformance;
            TimeCostSeconds = timeCostSeconds;
            ActualPerformance = actualPerformance;
            EffectiveKFactor = effectiveKFactor;
            RatingChange = ratingChange;
            ForegroundSeconds = foregroundSeconds;
            DragSeconds = dragSeconds;
            HintsUsed = hintsUsed;
        }

        /// <summary>結算後的實力 = 結算前 ＋ 變化。</summary>
        public double RatingAfter => RatingBefore + RatingChange;

        /// <summary>
        /// 下一題會比這一題難還是簡單。
        ///
        /// 比的是「<b>下一題實際會拿去出題的目標</b> − 這一題的難度」，不是 ΔR 的正負——玩家問的是下一題。
        /// ⚠️ 目標要先<b>夾進出題器的範圍</b>：實力 1800 的玩家目標會被夾到 1710，
        /// 不夾的話這一題 1705 也會顯示「會難一點」，但下一題其實一樣難。
        ///
        /// 門檻直接沿用出題器的容許範圍 ±50，不多一個參數：落在範圍內時下一題真的可能跟這一題一樣難。
        /// ⚠️ 正式版 K 小，大部分時候會是「差不多」——那是真的，不是 bug。
        /// </summary>
        public NextPuzzleTrend Trend
        {
            get
            {
                int nextTarget = TargetedPuzzleGenerator.ClampTarget(SkillRating.RoundRating(RatingAfter));
                int gap = nextTarget - Difficulty;
                if (gap > TargetedPuzzleGenerator.DefaultTolerance) return NextPuzzleTrend.Harder;
                if (gap < -TargetedPuzzleGenerator.DefaultTolerance) return NextPuzzleTrend.Easier;
                return NextPuzzleTrend.Similar;
            }
        }
    }

    /// <summary>
    /// ELO 換算（P2-14，issue #31）。<b>只做算術，沒有狀態</b>——誰持有實力、什麼時候結算，是
    /// <c>SkillRatingTracker</c> 與 <c>LevelController</c> 的事。
    ///
    /// <code>
    /// E = 1 ÷ (1 + 10^((D − R) ÷ 400))
    /// C = 前景 − (1 − r) × 拖曳 ＋ 提示次數 × P
    /// S = 過關：T ÷ (T + C)；放棄：0
    /// ΔR = 基準 K × 展示倍率 × (S − E)
    /// </code>
    ///
    /// ⭐ <b>實力的意思是「你大約 T 秒解完的難度」</b>。出題器在實力 ±50 內出題，所以 E 幾乎都在 0.5 附近，
    /// 公式實務上是 ΔR ≈ K × (S − 0.5)：<b>比 T 快加分，比 T 慢扣分——解完了也會扣</b>。
    /// 否決的另一種意思（解完一律不扣）會讓實力只有放棄才下降、題目一路變難直到玩家開始頻繁投降，
    /// 把兒童推進挫折區。玩家端所以只顯示方向（<see cref="RatingUpdate.Trend"/>），不顯示分數。
    ///
    /// ⭐ <b>兩個結構上的保證</b>（<c>SkillRatingTests</c> 釘住）：
    /// ① <b>繼續解永遠不比放棄差</b>——過關的 S &gt; 0（T 與 C 都有限），放棄的 S = 0；提示只加在 C 上，所以用光提示也一樣
    /// ② 放棄 = −K × E，<b>放棄簡單題很痛、放棄難題不太痛</b>，沒有另外的「扣多少」參數
    ///
    /// ⚠️ <b>要寫進系統概述文件的誠實聲明</b>：展示版 K 是正式設定的 10 倍；關掉 App 不算輸，所以實力值系統性偏高；
    /// 目前只以時間與放棄為輸入，其餘指標已在記錄但尚未納入；T 與 r 是開發者估的，待真人資料校正。
    /// </summary>
    public static class SkillRating
    {
        /// <summary>
        /// 結算一局。
        /// </summary>
        /// <param name="ratingBefore">結算前的實力。</param>
        /// <param name="difficulty">⚠️ <b>實際拿到那一題的難度</b>（<c>LevelData.Difficulty</c>），不是目標值。</param>
        /// <param name="outcome">怎麼結束的。</param>
        /// <param name="foregroundSeconds">前景累積秒數（<b>計時已經停了</b>之後的值）。</param>
        /// <param name="dragSeconds">拖曳累積秒數（已經套過單次上限）。</param>
        /// <param name="hintsUsed">用了幾次提示。</param>
        /// <param name="settings">參數。</param>
        /// <returns>結算明細。</returns>
        /// <exception cref="ArgumentNullException">沒有給參數。</exception>
        /// <exception cref="ArgumentException">
        /// 秒數或提示次數是負的，或<b>拖曳比前景長</b>——那違反 P2-13 的不變式（背景時不可能拖曳），
        /// 代表某個計時的呼叫點接錯了。不擋的話時間代價會變成負的、實際表現超過 1，<b>實力安靜地虛漲</b>。
        /// </exception>
        public static RatingUpdate Settle(double ratingBefore, int difficulty, LevelOutcome outcome,
                                          double foregroundSeconds, double dragSeconds, int hintsUsed,
                                          SkillRatingSettings settings)
        {
            if (settings == null) throw new ArgumentNullException(nameof(settings));
            if (!(foregroundSeconds >= 0.0) || !(dragSeconds >= 0.0) || hintsUsed < 0)
                throw new ArgumentException(
                    $"SkillRating.Settle：前景 {foregroundSeconds}、拖曳 {dragSeconds}、提示 {hintsUsed} 次，不能是負的");
            if (dragSeconds > foregroundSeconds)
                throw new ArgumentException(
                    $"SkillRating.Settle：拖曳 {dragSeconds} 秒比前景 {foregroundSeconds} 秒還長——" +
                    "拖曳 ≤ 前景是 P2-13 的不變式，一定是某個計時的呼叫點接錯了");

            double expected = ExpectedPerformance(ratingBefore, difficulty, settings.ratingScale);

            // 每拖一秒退 (1 − r) 秒。r = 1 時拖曳完全不影響，只看前景
            double timeCost = foregroundSeconds
                              - (1.0 - settings.dragWeightRatio) * dragSeconds
                              + hintsUsed * settings.hintPenaltySeconds;

            double actual = outcome == LevelOutcome.Solved
                ? settings.referenceSeconds / (settings.referenceSeconds + timeCost)
                : 0.0;

            double kFactor = settings.EffectiveKFactor;
            return new RatingUpdate(outcome, difficulty, ratingBefore, expected, timeCost, actual,
                                    kFactor, kFactor * (actual - expected),
                                    foregroundSeconds, dragSeconds, hintsUsed);
        }

        /// <summary>預期表現 E = 1 ÷ (1 + 10^((D − R) ÷ 尺度))。實力等於難度時是 0.5。</summary>
        /// <param name="rating">實力 R。</param>
        /// <param name="difficulty">難度 D。</param>
        /// <param name="ratingScale">尺度，標準值 400。</param>
        /// <returns>0 ～ 1。</returns>
        public static double ExpectedPerformance(double rating, int difficulty, double ratingScale) =>
            1.0 / (1.0 + Math.Pow(10.0, (difficulty - rating) / ratingScale));

        /// <summary>
        /// 實力換成整數：下一題的目標、寫進存檔都用這一支。
        /// ⚠️ <b>一定要指定「遠離零」</b>——C# 的 <c>Math.Round</c> 預設是銀行家捨入（12.5 → 12、13.5 → 14），
        /// 同樣是 .5 有時進有時捨，看起來像是隨機少一分。
        /// </summary>
        /// <param name="rating">實力（小數）。</param>
        /// <returns>四捨五入後的整數。</returns>
        public static int RoundRating(double rating) =>
            (int)Math.Round(rating, MidpointRounding.AwayFromZero);
    }
}
