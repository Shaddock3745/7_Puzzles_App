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

        /// <param name="minimum">左下角（兩個分量都取最小）。</param>
        /// <param name="maximum">右上角（兩個分量都取最大）。</param>
        public BoundingBox(ExactVector2 minimum, ExactVector2 maximum)
        {
            Minimum = minimum;
            Maximum = maximum;
        }

        /// <param name="points">要框住的點，至少一個。</param>
        /// <returns>剛好框住所有點的包圍盒。</returns>
        /// <exception cref="ArgumentException">點集合是 null 或空的。</exception>
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
        /// <param name="point">要測試的點。</param>
        /// <returns>點是否落在盒內或盒邊上。</returns>
        public bool Contains(ExactVector2 point)
            => point.X >= Minimum.X && point.X <= Maximum.X
            && point.Y >= Minimum.Y && point.Y <= Maximum.Y;

        /// <summary>other 是否完全落在自己裡面（含邊界）。</summary>
        /// <param name="other">要測試的包圍盒。</param>
        /// <returns>
        /// other 是否被自己完全包住。這是 ContainsPolygon 的第一道便宜篩選
        /// ——包圍盒都裝不下就不用往下算了。
        /// </returns>
        public bool Contains(BoundingBox other)
            => other.Minimum.X >= Minimum.X && other.Maximum.X <= Maximum.X
            && other.Minimum.Y >= Minimum.Y && other.Maximum.Y <= Maximum.Y;

        /// <returns>給人看的字串，形如 <c>"(0, 0) ~ (4√2, 4√2)"</c>。</returns>
        public override string ToString() => Minimum + " ~ " + Maximum;
    }

    /// <summary>
    /// 有洞的封閉二維區域，全程精確算術。盒子是一個 Region，片是一個 Region，
    /// P1-11 的剩餘連通塊也會是 Region。
    ///
    /// 建構時就把後面每一幀要用的東西算完並凍結：攤平的環頂點、放大座標的副本、
    /// 包圍盒、每個洞的一個內部點。之後唯讀。
    ///
    /// 為什麼要放大座標的副本：有些要判定的點是分數座標，而 Scalar 沒有除法。
    /// 把整個世界乘上一個倍數之後那些點就變成整數組合，
    /// 而「點在不在區域內」對等比例縮放不變，所以放大不影響答案。
    ///
    /// ⚠️ <b>倍數是 <see cref="ScaledCoordinateMultiplier"/>，不要在別處寫死數字。</b>
    /// 混用兩種尺度不會報錯，只會安靜地差一個倍數（§6 陷阱）。
    /// </summary>
    public sealed class Region
    {
        /// <summary>
        /// 放大座標的倍數。
        ///
        /// <b>為什麼是 4 不是 2</b>：這個副本要服務兩種點，取兩者的最小公倍數。
        ///
        /// <list type="bullet">
        /// <item><b>線段中點</b>（<see cref="EdgeStaysInside"/>）是 (a + b) / 2 → 需要 ×2</item>
        /// <item><b>洞的內部點</b>（<see cref="FindScaledInteriorPoint"/>）是
        ///   (a + 2b + c) / 4 → 需要 ×4</item>
        /// </list>
        ///
        /// ⚠️ 原本是 2，而洞的內部點那條規則當時是「兩個頂點的中點」，
        /// <b>三角形的洞永遠找不到內部點</b>——三角形沒有對角線，任兩個頂點都是一條邊，
        /// 中點必然落在邊界上。改成 4 是為了讓中位線的中點表示得出來。
        /// </summary>
        public const int ScaledCoordinateMultiplier = 4;

        /// <summary>每條環的頂點，CCW = 外環、CW = 洞。起點不重複出現在結尾。</summary>
        public readonly ExactVector2[][] Rings;

        /// <summary>與 Rings 同索引。用有向面積判斷，不存旗標（§2.1）。</summary>
        public readonly bool[] RingIsOuter;

        /// <summary>攤平的頂點清單。捏點會出現兩次，這是刻意保留的。</summary>
        public readonly ExactVector2[] Vertices;

        public readonly BoundingBox Bounds;

        /// <summary>每條環的 <see cref="ScaledCoordinateMultiplier"/> 倍座標副本。</summary>
        private readonly ExactVector2[][] scaledRings;

        /// <summary>
        /// 每個洞一個內部點，存 <see cref="ScaledCoordinateMultiplier"/> 倍座標。
        /// 沒有洞時是空陣列。
        /// </summary>
        private readonly ExactVector2[] scaledHoleInteriorPoints;

        /// <summary>邊界節點，第一次存取時算好並凍結。見 BoundaryNodes。</summary>
        private ExactVector2[] boundaryNodes;

        /// <param name="rings">
        /// 已經正規化過的環（起點沒有重複在結尾）。建構之後這些陣列就凍結了，不要再改。
        /// </param>
        /// <exception cref="ArgumentException">有環的頂點少於三個。</exception>
        private Region(ExactVector2[][] rings)
        {
            Rings = rings;
            RingIsOuter = new bool[rings.Length];
            scaledRings = new ExactVector2[rings.Length][];

            var flattenedVertices = new List<ExactVector2>();
            var holeInteriorPoints = new List<ExactVector2>();

            for (int ringIndex = 0; ringIndex < rings.Length; ringIndex++)
            {
                ExactVector2[] ring = rings[ringIndex];
                if (ring.Length < 3)
                    throw new ArgumentException($"Region：第 {ringIndex} 條環只有 {ring.Length} 個頂點");

                RingIsOuter[ringIndex] = Geometry.SignedDoubleArea(ring).Sign > 0;
                flattenedVertices.AddRange(ring);

                var scaledRing = new ExactVector2[ring.Length];
                for (int vertexIndex = 0; vertexIndex < ring.Length; vertexIndex++)
                    scaledRing[vertexIndex] = ring[vertexIndex] * ScaledCoordinateMultiplier;
                scaledRings[ringIndex] = scaledRing;

                if (!RingIsOuter[ringIndex])
                    holeInteriorPoints.Add(FindScaledInteriorPoint(ring, scaledRing, ringIndex));
            }

            Vertices = flattenedVertices.ToArray();
            scaledHoleInteriorPoints = holeInteriorPoints.ToArray();
            Bounds = BoundingBox.FromPoints(Vertices);
        }

        // ---- 建構 ----------------------------------------------------------

        /// <param name="rings">環的集合，繞向決定外環或洞（CCW = 外環，CW = 洞）。</param>
        /// <returns>新的區域。每條環都會被複製一份，之後改原本的陣列不影響它。</returns>
        /// <exception cref="ArgumentException">沒有給環，或有環的頂點少於三個。</exception>
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
        /// <param name="ring">頂點環。</param>
        /// <returns>只有一條環的區域。</returns>
        public static Region FromPolygon(IReadOnlyList<ExactVector2> ring)
            => new Region(new[] { NormalizeRing(ring) });

        /// <summary>從輪廓建區域。translation 就是盒子原點。</summary>
        /// <param name="walks">題目的輪廓環，外環與洞環混在一起沒關係。</param>
        /// <param name="translation">要套上去的平移量。盤面上的盒子就是傳盒子原點。</param>
        /// <returns>平移後的區域。</returns>
        /// <exception cref="ArgumentException">沒有給輪廓。</exception>
        /// <exception cref="InvalidOperationException">有環走不回起點（Walk.Vertices 會擋）。</exception>
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
        /// <param name="puzzle">題目。</param>
        /// <param name="boxOrigin">盒子原點，必須是精確座標，之後所有盒內判定都在這個座標系。</param>
        /// <returns>盤面上那個盒子的區域。</returns>
        public static Region FromPuzzle(Puzzle puzzle, ExactVector2 boxOrigin)
            => FromWalks(puzzle.Walks, boxOrigin);

        /// <summary>把一條環整理成內部格式：複製一份，並砍掉重複放在結尾的起點。</summary>
        /// <param name="ring">來源環。</param>
        /// <returns>新配置的頂點陣列，起點不重複出現在結尾。</returns>
        /// <exception cref="ArgumentException">環是 null 或頂點少於三個。</exception>
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

        // ---- 邊界節點 ------------------------------------------------------

        /// <summary>
        /// 區域邊界上的所有節點，規則跟片的節點完全一樣：每條邊界邊撒
        /// { 2a + 2b√2 | a,b ∈ ℕ } 且小於邊長的所有偏移（§0）。
        ///
        /// 用途是當吸附錨點。盒子的輪廓節點進了錨點集合之後，**第一片就有磁性**
        /// —— 它會吸到盒子的角落或邊上，而不是落在隨便一個格點上。
        ///
        /// 第一次存取時算好並快取，之後唯讀。捏點在 Vertices 裡會出現兩次，
        /// 但這裡是集合語意，重複的點只留一個。
        /// </summary>
        /// <returns>
        /// 邊界節點的世界座標（已含建構時的平移），沒有重複點。順序是
        /// 環的索引由小到大、環內沿頂點順序、每條邊上的偏移由小到大。
        /// </returns>
        public ExactVector2[] BoundaryNodes
        {
            get
            {
                if (boundaryNodes == null) boundaryNodes = BuildBoundaryNodes();
                return boundaryNodes;
            }
        }

        /// <summary>
        /// 走過每條環的每條邊，撒出合法偏移的節點。
        /// </summary>
        /// <returns>去重後的邊界節點。</returns>
        private ExactVector2[] BuildBoundaryNodes()
        {
            var nodes = new List<ExactVector2>();
            var seen = new HashSet<ExactVector2>();

            for (int ringIndex = 0; ringIndex < Rings.Length; ringIndex++)
            {
                ExactVector2[] ring = Rings[ringIndex];
                for (int vertexIndex = 0; vertexIndex < ring.Length; vertexIndex++)
                {
                    ExactVector2 edgeStart = ring[vertexIndex];
                    ExactVector2 edgeEnd = ring[(vertexIndex + 1) % ring.Length];

                    if (!Symmetry.TryDecompose(edgeEnd - edgeStart,
                                               out int direction, out Scalar edgeLength))
                        throw new InvalidOperationException(
                            $"Region.BoundaryNodes：第 {ringIndex} 條環的邊不在 45° 網格上");

                    // 含偏移 0、不含偏移 = 邊長（那個點是下一條邊的偏移 0）
                    foreach (Scalar offset in PiecePrototype.LegalOffsets(edgeLength))
                    {
                        ExactVector2 node = edgeStart + Symmetry.Step(direction, offset);
                        if (seen.Add(node)) nodes.Add(node);
                    }
                }
            }
            return nodes.ToArray();
        }

        // ---- 點的分類 ------------------------------------------------------

        /// <summary>點在區域的內部、邊界上、還是外部。</summary>
        /// <param name="point">要分類的點，原座標系。</param>
        /// <returns>三選一。邊界自成一類，不併進內或外。</returns>
        public RegionPosition Classify(ExactVector2 point) => Classify(Rings, point);

        /// <summary>
        /// 放大座標系下的分類。給分數座標的點用（它們在原座標系不是 ℤ[√2] 的點）。
        /// </summary>
        /// <param name="scaledPoint">
        /// 要分類的點，<b>已經乘過 <see cref="ScaledCoordinateMultiplier"/></b>。
        /// </param>
        /// <returns>三選一。傳錯座標系不會報錯，只會安靜地給錯答案。</returns>
        public RegionPosition ClassifyScaled(ExactVector2 scaledPoint)
            => Classify(scaledRings, scaledPoint);

        /// <summary>
        /// 兩點的中點落在哪。
        ///
        /// <b>存在的理由是把倍數關進來</b>：呼叫端只給兩個原座標的端點，
        /// 不必自己記得「中點在放大座標裡要乘幾」。
        /// 手動組放大座標是這個類別最容易安靜出錯的地方（§6 陷阱）。
        /// </summary>
        /// <param name="first">線段的一端，原座標。</param>
        /// <param name="second">線段的另一端，原座標。</param>
        /// <returns>中點在內部、邊界上、還是外部。</returns>
        private RegionPosition ClassifyMidpointOf(ExactVector2 first, ExactVector2 second)
            => ClassifyScaled((first + second) * (ScaledCoordinateMultiplier / 2));

        /// <summary>含邊界。</summary>
        /// <param name="point">要測試的點。</param>
        /// <returns>點是否在區域內或邊界上。</returns>
        public bool Contains(ExactVector2 point) => Classify(point) != RegionPosition.Outside;

        /// <summary>
        /// 射線法（水平 +X）。邊界先判掉，所以射線不會遇到「點就在邊上」的退化情況。
        /// 洞不用特別處理：穿越次數不看繞向，洞內的點自然變成偶數。
        /// </summary>
        /// <param name="rings">要比對的環，可以是原座標或 ×2 座標的那一套。</param>
        /// <param name="point">要分類的點，座標系要跟 rings 一致。</param>
        /// <returns>內部、邊界上、或外部。</returns>
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
        /// <param name="edgeStart">邊的起點。</param>
        /// <param name="edgeEnd">邊的終點。</param>
        /// <param name="point">射線的起點（往 +X 射出去）。</param>
        /// <returns>這條邊是否穿過射線。呼叫端每穿過一次就翻轉一次內外狀態。</returns>
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

        /// <summary>一片是否完全落在區域內。這就是吸附要傳給 SnapSolver 的 isAllowed。</summary>
        /// <param name="piece">要測試的片。</param>
        /// <returns>
        /// 片是否完全在區域內（含邊界）。
        /// <b>只管出界，不管跟別片重疊</b>——那是 <see cref="Geometry.ConvexOverlap"/> 的事，
        /// 合法擺放要兩個都過。
        /// </returns>
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
        /// <param name="convexPolygon">要測試的凸多邊形，繞向不拘。</param>
        /// <returns>
        /// 是否完全落在區域內（含邊界）。五條規則任何一條失敗就回 false。
        /// 全程精確算術，沒有容差。
        /// </returns>
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

            // 4：洞的內部點存的是放大座標，把多邊形就地放大同樣的倍數去比，不另外配置陣列
            for (int holeIndex = 0; holeIndex < scaledHoleInteriorPoints.Length; holeIndex++)
                if (Geometry.StrictlyInsideConvexPolygon(
                        convexPolygon, scaledHoleInteriorPoints[holeIndex],
                        ScaledCoordinateMultiplier))
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
        /// 全是現成的精確座標。中點的縮放交給 <see cref="ClassifyMidpointOf"/>。
        /// </summary>
        /// <param name="edgeStart">片邊的起點。</param>
        /// <param name="edgeEnd">片邊的終點。</param>
        /// <returns>這條邊有沒有整段都留在區域內（含邊界）。任何一小段的中點跑到外面就回 false。</returns>
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
                return ClassifyMidpointOf(edgeStart, edgeEnd) != RegionPosition.Outside;

            ExactVector2 edgeDirection = edgeEnd - edgeStart;
            splitPoints.Sort((left, right) =>
                ExactVector2.Dot(left - edgeStart, edgeDirection)
                    .CompareTo(ExactVector2.Dot(right - edgeStart, edgeDirection)));

            ExactVector2 previousPoint = edgeStart;
            for (int splitIndex = 0; splitIndex < splitPoints.Count; splitIndex++)
            {
                ExactVector2 splitPoint = splitPoints[splitIndex];
                if (splitPoint == previousPoint) continue;      // 捏點會重複出現
                if (ClassifyMidpointOf(previousPoint, splitPoint) == RegionPosition.Outside) return false;
                previousPoint = splitPoint;
            }
            return ClassifyMidpointOf(previousPoint, edgeEnd) != RegionPosition.Outside;
        }

        // ---- 洞的內部點 ----------------------------------------------------

        /// <summary>
        /// 找一個嚴格落在這條洞環內部的點，回傳放大座標。
        ///
        /// <b>候選是「兩條相鄰邊的中點，再取中點」</b>：連續三個頂點 a、b、c，
        /// 取 (a+b)/2 與 (b+c)/2 的中點，也就是 <b>(a + 2b + c) / 4</b>。
        /// 掃過環上每一個 b，直接驗證候選在不在環內——不去推理「這是不是耳朵」，
        /// 直接問答案，不中就換下一個。這一段只在建構時跑一次。
        ///
        /// <b>為什麼一定找得到</b>：耳朵定理保證任何簡單多邊形都有耳朵
        /// （n = 3 時整個三角形自己就是，n ≥ 4 時至少有兩個）。
        /// 在耳朵尖 b 上，三角形 abc 整個落在多邊形裡，而上面那個點是三角形
        /// <b>中位線的中點</b>，只要 a、b、c 不共線就嚴格落在三角形內部。
        ///
        /// ⚠️ <b>共線的三元組會產生落在邊界上的退化候選</b>，所以每個候選都要驗。
        /// 這不是理論上的顧慮——<c>BoundaryTracer</c> 的共線邊只在邊界度數 = 2 時合併，
        /// 捏點附近真的會留下共線的中繼頂點。
        ///
        /// ⚠️ <b>舊版用的是「任兩個頂點的中點」，三角形的洞永遠會失敗</b>：
        /// 三角形沒有對角線，任兩個頂點就是一條邊，中點必然落在邊界上。
        /// 它引的耳朵定理需要 n ≥ 4 才有對角線可用。實測 85 條真實洞環，
        /// 舊規則只過 39 條（三角形那 46 條全掛），新規則 85 條全過。
        /// </summary>
        /// <param name="ring">洞的頂點環，原座標。</param>
        /// <param name="scaledRing">同一條環的放大座標副本。</param>
        /// <param name="ringIndex">環的索引，只用在錯誤訊息裡。</param>
        /// <returns>
        /// 該洞內部的某一個點，<b>回傳的是乘過 <see cref="ScaledCoordinateMultiplier"/> 的座標</b>。
        /// </returns>
        /// <exception cref="InvalidOperationException">環是退化的（例如所有頂點共線）。</exception>
        private static ExactVector2 FindScaledInteriorPoint(ExactVector2[] ring,
                                                            ExactVector2[] scaledRing,
                                                            int ringIndex)
        {
            var singleScaledRing = new[] { scaledRing };
            int vertexCount = ring.Length;

            for (int vertexIndex = 0; vertexIndex < vertexCount; vertexIndex++)
            {
                ExactVector2 previous = ring[(vertexIndex + vertexCount - 1) % vertexCount];
                ExactVector2 current = ring[vertexIndex];
                ExactVector2 next = ring[(vertexIndex + 1) % vertexCount];

                // (a + 2b + c) 就是 (a + 2b + c)/4 的 ×4 座標。
                // ⚠️ 這裡寫死的 ×4 與 ScaledCoordinateMultiplier 是同一件事——
                // 改倍數的話這一行的算式要跟著重推，不是乘一個係數就好
                ExactVector2 scaledCandidate = previous + current + current + next;
                if (Classify(singleScaledRing, scaledCandidate) == RegionPosition.Inside)
                    return scaledCandidate;
            }

            throw new InvalidOperationException(
                $"Region：第 {ringIndex} 條環（洞）找不到內部點。環是退化的，或頂點順序有問題。");
        }
    }
}
