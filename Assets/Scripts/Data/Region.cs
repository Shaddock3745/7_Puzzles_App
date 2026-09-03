using System;
using System.Collections.Generic;

namespace Tangram.Data
{
    /// <summary>點相對於區域的位置。邊界自成一類，不併進內或外。</summary>
    public enum RegionPosition
    {
        Outside = 0,
        OnBoundary = 1,
        Inside = 2
    }

    /// <summary>軸對齊包圍盒。只做比較不做算術，所以完全精確。</summary>
    public readonly struct BoundingBox
    {
        public readonly ExactVector2 Minimum;
        public readonly ExactVector2 Maximum;

        public BoundingBox(ExactVector2 minimum, ExactVector2 maximum)
        {
            Minimum = minimum;
            Maximum = maximum;
        }

        public static BoundingBox FromPoints(IReadOnlyList<ExactVector2> points)
        {
            if (points == null || points.Count == 0)
                throw new ArgumentException("BoundingBox.FromPoints：點集合是空的");

            Scalar minimumX = points[0].X;
            Scalar maximumX = points[0].X;
            Scalar minimumY = points[0].Y;
            Scalar maximumY = points[0].Y;

            for (int pointIndex = 1; pointIndex < points.Count; pointIndex++)
            {
                ExactVector2 point = points[pointIndex];
                if (point.X < minimumX) minimumX = point.X;
                if (point.X > maximumX) maximumX = point.X;
                if (point.Y < minimumY) minimumY = point.Y;
                if (point.Y > maximumY) maximumY = point.Y;
            }

            return new BoundingBox(new ExactVector2(minimumX, minimumY),
                                   new ExactVector2(maximumX, maximumY));
        }

        /// <summary>含邊界。</summary>
        public bool Contains(ExactVector2 point)
            => point.X >= Minimum.X && point.X <= Maximum.X
            && point.Y >= Minimum.Y && point.Y <= Maximum.Y;

        /// <summary>other 是否完全落在自己裡面（含邊界）。</summary>
        public bool Contains(BoundingBox other)
            => other.Minimum.X >= Minimum.X && other.Maximum.X <= Maximum.X
            && other.Minimum.Y >= Minimum.Y && other.Maximum.Y <= Maximum.Y;

        public override string ToString() => Minimum + " ~ " + Maximum;
    }

    /// <summary>
    /// 有洞的封閉二維區域，全程精確算術。盒子是一個 Region，片是一個 Region，
    /// P1-11 的剩餘連通塊也會是 Region。
    ///
    /// 建構時就把後面每一幀要用的東西算完並凍結：攤平的環頂點、×2 座標的副本、
    /// 包圍盒、每個洞的一個內部點。之後唯讀。
    ///
    /// 為什麼要 ×2 座標的副本：判斷「片有沒有整段跑到區域外」要取線段中點，
    /// 中點要除以 2，而 Scalar 沒有除法。把整個世界乘 2 之後中點就是兩端點相加，
    /// 而「點在不在區域內」對等比例縮放不變，所以放大不影響答案。
    /// </summary>
    public sealed class Region
    {
        /// <summary>每條環的頂點，CCW = 外環、CW = 洞。起點不重複出現在結尾。</summary>
        public readonly ExactVector2[][] Rings;

        /// <summary>與 Rings 同索引。用有向面積判斷，不存旗標（§2.1）。</summary>
        public readonly bool[] RingIsOuter;

        /// <summary>攤平的頂點清單。捏點會出現兩次，這是刻意保留的。</summary>
        public readonly ExactVector2[] Vertices;

        public readonly BoundingBox Bounds;

        private readonly ExactVector2[][] doubledRings;

        /// <summary>每個洞一個內部點，存 ×2 座標。沒有洞時是空陣列。</summary>
        private readonly ExactVector2[] doubledHoleInteriorPoints;

        private Region(ExactVector2[][] rings)
        {
            Rings = rings;
            RingIsOuter = new bool[rings.Length];
            doubledRings = new ExactVector2[rings.Length][];

            var flattenedVertices = new List<ExactVector2>();
            var holeInteriorPoints = new List<ExactVector2>();

            for (int ringIndex = 0; ringIndex < rings.Length; ringIndex++)
            {
                ExactVector2[] ring = rings[ringIndex];
                if (ring.Length < 3)
                    throw new ArgumentException($"Region：第 {ringIndex} 條環只有 {ring.Length} 個頂點");

                RingIsOuter[ringIndex] = Geometry.SignedDoubleArea(ring).Sign > 0;
                flattenedVertices.AddRange(ring);

                var doubledRing = new ExactVector2[ring.Length];
                for (int vertexIndex = 0; vertexIndex < ring.Length; vertexIndex++)
                    doubledRing[vertexIndex] = ring[vertexIndex] * 2;
                doubledRings[ringIndex] = doubledRing;

                if (!RingIsOuter[ringIndex])
                    holeInteriorPoints.Add(FindDoubledInteriorPoint(ring, doubledRing, ringIndex));
            }

            Vertices = flattenedVertices.ToArray();
            doubledHoleInteriorPoints = holeInteriorPoints.ToArray();
            Bounds = BoundingBox.FromPoints(Vertices);
        }

        // ---- 建構 ----------------------------------------------------------

        public static Region FromRings(IReadOnlyList<ExactVector2[]> rings)
        {
            if (rings == null || rings.Count == 0)
                throw new ArgumentException("Region.FromRings：沒有給任何環");

            var copiedRings = new ExactVector2[rings.Count][];
            for (int ringIndex = 0; ringIndex < rings.Count; ringIndex++)
                copiedRings[ringIndex] = NormalizeRing(rings[ringIndex]);
            return new Region(copiedRings);
        }

        /// <summary>單一封閉環（沒有洞）。片本身就用這個。</summary>
        public static Region FromPolygon(IReadOnlyList<ExactVector2> ring)
            => new Region(new[] { NormalizeRing(ring) });

        /// <summary>從輪廓建區域。translation 就是盒子原點。</summary>
        public static Region FromWalks(IReadOnlyList<Walk> walks, ExactVector2 translation)
        {
            if (walks == null || walks.Count == 0)
                throw new ArgumentException("Region.FromWalks：沒有給任何輪廓");

            var rings = new ExactVector2[walks.Count][];
            for (int walkIndex = 0; walkIndex < walks.Count; walkIndex++)
            {
                ExactVector2[] walkVertices = walks[walkIndex].Vertices();   // 順便驗環有沒有封閉
                var ring = new ExactVector2[walkVertices.Length];
                for (int vertexIndex = 0; vertexIndex < walkVertices.Length; vertexIndex++)
                    ring[vertexIndex] = walkVertices[vertexIndex] + translation;
                rings[walkIndex] = ring;
            }
            return new Region(rings);
        }

        /// <summary>題目 + 盒子原點 → 盤面上那個盒子。</summary>
        public static Region FromPuzzle(Puzzle puzzle, ExactVector2 boxOrigin)
            => FromWalks(puzzle.Walks, boxOrigin);

        private static ExactVector2[] NormalizeRing(IReadOnlyList<ExactVector2> ring)
        {
            if (ring == null || ring.Count < 3)
                throw new ArgumentException("Region：一條環至少要三個頂點");

            // 有些來源會把起點重複放在結尾，這裡統一砍掉
            int vertexCount = ring.Count;
            if (ring[vertexCount - 1] == ring[0]) vertexCount--;

            var normalized = new ExactVector2[vertexCount];
            for (int vertexIndex = 0; vertexIndex < vertexCount; vertexIndex++)
                normalized[vertexIndex] = ring[vertexIndex];
            return normalized;
        }

        // ---- 點的分類 ------------------------------------------------------

        public RegionPosition Classify(ExactVector2 point) => Classify(Rings, point);

        /// <summary>×2 座標系下的分類。給中點測試用（中點在原座標系可能不是 ℤ[√2] 的點）。</summary>
        public RegionPosition ClassifyDoubled(ExactVector2 doubledPoint)
            => Classify(doubledRings, doubledPoint);

        /// <summary>含邊界。</summary>
        public bool Contains(ExactVector2 point) => Classify(point) != RegionPosition.Outside;

        /// <summary>
        /// 射線法（水平 +X）。邊界先判掉，所以射線不會遇到「點就在邊上」的退化情況。
        /// 洞不用特別處理：穿越次數不看繞向，洞內的點自然變成偶數。
        /// </summary>
        private static RegionPosition Classify(ExactVector2[][] rings, ExactVector2 point)
        {
            bool inside = false;

            for (int ringIndex = 0; ringIndex < rings.Length; ringIndex++)
            {
                ExactVector2[] ring = rings[ringIndex];
                for (int vertexIndex = 0; vertexIndex < ring.Length; vertexIndex++)
                {
                    ExactVector2 edgeStart = ring[vertexIndex];
                    ExactVector2 edgeEnd = ring[(vertexIndex + 1) % ring.Length];

                    if (Geometry.IsOnSegment(edgeStart, edgeEnd, point)) return RegionPosition.OnBoundary;
                    if (CrossesRayToTheRight(edgeStart, edgeEnd, point)) inside = !inside;
                }
            }

            return inside ? RegionPosition.Inside : RegionPosition.Outside;
        }

        /// <summary>
        /// 這條邊是否在 point 的右側穿過水平射線。
        ///
        /// y 的比較刻意用半開區間（一端含、一端不含），頂點正好落在射線上時才不會多數一次
        /// —— 這是射線法最經典的 bug。交點的 x 用叉積比，不做除法。
        /// </summary>
        private static bool CrossesRayToTheRight(ExactVector2 edgeStart, ExactVector2 edgeEnd,
                                                 ExactVector2 point)
        {
            bool startIsAbove = edgeStart.Y > point.Y;
            bool endIsAbove = edgeEnd.Y > point.Y;
            if (startIsAbove == endIsAbove) return false;

            // (交點x − point.x) 與 (edgeEnd.y − edgeStart.y) 同號 ⟺ 交點在 point 右邊
            int side = Geometry.Cross(edgeStart, edgeEnd, point).Sign;
            return endIsAbove ? side > 0 : side < 0;
        }

        // ---- 多邊形包含 ----------------------------------------------------

        public bool Contains(Placement piece) => ContainsPolygon(piece.Vertices());

        /// <summary>
        /// 凸多邊形是否完全落在區域內（含邊界）。這就是吸附的盒內硬限制。
        ///
        /// 五條規則由便宜到貴，任何一條失敗就拒絕：
        ///   0. 包圍盒
        ///   1. 每個頂點都在區域內（含邊界）   —— 擋整片跑到外面
        ///   2. 沒有片邊與輪廓邊真交叉         —— 擋頂點都在內、但邊從凹口穿出去
        ///   3. 沒有輪廓頂點嚴格落在片內部     —— 擋輪廓的尖角戳進片裡
        ///   4. 沒有洞的內部點落在片內部       —— 擋片整個蓋住一個洞
        ///   5. 每條片邊的區間中點都在區域內   —— 擋跨過捏點
        ///
        /// 規則 5 不能省。只有 1～4 的話，一片可以三個頂點都停在輪廓的角上、每條邊都只在
        /// 端點碰到輪廓（不算真交叉），卻有一整塊內部戳在捏點的另一側。測試裡有這個反例。
        ///
        /// 規則 2 必須排在規則 5 前面：沒有真交叉時，所有接觸點才保證是現成的頂點，
        /// 區間切割才不需要分數運算。
        /// </summary>
        public bool ContainsPolygon(IReadOnlyList<ExactVector2> convexPolygon)
        {
            int polygonVertexCount = convexPolygon == null ? 0 : convexPolygon.Count;
            if (polygonVertexCount < 3) return false;

            // 0
            if (!Bounds.Contains(BoundingBox.FromPoints(convexPolygon))) return false;

            // 1
            for (int vertexIndex = 0; vertexIndex < polygonVertexCount; vertexIndex++)
                if (Classify(convexPolygon[vertexIndex]) == RegionPosition.Outside) return false;

            // 2
            for (int vertexIndex = 0; vertexIndex < polygonVertexCount; vertexIndex++)
            {
                ExactVector2 polygonEdgeStart = convexPolygon[vertexIndex];
                ExactVector2 polygonEdgeEnd = convexPolygon[(vertexIndex + 1) % polygonVertexCount];

                for (int ringIndex = 0; ringIndex < Rings.Length; ringIndex++)
                {
                    ExactVector2[] ring = Rings[ringIndex];
                    for (int ringVertexIndex = 0; ringVertexIndex < ring.Length; ringVertexIndex++)
                    {
                        if (Geometry.SegmentsProperlyCross(
                                polygonEdgeStart, polygonEdgeEnd,
                                ring[ringVertexIndex], ring[(ringVertexIndex + 1) % ring.Length]))
                            return false;
                    }
                }
            }

            // 3
            for (int vertexIndex = 0; vertexIndex < Vertices.Length; vertexIndex++)
                if (Geometry.StrictlyInsideConvexPolygon(convexPolygon, Vertices[vertexIndex]))
                    return false;

            // 4：洞的內部點存的是 ×2 座標，把多邊形就地放大 2 倍去比，不另外配置陣列
            for (int holeIndex = 0; holeIndex < doubledHoleInteriorPoints.Length; holeIndex++)
                if (Geometry.StrictlyInsideConvexPolygon(
                        convexPolygon, doubledHoleInteriorPoints[holeIndex], 2))
                    return false;

            // 5
            for (int vertexIndex = 0; vertexIndex < polygonVertexCount; vertexIndex++)
            {
                if (!EdgeStaysInside(convexPolygon[vertexIndex],
                                     convexPolygon[(vertexIndex + 1) % polygonVertexCount]))
                    return false;
            }

            return true;
        }

        /// <summary>
        /// 區間中點掃描：把這條邊在「碰到輪廓的地方」切開，每一小段取中點驗一次。
        ///
        /// 為什麼中點就夠：兩個相鄰接觸點之間，這一小段不可能一半在內一半在外
        /// —— 要從內跑到外就得穿過輪廓，那會產生新的接觸點，而接觸點已經全部切開了。
        /// 所以每一小段要嘛整段在內、要嘛整段在外，取中點就能代表它。
        ///
        /// 走到這裡已經確定沒有真交叉（規則 2），接觸點只會是「輪廓頂點落在這條邊上」，
        /// 全是現成的精確座標。中點在 ×2 座標系裡就是兩端點相加。
        /// </summary>
        private bool EdgeStaysInside(ExactVector2 edgeStart, ExactVector2 edgeEnd)
        {
            List<ExactVector2> splitPoints = null;

            for (int vertexIndex = 0; vertexIndex < Vertices.Length; vertexIndex++)
            {
                ExactVector2 vertex = Vertices[vertexIndex];
                if (vertex == edgeStart || vertex == edgeEnd) continue;
                if (!Geometry.IsOnSegment(edgeStart, edgeEnd, vertex)) continue;

                if (splitPoints == null) splitPoints = new List<ExactVector2>();
                splitPoints.Add(vertex);
            }

            // 絕大多數情況：邊的中途沒碰到輪廓，只要驗一個中點
            if (splitPoints == null)
                return ClassifyDoubled(edgeStart + edgeEnd) != RegionPosition.Outside;

            ExactVector2 edgeDirection = edgeEnd - edgeStart;
            splitPoints.Sort((left, right) =>
                ExactVector2.Dot(left - edgeStart, edgeDirection)
                    .CompareTo(ExactVector2.Dot(right - edgeStart, edgeDirection)));

            ExactVector2 previousPoint = edgeStart;
            for (int splitIndex = 0; splitIndex < splitPoints.Count; splitIndex++)
            {
                ExactVector2 splitPoint = splitPoints[splitIndex];
                if (splitPoint == previousPoint) continue;      // 捏點會重複出現
                if (ClassifyDoubled(previousPoint + splitPoint) == RegionPosition.Outside) return false;
                previousPoint = splitPoint;
            }
            return ClassifyDoubled(previousPoint + edgeEnd) != RegionPosition.Outside;
        }

        // ---- 洞的內部點 ----------------------------------------------------

        /// <summary>
        /// 找一個嚴格落在這條洞環內部的點，回傳 ×2 座標。
        ///
        /// 做法是試頂點對的中點，然後直接驗證它在不在環內 —— 不去推理「這是不是耳朵」，
        /// 直接問答案，不中就換下一個。先試相鄰頂點的對角線（耳朵定理保證至少有兩個會中），
        /// 沒中再試所有頂點對。這一段只在建構時跑一次。
        ///
        /// 注意兩個座標系：中點是用原座標的頂點相加算出來的（a + b 就是中點的 ×2 座標），
        /// 但驗證要拿去跟 ×2 的環比。混用會差一個 2 倍。
        /// </summary>
        private static ExactVector2 FindDoubledInteriorPoint(ExactVector2[] ring,
                                                             ExactVector2[] doubledRing,
                                                             int ringIndex)
        {
            var singleDoubledRing = new[] { doubledRing };
            int vertexCount = ring.Length;

            for (int vertexIndex = 0; vertexIndex < vertexCount; vertexIndex++)
            {
                ExactVector2 previous = ring[(vertexIndex + vertexCount - 1) % vertexCount];
                ExactVector2 next = ring[(vertexIndex + 1) % vertexCount];
                ExactVector2 doubledMidpoint = previous + next;
                if (Classify(singleDoubledRing, doubledMidpoint) == RegionPosition.Inside)
                    return doubledMidpoint;
            }

            for (int firstIndex = 0; firstIndex < vertexCount; firstIndex++)
            {
                for (int secondIndex = firstIndex + 1; secondIndex < vertexCount; secondIndex++)
                {
                    ExactVector2 doubledMidpoint = ring[firstIndex] + ring[secondIndex];
                    if (Classify(singleDoubledRing, doubledMidpoint) == RegionPosition.Inside)
                        return doubledMidpoint;
                }
            }

            throw new InvalidOperationException(
                $"Region：第 {ringIndex} 條環（洞）找不到內部點。環是退化的，或頂點順序有問題。");
        }
    }
}
