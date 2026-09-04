using System;
using System.Collections.Generic;

namespace Tangram.Data
{
    /// <summary>§5 驗證清單。每次改動後跑。</summary>
    public static class Validation
    {
        /// <summary>一次驗證的結果。沒有失敗訊息就是全過。</summary>
        public sealed class Report
        {
            /// <summary>所有失敗訊息，依加入順序。</summary>
            public readonly List<string> Failures = new List<string>();

            /// <summary>是否完全沒有失敗。</summary>
            public bool Ok => Failures.Count == 0;

            /// <param name="message">失敗訊息，要寫清楚哪一項、實際值是多少。</param>
            public void Fail(string message) => Failures.Add(message);

            /// <returns>全過時回 <c>"OK"</c>，否則是換行串起來的所有失敗訊息。</returns>
            public override string ToString() => Ok ? "OK" : string.Join("\n", Failures.ToArray());
        }

        /// <summary>座標封閉：5 種原型 × 8 旋轉 × 2 翻轉，全部留在 ℤ[√2] 內。</summary>
        /// <param name="report">失敗會被加進這份報告，函式本身不回傳。</param>
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
        /// <param name="report">失敗會被加進這份報告。</param>
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
        /// <param name="pieces">要檢查的片。</param>
        /// <param name="report">失敗會被加進這份報告。</param>
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
        /// <param name="pieces">要檢查的片。</param>
        /// <param name="report">失敗會被加進這份報告，訊息含是哪兩片。</param>
        public static void CheckNoOverlap(IReadOnlyList<Placement> pieces, Report report)
        {
            if (!Geometry.NoOverlap(pieces, out int firstPiece, out int secondPiece))
                report.Fail($"第 {firstPiece} 片與第 {secondPiece} 片重疊");
        }

        /// <summary>連通：union-find，共用至少一點即相連（點接觸算數）。</summary>
        /// <param name="pieces">要檢查的片。</param>
        /// <param name="report">失敗會被加進這份報告。</param>
        public static void CheckConnected(IReadOnlyList<Placement> pieces, Report report)
        {
            if (!Geometry.AllConnected(pieces)) report.Fail("七片不連通");
        }

        /// <summary>往返：encode → decode → 與原排列一致（允許整體平移，decode 固定把第 0 片放原點）。</summary>
        /// <param name="pieces">要檢查的片。<b>第 0 片必須是 BFS 的根</b>，比對的平移基準靠它。</param>
        /// <param name="report">失敗會被加進這份報告。</param>
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

        /// <summary>整套跑一次（§5）。出題器產生的每一組結果都該過這關。</summary>
        /// <param name="pieces">
        /// 要檢查的片。傳 null 或空清單時只跑與輸入無關的兩項（座標封閉、節點數）。
        /// </param>
        /// <returns>一份報告。<c>report.Ok</c> 為 true 代表全過。</returns>
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
