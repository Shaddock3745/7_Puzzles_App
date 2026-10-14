using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using NUnit.Framework;
using UnityEngine;

namespace Tangram.Data.Tests
{
    /// <summary>
    /// <see cref="BeamSearchContourPuzzleBuilder"/>：最笨的 beam search（2026-10-14）。
    ///
    /// <b>日常的幾條只驗「結果一定合法、格式對」</b>，用最便宜的形狀（經典大正方形）、窄的 beam。
    /// <b>「逼得像不像」是 <see cref="逼近_具象題與掃描樣本的成績表"/> 的事</b>（<c>[Explicit]</c>，英文入口
    /// <c>ExplicitTools.ApproximatorTable</c>）——25 個具象題每一個都有已知答案，所以「逼不逼得回來」是有標準答案的驗收。
    /// </summary>
    public class BeamSearchContourPuzzleBuilderTests
    {
        private const string ScanSampleRelativePath = "Assets/Resources/ScanSamples/u2net_unity_contour.json";

        /// <summary>日常測試用的窄 beam：只驗合法性，不驗像不像，越快越好。</summary>
        private static ContourApproximationSettings NarrowSettings() =>
            new ContourApproximationSettings(beamWidth: 10, resultCount: 3);

        /// <summary>
        /// 把一個題目的外框變成輪廓——拿已知答案的形狀當目標用。
        ///
        /// 💡 走的是 <see cref="RecognizedContour.FromJson"/> 那條真的路：外框頂點的 y 取負號就是「照片座標、y 朝下」，
        /// 照協作者的格式組成 JSON。像素座標是負的也沒關係，讀檔那邊不檢查範圍（之後反正要移重心、縮放）。
        /// 有好幾個外環時取面積最大的那個；洞不管（目前的逼近本來就只看外框）。
        /// </summary>
        internal static RecognizedContour OutlineOf(Puzzle puzzle)
        {
            Walk largest = null;
            double largestArea = double.MinValue;
            foreach (Walk walk in puzzle.OuterWalks())
            {
                double area = walk.SignedDoubleArea().ToDouble();
                if (area > largestArea) { largestArea = area; largest = walk; }
            }

            var builder = new StringBuilder();
            ExactVector2[] vertices = largest.Vertices();
            builder.Append("{\"width\":100,\"height\":100,\"pointCount\":").Append(vertices.Length).Append(",\"points\":[");
            for (int i = 0; i < vertices.Length; i++)
            {
                if (i > 0) builder.Append(',');
                builder.Append("{\"x\":").Append(vertices[i].X.ToDouble().ToString("R", CultureInfo.InvariantCulture))
                       .Append(",\"y\":").Append((-vertices[i].Y.ToDouble()).ToString("R", CultureInfo.InvariantCulture))
                       .Append('}');
            }
            builder.Append("]}");
            return RecognizedContour.FromJson(builder.ToString());
        }

        private static RecognizedContour ClassicSquareOutline() =>
            OutlineOf(Puzzle.FromPlacements(Fixtures.ClassicSquare()));

        // ───────── 日常 ─────────

        /// <summary>
        /// ⭐ <b>結果一定合法</b>：七片、片種對得上槽、互不重疊、題目就是從這七片描出來的。
        /// 這是逼近跟「修輪廓」最大的差別——候選本來就全部合法，所以結果永遠拼得出來。
        /// </summary>
        [Test]
        public void 結果一定合法_七片不重疊而且題目就是它們描出來的()
        {
            IReadOnlyList<ContourApproximation> results =
                new BeamSearchContourPuzzleBuilder(NarrowSettings()).Approximate(ClassicSquareOutline());

            Assert.Greater(results.Count, 0, "連一個結果都沒有");
            foreach (ContourApproximation result in results)
            {
                Assert.AreEqual(7, result.PiecesBySlot.Length);
                for (int slot = 0; slot < 7; slot++)
                    Assert.AreEqual(PiecePrototype.TangramSet[slot], result.PiecesBySlot[slot].Kind, $"槽 {slot} 的片種不對");

                for (int i = 0; i < 7; i++)
                    for (int j = i + 1; j < 7; j++)
                        Assert.IsFalse(Geometry.ConvexOverlap(result.PiecesBySlot[i].Vertices(), result.PiecesBySlot[j].Vertices()),
                                       $"槽 {i} 與槽 {j} 重疊");

                Assert.AreEqual(Puzzle.FromPlacements(result.PiecesBySlot).Key, result.Puzzle.Key,
                                "題目跟參考解對不起來");
                Assert.That(result.IntersectionOverUnion, Is.InRange(0.0, 1.0));
            }
        }

        /// <summary>照 IoU 由高到低，而且同一個形狀只出現一次。</summary>
        [Test]
        public void 結果照IoU排序而且形狀不重複()
        {
            IReadOnlyList<ContourApproximation> results =
                new BeamSearchContourPuzzleBuilder(NarrowSettings()).Approximate(ClassicSquareOutline());

            for (int i = 1; i < results.Count; i++)
                Assert.GreaterOrEqual(results[i - 1].IntersectionOverUnion, results[i].IntersectionOverUnion);

            Assert.AreEqual(results.Count, results.Select(result => result.Puzzle.Key).Distinct().Count(), "有重複的形狀");
        }

        /// <summary>給遊戲用的那支只拿第 1 名，參考解跟題目要對得起來。</summary>
        [Test]
        public void 給遊戲用的那支回第一名()
        {
            var builder = new BeamSearchContourPuzzleBuilder(NarrowSettings());
            RecognizedContour outline = ClassicSquareOutline();

            ContourPuzzle built = builder.Build(outline);
            ContourApproximation best = builder.Approximate(outline)[0];

            Assert.AreEqual(best.Puzzle.Key, built.Puzzle.Key, "同一組參數應該得到同一個結果（搜尋要可重現）");
            Assert.AreEqual(built.Puzzle.Key, Puzzle.FromPlacements(built.ReferenceSolution).Key);
        }

        [Test]
        public void 不收null輪廓()
        {
            var builder = new BeamSearchContourPuzzleBuilder(NarrowSettings());
            Assert.Throws<ArgumentNullException>(() => builder.Approximate(null));
            Assert.Throws<ArgumentNullException>(() => builder.Build(null));
        }

        [Test]
        public void 參數不合法會丟例外()
        {
            Assert.Throws<ArgumentOutOfRangeException>(() => new ContourApproximationSettings(beamWidth: 0));
            Assert.Throws<ArgumentOutOfRangeException>(() => new ContourApproximationSettings(cellSize: 0.0));
            Assert.Throws<ArgumentOutOfRangeException>(() => new ContourApproximationSettings(outsidePenalty: -1.0));
            Assert.Throws<ArgumentOutOfRangeException>(() => new ContourApproximationSettings(offsetSpacing: 0.0));
            Assert.Throws<ArgumentOutOfRangeException>(() => new ContourApproximationSettings(resultCount: 0));
        }

        // ───────── [Explicit] 驗收工具 ─────────

        /// <summary>
        /// ⭐ <b>驗收：25 個具象題逼不逼得回來，加上協作者那隻貓</b>（2026-10-14）。英文入口 <c>ExplicitTools.ApproximatorTable</c>。
        ///
        /// 每個具象題印：最好的 IoU、第 1 名是不是就是原本的形狀、前幾名裡有沒有原本的形狀、花了幾秒。
        /// 另外匯出一張網頁 <c>%TEMP%\tangram_approximation.html</c>：每個形狀一格，紅線是目標輪廓、色塊是逼出來的第 1 名，用眼睛看。
        ///
        /// ⚠️ 時間是 Debug 的（約比 Release 慢 13 倍，見 <c>CLAUDE.md</c>〈怎麼跑測試〉），只能拿來比相對快慢。
        /// </summary>
        [Test, Explicit("逼近搜尋驗收：要跑好幾分鐘")]
        public void 逼近_具象題與掃描樣本的成績表()
        {
            ContourApproximationSettings settings = ContourApproximationSettings.Default;
            var builder = new BeamSearchContourPuzzleBuilder(settings);
            var panels = new List<string>();

            TestContext.WriteLine($"參數：{settings}");
            TestContext.WriteLine("名稱\tIoU\t第1名就是原形狀\t前幾名有原形狀\t秒");

            int recoveredFirst = 0, recoveredAny = 0;
            double sumIntersectionOverUnion = 0.0, totalSeconds = 0.0;
            IReadOnlyList<PuzzleLibrary.Entry> figurative = CalibrationEntries.FigurativeShapes();

            foreach (PuzzleLibrary.Entry entry in figurative)
            {
                RecognizedContour outline = OutlineOf(entry.Puzzle);
                var clock = Stopwatch.StartNew();
                IReadOnlyList<ContourApproximation> results = builder.Approximate(outline);
                double seconds = clock.Elapsed.TotalSeconds;

                ContourApproximation best = results[0];
                bool first = best.Puzzle.Key == entry.Puzzle.Key;
                bool any = results.Any(result => result.Puzzle.Key == entry.Puzzle.Key);
                if (first) recoveredFirst++;
                if (any) recoveredAny++;
                sumIntersectionOverUnion += best.IntersectionOverUnion;
                totalSeconds += seconds;

                TestContext.WriteLine($"{entry.Name}\t{best.IntersectionOverUnion:0.000}\t{(first ? "是" : "")}\t{(any ? "有" : "")}\t{seconds:0.0}");
                panels.Add(Panel(entry.Name, outline, best, first ? "逼回原形狀" : any ? "前幾名有原形狀" : ""));
            }

            TestContext.WriteLine($"具象題 {figurative.Count} 個：平均 IoU {sumIntersectionOverUnion / figurative.Count:0.000}，" +
                                  $"第 1 名就是原形狀 {recoveredFirst} 個，前幾名有 {recoveredAny} 個，合計 {totalSeconds:0.0} 秒");

            // 協作者那隻貓：沒有標準答案，只看 IoU 與圖
            RecognizedContour cat = RecognizedContour.FromJson(File.ReadAllText(RepositoryFiles.Find(ScanSampleRelativePath)));
            var catClock = Stopwatch.StartNew();
            IReadOnlyList<ContourApproximation> catResults = builder.Approximate(cat);
            TestContext.WriteLine($"掃描樣本（U²-Net 的貓）：IoU {catResults[0].IntersectionOverUnion:0.000}，{catClock.Elapsed.TotalSeconds:0.0} 秒");
            for (int i = 0; i < catResults.Count; i++)
                panels.Insert(i, Panel($"掃描樣本 第 {i + 1} 名", cat, catResults[i], ""));

            string path = Path.Combine(Path.GetTempPath(), "tangram_approximation.html");
            File.WriteAllText(path, Page(settings, panels), new UTF8Encoding(false));
            TestContext.WriteLine($"圖：{path}");
        }

        /// <summary>一格：紅線是目標輪廓（已經平移到題目座標），色塊是七片。</summary>
        private static string Panel(string title, RecognizedContour outline, ContourApproximation result, string note)
        {
            var outlinePoints = new List<(double x, double y)>();
            foreach (Vector2 point in outline.ModelOutline)
                outlinePoints.Add((point.x + result.OutlineOffset.x, point.y + result.OutlineOffset.y));

            var piecePolygons = new List<(double x, double y)[]>();
            foreach (Placement piece in result.PiecesBySlot)
                piecePolygons.Add(piece.Vertices().Select(vertex => (vertex.X.ToDouble(), vertex.Y.ToDouble())).ToArray());

            IEnumerable<(double x, double y)> everything = outlinePoints.Concat(piecePolygons.SelectMany(polygon => polygon));
            double minimumX = everything.Min(point => point.x) - 0.5, maximumX = everything.Max(point => point.x) + 0.5;
            double minimumY = everything.Min(point => point.y) - 0.5, maximumY = everything.Max(point => point.y) + 0.5;

            // SVG 的 y 朝下，模型的 y 朝上：畫的時候把 y 取負號
            string Points(IEnumerable<(double x, double y)> points) =>
                string.Join(" ", points.Select(point => string.Format(CultureInfo.InvariantCulture, "{0:0.###},{1:0.###}", point.x, -point.y)));

            string[] colors = { "#d9c25e", "#d96b6b", "#8fd96b", "#9b7fd9", "#d96bc8", "#6ba8d9", "#6bd9a8" };
            var svg = new StringBuilder();
            svg.Append(string.Format(CultureInfo.InvariantCulture,
                "<svg viewBox=\"{0:0.###} {1:0.###} {2:0.###} {3:0.###}\">",
                minimumX, -maximumY, maximumX - minimumX, maximumY - minimumY));
            for (int slot = 0; slot < piecePolygons.Count; slot++)
                svg.Append($"<polygon points=\"{Points(piecePolygons[slot])}\" fill=\"{colors[slot]}\" stroke=\"#222\" stroke-width=\"0.04\"/>");
            svg.Append($"<polygon points=\"{Points(outlinePoints)}\" fill=\"none\" stroke=\"#e00\" stroke-width=\"0.08\"/>");
            svg.Append("</svg>");

            return $"<figure>{svg}<figcaption><b>{title}</b>　IoU {result.IntersectionOverUnion:0.000}<br>{note}</figcaption></figure>";
        }

        private static string Page(ContourApproximationSettings settings, List<string> panels) =>
            "<!doctype html><html lang=\"zh-Hant\"><head><meta charset=\"utf-8\"><title>逼近搜尋驗收</title><style>" +
            "body{font-family:sans-serif;background:#fafafa;margin:16px}" +
            ".grid{display:grid;grid-template-columns:repeat(auto-fill,minmax(220px,1fr));gap:12px}" +
            "figure{margin:0;background:#fff;border:1px solid #ddd;padding:8px}svg{width:100%;height:200px}" +
            "figcaption{font-size:13px;margin-top:4px}</style></head><body>" +
            $"<h1>逼近搜尋驗收</h1><p>{settings}　紅線＝目標輪廓、色塊＝逼出來的七片</p>" +
            $"<div class=\"grid\">{string.Join("", panels)}</div></body></html>";
    }
}
