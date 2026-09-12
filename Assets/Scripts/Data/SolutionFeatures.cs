using System;
using System.Collections.Generic;

namespace Tangram.Data
{
    /// <summary>兩片大三角拼在一起時，合起來的那一塊是什麼形狀。</summary>
    public enum LargeTriangleUnion
    {
        /// <summary>兩片大三角沒有共用一整條邊。</summary>
        None = 0,

        /// <summary>共用斜邊 → 正方形。</summary>
        Square = 1,

        /// <summary>共用一條股、直角在同一端 → 更大的等腰直角三角形。</summary>
        Triangle = 2,

        /// <summary>共用一條股、直角在兩端 → 平行四邊形。</summary>
        Parallelogram = 3,
    }

    /// <summary>
    /// 一個題目的難度特徵裡，<b>要看參考解才算得出來</b>的那幾個（P2-6）。
    ///
    /// <b>為什麼不跟 <see cref="DifficultyFeatures"/> 放一起</b>：那一包的 XML doc 保證
    /// 「只由形狀決定」，而那條保證撐著〈對十六種整體變換全部不變〉那條測試，也撐著 ELO 的前提。
    /// 解的特徵混進去，那句話就得拿掉。分成兩包，兩邊各自都還能保證自己的不變性——
    /// 這一包的不變性是「<b>整組解一起做同一個變換，值不變</b>」。
    ///
    /// ⚠️ <b>這一包依賴「一題 = 形狀 + 參考解」</b>（`開發進度.md` G2 旁的說明）：
    /// 每一題都連著一組解存下來，難度才算得出來。玩家拼出的解可能跟參考解不同，
    /// 那時這幾個數字描述的是「參考解長什麼樣」，不是「玩家的解長什麼樣」——
    /// 使用者 2026-09-11 的判斷是這樣仍然有鑑別度，因為兩者通常差不多。
    ///
    /// 三個數字的權重（2026-09-12 在對照牆上定的）見 <see cref="DifficultyWeights"/>。
    /// </summary>
    public readonly struct SolutionFeatures
    {
        /// <summary>
        /// 大三角 ×2 與平行四邊形這三片裡，<b>只露出一個頂點（或完全沒露）的有幾片</b>，0～3。多 → 難。
        ///
        /// 露出 = 那個頂點沒有落在別片的邊上（含端點重合）。
        /// 使用者的觀察（2026-09-11）：大三角的尖角露在外面時，它的位置一眼就看得出來；
        /// 三片都藏起來時明顯變難。
        /// ⚠️ 完全沒露的也算進來——一片完全藏起來比只露一個更難，沒道理不算。
        /// </summary>
        public readonly int KeyPiecesExposingOneVertex;

        /// <summary>
        /// 兩片的邊重疊一段，而且<b>短的整條落在長的裡面</b>（端點可以重合）的地方有幾處。
        /// 接縫藏得乾淨 → 難，也比較好看。目前不進公式，留著當對照。
        /// </summary>
        public readonly int FullEdgeContactCount;

        /// <summary>
        /// 兩片的邊重疊一段，但<b>誰也沒把誰整條包住</b>的地方有幾處。少 → 難。
        ///
        /// 錯開的接觸會在輪廓上留下一個台階，等於告訴玩家「這裡是兩片的交界」。
        /// </summary>
        public readonly int StaggeredEdgeContactCount;

        /// <summary>
        /// 兩片大三角有沒有共用一整條邊、拼成一大塊，以及拼成什麼形狀。有 → 簡單。
        ///
        /// 使用者 2026-09-11 在 #219 發現：這種題意外地好拼——兩片合起來是一個大而簡單的形狀，
        /// 看出來之後剩下五片的空間很規整。⚠️ 而暫定難度本來把它們排得偏難
        /// （平均 1355、其他 1185），因為合起來那塊周長短、轉角少，正好踩中兩個主力特徵。
        /// ⚠️ 只認整條邊完全重合；錯開的貼合不算，那不會變成「一塊」。
        /// </summary>
        public readonly LargeTriangleUnion LargeTriangleUnion;

        /// <summary>兩片大三角有沒有拼成一塊。公式吃的是這個 0／1，不是拼成哪一種。</summary>
        public int LargeTrianglesJoined => LargeTriangleUnion == LargeTriangleUnion.None ? 0 : 1;

        /// <param name="keyPiecesExposingOneVertex">只露一個頂點的片數。</param>
        /// <param name="fullEdgeContactCount">完全接觸的處數。</param>
        /// <param name="staggeredEdgeContactCount">錯開接觸的處數。</param>
        /// <param name="largeTriangleUnion">兩片大三角合起來的形狀。</param>
        public SolutionFeatures(int keyPiecesExposingOneVertex,
                                int fullEdgeContactCount,
                                int staggeredEdgeContactCount,
                                LargeTriangleUnion largeTriangleUnion)
        {
            KeyPiecesExposingOneVertex = keyPiecesExposingOneVertex;
            FullEdgeContactCount = fullEdgeContactCount;
            StaggeredEdgeContactCount = staggeredEdgeContactCount;
            LargeTriangleUnion = largeTriangleUnion;
        }

        /// <summary>量一組解的特徵。</summary>
        /// <param name="pieces">參考解，七片。</param>
        /// <returns>這一組解的特徵。</returns>
        /// <exception cref="ArgumentNullException"><paramref name="pieces"/> 是 null。</exception>
        public static SolutionFeatures Measure(IReadOnlyList<Placement> pieces)
        {
            if (pieces == null) throw new ArgumentNullException(nameof(pieces));

            // 頂點算一次就好：三個數字都要用，而 Vertices() 每次呼叫都重新配置陣列
            var vertices = new ExactVector2[pieces.Count][];
            for (int index = 0; index < pieces.Count; index++) vertices[index] = pieces[index].Vertices();

            (int full, int staggered) = EdgeContactCountsOf(vertices);
            return new SolutionFeatures(
                KeyPiecesExposingOneVertexOf(pieces, vertices),
                full,
                staggered,
                LargeTriangleUnionOf(pieces, vertices));
        }

        /// <summary>
        /// 大三角 ×2 與平行四邊形這三片裡，只露出一個頂點（或完全沒露）的有幾片。
        /// 定義見 <see cref="KeyPiecesExposingOneVertex"/>。
        /// </summary>
        /// <param name="pieces">參考解。</param>
        /// <param name="vertices">每一片的頂點，索引與 <paramref name="pieces"/> 對齊。</param>
        /// <returns>0～3。</returns>
        private static int KeyPiecesExposingOneVertexOf(IReadOnlyList<Placement> pieces,
                                                        ExactVector2[][] vertices)
        {
            int piecesExposingOne = 0;
            for (int index = 0; index < pieces.Count; index++)
            {
                if (pieces[index].Kind != PieceKind.LargeTriangle
                    && pieces[index].Kind != PieceKind.Parallelogram) continue;

                int exposed = 0;
                foreach (ExactVector2 vertex in vertices[index])
                {
                    bool covered = false;
                    for (int other = 0; other < pieces.Count && !covered; other++)
                    {
                        if (other == index) continue;
                        ExactVector2[] otherVertices = vertices[other];
                        for (int k = 0; k < otherVertices.Length && !covered; k++)
                            // IsOnSegment 含端點，所以「跟別片的頂點重合」與「落在別片的邊上」一次涵蓋
                            covered = Geometry.IsOnSegment(otherVertices[k],
                                                           otherVertices[(k + 1) % otherVertices.Length],
                                                           vertex);
                    }
                    if (!covered) exposed++;
                }
                if (exposed <= 1) piecesExposingOne++;
            }
            return piecesExposingOne;
        }

        /// <summary>兩條邊的接觸方式。</summary>
        private enum EdgeContact
        {
            /// <summary>不共線、沒重疊，或只碰到一個點。</summary>
            None,

            /// <summary>重疊一段，但誰也沒把誰整條包住——會在輪廓上留下一個台階。</summary>
            Staggered,

            /// <summary>短的那條整條落在長的那條裡面（端點可以重合）。</summary>
            Full,
        }

        /// <summary>完全接觸與錯開接觸各有幾處。定義見兩個欄位自己的說明。</summary>
        /// <param name="vertices">每一片的頂點。</param>
        /// <returns>完全接觸、錯開接觸的處數。</returns>
        private static (int full, int staggered) EdgeContactCountsOf(ExactVector2[][] vertices)
        {
            int full = 0, staggered = 0;
            for (int first = 0; first < vertices.Length; first++)
            {
                ExactVector2[] firstVertices = vertices[first];
                for (int second = first + 1; second < vertices.Length; second++)
                {
                    ExactVector2[] secondVertices = vertices[second];
                    for (int i = 0; i < firstVertices.Length; i++)
                        for (int j = 0; j < secondVertices.Length; j++)
                        {
                            EdgeContact contact = ClassifyEdgeContact(
                                firstVertices[i], firstVertices[(i + 1) % firstVertices.Length],
                                secondVertices[j], secondVertices[(j + 1) % secondVertices.Length]);
                            if (contact == EdgeContact.Full) full++;
                            else if (contact == EdgeContact.Staggered) staggered++;
                        }
                }
            }
            return (full, staggered);
        }

        /// <summary>
        /// 兩條邊怎麼接觸。把第二條投影到第一條的方向上比區間——用內積、不做除法，全程精確。
        /// </summary>
        /// <param name="firstStart">第一條邊的起點。</param>
        /// <param name="firstEnd">第一條邊的終點。</param>
        /// <param name="secondStart">第二條邊的起點。</param>
        /// <param name="secondEnd">第二條邊的終點。</param>
        /// <returns>接觸方式。</returns>
        private static EdgeContact ClassifyEdgeContact(ExactVector2 firstStart, ExactVector2 firstEnd,
                                                       ExactVector2 secondStart, ExactVector2 secondEnd)
        {
            if (!Geometry.Cross(firstStart, firstEnd, secondStart).IsZero
                || !Geometry.Cross(firstStart, firstEnd, secondEnd).IsZero)
                return EdgeContact.None;   // 不共線

            ExactVector2 direction = firstEnd - firstStart;
            Scalar firstLow = Scalar.Zero;
            Scalar firstHigh = ExactVector2.Dot(direction, direction);
            Scalar projectedSecondStart = ExactVector2.Dot(secondStart - firstStart, direction);
            Scalar projectedSecondEnd = ExactVector2.Dot(secondEnd - firstStart, direction);
            Scalar secondLow = projectedSecondStart < projectedSecondEnd ? projectedSecondStart : projectedSecondEnd;
            Scalar secondHigh = projectedSecondStart < projectedSecondEnd ? projectedSecondEnd : projectedSecondStart;

            Scalar overlapLow = firstLow > secondLow ? firstLow : secondLow;
            Scalar overlapHigh = firstHigh < secondHigh ? firstHigh : secondHigh;
            if (!(overlapLow < overlapHigh)) return EdgeContact.None;   // 沒重疊，或只碰到一個點

            bool secondInsideFirst = secondLow >= firstLow && secondHigh <= firstHigh;
            bool firstInsideSecond = firstLow >= secondLow && firstHigh <= secondHigh;
            return secondInsideFirst || firstInsideSecond ? EdgeContact.Full : EdgeContact.Staggered;
        }

        /// <summary>
        /// 兩片大三角共用一整條邊時，合起來是什麼形狀。定義與理由見 <see cref="LargeTriangleUnion"/>。
        ///
        /// 大三角是等腰直角三角形，所以合起來只有三種可能：
        /// 共用斜邊 → 正方形；共用一條股、直角在同一端（兩個對角與共用邊的某個端點共線）→ 更大的三角形；
        /// 共用一條股、直角在兩端 → 平行四邊形。
        /// </summary>
        /// <param name="pieces">參考解。</param>
        /// <param name="vertices">每一片的頂點。</param>
        /// <returns>合起來的形狀，沒共用整條邊時是 <see cref="LargeTriangleUnion.None"/>。</returns>
        private static LargeTriangleUnion LargeTriangleUnionOf(IReadOnlyList<Placement> pieces,
                                                               ExactVector2[][] vertices)
        {
            var largeTriangles = new List<ExactVector2[]>(2);
            for (int index = 0; index < pieces.Count; index++)
                if (pieces[index].Kind == PieceKind.LargeTriangle) largeTriangles.Add(vertices[index]);
            if (largeTriangles.Count != 2) return LargeTriangleUnion.None;

            ExactVector2[] first = largeTriangles[0], second = largeTriangles[1];
            for (int i = 0; i < first.Length; i++)
            {
                ExactVector2 start = first[i], end = first[(i + 1) % first.Length];
                for (int j = 0; j < second.Length; j++)
                {
                    // 兩片都是 CCW，共用的那條邊在對方那裡是反著走的
                    if (second[j] != end || second[(j + 1) % second.Length] != start) continue;

                    ExactVector2 firstApex = first[(i + 2) % first.Length];
                    ExactVector2 secondApex = second[(j + 2) % second.Length];

                    // 對角是直角 ⇔ 共用的是斜邊
                    if (ExactVector2.Dot(start - firstApex, end - firstApex).IsZero)
                        return LargeTriangleUnion.Square;

                    bool straight = Geometry.Cross(firstApex, start, secondApex).IsZero
                                 || Geometry.Cross(firstApex, end, secondApex).IsZero;
                    return straight ? LargeTriangleUnion.Triangle : LargeTriangleUnion.Parallelogram;
                }
            }
            return LargeTriangleUnion.None;
        }

        /// <returns>給人看的字串，除錯與批次統計用。</returns>
        public override string ToString()
            => $"只露一點={KeyPiecesExposingOneVertex} 完全接觸={FullEdgeContactCount} " +
               $"錯開={StaggeredEdgeContactCount} 大三角合體={LargeTriangleUnion}";
    }
}
