using System;
using System.Collections.Generic;
using System.IO;
using NUnit.Framework;
using UnityEngine;

namespace Tangram.Data.Tests
{
    /// <summary>
    /// <see cref="RecognizedContour"/> 與 <see cref="FixedContourPuzzleBuilder"/>（2026-10-14，掃描模式）。
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
        /// <param name="points">像素座標的點，y 朝下。</param>
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
        /// ⚠️ <b>y 一定要翻</b>：照片的 y 朝下。不翻的話貓會倒過來，而且面積、重心都還是對的——只有這一條抓得到。
        /// 用一個直角在照片<b>左上角</b>的三角形：翻過來之後直角還是要在左上（x 最小、y 最大）。
        /// </summary>
        [Test]
        public void 轉換_y有翻過來()
        {
            string json = ContourJson(100, 100, new[] { (10.0, 10.0), (50.0, 10.0), (10.0, 50.0) });

            IReadOnlyList<Vector2> outline = RecognizedContour.FromJson(json).ModelOutline;

            float minimumX = float.MaxValue, maximumY = float.MinValue;
            foreach (Vector2 point in outline)
            {
                minimumX = Math.Min(minimumX, point.x);
                maximumY = Math.Max(maximumY, point.y);
            }

            int cornersAtTopLeft = 0;
            foreach (Vector2 point in outline)
                if (Math.Abs(point.x - minimumX) < 1e-4 && Math.Abs(point.y - maximumY) < 1e-4) cornersAtTopLeft++;

            Assert.AreEqual(1, cornersAtTopLeft, "直角應該在左上；沒翻 y 的話它會跑到左下");
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
            // 照片座標（y 朝下）裡，這兩組一組順時針一組逆時針
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

        // ───────── 假的出題器 ─────────

        /// <summary>
        /// 假的出題器回的是題庫那一題，<b>參考解是複本</b>——交出去被改了不能污染題庫。
        /// </summary>
        [Test]
        public void 假出題器_回固定那一題_參考解是複本()
        {
            PuzzleLibrary library = PuzzleLibrary.FromText(File.ReadAllText(RepositoryFiles.Find("Assets/Resources/puzzle_library.txt")));
            PuzzleLibrary.Entry entry = library.FigurativeShapes[0];
            var builder = new FixedContourPuzzleBuilder(entry);

            ContourPuzzle built = builder.Build(RecognizedContour.FromJson(SampleJson()));

            Assert.AreEqual(entry.Name, built.Name);
            Assert.AreSame(entry.Puzzle, built.Puzzle);
            Assert.AreNotSame(entry.Pieces, built.ReferenceSolution, "參考解要複製一份");
            CollectionAssert.AreEqual(entry.Pieces, built.ReferenceSolution);
        }

        [Test]
        public void 假出題器_不收null輪廓()
        {
            PuzzleLibrary library = PuzzleLibrary.FromText(File.ReadAllText(RepositoryFiles.Find("Assets/Resources/puzzle_library.txt")));
            var builder = new FixedContourPuzzleBuilder(library.FigurativeShapes[0]);

            Assert.Throws<ArgumentNullException>(() => builder.Build(null));
        }
    }
}
