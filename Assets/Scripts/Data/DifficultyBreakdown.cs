using System;
using System.Collections.Generic;

namespace Tangram.Data
{
    /// <summary>
    /// 難度公式裡<b>一個特徵</b>的明細：原始值怎麼一路變成「這一項貢獻幾分」。
    /// </summary>
    public readonly struct DifficultyTermContribution
    {
        /// <summary>特徵的鍵，見 <see cref="DifficultyTermKeys"/>。</summary>
        public readonly string Key;

        /// <summary>這一題量到的原始數字。</summary>
        public readonly double RawValue;

        /// <summary>基準批次的平均。</summary>
        public readonly double Mean;

        /// <summary>基準批次的標準差。</summary>
        public readonly double Deviation;

        /// <summary>
        /// 離平均幾個標準差 =（原始值 − 平均）÷ 標準差，<b>還沒乘方向</b>。
        /// ⚠️ 權重 0 的特徵標準差可能是 0（讀檔時不檢查），那時這一格是 <c>NaN</c>——它不算分，所以無所謂。
        /// </summary>
        public readonly double StandardScore;

        /// <summary>權重。</summary>
        public readonly double Weight;

        /// <summary>+1 = 數字越大越難，−1 = 越小越難。</summary>
        public readonly int Direction;

        /// <summary>
        /// 這一項在加權和裡的份量 = 權重 × 方向 ×（原始值 − 平均）÷ 標準差。權重 0 時是 0。
        /// ⚠️ 算式的先後順序刻意跟 <see cref="DifficultyEvaluator.WeightedSum"/> 一字不差，
        /// 所以照順序加起來跟它<b>完全相等</b>（測試釘住）。
        /// </summary>
        public readonly double WeightedTerm;

        /// <summary>
        /// ⭐ <b>換成分數是幾分</b> = 份量 × 間距 ÷ 加權和的標準差。
        /// 給人看的數字：「基準 1200，周長 +66，轉角 −40……」。
        /// </summary>
        public readonly double Points;

        /// <param name="key">特徵的鍵。</param>
        /// <param name="rawValue">原始數字。</param>
        /// <param name="mean">平均。</param>
        /// <param name="deviation">標準差。</param>
        /// <param name="standardScore">離平均幾個標準差。</param>
        /// <param name="weight">權重。</param>
        /// <param name="direction">方向。</param>
        /// <param name="weightedTerm">加權和裡的份量。</param>
        /// <param name="points">換成分數是幾分。</param>
        public DifficultyTermContribution(string key, double rawValue, double mean, double deviation,
                                          double standardScore, double weight, int direction,
                                          double weightedTerm, double points)
        {
            Key = key;
            RawValue = rawValue;
            Mean = mean;
            Deviation = deviation;
            StandardScore = standardScore;
            Weight = weight;
            Direction = direction;
            WeightedTerm = weightedTerm;
            Points = points;
        }
    }

    /// <summary>
    /// <b>這一題為什麼是這個分數</b>（P2-15，issue #32）：每個特徵各一列，加上總和與對帳。
    ///
    /// <b>它不是難度的來源，是解釋。</b>難度的真相仍然是出題的人算好、存進 <c>LevelData.Difficulty</c> 的那個整數；
    /// 這裡從題目與參考解<b>重量一次</b>，再問「算出來的跟存著的一不一樣」（<see cref="Matches"/>）。
    ///
    /// ⚠️ <b>這是刻意的重算</b>，看起來違反 2026-09-13 定的「同一題只算一次」。那條要擋的是
    /// 「兩個數字不一樣而沒人發現」——ELO 用其中一個、畫面顯示另一個。這裡的重算只拿來顯示，
    /// 而且<b>一定跟存著的那個對帳</b>，所以兩者不一樣時會響，不會安靜。對不上的可能原因：
    /// <list type="bullet">
    /// <item>出題器量特徵的方式跟這裡不一樣了（有人改了其中一邊）</item>
    /// <item>參考解平移到盤面座標之後某個特徵跟著變了（原點現在是零所以不會，原點規則一改就會）</item>
    /// <item>Play 中改了權重檔，而這一題是用舊權重算的</item>
    /// </list>
    ///
    /// ⚠️ <b>刻意不動 <see cref="DifficultyEvaluator.WeightedSum"/></b>。尺已經凍結，連加總的先後改了都可能讓某一題差 1 分。
    /// 所以總和與分數直接叫原本那兩支，拆解另外算，再由測試釘住「拆解加起來 ＝ 原本的加權和，完全相等」。
    ///
    /// 💡 <b>成本</b>：「被外框逼出來的片數」每題約 0.2 ms。一關算一次就好，不要每次刷新都算。
    /// </summary>
    public sealed class DifficultyBreakdown
    {
        /// <summary>每個特徵一列，<b>順序跟權重檔一樣</b>。權重 0 的也列出來（分數 0）——「有量但不算分」本身就是資訊。</summary>
        public readonly IReadOnlyList<DifficultyTermContribution> Terms;

        /// <summary>難度尺的中心（1200）。</summary>
        public readonly double Center;

        /// <summary>
        /// 校正量 = −間距 × 加權和的平均 ÷ 加權和的標準差。
        /// 每個特徵都已經置中，所以理論上是 0；權重檔裡那個平均有一點點誤差時才不是。
        /// 中心 ＋ 校正量 ＋ 每一列的分數 ＝ 還沒四捨五入的難度。
        /// </summary>
        public readonly double CenterOffsetPoints;

        /// <summary>加權和，<b>直接叫 <see cref="DifficultyEvaluator.WeightedSum"/></b>。</summary>
        public readonly double WeightedSum;

        /// <summary>重算出來的難度，<b>直接叫 <see cref="DifficultyEvaluator.Score(in DifficultyFeatures, int, in SolutionFeatures, DifficultyWeights)"/></b>。</summary>
        public readonly int Score;

        /// <summary>這一題用的權重的指紋，見 <see cref="DifficultyWeights.Fingerprint"/>。</summary>
        public readonly string WeightsFingerprint;

        private DifficultyBreakdown(IReadOnlyList<DifficultyTermContribution> terms, double center,
                                    double centerOffsetPoints, double weightedSum, int score, string weightsFingerprint)
        {
            Terms = terms;
            Center = center;
            CenterOffsetPoints = centerOffsetPoints;
            WeightedSum = weightedSum;
            Score = score;
            WeightsFingerprint = weightsFingerprint;
        }

        /// <summary>
        /// 從題目與參考解量特徵，再拆解。
        /// </summary>
        /// <param name="puzzle">題目。</param>
        /// <param name="referenceSolution">參考解。解的特徵對平移與整體變換不變，所以盤面座標或題目座標都可以。</param>
        /// <param name="weights">權重。</param>
        /// <returns>拆解結果。</returns>
        /// <exception cref="ArgumentNullException">題目、參考解或權重是 null。</exception>
        public static DifficultyBreakdown Measure(Puzzle puzzle, IReadOnlyList<Placement> referenceSolution,
                                                 DifficultyWeights weights)
        {
            if (puzzle == null) throw new ArgumentNullException(nameof(puzzle));
            if (referenceSolution == null) throw new ArgumentNullException(nameof(referenceSolution));

            return Of(DifficultyFeatures.Measure(puzzle.Walks),
                      DifficultyFeatures.ForcedPieceCountOf(puzzle.Walks),
                      SolutionFeatures.Measure(referenceSolution),
                      weights);
        }

        /// <summary>特徵都量好了的版本。</summary>
        /// <param name="shapeFeatures">只看形狀的特徵。</param>
        /// <param name="forcedPieceCount">被外框逼出來的片數。</param>
        /// <param name="solutionFeatures">看參考解的特徵。</param>
        /// <param name="weights">權重。</param>
        /// <returns>拆解結果。</returns>
        /// <exception cref="ArgumentNullException">權重是 null。</exception>
        /// <exception cref="InvalidOperationException">權重沒有尺度（跟 <see cref="DifficultyEvaluator.Score(in DifficultyFeatures, int, in SolutionFeatures, DifficultyWeights)"/> 同一條）。</exception>
        public static DifficultyBreakdown Of(in DifficultyFeatures shapeFeatures, int forcedPieceCount,
                                             in SolutionFeatures solutionFeatures, DifficultyWeights weights)
        {
            if (weights == null) throw new ArgumentNullException(nameof(weights));

            // 先叫原本那兩支：權重壞掉的話在這裡就丟例外，訊息跟遊戲裡看到的一樣
            int score = DifficultyEvaluator.Score(shapeFeatures, forcedPieceCount, solutionFeatures, weights);
            double weightedSum = DifficultyEvaluator.WeightedSum(shapeFeatures, forcedPieceCount, solutionFeatures, weights);

            double pointsPerWeightedUnit = weights.spread / weights.weightedDeviation;
            var terms = new DifficultyTermContribution[weights.terms.Length];

            for (int i = 0; i < weights.terms.Length; i++)
            {
                DifficultyTermWeight term = weights.terms[i];
                double raw = DifficultyEvaluator.RawValueOf(term.key, shapeFeatures, forcedPieceCount, solutionFeatures);
                double standardScore = (raw - term.mean) / term.deviation;

                // ⚠️ 份量的算式要跟 WeightedSum 裡那一行一字不差（先乘權重與方向、再乘差、再除標準差），
                // 換成「權重 × 方向 × standardScore」的話浮點數的中間值不同，加起來可能差最後一位
                double weightedTerm = term.weight == 0.0
                    ? 0.0
                    : term.weight * term.direction * (raw - term.mean) / term.deviation;

                terms[i] = new DifficultyTermContribution(term.key, raw, term.mean, term.deviation, standardScore,
                                                          term.weight, term.direction, weightedTerm,
                                                          weightedTerm * pointsPerWeightedUnit);
            }

            return new DifficultyBreakdown(Array.AsReadOnly(terms), weights.center,
                                           -weights.weightedMean * pointsPerWeightedUnit,
                                           weightedSum, score, weights.Fingerprint());
        }

        /// <summary>
        /// 對帳：重算出來的難度跟存著的一不一樣。<b>比整數，完全相等才算過。</b>
        /// </summary>
        /// <param name="storedDifficulty">存著的難度（<c>LevelData.Difficulty</c>）。</param>
        /// <returns>一樣就是 true。</returns>
        public bool Matches(int storedDifficulty) => Score == storedDifficulty;
    }
}
