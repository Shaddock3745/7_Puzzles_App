using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using NUnit.Framework;
using UnityEngine;

namespace Tangram.Data.Tests
{
    /// <summary>
    /// <see cref="RecognizedContour"/>（2026-10-14，掃描模式）。
    ///
    /// <b>這一支在看什麼</b>：協作者交的那份樣本讀不讀得進來、換到模型座標之後是不是
    /// 「面積 32、重心在原點、逆時針、y 朝上、沒變形」，以及壞掉的輸入會不會被擋下來。
    /// </summary>
    public class RecognizedContourTests
    {
        /// <summary>協作者交的樣本（U²-Net 的輸出，一隻貓），遊戲的假辨識模組回的就是這一份。</summary>
        private const string SampleRelativePath = "Assets/Resources/ScanSamples/u2net_unity_contour.json";

        private static string SampleJson() => File.ReadAllText(RepositoryFiles.Find(SampleRelativePath));

        /// <summary>照協作者的格式組一份 JSON。</summary>
        /// <param name="width">圖片寬度。</param>
        /// <param name="height">圖片高度。</param>
        /// <param name="points">像素座標的點，y 朝上（跟協作者的 JSON 一樣）。</param>
        /// <param name="pointCount">檔案自己報的點數；不給就用實際的點數。</param>
        private static string ContourJson(int width, int height, (double x, double y)[] points, int? pointCount = null)
        {
            var parts = new List<string>();
            foreach ((double x, double y) in points)
                parts.Add($"{{\"x\":{x},\"y\":{y},\"normalizedX\":{x / width},\"normalizedY\":{y / height}}}");

            return $"{{\"width\":{width},\"height\":{height},\"pointCount\":{pointCount ?? points.Length}," +
                   $"\"points\":[{string.Join(",", parts)}]}}";
        }

        /// <returns>多邊形的有向面積，逆時針為正。</returns>
        private static double SignedArea(IReadOnlyList<Vector2> polygon)
        {
            double doubled = 0.0;
            for (int i = 0; i < polygon.Count; i++)
            {
                Vector2 current = polygon[i], next = polygon[(i + 1) % polygon.Count];
                doubled += (double)current.x * next.y - (double)next.x * current.y;
            }
            return doubled / 2.0;
        }

        /// <returns>多邊形的面積重心。</returns>
        private static (double x, double y) Centroid(IReadOnlyList<Vector2> polygon)
        {
            double doubled = 0.0, x = 0.0, y = 0.0;
            for (int i = 0; i < polygon.Count; i++)
            {
                Vector2 current = polygon[i], next = polygon[(i + 1) % polygon.Count];
                double cross = (double)current.x * next.y - (double)next.x * current.y;
                doubled += cross;
                x += (current.x + next.x) * cross;
                y += (current.y + next.y) * cross;
            }
            return (x / (3.0 * doubled), y / (3.0 * doubled));
        }

        // ───────── 樣本 ─────────

        [Test]
        public void 樣本_讀得進來()
        {
            RecognizedContour contour = RecognizedContour.FromJson(SampleJson());

            Assert.AreEqual(320, contour.ImageWidth);
            Assert.AreEqual(320, contour.ImageHeight);
            Assert.AreEqual(86, contour.PixelPoints.Count, "樣本沒有重複點，一個都不該被拿掉");
            Assert.AreEqual(86, contour.ModelOutline.Count);
        }

        /// <summary>⭐ 逼近搜尋的前提全在這一條：面積 32、重心在原點、逆時針。</summary>
        [Test]
        public void 樣本_換到模型座標之後面積是32_重心在原點_逆時針()
        {
            RecognizedContour contour = RecognizedContour.FromJson(SampleJson());

            double area = SignedArea(contour.ModelOutline);
            Assert.AreEqual(RecognizedContour.ModelArea, area, 1e-3, "面積不對，或繞向是順時針（面積會是負的）");

            (double x, double y) centroid = Centroid(contour.ModelOutline);
            Assert.AreEqual(0.0, centroid.x, 1e-4);
            Assert.AreEqual(0.0, centroid.y, 1e-4);
        }

        // ───────── 座標轉換 ─────────

        /// <summary>
        /// ⚠️ <b>y 不能翻</b>：協作者的 JSON 是 y 朝上（Unity 的習慣，2026-10-17 更正，除錯記錄 015）。
        /// 翻了的話貓會倒過來，而且面積、重心都還是對的——只有這一條抓得到。
        /// 用一個直角在<b>左下角</b>的三角形（y 朝上時 (10, 10) 是左下）：換完之後直角還是要在左下（x 最小、y 最小）。
        /// </summary>
        [Test]
        public void 轉換_y朝上不翻()
        {
            string json = ContourJson(100, 100, new[] { (10.0, 10.0), (50.0, 10.0), (10.0, 50.0) });

            IReadOnlyList<Vector2> outline = RecognizedContour.FromJson(json).ModelOutline;

            float minimumX = float.MaxValue, minimumY = float.MaxValue;
            foreach (Vector2 point in outline)
            {
                minimumX = Math.Min(minimumX, point.x);
                minimumY = Math.Min(minimumY, point.y);
            }

            int cornersAtBottomLeft = 0;
            foreach (Vector2 point in outline)
                if (Math.Abs(point.x - minimumX) < 1e-4 && Math.Abs(point.y - minimumY) < 1e-4) cornersAtBottomLeft++;

            Assert.AreEqual(1, cornersAtBottomLeft, "直角應該在左下；翻了 y 的話它會跑到左上（貓會倒過來）");
        }

        /// <summary>等比縮放：20 × 10 的長方形縮完還是 2 : 1，不能被拉成正方形。</summary>
        [Test]
        public void 轉換_等比縮放不變形()
        {
            string json = ContourJson(100, 100, new[] { (0.0, 0.0), (20.0, 0.0), (20.0, 10.0), (0.0, 10.0) });

            IReadOnlyList<Vector2> outline = RecognizedContour.FromJson(json).ModelOutline;

            float minimumX = float.MaxValue, maximumX = float.MinValue, minimumY = float.MaxValue, maximumY = float.MinValue;
            foreach (Vector2 point in outline)
            {
                minimumX = Math.Min(minimumX, point.x); maximumX = Math.Max(maximumX, point.x);
                minimumY = Math.Min(minimumY, point.y); maximumY = Math.Max(maximumY, point.y);
            }

            Assert.AreEqual(2.0, (maximumX - minimumX) / (maximumY - minimumY), 1e-4);
            Assert.AreEqual(8.0, maximumX - minimumX, 1e-4, "面積 32 的 2:1 長方形是 8 × 4");
        }

        /// <summary>照片裡順時針給的點，換完要變成逆時針——兩種繞向都要能吃。</summary>
        [Test]
        public void 轉換_兩種繞向都變成逆時針()
        {
            // 這兩組一組順時針一組逆時針
            var clockwise = new[] { (0.0, 0.0), (10.0, 0.0), (10.0, 10.0), (0.0, 10.0) };
            var counterClockwise = new[] { (0.0, 0.0), (0.0, 10.0), (10.0, 10.0), (10.0, 0.0) };

            Assert.Greater(SignedArea(RecognizedContour.FromJson(ContourJson(100, 100, clockwise)).ModelOutline), 0.0);
            Assert.Greater(SignedArea(RecognizedContour.FromJson(ContourJson(100, 100, counterClockwise)).ModelOutline), 0.0);
        }

        /// <summary>最後一點重複寫了第一點、或中間連續重複，都要拿掉——不然會有長度 0 的邊。</summary>
        [Test]
        public void 轉換_重複的點會被拿掉()
        {
            string json = ContourJson(100, 100, new[]
            {
                (0.0, 0.0), (10.0, 0.0), (10.0, 0.0), (10.0, 10.0), (0.0, 10.0), (0.0, 0.0),
            });

            Assert.AreEqual(4, RecognizedContour.FromJson(json).PixelPoints.Count);
        }

        // ───────── 壞掉的輸入 ─────────

        [Test]
        public void 不合法_空字串()
        {
            Assert.Throws<ArgumentException>(() => RecognizedContour.FromJson(null));
            Assert.Throws<ArgumentException>(() => RecognizedContour.FromJson("   "));
        }

        [Test]
        public void 不合法_不是JSON()
        {
            Assert.Throws<ArgumentException>(() => RecognizedContour.FromJson("這不是 JSON"));
        }

        /// <summary>⚠️ <c>JsonUtility</c> 遇到不認得的欄位不吭聲——別的格式會讀成「每個欄位都是預設值」，要擋。</summary>
        [Test]
        public void 不合法_不是這個格式()
        {
            var error = Assert.Throws<ArgumentException>(() => RecognizedContour.FromJson("{\"rings\":[]}"));
            StringAssert.Contains("points", error.Message);
        }

        /// <summary>檔案自己報的點數對不上＝被截斷了，那條輪廓少了一截。</summary>
        [Test]
        public void 不合法_點數對不上()
        {
            string json = ContourJson(100, 100, new[] { (0.0, 0.0), (10.0, 0.0), (0.0, 10.0) }, pointCount: 5);
            var error = Assert.Throws<ArgumentException>(() => RecognizedContour.FromJson(json));
            StringAssert.Contains("pointCount", error.Message);
        }

        [Test]
        public void 不合法_點不到三個()
        {
            string json = ContourJson(100, 100, new[] { (0.0, 0.0), (10.0, 0.0) });
            Assert.Throws<ArgumentException>(() => RecognizedContour.FromJson(json));
        }

        /// <summary>重複點拿掉之後才不到三個，也要擋。</summary>
        [Test]
        public void 不合法_拿掉重複點之後不到三個()
        {
            string json = ContourJson(100, 100, new[] { (0.0, 0.0), (10.0, 0.0), (10.0, 0.0), (0.0, 0.0) });
            Assert.Throws<ArgumentException>(() => RecognizedContour.FromJson(json));
        }

        /// <summary>點全部共線，面積是零——不擋的話縮放倍率是無限大。</summary>
        [Test]
        public void 不合法_面積是零()
        {
            string json = ContourJson(100, 100, new[] { (0.0, 0.0), (10.0, 10.0), (20.0, 20.0) });
            var error = Assert.Throws<ArgumentException>(() => RecognizedContour.FromJson(json));
            StringAssert.Contains("面積", error.Message);
        }

        [Test]
        public void 不合法_圖片尺寸不是正數()
        {
            string json = ContourJson(0, 100, new[] { (0.0, 0.0), (10.0, 0.0), (0.0, 10.0) });
            Assert.Throws<ArgumentException>(() => RecognizedContour.FromJson(json));
        }

        // ───────── 像素 → 輪廓座標（照片對照，2026-10-14）─────────

        /// <summary>
        /// ⭐ <b>每個輪廓點換過去都落在 <see cref="RecognizedContour.ModelOutline"/> 上</b>。
        /// 照片對照靠的就是這條：照片用 <see cref="RecognizedContour.PixelToModel"/> 換，輪廓也是用它換的，
        /// 兩邊是同一套數字——哪天有人把其中一邊改成自己算，這條會紅。
        /// </summary>
        [Test]
        public void 換算_每個輪廓點換過去都落在輪廓上()
        {
            RecognizedContour contour = RecognizedContour.FromJson(SampleJson());

            foreach (Vector2 pixel in contour.PixelPoints)
            {
                Vector2 model = contour.PixelToModel(pixel.x, pixel.y);
                bool found = false;
                foreach (Vector2 outlinePoint in contour.ModelOutline)
                    if (Math.Abs(outlinePoint.x - model.x) < 1e-6 && Math.Abs(outlinePoint.y - model.y) < 1e-6) { found = true; break; }
                Assert.IsTrue(found, $"像素點 {pixel} 換過去是 {model}，不在輪廓上");
            }
        }

        /// <summary>
        /// 整張圖在輪廓座標裡是一個正的長方形：寬高比跟原圖一樣、大小是像素數 × 縮放倍率，
        /// 而且像素 (0, 0) 落在長方形的左下角（y 朝上，原點在圖的左下）。
        /// </summary>
        [Test]
        public void 換算_整張圖是正的長方形而且原點在左下()
        {
            string json = ContourJson(200, 100, new[] { (20.0, 10.0), (180.0, 10.0), (180.0, 90.0), (20.0, 90.0) });
            RecognizedContour contour = RecognizedContour.FromJson(json);

            (Vector2 minimum, Vector2 maximum) = contour.ImageBoundsInModel();
            double width = maximum.x - minimum.x, height = maximum.y - minimum.y;

            Assert.AreEqual(2.0, width / height, 1e-5, "寬高比要跟原圖（200 × 100）一樣");
            Assert.AreEqual(200 * contour.ModelUnitsPerPixel, width, 1e-4);

            Vector2 origin = contour.PixelToModel(0, 0);
            Assert.AreEqual(minimum.x, origin.x, 1e-5);
            Assert.AreEqual(minimum.y, origin.y, 1e-5, "像素 y = 0 是圖的最下面，要對到長方形最下面");
        }

        // ───────── 剪影（照片替身，2026-10-14）─────────

        /// <summary>邊長 80 的正方形，每個像素中心都清楚地在裡或外，所以剛好是 6400 個。</summary>
        [Test]
        public void 剪影_正方形的像素數剛好等於面積()
        {
            string json = ContourJson(100, 100, new[] { (10.0, 10.0), (90.0, 10.0), (90.0, 90.0), (10.0, 90.0) });
            bool[] mask = RecognizedContour.FromJson(json).SilhouetteMask();

            Assert.AreEqual(100 * 100, mask.Length);
            Assert.AreEqual(80 * 80, mask.Count(inside => inside));
        }

        /// <summary>
        /// ⚠️ <b>第 0 列是照片最上面</b>，而像素座標是 y 朝上——兩者剛好相反，弄錯的話替身照片會上下顛倒、跟輪廓錯開。
        /// 用一個直角在左下角的三角形（y 朝上時 (0, 0) 是左下）：最下面那列的左邊在裡面，最上面那列的左邊在外面。
        /// </summary>
        [Test]
        public void 剪影_第0列是照片最上面()
        {
            string json = ContourJson(100, 100, new[] { (0.0, 0.0), (60.0, 0.0), (0.0, 60.0) });
            bool[] mask = RecognizedContour.FromJson(json).SilhouetteMask();

            Assert.IsTrue(mask[99 * 100 + 5], "最下面那列（第 99 列）的左邊應該在三角形裡");
            Assert.IsFalse(mask[0 * 100 + 5], "最上面那列（第 0 列）的左邊應該在三角形外");
        }

        /// <summary>貓的剪影像素數跟輪廓的像素面積差不到一圈邊緣（周長）。</summary>
        [Test]
        public void 剪影_貓的像素數接近輪廓面積()
        {
            RecognizedContour contour = RecognizedContour.FromJson(SampleJson());
            int count = contour.SilhouetteMask().Count(inside => inside);

            double doubledArea = 0.0, perimeter = 0.0;
            IReadOnlyList<Vector2> points = contour.PixelPoints;
            for (int i = 0; i < points.Count; i++)
            {
                Vector2 current = points[i], next = points[(i + 1) % points.Count];
                doubledArea += (double)current.x * next.y - (double)next.x * current.y;
                perimeter += Vector2.Distance(current, next);
            }

            Assert.AreEqual(Math.Abs(doubledArea) / 2.0, count, perimeter, "差太多：剪影跟輪廓不是同一個形狀");
        }

        // 💡 原本這裡還有兩條測假出題器（FixedContourPuzzleBuilder）的，2026-10-14 換成 beam search 之後連同假出題器一起刪了。
        //    逼近的測試在 BeamSearchContourPuzzleBuilderTests。
    }
}
