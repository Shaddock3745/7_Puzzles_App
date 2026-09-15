using System;
using System.Collections.Generic;

namespace Tangram.Data
{
    /// <summary>全整數（ℤ[√2]）的多邊形工具。沒有容差，也不需要容差。</summary>
    public static class Geometry
    {
        /// <summary>
        /// 兩組頂點平移之後能不能完全重合——「形狀方向一樣、位置不算」。
        ///
        /// ⚠️ <b>比形狀一定要比頂點集合，不能比姿態欄位</b>：正方形轉 0 與轉 2 佔位一樣，
        /// 三角形「翻面 ＋ 轉某格」跟「不翻面轉另一格」也一樣（CLAUDE.md〈Placement.Equals 是姿態相等，不是形狀相等〉）。
        /// 2026-09-14 從 <c>FeedbackPractice</c> 搬來，因為翻轉鈕也要用。
        /// </summary>
        /// <param name="first">第一組頂點（順序不拘）。</param>
        /// <param name="second">第二組頂點（順序不拘）。</param>
        /// <returns>能重合的話 true。</returns>
        public static bool SameVerticesIgnoringPosition(IReadOnlyList<ExactVector2> first, IReadOnlyList<ExactVector2> second)
        {
            if (first.Count != second.Count || first.Count == 0) return false;

            var secondVertices = new HashSet<ExactVector2>(second);

            // 如果能重合，第一組的第 0 個頂點一定落在第二組的某個頂點上——逐一試那幾個平移量
            foreach (ExactVector2 candidate in secondVertices)
            {
                ExactVector2 shift = candidate - first[0];
                bool allMatch = true;
                foreach (ExactVector2 vertex in first)
                    if (!secondVertices.Contains(vertex + shift)) { allMatch = false; break; }
                if (allMatch) return true;
            }
            return false;
        }

        /// <summary>
        /// 兩組頂點是不是<b>同一個集合</b>——形狀、方向、位置全都一樣（2026-09-15，提示要問「這片擺對了沒」）。
        ///
        /// 跟 <see cref="SameVerticesIgnoringPosition"/> 的差別只有位置算不算。
        /// ⚠️ 同一個理由不能比姿態欄位：正方形轉 0 與轉 2 的 <c>Placement</c> 不相等，佔的位置卻一模一樣。
        /// </summary>
        /// <param name="first">第一組頂點（順序不拘）。</param>
        /// <param name="second">第二組頂點（順序不拘）。</param>
        /// <returns>頂點數相同而且每一個都對得上的話 true。空的一律 false。</returns>
        public static bool SameVertexSet(IReadOnlyList<ExactVector2> first, IReadOnlyList<ExactVector2> second)
        {
            if (first.Count != second.Count || first.Count == 0) return false;

            // 七巧板的片沒有重複頂點，所以「數量一樣 ＋ 第一組每個都在第二組裡」就是集合相等
            var secondVertices = new HashSet<ExactVector2>(second);
            foreach (ExactVector2 vertex in first)
                if (!secondVertices.Contains(vertex)) return false;
            return true;
        }

        /// <summary>shoelace，回傳面積 ×2。正 = CCW（外環），負 = CW（洞）。</summary>
        /// <param name="polygon">頂點環，起點不用重複放在結尾。</param>
        /// <returns>
        /// 有向面積的兩倍。回傳 ×2 是為了避免除法——ℤ[√2] 沒有除法，
        /// 而所有面積比較（例如「總和是不是 32」）用兩倍值一樣成立。
        /// </returns>
        public static Scalar SignedDoubleArea(IReadOnlyList<ExactVector2> polygon)
        {
            Scalar accumulated = Scalar.Zero;
            int vertexCount = polygon.Count;
            for (int vertexIndex = 0; vertexIndex < vertexCount; vertexIndex++)
            {
                ExactVector2 current = polygon[vertexIndex];
                ExactVector2 next = polygon[(vertexIndex + 1) % vertexCount];
                accumulated = accumulated + ExactVector2.Cross(current, next);
            }
            return accumulated;
        }

        /// <summary>
        /// 凸多邊形的嚴格重疊判定（SAT，精確算術）。
        /// 距離 0 算「分離」，只有內部交集 &gt; 0 才算重疊 —— 否則邊接觸與點接觸全誤判（§6 陷阱）。
        /// </summary>
        /// <param name="first">第一個凸多邊形的頂點環。</param>
        /// <param name="second">第二個凸多邊形的頂點環。</param>
        /// <returns>兩者的**內部**是否有交集。邊貼合與點接觸都回 false。</returns>
        public static bool ConvexOverlap(IReadOnlyList<ExactVector2> first,
                                         IReadOnlyList<ExactVector2> second)
        {
            return !HasSeparatingAxis(first, second) && !HasSeparatingAxis(second, first);
        }

        /// <summary>拿 axisOwner 的每條邊的法線當分離軸試一次。</summary>
        /// <param name="axisOwner">提供分離軸的多邊形。</param>
        /// <param name="other">另一個多邊形。</param>
        /// <returns>是否找得到一條分離軸。找得到就代表兩者不重疊。</returns>
        private static bool HasSeparatingAxis(IReadOnlyList<ExactVector2> axisOwner,
                                              IReadOnlyList<ExactVector2> other)
        {
            int vertexCount = axisOwner.Count;
            for (int vertexIndex = 0; vertexIndex < vertexCount; vertexIndex++)
            {
                ExactVector2 edgeStart = axisOwner[vertexIndex];
                ExactVector2 edgeEnd = axisOwner[(vertexIndex + 1) % vertexCount];
                ExactVector2 edge = edgeEnd - edgeStart;
                ExactVector2 axis = new ExactVector2(-edge.Y, edge.X);   // 不需正規化

                Project(axisOwner, axis, out Scalar ownerMinimum, out Scalar ownerMaximum);
                Project(other, axis, out Scalar otherMinimum, out Scalar otherMaximum);

                // 觸碰即視為分離
                if (ownerMaximum <= otherMinimum || otherMaximum <= ownerMinimum) return true;
            }
            return false;
        }

        /// <summary>把多邊形投影到一條軸上，取投影區間。軸不需要正規化。</summary>
        /// <param name="polygon">要投影的多邊形。</param>
        /// <param name="axis">投影軸（方向向量，長度無所謂）。</param>
        /// <param name="minimum">投影區間的下界。</param>
        /// <param name="maximum">投影區間的上界。</param>
        private static void Project(IReadOnlyList<ExactVector2> polygon, ExactVector2 axis,
                                    out Scalar minimum, out Scalar maximum)
        {
            minimum = maximum = ExactVector2.Dot(polygon[0], axis);
            for (int vertexIndex = 1; vertexIndex < polygon.Count; vertexIndex++)
            {
                Scalar projection = ExactVector2.Dot(polygon[vertexIndex], axis);
                if (projection < minimum) minimum = projection;
                if (projection > maximum) maximum = projection;
            }
        }

        /// <summary>指定點是否嚴格落在線段內部（共線且不含端點）。</summary>
        /// <param name="segmentStart">線段起點。</param>
        /// <param name="segmentEnd">線段終點。</param>
        /// <param name="point">要測試的點。</param>
        /// <returns>
        /// 點是否落在線段內部。端點回 false。
        /// 前提是線段落在 45° 網格上（走 <see cref="Symmetry.TryDecompose"/>）；
        /// 不確定的話用 <see cref="IsOnSegment"/>。
        /// </returns>
        public static bool StrictlyOnSegment(ExactVector2 segmentStart, ExactVector2 segmentEnd,
                                             ExactVector2 point)
        {
            if (point == segmentStart || point == segmentEnd) return false;

            if (!Symmetry.TryDecompose(segmentEnd - segmentStart,
                                       out int segmentDirection, out Scalar segmentLength)) return false;
            if (!Symmetry.TryDecompose(point - segmentStart,
                                       out int pointDirection, out Scalar pointOffset)) return false;

            return segmentDirection == pointDirection && pointOffset < segmentLength;
        }

        /// <summary>union-find：共用至少一個節點即相連（點接觸算數，§5）。</summary>
        /// <param name="pieces">要檢查的片。</param>
        /// <returns>
        /// 是否全部連成一塊。0 或 1 片一律回 true。
        /// 盒子模式的過關判定不需要它（填滿連通的盒子必然連通），出題器仍會用到。
        /// </returns>
        public static bool AllConnected(IReadOnlyList<Placement> pieces)
        {
            int pieceCount = pieces.Count;
            if (pieceCount <= 1) return true;

            var ownerByNodePosition = new Dictionary<ExactVector2, int>();
            var parents = new int[pieceCount];
            for (int pieceIndex = 0; pieceIndex < pieceCount; pieceIndex++) parents[pieceIndex] = pieceIndex;

            // 找根，順便做路徑壓縮
            int FindRoot(int pieceIndex)
            {
                while (parents[pieceIndex] != pieceIndex)
                {
                    parents[pieceIndex] = parents[parents[pieceIndex]];
                    pieceIndex = parents[pieceIndex];
                }
                return pieceIndex;
            }

            void UnionPieces(int firstPiece, int secondPiece)
            {
                int firstRoot = FindRoot(firstPiece);
                int secondRoot = FindRoot(secondPiece);
                if (firstRoot != secondRoot) parents[firstRoot] = secondRoot;
            }

            for (int pieceIndex = 0; pieceIndex < pieceCount; pieceIndex++)
            {
                foreach (ExactVector2 nodePosition in pieces[pieceIndex].Nodes())
                {
                    if (ownerByNodePosition.TryGetValue(nodePosition, out int otherPiece))
                        UnionPieces(pieceIndex, otherPiece);
                    else
                        ownerByNodePosition[nodePosition] = pieceIndex;
                }
            }

            int firstPieceRoot = FindRoot(0);
            for (int pieceIndex = 1; pieceIndex < pieceCount; pieceIndex++)
                if (FindRoot(pieceIndex) != firstPieceRoot) return false;
            return true;
        }

        /// <summary>任兩片是否都不重疊（內部交集面積 = 0）。</summary>
        /// <param name="pieces">要檢查的片。</param>
        /// <param name="firstOverlappingPiece">找到重疊時是第一片的索引，否則 −1。</param>
        /// <param name="secondOverlappingPiece">找到重疊時是第二片的索引，否則 −1。</param>
        /// <returns>全部都不重疊回 true。找到第一組重疊就回 false 並回報是哪兩片。</returns>
        public static bool NoOverlap(IReadOnlyList<Placement> pieces,
                                     out int firstOverlappingPiece, out int secondOverlappingPiece)
        {
            var polygons = new ExactVector2[pieces.Count][];
            for (int pieceIndex = 0; pieceIndex < pieces.Count; pieceIndex++)
                polygons[pieceIndex] = pieces[pieceIndex].Vertices();

            for (int firstIndex = 0; firstIndex < polygons.Length; firstIndex++)
            {
                for (int secondIndex = firstIndex + 1; secondIndex < polygons.Length; secondIndex++)
                {
                    if (ConvexOverlap(polygons[firstIndex], polygons[secondIndex]))
                    {
                        firstOverlappingPiece = firstIndex;
                        secondOverlappingPiece = secondIndex;
                        return false;
                    }
                }
            }

            firstOverlappingPiece = -1;
            secondOverlappingPiece = -1;
            return true;
        }

        /// <summary>
        /// 以 origin 為原點的二維外積：(first − origin) × (second − origin)。
        /// 正 = second 在 origin→first 的左側，零 = 三點共線。
        /// </summary>
        /// <param name="origin">當原點的那個點。</param>
        /// <param name="first">第一個方向的端點。</param>
        /// <param name="second">第二個方向的端點。</param>
        /// <returns>外積值。通常只看 <c>.Sign</c>。</returns>
        public static Scalar Cross(ExactVector2 origin, ExactVector2 first, ExactVector2 second)
            => ExactVector2.Cross(first - origin, second - origin);

        /// <summary>
        /// 點是否落在線段上（含兩端點）。
        /// 與 StrictlyOnSegment 的差別：這裡含端點，而且不預設線段落在 45° 網格上
        /// （走叉積 + 內積，不經過 Symmetry.TryDecompose）。
        /// </summary>
        /// <param name="segmentStart">線段起點。</param>
        /// <param name="segmentEnd">線段終點。</param>
        /// <param name="point">要測試的點。</param>
        /// <returns>點是否在線段上（含端點）。</returns>
        public static bool IsOnSegment(ExactVector2 segmentStart, ExactVector2 segmentEnd,
                                       ExactVector2 point)
        {
            if (!Cross(segmentStart, segmentEnd, point).IsZero) return false;

            ExactVector2 segment = segmentEnd - segmentStart;
            Scalar projection = ExactVector2.Dot(point - segmentStart, segment);
            if (projection.Sign < 0) return false;
            return projection <= ExactVector2.Dot(segment, segment);
        }

        /// <summary>
        /// 兩線段是否「真」交叉：交點同時落在兩條線段的內部。
        ///
        /// 端點接觸、T 字接觸、共線重疊一律回 false —— 與 ConvexOverlap 的
        /// 「距離 0 算分離」是同一個約定（§6 陷阱）。四個叉積只要有一個是 0，
        /// 就代表某個端點落在對方的直線上，那就不算真交叉。
        /// 不要把下面的比較改成含等號，會讓所有邊貼合的擺放誤判成非法。
        ///
        /// 附帶保證：沒有真交叉時，兩條線段的所有接觸點都是某一方的端點，
        /// 所以是現成的 ℤ[√2] 座標 —— Region 的區間中點掃描靠這件事才不需要分數運算。
        /// </summary>
        /// <param name="firstStart">第一條線段的起點。</param>
        /// <param name="firstEnd">第一條線段的終點。</param>
        /// <param name="secondStart">第二條線段的起點。</param>
        /// <param name="secondEnd">第二條線段的終點。</param>
        /// <returns>是否真交叉。端點接觸、T 字接觸、共線重疊都回 false。</returns>
        public static bool SegmentsProperlyCross(ExactVector2 firstStart, ExactVector2 firstEnd,
                                                 ExactVector2 secondStart, ExactVector2 secondEnd)
        {
            int secondStartSide = Cross(firstStart, firstEnd, secondStart).Sign;
            int secondEndSide = Cross(firstStart, firstEnd, secondEnd).Sign;
            if (secondStartSide * secondEndSide >= 0) return false;

            int firstStartSide = Cross(secondStart, secondEnd, firstStart).Sign;
            int firstEndSide = Cross(secondStart, secondEnd, firstEnd).Sign;
            return firstStartSide * firstEndSide < 0;
        }

        /// <summary>
        /// 點是否嚴格落在凸多邊形內部（落在邊上不算）。繞向不拘，CCW 與 CW 都吃。
        /// polygonScale 讓呼叫端就地把多邊形放大，不必另外配置一份放大後的陣列
        /// —— Region 的洞包覆檢查要在 ×2 座標系下比對，靠它避免每次判定都配置記憶體。
        /// </summary>
        /// <param name="convexPolygon">凸多邊形的頂點環。</param>
        /// <param name="point">要測試的點，座標已經是縮放後的座標系。</param>
        /// <param name="polygonScale">先把多邊形的每個頂點乘上這個倍率再比。預設 1 就是不縮放。</param>
        /// <returns>點是否嚴格在內部。落在邊上或頂點上回 false。</returns>
        public static bool StrictlyInsideConvexPolygon(IReadOnlyList<ExactVector2> convexPolygon,
                                                       ExactVector2 point,
                                                       long polygonScale = 1)
        {
            int expectedSide = 0;
            int vertexCount = convexPolygon.Count;
            for (int vertexIndex = 0; vertexIndex < vertexCount; vertexIndex++)
            {
                ExactVector2 edgeStart = convexPolygon[vertexIndex] * polygonScale;
                ExactVector2 edgeEnd = convexPolygon[(vertexIndex + 1) % vertexCount] * polygonScale;

                int side = Cross(edgeStart, edgeEnd, point).Sign;
                if (side == 0) return false;
                if (expectedSide == 0) expectedSide = side;
                else if (side != expectedSide) return false;
            }
            return expectedSide != 0;
        }

        /// <summary>
        /// 一堆點的凸包面積 ×2（Andrew's monotone chain，全程精確叉積）。
        ///
        /// 難度特徵「凸包填滿率」用它：填滿率 = 形狀面積 ÷ 凸包面積 =
        /// <see cref="DifficultyFeatures.ExpectedDoubleArea"/> ÷ 這個值。
        /// ⚠️ <b>刻意回傳面積而不是比率</b>——比率是除法，存下來就變浮點（鐵則一）。
        /// 要比「填滿率 ≥ 分子/分母」時改成兩邊乘開的整數比較，見
        /// <c>AestheticLimits.Violations</c>。
        ///
        /// 共線的點不會留在凸包上（叉積為 0 就彈掉），所以面積跟有沒有中繼點無關。
        /// </summary>
        /// <param name="points">任意順序的點，可以有重複。</param>
        /// <returns>凸包面積的兩倍，恆為非負。點少於三個（或全部共線）時是 0。</returns>
        /// <exception cref="ArgumentNullException"><paramref name="points"/> 是 null。</exception>
        public static Scalar ConvexHullDoubleArea(IReadOnlyList<ExactVector2> points)
        {
            if (points == null) throw new ArgumentNullException(nameof(points));
            if (points.Count < 3) return Scalar.Zero;

            var sorted = new List<ExactVector2>(points);
            sorted.Sort((left, right) => left.CompareTo(right));

            // 下半部再上半部：兩趟都把「不是左轉」的點彈掉，剩下的就是凸包（CCW）
            var hull = new List<ExactVector2>(sorted.Count + 1);
            for (int pass = 0; pass < 2; pass++)
            {
                int passStart = hull.Count;
                for (int index = 0; index < sorted.Count; index++)
                {
                    ExactVector2 point = pass == 0 ? sorted[index] : sorted[sorted.Count - 1 - index];
                    while (hull.Count >= passStart + 2
                           && Cross(hull[hull.Count - 2], hull[hull.Count - 1], point).Sign <= 0)
                        hull.RemoveAt(hull.Count - 1);
                    hull.Add(point);
                }
                hull.RemoveAt(hull.Count - 1);   // 結尾那個是下一趟的起點，留著會重複
            }

            Scalar doubleArea = SignedDoubleArea(hull);
            return doubleArea.Sign < 0 ? -doubleArea : doubleArea;
        }
    }
}
