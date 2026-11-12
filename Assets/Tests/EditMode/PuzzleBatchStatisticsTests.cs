using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Text;
using NUnit.Framework;
using Tangram.Data;

namespace Tangram.Data.Tests
{
    /// <summary>
    /// 批次統計（issue #26）。
    ///
    /// <b>這不是功能，是一次量測。</b>它要回答的只有一個問題：
    /// <b>難度特徵在真實的題目池裡到底分不分得開。</b>
    ///
    /// 為什麼要現在問：難度系統是兩層——<see cref="DifficultyFeatures"/> 量出原始數字，
    /// <c>DifficultyEvaluator</c> 加權換算成 1200 為中心的尺度。
    /// 第二層沒辦法憑空做，因為<b>權重要看過一批題目的分布才定得出來</b>。
    /// 而且如果某個特徵在一千題裡幾乎是常數，要回頭改的是<b>特徵（換一個）</b>而不是權重——
    /// 權重乘上一個常數等於白給一個特徵。
    ///
    /// ⚠️ <b>現在回頭改特徵很便宜</b>（出題器已經能量產題目，改完重跑就好），
    /// 等權重定完、照難度出題、串進遊戲之後才發現就要拆一整串。
    ///
    /// ⚠️ <b>掛 [Explicit] 是刻意的</b>：一千題約兩千次呼叫、數十秒。
    /// 而「動到 Data 層一定要重跑全部測試」是這個專案的紀律——
    /// 每次 Run All 都多等半分鐘會讓人開始跳過測試，那個代價比這份報告大。
    /// 要跑：Unity 測試視窗裡單獨點它，或
    /// <c>dotnet test Tools/DotnetTests -- "NUnit.Where=name == BatchStatistics"</c>（⚠️ 不能用 <c>--filter</c>，見 <c>文件/開發須知.md</c>〈怎麼跑測試〉）。
    /// </summary>
    public class PuzzleBatchStatisticsTests
    {
        /// <summary>固定種子。<b>報告裡要寫明它</b>，不然數字重現不了。</summary>
        private const int Seed = 20260910;

        /// <summary>要拿滿幾題。</summary>
        private const int WantedPuzzleCount = 1000;

        /// <summary>呼叫次數的硬上限。只防病態情況，正常大約兩千次就滿了。</summary>
        private const int MaximumAttempts = 40000;

        /// <summary>
        /// 判「有沒有鑑別力」的門檻：一千題裡至少要出現這麼多種相異值。
        ///
        /// ⚠️ <b>這個數字事先訂死</b>，不是看到分布才回頭填的——否則結論會變成事後合理化。
        /// 訂得很寬（只有 3）是因為它抓的不是「夠不夠好用」而是「是不是根本沒在動」。
        /// </summary>
        private const int MinimumDistinctValuesForDiscrimination = 3;

        /// <summary>一次拼得起來的嘗試量到的東西。拼不起來的不會有這個。</summary>
        private readonly struct MeasuredAttempt
        {
            /// <summary>第幾次呼叫（從 0 起算），寫進 CSV 用。</summary>
            public readonly int AttemptIndex;

            /// <summary>這次的結果：生出來、被審美篩掉、還是重複。</summary>
            public readonly GenerationOutcome Outcome;

            /// <summary>這個形狀的五個原始特徵。</summary>
            public readonly DifficultyFeatures Features;

            /// <summary>外框周長，由恆等式反解（見 <see cref="PerimeterOf"/>）。</summary>
            public readonly Scalar Perimeter;

            /// <param name="attemptIndex">第幾次呼叫。</param>
            /// <param name="outcome">這次的結果。</param>
            /// <param name="features">形狀的特徵。</param>
            /// <param name="perimeter">外框周長。</param>
            public MeasuredAttempt(int attemptIndex, GenerationOutcome outcome,
                DifficultyFeatures features, Scalar perimeter)
            {
                AttemptIndex = attemptIndex;
                Outcome = outcome;
                Features = features;
                Perimeter = perimeter;
            }
        }

        /// <summary>
        /// 外框周長。<see cref="DifficultyFeatures"/> 沒有這個欄位，因為它是那條恆等式的反解：
        /// 七片周長總和 = 外框周長 + 2 × 藏起來的長度。
        ///
        /// ⚠️ 它要單獨列進報告，因為它<b>同時是審美門檻與難度特徵</b>——
        /// 審美篩的是緊湊度（面積恆為 32，所以就是篩周長），而「藏起來的邊」是它的另一面。
        /// 篩太兇會把難度的變化範圍一起削掉，那件事只有把周長攤出來才看得見。
        /// </summary>
        /// <param name="features">形狀的特徵。</param>
        /// <returns>外框周長。</returns>
        private static Scalar PerimeterOf(DifficultyFeatures features)
        {
            return DifficultyFeatures.TotalPiecePerimeter - features.DoubledHiddenEdgeLength;
        }

        /// <summary>
        /// <b>issue #26 的完成定義</b>：生一千題，把重複率、三個特徵的分布、
        /// 審美篩掉幾成、平均生成時間四樣東西輸出。
        ///
        /// ⚠️ <b>刻意只斷言「特徵有沒有在動」，不斷言分布長什麼樣。</b>
        /// 那些數字現在還沒有「應該是多少」的根據，硬訂門檻只會變成每次調參都要跟著改的假警報。
        /// 剩下的工作是把數字印出來給人看。
        /// </summary>
        [Test, Explicit("批次統計：跑一千題要數十秒，不進日常的 Run All")]
        public void 批次統計_一千題的特徵分布量得出來()
        {
            var generator = new PuzzleGenerator(Seed);
            var outcomeCounts = new Dictionary<GenerationOutcome, int>();

            // ⚠️ 兩個池子要分開。未篩選池含被審美丟掉的與重複的，篩選後池只有真的生出來的。
            //
            // 為什麼一定要兩個：「特徵沒鑑別力」與「門檻切太兇」在單一池子裡長得一模一樣，
            // 而它們的處置完全相反——前者要換特徵，後者只要改審美篩選的門檻。
            // 兩個池子並排才分得出來：都窄 = 特徵的錯，未篩選寬而篩選後窄 = 門檻的錯。
            var unfilteredPool = new List<MeasuredAttempt>();
            var acceptedPool = new List<MeasuredAttempt>();

            var stopwatch = Stopwatch.StartNew();
            int totalAttempts = 0;

            while (acceptedPool.Count < WantedPuzzleCount && totalAttempts < MaximumAttempts)
            {
                GenerationResult result = generator.TryGenerate();
                int attemptIndex = totalAttempts;
                totalAttempts++;

                outcomeCounts.TryGetValue(result.Outcome, out int count);
                outcomeCounts[result.Outcome] = count + 1;

                // ⚠️ 拼不起來的一定要排除：那時候 Features 是 default，五個欄位全是 0。
                // 混進分布會在每個特徵的低端塞一坨假的 0，然後看起來像「凹角常常是 0」
                if (result.Outcome == GenerationOutcome.PackingFailed) continue;

                var measured = new MeasuredAttempt(
                    attemptIndex, result.Outcome, result.Features, PerimeterOf(result.Features));

                unfilteredPool.Add(measured);
                if (result.Outcome == GenerationOutcome.Generated) acceptedPool.Add(measured);
            }

            stopwatch.Stop();

            ReportBatchOverview(outcomeCounts, totalAttempts, acceptedPool.Count, stopwatch.ElapsedMilliseconds);
            ReportDuplicateRate(outcomeCounts);
            ReportFeatureDistributions(unfilteredPool, acceptedPool);
            ReportCsvPath(unfilteredPool);

            Assert.AreEqual(WantedPuzzleCount, acceptedPool.Count,
                $"呼叫了 {totalAttempts} 次還沒拿滿 {WantedPuzzleCount} 題");
            Assert.GreaterOrEqual(unfilteredPool.Count, acceptedPool.Count,
                "未篩選池不可能比篩選後池小——兩個池子沒有真的分開");

            AssertFeatureDiscriminates("凹角數", DistinctConcaveAngleCounts(acceptedPool));
            AssertFeatureDiscriminates("邊方向種類", DistinctDirectionVarietyCounts(acceptedPool));
            AssertFeatureDiscriminates("藏起來的邊長", DistinctHiddenEdgeLengths(acceptedPool));
        }

        // ─────────────────────────────────────────────────────────────
        // 一、批次概況
        // ─────────────────────────────────────────────────────────────

        /// <param name="outcomeCounts">四種結果各出現幾次。</param>
        /// <param name="totalAttempts">總共呼叫幾次。</param>
        /// <param name="acceptedCount">拿到幾題。</param>
        /// <param name="elapsedMilliseconds">總耗時。</param>
        private static void ReportBatchOverview(Dictionary<GenerationOutcome, int> outcomeCounts,
            int totalAttempts, int acceptedCount, long elapsedMilliseconds)
        {
            TestContext.WriteLine("══════ 批次統計 ══════");
            TestContext.WriteLine($"種子 {Seed}　目標 {WantedPuzzleCount} 題");
            TestContext.WriteLine("");
            TestContext.WriteLine("【一、批次概況】");
            TestContext.WriteLine($"  總呼叫次數　{totalAttempts}");

            foreach (GenerationOutcome outcome in Enum.GetValues(typeof(GenerationOutcome)))
            {
                outcomeCounts.TryGetValue(outcome, out int count);
                TestContext.WriteLine(
                    $"    {outcome,-22}{count,6}　（{(double)count / totalAttempts:P1}）");
            }

            double millisecondsPerAttempt = (double)elapsedMilliseconds / totalAttempts;
            double millisecondsPerPuzzle = acceptedCount > 0
                ? (double)elapsedMilliseconds / acceptedCount
                : 0;

            TestContext.WriteLine($"  總耗時　　　{elapsedMilliseconds} ms");
            TestContext.WriteLine($"  平均一次呼叫　{millisecondsPerAttempt:F1} ms");
            TestContext.WriteLine($"  ⭐ 平均拿到一題　{millisecondsPerPuzzle:F1} ms");
            TestContext.WriteLine("     ⚠️「指定難度 → 當場生成」在乎的是這個數字，");
            TestContext.WriteLine("        而且它還要再乘一次難度篩選的命中率，所以這是樂觀下限。");
            TestContext.WriteLine("");
        }

        // ─────────────────────────────────────────────────────────────
        // 二、重複率
        // ─────────────────────────────────────────────────────────────

        /// <summary>
        /// ⚠️ <b>重複率的分母不是總呼叫次數。</b>
        ///
        /// 出題器的順序是「拼 → 算特徵 → 審美 → 正規化 → 去重」，去重刻意排在審美後面
        /// （正規化要跑 16 次輪廓追蹤，很貴，先把醜的丟掉省那筆錢）。
        /// 所以量得到的是「<b>通過審美的裡面</b>有幾成重複」，
        /// 未篩選池的重複率量不到——除非對每個形狀都付那 16 次追蹤。
        /// 這裡選擇不付，因為重複率是拿來判斷「隨機性夠不夠散」，篩選後才是在乎的池子。
        /// </summary>
        /// <param name="outcomeCounts">四種結果各出現幾次。</param>
        private static void ReportDuplicateRate(Dictionary<GenerationOutcome, int> outcomeCounts)
        {
            outcomeCounts.TryGetValue(GenerationOutcome.Generated, out int generatedCount);
            outcomeCounts.TryGetValue(GenerationOutcome.Duplicate, out int duplicateCount);
            int passedAesthetics = generatedCount + duplicateCount;

            TestContext.WriteLine("【二、重複率】");
            TestContext.WriteLine($"  分母是「通過審美的次數」＝ 生出來 {generatedCount} + 重複 {duplicateCount} = {passedAesthetics}");

            if (passedAesthetics > 0)
                TestContext.WriteLine($"  重複率　{(double)duplicateCount / passedAesthetics:P2}");

            TestContext.WriteLine("  ⚠️ 未篩選池的重複率量不到（去重排在審美後面，見這支的 XML doc）");
            TestContext.WriteLine("");
        }

        // ─────────────────────────────────────────────────────────────
        // 三、特徵分布
        // ─────────────────────────────────────────────────────────────

        /// <param name="unfilteredPool">未篩選池。</param>
        /// <param name="acceptedPool">篩選後池。</param>
        private static void ReportFeatureDistributions(
            List<MeasuredAttempt> unfilteredPool, List<MeasuredAttempt> acceptedPool)
        {
            TestContext.WriteLine("【三、特徵分布】");
            TestContext.WriteLine($"  未篩選池 {unfilteredPool.Count} 筆（拼得起來的全部，含被審美丟掉與重複的）");
            TestContext.WriteLine($"  篩選後池 {acceptedPool.Count} 筆（玩家實際會碰到的）");
            TestContext.WriteLine("");

            // ⚠️ 五個欄位裡只有三個是難度特徵。捏點與洞的角色是審美門檻，
            // 它們在篩選後池裡被壓在門檻以下是設計預期，不是「特徵沒鑑別力」。
            // 這件事在報告裡分清楚，否則會誤判特徵要重做
            //
            // ⚠️ 門檻的數字一律跟 AestheticLimits.Default 拿，不要自己抄一份——
            // 抄過來的話改門檻時這裡會安靜地變成錯的說明
            // （第一版就抄了「洞 = 0，篩選後必然全是 0」，門檻放寬成 1 之後那句就成了假的）
            AestheticLimits limits = AestheticLimits.Default;

            ReportIntegerFeature("凹角數",
                $"難度特徵，同時是審美門檻（≤{limits.MaximumConcaveAngleCount}）",
                ConcaveAngleCountsOf(unfilteredPool), ConcaveAngleCountsOf(acceptedPool));

            ReportIntegerFeature("邊方向種類", "難度特徵，沒有門檻",
                DirectionVarietyCountsOf(unfilteredPool), DirectionVarietyCountsOf(acceptedPool));

            ReportPerimeter(unfilteredPool, acceptedPool);

            ReportIntegerFeature("捏點數", $"審美門檻（≤{limits.MaximumPinchPointCount}）",
                PinchPointCountsOf(unfilteredPool), PinchPointCountsOf(acceptedPool));

            ReportIntegerFeature("洞數", $"審美門檻（≤{limits.MaximumHoleCount}）",
                HoleCountsOf(unfilteredPool), HoleCountsOf(acceptedPool));
        }

        /// <summary>
        /// 印一個整數特徵的直方圖，兩個池子並排。
        ///
        /// ⚠️ <b>用直方圖不用平均數</b>：這四個量的值域都很小（實測 0 ～ 11），
        /// 而要問的是「分不分得開」不是「大概多少」。平均數在這裡沒有資訊量。
        /// </summary>
        /// <param name="title">特徵名稱。</param>
        /// <param name="role">這個量在系統裡的角色，決定「幾乎是常數」該怎麼解讀。</param>
        /// <param name="unfilteredValues">未篩選池的值。</param>
        /// <param name="acceptedValues">篩選後池的值。</param>
        private static void ReportIntegerFeature(string title, string role,
            List<int> unfilteredValues, List<int> acceptedValues)
        {
            var unfilteredHistogram = HistogramOf(unfilteredValues);
            var acceptedHistogram = HistogramOf(acceptedValues);

            var allValues = new SortedSet<int>();
            foreach (KeyValuePair<int, int> entry in unfilteredHistogram) allValues.Add(entry.Key);
            foreach (KeyValuePair<int, int> entry in acceptedHistogram) allValues.Add(entry.Key);

            TestContext.WriteLine($"  ── {title}（{role}）");
            TestContext.WriteLine("     值　│　未篩選　　　　│　篩選後");

            foreach (int value in allValues)
            {
                unfilteredHistogram.TryGetValue(value, out int unfilteredCount);
                acceptedHistogram.TryGetValue(value, out int acceptedCount);

                string unfilteredShare = unfilteredValues.Count > 0
                    ? ((double)unfilteredCount / unfilteredValues.Count).ToString("P1", CultureInfo.InvariantCulture)
                    : "—";
                string acceptedShare = acceptedValues.Count > 0
                    ? ((double)acceptedCount / acceptedValues.Count).ToString("P1", CultureInfo.InvariantCulture)
                    : "—";

                TestContext.WriteLine(
                    $"     {value,3}　│　{unfilteredCount,6}　{unfilteredShare,7}　│　{acceptedCount,6}　{acceptedShare,7}");
            }

            TestContext.WriteLine(
                $"     相異值個數：未篩選 {unfilteredHistogram.Count}　篩選後 {acceptedHistogram.Count}" +
                $"（判準：篩選後 < {MinimumDistinctValuesForDiscrimination} 就是沒在動）");
            TestContext.WriteLine("");
        }

        /// <summary>
        /// 印周長的分位數。它是 <see cref="Scalar"/>（n + m√2），不是整數，所以不列直方圖。
        ///
        /// ⚠️ 排序與分位數要轉 double 才算得了，但<b>只在輸出這一層轉</b>——
        /// 浮點永遠不進模型（鐵則一）。
        /// </summary>
        /// <param name="unfilteredPool">未篩選池。</param>
        /// <param name="acceptedPool">篩選後池。</param>
        private static void ReportPerimeter(
            List<MeasuredAttempt> unfilteredPool, List<MeasuredAttempt> acceptedPool)
        {
            List<Scalar> unfilteredPerimeters = SortedPerimetersOf(unfilteredPool);
            List<Scalar> acceptedPerimeters = SortedPerimetersOf(acceptedPool);

            TestContext.WriteLine("  ── 外框周長／藏起來的邊長（難度特徵，同時是審美門檻）");
            TestContext.WriteLine("     ⚠️ 這一列是這張卡最該盯的：篩選切的就是它。");
            TestContext.WriteLine("        未篩選寬而篩選後窄 = 門檻削掉了難度的變化範圍，那要改的是審美篩選，不是特徵。");
            TestContext.WriteLine("     　　　　│　最小　　25%　　中位　　75%　　90%　　最大　相異值");
            TestContext.WriteLine($"     未篩選　│{QuantileRowOf(unfilteredPerimeters)}");
            TestContext.WriteLine($"     篩選後　│{QuantileRowOf(acceptedPerimeters)}");
            TestContext.WriteLine("     參考：古典大正方形 22.6（下限附近）／七片排成一長串 68.3（極端）");
            TestContext.WriteLine("");
        }

        /// <param name="sortedPerimeters">已排序的周長。</param>
        /// <returns>六個分位數加相異值個數，排成一行。</returns>
        private static string QuantileRowOf(List<Scalar> sortedPerimeters)
        {
            if (sortedPerimeters.Count == 0) return "　（空）";

            var distinct = new HashSet<Scalar>();
            foreach (Scalar perimeter in sortedPerimeters) distinct.Add(perimeter);

            return $"　{QuantileOf(sortedPerimeters, 0.00),6:F1}" +
                   $"　{QuantileOf(sortedPerimeters, 0.25),6:F1}" +
                   $"　{QuantileOf(sortedPerimeters, 0.50),6:F1}" +
                   $"　{QuantileOf(sortedPerimeters, 0.75),6:F1}" +
                   $"　{QuantileOf(sortedPerimeters, 0.90),6:F1}" +
                   $"　{QuantileOf(sortedPerimeters, 1.00),6:F1}" +
                   $"　{distinct.Count,6}";
        }

        /// <param name="sortedValues">已排序的值。</param>
        /// <param name="fraction">要第幾分位，0 ～ 1。</param>
        /// <returns>那個分位的值。取最接近的較低索引，報告用不需要內插。</returns>
        private static double QuantileOf(List<Scalar> sortedValues, double fraction)
        {
            int index = (int)(fraction * (sortedValues.Count - 1));
            return sortedValues[index].ToDouble();
        }

        // ─────────────────────────────────────────────────────────────
        // 四、CSV
        // ─────────────────────────────────────────────────────────────

        /// <summary>
        /// 把每一筆原始資料寫成 CSV。
        ///
        /// 為什麼要留原始資料：P2 收尾時要再跑一次同一份東西，那次的輸出直接當系統概述文件的圖。
        /// 有 CSV 就不用為了畫圖重跑一次。
        ///
        /// ⚠️ 寫檔失敗<b>不讓測試變紅</b>——它是附帶產物，報告本身已經印在 log 裡了。
        /// </summary>
        /// <param name="unfilteredPool">未篩選池（要留的就是這個，篩選後池是它的子集）。</param>
        private static void ReportCsvPath(List<MeasuredAttempt> unfilteredPool)
        {
            string path = Path.Combine(Path.GetTempPath(), "tangram_p2_5_batch.csv");
            var text = new StringBuilder();

            text.AppendLine("呼叫序號,結果,凹角數,邊方向種類,捏點數,洞數,藏起來的邊長x2有理部,藏起來的邊長x2根號2係數,外框周長");

            foreach (MeasuredAttempt attempt in unfilteredPool)
            {
                DifficultyFeatures features = attempt.Features;
                text.Append(attempt.AttemptIndex).Append(',')
                    .Append(attempt.Outcome).Append(',')
                    .Append(features.ConcaveAngleCount).Append(',')
                    .Append(features.DirectionVarietyCount).Append(',')
                    .Append(features.PinchPointCount).Append(',')
                    .Append(features.HoleCount).Append(',')
                    .Append(features.DoubledHiddenEdgeLength.RationalPart).Append(',')
                    .Append(features.DoubledHiddenEdgeLength.Root2Coefficient).Append(',')
                    .Append(attempt.Perimeter.ToDouble().ToString("F4", CultureInfo.InvariantCulture))
                    .AppendLine();
            }

            TestContext.WriteLine("【四、原始資料】");
            try
            {
                // 帶 BOM 的 UTF-8：不帶的話 Excel 會把中文表頭讀成亂碼
                File.WriteAllText(path, text.ToString(), new UTF8Encoding(true));
                TestContext.WriteLine($"  {unfilteredPool.Count} 筆已寫到 {path}");
            }
            catch (IOException error)
            {
                TestContext.WriteLine($"  ⚠️ CSV 寫不出去（{error.Message}），報告本身不受影響");
            }
            catch (UnauthorizedAccessException error)
            {
                TestContext.WriteLine($"  ⚠️ CSV 寫不出去（{error.Message}），報告本身不受影響");
            }
            TestContext.WriteLine("");
        }

        // ─────────────────────────────────────────────────────────────
        // 五、斷言
        // ─────────────────────────────────────────────────────────────

        /// <summary>
        /// <b>issue #26 的完成定義第二條</b>：三個難度特徵都要有變化。
        ///
        /// ⚠️ 這條紅了不代表程式壞了，代表<b>那個特徵不能用</b>——
        /// 處置是回頭換一個特徵，不是調權重。
        /// </summary>
        /// <param name="title">特徵名稱。</param>
        /// <param name="distinctValueCount">篩選後池裡的相異值個數。</param>
        private static void AssertFeatureDiscriminates(string title, int distinctValueCount)
        {
            Assert.GreaterOrEqual(distinctValueCount, MinimumDistinctValuesForDiscrimination,
                $"「{title}」在 {WantedPuzzleCount} 題裡只有 {distinctValueCount} 種值，" +
                "幾乎是常數 → 要改的是特徵（換一個），不是權重");
        }

        // ─────────────────────────────────────────────────────────────
        // 六、取值與統計的小工具
        //     ⚠️ 專案不用 Linq，所以這些是手寫的迴圈
        // ─────────────────────────────────────────────────────────────

        /// <param name="values">一串值。</param>
        /// <returns>每個值各出現幾次，依值排序。</returns>
        private static SortedDictionary<int, int> HistogramOf(List<int> values)
        {
            var histogram = new SortedDictionary<int, int>();
            foreach (int value in values)
            {
                histogram.TryGetValue(value, out int count);
                histogram[value] = count + 1;
            }
            return histogram;
        }

        /// <param name="pool">一個池子。</param>
        /// <returns>池子裡每一筆的凹角數。</returns>
        private static List<int> ConcaveAngleCountsOf(List<MeasuredAttempt> pool)
        {
            var values = new List<int>(pool.Count);
            foreach (MeasuredAttempt attempt in pool) values.Add(attempt.Features.ConcaveAngleCount);
            return values;
        }

        /// <param name="pool">一個池子。</param>
        /// <returns>池子裡每一筆的邊方向種類數。</returns>
        private static List<int> DirectionVarietyCountsOf(List<MeasuredAttempt> pool)
        {
            var values = new List<int>(pool.Count);
            foreach (MeasuredAttempt attempt in pool) values.Add(attempt.Features.DirectionVarietyCount);
            return values;
        }

        /// <param name="pool">一個池子。</param>
        /// <returns>池子裡每一筆的捏點數。</returns>
        private static List<int> PinchPointCountsOf(List<MeasuredAttempt> pool)
        {
            var values = new List<int>(pool.Count);
            foreach (MeasuredAttempt attempt in pool) values.Add(attempt.Features.PinchPointCount);
            return values;
        }

        /// <param name="pool">一個池子。</param>
        /// <returns>池子裡每一筆的洞數。</returns>
        private static List<int> HoleCountsOf(List<MeasuredAttempt> pool)
        {
            var values = new List<int>(pool.Count);
            foreach (MeasuredAttempt attempt in pool) values.Add(attempt.Features.HoleCount);
            return values;
        }

        /// <param name="pool">一個池子。</param>
        /// <returns>池子裡每一筆的周長，由小到大排好。</returns>
        private static List<Scalar> SortedPerimetersOf(List<MeasuredAttempt> pool)
        {
            var values = new List<Scalar>(pool.Count);
            foreach (MeasuredAttempt attempt in pool) values.Add(attempt.Perimeter);
            values.Sort();
            return values;
        }

        /// <param name="pool">一個池子。</param>
        /// <returns>凹角數有幾種相異值。</returns>
        private static int DistinctConcaveAngleCounts(List<MeasuredAttempt> pool)
        {
            var distinct = new HashSet<int>();
            foreach (MeasuredAttempt attempt in pool) distinct.Add(attempt.Features.ConcaveAngleCount);
            return distinct.Count;
        }

        /// <param name="pool">一個池子。</param>
        /// <returns>邊方向種類數有幾種相異值。</returns>
        private static int DistinctDirectionVarietyCounts(List<MeasuredAttempt> pool)
        {
            var distinct = new HashSet<int>();
            foreach (MeasuredAttempt attempt in pool) distinct.Add(attempt.Features.DirectionVarietyCount);
            return distinct.Count;
        }

        /// <param name="pool">一個池子。</param>
        /// <returns>藏起來的邊長有幾種相異值。</returns>
        private static int DistinctHiddenEdgeLengths(List<MeasuredAttempt> pool)
        {
            var distinct = new HashSet<Scalar>();
            foreach (MeasuredAttempt attempt in pool) distinct.Add(attempt.Features.DoubledHiddenEdgeLength);
            return distinct.Count;
        }
    }
}
