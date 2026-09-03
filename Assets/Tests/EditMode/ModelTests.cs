using System;
using System.Collections.Generic;
using NUnit.Framework;
using Tangram.Data;

namespace Tangram.Data.Tests
{
    /// <summary>§5 驗證清單 + §6 陷阱清單。每次改動後跑。</summary>
    public class ModelTests
    {
        private static Placement[] Square() => Fixtures.ClassicSquare();

        [Test]
        public void 座標封閉_五種原型_八旋轉_兩翻轉()
        {
            var report = new Validation.Report();
            Validation.CheckCoordinateClosure(report);
            Assert.IsTrue(report.Ok, report.ToString());
        }

        [Test]
        public void 節點數符合規格表()
        {
            var report = new Validation.Report();
            Validation.CheckNodeCounts(report);
            Assert.IsTrue(report.Ok, report.ToString());
        }

        [Test]
        public void 大正方形_通過完整驗證清單()
        {
            var report = Validation.RunAll(Square());
            Assert.IsTrue(report.Ok, report.ToString());
        }

        [Test]
        public void 大正方形_輪廓是一條四邊環_面積32()
        {
            List<Walk> walks = BoundaryTracer.Trace(Square());
            Assert.AreEqual(1, walks.Count);
            Assert.AreEqual(4, walks[0].Edges.Length);
            Assert.IsTrue(walks[0].IsOuter);
            Assert.AreEqual(Scalar.FromInteger(64), walks[0].SignedDoubleArea());
        }

        [Test]
        public void Key對十六種整體變換與平移不變()
        {
            Placement[] squarePieces = Square();
            string key = Canonical.ArrangementKey(squarePieces);
            string pkey = Canonical.PuzzleKey(squarePieces);

            foreach (var (rotation, flipped) in Symmetry.GlobalTransforms)
            {
                var moved = new List<Placement>();
                foreach (Placement piece in squarePieces) moved.Add(piece.ApplyGlobal(rotation, flipped));
                Assert.AreEqual(key, Canonical.ArrangementKey(moved), $"rotation={rotation} flipped={flipped}");
                Assert.AreEqual(pkey, Canonical.PuzzleKey(moved), $"rotation={rotation} flipped={flipped}");
            }

            var shifted = new List<Placement>();
            var delta = new ExactVector2(new Scalar(7, -3), new Scalar(-5, 11));
            foreach (Placement piece in squarePieces) shifted.Add(piece.Translated(delta));
            Assert.AreEqual(key, Canonical.ArrangementKey(shifted));
            Assert.AreEqual(pkey, Canonical.PuzzleKey(shifted));
        }

        [Test]
        public void 相對編碼往返一致()
        {
            Placement[] squarePieces = Square();
            Arrangement arrangement = Arrangement.FromPlacements(squarePieces).WithKeys();
            Assert.AreEqual(6, arrangement.Links.Length);
            Assert.AreEqual(7, arrangement.Pieces.Length);
            Assert.AreEqual(Canonical.ArrangementKey(squarePieces), Canonical.ArrangementKey(arrangement.Decode()));
        }

        [Test]
        public void Links不是生成樹時Decode會擋下來()
        {
            Arrangement arrangement = Arrangement.FromPlacements(Square());
            var broken = new Arrangement(arrangement.Pieces, new[] { arrangement.Links[0] });
            Assert.Throws<InvalidOperationException>(() => broken.Decode());
        }

        [Test]
        public void 點接觸_算相連_不算重疊_產生兩條環()
        {
            var pieces = new[]
            {
                new Placement(PieceKind.SmallTriangle, 0, false, ExactVector2.Zero),
                new Placement(PieceKind.SmallTriangle, 4, false, new ExactVector2(4, 0)),
            };
            Assert.IsTrue(Geometry.AllConnected(pieces));
            Assert.IsTrue(Geometry.NoOverlap(pieces, out _, out _));
            Assert.AreEqual(2, BoundaryTracer.Trace(pieces).Count, "捏點應產生兩條各自簡單的環");
        }

        [Test]
        public void T型頂點_邊要切斷_面積正確()
        {
            var pieces = new[]
            {
                new Placement(PieceKind.LargeTriangle, 0, false, ExactVector2.Zero),
                new Placement(PieceKind.SmallTriangle, 4, false, new ExactVector2(0, 2)),
            };
            Assert.IsTrue(Geometry.NoOverlap(pieces, out _, out _));
            List<Walk> walks = BoundaryTracer.Trace(pieces);
            Assert.AreEqual(1, walks.Count);
            Assert.AreEqual(Scalar.FromInteger(20), walks[0].SignedDoubleArea());
        }

        [Test]
        public void 邊貼合時共線邊要合併()
        {
            var pieces = new[]
            {
                new Placement(PieceKind.SmallTriangle, 0, false, ExactVector2.Zero),
                new Placement(PieceKind.SmallTriangle, 4, false, new ExactVector2(2, 2)),
            };
            List<Walk> walks = BoundaryTracer.Trace(pieces);
            Assert.AreEqual(1, walks.Count);
            Assert.AreEqual(4, walks[0].Edges.Length, "兩個小三角拼成正方形");
        }

        [Test]
        public void 重疊要被抓到()
        {
            var pieces = new[]
            {
                new Placement(PieceKind.SmallTriangle, 0, false, ExactVector2.Zero),
                new Placement(PieceKind.SmallTriangle, 0, false, new ExactVector2(1, 0)),
            };
            Assert.IsFalse(Geometry.NoOverlap(pieces, out _, out _));
        }

        [Test]
        public void Rotate45的偶數Assert還在()
        {
            Assert.Throws<InvalidOperationException>(() => Symmetry.Rotate45(new ExactVector2(1, 0)));
            Assert.DoesNotThrow(() => Symmetry.Rotate45(new ExactVector2(2, 0)));
        }

        [Test]
        public void 翻轉後繞向重新正規化()
        {
            foreach (PieceKind kind in Enum.GetValues(typeof(PieceKind)))
                for (int rotation = 0; rotation < 8; rotation++)
                    foreach (bool flipped in new[] { false, true })
                    {
                        var piece = new Placement(kind, rotation, flipped, ExactVector2.Zero);
                        Assert.AreEqual(1, Geometry.SignedDoubleArea(piece.Vertices()).Sign,
                            $"{kind} rotation={rotation} flipped={flipped} 繞向不是 CCW");
                    }
        }

        [Test]
        public void Json往返()
        {
            Arrangement arrangement = Arrangement.FromPlacements(Square()).WithKeys();
            Arrangement restored = Serialization.ArrangementFromJson(Serialization.ToJson(arrangement));
            Assert.AreEqual(arrangement.Key, restored.Key);
            Assert.AreEqual(arrangement.PuzzleKey, restored.PuzzleKey);
            CollectionAssert.AreEqual(arrangement.Pieces, restored.Pieces);
            CollectionAssert.AreEqual(arrangement.Links, restored.Links);

            Puzzle puzzle = Puzzle.FromPlacements(Square());
            Puzzle restoredPuzzle = Serialization.PuzzleFromJson(Serialization.ToJson(puzzle));
            Assert.AreEqual(puzzle.Key, restoredPuzzle.Key);
            Assert.AreEqual(Canonical.PuzzleKeyFromWalks(puzzle.Walks), Canonical.PuzzleKeyFromWalks(restoredPuzzle.Walks));
        }

        [Test]
        public void Scalar精確比較()
        {
            Assert.IsTrue(new Scalar(2, 0) < new Scalar(0, 2));      // 2 < 2√2
            Assert.IsTrue(new Scalar(3, 0) > new Scalar(0, 2));      // 3 > 2√2
            Assert.IsTrue(new Scalar(-1, 1).Sign > 0);               // √2 − 1 > 0
            Assert.AreEqual(Scalar.FromInteger(8), new Scalar(0, 2) * new Scalar(0, 2));    // (2√2)² = 8
            Assert.AreEqual(Scalar.FromInteger(2), Scalar.Root2.MultiplyByRoot2());               // √2·√2 = 2
            Assert.AreEqual(Scalar.Root2, Scalar.Two.MultiplyByHalfRoot2());              // 2·(√2/2) = √2
        }
    }
}
