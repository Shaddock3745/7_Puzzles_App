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
    ///
    /// 盒子模式下這支已經**退出遊玩迴圈**——過關判定不用它。
    /// 剩下的用途只有兩個：出題去重（PuzzleKey）、解去重與計數（ArrangementKey）。
    /// </summary>
    public static class Canonical
    {
        /// <summary>
        /// 排列的 canonical form（§1.3）。
        /// 對 16 種整體變換各序列化一次：平移到字典序最小頂點為原點、
        /// 每片的頂點環旋轉到自己的最小起點（片自身對稱因此自動被商掉）、
        /// 同種片之間排序，取字典序最小者。
        /// </summary>
        /// <param name="pieces">要算的絕對擺放。</param>
        /// <returns>
        /// 雜湊前的完整字串。<b>除錯時看這個比看 key 有用得多</b>——
        /// 兩個東西不相等時可以直接看出差在哪。
        /// </returns>
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

        /// <param name="pieces">要算的絕對擺放。</param>
        /// <returns>32 個 hex 字元的排列 key。轉過角度、鏡射過、平移過都會得到同一串。</returns>
        public static string ArrangementKey(IReadOnlyList<Placement> pieces)
            => Hash(ArrangementForm(pieces));

        /// <summary>
        /// 題目的 canonical form（§2.4）。不要對每條環各自正規化再拼回去（anchor 的變換會出錯），
        /// 直接對 16 種整體變換各重跑一次 walk 追蹤 —— 順便解決鏡射翻轉定向的問題。
        /// 邊數 10～30，重跑 16 次成本可忽略。
        /// </summary>
        /// <param name="pieces">要算的絕對擺放。</param>
        /// <returns>雜湊前的完整字串。</returns>
        /// <remarks><b>會跑 16 次輪廓追蹤，不要放在每幀的程式碼裡。</b></remarks>
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

                // 先平移再選起點：CanonicalStart 會把 anchor 換成新起點的座標，
                // 平移沒先做的話 anchor 會停在舊座標系
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

        /// <param name="pieces">要算的絕對擺放。</param>
        /// <returns>32 個 hex 字元的題目 key。同一個形狀不管怎麼擺都會得到同一串。</returns>
        public static string PuzzleKey(IReadOnlyList<Placement> pieces) => Hash(PuzzleForm(pieces));

        /// <summary>由既有 walks 直接算 key（使用者手繪輪廓用；此時沒有七片可重跑）。</summary>
        /// <param name="walks">輪廓環。</param>
        /// <returns>32 個 hex 字元的題目 key。</returns>
        /// <remarks>
        /// 注意：這條路徑<b>不保證</b>跟 <see cref="PuzzleKey"/> 對同一個形狀給出同一個 key。
        /// PuzzleKey 的 walks 來自 BoundaryTracer，共線段已經合併；手繪的輪廓如果多了
        /// 冗餘的共線分割點，邊序列就不一樣。真的要做手繪題目時得先寫一個 walk 正規化。
        /// </remarks>
        public static string PuzzleKeyFromWalks(IReadOnlyList<Walk> walks) => Hash(WalksForm(walks));

        /// <param name="walks">輪廓環。</param>
        /// <returns>雜湊前的完整字串。</returns>
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
        /// <param name="walk">要變換的環。</param>
        /// <param name="globalRotation">整體旋轉值 0~7。</param>
        /// <param name="globalFlipped">整體是否翻面。</param>
        /// <returns>變換後的新環，繞向與原本相同。原本的環不會被動到。</returns>
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
        /// <param name="walk">要正規化的環。</param>
        /// <returns>
        /// 幾何相同、但起點固定的新環。環有旋轉對稱時取索引最小的那個起點，結果仍然是確定的。
        /// 成本是 O(n²) 的字串建構，n 是邊數（10～30），可以接受。
        /// </returns>
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

        /// <param name="ring">頂點環。</param>
        /// <param name="newOrigin">要當成新原點的點。</param>
        /// <returns>每個頂點都減掉新原點的新陣列。原本的不會被動到。</returns>
        private static ExactVector2[] ShiftRing(ExactVector2[] ring, ExactVector2 newOrigin)
        {
            var shifted = new ExactVector2[ring.Length];
            for (int vertexIndex = 0; vertexIndex < ring.Length; vertexIndex++)
                shifted[vertexIndex] = ring[vertexIndex] - newOrigin;
            return shifted;
        }

        /// <summary>頂點環（已 CCW）旋轉到字典序最小頂點起點後序列化。</summary>
        /// <param name="ring">頂點環。</param>
        /// <returns>
        /// 從最小頂點開始、用空白隔開的座標字串。
        /// 這裡挑的是「最小的頂點」，跟 <see cref="CanonicalStart"/> 挑「最小的邊序列字串」
        /// 是兩種不同的準則——各自確定就夠了，不需要一致。
        /// </returns>
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

        /// <param name="form">雜湊前的 canonical 字串。</param>
        /// <returns>
        /// SHA-256 的前 16 bytes，轉成 32 個小寫 hex 字元。
        /// 128 bit 對這個用途遠遠夠用，長度固定，可以直接當 DB 主鍵。
        /// </returns>
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
