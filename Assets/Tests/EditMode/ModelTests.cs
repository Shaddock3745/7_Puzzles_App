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

        // ---- 捏點：環的組成必須唯一（實際撞到的 bug）------------------------

        /// <param name="x">左下角的 x。</param>
        /// <param name="y">左下角的 y。</param>
        /// <returns>擺在該處、不轉不翻的正方形片（邊長 2）。</returns>
        private static Placement SquareAt(long x, long y)
            => new Placement(PieceKind.Square, 0, false, new ExactVector2(x, y));

        /// <summary>
        /// 3×3 的正方形格子，挖掉<b>正中央</b>與<b>右上角</b>兩格。
        ///
        /// 中央那格是空的、四周有材料，但它跟右上角那格（也是空的、而且對外開放）
        /// <b>在 (4,4) 這一點相碰</b>。點沒有面積，所以中央那格仍然是關起來的洞，
        /// 而洞的邊界與外框的邊界在那一點接上了。
        ///
        /// ⚠️ <b>這是實際撞到的那個形狀的最小版本。</b>材料的內部仍然連通
        /// （可以從中央那格上面繞過去），所以拓樸上正確的答案是
        /// <b>一條會自己碰到自己的環</b>，不是外環加洞環兩條。
        /// </summary>
        /// <returns>七個正方形片。刻意不是七巧板的那七片——這裡要驗的是追邊，不是題目。</returns>
        private static Placement[] PinchedHoleFixture() => new[]
        {
            // ⚠️ 第一片刻意用旋轉的方式擺，讓它的第 0 個頂點正好落在捏點 (4,4)。
            // 它佔的位置跟 SquareAt(2, 4) 完全一樣，差別只在頂點從哪個角開始數。
            //
            // 為什麼要這樣：追環是從**索引最小的未使用邊**起步的，而那條邊是第一片的第一條邊。
            // 捏點如果不是某一片的第 0 個頂點，追環就永遠不會「從捏點起步」——
            // 而那正是舊寫法唯一會誤判的路徑。用 SquareAt(2, 4) 的話捏點是它的右下角，
            // 這個 fixture 就會變成綠的但什麼都沒驗到（第一版就是這樣，變異測試抓出來的）
            new Placement(PieceKind.Square, 2, false, new ExactVector2(4, 4)),

            SquareAt(0, 0), SquareAt(2, 0), SquareAt(4, 0),
            SquareAt(0, 2),                 SquareAt(4, 2),
            SquareAt(0, 4)
        };

        /// <param name="walks">追出來的環。</param>
        /// <returns>
        /// 環的「內容指紋」：條數加上每條的有向面積（排序過）。
        /// <b>刻意不含座標與順序</b>——順序本來就不保證，而旋轉鏡射會改座標卻不該改內容。
        /// </returns>
        private static string RingSignature(List<Walk> walks)
        {
            var areas = new List<Scalar>(walks.Count);
            foreach (Walk walk in walks) areas.Add(walk.SignedDoubleArea());
            areas.Sort();

            var text = new System.Text.StringBuilder();
            text.Append(walks.Count).Append(':');
            foreach (Scalar area in areas) text.Append(area).Append(',');
            return text.ToString();
        }

        /// <summary>
        /// ⭐ <b>洞的尖端頂到外框時，答案是一條自我接觸的環。</b>
        ///
        /// 判準是拓樸而不是喜好：材料的內部有幾塊連通區域就有幾條環。
        /// 這個形狀的材料內部是一塊（可以從中央那格上面繞過去）→ 一條環。
        /// 對照組是上面那條「兩個小三角只靠一角相碰」——那裡內部真的是兩塊 → 兩條環。
        /// </summary>
        [Test]
        public void 捏點_洞頂到外框時描出一條自我接觸的環()
        {
            Placement[] pieces = PinchedHoleFixture();
            Assert.IsTrue(Geometry.NoOverlap(pieces, out _, out _));

            List<Walk> walks = BoundaryTracer.Trace(pieces);

            Assert.AreEqual(1, walks.Count, "材料內部是連通的一塊，應該只有一條環");
            Assert.AreEqual(Scalar.FromInteger(56), walks[0].SignedDoubleArea(),
                "七個邊長 2 的正方形，面積 ×2 應該是 56");
        }

        /// <summary>
        /// ⭐ <b>環的組成不受片的枚舉順序影響。</b>
        ///
        /// ⚠️ 這條抓的是「答案不唯一」，那種 bug 不會自己現形——
        /// 原本的追邊規則在捏點上會因為先追哪一圈而描出 1 條或 2 條環，
        /// 而每一次單獨看都像是合理的輸出。實測同一個形狀洗牌 200 次會拿到兩種答案。
        ///
        /// 順序本身仍然不保證（見 <c>BoundaryTracer.Trace</c> 的說明），所以比的是內容指紋。
        /// </summary>
        [Test]
        public void 捏點_環的組成不受片的枚舉順序影響()
        {
            Placement[] pieces = PinchedHoleFixture();
            string expected = RingSignature(BoundaryTracer.Trace(pieces));

            var random = new Random(20260910);
            for (int trial = 0; trial < 200; trial++)
            {
                var shuffled = new List<Placement>(pieces);
                for (int index = shuffled.Count - 1; index > 0; index--)
                {
                    int swapWith = random.Next(index + 1);
                    Placement held = shuffled[index];
                    shuffled[index] = shuffled[swapWith];
                    shuffled[swapWith] = held;
                }

                Assert.AreEqual(expected, RingSignature(BoundaryTracer.Trace(shuffled)),
                    $"第 {trial} 次洗牌描出了不一樣的環");
            }
        }

        /// <summary>
        /// ⭐ <b>環的組成對十六種整體變換一致。</b>
        ///
        /// ⚠️ <b>鏡射是重點</b>：原本的規則講的是「最大左轉」，而鏡射會把左右對調，
        /// 所以捏點上的分岔會倒過來選。症狀是同一個形狀在不同姿態下洞數不一樣，
        /// 連帶難度值也不一樣——而 ELO 要求同一題永遠得到同一個難度值。
        ///
        /// 💡 <c>DifficultyFeatureTests</c> 那條同名的不變性測試驗的是<b>特徵</b>，
        /// 這條驗的是它上游的<b>拓樸</b>。上游壞掉的話下游那條也會紅，
        /// 但先在這裡紅比較好查。
        /// </summary>
        [Test]
        public void 捏點_環的組成對十六種整體變換一致()
        {
            Placement[] pieces = PinchedHoleFixture();
            string expected = RingSignature(BoundaryTracer.Trace(pieces));

            foreach ((int rotation, bool flipped) in Symmetry.GlobalTransforms)
            {
                var transformed = new List<Placement>(pieces.Length);
                foreach (Placement piece in pieces)
                    transformed.Add(piece.ApplyGlobal(rotation, flipped));

                Assert.AreEqual(expected, RingSignature(BoundaryTracer.Trace(transformed)),
                    $"旋轉 {rotation}、翻面 {flipped} 之後描出了不一樣的環");
            }
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
