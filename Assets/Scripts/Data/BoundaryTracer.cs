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

            public HalfEdge(ExactVector2 start, ExactVector2 end, int direction, Scalar length)
            {
                Start = start;
                End = end;
                Direction = direction;
                Length = length;
            }
        }

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
                    if (arrivalVertex == boundaryEdges[startEdgeIndex].Start) break;

                    int nextEdgeIndex = NextBoundaryEdge(
                        boundaryEdges, outgoingEdgesByVertex, isUsed,
                        arrivalVertex, boundaryEdges[currentEdgeIndex].Direction);

                    if (nextEdgeIndex < 0)
                        throw new InvalidOperationException("追環中斷：邊界不封閉 " + arrivalVertex);

                    currentEdgeIndex = nextEdgeIndex;
                }
                walks.Add(BuildWalk(boundaryEdges, boundaryDegree, ring));
            }
            return walks;
        }

        /// <summary>
        /// 頂點分岔規則（點接觸靠這條）：以某個方向抵達頂點，取其反向為起點，
        /// 順時針掃描該頂點的出邊，取第一個找到的 —— 等價於「取可行的最大左轉」。
        /// </summary>
        private static int NextBoundaryEdge(List<HalfEdge> boundaryEdges,
                                            Dictionary<ExactVector2, List<int>> outgoingEdgesByVertex,
                                            bool[] isUsed,
                                            ExactVector2 vertex,
                                            int arrivalDirection)
        {
            if (!outgoingEdgesByVertex.TryGetValue(vertex, out var candidates)) return -1;

            int reverseDirection = Symmetry.Modulo8(arrivalDirection + 4);
            for (int clockwiseTurn = 0; clockwiseTurn < 8; clockwiseTurn++)
            {
                int wantedDirection = Symmetry.Modulo8(reverseDirection - clockwiseTurn);
                foreach (int candidateIndex in candidates)
                    if (!isUsed[candidateIndex] && boundaryEdges[candidateIndex].Direction == wantedDirection)
                        return candidateIndex;
            }
            return -1;
        }

        /// <summary>
        /// 共線邊合併：只在該頂點邊界度數 = 2 時合併。
        /// 捏點度數是 4，合併會毀掉拓樸（§6 陷阱）。
        /// </summary>
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
        private static (ExactVector2, ExactVector2) UndirectedKey(ExactVector2 start, ExactVector2 end)
            => start.CompareTo(end) <= 0 ? (start, end) : (end, start);

        private static void Increment(Dictionary<ExactVector2, int> counts, ExactVector2 key)
        {
            counts.TryGetValue(key, out int count);
            counts[key] = count + 1;
        }
    }
}
