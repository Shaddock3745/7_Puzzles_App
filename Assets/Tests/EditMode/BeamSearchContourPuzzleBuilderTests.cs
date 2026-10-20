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
        /// 💡 走的是 <see cref="RecognizedContour.FromJson"/> 那條真的路：協作者的 JSON 是 y 朝上（2026-10-17 更正），跟模型同方向，所以外框頂點直接當像素座標，
        /// 照協作者的格式組成 JSON。像素座標是負的也沒關係，讀檔那邊不檢查範圍（之後反正要移重心、縮放）。
        /// 洞不管（目前的逼近本來就只看外框）。
        ///
        /// ⚠️ <b>有好幾個外環時在捏點接成一條</b>（2026-10-20，除錯記錄 016）。外框在捏點（兩塊只碰一個點）會斷成好幾個外環，
        /// 25 個具象題裡有 11 個是這樣（四隻貓全部：頭、身體、尾巴各一個環）。原本「取面積最大的那個」，目標就只剩軀幹，
        /// <b>而且軀幹還被放大到面積 32</b>——那 11 個的成績一直是對著錯的目標量的，不報錯，是使用者看圖發現「貓都只有軀幹」。
        /// </summary>
        internal static RecognizedContour OutlineOf(Puzzle puzzle)
        {
            var builder = new StringBuilder();
            ExactVector2[] vertices = JoinAtPinchPoints(puzzle.OuterWalks().Select(walk => walk.Vertices()).ToList());
            builder.Append("{\"width\":100,\"height\":100,\"pointCount\":").Append(vertices.Length).Append(",\"points\":[");
            for (int i = 0; i < vertices.Length; i++)
            {
                if (i > 0) builder.Append(',');
                builder.Append("{\"x\":").Append(vertices[i].X.ToDouble().ToString("R", CultureInfo.InvariantCulture))
                       .Append(",\"y\":").Append(vertices[i].Y.ToDouble().ToString("R", CultureInfo.InvariantCulture))
                       .Append('}');
            }
            builder.Append("]}");
            return RecognizedContour.FromJson(builder.ToString());
        }

        /// <summary>
        /// 幾個外環在共用的頂點（捏點）接成一條：走第一個環走到共用點 → 換走另一個環一整圈回到共用點 → 繼續走第一個環。
        /// 共用點會出現兩次（不相鄰），面積是各環相加，射線法判斷裡外照樣對。
        /// 七巧板的形狀是連通的，所以每個環一定跟已經接好的那條有共用點。
        /// </summary>
        /// <param name="rings">外環的頂點（精確座標）。</param>
        /// <returns>接好的一條。只有一個環時就是它。</returns>
        /// <exception cref="InvalidOperationException">有一個環跟其他環都沒有共用點。</exception>
        internal static ExactVector2[] JoinAtPinchPoints(List<ExactVector2[]> rings)
        {
            var joined = new List<ExactVector2>(rings[0]);
            var remaining = rings.GetRange(1, rings.Count - 1);
            while (remaining.Count > 0)
            {
                bool merged = false;
                for (int ringIndex = 0; ringIndex < remaining.Count && !merged; ringIndex++)
                    for (int i = 0; i < joined.Count && !merged; i++)
                    {
                        ExactVector2[] ring = remaining[ringIndex];
                        int shared = Array.IndexOf(ring, joined[i]);
                        if (shared < 0) continue;

                        // 從共用點的下一個開始繞一圈，最後回到共用點本身
                        var detour = new List<ExactVector2>(ring.Length);
                        for (int k = 1; k <= ring.Length; k++) detour.Add(ring[(shared + k) % ring.Length]);
                        joined.InsertRange(i + 1, detour);
                        remaining.RemoveAt(ringIndex);
                        merged = true;
                    }
                if (!merged) throw new InvalidOperationException("有一個外環跟其他環都沒有共用點，形狀不連通。");
            }
            return joined.ToArray();
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

        /// <summary>
        /// 排序：<b>沒開特徵照 IoU</b>、<b>開特徵照特徵分</b>（2026-10-20），由高到低；兩種都不能有重複的形狀。
        /// </summary>
        [Test]
        public void 結果照排名分數排序而且形狀不重複()
        {
            IReadOnlyList<ContourApproximation> withoutFeatures =
                new BeamSearchContourPuzzleBuilder(NarrowSettings().WithoutFeatures()).Approximate(ClassicSquareOutline());
            for (int i = 1; i < withoutFeatures.Count; i++)
                Assert.GreaterOrEqual(withoutFeatures[i - 1].IntersectionOverUnion, withoutFeatures[i].IntersectionOverUnion);

            IReadOnlyList<ContourApproximation> withFeatures =
                new BeamSearchContourPuzzleBuilder(NarrowSettings()).Approximate(ClassicSquareOutline());
            for (int i = 1; i < withFeatures.Count; i++)
                Assert.GreaterOrEqual(withFeatures[i - 1].FeatureScore, withFeatures[i].FeatureScore);

            foreach (IReadOnlyList<ContourApproximation> results in new[] { withoutFeatures, withFeatures })
                Assert.AreEqual(results.Count, results.Select(result => result.Puzzle.Key).Distinct().Count(), "有重複的形狀");
        }

        /// <summary>
        /// 突出蓋到幾成、缺口被填幾成都在 0 ～ 1（2026-10-20）。<b>權重歸零（不用特徵搜尋）照樣量得出來</b>，對照組才比得了；
        /// 半徑是 0 時沒有特徵可量，固定是 1 與 0。
        /// </summary>
        [Test]
        public void 特徵覆蓋率在0到1之間()
        {
            foreach (ContourApproximationSettings settings in new[] { NarrowSettings(), NarrowSettings().WithoutFeatures() })
                foreach (ContourApproximation result in new BeamSearchContourPuzzleBuilder(settings).Approximate(ClassicSquareOutline()))
                {
                    Assert.That(result.ProtrusionCoverage, Is.InRange(0.0, 1.0));
                    Assert.That(result.NotchFill, Is.InRange(0.0, 1.0));
                }

            var noRadius = new ContourApproximationSettings(beamWidth: 10, resultCount: 3, featureRadius: 0.0);
            foreach (ContourApproximation result in new BeamSearchContourPuzzleBuilder(noRadius).Approximate(ClassicSquareOutline()))
            {
                Assert.AreEqual(1.0, result.ProtrusionCoverage);
                Assert.AreEqual(0.0, result.NotchFill);
            }
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

            // 照片對照要用的平移量要一起帶過去（2026-10-14），不然照片疊不回盒子上
            Assert.AreEqual(best.OutlineOffset.x, built.OutlineOffset.x, 0f);
            Assert.AreEqual(best.OutlineOffset.y, built.OutlineOffset.y, 0f);
        }

        /// <summary>
        /// ⭐ <b>微調設成 0 輪 ＝ 第一版，一模一樣</b>（2026-10-14 第二版）。防的是「加微調的時候順手改壞了原本那條路」。
        /// 數字是加微調之前，用第一版在經典大正方形、N = 10、回 3 個時記下來的。
        /// IoU 是兩個整數的比，所以可以用極小的容差比。
        /// </summary>
        [Test]
        public void 微調設成0輪時結果跟第一版一模一樣()
        {
            var settings = new ContourApproximationSettings(beamWidth: 10, resultCount: 3, refinementRounds: 0).WithoutFeatures();
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
                                                            refinementStartPieceCount: 1).WithoutFeatures();
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

        /// <summary>
        /// ⭐ <b>不找特徵 ＝ 第三版，一模一樣</b>（2026-10-20）。跟上面兩條同一個用意：加特徵的時候不能改壞第三版那條路
        /// （格子多留的空白、取樣點的起點、排序都會影響）。數字是加特徵之前，用第三版的預設參數（N = 10、回 3 個）記下來的。
        /// </summary>
        [Test]
        public void 不找特徵時結果跟第三版一模一樣()
        {
            IReadOnlyList<ContourApproximation> results =
                new BeamSearchContourPuzzleBuilder(NarrowSettings().WithoutFeatures()).Approximate(ClassicSquareOutline());

            (double intersectionOverUnion, string key)[] thirdVersion =
            {
                (0.9008189262966333, "9a7f041529bb6a1e2bf4b91bb747354f"),
                (0.899090909090909, "69e4115268677ead27eb14e603b92dd2"),
                (0.8662192393736018, "4661667e25d5d90df3162c1af6f64a8b"),
            };

            Assert.AreEqual(thirdVersion.Length, results.Count);
            for (int i = 0; i < thirdVersion.Length; i++)
            {
                Assert.AreEqual(thirdVersion[i].intersectionOverUnion, results[i].IntersectionOverUnion, 1e-12, $"第 {i + 1} 名的 IoU");
                Assert.AreEqual(thirdVersion[i].key, results[i].Puzzle.Key, $"第 {i + 1} 名的形狀");
            }
        }

        /// <summary>
        /// 權重是 0 時，<b>半徑多少都不影響結果</b>（半徑只決定格子多留幾圈空白與量測用的特徵）。
        /// ⚠️ 半徑刻意用 0.8：留白 15 格 ＝ 1.875，<b>不是取樣間隔 0.25 的整數倍</b>。
        /// 預設的 0.75 留白 14 格 ＝ 1.75 剛好是整數倍，取樣點就算從留白後的原點起算也碰巧重合，抓不到那個錯
        /// （2026-10-20 變異測試發現的）。
        /// </summary>
        [Test]
        public void 權重是0時半徑不影響結果()
        {
            IReadOnlyList<ContourApproximation> noRadius = new BeamSearchContourPuzzleBuilder(
                new ContourApproximationSettings(beamWidth: 10, resultCount: 3, featureRadius: 0.0)).Approximate(ClassicSquareOutline());
            IReadOnlyList<ContourApproximation> withRadius = new BeamSearchContourPuzzleBuilder(
                new ContourApproximationSettings(beamWidth: 10, resultCount: 3, featureRadius: 0.8,
                                                 protrusionWeight: 0.0, notchWeight: 0.0)).Approximate(ClassicSquareOutline());

            Assert.AreEqual(noRadius.Count, withRadius.Count);
            for (int i = 0; i < noRadius.Count; i++)
            {
                Assert.AreEqual(noRadius[i].Puzzle.Key, withRadius[i].Puzzle.Key, $"第 {i + 1} 名的形狀");
                Assert.AreEqual(noRadius[i].IntersectionOverUnion, withRadius[i].IntersectionOverUnion, 1e-12, $"第 {i + 1} 名的 IoU");
                Assert.AreEqual(noRadius[i].OutlineOffset.x, withRadius[i].OutlineOffset.x, 0f);
                Assert.AreEqual(noRadius[i].OutlineOffset.y, withRadius[i].OutlineOffset.y, 0f);
            }
        }

        /// <summary>
        /// ⭐ <b>驗收工具的目標一定是整個形狀</b>（2026-10-20，除錯記錄 016）：每個具象題轉成輪廓之後，面積都是 32，
        /// 不是只有最大的那一塊。外框頂點直接當像素座標，所以像素面積就是模型面積。
        /// 同時確認這一條真的有測到捏點：至少要有一個具象題的外框斷成好幾個環（貓都是）。
        /// </summary>
        [Test]
        public void 驗收工具的目標輪廓是整個形狀不是最大的那一塊()
        {
            int splitCount = 0;
            foreach (PuzzleLibrary.Entry entry in CalibrationEntries.FigurativeShapes())
            {
                if (entry.Puzzle.OuterWalks().Count() > 1) splitCount++;

                IReadOnlyList<Vector2> points = OutlineOf(entry.Puzzle).PixelPoints;
                double doubleArea = 0.0;
                for (int i = 0, previous = points.Count - 1; i < points.Count; previous = i++)
                    doubleArea += (double)points[previous].x * points[i].y - (double)points[i].x * points[previous].y;
                Assert.AreEqual(RecognizedContour.ModelArea, Math.Abs(doubleArea) / 2.0, 1e-3, $"{entry.Name} 的目標輪廓面積不對");
            }
            Assert.Greater(splitCount, 0, "沒有一個具象題有捏點，這條測不到它要防的事");
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
            Assert.Throws<ArgumentOutOfRangeException>(() => new ContourApproximationSettings(featureRadius: -0.1));
            Assert.Throws<ArgumentOutOfRangeException>(() => new ContourApproximationSettings(protrusionWeight: -1.0));
            Assert.Throws<ArgumentOutOfRangeException>(() => new ContourApproximationSettings(notchWeight: -1.0));
        }

        /// <summary>半徑或兩個權重是 0 就等於沒開特徵；<c>WithoutFeatures</c> 只關特徵、其他照抄。</summary>
        [Test]
        public void 特徵的開關()
        {
            Assert.IsTrue(ContourApproximationSettings.Default.UsesFeatures);
            Assert.IsFalse(new ContourApproximationSettings(featureRadius: 0.0).UsesFeatures);
            Assert.IsFalse(new ContourApproximationSettings(protrusionWeight: 0.0, notchWeight: 0.0).UsesFeatures);
            Assert.IsTrue(new ContourApproximationSettings(protrusionWeight: 0.0).UsesFeatures, "只開缺口也算開了");

            ContourApproximationSettings original = new ContourApproximationSettings(beamWidth: 7, resultCount: 2, refinementRounds: 1);
            ContourApproximationSettings without = original.WithoutFeatures();
            Assert.IsFalse(without.UsesFeatures);
            Assert.AreEqual(original.BeamWidth, without.BeamWidth);
            Assert.AreEqual(original.ResultCount, without.ResultCount);
            Assert.AreEqual(original.RefinementRounds, without.RefinementRounds);
            Assert.AreEqual(original.RefinementStartPieceCount, without.RefinementStartPieceCount);
            Assert.AreEqual(original.FeatureRadius, without.FeatureRadius, "半徑照留：結果的特徵覆蓋率照樣量得出來");
        }

        // ───────── [Explicit] 驗收工具 ─────────

        /// <summary>
        /// ⭐ <b>驗收：25 個具象題逼不逼得回來，加上協作者那隻貓</b>（2026-10-14）。英文入口 <c>ExplicitTools.ApproximatorTable</c>。
        ///
        /// <b>兩組參數並排</b>：⭐ 2026-10-20 起對照組＝第三版（不找特徵）、實驗組＝預設參數（找特徵）。
        /// （第二版到第三版期間是「不微調」對「微調」，數字留在 <c>開發進度.md</c>〈逼近搜尋〉。）
        /// 每個具象題印兩組的 IoU、⭐ 突出蓋到幾成、缺口被填幾成、第 1 名是不是原本的形狀、前幾名有沒有、花了幾秒。
        /// 另外匯出網頁 <c>%TEMP%\tangram_approximation.html</c>：每個形狀一格、左右兩張圖（左對照、右實驗），
        /// 紅線是目標輪廓、色塊是第 1 名；⭐ 橘色格子是找到的突出部位、藍色是缺口（半徑照預設參數）。
        ///
        /// ⚠️ 時間是 Debug 的（約比 Release 慢 13 倍，見 <c>CLAUDE.md</c>〈怎麼跑測試〉），只能拿來比相對快慢。
        /// </summary>
        [Test, Explicit("逼近搜尋驗收：要跑好幾分鐘")]
        public void 逼近_具象題與掃描樣本的成績表()
        {
            // 2026-10-20 起：對照＝第三版（不找特徵）、實驗＝預設（找特徵）。第一版對第三版的比較見 開發進度.md〈逼近搜尋〉
            ContourApproximationSettings experiment = ContourApproximationSettings.Default;
            ContourApproximationSettings control = experiment.WithoutFeatures();
            var builders = new[] { new BeamSearchContourPuzzleBuilder(control), new BeamSearchContourPuzzleBuilder(experiment) };
            string[] labels = { "對照", "實驗" };

            TestContext.WriteLine($"對照：{control}");
            TestContext.WriteLine($"實驗：{experiment}");
            TestContext.WriteLine("名稱\t對照IoU\t實驗IoU\t對照突出\t實驗突出\t對照缺口\t實驗缺口\t對照逼回\t實驗逼回\t對照秒\t實驗秒");

            var rows = new List<string>();
            var recoveredFirst = new int[2];
            var recoveredAny = new int[2];
            var sumIntersectionOverUnion = new double[2];
            var sumProtrusionCoverage = new double[2];
            var sumNotchFill = new double[2];
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
                    sumProtrusionCoverage[arm] += best[arm].ProtrusionCoverage;
                    sumNotchFill[arm] += best[arm].NotchFill;
                    totalSeconds[arm] += seconds[arm];
                    marks[arm] = first ? "是" : any ? "前幾名" : "";
                }

                TestContext.WriteLine($"{entry.Name}\t{best[0].IntersectionOverUnion:0.000}\t{best[1].IntersectionOverUnion:0.000}\t" +
                                      $"{best[0].ProtrusionCoverage:0.00}\t{best[1].ProtrusionCoverage:0.00}\t" +
                                      $"{best[0].NotchFill:0.00}\t{best[1].NotchFill:0.00}\t" +
                                      $"{marks[0]}\t{marks[1]}\t{seconds[0]:0.0}\t{seconds[1]:0.0}");
                rows.Add(Row(entry.Name, outline, best, marks, labels));
            }

            for (int arm = 0; arm < 2; arm++)
                TestContext.WriteLine($"{labels[arm]}：具象題 {figurative.Count} 個平均 IoU {sumIntersectionOverUnion[arm] / figurative.Count:0.000}，" +
                                      $"突出蓋到 {sumProtrusionCoverage[arm] / figurative.Count:0.00}、缺口被填 {sumNotchFill[arm] / figurative.Count:0.00}，" +
                                      $"第 1 名就是原形狀 {recoveredFirst[arm]} 個，前幾名有 {recoveredAny[arm]} 個，合計 {totalSeconds[arm]:0.0} 秒");

            // 協作者那隻貓：沒有標準答案，只看 IoU 與圖
            RecognizedContour cat = RecognizedContour.FromJson(File.ReadAllText(RepositoryFiles.Find(ScanSampleRelativePath)));
            var catBest = new ContourApproximation[2];
            for (int arm = 0; arm < 2; arm++)
            {
                var clock = Stopwatch.StartNew();
                catBest[arm] = builders[arm].Approximate(cat)[0];
                TestContext.WriteLine($"掃描樣本（U²-Net 的貓）{labels[arm]}：IoU {catBest[arm].IntersectionOverUnion:0.000}，突出蓋到 {catBest[arm].ProtrusionCoverage:0.00}、缺口被填 {catBest[arm].NotchFill:0.00}，{clock.Elapsed.TotalSeconds:0.0} 秒");
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

            // 特徵格子畫在片的底下：橘＝突出、藍＝缺口。格子在輪廓座標，平移到題目座標再畫
            ContourApproximationSettings defaults = ContourApproximationSettings.Default;
            var features = new OutlineFeatureMap(outline.ModelOutline, defaults.CellSize, defaults.FeatureRadius);
            for (int row = 0; row < features.RowCount; row++)
                for (int column = 0; column < features.ColumnCount; column++)
                {
                    OutlineCellKind kind = features.KindOfCell(column, row);
                    if (kind != OutlineCellKind.Protrusion && kind != OutlineCellKind.Notch) continue;
                    double left = features.OriginX + column * features.CellSize + result.OutlineOffset.x;
                    double top = features.OriginY + (row + 1) * features.CellSize + result.OutlineOffset.y;
                    svg.Append(string.Format(CultureInfo.InvariantCulture,
                        "<rect x=\"{0:0.###}\" y=\"{1:0.###}\" width=\"{2:0.###}\" height=\"{2:0.###}\" fill=\"{3}\"/>",
                        left, -top, features.CellSize, kind == OutlineCellKind.Protrusion ? "#ffc88c" : "#96c8ff"));
                }

            for (int slot = 0; slot < piecePolygons.Count; slot++)
                svg.Append($"<polygon points=\"{Points(piecePolygons[slot])}\" fill=\"{colors[slot]}\" stroke=\"#222\" stroke-width=\"0.04\"/>");
            svg.Append($"<polygon points=\"{Points(outlinePoints)}\" fill=\"none\" stroke=\"#e00\" stroke-width=\"0.08\"/>");
            svg.Append("</svg>");

            return $"<figure>{svg}<figcaption><b>{title}</b>　IoU {result.IntersectionOverUnion:0.000}" +
                   $"　突出 {result.ProtrusionCoverage:0.00}　缺口 {result.NotchFill:0.00}<br>{note}</figcaption></figure>";
        }

        private static string Page(string settingsDescription, List<string> rows) =>
            "<!doctype html><html lang=\"zh-Hant\"><head><meta charset=\"utf-8\"><title>逼近搜尋驗收</title><style>" +
            "body{font-family:sans-serif;background:#fafafa;margin:16px}" +
            ".grid{display:grid;grid-template-columns:repeat(auto-fill,minmax(420px,1fr));gap:12px}" +
            "section{background:#fff;border:1px solid #ddd;padding:8px}h2{font-size:15px;margin:0 0 4px}" +
            ".pair{display:grid;grid-template-columns:1fr 1fr;gap:8px}" +
            "figure{margin:0}svg{width:100%;height:180px}figcaption{font-size:13px;margin-top:4px}</style></head><body>" +
            $"<h1>逼近搜尋驗收</h1><p>{settingsDescription}<br>紅線＝目標輪廓、色塊＝逼出來的第 1 名、橘格＝突出、藍格＝缺口；每列左邊對照、右邊實驗</p>" +
            $"<div class=\"grid\">{string.Join("", rows)}</div></body></html>";
    }
}
