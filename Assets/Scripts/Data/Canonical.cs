using System;
using System.Collections.Generic;
using System.Security.Cryptography;
using System.Text;

namespace Tangram.Data
{
    /// <summary>
    /// 一組擺放正規化之後的樣子：轉到 16 種整體變換裡字典序最小的那一版、
    /// 並平移到字典序最小的頂點當原點。由 <see cref="Canonical.NormalizePuzzle"/> 產生。
    ///
    /// <b>存題庫時 <see cref="Walks"/> 與 <see cref="Pieces"/> 要一起存</b>，
    /// 它們保證在同一個座標系——這正是這個型別存在的理由。
    /// </summary>
    public readonly struct CanonicalPuzzle
    {
        /// <summary>
        /// 正規化之後的擺放，順序與傳進去的一致。存題庫時這就是**參考解**。
        ///
        /// ⚠️ 對稱的形狀，從不同擺法正規化可能得到**不同但同樣正確**的一組
        /// （見 <see cref="Canonical.NormalizePuzzle"/> 的說明）。
        /// </summary>
        public readonly Placement[] Pieces;

        /// <summary>
        /// 正規化之後的外框，**依環的 key 排序**，與 <see cref="Form"/> 的組成一一對應。
        /// 存題庫時這就是**題目**。
        /// </summary>
        public readonly Walk[] Walks;

        /// <summary>雜湊前的完整字串。除錯時看這個比看 key 有用得多。</summary>
        public readonly string Form;

        /// <summary>32 個 hex 字元的題目 key。</summary>
        public readonly string Key;

        /// <summary>贏的那個整體旋轉 0~7。</summary>
        public readonly int GlobalRotation;

        /// <summary>贏的那一版有沒有鏡射。</summary>
        public readonly bool IsGloballyFlipped;

        /// <summary>
        /// 變換之後再套的平移量。
        /// 重現方式是 <c>原本的片.ApplyGlobal(GlobalRotation, IsGloballyFlipped).Translated(Translation)</c>
        /// ——順序不能反過來。
        /// </summary>
        public readonly ExactVector2 Translation;

        /// <param name="pieces">正規化之後的擺放。</param>
        /// <param name="walks">正規化之後的外框，已依 key 排序。</param>
        /// <param name="form">雜湊前的完整字串。</param>
        /// <param name="key">題目 key。</param>
        /// <param name="globalRotation">贏的整體旋轉。</param>
        /// <param name="isGloballyFlipped">贏的那一版有沒有鏡射。</param>
        /// <param name="translation">變換之後再套的平移量。</param>
        public CanonicalPuzzle(Placement[] pieces, Walk[] walks, string form, string key,
                               int globalRotation, bool isGloballyFlipped, ExactVector2 translation)
        {
            Pieces = pieces;
            Walks = walks;
            Form = form;
            Key = key;
            GlobalRotation = globalRotation;
            IsGloballyFlipped = isGloballyFlipped;
            Translation = translation;
        }
    }

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
        /// 題目的 canonical form（§2.4）。
        ///
        /// ⚠️ <b>這支只是 <see cref="NormalizePuzzle"/> 的門面</b>——正規化的規則只有那一份。
        /// 只要 form 的話用這支，要連正規化之後的擺放與外框一起拿的話用那支。
        /// </summary>
        /// <param name="pieces">要算的絕對擺放。</param>
        /// <returns>雜湊前的完整字串。</returns>
        /// <remarks><b>會跑 16 次輪廓追蹤，不要放在每幀的程式碼裡。</b></remarks>
        public static string PuzzleForm(IReadOnlyList<Placement> pieces)
            => NormalizePuzzle(pieces).Form;

        /// <summary>
        /// 把一組擺放**正規化**：轉到 16 種整體變換裡字典序最小的那一版，並平移到
        /// 字典序最小的頂點當原點。回傳正規化之後的擺放、外框、form 與 key。
        ///
        /// <b>為什麼要有這支</b>：出題器要把題目**存成正規化的樣子**，
        /// 這樣同一個形狀不管從哪一種擺法生出來，存進去都一模一樣，去重才乾淨。
        /// 但參考解<b>必須套一模一樣的變換</b>，否則題目在一個角度、答案在另一個角度——
        /// 症狀是提示的覆蓋框整組偏移或轉 45°，而且不報錯。
        ///
        /// ⚠️ 原本只有「算 key」那條路，它算得出變換卻不回報，出題器只能自己再跑一次 16 種。
        /// <b>那會讓正規化的規則有兩份</b>，哪天分家的症狀是「存進去的 key 跟存進去的外框對不起來」。
        /// 所以規則留在這裡，門面在上面那支。
        ///
        /// <b>平手時取第一個</b>：對稱的形狀會有好幾種變換算出同樣的 form，
        /// 底下用的是嚴格比較（<c>&lt; 0</c> 才換掉），所以 <see cref="Symmetry.GlobalTransforms"/>
        /// 裡先出現的那個會贏。這個行為是確定的，動它會讓已經存好的題庫對不上。
        ///
        /// ⚠️ <b>一個必須知道的不對稱</b>：對同一個形狀的 16 種擺法各叫一次，
        /// <see cref="CanonicalPuzzle.Key"/> 與 <see cref="CanonicalPuzzle.Walks"/> <b>一定相同</b>
        /// （變換群是封閉的，取遍 16 種得到的集合一樣），
        /// 但 <see cref="CanonicalPuzzle.Pieces"/> <b>可能不同</b>——
        /// 對稱的外框可以被不同的擺法填出來，平手時贏的變換不一樣，填法就不一樣。
        /// 兩組都是那個外框的正確解，所以不是 bug；
        /// <b>但不要寫「同一形狀正規化後擺放必然相同」這種假設</b>。
        /// </summary>
        /// <param name="pieces">要正規化的絕對擺放。</param>
        /// <returns>正規化的結果。</returns>
        /// <remarks><b>會跑 16 次輪廓追蹤</b>，跟原本算 key 是同一趟，不會更貴。</remarks>
        public static CanonicalPuzzle NormalizePuzzle(IReadOnlyList<Placement> pieces)
        {
            string smallestForm = null;
            int winningRotation = 0;
            bool winningFlipped = false;
            ExactVector2 winningTranslation = default;
            Walk[] winningWalks = null;

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
                var normalizedWalks = new List<Walk>(walks.Count);
                var walkKeys = new List<string>(walks.Count);
                foreach (Walk walk in walks)
                {
                    Walk normalized = CanonicalStart(walk.Translated(-smallestVertex));
                    normalizedWalks.Add(normalized);
                    walkKeys.Add(normalized.ToKey());
                }

                // 環的順序跟著 key 一起排，回傳的 Walks 才會與 form 的組成一一對應。
                // 不排的話順序取決於 BoundaryTracer 從哪條半邊起步，那是實作細節不該外洩
                var order = new int[walkKeys.Count];
                for (int i = 0; i < order.Length; i++) order[i] = i;
                Array.Sort(order, (left, right)
                    => string.CompareOrdinal(walkKeys[left], walkKeys[right]));

                var sortedKeys = new string[order.Length];
                var sortedWalks = new Walk[order.Length];
                for (int i = 0; i < order.Length; i++)
                {
                    sortedKeys[i] = walkKeys[order[i]];
                    sortedWalks[i] = normalizedWalks[order[i]];
                }

                string form = string.Join(";", sortedKeys);
                if (smallestForm == null || string.CompareOrdinal(form, smallestForm) < 0)
                {
                    smallestForm = form;
                    winningRotation = globalRotation;
                    winningFlipped = globalFlipped;
                    winningTranslation = -smallestVertex;
                    winningWalks = sortedWalks;
                }
            }

            // 贏的那一版重新套到原本的七片上。7 次變換，相對 16 次追蹤可忽略
            var normalizedPieces = new Placement[pieces.Count];
            for (int pieceIndex = 0; pieceIndex < pieces.Count; pieceIndex++)
                normalizedPieces[pieceIndex] = pieces[pieceIndex]
                    .ApplyGlobal(winningRotation, winningFlipped)
                    .Translated(winningTranslation);

            return new CanonicalPuzzle(normalizedPieces, winningWalks ?? new Walk[0],
                                       smallestForm, Hash(smallestForm),
                                       winningRotation, winningFlipped, winningTranslation);
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
