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
        /// 💡 這組參數有開平移微調（預設 2 輪），所以這一條同時在驗「微調只動輪廓、不動任何一片」。
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

        /// <summary>
        /// ⭐ <b>微調設成 0 輪 ＝ 第一版，一模一樣</b>（2026-10-14 第二版）。防的是「加微調的時候順手改壞了原本那條路」。
        /// 數字是加微調之前，用第一版在經典大正方形、N = 10、回 3 個時記下來的。
        /// IoU 是兩個整數的比，所以可以用極小的容差比。
        /// </summary>
        [Test]
        public void 微調設成0輪時結果跟第一版一模一樣()
        {
            var settings = new ContourApproximationSettings(beamWidth: 10, resultCount: 3, refinementRounds: 0);
            IReadOnlyList<ContourApproximation> results =
                new BeamSearchContourPuzzleBuilder(settings).Approximate(ClassicSquareOutline());

            (double intersectionOverUnion, string key)[] firstVersion =
            {
                (0.9008189262966333, "9a7f041529bb6a1e2bf4b91bb747354f"),
                (0.899090909090909, "69e4115268677ead27eb14e603b92dd2"),
                (0.8655649843680214, "e984d8eee23fd89c85834942e443b73d"),
            };

            Assert.AreEqual(firstVersion.Length, results.Count);
            for (int i = 0; i < firstVersion.Length; i++)
            {
                Assert.AreEqual(firstVersion[i].intersectionOverUnion, results[i].IntersectionOverUnion, 1e-12, $"第 {i + 1} 名的 IoU");
                Assert.AreEqual(firstVersion[i].key, results[i].Puzzle.Key, $"第 {i + 1} 名的形狀");
            }
        }

        /// <summary>
        /// ⭐ <b>從第 1 片開始微調 ＝ 第二版，一模一樣</b>（2026-10-14 第三版）。跟上一條同一個用意：
        /// 加「從第幾片開始」的時候不能改壞第二版那條路。數字是加這個參數之前，用第二版（微調 2 輪）記下來的。
        /// </summary>
        [Test]
        public void 從第1片開始微調時結果跟第二版一模一樣()
        {
            var settings = new ContourApproximationSettings(beamWidth: 10, resultCount: 3, refinementRounds: 2,
                                                            refinementStartPieceCount: 1);
            IReadOnlyList<ContourApproximation> results =
                new BeamSearchContourPuzzleBuilder(settings).Approximate(ClassicSquareOutline());

            (double intersectionOverUnion, string key)[] secondVersion =
            {
                (0.9046333783175888, "10bde9cf110df35e8bd6acce686e29d5"),
                (0.9035166816952209, "3f7ae4a9cb9f5972944885faf19b2dfa"),
                (0.9030085316569376, "386bdc0b870e19f8c6782c633ca91df2"),
            };

            Assert.AreEqual(secondVersion.Length, results.Count);
            for (int i = 0; i < secondVersion.Length; i++)
            {
                Assert.AreEqual(secondVersion[i].intersectionOverUnion, results[i].IntersectionOverUnion, 1e-12, $"第 {i + 1} 名的 IoU");
                Assert.AreEqual(secondVersion[i].key, results[i].Puzzle.Key, $"第 {i + 1} 名的形狀");
            }
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
            Assert.Throws<ArgumentOutOfRangeException>(() => new ContourApproximationSettings(refinementRounds: -1));
            Assert.Throws<ArgumentOutOfRangeException>(() => new ContourApproximationSettings(refinementStartPieceCount: 0));
            Assert.Throws<ArgumentOutOfRangeException>(() => new ContourApproximationSettings(refinementStartPieceCount: 8));
        }

        // ───────── [Explicit] 驗收工具 ─────────

        /// <summary>
        /// ⭐ <b>驗收：25 個具象題逼不逼得回來，加上協作者那隻貓</b>（2026-10-14）。英文入口 <c>ExplicitTools.ApproximatorTable</c>。
        ///
        /// <b>兩組參數並排</b>（第二版起）：對照組＝不微調（等於第一版）、實驗組＝預設參數（微調 2 輪）。
        /// 每個具象題印兩組的 IoU、第 1 名是不是原本的形狀、前幾名有沒有、花了幾秒。
        /// 另外匯出網頁 <c>%TEMP%\tangram_approximation.html</c>：每個形狀一格、左右兩張圖（左對照、右實驗），
        /// 紅線是目標輪廓、色塊是第 1 名。
        ///
        /// ⚠️ 時間是 Debug 的（約比 Release 慢 13 倍，見 <c>CLAUDE.md</c>〈怎麼跑測試〉），只能拿來比相對快慢。
        /// </summary>
        [Test, Explicit("逼近搜尋驗收：要跑好幾分鐘")]
        public void 逼近_具象題與掃描樣本的成績表()
        {
            ContourApproximationSettings experiment = ContourApproximationSettings.Default;
            var control = new ContourApproximationSettings(
                experiment.BeamWidth, experiment.CellSize, experiment.OutsidePenalty, experiment.OffsetSpacing,
                experiment.ResultCount, refinementRounds: 0);
            var builders = new[] { new BeamSearchContourPuzzleBuilder(control), new BeamSearchContourPuzzleBuilder(experiment) };
            string[] labels = { "對照", "實驗" };

            TestContext.WriteLine($"對照：{control}");
            TestContext.WriteLine($"實驗：{experiment}");
            TestContext.WriteLine("名稱\t對照IoU\t實驗IoU\t對照逼回\t實驗逼回\t對照秒\t實驗秒");

            var rows = new List<string>();
            var recoveredFirst = new int[2];
            var recoveredAny = new int[2];
            var sumIntersectionOverUnion = new double[2];
            var totalSeconds = new double[2];
            IReadOnlyList<PuzzleLibrary.Entry> figurative = CalibrationEntries.FigurativeShapes();

            foreach (PuzzleLibrary.Entry entry in figurative)
            {
                RecognizedContour outline = OutlineOf(entry.Puzzle);
                var best = new ContourApproximation[2];
                var marks = new string[2];
                var seconds = new double[2];

                for (int arm = 0; arm < 2; arm++)
                {
                    var clock = Stopwatch.StartNew();
                    IReadOnlyList<ContourApproximation> results = builders[arm].Approximate(outline);
                    seconds[arm] = clock.Elapsed.TotalSeconds;

                    // ⚠️ 2026-10-14 第二版跑出過 1.016（共用邊上的格子被兩片各算一次）。超過 1 一定是計數錯了，直接失敗
                    foreach (ContourApproximation result in results)
                        Assert.LessOrEqual(result.IntersectionOverUnion, 1.0, $"{entry.Name}（{labels[arm]}）的 IoU 超過 1：格子被重複計算了");

                    best[arm] = results[0];
                    bool first = best[arm].Puzzle.Key == entry.Puzzle.Key;
                    bool any = results.Any(result => result.Puzzle.Key == entry.Puzzle.Key);
                    if (first) recoveredFirst[arm]++;
                    if (any) recoveredAny[arm]++;
                    sumIntersectionOverUnion[arm] += best[arm].IntersectionOverUnion;
                    totalSeconds[arm] += seconds[arm];
                    marks[arm] = first ? "是" : any ? "前幾名" : "";
                }

                TestContext.WriteLine($"{entry.Name}\t{best[0].IntersectionOverUnion:0.000}\t{best[1].IntersectionOverUnion:0.000}\t" +
                                      $"{marks[0]}\t{marks[1]}\t{seconds[0]:0.0}\t{seconds[1]:0.0}");
                rows.Add(Row(entry.Name, outline, best, marks, labels));
            }

            for (int arm = 0; arm < 2; arm++)
                TestContext.WriteLine($"{labels[arm]}：具象題 {figurative.Count} 個平均 IoU {sumIntersectionOverUnion[arm] / figurative.Count:0.000}，" +
                                      $"第 1 名就是原形狀 {recoveredFirst[arm]} 個，前幾名有 {recoveredAny[arm]} 個，合計 {totalSeconds[arm]:0.0} 秒");

            // 協作者那隻貓：沒有標準答案，只看 IoU 與圖
            RecognizedContour cat = RecognizedContour.FromJson(File.ReadAllText(RepositoryFiles.Find(ScanSampleRelativePath)));
            var catBest = new ContourApproximation[2];
            for (int arm = 0; arm < 2; arm++)
            {
                var clock = Stopwatch.StartNew();
                catBest[arm] = builders[arm].Approximate(cat)[0];
                TestContext.WriteLine($"掃描樣本（U²-Net 的貓）{labels[arm]}：IoU {catBest[arm].IntersectionOverUnion:0.000}，{clock.Elapsed.TotalSeconds:0.0} 秒");
            }
            rows.Insert(0, Row("掃描樣本（U²-Net 的貓）", cat, catBest, new[] { "", "" }, labels));

            string path = Path.Combine(Path.GetTempPath(), "tangram_approximation.html");
            File.WriteAllText(path, Page($"對照：{control}<br>實驗：{experiment}", rows), new UTF8Encoding(false));
            TestContext.WriteLine($"圖：{path}");
        }

        /// <summary>一列：同一個形狀，左邊對照組、右邊實驗組。</summary>
        private static string Row(string title, RecognizedContour outline, ContourApproximation[] best, string[] marks, string[] labels) =>
            $"<section><h2>{title}</h2><div class=\"pair\">" +
            Panel(labels[0], outline, best[0], marks[0] == "是" ? "逼回原形狀" : marks[0] == "前幾名" ? "前幾名有原形狀" : "") +
            Panel(labels[1], outline, best[1], marks[1] == "是" ? "逼回原形狀" : marks[1] == "前幾名" ? "前幾名有原形狀" : "") +
            "</div></section>";

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

        private static string Page(string settingsDescription, List<string> rows) =>
            "<!doctype html><html lang=\"zh-Hant\"><head><meta charset=\"utf-8\"><title>逼近搜尋驗收</title><style>" +
            "body{font-family:sans-serif;background:#fafafa;margin:16px}" +
            ".grid{display:grid;grid-template-columns:repeat(auto-fill,minmax(420px,1fr));gap:12px}" +
            "section{background:#fff;border:1px solid #ddd;padding:8px}h2{font-size:15px;margin:0 0 4px}" +
            ".pair{display:grid;grid-template-columns:1fr 1fr;gap:8px}" +
            "figure{margin:0}svg{width:100%;height:180px}figcaption{font-size:13px;margin-top:4px}</style></head><body>" +
            $"<h1>逼近搜尋驗收</h1><p>{settingsDescription}<br>紅線＝目標輪廓、色塊＝逼出來的第 1 名；每列左邊對照、右邊實驗</p>" +
            $"<div class=\"grid\">{string.Join("", rows)}</div></body></html>";
    }
}
