using System;
using System.Collections.Generic;

namespace Tangram.Data
{
    /// <summary>
    /// 從七片算出 walks（§2.3）：
    /// 1. 收集所有片的邊，在所有交點處切斷（含 T 型頂點）
    /// 2. 保留「兩側材質不同」的邊 → 邊界邊
    /// 3. 每條邊界邊定向成「內部在左」
    /// 4. 從任一未走過的半邊開始追，追到回起點；重複到走完
    /// </summary>
    public static class BoundaryTracer
    {
        private readonly struct HalfEdge
        {
            public readonly ExactVector2 Start;
            public readonly ExactVector2 End;
            public readonly int Direction;
            public readonly Scalar Length;

            /// <param name="start">有向小段的起點。</param>
            /// <param name="end">有向小段的終點。</param>
            /// <param name="direction">0~7 的方向。</param>
            /// <param name="length">這一小段的長度。</param>
            public HalfEdge(ExactVector2 start, ExactVector2 end, int direction, Scalar length)
            {
                Start = start;
                End = end;
                Direction = direction;
                Length = length;
            }
        }

        /// <summary>從一組不重疊的片算出它們聯集的外框。</summary>
        /// <param name="pieces">
        /// 要追輪廓的片。<b>前提是彼此不重疊</b>——有重疊時行為未定義，
        /// 呼叫前該先過 <see cref="Geometry.NoOverlap"/>。
        /// </param>
        /// <returns>
        /// 所有封閉環，外環與洞環混在一起。要分辨用 <see cref="Walk.IsOuter"/>。
        /// 共線段已經合併，但捏點會被保留。
        ///
        /// ⚠️ <b>環的「順序」不保證</b>（取決於哪條未使用的半邊索引最小，也就是片的枚舉順序），
        /// 但<b>環的「內容」保證一樣</b>——同一組片不管以什麼順序傳進來，
        /// 都會描出同一組環（見 <see cref="NextBoundaryEdge"/>）。
        /// 要固定順序請走 <c>Canonical.NormalizePuzzle</c>，它會依 key 排序。
        /// </returns>
        /// <exception cref="InvalidOperationException">
        /// 有邊不在 45° 網格上，或追環中斷（邊界不封閉，通常代表輸入有重疊）。
        /// </exception>
        public static List<Walk> Trace(IReadOnlyList<Placement> pieces)
        {
            var polygons = new List<ExactVector2[]>(pieces.Count);
            var cutPoints = new HashSet<ExactVector2>();

            foreach (Placement piece in pieces)
            {
                ExactVector2[] vertices = piece.Vertices();   // 已正規化成 CCW → 內部在左
                polygons.Add(vertices);
                foreach (ExactVector2 vertex in vertices) cutPoints.Add(vertex);
            }

            // 1+2. 切成不可再分的有向小段，並統計每段被幾片擁有
            var segments = new List<HalfEdge>();
            var ownerCounts = new Dictionary<(ExactVector2, ExactVector2), int>();

            foreach (ExactVector2[] polygon in polygons)
            {
                for (int vertexIndex = 0; vertexIndex < polygon.Length; vertexIndex++)
                {
                    ExactVector2 edgeStart = polygon[vertexIndex];
                    ExactVector2 edgeEnd = polygon[(vertexIndex + 1) % polygon.Length];

                    foreach (HalfEdge segment in Split(edgeStart, edgeEnd, cutPoints))
                    {
                        segments.Add(segment);
                        var key = UndirectedKey(segment.Start, segment.End);
                        ownerCounts.TryGetValue(key, out int count);
                        ownerCounts[key] = count + 1;
                    }
                }
            }

            // 被兩片共用 = 內部邊；只有一片 = 邊界邊。方向沿用該片的 CCW 繞向（內部在左）
            var boundaryEdges = new List<HalfEdge>();
            foreach (HalfEdge segment in segments)
                if (ownerCounts[UndirectedKey(segment.Start, segment.End)] == 1)
                    boundaryEdges.Add(segment);

            // 出邊表 + 邊界（無向）度數
            var outgoingEdgesByVertex = new Dictionary<ExactVector2, List<int>>();
            var boundaryDegree = new Dictionary<ExactVector2, int>();

            for (int edgeIndex = 0; edgeIndex < boundaryEdges.Count; edgeIndex++)
            {
                ExactVector2 startVertex = boundaryEdges[edgeIndex].Start;
                if (!outgoingEdgesByVertex.TryGetValue(startVertex, out var outgoing))
                    outgoingEdgesByVertex[startVertex] = outgoing = new List<int>();
                outgoing.Add(edgeIndex);

                Increment(boundaryDegree, boundaryEdges[edgeIndex].Start);
                Increment(boundaryDegree, boundaryEdges[edgeIndex].End);
            }

            // 4. 追環
            var isUsed = new bool[boundaryEdges.Count];
            var walks = new List<Walk>();

            for (int startEdgeIndex = 0; startEdgeIndex < boundaryEdges.Count; startEdgeIndex++)
            {
                if (isUsed[startEdgeIndex]) continue;

                var ring = new List<int>();
                int currentEdgeIndex = startEdgeIndex;
                while (true)
                {
                    isUsed[currentEdgeIndex] = true;
                    ring.Add(currentEdgeIndex);

                    ExactVector2 arrivalVertex = boundaryEdges[currentEdgeIndex].End;
                    int nextEdgeIndex = NextBoundaryEdge(
                        boundaryEdges, outgoingEdgesByVertex,
                        arrivalVertex, boundaryEdges[currentEdgeIndex].Direction);

                    if (nextEdgeIndex < 0)
                        throw new InvalidOperationException("追環中斷：邊界不封閉 " + arrivalVertex);

                    // ⚠️ 收工條件看的是「下一條是不是起點那條邊」，不是「有沒有走回起點那個頂點」。
                    // 捏點會被同一圈經過兩次，所以用頂點問的話，起點剛好是捏點時
                    // 第一次經過就會誤判成走完了——同一個形狀會因為從哪條邊起步而
                    // 描出不同的環，而且不報錯（批次統計時實際撞到過）。
                    // 邊只會被經過一次，所以用邊問才是對的
                    if (nextEdgeIndex == startEdgeIndex) break;

                    // 配對是進邊與出邊的一對一排列，所以正確的下一條不可能已經走過
                    // （每條邊只有一個前一步）。真的撞到代表配對壞了，例如同一個頂點
                    // 冒出兩條同方向的出邊。與其無限繞圈，不如當場炸掉
                    if (isUsed[nextEdgeIndex])
                        throw new InvalidOperationException(
                            $"追環出錯：{arrivalVertex} 的下一條邊已經走過了。" +
                            "頂點的進出邊配對不是一對一，邊的切分可能有重複");

                    currentEdgeIndex = nextEdgeIndex;
                }
                walks.Add(BuildWalk(boundaryEdges, boundaryDegree, ring));
            }
            return walks;
        }

        /// <summary>
        /// 頂點分岔規則（點接觸靠這條）：以某個方向抵達頂點，取其反向為起點，
        /// 順時針掃描該頂點的出邊，取第一個找到的 —— 等價於「取最大左轉」。
        ///
        /// ⚠️ <b>只看角度，不看走過沒有。</b>這樣「從這條進來、往哪條出去」
        /// 就是頂點上進邊與出邊的一個<b>固定的一對一配對</b>，
        /// 而配對的循環就是環——從循環上哪一條邊起步都會得到同一個循環。
        ///
        /// 原本這裡多一個「還沒走過」的條件（註解寫「可行的最大左轉」）。
        /// 在一般頂點沒差（只有一條出邊），但捏點度數是 4、有三條候選出邊，
        /// <b>先追的那一圈會先吃掉一條，後追的只能挑剩下的</b>——
        /// 於是同一個形狀會因為片的枚舉順序不同而描出不同數量的環，而且不報錯。
        ///
        /// 💡 <b>「最大左轉」本身是對的，不要改成右轉。</b>它給出的正是拓樸上正確的答案：
        /// 材料內部分成幾塊連通區域就描出幾條環。兩個正方形只靠一角相碰時內部是兩塊 → 兩條環；
        /// 挖掉的三角形尖端頂到外框時內部仍是一塊（可以從三角形上面繞過去）→ 一條自我接觸的環。
        /// </summary>
        /// <param name="boundaryEdges">全部的邊界半邊。</param>
        /// <param name="outgoingEdgesByVertex">頂點 → 從它出發的半邊索引清單。</param>
        /// <param name="vertex">目前站在哪個頂點。</param>
        /// <param name="arrivalDirection">是以哪個方向走到這個頂點的。</param>
        /// <returns>
        /// 下一條要走的半邊索引。這個頂點沒有出邊時回 −1
        /// （代表邊界不封閉，呼叫端會丟例外）。
        /// </returns>
        private static int NextBoundaryEdge(List<HalfEdge> boundaryEdges,
                                            Dictionary<ExactVector2, List<int>> outgoingEdgesByVertex,
                                            ExactVector2 vertex,
                                            int arrivalDirection)
        {
            if (!outgoingEdgesByVertex.TryGetValue(vertex, out var candidates)) return -1;

            int reverseDirection = Symmetry.Modulo8(arrivalDirection + 4);
            for (int clockwiseTurn = 0; clockwiseTurn < 8; clockwiseTurn++)
            {
                int wantedDirection = Symmetry.Modulo8(reverseDirection - clockwiseTurn);
                foreach (int candidateIndex in candidates)
                    if (boundaryEdges[candidateIndex].Direction == wantedDirection)
                        return candidateIndex;
            }
            return -1;
        }

        /// <summary>
        /// 共線邊合併：只在該頂點邊界度數 = 2 時合併。
        /// 捏點度數是 4，合併會毀掉拓樸（§6 陷阱）。
        /// </summary>
        /// <param name="boundaryEdges">全部的邊界半邊。</param>
        /// <param name="boundaryDegree">頂點 → 有幾條邊界小段碰到它。一般頂點 2、捏點 4。</param>
        /// <param name="ring">這一圈的半邊索引，依走訪順序。</param>
        /// <returns>合併共線段之後的封閉環，anchor 是某個轉角（不會是被合併掉的中繼點）。</returns>
        private static Walk BuildWalk(List<HalfEdge> boundaryEdges,
                                      Dictionary<ExactVector2, int> boundaryDegree,
                                      List<int> ring)
        {
            int ringLength = ring.Count;

            // 第 ringPosition 段的起點能不能被前一段吃掉
            bool IsMergeableAt(int ringPosition)
            {
                HalfEdge previous = boundaryEdges[ring[(ringPosition - 1 + ringLength) % ringLength]];
                HalfEdge current = boundaryEdges[ring[ringPosition]];
                return previous.Direction == current.Direction
                    && boundaryDegree[current.Start] == 2;
            }

            // 從一個「不可合併」的位置起頭，避免首尾接縫處漏掉合併
            int startPosition = 0;
            for (int ringPosition = 0; ringPosition < ringLength; ringPosition++)
                if (!IsMergeableAt(ringPosition)) { startPosition = ringPosition; break; }

            var mergedEdges = new List<Edge>();
            ExactVector2 anchor = boundaryEdges[ring[startPosition]].Start;
            int currentDirection = -1;
            Scalar accumulatedLength = Scalar.Zero;

            for (int step = 0; step < ringLength; step++)
            {
                int ringPosition = (startPosition + step) % ringLength;
                HalfEdge edge = boundaryEdges[ring[ringPosition]];

                if (step > 0 && IsMergeableAt(ringPosition))
                {
                    accumulatedLength = accumulatedLength + edge.Length;
                }
                else
                {
                    if (step > 0) mergedEdges.Add(new Edge(currentDirection, accumulatedLength));
                    currentDirection = edge.Direction;
                    accumulatedLength = edge.Length;
                }
            }
            mergedEdges.Add(new Edge(currentDirection, accumulatedLength));

            return new Walk(anchor, mergedEdges.ToArray());
        }

        /// <summary>把一條邊在所有落在它內部的切點處切開。</summary>
        /// <param name="edgeStart">邊的起點。</param>
        /// <param name="edgeEnd">邊的終點。</param>
        /// <param name="cutPoints">所有可能的切點（全部片的頂點）。只有落在這條邊內部的會生效。</param>
        /// <returns>
        /// 依序 yield 出不可再分的有向小段。沒有切點時就只有一段（原邊本身）。
        /// 是延遲求值的迭代器，成本是每次呼叫掃一遍整個切點集合。
        /// </returns>
        /// <exception cref="InvalidOperationException">邊不在 45° 網格上。</exception>
        private static IEnumerable<HalfEdge> Split(ExactVector2 edgeStart, ExactVector2 edgeEnd,
                                                   HashSet<ExactVector2> cutPoints)
        {
            if (!Symmetry.TryDecompose(edgeEnd - edgeStart, out int direction, out Scalar edgeLength))
                throw new InvalidOperationException("邊不在 45° 網格上");

            var interiorPoints = new List<(Scalar offset, ExactVector2 point)>();
            foreach (ExactVector2 candidate in cutPoints)
            {
                if (!Geometry.StrictlyOnSegment(edgeStart, edgeEnd, candidate)) continue;
                Symmetry.TryDecompose(candidate - edgeStart, out _, out Scalar offset);
                interiorPoints.Add((offset, candidate));
            }
            interiorPoints.Sort((left, right) => left.offset.CompareTo(right.offset));

            ExactVector2 previousPoint = edgeStart;
            Scalar previousOffset = Scalar.Zero;
            foreach (var (offset, point) in interiorPoints)
            {
                yield return new HalfEdge(previousPoint, point, direction, offset - previousOffset);
                previousPoint = point;
                previousOffset = offset;
            }
            yield return new HalfEdge(previousPoint, edgeEnd, direction, edgeLength - previousOffset);
        }

        /// <summary>把兩端點依字典序排好，讓 A→B 與 B→A 得到同一個 key。</summary>
        /// <param name="start">一端。</param>
        /// <param name="end">另一端。</param>
        /// <returns>字典序小的在前的端點對，可以直接當 Dictionary 的 key。</returns>
        private static (ExactVector2, ExactVector2) UndirectedKey(ExactVector2 start, ExactVector2 end)
            => start.CompareTo(end) <= 0 ? (start, end) : (end, start);

        /// <summary>把某個 key 的計數加一，沒有的話從 0 開始。</summary>
        /// <param name="counts">計數表，會被就地修改。</param>
        /// <param name="key">要加一的 key。</param>
        private static void Increment(Dictionary<ExactVector2, int> counts, ExactVector2 key)
        {
            counts.TryGetValue(key, out int count);
            counts[key] = count + 1;
        }
    }
}
