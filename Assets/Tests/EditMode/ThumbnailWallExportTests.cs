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
    /// 那 2000 個形狀就是<see cref="BaselineBatch">基準批次</see>，生法（為什麼用全開的門檻）寫在那裡。
    ///
    /// ⭐ <b>權重檔也一起塞進資料</b>（2026-09-13 起）：網頁上「暫定難度」的預設權重讀的是它，
    /// 不再在 JS 裡另寫一份——那是權重的第三份拷貝。
    ///
    /// ⚠️ <b>掛 [Explicit]</b>：跑 2000 個形狀要數十秒，不進日常的 Run All。要跑：
    /// <c>dotnet test Tools/DotnetTests --filter "FullyQualifiedName~ThumbnailWallExportTests"</c>。
    /// </summary>
    public class ThumbnailWallExportTests
    {
        /// <summary>輸出檔名，放在系統暫存資料夾。</summary>
        private const string OutputFileName = "tangram_thumbnail_wall.json";

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
            DifficultyWeights weights = RepositoryFiles.LoadDifficultyWeights();
            AestheticLimits defaultLimits = AestheticLimits.Default;

            BaselineBatch batch = BaselineBatch.Generate();
            List<GenerationResult> shapes = batch.Shapes;

            string json = BuildJson(shapes, defaultLimits, weights);
            string path = Path.Combine(Path.GetTempPath(), OutputFileName);
            WriteChecked(path, json);

            // 順便把資料塞進頁面範本，產出可以直接發布的網頁。
            // ⚠️ 原本這一步是一支 PowerShell 腳本，但使用者的電腦執行原則不允許跑 .ps1——
            // 那是對方的安全設定，不該由工具繞過；而且換一台電腦照步驟做一樣會卡住。
            // 併進這裡之後，整個流程就只剩「跑這支測試」一步
            string pagePath = Path.Combine(Path.GetTempPath(), PageFileName);
            WriteChecked(pagePath, BuildPage(json));

            ReportSummary(shapes, defaultLimits, batch.Attempts, batch.OutcomeCounts, path, json.Length);
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
            string template = File.ReadAllText(RepositoryFiles.Find(TemplateRelativePath), new UTF8Encoding(false));
            Assert.IsTrue(template.Contains(DataPlaceholder), $"範本裡找不到佔位字串 {DataPlaceholder}");

            // 資料放在 <script> 裡：把 "</" 跳脫掉，資料裡就算出現 </script> 也不會把標籤提早關掉
            return template.Replace(DataPlaceholder, json.Replace("</", "<\\/"));
        }

        // ─────────────────────────────────────────────────────────────
        // JSON
        //   ⚠️ 手寫，不用 System.Text.Json：這支測試也會被 Unity 編譯，Unity 的設定檔裡沒有它
        //   數字一律用 InvariantCulture——系統地區是中文時小數點不會變成別的符號，但千分位之類的設定照樣會咬人
        // ─────────────────────────────────────────────────────────────

        /// <param name="shapes">生出來的形狀。</param>
        /// <param name="defaultLimits">拿來判「收不收」的門檻。</param>
        /// <param name="weights">權重檔，算正式難度用，也整份塞進資料給網頁當預設權重。</param>
        /// <returns>整份 JSON。</returns>
        private static string BuildJson(List<GenerationResult> shapes, AestheticLimits defaultLimits,
                                        DifficultyWeights weights)
        {
            var text = new StringBuilder(shapes.Count * 900);
            text.Append("{\"seed\":").Append(BaselineBatch.Seed)
                .Append(",\"shapeCount\":").Append(shapes.Count)
                .Append(",\"defaultLimits\":{")
                .Append("\"maximumHoleCount\":").Append(defaultLimits.MaximumHoleCount)
                .Append(",\"maximumPinchPointCount\":").Append(defaultLimits.MaximumPinchPointCount)
                .Append(",\"maximumConcaveAngleCount\":").Append(defaultLimits.MaximumConcaveAngleCount)
                .Append(",\"maximumPerimeter\":").Append(Number(defaultLimits.MaximumPerimeter.ToDouble()))
                .Append('}');

            // 權重檔：{ "perimeter": { "weight": 0.35, "direction": -1 }, ... }。
            // 網頁只要權重與方向——平均與標準差它自己拿當下那一池現算（那是探索用的暫定難度，刻意如此）
            text.Append(",\"difficultyWeights\":{");
            for (int termIndex = 0; termIndex < weights.terms.Length; termIndex++)
            {
                DifficultyTermWeight term = weights.terms[termIndex];
                if (termIndex > 0) text.Append(',');
                text.Append('"').Append(term.key).Append("\":{\"weight\":")
                    .Append(term.weight.ToString("R", CultureInfo.InvariantCulture))
                    .Append(",\"direction\":").Append(term.direction).Append('}');
            }
            text.Append('}');

            text.Append(",\"shapes\":[");
            for (int index = 0; index < shapes.Count; index++)
            {
                if (index > 0) text.Append(',');
                AppendShape(text, index, shapes[index], defaultLimits, weights);
            }

            text.Append("]}");
            return text.ToString();
        }

        /// <param name="text">寫到這裡。</param>
        /// <param name="index">第幾個形狀（0 起算），牆上用來指認某一張。</param>
        /// <param name="shape">生成器的結果（必定是 Generated）。</param>
        /// <param name="defaultLimits">拿來判「收不收」的門檻。</param>
        /// <param name="weights">權重檔，算正式難度用。</param>
        private static void AppendShape(StringBuilder text, int index, GenerationResult shape,
                                        AestheticLimits defaultLimits, DifficultyWeights weights)
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
                .Append(",\"cornerCount\":").Append(features.CornerCount)
                .Append(",\"longHypotenuseEdgeCount\":").Append(LongHypotenuseEdgeCountOf(shape.Puzzle.Walks));
            string swap = EquivalentSwapOf(shape.ReferenceSolution);
            text.Append(",\"equivalentSwap\":\"").Append(swap).Append('"')
                .Append(",\"hasEquivalentSwap\":").Append(swap == NoSwap ? 0 : 1)
                // 填滿率 = 面積 ÷ 凸包面積。資料檔給網頁看，所以這裡才轉成小數；門檻判斷仍然是整數比較
                .Append(",\"convexHullFillRatio\":").Append(Number(
                    DifficultyFeatures.ExpectedDoubleArea.ToDouble() / features.ConvexHullDoubleArea.ToDouble()));

            // ⚠️ 從解算的那幾個都問 SolutionFeatures，不要在這裡再寫一份——
            // 牆上看到的數字必須跟遊戲裡算難度用的是同一份實作，不然調完權重才發現兩邊不一樣
            SolutionFeatures solution = SolutionFeatures.Measure(shape.ReferenceSolution);
            text.Append(",\"fullEdgeContactCount\":").Append(solution.FullEdgeContactCount)
                .Append(",\"staggeredEdgeContactCount\":").Append(solution.StaggeredEdgeContactCount)
                .Append(",\"keyPiecesExposingOneVertex\":").Append(solution.KeyPiecesExposingOneVertex)
                .Append(",\"largeTriangleUnion\":\"").Append(solution.LargeTriangleUnion).Append('"')
                .Append(",\"largeTrianglesJoined\":").Append(solution.LargeTrianglesJoined);

            List<Placement> forcedPieces = DifficultyFeatures.ForcedPiecesOf(shape.Puzzle.Walks);
            text.Append(",\"forcedPieceCount\":").Append(forcedPieces.Count)
                .Append(",\"forcedPieceKinds\":[");
            for (int forcedIndex = 0; forcedIndex < forcedPieces.Count; forcedIndex++)
                text.Append(forcedIndex == 0 ? "" : ",").Append('"').Append(forcedPieces[forcedIndex].Kind).Append('"');
            text.Append(']');

            // 正式的難度值（P2-6 的權重檔）。牆上那個可調權重的「暫定難度」是探索用的，兩者刻意並存：
            // 調權重要看得到「改成這樣會排成什麼樣子」，而這一欄是「現在真的會給玩家的數字」
            text.Append(",\"difficulty\":").Append(DifficultyEvaluator.Score(
                features, forcedPieces.Count, solution, weights));
            Scalar longestEdge = LongestEdgeOf(shape.Puzzle.Walks);
            text.Append(",\"longestEdgeLength\":").Append(Number(longestEdge.ToDouble()))
                .Append(",\"longestEdgeExact\":");
            AppendExactScalar(text, longestEdge);
            text.Append(",\"perimeter\":").Append(Number(perimeter.ToDouble()))
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

                // 這一片在世界座標的頂點（浮點，只給牆上畫分片線用）。跟輪廓同一個座標系——生成器保證參考解與題目一起正規化
                text.Append(",\"vertices\":[");
                ExactVector2[] pieceVertices = piece.Vertices();
                for (int vertexIndex = 0; vertexIndex < pieceVertices.Length; vertexIndex++)
                {
                    if (vertexIndex > 0) text.Append(',');
                    text.Append('[').Append(Number(pieceVertices[vertexIndex].X.ToDouble()))
                        .Append(',').Append(Number(pieceVertices[vertexIndex].Y.ToDouble())).Append(']');
                }
                text.Append("]}");
            }
            text.Append("]}");
        }

        /// <summary>
        /// 輪廓上長度剛好是 4√2 的邊有幾條。<b>探索用</b>（2026-09-11 使用者提議「特殊邊長」）。
        ///
        /// 單片的邊裡只有大三角的斜邊是 4√2，所以它通常是「大三角放這裡」的提示。
        /// ⚠️ 但它也可能是兩片接起來的（例如小三角斜邊 2√2 + 平行四邊形長邊 2√2），那時就不是提示而是陷阱。
        /// 只看輪廓就能算，不需要解。其他長度（2、2√2、4）佔了輪廓邊的七成以上，幾乎每題都有，所以只挑這一個。
        /// </summary>
        /// <param name="walks">正規化後的輪廓環。</param>
        /// <returns>長度剛好 4√2 的輪廓邊數。</returns>
        private static int LongHypotenuseEdgeCountOf(Walk[] walks)
        {
            var longHypotenuse = new Scalar(0, 4);
            int count = 0;
            foreach (Walk walk in walks)
                foreach (Edge edge in walk.Edges)
                    if (edge.Length == longHypotenuse) count++;
            return count;
        }

        /// <summary>
        /// 輪廓上最長的一條直邊。<b>探索用</b>（2026-09-11 使用者提議「最大邊長」）。
        ///
        /// 單片最長的邊是大三角的斜邊 4√2，所以<b>比 4√2 還長的邊一定是兩片以上接起來的</b>。
        /// 剛好 4 或 4√2 的邊可能是一片、也可能是兩片，那兩種不確定。
        /// ⚠️ 捏點處共線的中繼點會把一條直邊切成兩段，要先接回去——所以從一個真正的轉角開始走。
        /// 跟現有特徵幾乎無關（跟周長 0.07、凹角 −0.31、填滿率 −0.02），是目前唯一真正帶來新資訊的候選。
        /// </summary>
        /// <param name="walks">正規化後的輪廓環。</param>
        /// <returns>最長直邊的精確長度。</returns>
        private static Scalar LongestEdgeOf(Walk[] walks)
        {
            Scalar longest = Scalar.Zero;
            foreach (Walk walk in walks)
            {
                Edge[] edges = walk.Edges;
                int count = edges.Length;

                int start = 0;
                for (int index = 0; index < count; index++)
                {
                    if (edges[index].Direction == edges[(index - 1 + count) % count].Direction) continue;
                    start = index;
                    break;
                }

                Scalar straightRun = Scalar.Zero;
                for (int step = 0; step < count; step++)
                {
                    Edge edge = edges[(start + step) % count];
                    Edge previous = edges[(start + step - 1 + count) % count];
                    straightRun = step > 0 && edge.Direction == previous.Direction
                        ? straightRun + edge.Length
                        : edge.Length;
                    if (straightRun > longest) longest = straightRun;
                }
            }
            return longest;
        }

        /// <summary>參考解裡沒有看得出來的等價交換。</summary>
        private const string NoSwap = "None";

        /// <summary>
        /// 參考解裡兩個小三角有沒有剛好拼成另一片的形狀。<b>探索用</b>（2026-09-11 使用者提議「等價交換」）。
        ///
        /// 兩個小三角共用一整條邊時，拼出來的必定是正方形、中三角或平行四邊形之一——
        /// 那一片本身也能被兩個小三角填滿，所以<b>把這一對跟那一片對調就是另一種合法的解</b>。
        /// 有 = 這個形狀保證不只一種解（容錯高）；⚠️ 沒有 ≠ 只有一種解，別種交換沒算進來。
        /// </summary>
        /// <param name="pieces">參考解。</param>
        /// <returns><c>None</c>、<c>Square</c>、<c>MediumTriangle</c> 或 <c>Parallelogram</c>。</returns>
        private static string EquivalentSwapOf(Placement[] pieces)
        {
            var smallTriangles = new List<Placement>(2);
            foreach (Placement piece in pieces)
                if (piece.Kind == PieceKind.SmallTriangle) smallTriangles.Add(piece);
            if (smallTriangles.Count != 2) return NoSwap;

            // 共用兩個頂點 = 共用一整條邊（三角形的任兩個頂點就是一條邊）
            var firstVertices = new HashSet<ExactVector2>(smallTriangles[0].Vertices());
            int shared = 0;
            foreach (ExactVector2 vertex in smallTriangles[1].Vertices())
                if (firstVertices.Contains(vertex)) shared++;
            if (shared != 2) return NoSwap;

            Edge[] edges = BoundaryTracer.Trace(smallTriangles)[0].Edges;
            if (edges.Length == 3) return "MediumTriangle";
            foreach (Edge edge in edges)
                if (edge.Length != Scalar.FromInteger(2)) return "Parallelogram";
            return "Square";
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
                         AestheticRules.ConcaveAngleCount, AestheticRules.Perimeter,
                         AestheticRules.ConvexHullFillRatio
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
                             AestheticRules.ConcaveAngleCount, AestheticRules.Perimeter,
                             AestheticRules.ConvexHullFillRatio
                         })
                {
                    if ((violations & rule) == 0) continue;
                    blockedBy.TryGetValue(rule, out int count);
                    blockedBy[rule] = count + 1;
                }
            }

            TestContext.WriteLine("══════ 縮圖牆資料匯出 ══════");
            TestContext.WriteLine($"種子 {BaselineBatch.Seed}，全開門檻叫了 {attempts} 次，拿到 {shapes.Count} 個拼得起來的形狀");
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
