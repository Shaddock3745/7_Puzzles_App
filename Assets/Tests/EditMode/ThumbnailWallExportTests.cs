using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using NUnit.Framework;
using Tangram.Data;

namespace Tangram.Data.Tests
{
    /// <summary>
    /// 縮圖牆的資料匯出（2026-09-11）。
    ///
    /// <b>這不是功能，是一個開發工具的資料來源。</b>它生出 2000 個形狀，連同輪廓、特徵、
    /// 「預設門檻收不收、被哪一條擋」、參考解一起寫成一個 JSON 檔；
    /// 縮圖牆頁面（<c>Tools/ThumbnailWall/</c>）把那個檔嵌進去發布。
    ///
    /// <b>為什麼用全開的門檻生</b>：生成器對被審美門檻篩掉的形狀只回傳特徵、不回傳輪廓
    /// （輪廓要付正規化那 16 次追邊才有），所以用預設門檻生的話，被擋掉的那些根本畫不出來。
    /// 全開之後每個拼得起來的形狀都會走完正規化；「預設門檻收不收」再用
    /// <see cref="AestheticLimits.Violations"/> 問一次——跟生成器用的是同一個判斷，
    /// <c>PuzzleGeneratorTests.門檻_生成器的判定跟AestheticLimits一致</c> 釘住這件事。
    ///
    /// ⚠️ 全開跑出來的序列跟預設門檻下不完全一樣（去重的集合不同：被擋掉的形狀也會進去重表），
    /// 但實測一千題重複數是 0，實務上是同一批。
    ///
    /// ⚠️ <b>掛 [Explicit]</b>：跑 2000 個形狀要數十秒，不進日常的 Run All。要跑：
    /// <c>dotnet test Tools/DotnetTests --filter "FullyQualifiedName~ThumbnailWallExportTests"</c>。
    /// </summary>
    public class ThumbnailWallExportTests
    {
        /// <summary>固定種子，跟 P2-5 批次統計同一個。</summary>
        private const int Seed = 20260910;

        /// <summary>要拿滿幾個拼得起來的形狀（未篩選池的大小）。</summary>
        private const int WantedShapeCount = 2000;

        /// <summary>呼叫次數的硬上限。只防病態情況。</summary>
        private const int MaximumAttempts = 20000;

        /// <summary>輸出檔名，放在系統暫存資料夾。</summary>
        private const string OutputFileName = "tangram_thumbnail_wall.json";

        /// <summary>
        /// 全開的門檻：什麼都收。
        ///
        /// ⚠️ <b>周長上限不能用 long 的最大值</b>。<see cref="Scalar"/> 比大小是相減之後看正負，
        /// 極端大的值相減會溢位，而且不報錯——結果是「全開」變成「全關」之類的怪行為。
        /// 一百萬遠大於任何可能的周長（七片排成一長串也不到 69），又絕對不會溢位。
        /// 三個整數門檻是直接用 &gt; 比，int 的最大值沒有這個問題。
        /// </summary>
        private static readonly AestheticLimits WideOpenLimits = new AestheticLimits(
            int.MaxValue, int.MaxValue, int.MaxValue, Scalar.FromInteger(1000000));

        /// <summary>
        /// 生 2000 個形狀，寫成縮圖牆要的 JSON。
        ///
        /// ⚠️ <b>寫檔失敗要讓測試變紅</b>（跟 P2-5 批次統計的 CSV 相反）。這個檔就是這支測試存在的全部理由；
        /// 而且 2026-09-11 才撞過一次「CSV 被鎖住、只印一行警告、之後拿到的一直是舊檔」。
        /// 寫完還會回頭比對檔案大小，證明真的寫進去了。
        /// </summary>
        [Test, Explicit("縮圖牆資料匯出：生 2000 個形狀要數十秒，不進日常的 Run All")]
        public void 縮圖牆_匯出兩千個形狀()
        {
            var generator = new PuzzleGenerator(Seed, WideOpenLimits);
            AestheticLimits defaultLimits = AestheticLimits.Default;

            var shapes = new List<GenerationResult>(WantedShapeCount);
            var outcomeCounts = new Dictionary<GenerationOutcome, int>();
            int attempts = 0;

            while (shapes.Count < WantedShapeCount && attempts < MaximumAttempts)
            {
                GenerationResult result = generator.TryGenerate();
                attempts++;
                outcomeCounts.TryGetValue(result.Outcome, out int count);
                outcomeCounts[result.Outcome] = count + 1;

                if (result.Outcome == GenerationOutcome.Generated) shapes.Add(result);
            }

            Assert.AreEqual(WantedShapeCount, shapes.Count, $"呼叫了 {attempts} 次還沒拿滿");
            outcomeCounts.TryGetValue(GenerationOutcome.RejectedByAesthetics, out int rejectedUnderWideOpen);
            Assert.AreEqual(0, rejectedUnderWideOpen, "全開的門檻不應該擋掉任何形狀");

            string json = BuildJson(shapes, defaultLimits);
            string path = Path.Combine(Path.GetTempPath(), OutputFileName);
            WriteChecked(path, json);

            // 順便把資料塞進頁面範本，產出可以直接發布的網頁。
            // ⚠️ 原本這一步是一支 PowerShell 腳本，但使用者的電腦執行原則不允許跑 .ps1——
            // 那是對方的安全設定，不該由工具繞過；而且換一台電腦照步驟做一樣會卡住。
            // 併進這裡之後，整個流程就只剩「跑這支測試」一步
            string pagePath = Path.Combine(Path.GetTempPath(), PageFileName);
            WriteChecked(pagePath, BuildPage(json));

            ReportSummary(shapes, defaultLimits, attempts, outcomeCounts, path, json.Length);
            TestContext.WriteLine($"網頁寫到 {pagePath}");
        }

        /// <summary>組好的網頁檔名，放在系統暫存資料夾，發布時拿這個檔。</summary>
        private const string PageFileName = "tangram_feature_wall.html";

        /// <summary>範本相對於專案根目錄的位置。</summary>
        private static readonly string TemplateRelativePath =
            Path.Combine("Tools", "ThumbnailWall", "ThumbnailWall.template.html");

        /// <summary>範本裡要被資料取代的佔位字串。</summary>
        private const string DataPlaceholder = "/*__WALL_DATA__*/null";

        /// <summary>
        /// 寫檔，寫完回頭比對大小。<b>失敗就讓測試變紅</b>——
        /// 2026-09-11 撞過「CSV 被鎖住、只印一行警告、之後拿到的一直是舊檔」。
        /// </summary>
        /// <param name="path">要寫到哪。</param>
        /// <param name="content">內容。</param>
        private static void WriteChecked(string path, string content)
        {
            byte[] bytes = new UTF8Encoding(false).GetBytes(content);
            File.WriteAllBytes(path, bytes);
            Assert.AreEqual(bytes.Length, new FileInfo(path).Length, $"{path} 的大小對不上，沒有完整寫進去");
        }

        /// <summary>把資料塞進範本。</summary>
        /// <param name="json">整份資料。</param>
        /// <returns>可以直接發布的網頁內容。</returns>
        private static string BuildPage(string json)
        {
            string template = File.ReadAllText(FindTemplate(), new UTF8Encoding(false));
            Assert.IsTrue(template.Contains(DataPlaceholder), $"範本裡找不到佔位字串 {DataPlaceholder}");

            // 資料放在 <script> 裡：把 "</" 跳脫掉，資料裡就算出現 </script> 也不會把標籤提早關掉
            return template.Replace(DataPlaceholder, json.Replace("</", "<\\/"));
        }

        /// <summary>
        /// 找範本：從目前目錄與執行檔目錄往上找，直到看見 <c>Tools/ThumbnailWall/</c>。
        ///
        /// 兩種跑法的起點不一樣——Unity 的 Test Runner 目前目錄是專案根目錄，
        /// <c>dotnet test</c> 的執行檔在 <c>Tools/DotnetTests/bin/...</c>——往上找兩邊都找得到。
        /// </summary>
        /// <returns>範本的完整路徑。</returns>
        private static string FindTemplate()
        {
            foreach (string start in new[] { Directory.GetCurrentDirectory(), AppContext.BaseDirectory })
            {
                for (DirectoryInfo directory = new DirectoryInfo(start); directory != null; directory = directory.Parent)
                {
                    string candidate = Path.Combine(directory.FullName, TemplateRelativePath);
                    if (File.Exists(candidate)) return candidate;
                }
            }

            Assert.Fail($"從 {Directory.GetCurrentDirectory()} 往上找不到 {TemplateRelativePath}");
            return null;
        }

        // ─────────────────────────────────────────────────────────────
        // JSON
        //   ⚠️ 手寫，不用 System.Text.Json：這支測試也會被 Unity 編譯，Unity 的設定檔裡沒有它
        //   數字一律用 InvariantCulture——系統地區是中文時小數點不會變成別的符號，但千分位之類的設定照樣會咬人
        // ─────────────────────────────────────────────────────────────

        /// <param name="shapes">生出來的形狀。</param>
        /// <param name="defaultLimits">拿來判「收不收」的門檻。</param>
        /// <returns>整份 JSON。</returns>
        private static string BuildJson(List<GenerationResult> shapes, AestheticLimits defaultLimits)
        {
            var text = new StringBuilder(shapes.Count * 900);
            text.Append("{\"seed\":").Append(Seed)
                .Append(",\"shapeCount\":").Append(shapes.Count)
                .Append(",\"defaultLimits\":{")
                .Append("\"maximumHoleCount\":").Append(defaultLimits.MaximumHoleCount)
                .Append(",\"maximumPinchPointCount\":").Append(defaultLimits.MaximumPinchPointCount)
                .Append(",\"maximumConcaveAngleCount\":").Append(defaultLimits.MaximumConcaveAngleCount)
                .Append(",\"maximumPerimeter\":").Append(Number(defaultLimits.MaximumPerimeter.ToDouble()))
                .Append("},\"shapes\":[");

            for (int index = 0; index < shapes.Count; index++)
            {
                if (index > 0) text.Append(',');
                AppendShape(text, index, shapes[index], defaultLimits);
            }

            text.Append("]}");
            return text.ToString();
        }

        /// <param name="text">寫到這裡。</param>
        /// <param name="index">第幾個形狀（0 起算），牆上用來指認某一張。</param>
        /// <param name="shape">生成器的結果（必定是 Generated）。</param>
        /// <param name="defaultLimits">拿來判「收不收」的門檻。</param>
        private static void AppendShape(StringBuilder text, int index, GenerationResult shape,
                                        AestheticLimits defaultLimits)
        {
            DifficultyFeatures features = shape.Features;
            AestheticRules violations = defaultLimits.Violations(features);

            // 周長 = 七片周長總和 − 藏起來的長度 ×2（DifficultyFeatures 的恆等式反解，同 AestheticLimits.Violations）
            Scalar perimeter = DifficultyFeatures.TotalPiecePerimeter - features.DoubledHiddenEdgeLength;

            text.Append("{\"index\":").Append(index)
                .Append(",\"key\":\"").Append(shape.Puzzle.Key).Append('"')
                .Append(",\"acceptedByDefault\":").Append(violations == AestheticRules.None ? "true" : "false")
                .Append(",\"violations\":[");
            AppendViolations(text, violations);
            text.Append(']')
                .Append(",\"concaveAngleCount\":").Append(features.ConcaveAngleCount)
                .Append(",\"directionVarietyCount\":").Append(features.DirectionVarietyCount)
                .Append(",\"pinchPointCount\":").Append(features.PinchPointCount)
                .Append(",\"holeCount\":").Append(features.HoleCount)
                .Append(",\"perimeter\":").Append(Number(perimeter.ToDouble()))
                .Append(",\"doubledHiddenEdgeLength\":");
            AppendExactScalar(text, features.DoubledHiddenEdgeLength);

            // 輪廓：正規化後的環，轉成浮點只拿來畫。外環與洞環混在一起，頁面用 evenodd 填色
            text.Append(",\"rings\":[");
            Walk[] walks = shape.Puzzle.Walks;
            for (int walkIndex = 0; walkIndex < walks.Length; walkIndex++)
            {
                if (walkIndex > 0) text.Append(',');
                text.Append('[');
                ExactVector2[] vertices = walks[walkIndex].Vertices();
                for (int vertexIndex = 0; vertexIndex < vertices.Length; vertexIndex++)
                {
                    if (vertexIndex > 0) text.Append(',');
                    text.Append('[').Append(Number(vertices[vertexIndex].X.ToDouble()))
                        .Append(',').Append(Number(vertices[vertexIndex].Y.ToDouble())).Append(']');
                }
                text.Append(']');
            }
            text.Append(']');

            // 參考解：精確座標，給之後挑圖牆「挑中就能變成題目」用。對照牆用不到
            text.Append(",\"referenceSolution\":[");
            Placement[] pieces = shape.ReferenceSolution;
            for (int pieceIndex = 0; pieceIndex < pieces.Length; pieceIndex++)
            {
                Placement piece = pieces[pieceIndex];
                if (pieceIndex > 0) text.Append(',');
                text.Append("{\"kind\":\"").Append(piece.Kind).Append('"')
                    .Append(",\"rotation\":").Append(piece.Rotation)
                    .Append(",\"flipped\":").Append(piece.IsFlipped ? "true" : "false")
                    .Append(",\"x\":");
                AppendExactScalar(text, piece.Translation.X);
                text.Append(",\"y\":");
                AppendExactScalar(text, piece.Translation.Y);
                text.Append('}');
            }
            text.Append("]}");
        }

        /// <summary>沒過的門檻寫成字串陣列，順序固定，全過就是空陣列。</summary>
        /// <param name="text">寫到這裡。</param>
        /// <param name="violations">沒過的規則。</param>
        private static void AppendViolations(StringBuilder text, AestheticRules violations)
        {
            bool first = true;
            foreach (AestheticRules rule in new[]
                     {
                         AestheticRules.HoleCount, AestheticRules.PinchPointCount,
                         AestheticRules.ConcaveAngleCount, AestheticRules.Perimeter
                     })
            {
                if ((violations & rule) == 0) continue;
                if (!first) text.Append(',');
                text.Append('"').Append(rule).Append('"');
                first = false;
            }
        }

        /// <summary>精確的 n + m√2 寫成 [n, m]，不經過浮點。</summary>
        /// <param name="text">寫到這裡。</param>
        /// <param name="value">要寫的值。</param>
        private static void AppendExactScalar(StringBuilder text, Scalar value)
        {
            text.Append('[').Append(value.RationalPart.ToString(CultureInfo.InvariantCulture))
                .Append(',').Append(value.Root2Coefficient.ToString(CultureInfo.InvariantCulture)).Append(']');
        }

        /// <summary>浮點數寫到小數四位。只給顯示用，四位對縮圖綽綽有餘。</summary>
        /// <param name="value">要寫的值。</param>
        /// <returns>JSON 用的數字字串。</returns>
        private static string Number(double value)
            => Math.Round(value, 4).ToString("0.####", CultureInfo.InvariantCulture);

        // ─────────────────────────────────────────────────────────────
        // 摘要
        // ─────────────────────────────────────────────────────────────

        /// <param name="shapes">生出來的形狀。</param>
        /// <param name="defaultLimits">拿來判「收不收」的門檻。</param>
        /// <param name="attempts">總共叫了幾次生成器。</param>
        /// <param name="outcomeCounts">各種結果的次數。</param>
        /// <param name="path">輸出檔路徑。</param>
        /// <param name="byteCount">輸出檔大小。</param>
        private static void ReportSummary(List<GenerationResult> shapes, AestheticLimits defaultLimits,
                                          int attempts, Dictionary<GenerationOutcome, int> outcomeCounts,
                                          string path, int byteCount)
        {
            int accepted = 0;
            var blockedBy = new Dictionary<AestheticRules, int>();
            foreach (GenerationResult shape in shapes)
            {
                AestheticRules violations = defaultLimits.Violations(shape.Features);
                if (violations == AestheticRules.None) { accepted++; continue; }

                foreach (AestheticRules rule in new[]
                         {
                             AestheticRules.HoleCount, AestheticRules.PinchPointCount,
                             AestheticRules.ConcaveAngleCount, AestheticRules.Perimeter
                         })
                {
                    if ((violations & rule) == 0) continue;
                    blockedBy.TryGetValue(rule, out int count);
                    blockedBy[rule] = count + 1;
                }
            }

            TestContext.WriteLine("══════ 縮圖牆資料匯出 ══════");
            TestContext.WriteLine($"種子 {Seed}，全開門檻叫了 {attempts} 次，拿到 {shapes.Count} 個拼得起來的形狀");
            foreach (KeyValuePair<GenerationOutcome, int> entry in outcomeCounts)
                TestContext.WriteLine($"  {entry.Key}：{entry.Value}");
            TestContext.WriteLine($"預設門檻會收的：{accepted}（{(double)accepted / shapes.Count:P1}）");
            TestContext.WriteLine("被擋掉的，依沒過的規則（一個形狀可能同時沒過好幾條）：");
            foreach (KeyValuePair<AestheticRules, int> entry in blockedBy)
                TestContext.WriteLine($"  {entry.Key}：{entry.Value}");
            TestContext.WriteLine($"寫到 {path}（{byteCount / 1024.0:F0} KB）");
        }
    }
}
