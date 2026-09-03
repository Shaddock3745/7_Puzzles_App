using System;
using System.Collections.Generic;
using NUnit.Framework;
using Tangram.Data;

namespace Tangram.Data.Tests
{
    /// <summary>
    /// P1-4：區域判定。Geometry 的基本判斷式 + Region 的五條包含規則。
    /// 每個「不合法」的案例都標了它是被哪一條規則擋下來的 —— 拿掉某條規則時，
    /// 對應的測試就會炸給你看。
    /// </summary>
    public class RegionTests
    {
        private static ExactVector2 Point(long x, long y) => new ExactVector2(x, y);

        /// <summary>攤平的座標對 → 一條環。</summary>
        private static ExactVector2[] Ring(params long[] coordinates)
        {
            var ring = new ExactVector2[coordinates.Length / 2];
            for (int index = 0; index < ring.Length; index++)
                ring[index] = new ExactVector2(coordinates[index * 2], coordinates[index * 2 + 1]);
            return ring;
        }

        /// <summary>[0,4] × [0,4]。</summary>
        private static Region Box() => Region.FromPolygon(Ring(0, 0, 4, 0, 4, 4, 0, 4));

        /// <summary>[0,6]² 中間挖掉 [2,4]²。</summary>
        private static Region BoxWithHole() => Region.FromRings(new List<ExactVector2[]>
        {
            Ring(0, 0, 6, 0, 6, 6, 0, 6),      // 外環 CCW
            Ring(2, 2, 2, 4, 4, 4, 4, 2)       // 洞 CW
        });

        /// <summary>
        /// 兩塊 2×2 只在原點捏在一起。一條環，捏點 (0,0) 在環裡出現兩次
        /// —— BoundaryTracer 產出的形狀就長這樣。
        /// </summary>
        private static Region PinchedBox() => Region.FromPolygon(
            Ring(-2, -2, 0, -2, 0, 0, 2, 0, 2, 2, 0, 2, 0, 0, -2, 0));

        /// <summary>ㄈ 字形，開口朝右，凹口是 [2,6] × [2,4]。</summary>
        private static Region CShapedBox() => Region.FromPolygon(
            Ring(0, 0, 6, 0, 6, 2, 2, 2, 2, 4, 6, 4, 6, 6, 0, 6));

        // ---- Geometry 的基本判斷式 ----------------------------------------

        [Test]
        public void 線段上的點_含端點()
        {
            Assert.IsTrue(Geometry.IsOnSegment(Point(0, 0), Point(4, 0), Point(2, 0)));
            Assert.IsTrue(Geometry.IsOnSegment(Point(0, 0), Point(4, 0), Point(0, 0)));   // 端點算
            Assert.IsTrue(Geometry.IsOnSegment(Point(0, 0), Point(4, 0), Point(4, 0)));
            Assert.IsFalse(Geometry.IsOnSegment(Point(0, 0), Point(4, 0), Point(5, 0)));  // 延長線上不算
            Assert.IsFalse(Geometry.IsOnSegment(Point(0, 0), Point(4, 0), Point(2, 1)));
        }

        [Test]
        public void 真交叉_端點接觸與共線重疊都不算()
        {
            // X 形，交點在兩條線段內部
            Assert.IsTrue(Geometry.SegmentsProperlyCross(
                Point(0, 0), Point(2, 2), Point(0, 2), Point(2, 0)));

            // 共用端點
            Assert.IsFalse(Geometry.SegmentsProperlyCross(
                Point(0, 0), Point(2, 2), Point(0, 0), Point(2, 0)));

            // T 字：端點落在對方的內部
            Assert.IsFalse(Geometry.SegmentsProperlyCross(
                Point(0, 0), Point(4, 0), Point(2, 0), Point(2, 2)));

            // 共線重疊
            Assert.IsFalse(Geometry.SegmentsProperlyCross(
                Point(0, 0), Point(4, 0), Point(2, 0), Point(6, 0)));

            // 共線分離
            Assert.IsFalse(Geometry.SegmentsProperlyCross(
                Point(0, 0), Point(2, 0), Point(4, 0), Point(6, 0)));

            // 平行
            Assert.IsFalse(Geometry.SegmentsProperlyCross(
                Point(0, 0), Point(4, 0), Point(0, 1), Point(4, 1)));
        }

        [Test]
        public void 嚴格在凸多邊形內_邊上不算_繞向不拘()
        {
            ExactVector2[] counterClockwise = Ring(0, 0, 4, 0, 4, 4, 0, 4);
            ExactVector2[] clockwise = Ring(0, 0, 0, 4, 4, 4, 4, 0);

            Assert.IsTrue(Geometry.StrictlyInsideConvexPolygon(counterClockwise, Point(2, 2)));
            Assert.IsTrue(Geometry.StrictlyInsideConvexPolygon(clockwise, Point(2, 2)));
            Assert.IsFalse(Geometry.StrictlyInsideConvexPolygon(counterClockwise, Point(0, 2)));  // 邊上
            Assert.IsFalse(Geometry.StrictlyInsideConvexPolygon(counterClockwise, Point(0, 0)));  // 頂點
            Assert.IsFalse(Geometry.StrictlyInsideConvexPolygon(counterClockwise, Point(5, 2)));

            // polygonScale：把多邊形就地放大兩倍，等於在 ×2 座標系裡比對
            Assert.IsTrue(Geometry.StrictlyInsideConvexPolygon(counterClockwise, Point(5, 5), 2));
            Assert.IsFalse(Geometry.StrictlyInsideConvexPolygon(counterClockwise, Point(5, 5)));
        }

        // ---- 點的分類 ------------------------------------------------------

        [Test]
        public void 點在區域的內部_外部_邊上_頂點上()
        {
            Region box = Box();
            Assert.AreEqual(RegionPosition.Inside, box.Classify(Point(2, 2)));
            Assert.AreEqual(RegionPosition.Outside, box.Classify(Point(5, 2)));
            Assert.AreEqual(RegionPosition.Outside, box.Classify(Point(-1, 2)));
            Assert.AreEqual(RegionPosition.OnBoundary, box.Classify(Point(0, 2)));   // 邊上
            Assert.AreEqual(RegionPosition.OnBoundary, box.Classify(Point(4, 4)));   // 頂點
            Assert.IsTrue(box.Contains(Point(0, 2)));                                // Contains 含邊界
        }

        [Test]
        public void 洞內的點算在區域外_洞的邊上算邊界()
        {
            Region box = BoxWithHole();
            Assert.AreEqual(RegionPosition.Inside, box.Classify(Point(1, 1)));
            Assert.AreEqual(RegionPosition.Outside, box.Classify(Point(3, 3)));      // 洞裡
            Assert.AreEqual(RegionPosition.OnBoundary, box.Classify(Point(2, 3)));   // 洞的邊
            Assert.AreEqual(RegionPosition.OnBoundary, box.Classify(Point(2, 2)));   // 洞的頂點
            Assert.AreEqual(1, CountHoles(box));
        }

        [Test]
        public void 射線正好通過頂點時不會誤判()
        {
            // 菱形，(0,2) 與 (4,2) 兩個頂點都正好落在 y = 2 的水平射線上
            Region diamond = Region.FromPolygon(Ring(2, 0, 4, 2, 2, 4, 0, 2));

            Assert.AreEqual(RegionPosition.Outside, diamond.Classify(Point(-1, 2)));  // 左邊，射線穿過兩個頂點
            Assert.AreEqual(RegionPosition.Inside, diamond.Classify(Point(2, 2)));    // 正中央
            Assert.AreEqual(RegionPosition.Outside, diamond.Classify(Point(5, 2)));   // 右邊
            Assert.AreEqual(RegionPosition.Outside, diamond.Classify(Point(0, 0)));   // 角落外側
        }

        [Test]
        public void 捏點本身是邊界_兩側楔形在內_對角楔形在外()
        {
            Region box = PinchedBox();
            Assert.AreEqual(RegionPosition.OnBoundary, box.Classify(Point(0, 0)));   // 捏點
            Assert.AreEqual(RegionPosition.Inside, box.Classify(Point(-1, -1)));
            Assert.AreEqual(RegionPosition.Inside, box.Classify(Point(1, 1)));
            Assert.AreEqual(RegionPosition.Outside, box.Classify(Point(1, -1)));
            Assert.AreEqual(RegionPosition.Outside, box.Classify(Point(-1, 1)));
        }

        [Test]
        public void 座標含根號二也精確判定()
        {
            ExactVector2 zero = ExactVector2.Zero;
            ExactVector2 corner = new ExactVector2(Scalar.FromRoot2Multiple(2), Scalar.Zero);
            ExactVector2 opposite = new ExactVector2(Scalar.FromRoot2Multiple(2), Scalar.FromRoot2Multiple(2));
            ExactVector2 top = new ExactVector2(Scalar.Zero, Scalar.FromRoot2Multiple(2));
            Region box = Region.FromPolygon(new[] { zero, corner, opposite, top });

            var center = new ExactVector2(Scalar.Root2, Scalar.Root2);
            var onEdge = new ExactVector2(Scalar.Zero, Scalar.Root2);
            var outside = new ExactVector2(Scalar.FromRoot2Multiple(3), Scalar.Root2);

            Assert.AreEqual(RegionPosition.Inside, box.Classify(center));
            Assert.AreEqual(RegionPosition.OnBoundary, box.Classify(onEdge));
            Assert.AreEqual(RegionPosition.Outside, box.Classify(outside));

            // 2 與 √2 不能比錯：√2 < 2 所以 (2, √2) 還在盒內
            Assert.AreEqual(RegionPosition.Inside, box.Classify(new ExactVector2(Scalar.Two, Scalar.Root2)));
        }

        [Test]
        public void 乘二座標系不改變判定()
        {
            Region box = PinchedBox();
            ExactVector2[] samples = Ring(0, 0, -1, -1, 1, 1, 1, -1, -1, 1, 2, 2, 3, 3);

            foreach (ExactVector2 sample in samples)
                Assert.AreEqual(box.Classify(sample), box.ClassifyDoubled(sample * 2), sample.ToString());
        }

        // ---- 多邊形包含 ----------------------------------------------------

        [Test]
        public void 片在盒內_含貼邊與貼角()
        {
            Region box = Box();
            Assert.IsTrue(box.ContainsPolygon(Ring(1, 1, 2, 1, 2, 2, 1, 2)));   // 完全在內部
            Assert.IsTrue(box.ContainsPolygon(Ring(0, 0, 2, 0, 2, 2, 0, 2)));   // 貼著角落
            Assert.IsTrue(box.ContainsPolygon(Ring(0, 0, 4, 0, 4, 4, 0, 4)));   // 剛好等於盒子
        }

        [Test]
        public void 片超出盒子一點點就不合法()
        {
            Region box = Box();
            Assert.IsFalse(box.ContainsPolygon(Ring(3, 3, 5, 3, 5, 5, 3, 5)));  // 規則 0：包圍盒
            Assert.IsFalse(box.ContainsPolygon(Ring(5, 5, 6, 5, 6, 6, 5, 6)));  // 整片在外面
        }

        [Test]
        public void 頂點都在區域內但邊穿過凹口_不合法()
        {
            // 規則 2 擋下來的。三個頂點分別在下臂、上臂、脊椎裡，
            // 但 (5,1)→(5,5) 這條邊整段穿過開口。
            Region box = CShapedBox();
            ExactVector2[] piece = Ring(5, 1, 5, 5, 1, 3);

            foreach (ExactVector2 vertex in piece)
                Assert.AreNotEqual(RegionPosition.Outside, box.Classify(vertex), "頂點應該都在區域內");

            Assert.IsFalse(box.ContainsPolygon(piece));
        }

        [Test]
        public void 跨過捏點的片_不合法()
        {
            // 這是規則 5（區間中點掃描）唯一擋得住的案例，拿掉它這個測試就會紅。
            //
            // 片是真的中三角：股 2√2、斜邊 4、面積 4。三個頂點分別是
            // 左上塊的左上角、左上塊的右下角、右上塊的右下角 —— 全部在區域邊界上。
            Region box = PinchedBox();
            var mediumTriangle = new Placement(PieceKind.MediumTriangle, 0, false, Point(0, -2));
            ExactVector2[] piece = mediumTriangle.Vertices();

            // 規則 1 過：三個頂點都在區域內（都在邊界上）
            foreach (ExactVector2 vertex in piece)
                Assert.AreNotEqual(RegionPosition.Outside, box.Classify(vertex), vertex.ToString());

            // 規則 2 過：所有接觸都發生在端點或共線處，沒有真交叉
            for (int pieceIndex = 0; pieceIndex < piece.Length; pieceIndex++)
            {
                ExactVector2 pieceStart = piece[pieceIndex];
                ExactVector2 pieceEnd = piece[(pieceIndex + 1) % piece.Length];
                ExactVector2[] ring = box.Rings[0];

                for (int ringIndex = 0; ringIndex < ring.Length; ringIndex++)
                    Assert.IsFalse(Geometry.SegmentsProperlyCross(
                        pieceStart, pieceEnd, ring[ringIndex], ring[(ringIndex + 1) % ring.Length]),
                        "不應該有真交叉");
            }

            // 規則 3 過：捏點落在片的邊上，不是嚴格內部
            foreach (ExactVector2 vertex in box.Vertices)
                Assert.IsFalse(Geometry.StrictlyInsideConvexPolygon(piece, vertex), vertex.ToString());

            // 但片的內部確實戳到了區域外
            Assert.AreEqual(RegionPosition.Outside, box.Classify(Point(1, -1)));
            Assert.IsFalse(box.ContainsPolygon(piece), "跨過捏點的片必須擋下來");
            Assert.IsFalse(box.Contains(mediumTriangle));
        }

        [Test]
        public void 填進捏點一側的片_合法()
        {
            Region box = PinchedBox();
            var smallTriangle = new Placement(PieceKind.SmallTriangle, 0, false, Point(0, 0));

            Assert.IsTrue(box.Contains(smallTriangle), "碰到捏點是合法的，不能被規則 5 誤殺");
            Assert.IsTrue(box.ContainsPolygon(Ring(-2, -2, 0, -2, 0, 0, -2, 0)));
        }

        [Test]
        public void 片整個蓋住一個洞_不合法()
        {
            // 規則 4。片剛好等於洞：所有洞頂點都是片的頂點（規則 3 過不了關），
            // 邊全部共線（規則 2 過），區間中點全落在邊界上（規則 5 過）。
            Region box = BoxWithHole();
            ExactVector2[] exactlyTheHole = Ring(2, 2, 4, 2, 4, 4, 2, 4);

            foreach (ExactVector2 vertex in box.Vertices)
                Assert.IsFalse(Geometry.StrictlyInsideConvexPolygon(exactlyTheHole, vertex),
                    "沒有任何輪廓頂點嚴格落在片內部，所以規則 3 抓不到");

            Assert.IsFalse(box.ContainsPolygon(exactlyTheHole), "片蓋住洞必須擋下來");

            // 蓋住洞又超出去的，規則 3 就會先抓到
            Assert.IsFalse(box.ContainsPolygon(Ring(1, 1, 5, 1, 5, 5, 1, 5)));
        }

        [Test]
        public void 片貼著洞的邊緣但沒蓋住_合法()
        {
            // 這是「用洞的頂點去測」會誤殺的案例：兩個洞頂點就在片的右邊上。
            Region box = BoxWithHole();
            Assert.IsTrue(box.ContainsPolygon(Ring(0, 2, 2, 2, 2, 4, 0, 4)));
        }

        // ---- 接上真的題目 --------------------------------------------------

        [Test]
        public void 經典大正方形_七片都判定在盒內()
        {
            Placement[] pieces = Fixtures.ClassicSquare();
            Puzzle puzzle = Puzzle.FromPlacements(pieces);
            Region box = Region.FromPuzzle(puzzle, ExactVector2.Zero);

            foreach (Placement piece in pieces)
                Assert.IsTrue(box.Contains(piece), piece.ToString());
        }

        [Test]
        public void 盒子平移後判定跟著平移()
        {
            Placement[] pieces = Fixtures.ClassicSquare();
            Puzzle puzzle = Puzzle.FromPlacements(pieces);
            var boxOrigin = new ExactVector2(new Scalar(100, 6), new Scalar(-40, 3));
            Region box = Region.FromPuzzle(puzzle, boxOrigin);

            foreach (Placement piece in pieces)
            {
                Assert.IsTrue(box.Contains(piece.Translated(boxOrigin)), piece.ToString());
                Assert.IsFalse(box.Contains(piece), "沒跟著平移的片應該落在盒外");
            }
        }

        [Test]
        public void 片超出盒子邊界就不合法()
        {
            Placement[] pieces = Fixtures.ClassicSquare();
            Region box = Region.FromPuzzle(Puzzle.FromPlacements(pieces), ExactVector2.Zero);

            // 整批搬到盒子外面
            foreach (Placement piece in pieces)
                Assert.IsFalse(box.Contains(piece.Translated(Point(10, 0))), piece.Kind.ToString());

            // 只凸出去一點點也擋得住：原本貼著右下角的大三角往右移一格
            Assert.IsFalse(box.Contains(pieces[0].Translated(Point(1, 0))));

            // 但沒碰到右邊界的片往右移一格仍然「在盒內」。
            // 包含判定只回答「有沒有跑出盒子」，不回答「放對了沒有」
            // —— 對不對是重疊檢查與過關判定的事。
            Assert.IsTrue(box.Contains(pieces[1].Translated(Point(1, 0))));
        }

        [Test]
        public void 題目的Region是快取的()
        {
            Puzzle puzzle = Puzzle.FromPlacements(Fixtures.ClassicSquare());
            Assert.AreSame(puzzle.Region, puzzle.Region);
            Assert.IsTrue(puzzle.Region.Contains(Fixtures.ClassicSquare()[0]));
        }

        [Test]
        public void 包圍盒()
        {
            Region box = Box();
            Assert.AreEqual(Point(0, 0), box.Bounds.Minimum);
            Assert.AreEqual(Point(4, 4), box.Bounds.Maximum);
            Assert.IsTrue(box.Bounds.Contains(Point(4, 0)));          // 含邊界
            Assert.IsFalse(box.Bounds.Contains(Point(5, 0)));
            Assert.IsTrue(box.Bounds.Contains(BoundingBox.FromPoints(Ring(1, 1, 2, 2))));
            Assert.IsFalse(box.Bounds.Contains(BoundingBox.FromPoints(Ring(1, 1, 5, 2))));
        }

        private static int CountHoles(Region region)
        {
            int holeCount = 0;
            foreach (bool isOuter in region.RingIsOuter) if (!isOuter) holeCount++;
            return holeCount;
        }
    }
}
