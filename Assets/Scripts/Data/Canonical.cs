using System;
using System.Collections.Generic;
using System.Security.Cryptography;
using System.Text;

namespace Tangram.Data
{
    /// <summary>
    /// canonical key。全程整數，雜湊穩定。
    /// 相對編碼不是 canonical form（同一排列有多種生成樹），
    /// 所以去重一律先還原成絕對座標（§1.3、§6 陷阱）。
    /// </summary>
    public static class Canonical
    {
        /// <summary>
        /// 排列的 canonical form（§1.3）。
        /// 對 16 種整體變換各序列化一次：平移到字典序最小頂點為原點、
        /// 每片的頂點環旋轉到自己的最小起點（片自身對稱因此自動被商掉）、
        /// 同種片之間排序，取字典序最小者。
        /// </summary>
        public static string ArrangementForm(IReadOnlyList<Placement> pieces)
        {
            string smallestForm = null;

            foreach (var (globalRotation, globalFlipped) in Symmetry.GlobalTransforms)
            {
                var vertexRings = new List<ExactVector2[]>(pieces.Count);
                var pieceKinds = new List<PieceKind>(pieces.Count);
                ExactVector2 smallestVertex = default;
                bool hasVertex = false;

                foreach (Placement piece in pieces)
                {
                    ExactVector2[] ring = piece.ApplyGlobal(globalRotation, globalFlipped).Vertices();
                    vertexRings.Add(ring);
                    pieceKinds.Add(piece.Kind);

                    foreach (ExactVector2 vertex in ring)
                    {
                        if (!hasVertex || vertex.CompareTo(smallestVertex) < 0)
                        {
                            smallestVertex = vertex;
                            hasVertex = true;
                        }
                    }
                }

                var pieceKeys = new List<string>(pieces.Count);
                for (int pieceIndex = 0; pieceIndex < vertexRings.Count; pieceIndex++)
                {
                    ExactVector2[] shiftedRing = ShiftRing(vertexRings[pieceIndex], smallestVertex);
                    pieceKeys.Add(PieceKindNames.ToStorageString(pieceKinds[pieceIndex])
                                  + "{" + RingKey(shiftedRing) + "}");
                }
                pieceKeys.Sort(StringComparer.Ordinal);

                string form = string.Join(";", pieceKeys);
                if (smallestForm == null || string.CompareOrdinal(form, smallestForm) < 0)
                    smallestForm = form;
            }
            return smallestForm;
        }

        public static string ArrangementKey(IReadOnlyList<Placement> pieces)
            => Hash(ArrangementForm(pieces));

        /// <summary>
        /// 題目的 canonical form（§2.4）。不要對每條環各自正規化再拼回去（anchor 的變換會出錯），
        /// 直接對 16 種整體變換各重跑一次 walk 追蹤 —— 順便解決鏡射翻轉定向的問題。
        /// 邊數 10～30，重跑 16 次成本可忽略。
        /// </summary>
        public static string PuzzleForm(IReadOnlyList<Placement> pieces)
        {
            string smallestForm = null;

            foreach (var (globalRotation, globalFlipped) in Symmetry.GlobalTransforms)
            {
                var transformedPieces = new List<Placement>(pieces.Count);
                foreach (Placement piece in pieces)
                    transformedPieces.Add(piece.ApplyGlobal(globalRotation, globalFlipped));

                List<Walk> walks = BoundaryTracer.Trace(transformedPieces);

                ExactVector2 smallestVertex = default;
                bool hasVertex = false;
                foreach (Walk walk in walks)
                {
                    foreach (ExactVector2 vertex in walk.Vertices())
                    {
                        if (!hasVertex || vertex.CompareTo(smallestVertex) < 0)
                        {
                            smallestVertex = vertex;
                            hasVertex = true;
                        }
                    }
                }

                var walkKeys = new List<string>(walks.Count);
                foreach (Walk walk in walks)
                    walkKeys.Add(CanonicalStart(walk.Translated(-smallestVertex)).ToKey());
                walkKeys.Sort(StringComparer.Ordinal);

                string form = string.Join(";", walkKeys);
                if (smallestForm == null || string.CompareOrdinal(form, smallestForm) < 0)
                    smallestForm = form;
            }
            return smallestForm;
        }

        public static string PuzzleKey(IReadOnlyList<Placement> pieces) => Hash(PuzzleForm(pieces));

        /// <summary>由既有 walks 直接算 key（使用者手繪輪廓用；此時沒有七片可重跑）。</summary>
        public static string PuzzleKeyFromWalks(IReadOnlyList<Walk> walks) => Hash(WalksForm(walks));

        public static string WalksForm(IReadOnlyList<Walk> walks)
        {
            string smallestForm = null;

            foreach (var (globalRotation, globalFlipped) in Symmetry.GlobalTransforms)
            {
                var transformedWalks = new List<Walk>(walks.Count);
                ExactVector2 smallestVertex = default;
                bool hasVertex = false;

                foreach (Walk walk in walks)
                {
                    Walk transformed = TransformWalk(walk, globalRotation, globalFlipped);
                    transformedWalks.Add(transformed);

                    foreach (ExactVector2 vertex in transformed.Vertices())
                    {
                        if (!hasVertex || vertex.CompareTo(smallestVertex) < 0)
                        {
                            smallestVertex = vertex;
                            hasVertex = true;
                        }
                    }
                }

                var walkKeys = new List<string>(transformedWalks.Count);
                foreach (Walk walk in transformedWalks)
                    walkKeys.Add(CanonicalStart(walk.Translated(-smallestVertex)).ToKey());
                walkKeys.Sort(StringComparer.Ordinal);

                string form = string.Join(";", walkKeys);
                if (smallestForm == null || string.CompareOrdinal(form, smallestForm) < 0)
                    smallestForm = form;
            }
            return smallestForm;
        }

        /// <summary>
        /// 旋轉時方向全部加同一個值；鏡射時用頂點重建（反轉順序加鏡射，兩次繞向反轉互相抵消），
        /// 比直接操作邊序列不容易寫錯（§2.2、§6）。
        /// </summary>
        public static Walk TransformWalk(Walk walk, int globalRotation, bool globalFlipped)
        {
            int edgeCount = walk.Edges.Length;
            var transformedEdges = new Edge[edgeCount];

            if (!globalFlipped)
            {
                for (int edgeIndex = 0; edgeIndex < edgeCount; edgeIndex++)
                    transformedEdges[edgeIndex] = new Edge(
                        Symmetry.ApplyToDirection(globalRotation, false, walk.Edges[edgeIndex].Direction),
                        walk.Edges[edgeIndex].Length);

                return new Walk(Symmetry.Apply(globalRotation, false, walk.Anchor), transformedEdges);
            }

            ExactVector2[] vertices = walk.Vertices();
            var mirroredVertices = new ExactVector2[edgeCount];
            for (int vertexIndex = 0; vertexIndex < edgeCount; vertexIndex++)
                mirroredVertices[vertexIndex] =
                    Symmetry.Apply(globalRotation, true, vertices[edgeCount - 1 - vertexIndex]);

            for (int vertexIndex = 0; vertexIndex < edgeCount; vertexIndex++)
            {
                ExactVector2 delta = mirroredVertices[(vertexIndex + 1) % edgeCount]
                                   - mirroredVertices[vertexIndex];
                Symmetry.TryDecompose(delta, out int direction, out Scalar length);
                transformedEdges[vertexIndex] = new Edge(direction, length);
            }
            return new Walk(mirroredVertices[0], transformedEdges);
        }

        /// <summary>把邊序列旋轉到自己的字典序最小起點。</summary>
        public static Walk CanonicalStart(Walk walk)
        {
            int edgeCount = walk.Edges.Length;
            int smallestStart = 0;
            string smallestKey = null;

            for (int startIndex = 0; startIndex < edgeCount; startIndex++)
            {
                var builder = new StringBuilder();
                for (int offset = 0; offset < edgeCount; offset++)
                {
                    if (offset > 0) builder.Append(' ');
                    builder.Append(walk.Edges[(startIndex + offset) % edgeCount].ToKey());
                }

                string key = builder.ToString();
                if (smallestKey == null || string.CompareOrdinal(key, smallestKey) < 0)
                {
                    smallestKey = key;
                    smallestStart = startIndex;
                }
            }
            return walk.RotatedTo(smallestStart);
        }

        private static ExactVector2[] ShiftRing(ExactVector2[] ring, ExactVector2 newOrigin)
        {
            var shifted = new ExactVector2[ring.Length];
            for (int vertexIndex = 0; vertexIndex < ring.Length; vertexIndex++)
                shifted[vertexIndex] = ring[vertexIndex] - newOrigin;
            return shifted;
        }

        /// <summary>頂點環（已 CCW）旋轉到字典序最小頂點起點後序列化。</summary>
        private static string RingKey(ExactVector2[] ring)
        {
            int vertexCount = ring.Length;
            int smallestStart = 0;
            for (int vertexIndex = 1; vertexIndex < vertexCount; vertexIndex++)
                if (ring[vertexIndex].CompareTo(ring[smallestStart]) < 0) smallestStart = vertexIndex;

            var builder = new StringBuilder();
            for (int offset = 0; offset < vertexCount; offset++)
            {
                if (offset > 0) builder.Append(' ');
                builder.Append(ring[(smallestStart + offset) % vertexCount].ToKey());
            }
            return builder.ToString();
        }

        public static string Hash(string form)
        {
            using (var algorithm = SHA256.Create())
            {
                byte[] digest = algorithm.ComputeHash(Encoding.UTF8.GetBytes(form));
                var builder = new StringBuilder(32);
                for (int byteIndex = 0; byteIndex < 16; byteIndex++)
                    builder.Append(digest[byteIndex].ToString("x2"));
                return builder.ToString();
            }
        }
    }
}
