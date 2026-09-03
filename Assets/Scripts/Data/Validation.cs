using System;
using System.Collections.Generic;

namespace Tangram.Data
{
    /// <summary>§5 驗證清單。每次改動後跑。</summary>
    public static class Validation
    {
        public sealed class Report
        {
            public readonly List<string> Failures = new List<string>();
            public bool Ok => Failures.Count == 0;
            public void Fail(string message) => Failures.Add(message);
            public override string ToString() => Ok ? "OK" : string.Join("\n", Failures.ToArray());
        }

        /// <summary>座標封閉：5 種原型 × 8 旋轉 × 2 翻轉，全部留在 ℤ[√2] 內。</summary>
        public static void CheckCoordinateClosure(Report report)
        {
            foreach (PieceKind kind in Enum.GetValues(typeof(PieceKind)))
            {
                PiecePrototype prototype = PiecePrototype.Get(kind);
                for (int rotation = 0; rotation < 8; rotation++)
                {
                    for (int flipIndex = 0; flipIndex < 2; flipIndex++)
                    {
                        bool flipped = flipIndex == 1;
                        foreach (ExactVector2 node in prototype.Nodes)
                        {
                            try
                            {
                                Symmetry.Apply(rotation, flipped, node);
                            }
                            catch (Exception error)
                            {
                                report.Fail($"座標封閉失敗：{kind} rotation={rotation} " +
                                            $"flipped={flipped} {node} → {error.Message}");
                            }
                        }
                    }
                }
            }
        }

        /// <summary>節點數必須符合 §0 的表（小三角 4、正方形 4、平行四邊形 6、中三角 7、大三角 11）。</summary>
        public static void CheckNodeCounts(Report report)
        {
            var expectedNodeCounts = new Dictionary<PieceKind, int>
            {
                { PieceKind.SmallTriangle, 4 },
                { PieceKind.Square, 4 },
                { PieceKind.Parallelogram, 6 },
                { PieceKind.MediumTriangle, 7 },
                { PieceKind.LargeTriangle, 11 }
            };

            foreach (var entry in expectedNodeCounts)
            {
                int actualNodeCount = PiecePrototype.Get(entry.Key).NodeCount;
                if (actualNodeCount != entry.Value)
                    report.Fail($"{entry.Key} 節點數 {actualNodeCount}，應為 {entry.Value}");
            }

            int totalNodeCount = 0;
            foreach (PieceKind kind in PiecePrototype.TangramSet)
                totalNodeCount += PiecePrototype.Get(kind).NodeCount;
            if (totalNodeCount != 47)
                report.Fail($"七片節點總數 {totalNodeCount}，應為 47");
        }

        /// <summary>面積：所有環有向面積總和 = 32（這裡比較 ×2 的值）。</summary>
        public static void CheckArea(IReadOnlyList<Placement> pieces, Report report)
        {
            Scalar pieceAreaSum = Scalar.Zero;
            foreach (Placement piece in pieces)
                pieceAreaSum = pieceAreaSum + Geometry.SignedDoubleArea(piece.Vertices());
            if (pieceAreaSum != Scalar.FromInteger(64))
                report.Fail($"七片面積總和 ×2 = {pieceAreaSum}，應為 64");

            List<Walk> walks = BoundaryTracer.Trace(pieces);
            Scalar walkAreaSum = Scalar.Zero;
            foreach (Walk walk in walks) walkAreaSum = walkAreaSum + walk.SignedDoubleArea();
            if (walkAreaSum != Scalar.FromInteger(64))
                report.Fail($"輪廓有向面積總和 ×2 = {walkAreaSum}，應為 64");
        }

        /// <summary>無重疊：任兩片內部交集面積 = 0（凸多邊形 → SAT + 精確算術）。</summary>
        public static void CheckNoOverlap(IReadOnlyList<Placement> pieces, Report report)
        {
            if (!Geometry.NoOverlap(pieces, out int firstPiece, out int secondPiece))
                report.Fail($"第 {firstPiece} 片與第 {secondPiece} 片重疊");
        }

        /// <summary>連通：union-find，共用至少一點即相連（點接觸算數）。</summary>
        public static void CheckConnected(IReadOnlyList<Placement> pieces, Report report)
        {
            if (!Geometry.AllConnected(pieces)) report.Fail("七片不連通");
        }

        /// <summary>往返：encode → decode → 與原排列一致（允許整體平移，decode 固定把第 0 片放原點）。</summary>
        public static void CheckRoundTrip(IReadOnlyList<Placement> pieces, Report report)
        {
            Arrangement arrangement;
            Placement[] decoded;
            try
            {
                arrangement = Arrangement.FromPlacements(pieces);
                decoded = arrangement.Decode();
            }
            catch (Exception error)
            {
                report.Fail("往返失敗：" + error.Message);
                return;
            }

            ExactVector2 offset = decoded[0].Translation - pieces[0].Translation;
            for (int pieceIndex = 0; pieceIndex < pieces.Count; pieceIndex++)
            {
                Placement expected = pieces[pieceIndex].Translated(offset);
                if (!decoded[pieceIndex].Equals(expected))
                    report.Fail($"往返不一致：第 {pieceIndex} 片 {decoded[pieceIndex]} ≠ {expected}");
            }

            if (arrangement.Links.Length != pieces.Count - 1)
                report.Fail($"links 應為 {pieces.Count - 1} 條，實際 {arrangement.Links.Length} 條");
        }

        /// <summary>整套跑一次（§5）。</summary>
        public static Report RunAll(IReadOnlyList<Placement> pieces)
        {
            var report = new Report();
            CheckCoordinateClosure(report);
            CheckNodeCounts(report);

            if (pieces != null && pieces.Count > 0)
            {
                CheckNoOverlap(pieces, report);
                CheckConnected(pieces, report);
                CheckArea(pieces, report);
                CheckRoundTrip(pieces, report);
            }
            return report;
        }
    }
}
