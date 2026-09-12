using System;
using System.Collections.Generic;
using NUnit.Framework;
using Tangram.Data;

namespace Tangram.Data.Tests
{
    /// <summary>
    /// 難度特徵函數（P2-2，issue #23）。
    ///
    /// <b>最重要的是〈對十六種整體變換全部不變〉那一條</b>——它驗的不是某個函數對不對，
    /// 而是 <b>ELO 的前提成不成立</b>：同一題必須永遠得到同一個難度值，
    /// 而 <see cref="Canonical"/> 把互為旋轉／鏡射的題目當成同一題。
    /// </summary>
    public class DifficultyFeatureTests
    {
        private static Puzzle ClassicSquarePuzzle() => Puzzle.FromPlacements(Fixtures.ClassicSquare());

        private static Edge Straight(int direction, long length)
            => new Edge(direction, Scalar.FromInteger(length));

        // ─────────────────────────────────────────────────────────────
        // 一、⭐ ELO 的前提
        // ─────────────────────────────────────────────────────────────

        /// <summary>
        /// ⭐ <b>四個數字對 16 種整體變換全部不變。</b>
        ///
        /// <c>Canonical.PuzzleKey</c> 對 16 種變換不變，也就是說<b>互為旋轉／鏡射的題目
        /// 是同一題</b>，題庫裡只會存其中一個代表。如果特徵會隨代表不同而改變，
        /// 同一題就會拿到不同的難度值，ELO 直接失去意義。
        ///
        /// 這條同時驗掉三件事：
        /// <list type="bullet">
        /// <item>凹角的繞向處理對不對（鏡射會反轉繞向）</item>
        /// <item>「相異方向的數量」真的是不變量——旋轉是 <c>d → d+1</c>、
        ///       鏡射是 <c>d → −d</c>，兩個都是 {0..7} 上的一對一對應，所以個數不變</item>
        /// <item><c>Canonical.TransformWalk</c> 的鏡射分支有沒有把頂點順序反轉回來</item>
        /// </list>
        ///
        /// ⚠️ <b>一定要有不對稱的形狀。</b>古典大正方形對 16 種變換的結果幾何上完全相同，
        /// 光用它的話這條測試幾乎驗不到東西——鏡射整個寫錯也會過。
        /// 所以另外加一個 L 形：它經過鏡射會變成另一個 L，輪廓資料真的不一樣，
        /// 但五個數字必須一樣。
        /// </summary>
        [TestCaseSource(nameof(ShapesForInvarianceCheck))]
        public void 特徵_對十六種整體變換全部不變(string shapeName, Walk[] walks)
        {
            DifficultyFeatures original = DifficultyFeatures.Measure(walks);

            foreach ((int rotation, bool flipped) in Symmetry.GlobalTransforms)
            {
                var transformed = new Walk[walks.Length];
                for (int walkIndex = 0; walkIndex < walks.Length; walkIndex++)
                    transformed[walkIndex] =
                        Canonical.TransformWalk(walks[walkIndex], rotation, flipped);

                DifficultyFeatures actual = DifficultyFeatures.Measure(transformed);
                string label = $"{shapeName} rotation={rotation} flipped={flipped}";

                Assert.AreEqual(original.ConcaveAngleCount, actual.ConcaveAngleCount, $"{label} 凹角數");
                Assert.AreEqual(original.DirectionVarietyCount, actual.DirectionVarietyCount, $"{label} 角度種類");
                Assert.AreEqual(original.DoubledHiddenEdgeLength, actual.DoubledHiddenEdgeLength, $"{label} 藏起來的長度");
                Assert.AreEqual(original.PinchPointCount, actual.PinchPointCount, $"{label} 捏點數");
                Assert.AreEqual(original.HoleCount, actual.HoleCount, $"{label} 洞數");

                // 2026-09-12 加的三個（P2-6）。凸包與逼出來的片數都跟座標有關，
                // 是這條測試裡最可能被鏡射寫錯咬到的
                Assert.AreEqual(original.CornerCount, actual.CornerCount, $"{label} 轉角數");
                Assert.AreEqual(original.ConvexHullDoubleArea, actual.ConvexHullDoubleArea, $"{label} 凸包面積");
                Assert.AreEqual(DifficultyFeatures.ForcedPieceCountOf(walks),
                                DifficultyFeatures.ForcedPieceCountOf(transformed), $"{label} 被逼出來的片數");
            }
        }

        /// <summary>
        /// 給不變性檢查用的形狀。
        ///
        /// <b>L 形</b>是 8×6 的長方形缺一角 4×4，面積 48 − 16 = 32，剛好過守衛。
        /// 它只有一個凹角，而且<b>不是鏡射對稱的</b>——所以繞向如果沒被正規化回來，
        /// 凹角數會從 1 變成 5（凹凸互換），洞數也會從 0 變成 1（有向面積變負）。
        /// </summary>
        private static IEnumerable<TestCaseData> ShapesForInvarianceCheck()
        {
            yield return new TestCaseData("古典大正方形", ClassicSquarePuzzle().Walks)
                .SetName("特徵_對十六種整體變換全部不變_古典大正方形");

            var lShape = new[]
            {
                new Walk(new ExactVector2(0, 0), new[]
                {
                    Straight(0, 8), Straight(2, 2), Straight(4, 4),
                    Straight(2, 4), Straight(4, 4), Straight(6, 6)
                })
            };
            yield return new TestCaseData("L 形", lShape)
                .SetName("特徵_對十六種整體變換全部不變_不對稱的L形");
        }

        /// <summary>L 形的五個數字手算對一次，順便確認上面那個形狀真的只有一個凹角。</summary>
        [Test]
        public void L形_五個數字都對得上()
        {
            var lShape = new Walk(new ExactVector2(0, 0), new[]
            {
                Straight(0, 8), Straight(2, 2), Straight(4, 4),
                Straight(2, 4), Straight(4, 4), Straight(6, 6)
            });

            DifficultyFeatures features = DifficultyFeatures.Measure(new[] { lShape });

            Assert.AreEqual(1, features.ConcaveAngleCount, "L 形的內凹角只有一個");
            Assert.AreEqual(4, features.DirectionVarietyCount);
            Assert.AreEqual(0, features.PinchPointCount);
            Assert.AreEqual(0, features.HoleCount);

            // 周長 = 8 + 2 + 4 + 4 + 4 + 6 = 28
            Assert.AreEqual(new Scalar(12, 20), features.DoubledHiddenEdgeLength);
        }

        /// <summary>同一題跑兩次結果一樣。決定性是 ELO 的另一半前提。</summary>
        [Test]
        public void 特徵_同一題跑兩次結果一樣()
        {
            Puzzle puzzle = ClassicSquarePuzzle();
            DifficultyFeatures first = DifficultyFeatures.Measure(puzzle);
            DifficultyFeatures second = DifficultyFeatures.Measure(puzzle);

            Assert.AreEqual(first.ConcaveAngleCount, second.ConcaveAngleCount);
            Assert.AreEqual(first.DirectionVarietyCount, second.DirectionVarietyCount);
            Assert.AreEqual(first.DoubledHiddenEdgeLength, second.DoubledHiddenEdgeLength);
            Assert.AreEqual(first.PinchPointCount, second.PinchPointCount);
            Assert.AreEqual(first.HoleCount, second.HoleCount);
        }

        // ─────────────────────────────────────────────────────────────
        // 二、藏起來的邊：那個常數是整條推論的地基
        // ─────────────────────────────────────────────────────────────

        /// <summary>
        /// ⚠️ <b>七片的周長總和 = 40 + 20√2。</b>
        ///
        /// 「藏起來的長度 =（常數 − 外框周長）÷ 2」整條推論就建在這個常數上，
        /// 而它之所以是常數，是因為剛體變換不改變邊長、而七片永遠是同樣那七片。
        ///
        /// 這條測試從 <see cref="PiecePrototype"/> 重新加一次，跟寫死的常數對。
        /// <b>哪天有人改了原型頂點（鐵則二那個坑），這裡會當場紅</b>——
        /// 沒有它的話，藏起來的長度會安靜地全部算錯。
        /// </summary>
        [Test]
        public void 常數_七片周長總和是四十加二十根號二()
        {
            Scalar total = Scalar.Zero;

            foreach (PieceKind kind in PiecePrototype.TangramSet)
            {
                ExactVector2[] vertices = PiecePrototype.Get(kind).Vertices;
                for (int vertexIndex = 0; vertexIndex < vertices.Length; vertexIndex++)
                {
                    ExactVector2 edge = vertices[(vertexIndex + 1) % vertices.Length] - vertices[vertexIndex];
                    Assert.IsTrue(Symmetry.TryDecompose(edge, out _, out Scalar length),
                                  $"{kind} 有邊不在 45° 網格上");
                    total = total + length;
                }
            }

            Assert.AreEqual(DifficultyFeatures.TotalPiecePerimeter, total);
        }

        /// <summary>
        /// 古典大正方形手算對一次：邊長 4√2 的正方形，周長 16√2。
        ///
        /// 藏起來的長度 =（40 + 20√2 − 16√2）÷ 2 = 20 + 2√2，所以兩倍是 40 + 4√2。
        /// 其餘四個數字：凸形狀所以凹角 0、軸對齊所以方向只有 4 種、沒有捏點也沒有洞。
        /// </summary>
        [Test]
        public void 古典大正方形_五個數字都對得上()
        {
            DifficultyFeatures features = DifficultyFeatures.Measure(ClassicSquarePuzzle());

            Assert.AreEqual(0, features.ConcaveAngleCount, "正方形是凸的，不該有凹角");
            Assert.AreEqual(4, features.DirectionVarietyCount, "軸對齊的正方形只用到四個方向");
            Assert.AreEqual(new Scalar(40, 4), features.DoubledHiddenEdgeLength);
            Assert.AreEqual(0, features.PinchPointCount);
            Assert.AreEqual(0, features.HoleCount);
            Assert.AreEqual(4, features.CornerCount, "正方形只有四個轉角");

            // 凸形狀的凸包就是它自己，所以填滿率剛好 1：凸包面積 ×2 = 七片面積 ×2 = 64
            Assert.AreEqual(DifficultyFeatures.ExpectedDoubleArea, features.ConvexHullDoubleArea,
                "凸形狀的凸包等於自己");
        }

        // ─────────────────────────────────────────────────────────────
        // 二之二、被外框逼出來的片（P2-6）
        // ─────────────────────────────────────────────────────────────

        /// <summary>
        /// 古典大正方形一片都逼不出來：四個角都是 90°，每個角都有一堆片放得進去
        /// （大中小三角的直角、正方形，還有兩個 45° 尖角湊一個 90° 的各種組合）。
        /// </summary>
        [Test]
        public void 逼出來的片_古典大正方形一片都沒有()
        {
            Assert.AreEqual(0, DifficultyFeatures.ForcedPieceCountOf(ClassicSquarePuzzle().Walks));
        }

        /// <summary>
        /// ⭐ <b>被逼出來的片一定出現在參考解裡。</b>
        ///
        /// 這條是「逼出來」這個概念的定義本身：如果某個角只放得進一種擺法，
        /// 那<b>任何</b>解都得那樣放——參考解當然也不例外。
        /// ⚠️ 反過來不成立：參考解裡的片不一定被逼出來。
        ///
        /// 它同時是 <c>ForcedPiecesOf</c> 唯一有力的守衛。角的凸凹判斷、開口的方向、
        /// 「一種佔位」的定義，任何一個寫錯都會讓某個角少算擺法，然後這裡就會冒出
        /// 一片不在參考解裡的片。（同樣的檢查在縮圖牆的匯出程式跑過 2000 個形狀。）
        /// </summary>
        [Test]
        public void 逼出來的片_一定在參考解裡()
        {
            var generator = new PuzzleGenerator(seed: 20260912);
            int checkedPuzzles = 0, forcedTotal = 0;

            for (int attempt = 0; attempt < 5000 && checkedPuzzles < 40; attempt++)
            {
                GenerationResult result = generator.TryGenerate();
                if (result.Outcome != GenerationOutcome.Generated) continue;
                checkedPuzzles++;

                var referenceFootprints = new HashSet<string>();
                foreach (Placement piece in result.ReferenceSolution)
                    referenceFootprints.Add(FootprintKeyOf(piece));

                foreach (Placement forced in DifficultyFeatures.ForcedPiecesOf(result.Puzzle.Walks))
                {
                    forcedTotal++;
                    Assert.IsTrue(referenceFootprints.Contains(FootprintKeyOf(forced)),
                        $"{result.Puzzle.Key}：逼出來的 {forced} 不在參考解裡");
                }
            }

            Assert.AreEqual(40, checkedPuzzles, "沒能在上限內生滿 40 題");
            TestContext.WriteLine($"40 題裡總共逼出 {forcedTotal} 片");
        }

        /// <summary>「佔哪塊地」的鍵：片種 + 排序過的頂點。姿態不同但佔位相同的會得到同一個鍵。</summary>
        /// <param name="placement">擺放。</param>
        /// <returns>比對用的字串。</returns>
        private static string FootprintKeyOf(Placement placement)
        {
            ExactVector2[] vertices = placement.Vertices();
            Array.Sort(vertices, (left, right) => left.CompareTo(right));
            var key = new System.Text.StringBuilder(placement.Kind.ToString());
            foreach (ExactVector2 vertex in vertices) key.Append(' ').Append(vertex.ToKey());
            return key.ToString();
        }

        // ─────────────────────────────────────────────────────────────
        // 三、洞與捏點：用合成的輪廓驗規則本身
        // ─────────────────────────────────────────────────────────────

        /// <summary>
        /// <b>洞環不需要翻號。</b>
        ///
        /// <see cref="BoundaryTracer"/> 把每條邊界邊都定向成**內部在左**，
        /// 所以洞環雖然有向面積是負的，走的時候材料一樣在左手邊——
        /// 「右轉 = 凹」對外環與洞環是同一條規則。
        ///
        /// 這裡用邊長 6 的正方形挖一個邊長 2 的方洞（面積 36 − 4 = 32，剛好過守衛）。
        /// 洞的四個角從材料的角度看都是 270°，所以<b>四個都是凹角</b>；外框四個角都是凸的。
        ///
        /// ⚠️ 這是**合成的輪廓**，不是真的用七片拼得出來的形狀——
        /// 特徵函數只守面積，這樣才驗得到規則本身。
        /// </summary>
        [Test]
        public void 洞_四個角都算凹角而且不必依繞向翻號()
        {
            // 外框：逆時針，材料在左
            var outerRing = new Walk(new ExactVector2(0, 0), new[]
            {
                Straight(0, 6), Straight(2, 6), Straight(4, 6), Straight(6, 6)
            });

            // 洞：順時針（有向面積為負），但材料一樣在左
            var holeRing = new Walk(new ExactVector2(2, 2), new[]
            {
                Straight(2, 2), Straight(0, 2), Straight(6, 2), Straight(4, 2)
            });

            Assert.IsTrue(outerRing.IsOuter, "外框應該是外環");
            Assert.IsFalse(holeRing.IsOuter, "洞應該是洞環");

            DifficultyFeatures features = DifficultyFeatures.Measure(new[] { outerRing, holeRing });

            Assert.AreEqual(1, features.HoleCount);
            Assert.AreEqual(4, features.ConcaveAngleCount, "洞的四個角都是 270°，外框四個角都是凸的");
            Assert.AreEqual(4, features.DirectionVarietyCount);
            Assert.AreEqual(0, features.PinchPointCount);

            // 周長 = 外框 24 + 洞 8 = 32。洞的邊也外露，一樣要算
            Assert.AreEqual(new Scalar(8, 20), features.DoubledHiddenEdgeLength);
        }

        /// <summary>
        /// 捏點：兩塊材料只碰到一個點，那個點會在環上被經過兩次。
        ///
        /// 這裡用兩個邊長 4 的正方形在 (4,4) 對角相接（面積 16 + 16 = 32）。
        /// 捏點算的是**相異的點**，所以是 1 個不是 2 個。
        /// 那個點的兩次經過都是 270°，所以凹角是 2。
        /// </summary>
        [Test]
        public void 捏點_同一個點被經過兩次算一個捏點()
        {
            var ring = new Walk(new ExactVector2(0, 0), new[]
            {
                Straight(0, 4), Straight(2, 4),   // 左下那塊的下緣與右緣，走到 (4,4)
                Straight(0, 4), Straight(2, 4),   // 右上那塊
                Straight(4, 4), Straight(6, 4),   // 繞回 (4,4)
                Straight(4, 4), Straight(6, 4)    // 左下那塊的上緣與左緣
            });

            DifficultyFeatures features = DifficultyFeatures.Measure(new[] { ring });

            Assert.AreEqual(1, features.PinchPointCount, "相異的點只有一個");
            Assert.AreEqual(2, features.ConcaveAngleCount, "捏點被經過兩次，兩次都是 270°");
            Assert.AreEqual(0, features.HoleCount);
        }

        // ─────────────────────────────────────────────────────────────
        // 四、守衛
        // ─────────────────────────────────────────────────────────────

        /// <summary>
        /// ⚠️ 面積不是 32 就丟例外。
        ///
        /// 「藏起來的長度」那條公式假設圖形正好是那七片。餵一個手繪題目或半成品進來，
        /// 算出來會是垃圾甚至負數，而且不報錯。丟例外不丟結果，是因為這代表<b>呼叫端有 bug</b>。
        /// </summary>
        [Test]
        public void 守衛_面積不是三十二就丟例外()
        {
            var wrongSize = new Walk(new ExactVector2(0, 0), new[]
            {
                Straight(0, 4), Straight(2, 4), Straight(4, 4), Straight(6, 4)
            });

            Assert.Throws<InvalidOperationException>(
                () => DifficultyFeatures.Measure(new[] { wrongSize }));
        }

        /// <summary>
        /// ⚠️ 180° 折返是一根零寬度的刺，合法輸入不可能出現，所以丟例外。
        ///
        /// 理由：每個切好的線段只會被一片擁有，所以「沿原路走回去」的那條邊界邊
        /// 根本不存在。看到它就表示輪廓資料壞了。
        /// </summary>
        [Test]
        public void 守衛_原地折返就丟例外()
        {
            // 走出去再原路走回來，圍不出面積——面積守衛之前就該被折返擋下來
            var spike = new Walk(new ExactVector2(0, 0), new[]
            {
                Straight(0, 4), Straight(4, 4)
            });

            Assert.Throws<InvalidOperationException>(
                () => DifficultyFeatures.Measure(new[] { spike }));
        }
    }
}
