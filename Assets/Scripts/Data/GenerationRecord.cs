namespace Tangram.Data
{
    /// <summary>
    /// 這一題<b>是怎麼生出來的</b>（issue #32）：要多難、拿到多難、正中還是將就、試了幾次。
    ///
    /// <b>為什麼要留</b>：「將就」在畫面上看不出來——目標 1700 有 40% 會將就，玩家拿到一題難度不對的題目，
    /// 一切看起來都正常。原本只印在 log 裡，生完就丟了，事後被問起就答不出來。
    ///
    /// ⚠️ <b>刻意不直接存 <see cref="TargetedGenerationResult"/></b>：那裡面帶著題目與參考解，
    /// 而那份參考解是<b>題目自己的座標</b>，<c>LevelData.ReferenceSolution</c> 是<b>平移過的盤面座標</b>。
    /// 兩份參考解並存在不同座標系，就是「兩個地方各自認定真相」那個坑。所以只抄四個數字。
    ///
    /// <b>三種進關方式</b>：
    /// <list type="bullet">
    /// <item>照難度生成功（正中或將就）→ 有紀錄</item>
    /// <item>⭐ <b>生成失敗、退回大正方形</b> → 也有紀錄，下場是失敗。不標出來的話，
    ///   走勢上那一跳（大正方形 2261 分）會看起來像 ELO 壞了</item>
    /// <item>回饋場景的題單、練習題、出題工具 → 沒有紀錄（<c>LevelData.Generation</c> 是 null）</item>
    /// </list>
    /// </summary>
    public readonly struct GenerationRecord
    {
        /// <summary>這次的下場。</summary>
        public readonly TargetedGenerationOutcome Outcome;

        /// <summary>夾過之後的目標難度。</summary>
        public readonly int TargetDifficulty;

        /// <summary>
        /// 出題器拿到的那一題的難度。⚠️ <b>失敗時是 0</b>——那時玩家拿到的是退路的題目，
        /// 難度看 <c>LevelData.Difficulty</c>，不是這裡。
        /// </summary>
        public readonly int GeneratedDifficulty;

        /// <summary>叫了幾次出題器（生失敗的也算）。</summary>
        public readonly int AttemptCount;

        /// <param name="outcome">下場。</param>
        /// <param name="targetDifficulty">夾過之後的目標難度。</param>
        /// <param name="generatedDifficulty">拿到的難度，失敗時是 0。</param>
        /// <param name="attemptCount">叫了幾次。</param>
        public GenerationRecord(TargetedGenerationOutcome outcome, int targetDifficulty,
                                int generatedDifficulty, int attemptCount)
        {
            Outcome = outcome;
            TargetDifficulty = targetDifficulty;
            GeneratedDifficulty = generatedDifficulty;
            AttemptCount = attemptCount;
        }

        /// <summary>從出題結果抄四個數字，題目與參考解不抄。</summary>
        /// <param name="result">出題結果。</param>
        /// <returns>出題紀錄。</returns>
        public static GenerationRecord From(in TargetedGenerationResult result) =>
            new GenerationRecord(result.Outcome, result.TargetDifficulty, result.Difficulty, result.AttemptCount);

        /// <summary>拿到的難度離目標多遠（絕對值）。失敗時沒有意義。</summary>
        public int DistanceFromTarget => System.Math.Abs(GeneratedDifficulty - TargetDifficulty);
    }
}
