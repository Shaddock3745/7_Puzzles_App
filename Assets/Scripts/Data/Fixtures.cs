using System;
using System.Collections.Generic;

namespace Tangram.Data
{
    /// <summary>已知的排列，給驗證與測試當基準（§5）。</summary>
    public static class Fixtures
    {
        /// <summary>
        /// 經典大正方形（邊長 4√2、面積 32）。
        /// 這裡用「邊長 4 的方格圖」座標寫（人腦好驗算），最後整體 ×√2 換算成 §0 的單位。
        /// 姿態由 TryMatch 反推，所以只需要寫頂點，不必手算旋轉值。
        /// </summary>
        /// <returns>
        /// 七片的絕對擺放，順序是大三角 ×2、中三角、小三角 ×2、正方形、平行四邊形
        /// ——跟 <see cref="PiecePrototype.TangramSet"/> 一致。每次呼叫都是新陣列。
        /// </returns>
        /// <exception cref="InvalidOperationException">頂點座標寫錯，對不上任何原型姿態。</exception>
        public static Placement[] ClassicSquare()
        {
            var pieceOutlines = new (PieceKind kind, (long x, long y)[] vertices)[]
            {
                (PieceKind.LargeTriangle,   new[] { (0L, 0L), (4L, 0L), (2L, 2L) }),
                (PieceKind.LargeTriangle,   new[] { (0L, 0L), (2L, 2L), (0L, 4L) }),
                (PieceKind.MediumTriangle,  new[] { (2L, 4L), (4L, 2L), (4L, 4L) }),
                (PieceKind.SmallTriangle,   new[] { (0L, 4L), (1L, 3L), (2L, 4L) }),
                (PieceKind.SmallTriangle,   new[] { (2L, 2L), (3L, 1L), (3L, 3L) }),
                (PieceKind.Square,          new[] { (1L, 3L), (2L, 2L), (3L, 3L), (2L, 4L) }),
                (PieceKind.Parallelogram,   new[] { (3L, 1L), (4L, 0L), (4L, 2L), (3L, 3L) }),
            };

            var placements = new Placement[pieceOutlines.Length];
            for (int pieceIndex = 0; pieceIndex < pieceOutlines.Length; pieceIndex++)
            {
                (long x, long y)[] gridVertices = pieceOutlines[pieceIndex].vertices;
                var scaledVertices = new ExactVector2[gridVertices.Length];

                for (int vertexIndex = 0; vertexIndex < scaledVertices.Length; vertexIndex++)
                    scaledVertices[vertexIndex] = new ExactVector2(          // ×√2
                        Scalar.FromRoot2Multiple(gridVertices[vertexIndex].x),
                        Scalar.FromRoot2Multiple(gridVertices[vertexIndex].y));

                if (!TryMatch(pieceOutlines[pieceIndex].kind, scaledVertices, out placements[pieceIndex]))
                    throw new InvalidOperationException(
                        $"ClassicSquare：第 {pieceIndex} 片（{pieceOutlines[pieceIndex].kind}）無法對上原型");
            }
            return placements;
        }

        /// <summary>
        /// 找出能讓原型頂點集合等於目標的姿態與平移。
        /// 比對的是頂點「集合」，所以對稱的片會有多組姿態成立，回傳先找到的那個
        /// —— 幾何上完全相同，沒有差別。匯入手繪或既有版面時也用得上。
        /// </summary>
        /// <param name="kind">要對的片種。</param>
        /// <param name="targetVertices">目標頂點，順序與繞向都不拘（只比集合）。</param>
        /// <param name="placement">成功時是對上的擺放；失敗時是 default。</param>
        /// <returns>
        /// 是否找得到。頂點數不符、或這組頂點根本不是該片種的形狀時回 false。
        /// 成本是 8 旋轉 × 2 翻面 × 頂點數，很小。
        /// </returns>
        public static bool TryMatch(PieceKind kind, IReadOnlyList<ExactVector2> targetVertices,
                                    out Placement placement)
        {
            placement = default;
            PiecePrototype prototype = PiecePrototype.Get(kind);
            if (prototype.Vertices.Length != targetVertices.Count) return false;

            var targetPoints = new HashSet<ExactVector2>(targetVertices);

            for (int rotation = 0; rotation < 8; rotation++)
            {
                for (int flipIndex = 0; flipIndex < 2; flipIndex++)
                {
                    bool flipped = flipIndex == 1;
                    var rotatedVertices = new ExactVector2[prototype.Vertices.Length];
                    bool isLegalPose = true;

                    for (int vertexIndex = 0; vertexIndex < rotatedVertices.Length && isLegalPose; vertexIndex++)
                    {
                        try
                        {
                            rotatedVertices[vertexIndex] =
                                Symmetry.Apply(rotation, flipped, prototype.Vertices[vertexIndex]);
                        }
                        catch (InvalidOperationException)
                        {
                            isLegalPose = false;   // 這個姿態離開了 ℤ[√2]，跳過
                        }
                    }
                    if (!isLegalPose) continue;

                    for (int anchorIndex = 0; anchorIndex < rotatedVertices.Length; anchorIndex++)
                    {
                        ExactVector2 candidateTranslation = targetVertices[0] - rotatedVertices[anchorIndex];
                        bool allVerticesMatch = true;

                        for (int vertexIndex = 0; vertexIndex < rotatedVertices.Length; vertexIndex++)
                        {
                            if (!targetPoints.Contains(candidateTranslation + rotatedVertices[vertexIndex]))
                            {
                                allVerticesMatch = false;
                                break;
                            }
                        }

                        if (allVerticesMatch)
                        {
                            placement = new Placement(kind, rotation, flipped, candidateTranslation);
                            return true;
                        }
                    }
                }
            }
            return false;
        }
    }
}
