using System;

namespace Tangram.Data
{
    /// <summary>
    /// 一次「照難度出題」的下場。<b>三種刻意分開</b>——
    /// 合成一個 bool 的話，呼叫端就沒辦法誠實顯示「這不是你要的那個難度」。
    /// </summary>
    public enum TargetedGenerationOutcome
    {
        /// <summary>正中：難度落在容許範圍裡。</summary>
        OnTarget,

        /// <summary>
        /// 將就：預算用完了，回的是試過的那些裡面**最接近**的一題。
        /// 題目與參考解都是好的，只是難度不是你要的。
        /// </summary>
        SettledForNearest,

        /// <summary>失敗：預算用完，而且**一題都沒生出來**（全被拼不起來／太醜／重複擋掉）。</summary>
        Failed,
    }

    /// <summary>
    /// <b>出題器實際到得了的目標難度範圍</b>（2026-09-14 從常數搬進權重檔）。要求生成時的目標先夾進這裡。
    ///
    /// <b>數字怎麼量的</b>：基準批次（種子 20260910）裡<b>預設門檻收的那 845 題</b>，
    /// 在目前的尺上取第 1 與第 99 百分位（最近排名法）。只量篩過的那一群，因為出題器只會交出篩過的形狀。
    ///
    /// ⚠️ <b>為什麼住在權重檔，不是程式常數</b>：它是在「目前的尺」上量的，權重一改就過期，
    /// 跟換算倍率是同一種東西。放在權重檔，重算工具（<c>DifficultyEvaluatorTests.尺度_權重改過之後重算換算倍率</c>）
    /// 就能一起算、一起寫回；放程式裡的話要記得手動回來改，而忘了的症狀是「高實力玩家常拿到將就的題」，不報錯。
    /// 原本的 760 ～ 1710 查不到是怎麼量的，重算工具量不回來，9/14 換成量得回來的 744 ～ 1660。
    ///
    /// ⚠️ <b>夾的是「要求的目標值」，不是難度值本身</b>——<see cref="DifficultyEvaluator.Score"/> 維持沒有上下限。
    /// 硬截難度值會讓「古典大正方形 2261 分」這種<b>有用的警訊</b>消失。
    ///
    /// <b>是 class 不是 struct</b>：struct 的預設值是 [0, 0]，會安靜地把所有目標夾成 0；
    /// class 的話「沒給」就是 null，而建構子擋掉不合法的範圍，所以<b>拿得到的範圍一定是合法的</b>。
    /// </summary>
    public sealed class TargetDifficultyRange
    {
        /// <summary>下限，大於 0。</summary>
        public readonly int Minimum;

        /// <summary>上限，大於下限。</summary>
        public readonly int Maximum;

        /// <param name="minimum">下限。</param>
        /// <param name="maximum">上限。</param>
        /// <exception cref="ArgumentException">下限不是正數，或上限沒有大於下限。</exception>
        public TargetDifficultyRange(int minimum, int maximum)
        {
            if (minimum <= 0)
                throw new ArgumentException($"目標難度範圍的下限是 {minimum}，必須大於 0", nameof(minimum));
            if (maximum <= minimum)
                throw new ArgumentException($"目標難度範圍是 {minimum} ～ {maximum}，上限必須大於下限", nameof(maximum));
            Minimum = minimum;
            Maximum = maximum;
        }

        /// <summary>把目標難度夾進這個範圍。<b>全專案只有這裡在夾</b>。</summary>
        /// <param name="targetDifficulty">要的目標難度。</param>
        /// <returns>夾過的值。</returns>
        public int Clamp(int targetDifficulty)
        {
            if (targetDifficulty < Minimum) return Minimum;
            if (targetDifficulty > Maximum) return Maximum;
            return targetDifficulty;
        }
    }

    /// <summary>照難度出題的結果。</summary>
    public readonly struct TargetedGenerationResult
    {
        /// <summary>這次的下場。</summary>
        public readonly TargetedGenerationOutcome Outcome;

        /// <summary><see cref="TargetedGenerationOutcome.Failed"/> 時是 null，其餘都有題目。</summary>
        public readonly Puzzle Puzzle;

        /// <summary>
        /// 參考解，**索引就是槽索引**（對應 <see cref="PiecePrototype.TangramSet"/>）。
        /// 跟題目一起正規化過，保證同一個座標系。失敗時是 null。
        /// </summary>
        public readonly Placement[] ReferenceSolution;

        /// <summary>
        /// ⚠️ <b>實際拿到那一題的難度值</b>，不是要求的目標值。失敗時是 0。
        ///
        /// <b>ELO 只能用這個。</b>拿 <see cref="TargetDifficulty"/> 去算的話，
        /// 實力 1900 的玩家（目標被夾到上限 1660）每一場都在對一個不存在的對手算分，
        /// 實力會一路虛漲<b>而且不報錯</b>。
        /// </summary>
        public readonly int Difficulty;

        /// <summary>
        /// 這次實際使用的目標難度，<b>已經夾進權重檔的 <see cref="DifficultyWeights.TargetRange"/></b>。
        /// 要的值超出範圍時，這裡跟傳進去的會不一樣——那不是錯誤，是設計。
        /// </summary>
        public readonly int TargetDifficulty;

        /// <summary>
        /// 叫了幾次出題器才走到這一步。<b>生失敗的那幾次也算</b>（時間確實花掉了）。
        /// 監控面板要顯示它，之後調預算也靠它。
        /// </summary>
        public readonly int AttemptCount;

        /// <param name="outcome">這次的下場。</param>
        /// <param name="puzzle">題目，失敗時傳 null。</param>
        /// <param name="referenceSolution">參考解，失敗時傳 null。</param>
        /// <param name="difficulty">實際拿到那題的難度值，失敗時傳 0。</param>
        /// <param name="targetDifficulty">夾過之後的目標難度。</param>
        /// <param name="attemptCount">叫了幾次出題器。</param>
        public TargetedGenerationResult(TargetedGenerationOutcome outcome, Puzzle puzzle,
                                        Placement[] referenceSolution, int difficulty,
                                        int targetDifficulty, int attemptCount)
        {
            Outcome = outcome;
            Puzzle = puzzle;
            ReferenceSolution = referenceSolution;
            Difficulty = difficulty;
            TargetDifficulty = targetDifficulty;
            AttemptCount = attemptCount;
        }
    }

    /// <summary>
    /// 出題器 第二版（issue #28）：**給一個目標難度，生出落在範圍內的題**——
    /// 一直叫 <see cref="PuzzleGenerator.TryGenerate"/>，算難度，不合就丟掉重生。
    ///
    /// <b>⚠️ 為什麼是包在外面，不是在 <see cref="PuzzleGenerator"/> 裡多一支方法</b>（2026-09-12 定）：
    ///
    /// ① <see cref="PuzzleGenerator.TryGenerate"/> 的「<b>不自己重試</b>」是一份有人在用的契約——
    ///    批次統計要量的就是「試幾次中一題」。同一個類別裡再放一支會重試的方法，
    ///    這個類別就有兩種契約；<b>它不會馬上壞，它會在半年後壞</b>（有人看到兩支很像就順手統一，
    ///    而批次統計會安靜地量到錯的數字）。
    /// ② <b>權重是可替換的資料</b>（校正權重會改 <c>DifficultyWeights.json</c>，改完要跑重算工具）。
    ///    混進出題器之後，改權重就等於改出題器的行為，而批次統計要的正是不受權重影響的原始分布。
    /// ③ 包在外面<b>不需要在出題器上加任何新 API</b>——<see cref="GenerationResult"/> 已經帶回
    ///    題目、參考解與形狀特徵，其餘兩個是靜態函數。
    ///
    /// 💡 <b>這一層也是之後「離線先生好一池難題」要接進來的地方</b>：
    /// 同一個要求「給我一題 1600」，實作從「一直生到中」換成
    /// 「預算用完、要回將就的那一刻先去翻池子」，<b>外面所有呼叫端一行都不用改</b>。
    /// ⚠️ 接上去的時候，「來自題庫」與「將就」<b>要分得出來</b>，
    /// 否則池子會默默蓋掉現場生成的失敗，你就不會知道現場生成到底行不行。
    /// </summary>
    public sealed class TargetedPuzzleGenerator
    {
        /// <summary>
        /// 預設的容許範圍（目標 ±50）。
        /// 100 分寬的帶子玩起來分得出來，而且這個寬度下的生成時間量過：
        /// Debug 建置目標 1200 約 260 ms、1500 約 350 ms、1600 跳到 1.4 秒。
        /// </summary>
        public const int DefaultTolerance = 50;

        /// <summary>
        /// 預設的預算（叫幾次出題器）。
        ///
        /// ⚠️ <b>算次數不算時間</b>：時間在不同機器上不一樣，拿它當上限會讓測試時好時壞；
        /// 次數是決定性的，寫得進測試。200 是量出來的兩倍餘裕——目標 1600 平均要 84 次。
        /// </summary>
        public const int DefaultAttemptBudget = 200;

        private readonly PuzzleGenerator generator;
        private readonly DifficultyWeights weights;

        /// <param name="generator">
        /// 出題器。<b>由呼叫端持有</b>——去重（<c>seenKeys</c>）與亂數是跨呼叫的狀態，
        /// 每次請求新開一個的話，同一題會一直重複出現。
        /// </param>
        /// <param name="weights">
        /// 難度權重，<b>必填</b>。⚠️ 要跟 <c>LevelSession.Load</c> 收到的難度出自同一份——
        /// 現在的做法是<b>直接把這裡算出來的 <see cref="TargetedGenerationResult.Difficulty"/> 傳過去</b>，
        /// 同一題只算一次，兩邊就不可能對不上。
        /// </param>
        /// <exception cref="ArgumentNullException">沒有給出題器或權重。</exception>
        public TargetedPuzzleGenerator(PuzzleGenerator generator, DifficultyWeights weights)
        {
            this.generator = generator ?? throw new ArgumentNullException(nameof(generator));
            this.weights = weights ?? throw new ArgumentNullException(nameof(weights));
        }

        /// <summary>
        /// 生一題，目標難度 <paramref name="targetDifficulty"/>。
        ///
        /// 做法：最多叫 <paramref name="attemptBudget"/> 次出題器，
        /// 第一題落進 [目標 − 容許, 目標 + 容許] 就<b>立刻回傳</b>；
        /// 都沒落進去就回<b>試過的裡面最接近的那一題</b>；一題都沒生出來才算失敗。
        ///
        /// ⚠️ <b>形狀特徵不重算</b>：<see cref="GenerationResult.Features"/> 已經帶回來了，
        /// 這裡用 <see cref="DifficultyEvaluator.Score(in DifficultyFeatures, int, in SolutionFeatures, DifficultyWeights)"/>
        /// 那支「特徵都量好了」的多載。
        ///
        /// 💡 每一輪仍然要付「被外框逼出來的片數」那筆錢（每個形狀約 0.2 ms，是這些特徵裡最貴的）。
        /// 目標 1600 跑 84 輪等於多 17 ms，可接受，<b>先不要為了它把邏輯弄複雜</b>。
        /// 真的嫌慢時的做法是先用周長與轉角數粗篩（那兩項佔分數變異約 77%），差太遠就不算後面的。
        /// </summary>
        /// <param name="targetDifficulty">要的目標難度；超出範圍會被權重檔的 <see cref="DifficultyWeights.TargetRange"/> 夾住。</param>
        /// <param name="tolerance">容許範圍（目標 ±這個值）。</param>
        /// <param name="attemptBudget">最多叫幾次出題器；<b>生失敗的那幾次也算</b>。</param>
        /// <returns>這次的結果，含實際難度值與花掉的次數。</returns>
        /// <exception cref="ArgumentOutOfRangeException">容許範圍是負的，或預算不是正數。</exception>
        public TargetedGenerationResult Generate(int targetDifficulty,
                                                 int tolerance = DefaultTolerance,
                                                 int attemptBudget = DefaultAttemptBudget)
        {
            if (tolerance < 0)
                throw new ArgumentOutOfRangeException(nameof(tolerance), tolerance,
                    "容許範圍不能是負的——負的代表「永遠不可能正中」，那是靜默的死迴圈");
            if (attemptBudget <= 0)
                throw new ArgumentOutOfRangeException(nameof(attemptBudget), attemptBudget,
                    "預算必須是正數——0 次代表一題都不生，回傳的失敗會看起來像生成器壞了");

            int target = weights.TargetRange.Clamp(targetDifficulty);

            // 「目前最接近的」要連它的難度值一起記住。只記題目、最後再算一次的話，
            // 那一次重算會再付一遍 ForcedPieceCountOf 的錢，而且多一個會不一致的地方
            Puzzle nearestPuzzle = null;
            Placement[] nearestSolution = null;
            int nearestDifficulty = 0;
            int nearestDistance = int.MaxValue;

            for (int attempt = 1; attempt <= attemptBudget; attempt++)
            {
                GenerationResult result = generator.TryGenerate();

                // 拼不起來／太醜／重複都算用掉一次預算——時間確實花掉了。
                // 只算成功的那幾次，等於「預算」在慢的那一端會失控
                if (result.Outcome != GenerationOutcome.Generated) continue;

                int difficulty = DifficultyEvaluator.Score(
                    result.Features,
                    DifficultyFeatures.ForcedPieceCountOf(result.Puzzle.Walks),
                    SolutionFeatures.Measure(result.ReferenceSolution),
                    weights);

                int distance = Math.Abs(difficulty - target);
                if (distance <= tolerance)
                    return new TargetedGenerationResult(
                        TargetedGenerationOutcome.OnTarget,
                        result.Puzzle, result.ReferenceSolution, difficulty, target, attempt);

                if (distance < nearestDistance)
                {
                    nearestDistance = distance;
                    nearestPuzzle = result.Puzzle;
                    nearestSolution = result.ReferenceSolution;
                    nearestDifficulty = difficulty;
                }
            }

            // ⚠️ 「將就」與「失敗」不可以合併：前者手上有一題好題目、只是難度不對，
            // 後者連題目都沒有。呼叫端對這兩件事該做的事完全不同
            if (nearestPuzzle == null)
                return new TargetedGenerationResult(
                    TargetedGenerationOutcome.Failed, null, null, 0, target, attemptBudget);

            return new TargetedGenerationResult(
                TargetedGenerationOutcome.SettledForNearest,
                nearestPuzzle, nearestSolution, nearestDifficulty, target, attemptBudget);
        }
    }
}
