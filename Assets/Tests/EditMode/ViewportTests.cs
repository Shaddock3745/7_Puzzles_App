using NUnit.Framework;
using Tangram.Core;
using Tangram.Data;
using UnityEngine;

namespace Tangram.Tests
{
    /// <summary>
    /// 座標與版面層（P1-1）的測試。
    ///
    /// 這一層的錯誤有個共同特徵：<b>不會當機，只會「位置怪怪的」</b>。
    /// 所以這裡的主力是 round-trip 測試——它不需要知道正確答案是什麼，
    /// 只要求兩個方向互為反函式，因此「Y 軸反了 / roll 轉錯邊 / 差半個螢幕」都躲不掉。
    /// 手算期望值的測試躲得掉，因為寫程式時搞錯的地方，手算時會用同一個錯誤的腦內模型再錯一次。
    /// </summary>
    public class ViewportTests
    {
        private const float Tolerance = 1e-3f;

        /// <summary>一個好認的直式螢幕。</summary>
        private const int PhoneWidth = 1080;
        private const int PhoneHeight = 1920;

        /// <summary>邊長 4 的正方形盒子，中心刻意不在原點，才抓得到「忘記減掉盒子中心」。</summary>
        private static FloatBoundingBox SquareBox()
            => new FloatBoundingBox(new Vector2(10f, 20f), new Vector2(14f, 24f));

        /// <summary>寬 8 高 2 的橫長盒子。</summary>
        private static FloatBoundingBox WideBox()
            => new FloatBoundingBox(new Vector2(0f, 0f), new Vector2(8f, 2f));

        // ------------------------------------------------------------------
        // 旋轉：整數運算，沒有三角函數
        // ------------------------------------------------------------------

        [Test]
        public void 旋轉_四個方位都對()
        {
            Vector2 unitX = new Vector2(1f, 0f);

            AssertVectorEqual(new Vector2(1f, 0f), ViewportMath.RotateQuarterTurns(unitX, 0));
            AssertVectorEqual(new Vector2(0f, 1f), ViewportMath.RotateQuarterTurns(unitX, 1));
            AssertVectorEqual(new Vector2(-1f, 0f), ViewportMath.RotateQuarterTurns(unitX, 2));
            AssertVectorEqual(new Vector2(0f, -1f), ViewportMath.RotateQuarterTurns(unitX, 3));
        }

        [Test]
        public void 旋轉_負數與超過四都先取模()
        {
            Vector2 vector = new Vector2(3f, -7f);

            AssertVectorEqual(ViewportMath.RotateQuarterTurns(vector, 1),
                              ViewportMath.RotateQuarterTurns(vector, 5));
            AssertVectorEqual(ViewportMath.RotateQuarterTurns(vector, 3),
                              ViewportMath.RotateQuarterTurns(vector, -1));
            AssertVectorEqual(vector, ViewportMath.RotateQuarterTurns(vector, -4));
        }

        [Test]
        public void 旋轉_逆旋轉真的是反函式()
        {
            Vector2 vector = new Vector2(2.5f, -4.25f);

            for (int quarterTurns = 0; quarterTurns < 4; quarterTurns++)
            {
                Vector2 rotated = ViewportMath.RotateQuarterTurns(vector, quarterTurns);
                Vector2 back = ViewportMath.RotateQuarterTurnsInverse(rotated, quarterTurns);
                AssertVectorEqual(vector, back);
            }
        }

        [Test]
        public void 旋轉_整數座標轉完還是整數_證明沒有浮點誤差()
        {
            // 這就是「roll 限定 0/90/180/270」換來的性質：轉一整圈也不會累積誤差
            Vector2 vector = new Vector2(7f, -13f);
            Vector2 current = vector;
            for (int i = 0; i < 4; i++) current = ViewportMath.RotateQuarterTurns(current, 1);

            Assert.AreEqual(vector.x, current.x, 0f, "轉四次 90° 應該完全回到原值，一位元都不差");
            Assert.AreEqual(vector.y, current.y, 0f);
        }

        // ------------------------------------------------------------------
        // screenPixelsPerModelUnit：那個 2 是因為 orthographicSize 是半高
        // ------------------------------------------------------------------

        [Test]
        public void 每單位像素_orthographicSize是半高不是全高()
        {
            // 螢幕高 800、正交半高 4 → 可見的世界高度是 8 → 每單位 100 像素
            Assert.AreEqual(100f, ViewportMath.ScreenPixelsPerModelUnitFrom(800f, 4f), Tolerance);
        }

        [Test]
        public void 每單位像素_參數不合法時回零而不是無限大()
        {
            Assert.AreEqual(0f, ViewportMath.ScreenPixelsPerModelUnitFrom(800f, 0f));
            Assert.AreEqual(0f, ViewportMath.ScreenPixelsPerModelUnitFrom(0f, 4f));
            Assert.AreEqual(0f, ViewportMath.ScreenPixelsPerModelUnitFrom(-1f, 4f));
        }

        [Test]
        public void Fit_算出來的解自己前後一致()
        {
            ViewportSolution solution = ViewportMath.Fit(
                SquareBox(), LayoutRule.Portrait, allowRoll: true, PhoneWidth, PhoneHeight);

            // orthographicSize 與 screenPixelsPerModelUnit 必須是同一件事的兩種說法
            Assert.AreEqual(solution.ScreenPixelsPerModelUnit,
                            ViewportMath.ScreenPixelsPerModelUnitFrom(PhoneHeight, solution.OrthographicSize),
                            Tolerance);
        }

        // ------------------------------------------------------------------
        // Fit：盒子要落在盒子區裡，而且置中
        // ------------------------------------------------------------------

        [Test]
        public void Fit_盒子中心會落在盒子區中心()
        {
            LayoutRule rule = LayoutRule.Portrait;
            FloatBoundingBox box = SquareBox();
            ViewportSolution solution =
                ViewportMath.Fit(box, rule, allowRoll: true, PhoneWidth, PhoneHeight);

            Vector2 boxCenterOnScreen = ViewportMath.WorldToScreen(box.Center, solution);

            Assert.AreEqual(PhoneWidth * rule.BoxAreaCenterXRatio, boxCenterOnScreen.x, Tolerance);
            Assert.AreEqual(PhoneHeight * rule.BoxAreaCenterYRatio, boxCenterOnScreen.y, Tolerance);
        }

        [Test]
        public void Fit_盒子四個角都不會超出盒子區()
        {
            LayoutRule rule = LayoutRule.Portrait;
            FloatBoundingBox box = WideBox();
            ViewportSolution solution =
                ViewportMath.Fit(box, rule, allowRoll: true, PhoneWidth, PhoneHeight);

            Vector2[] corners =
            {
                box.Minimum,
                new Vector2(box.Maximum.x, box.Minimum.y),
                box.Maximum,
                new Vector2(box.Minimum.x, box.Maximum.y)
            };

            float leftPixels = PhoneWidth * rule.BoxAreaMinimumX;
            float rightPixels = PhoneWidth * rule.BoxAreaMaximumX;
            float bottomPixels = PhoneHeight * rule.BoxAreaMinimumY;
            float topPixels = PhoneHeight * rule.BoxAreaMaximumY;

            foreach (Vector2 corner in corners)
            {
                Vector2 onScreen = ViewportMath.WorldToScreen(corner, solution);
                Assert.GreaterOrEqual(onScreen.x, leftPixels - Tolerance, "角超出盒子區左邊");
                Assert.LessOrEqual(onScreen.x, rightPixels + Tolerance, "角超出盒子區右邊");
                Assert.GreaterOrEqual(onScreen.y, bottomPixels - Tolerance, "角超出盒子區下邊");
                Assert.LessOrEqual(onScreen.y, topPixels + Tolerance, "角超出盒子區上邊");
            }
        }

        [Test]
        public void Fit_盒子至少貼滿盒子區的其中一個方向()
        {
            // 不只要「放得進去」，還要「放到最大」。少了這條，一個永遠回極小縮放的
            // 實作也能通過上面那個「不超出」的測試
            LayoutRule rule = LayoutRule.Portrait;
            FloatBoundingBox box = SquareBox();
            ViewportSolution solution =
                ViewportMath.Fit(box, rule, allowRoll: false, PhoneWidth, PhoneHeight);

            float boxWidthOnScreen = box.Width * solution.ScreenPixelsPerModelUnit;
            float boxHeightOnScreen = box.Height * solution.ScreenPixelsPerModelUnit;
            float areaWidthPixels = PhoneWidth * rule.BoxAreaWidthRatio;
            float areaHeightPixels = PhoneHeight * rule.BoxAreaHeightRatio;

            bool fillsWidth = Mathf.Abs(boxWidthOnScreen - areaWidthPixels) < Tolerance;
            bool fillsHeight = Mathf.Abs(boxHeightOnScreen - areaHeightPixels) < Tolerance;
            Assert.IsTrue(fillsWidth || fillsHeight,
                          "fit 應該是 min(寬限制, 高限制)，所以一定有一個方向剛好貼滿");
        }

        [Test]
        public void Fit_相機的垂直視野等於兩倍orthographicSize()
        {
            ViewportSolution solution = ViewportMath.Fit(
                SquareBox(), LayoutRule.Portrait, allowRoll: false, PhoneWidth, PhoneHeight);

            Vector2 bottomEdge = ViewportMath.ScreenToWorld(new Vector2(PhoneWidth * 0.5f, 0f), solution);
            Vector2 topEdge = ViewportMath.ScreenToWorld(new Vector2(PhoneWidth * 0.5f, PhoneHeight), solution);

            Assert.AreEqual(2f * solution.OrthographicSize, topEdge.y - bottomEdge.y, Tolerance);
        }

        // ------------------------------------------------------------------
        // Y 軸方向：Unity 螢幕座標原點左下、Y 向上
        // ------------------------------------------------------------------

        [Test]
        public void 螢幕座標_原點在左下而且Y向上()
        {
            ViewportSolution solution = ViewportMath.Fit(
                SquareBox(), LayoutRule.Portrait, allowRoll: false, PhoneWidth, PhoneHeight);

            Vector2 screenCenter = new Vector2(PhoneWidth * 0.5f, PhoneHeight * 0.5f);
            Vector2 higherOnScreen = new Vector2(screenCenter.x, screenCenter.y + 100f);
            Vector2 righterOnScreen = new Vector2(screenCenter.x + 100f, screenCenter.y);

            Vector2 atCenter = ViewportMath.ScreenToWorld(screenCenter, solution);
            Vector2 atHigher = ViewportMath.ScreenToWorld(higherOnScreen, solution);
            Vector2 atRighter = ViewportMath.ScreenToWorld(righterOnScreen, solution);

            Assert.Greater(atHigher.y, atCenter.y, "螢幕往上，世界也要往上（沒有 height - y）");
            Assert.Greater(atRighter.x, atCenter.x, "螢幕往右，世界也要往右");
            Assert.AreEqual(solution.CameraCenter.x, atCenter.x, Tolerance,
                            "螢幕正中心對應相機中心");
            Assert.AreEqual(solution.CameraCenter.y, atCenter.y, Tolerance);
        }

        // ------------------------------------------------------------------
        // round-trip：這一層真正的主力測試
        // ------------------------------------------------------------------

        [Test]
        public void RoundTrip_螢幕到世界再回螢幕_四個roll都對()
        {
            Vector2[] screenPoints =
            {
                new Vector2(0f, 0f),
                new Vector2(PhoneWidth, PhoneHeight),
                new Vector2(PhoneWidth * 0.5f, PhoneHeight * 0.5f),
                new Vector2(137f, 1801f),
                new Vector2(1043f, 62f)
            };

            for (int quarterTurns = 0; quarterTurns < 4; quarterTurns++)
            {
                ViewportSolution solution = new ViewportSolution(
                    new Vector2(12f, -34f), 5f, quarterTurns, 96f, PhoneWidth, PhoneHeight);

                foreach (Vector2 screenPoint in screenPoints)
                {
                    Vector2 world = ViewportMath.ScreenToWorld(screenPoint, solution);
                    Vector2 back = ViewportMath.WorldToScreen(world, solution);
                    AssertVectorEqual(screenPoint, back,
                                      "roll = " + quarterTurns + " 的 round-trip 對不上");
                }
            }
        }

        [Test]
        public void RoundTrip_世界到螢幕再回世界_四個roll都對()
        {
            Vector2[] worldPoints =
            {
                new Vector2(0f, 0f),
                new Vector2(12f, -34f),
                new Vector2(-7.5f, 21.25f)
            };

            for (int quarterTurns = 0; quarterTurns < 4; quarterTurns++)
            {
                ViewportSolution solution = new ViewportSolution(
                    new Vector2(12f, -34f), 5f, quarterTurns, 96f, PhoneWidth, PhoneHeight);

                foreach (Vector2 worldPoint in worldPoints)
                {
                    Vector2 screen = ViewportMath.WorldToScreen(worldPoint, solution);
                    Vector2 back = ViewportMath.ScreenToWorld(screen, solution);
                    AssertVectorEqual(worldPoint, back,
                                      "roll = " + quarterTurns + " 的 round-trip 對不上");
                }
            }
        }

        [Test]
        public void RoundTrip_經過Fit算出來的解也要對()
        {
            // 上面兩個用手捏的解，這個用真的 Fit 出來的解，確保 Fit 沒有寫出自相矛盾的欄位
            ViewportSolution solution = ViewportMath.Fit(
                WideBox(), LayoutRule.Portrait, allowRoll: true, PhoneWidth, PhoneHeight);
            Assert.IsTrue(solution.IsValid);

            Vector2 screenPoint = new Vector2(321f, 654f);
            Vector2 world = ViewportMath.ScreenToWorld(screenPoint, solution);
            AssertVectorEqual(screenPoint, ViewportMath.WorldToScreen(world, solution));
        }

        // ------------------------------------------------------------------
        // roll：一個比較就夠，不用列舉四種 case
        // ------------------------------------------------------------------

        [Test]
        public void Roll_橫盒子遇上直盒子區會轉九十度()
        {
            // 盒子區是直的（0.9 寬 × 0.63 高 的螢幕比例，在 1080×1920 上是 972×1210 像素）
            ViewportSolution solution = ViewportMath.Fit(
                WideBox(), LayoutRule.Portrait, allowRoll: true, PhoneWidth, PhoneHeight);

            Assert.AreEqual(1, solution.QuarterTurns, "橫的盒子放進直的區域應該轉");
        }

        [Test]
        public void Roll_轉了之後真的放得比較大()
        {
            FloatBoundingBox box = WideBox();
            LayoutRule rule = LayoutRule.Portrait;

            ViewportSolution rolled =
                ViewportMath.Fit(box, rule, allowRoll: true, PhoneWidth, PhoneHeight);
            ViewportSolution notRolled =
                ViewportMath.Fit(box, rule, allowRoll: false, PhoneWidth, PhoneHeight);

            Assert.AreEqual(1, rolled.QuarterTurns);
            Assert.AreEqual(0, notRolled.QuarterTurns);
            Assert.Greater(rolled.ScreenPixelsPerModelUnit, notRolled.ScreenPixelsPerModelUnit,
                           "roll 存在的唯一理由就是 fit 後比較大，沒變大就不該轉");
        }

        [Test]
        public void Roll_精選題不轉_而且代價是放得比較小()
        {
            // 互動設計 §2.6：相機 roll 90° 跟把世界轉 90° 在螢幕上完全等價，
            // 一隻貓不管轉哪邊都會躺著，所以精選題就是接受 fit 得比較差
            ViewportSolution curated = ViewportMath.Fit(
                WideBox(), LayoutRule.Portrait, allowRoll: false, PhoneWidth, PhoneHeight);

            Assert.AreEqual(0, curated.QuarterTurns);
            Assert.IsTrue(curated.IsValid, "不轉只是放得比較小，不是算不出來");
        }

        [Test]
        public void Roll_方形盒子不需要轉()
        {
            ViewportSolution solution = ViewportMath.Fit(
                SquareBox(), LayoutRule.Portrait, allowRoll: true, PhoneWidth, PhoneHeight);

            Assert.AreEqual(0, solution.QuarterTurns,
                            "轉了也不會變大的時候就不該轉（比較是嚴格大於）");
        }

        [Test]
        public void Roll_轉了之後盒子中心一樣要落在盒子區中心()
        {
            // roll 最容易錯的地方：相機位置忘了跟著轉，結果盒子飛到畫面外
            LayoutRule rule = LayoutRule.Portrait;
            FloatBoundingBox box = WideBox();
            ViewportSolution solution =
                ViewportMath.Fit(box, rule, allowRoll: true, PhoneWidth, PhoneHeight);

            Assert.AreEqual(1, solution.QuarterTurns);

            Vector2 boxCenterOnScreen = ViewportMath.WorldToScreen(box.Center, solution);
            Assert.AreEqual(PhoneWidth * rule.BoxAreaCenterXRatio, boxCenterOnScreen.x, Tolerance);
            Assert.AreEqual(PhoneHeight * rule.BoxAreaCenterYRatio, boxCenterOnScreen.y, Tolerance);
        }

        // ------------------------------------------------------------------
        // 退化守衛：不能回 NaN
        // ------------------------------------------------------------------

        [Test]
        public void 守衛_螢幕尺寸為零時回無效解而不是NaN()
        {
            // Screen.width 在初始化中與視窗最小化時會是 0。NaN 一路寫進 Camera
            // 之後畫面全黑而且不丟例外，是最難查的那種
            ViewportSolution zeroWidth =
                ViewportMath.Fit(SquareBox(), LayoutRule.Portrait, true, 0f, PhoneHeight);
            ViewportSolution zeroHeight =
                ViewportMath.Fit(SquareBox(), LayoutRule.Portrait, true, PhoneWidth, 0f);

            Assert.IsFalse(zeroWidth.IsValid);
            Assert.IsFalse(zeroHeight.IsValid);
            Assert.IsFalse(float.IsNaN(zeroWidth.OrthographicSize));
            Assert.IsFalse(float.IsNaN(zeroHeight.OrthographicSize));
        }

        [Test]
        public void 守衛_退化的盒子回無效解()
        {
            FloatBoundingBox flatBox =
                new FloatBoundingBox(new Vector2(0f, 0f), new Vector2(4f, 0f));
            FloatBoundingBox emptyBox =
                new FloatBoundingBox(new Vector2(0f, 0f), new Vector2(0f, 0f));

            Assert.IsTrue(flatBox.IsDegenerate);
            Assert.IsTrue(emptyBox.IsDegenerate);
            Assert.IsFalse(
                ViewportMath.Fit(flatBox, LayoutRule.Portrait, true, PhoneWidth, PhoneHeight).IsValid);
        }

        [Test]
        public void 守衛_無效解拿去換算座標回零而不是NaN()
        {
            ViewportSolution invalid = ViewportSolution.Invalid;

            Vector2 world = ViewportMath.ScreenToWorld(new Vector2(100f, 200f), invalid);
            Vector2 screen = ViewportMath.WorldToScreen(new Vector2(1f, 2f), invalid);

            Assert.AreEqual(0f, world.x);
            Assert.AreEqual(0f, world.y);
            Assert.AreEqual(0f, screen.x);
            Assert.AreEqual(0f, screen.y);
        }

        [Test]
        public void 守衛_版面規則的矩形反了會被擋掉()
        {
            LayoutRule reversed = new LayoutRule(0.9f, 0.9f, 0.1f, 0.1f);

            Assert.IsFalse(reversed.IsValid);
            Assert.IsFalse(
                ViewportMath.Fit(SquareBox(), reversed, true, PhoneWidth, PhoneHeight).IsValid);
        }

        // ------------------------------------------------------------------
        // 角色二：持有輸入、重算不快取
        // ------------------------------------------------------------------

        [Test]
        public void 座標狀態_沒設題目時回無效解()
        {
            BoardLayout layout = new BoardLayout(LayoutRule.Portrait);

            Assert.IsFalse(layout.HasBox);
            Assert.IsFalse(layout.Solve(PhoneWidth, PhoneHeight).IsValid);
        }

        [Test]
        public void 座標狀態_螢幕尺寸變化偵測()
        {
            BoardLayout layout = new BoardLayout(LayoutRule.Portrait);
            layout.SetBox(SquareBoxExact(), allowRoll: true);

            Assert.IsTrue(layout.HasScreenSizeChanged(PhoneWidth, PhoneHeight),
                          "剛換題目一定要重算");

            layout.RecordScreenSize(PhoneWidth, PhoneHeight);
            Assert.IsFalse(layout.HasScreenSizeChanged(PhoneWidth, PhoneHeight));
            Assert.IsTrue(layout.HasScreenSizeChanged(PhoneHeight, PhoneWidth), "轉螢幕要重算");
        }

        [Test]
        public void 座標狀態_換題目會強迫下一次重算()
        {
            BoardLayout layout = new BoardLayout(LayoutRule.Portrait);
            layout.SetBox(SquareBoxExact(), allowRoll: true);
            layout.RecordScreenSize(PhoneWidth, PhoneHeight);

            layout.SetBox(SquareBoxExact(), allowRoll: false);

            Assert.IsTrue(layout.HasScreenSizeChanged(PhoneWidth, PhoneHeight),
                          "螢幕沒變但題目變了，還是得重算並推送");
        }

        [Test]
        public void 座標狀態_重算不快取()
        {
            BoardLayout layout = new BoardLayout(LayoutRule.Portrait);
            layout.SetBox(SquareBoxExact(), allowRoll: true);

            ViewportSolution before = layout.Solve(PhoneWidth, PhoneHeight);
            layout.SetRule(new LayoutRule(0.0f, 0.0f, 1.0f, 1.0f));
            ViewportSolution after = layout.Solve(PhoneWidth, PhoneHeight);

            Assert.AreNotEqual(before.ScreenPixelsPerModelUnit, after.ScreenPixelsPerModelUnit,
                               "改了輸入之後 Solve 必須反映新輸入，不能拿舊係數");
        }

        [Test]
        public void 座標狀態_精選題的鎖定是關掉allowRoll不是寫死roll()
        {
            BoardLayout layout = new BoardLayout(LayoutRule.Portrait);
            layout.SetBox(WideBoxExact(), allowRoll: false);

            Assert.IsFalse(layout.AllowRoll);
            Assert.AreEqual(0, layout.Solve(PhoneWidth, PhoneHeight).QuarterTurns);
        }

        // ------------------------------------------------------------------
        // 精確 → 浮點：這一題唯一的轉換點，反方向永遠不准
        // ------------------------------------------------------------------

        [Test]
        public void 精確轉浮點_含根號二的座標也對()
        {
            // 4√2 ≈ 5.656854，是大正方形的邊長
            BoundingBox exact = new BoundingBox(
                ExactVector2.Zero,
                new ExactVector2(Scalar.FromRoot2Multiple(4), Scalar.FromInteger(2)));

            FloatBoundingBox converted = BoardLayout.ToFloat(exact);

            Assert.AreEqual(0f, converted.Minimum.x, Tolerance);
            Assert.AreEqual(5.656854f, converted.Maximum.x, Tolerance);
            Assert.AreEqual(2f, converted.Maximum.y, Tolerance);
            Assert.AreEqual(5.656854f, converted.Width, Tolerance);
        }

        [Test]
        public void 精確轉浮點_接得上真的題目()
        {
            // 走完整條路：七片 → Puzzle → Region → Bounds → 版面解
            Puzzle puzzle = Puzzle.FromPlacements(Fixtures.ClassicSquare());
            ExactVector2 boxOrigin = new ExactVector2(3, 5);
            Region boxRegion = Region.FromPuzzle(puzzle, boxOrigin);

            BoardLayout layout = new BoardLayout(LayoutRule.Portrait);
            layout.SetBox(boxRegion.Bounds, allowRoll: true);

            ViewportSolution solution = layout.Solve(PhoneWidth, PhoneHeight);
            Assert.IsTrue(solution.IsValid);
            Assert.Greater(solution.ScreenPixelsPerModelUnit, 0f);

            // 經典正方形是方的，不需要 roll
            Assert.AreEqual(0, solution.QuarterTurns);

            // 盒子中心要落在盒子區中心
            FloatBoundingBox floatBounds = BoardLayout.ToFloat(boxRegion.Bounds);
            Vector2 centerOnScreen = ViewportMath.WorldToScreen(floatBounds.Center, solution);
            Assert.AreEqual(PhoneWidth * LayoutRule.Portrait.BoxAreaCenterXRatio,
                            centerOnScreen.x, Tolerance);
            Assert.AreEqual(PhoneHeight * LayoutRule.Portrait.BoxAreaCenterYRatio,
                            centerOnScreen.y, Tolerance);
        }

        // ------------------------------------------------------------------
        // 輔助
        // ------------------------------------------------------------------

        /// <summary>邊長 4 的正方形盒子，精確版。</summary>
        private static BoundingBox SquareBoxExact()
            => new BoundingBox(new ExactVector2(10, 20), new ExactVector2(14, 24));

        /// <summary>寬 8 高 2 的橫長盒子，精確版。</summary>
        private static BoundingBox WideBoxExact()
            => new BoundingBox(ExactVector2.Zero, new ExactVector2(8, 2));

        private static void AssertVectorEqual(Vector2 expected, Vector2 actual,
                                              string message = null)
        {
            Assert.AreEqual(expected.x, actual.x, Tolerance, message);
            Assert.AreEqual(expected.y, actual.y, Tolerance, message);
        }
    }
}
