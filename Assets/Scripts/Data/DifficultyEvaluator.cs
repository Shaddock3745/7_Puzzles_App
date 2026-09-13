using System;
using System.Collections.Generic;

namespace Tangram.Data
{
    /// <summary>
    /// 把原始特徵換成<b>以 1200 為中心的難度值</b>（P2-6，issue #27）。
    ///
    /// 分工：<see cref="DifficultyFeatures"/> 與 <see cref="SolutionFeatures"/> 只量數字、不給分；
    /// 權重與尺度是 <see cref="DifficultyWeights"/> 那份資料；這一支只負責套公式。
    /// 三者分開是因為<b>權重要看過一批題目的分布才定得出來</b>，而特徵函數現在就能算、能測。
    ///
    /// <b>為什麼難度值必須是「同一題永遠同一個數字」</b>：ELO 拿它當對手的實力，
    /// 而 <see cref="Canonical"/> 把互為旋轉／鏡射的題目當成同一題。所以
    /// ① 平均與標準差是存下來的常數（不是拿當下那一池現算）
    /// ② 特徵本身對 16 種整體變換不變
    /// ③ 最後<b>四捨五入成整數</b>——中間是 double，不同平台最後一兩位可能有差，
    ///    取整數就把那點差距吃掉了。
    ///
    /// ⚠️ <b>這裡的 double 只是分數，不是幾何</b>。鐵則一管的是座標——座標仍然全程精確，
    /// 只有最後換算成分數這一步才離開 <see cref="Scalar"/>。
    ///
    /// ⚠️ <b>公式是線性的，所以離基準批次越遠越不可信</b>（2026-09-12 量到，記在 `待辦清單.md`
    /// 〈13 個凸多邊形〉）。古典大正方形的周長與轉角數分別離平均 −5.2 與 −6.3 個標準差，
    /// 算出來是 <b>2261</b>，比生成池 2000 題的最高分還高 300 分。那個數字只說明「比我們見過的都極端」，
    /// <b>不代表難 5 倍</b>。<b>手工題特別容易踩到</b>——生成器產不出凸多邊形（5000 個裡 0 個），
    /// 但人用出題工具拼得出來。精選題進來之後要先看它們落在哪，不要照單全收。
    /// </summary>
    public static class DifficultyEvaluator
    {
        /// <summary>
        /// 一題的難度值。
        ///
        /// ⚠️ 這一支會自己量特徵，其中<b>被外框逼出來的片數是最貴的一段</b>
        /// （見 <see cref="DifficultyFeatures.ForcedPieceCountOf"/>）。
        /// 出題器已經量過形狀特徵的話，改用另一個多載，不要重算。
        /// </summary>
        /// <param name="puzzle">題目。</param>
        /// <param name="referenceSolution">參考解。</param>
        /// <param name="weights">權重，<b>必填</b>——從權重檔讀，見 <see cref="DifficultyWeights"/>。</param>
        /// <returns>難度值，1200 是基準批次的平均。</returns>
        /// <exception cref="ArgumentNullException">題目、參考解或權重是 null。</exception>
        public static int Score(Puzzle puzzle, IReadOnlyList<Placement> referenceSolution,
                                DifficultyWeights weights)
        {
            // ⚠️ 2026-09-13 以前這裡對 null 題目回 0，是給出題工具那個「沒有題目的空方框」用的——
            // 當時 LevelData 的建構子會自己算難度。現在難度由出題的人算好傳進 LevelSession.Load，
            // 空方框走 LoadBoxOnly、根本不會叫這裡，所以 null 一律是呼叫端弄錯，要響
            if (puzzle == null) throw new ArgumentNullException(nameof(puzzle));
            if (referenceSolution == null) throw new ArgumentNullException(nameof(referenceSolution));

            return Score(DifficultyFeatures.Measure(puzzle.Walks),
                         DifficultyFeatures.ForcedPieceCountOf(puzzle.Walks),
                         SolutionFeatures.Measure(referenceSolution),
                         weights);
        }

        /// <summary>特徵都量好了的版本。出題器與批次統計用這一支，省下重算。</summary>
        /// <param name="shapeFeatures">只看形狀的特徵。</param>
        /// <param name="forcedPieceCount">被外框逼出來的片數。</param>
        /// <param name="solutionFeatures">看參考解的特徵。</param>
        /// <param name="weights">權重，<b>必填</b>。</param>
        /// <returns>難度值。</returns>
        /// <exception cref="ArgumentNullException">權重是 null。</exception>
        public static int Score(in DifficultyFeatures shapeFeatures, int forcedPieceCount,
                                in SolutionFeatures solutionFeatures, DifficultyWeights weights)
        {
            if (weights == null) throw new ArgumentNullException(nameof(weights));
            double weightedSum = WeightedSum(shapeFeatures, forcedPieceCount, solutionFeatures, weights);

            // 標準差 0 代表這份權重是壞的（整批題目的加權和完全一樣）。默默回中心值會讓
            // 「所有題目難度都一樣」看起來像正常運作，所以寧可丟例外。
            // 讀檔時 DifficultyWeights.FromJson 已經擋過一次；這裡再擋是給「在記憶體裡改過的權重」
            if (weights.weightedDeviation <= 0.0)
                throw new InvalidOperationException(
                    $"DifficultyWeights.weightedDeviation 是 {weights.weightedDeviation}，" +
                    "必須大於 0——這份權重沒有尺度，算出來的難度沒有意義");

            double score = weights.center
                + weights.spread * (weightedSum - weights.weightedMean) / weights.weightedDeviation;
            return (int)Math.Round(score, MidpointRounding.AwayFromZero);
        }

        /// <summary>
        /// 加權和（還沒換算成分數）。<b>每個特徵先換成「離平均幾個標準差」再乘權重與方向。</b>
        /// 檢視單一特徵貢獻多少時用得到，批次統計也用它算尺度。
        /// </summary>
        /// <param name="shapeFeatures">只看形狀的特徵。</param>
        /// <param name="forcedPieceCount">被外框逼出來的片數。</param>
        /// <param name="solutionFeatures">看參考解的特徵。</param>
        /// <param name="weights">權重，<b>必填</b>。</param>
        /// <returns>加權和。</returns>
        /// <exception cref="ArgumentNullException">權重是 null。</exception>
        /// <exception cref="InvalidOperationException">某個特徵的標準差 ≤ 0，或鍵不認得。</exception>
        public static double WeightedSum(in DifficultyFeatures shapeFeatures, int forcedPieceCount,
                                         in SolutionFeatures solutionFeatures, DifficultyWeights weights)
        {
            if (weights == null) throw new ArgumentNullException(nameof(weights));
            double total = 0.0;

            foreach (DifficultyTermWeight term in weights.terms)
            {
                if (term.weight == 0.0) continue;   // 0 分的特徵連標準差都不必檢查
                if (term.deviation <= 0.0)
                    throw new InvalidOperationException(
                        $"特徵 {term.key} 的標準差是 {term.deviation}，必須大於 0");

                double raw = RawValueOf(term.key, shapeFeatures, forcedPieceCount, solutionFeatures);
                total += term.weight * term.direction * (raw - term.mean) / term.deviation;
            }
            return total;
        }

        /// <summary>一個鍵對應的原始數字。</summary>
        /// <param name="key">特徵的鍵，見 <see cref="DifficultyTermKeys"/>。</param>
        /// <param name="shapeFeatures">只看形狀的特徵。</param>
        /// <param name="forcedPieceCount">被外框逼出來的片數。</param>
        /// <param name="solutionFeatures">看參考解的特徵。</param>
        /// <returns>原始數字。</returns>
        /// <exception cref="InvalidOperationException">鍵不認得——打錯字要響，不能默默當成 0。</exception>
        public static double RawValueOf(string key, in DifficultyFeatures shapeFeatures,
                                        int forcedPieceCount, in SolutionFeatures solutionFeatures)
        {
            switch (key)
            {
                // 周長 = 七片周長總和 − 藏起來的長度 ×2（DifficultyFeatures 那條恆等式的反解）
                case DifficultyTermKeys.Perimeter:
                    return (DifficultyFeatures.TotalPiecePerimeter
                            - shapeFeatures.DoubledHiddenEdgeLength).ToDouble();
                case DifficultyTermKeys.CornerCount:
                    return shapeFeatures.CornerCount;
                case DifficultyTermKeys.ForcedPieceCount:
                    return forcedPieceCount;
                case DifficultyTermKeys.KeyPiecesExposingOneVertex:
                    return solutionFeatures.KeyPiecesExposingOneVertex;
                case DifficultyTermKeys.StaggeredEdgeContactCount:
                    return solutionFeatures.StaggeredEdgeContactCount;
                case DifficultyTermKeys.LargeTrianglesJoined:
                    return solutionFeatures.LargeTrianglesJoined;
                default:
                    throw new InvalidOperationException(
                        $"不認得的難度特徵鍵「{key}」——合法的值在 DifficultyTermKeys 裡");
            }
        }
    }
}
