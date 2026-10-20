using System;
using NUnit.Framework;
using UnityEngine;

namespace Tangram.Data.Tests
{
    /// <summary>
    /// <see cref="OutlineFeatureMap"/>：輪廓上哪裡是突出、哪裡是缺口（2026-10-20）。
    /// 形狀直接用模型座標寫，格子 1/8、半徑 0.75（逼近搜尋的預設）。
    /// </summary>
    public class OutlineFeatureMapTests
    {
        private const double CellSize = 0.125;
        private const double Radius = 0.75;

        private static Vector2[] Polygon(params (float x, float y)[] points)
        {
            var result = new Vector2[points.Length];
            for (int i = 0; i < points.Length; i++) result[i] = new Vector2(points[i].x, points[i].y);
            return result;
        }

        /// <summary>邊長 4 的正方形，中心在原點。</summary>
        private static Vector2[] Square() => Polygon((-2, -2), (2, -2), (2, 2), (-2, 2));

        /// <summary>
        /// 正方形的身體不是突出；<b>直角的尖端是</b>——開運算會把角磨圓，那正是「星星的角」被認出來的同一個道理。
        /// </summary>
        [Test]
        public void 正方形只有角的尖端算突出()
        {
            var map = new OutlineFeatureMap(Square(), CellSize, Radius);

            Assert.AreEqual(OutlineCellKind.Inside, map.KindAt(0.0, 0.0), "中心");
            Assert.AreEqual(OutlineCellKind.Inside, map.KindAt(0.0, 1.95), "邊的中點");
            Assert.AreEqual(OutlineCellKind.Protrusion, map.KindAt(1.95, 1.95), "角的尖端");
            Assert.AreEqual(0, map.NotchCellCount, "凸的形狀沒有缺口");

            // 四個角各一小塊：一個角被磨掉的面積是 r²(1 − π/4) ≈ 0.12，約 8 格
            Assert.That(map.ProtrusionCellCount, Is.InRange(4, 4 * 16));
        }

        /// <summary>⭐ 比 2r 細的刺（尾巴、耳朵）整根是突出，身體不是。</summary>
        [Test]
        public void 細刺整根算突出()
        {
            // 正方形右邊伸出一根寬 0.5、長 2 的刺
            Vector2[] outline = Polygon((-2, -2), (2, -2), (2, -0.25f), (4, -0.25f), (4, 0.25f), (2, 0.25f), (2, 2), (-2, 2));
            var map = new OutlineFeatureMap(outline, CellSize, Radius);

            Assert.AreEqual(OutlineCellKind.Protrusion, map.KindAt(3.0, 0.0), "刺的中段");
            Assert.AreEqual(OutlineCellKind.Protrusion, map.KindAt(3.9, 0.0), "刺的尖端");
            Assert.AreEqual(OutlineCellKind.Inside, map.KindAt(0.0, 0.0), "身體");
        }

        /// <summary>⭐ 比 2r 窄的凹縫（兩耳之間）是缺口；寬的凹口不是。</summary>
        [Test]
        public void 窄縫算缺口寬口不算()
        {
            // 從上面往下切一條縫，寬 0.5、深 2
            Vector2[] narrow = Polygon((-3, -2), (3, -2), (3, 2), (0.25f, 2), (0.25f, 0), (-0.25f, 0), (-0.25f, 2), (-3, 2));
            Assert.AreEqual(OutlineCellKind.Notch, new OutlineFeatureMap(narrow, CellSize, Radius).KindAt(0.0, 1.0));

            // 同樣的深度，寬 3
            Vector2[] wide = Polygon((-3, -2), (3, -2), (3, 2), (1.5f, 2), (1.5f, 0), (-1.5f, 0), (-1.5f, 2), (-3, 2));
            Assert.AreEqual(OutlineCellKind.Outside, new OutlineFeatureMap(wide, CellSize, Radius).KindAt(0.0, 1.0));
        }

        /// <summary>半徑 0 ＝ 不找特徵：只有裡外兩種。</summary>
        [Test]
        public void 半徑0只分裡外()
        {
            Vector2[] outline = Polygon((-2, -2), (2, -2), (2, -0.25f), (4, -0.25f), (4, 0.25f), (2, 0.25f), (2, 2), (-2, 2));
            var map = new OutlineFeatureMap(outline, CellSize, 0.0);

            Assert.AreEqual(0, map.ProtrusionCellCount);
            Assert.AreEqual(0, map.NotchCellCount);
            Assert.AreEqual(OutlineCellKind.Inside, map.KindAt(3.0, 0.0));
            Assert.AreEqual(OutlineCellKind.Outside, map.KindAt(3.0, 1.0));
            Assert.AreEqual(OutlineCellKind.Outside, map.KindAt(100.0, 100.0), "格子範圍外一律是外面");
        }

        /// <summary>
        /// ⭐ <b>留不留空白，格子中心都在同一個位置</b>：裡面的格數一樣、原點都是格子的整數倍、不留空白的原點一樣。
        /// 逼近搜尋「不找特徵時跟第三版一模一樣」靠的就是這一條。
        /// </summary>
        [Test]
        public void 留空白不會移動格子()
        {
            Vector2[] outline = Polygon((-1.3f, -2.1f), (2.2f, -1.7f), (0.4f, 2.6f));
            var plain = new OutlineFeatureMap(outline, CellSize, 0.0);
            var padded = new OutlineFeatureMap(outline, CellSize, Radius);

            Assert.AreEqual(plain.InsideCellCount, padded.InsideCellCount);
            Assert.AreEqual(plain.UnpaddedOriginX, padded.UnpaddedOriginX);
            Assert.AreEqual(plain.UnpaddedOriginY, padded.UnpaddedOriginY);
            Assert.AreEqual(plain.OriginX, plain.UnpaddedOriginX, "不留空白時原點就是不留空白的原點");
            Assert.Greater(padded.ColumnCount, plain.ColumnCount);

            double columns = (padded.OriginX - plain.OriginX) / CellSize;
            Assert.AreEqual(Math.Round(columns), columns, 1e-9, "留白一定是整數格");
        }

        [Test]
        public void 參數不合法會丟例外()
        {
            Assert.Throws<ArgumentNullException>(() => new OutlineFeatureMap(null, CellSize, Radius));
            Assert.Throws<ArgumentOutOfRangeException>(() => new OutlineFeatureMap(Square(), 0.0, Radius));
            Assert.Throws<ArgumentOutOfRangeException>(() => new OutlineFeatureMap(Square(), CellSize, -1.0));
        }
    }
}
