using System;
using System.Collections.Generic;

namespace Tangram.Data
{
    /// <summary>
    /// 一個題目的**原始**難度特徵（P2-2）。
    ///
    /// <b>這裡沒有分數，只有量出來的數字。</b>加權、加總、換算成 1200 為中心的尺度
    /// 全部是 <c>DifficultyEvaluator</c>（P2-6）的事。
    ///
    /// 兩件事分開是因為<b>權重要看過一批題目的分布才定得出來</b>：
    /// 特徵函數現在就能算、能測；權重要等出題器跑出上千題之後才知道該落在哪。
    ///
    /// ⚠️ <b>這些數字必須只由形狀決定</b>，因為 ELO 要求同一題永遠得到同一個難度值，
    /// 而 <see cref="Canonical"/> 把互為旋轉／鏡射的題目當成<b>同一題</b>——
    /// 所以特徵對 16 種整體變換全部不變是硬需求，不是巧合。
    /// 釘住它的是 <c>DifficultyFeatureTests.特徵_對十六種整體變換全部不變</c>。
    /// </summary>
    public readonly struct DifficultyFeatures
    {
        /// <summary>
        /// 一副七巧板七片的周長總和，恆為 <b>40 + 20√2</b>（單位：小三角的股 = 2）。
        ///
        /// 剛體變換不改變邊長，而七片永遠是同樣那七片，所以這是常數。
        /// <see cref="DoubledHiddenEdgeLength"/> 整條推論的地基就是它。
        ///
        /// ⚠️ 改了 <see cref="PiecePrototype"/> 的原型頂點（鐵則二那個坑）這個值就會變，
        /// 而藏起來的長度會<b>安靜地全部算錯</b>。所以有一條測試直接釘死它。
        /// </summary>
        public static readonly Scalar TotalPiecePerimeter = new Scalar(40, 20);

        /// <summary>七片的面積和 ×2。面積 32，所以是 64。</summary>
        public static readonly Scalar ExpectedDoubleArea = Scalar.FromInteger(64);

        /// <summary>內角大於 180° 的頂點有幾個。凹角多 → 難。</summary>
        public readonly int ConcaveAngleCount;

        /// <summary>輪廓上出現過幾種不同的邊方向，0~8。角度雜 → 難。</summary>
        public readonly int DirectionVarietyCount;

        /// <summary>
        /// 片與片貼合、玩家看不到的那些邊，**長度的兩倍**。藏得多 → 線索少 → 難。
        ///
        /// 存兩倍是為了留在 ℤ[√2] 裡不碰除法（跟 <c>SignedDoubleArea</c>、
        /// <c>Region</c> 內部的 ×2 同一招）。
        /// </summary>
        public readonly Scalar DoubledHiddenEdgeLength;

        /// <summary>兩片只碰到一個點的地方有幾處（相異的點，不是出現次數）。</summary>
        public readonly int PinchPointCount;

        /// <summary>形狀裡有幾個洞。P2-3 的審美篩選要用（建議篩成 0）。</summary>
        public readonly int HoleCount;

        /// <param name="concaveAngleCount">凹角數。</param>
        /// <param name="directionVarietyCount">相異邊方向的數量。</param>
        /// <param name="doubledHiddenEdgeLength">藏起來的邊長 ×2。</param>
        /// <param name="pinchPointCount">捏點數。</param>
        /// <param name="holeCount">洞數。</param>
        public DifficultyFeatures(int concaveAngleCount, int directionVarietyCount,
                                  Scalar doubledHiddenEdgeLength,
                                  int pinchPointCount, int holeCount)
        {
            ConcaveAngleCount = concaveAngleCount;
            DirectionVarietyCount = directionVarietyCount;
            DoubledHiddenEdgeLength = doubledHiddenEdgeLength;
            PinchPointCount = pinchPointCount;
            HoleCount = holeCount;
        }

        /// <summary>量一個題目的特徵。</summary>
        /// <param name="puzzle">題目。</param>
        /// <returns>這個題目的原始特徵。</returns>
        /// <exception cref="ArgumentNullException">題目或它的 <c>Walks</c> 是 null。</exception>
        /// <exception cref="InvalidOperationException">面積不是 32，或輪廓有零寬度的刺。</exception>
        public static DifficultyFeatures Measure(Puzzle puzzle)
        {
            if (puzzle == null) throw new ArgumentNullException(nameof(puzzle));
            return Measure(puzzle.Walks);
        }

        /// <summary>
        /// 量一組輪廓環的特徵。
        ///
        /// <b>外環與洞環用同一套規則</b>，不需要依繞向翻號——因為 <see cref="BoundaryTracer"/>
        /// 把每條邊界邊都定向成<b>內部在左</b>。洞環的有向面積是負的（<c>IsOuter</c> 回 false），
        /// 但走的時候材料一樣在左手邊，所以「右轉 = 凹」對兩者都成立。
        /// ⚠️ 這個性質經過 <see cref="Canonical.TransformWalk"/> 也還在——
        /// 它的鏡射分支會把頂點順序反轉回來（跟 <c>Placement.Vertices()</c> 同一招）。
        /// </summary>
        /// <param name="walks">輪廓環，外環與洞環混在一起沒關係。</param>
        /// <returns>原始特徵。</returns>
        /// <exception cref="ArgumentNullException"><paramref name="walks"/> 是 null。</exception>
        /// <exception cref="InvalidOperationException">
        /// 面積不是 32（代表這不是七片拼出來的），或某個頂點是 180° 折返。
        /// </exception>
        public static DifficultyFeatures Measure(IReadOnlyList<Walk> walks)
        {
            if (walks == null) throw new ArgumentNullException(nameof(walks));

            int concaveAngleCount = 0;
            int holeCount = 0;
            var seenDirections = new HashSet<int>();
            var vertexVisitCounts = new Dictionary<ExactVector2, int>();
            Scalar totalPerimeter = Scalar.Zero;
            Scalar totalDoubleArea = Scalar.Zero;

            for (int walkIndex = 0; walkIndex < walks.Count; walkIndex++)
            {
                Walk walk = walks[walkIndex];
                Edge[] edges = walk.Edges;

                // 頂點只算一次：面積、洞的判定、捏點的計數全部共用這一份。
                // ⚠️ 不用 Walk.IsOuter——它每次讀都重走一遍頂點
                ExactVector2[] vertices = walk.Vertices();
                Scalar doubleArea = Geometry.SignedDoubleArea(vertices);
                totalDoubleArea = totalDoubleArea + doubleArea;
                if (doubleArea.Sign < 0) holeCount++;

                for (int edgeIndex = 0; edgeIndex < edges.Length; edgeIndex++)
                {
                    // 洞環的邊也要加進周長——洞的邊界一樣是外露的，玩家看得到
                    totalPerimeter = totalPerimeter + edges[edgeIndex].Length;
                    seenDirections.Add(edges[edgeIndex].Direction);

                    // 第 edgeIndex 個頂點是第 edgeIndex 條邊的起點，
                    // 所以「進來的邊」是前一條，第 0 個頂點要繞回最後一條
                    int incomingIndex = (edgeIndex - 1 + edges.Length) % edges.Length;
                    int turn = Symmetry.Modulo8(
                        edges[edgeIndex].Direction - edges[incomingIndex].Direction);

                    // 0 = 直走（捏點那裡會出現，因為共線邊只在邊界度數 = 2 時才合併）
                    // 1~3 = 左轉，內角小於 180°，凸角
                    // 5~7 = 右轉，內角大於 180°，凹角
                    if (turn >= 5) concaveAngleCount++;
                    else if (turn == 4)
                        throw new InvalidOperationException(
                            $"輪廓在 {vertices[edgeIndex]} 原地折返——那是一根零寬度的刺。" +
                            "合法輸入不可能出現：每個切好的線段只會被一片擁有，" +
                            "所以「沿原路走回去」的邊界邊根本不存在");
                }

                // ⚠️ 計數表跨環共用：捏點有可能是外環碰到洞環，只看單一環會漏掉
                foreach (ExactVector2 vertex in vertices)
                {
                    vertexVisitCounts.TryGetValue(vertex, out int visits);
                    vertexVisitCounts[vertex] = visits + 1;
                }
            }

            // ⚠️ 守衛排在算完之後、回報之前：藏起來的長度那條公式的地基就是
            // 「圖形正好是那七片」。不驗的話，餵一個手繪題目或半成品進來會算出垃圾
            // 甚至負數，而且不報錯。丟例外不丟結果，是因為這代表呼叫端有 bug
            if (totalDoubleArea != ExpectedDoubleArea)
                throw new InvalidOperationException(
                    $"面積 ×2 是 {totalDoubleArea}，應該是 {ExpectedDoubleArea}——" +
                    "這組輪廓不是七巧板七片拼出來的，特徵算出來沒有意義");

            int pinchPointCount = 0;
            foreach (KeyValuePair<ExactVector2, int> visit in vertexVisitCounts)
                if (visit.Value >= 2) pinchPointCount++;

            return new DifficultyFeatures(
                concaveAngleCount,
                seenDirections.Count,
                TotalPiecePerimeter - totalPerimeter,
                pinchPointCount,
                holeCount);
        }

        /// <returns>給人看的字串，除錯與批次統計用。</returns>
        public override string ToString()
            => $"凹角={ConcaveAngleCount} 角度種類={DirectionVarietyCount} " +
               $"藏起來的長度×2={DoubledHiddenEdgeLength} 捏點={PinchPointCount} 洞={HoleCount}";
    }
}
