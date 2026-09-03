using System;
using System.Collections.Generic;

namespace Tangram.Data
{
    /// <summary>全整數（ℤ[√2]）的多邊形工具。沒有容差，也不需要容差。</summary>
    public static class Geometry
    {
        /// <summary>shoelace，回傳面積 ×2。正 = CCW（外環），負 = CW（洞）。</summary>
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
        public static bool ConvexOverlap(IReadOnlyList<ExactVector2> first,
                                         IReadOnlyList<ExactVector2> second)
        {
            return !HasSeparatingAxis(first, second) && !HasSeparatingAxis(second, first);
        }

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
        public static bool AllConnected(IReadOnlyList<Placement> pieces)
        {
            int pieceCount = pieces.Count;
            if (pieceCount <= 1) return true;

            var ownerByNodePosition = new Dictionary<ExactVector2, int>();
            var parents = new int[pieceCount];
            for (int pieceIndex = 0; pieceIndex < pieceCount; pieceIndex++) parents[pieceIndex] = pieceIndex;

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
        public static Scalar Cross(ExactVector2 origin, ExactVector2 first, ExactVector2 second)
            => ExactVector2.Cross(first - origin, second - origin);

        /// <summary>
        /// 點是否落在線段上（含兩端點）。
        /// 與 StrictlyOnSegment 的差別：這裡含端點，而且不預設線段落在 45° 網格上
        /// （走叉積 + 內積，不經過 Symmetry.TryDecompose）。
        /// </summary>
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
    }
}
