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

        /// <summary>
        /// ⭐ <b>三角形的洞建得起來。</b>
        ///
        /// 舊版找內部點的規則是「任兩個頂點的中點」，而三角形沒有對角線——
        /// 任兩個頂點就是一條邊，中點必然落在邊界上，於是<b>每一個三角形的洞都會丟例外</b>。
        /// P2-5 放寬審美門檻去看有洞的形狀時撞到，實測 46 條三角形洞環全掛。
        ///
        /// 現在的規則是「兩條相鄰邊的中點再取中點」，見
        /// <c>Region.FindScaledInteriorPoint</c>。
        /// </summary>
        [Test]
        public void 三角形的洞建得起來()
        {
            Region box = Region.FromRings(new List<ExactVector2[]>
            {
                Ring(0, 0, 8, 0, 8, 8, 0, 8),      // 外環 CCW
                Ring(2, 2, 2, 6, 6, 2)             // 三角形的洞 CW
            });

            Assert.AreEqual(1, CountHoles(box));
            Assert.AreEqual(RegionPosition.Outside, box.Classify(Point(3, 3)), "洞裡應該算外面");
            Assert.AreEqual(RegionPosition.Inside, box.Classify(Point(7, 7)), "洞外的材料應該算裡面");
        }

        /// <summary>
        /// ⭐ <b>洞環有共線的中繼頂點時仍然建得起來。</b>
        ///
        /// 這不是理論上的顧慮：<c>BoundaryTracer</c> 的共線邊只在邊界度數 = 2 時合併，
        /// 捏點附近真的會留下共線的中繼頂點。而找內部點的候選是連續三頂點算出來的，
        /// <b>共線的那一組會產生落在邊界上的退化候選</b>——所以每個候選都必須驗過，
        /// 不能挑第一個就回傳。
        /// </summary>
        [Test]
        public void 洞環有共線中繼頂點時仍然建得起來()
        {
            Region box = Region.FromRings(new List<ExactVector2[]>
            {
                Ring(0, 0, 8, 0, 8, 8, 0, 8),          // 外環 CCW
                Ring(2, 2, 2, 4, 2, 6, 6, 6, 6, 2)     // 洞 CW，左邊那條被 (2,4) 切成兩段
            });

            Assert.AreEqual(1, CountHoles(box));
            Assert.AreEqual(RegionPosition.Outside, box.Classify(Point(4, 4)), "洞裡應該算外面");
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

        /// <summary>
        /// 放大座標系不改變判定。
        ///
        /// ⚠️ 倍數走 <see cref="Region.ScaledCoordinateMultiplier"/>，不要寫死數字——
        /// 這條測試的用途就是釘住「兩套座標系會給同一個答案」，
        /// 自己抄一份倍數的話改倍數時它會安靜地變成驗別的東西。
        /// </summary>
        [Test]
        public void 放大座標系不改變判定()
        {
            Region box = PinchedBox();
            ExactVector2[] samples = Ring(0, 0, -1, -1, 1, 1, 1, -1, -1, 1, 2, 2, 3, 3);

            foreach (ExactVector2 sample in samples)
                Assert.AreEqual(box.Classify(sample),
                                box.ClassifyScaled(sample * Region.ScaledCoordinateMultiplier),
                                sample.ToString());
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
            // 原本這是規則 5（區間中點掃描）唯一擋得住的案例。2026-09-11 加了規則 4 之後，
            // 捏點伸進片裡的那兩段輪廓會先被規則 4 抓到，規則 5 變成第二道——
            // 拿掉規則 5 這條不會紅了。規則 5 自己不可取代的案例改由
            // 只在頂點碰到邊框_整片在凹口裡_不合法 釘住。
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
            // 片剛好等於洞：所有洞頂點都是片的頂點（規則 3 抓不到），
            // 邊全部共線（規則 2 過），區間中點全落在邊界上（規則 5 過）。
            // 2026-09-11 起先被規則 4 擋下（洞環貼著片的邊、外側朝內）；
            // 規則 6（原本的規則 4，洞的內部點）是第二道。
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

        // ---- 規則 4：輪廓的外側不准朝向片內部（2026-09-11）--------------------

        /// <summary>
        /// 3×3 的 2×2 格子挖掉<b>正中央</b>與<b>右上角</b>，手寫成<b>一條自我接觸的環</b>。
        ///
        /// 中央那格是空的，四周都有材料，但它跟右上角那格（空的、對外開放）在 (4,4) 這一點相碰——
        /// 所以它不是洞，是被捏點圍住的空地。環在 (4,4) 經過兩次：
        /// 一次從外框轉進去繞中央空格，一次繞回來繼續走外框。
        ///
        /// ⚠️ 刻意手寫而不是用 <c>BoundaryTracer</c> 追出來：這裡驗的是
        /// <b>「一條環、中間圍著空地」這種輸入</b>，不管將來追邊改成怎麼描它。
        /// </summary>
        /// <returns>面積 28 的區域，一條環，沒有洞環。</returns>
        private static Region PinchEnclosedBox() => Region.FromPolygon(Ring(
            0, 0, 6, 0, 6, 4,
            4, 4, 4, 2, 2, 2, 2, 4,    // 從捏點轉進去，順時針繞中央空格
            4, 4, 4, 6, 0, 6));        // 回到捏點，繼續走外框

        /// <summary>
        /// ⭐ <b>被捏點圍住的空地，放進剛好一樣大的片，不合法。</b>
        ///
        /// 2026-09-11 以前這裡會回 true：片的四個角都在邊框上（規則 1 過）、
        /// 四條邊全貼著輪廓（規則 2、3、5 問到的點都落在邊框上）、
        /// 而規則 6 只認得洞環，這塊空地是外環的一部分。
        /// 使用者在實機上看到的就是這種形狀（空地剛好是小三角的大小）。
        ///
        /// 兩種繞向都要擋：規則 4 用片的繞向判斷「片的內部在片邊的哪一側」，寫反的話只會擋一種。
        /// </summary>
        [Test]
        public void 被捏點圍住的空地_放進剛好一樣大的片_不合法()
        {
            Region box = PinchEnclosedBox();
            Assert.AreEqual(1, box.Rings.Length, "前提：一條環");
            Assert.AreEqual(0, CountHoles(box), "前提：沒有洞環，規則 6 管不到");
            Assert.AreEqual(RegionPosition.Outside, box.Classify(Point(3, 3)), "前提：區域自己知道中央是外面");

            Assert.IsFalse(box.ContainsPolygon(Ring(2, 2, 4, 2, 4, 4, 2, 4)), "逆時針的片");
            Assert.IsFalse(box.ContainsPolygon(Ring(2, 2, 2, 4, 4, 4, 4, 2)), "順時針的片");
            Assert.IsFalse(box.Contains(new Placement(PieceKind.Square, 0, false, Point(2, 2))));
        }

        /// <summary>
        /// 同一個盒子上，<b>貼著捏點</b>但確實在材料裡的片仍然合法。
        /// 規則 4 不能因為片碰到捏點、或邊貼著輪廓就誤殺。
        /// </summary>
        [Test]
        public void 被捏點圍住的空地旁邊_貼著捏點的片_合法()
        {
            Region box = PinchEnclosedBox();

            Assert.IsTrue(box.ContainsPolygon(Ring(2, 4, 4, 4, 4, 6, 2, 6)), "中央正上方那格，右下角就是捏點");
            Assert.IsTrue(box.ContainsPolygon(Ring(4, 2, 6, 2, 6, 4, 4, 4)), "中央正右方那格，左上角就是捏點");
            Assert.IsTrue(box.ContainsPolygon(Ring(0, 0, 2, 0, 2, 2, 0, 2)), "左下角那格，兩條邊貼外框");
        }

        /// <summary>
        /// 片的邊貼著外框放，<b>兩種繞向都合法</b>。
        ///
        /// 規則 4 在「輪廓段貼著片的邊」時要判斷方向：同向或反向，再配上片的繞向。
        /// 這條釘住判斷沒有寫反——寫反的話，每一片貼著外框放的片都會被擋掉。
        /// </summary>
        [Test]
        public void 片的邊貼著外框_兩種繞向都合法()
        {
            Region box = Box();
            Assert.IsTrue(box.ContainsPolygon(Ring(0, 0, 2, 0, 2, 2, 0, 2)), "逆時針的片");
            Assert.IsTrue(box.ContainsPolygon(Ring(0, 0, 0, 2, 2, 2, 2, 0)), "順時針的片");
            Assert.IsTrue(box.ContainsPolygon(Ring(0, 0, 4, 0, 4, 4, 0, 4)), "片跟盒子一模一樣大");
        }

        /// <summary>
        /// ⭐ <b>中點三角形形狀的洞，上面蓋一片大三角，不合法。</b>
        ///
        /// 這條存在的目的是<b>擋住一個看起來很合理的簡化</b>：
        /// 「規則 1、2、3、5 都過了之後，片的內部不是整塊在裡面就是整塊在外面，問一個內部點就好」。
        /// 那句話是錯的——洞的三條邊，兩端都剛好停在大三角的三條邊上（不算真交叉），
        /// 卻橫過片的內部，把它切成裡外兩部分。用 (a+2b+c)/4 取的三個內部點，
        /// 全部落在洞的邊上（下面的前提斷言）。
        ///
        /// 現在規則 4（有輪廓段橫過片的內部）與規則 6（洞的內部點在片內）都擋得住它。
        /// </summary>
        [Test]
        public void 中點三角形的洞被大三角蓋住_不合法()
        {
            Region box = Region.FromRings(new List<ExactVector2[]>
            {
                Ring(-2, -2, 8, -2, 8, 8, -2, 8),    // 外環 CCW
                Ring(2, 0, 0, 2, 2, 2)               // 洞：大三角三邊的中點，CW
            });
            ExactVector2[] largeTriangle = Ring(0, 0, 4, 0, 0, 4);

            // 前提：「問一個內部點」會拿到的三個候選，全落在洞的邊上
            Assert.AreEqual(RegionPosition.OnBoundary, box.Classify(Point(1, 1)));
            Assert.AreEqual(RegionPosition.OnBoundary, box.Classify(Point(2, 1)));
            Assert.AreEqual(RegionPosition.OnBoundary, box.Classify(Point(1, 2)));

            Assert.IsFalse(box.ContainsPolygon(largeTriangle));
        }

        /// <summary>
        /// ⭐ <b>片整個在凹口裡、只有頂點碰到邊框，不合法——規則 5 唯一擋得住的案例。</b>
        ///
        /// 三個頂點分別碰在ㄈ字凹口的內壁 (2,3)、下臂 (3,2)、上臂 (3,4)，三條邊全從凹口的空地穿過：
        /// <list type="bullet">
        /// <item>規則 1 過：頂點都在邊框上</item>
        /// <item>規則 2 過：片邊只在端點碰到輪廓，不算真交叉</item>
        /// <item>規則 3 過：沒有輪廓頂點在片裡</item>
        /// <item>規則 4 過：沒有輪廓段在片裡、也沒有貼著片的邊（輪廓在片的頂點處切開後，每段的中點都在片外）</item>
        /// </list>
        /// 只有規則 5 會發現片邊的中點在凹口裡。
        ///
        /// ⚠️ 2026-09-11 加規則 4 之後，原本守規則 5 的 <c>跨過捏點的片_不合法</c> 也會被規則 4 擋下，
        /// 變異測試（拿掉規則 5）發現<b>整個測試集沒有任何一條會紅</b>。這條就是補那個洞的。
        /// </summary>
        [Test]
        public void 只在頂點碰到邊框_整片在凹口裡_不合法()
        {
            Region box = CShapedBox();
            ExactVector2[] piece = Ring(2, 3, 3, 2, 3, 4);

            foreach (ExactVector2 vertex in piece)
                Assert.AreEqual(RegionPosition.OnBoundary, box.Classify(vertex), "前提：頂點都在邊框上");
            Assert.AreEqual(RegionPosition.Outside, box.Classify(Point(3, 3)), "前提：片的邊的中點在凹口裡");

            Assert.IsFalse(box.ContainsPolygon(piece));
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
