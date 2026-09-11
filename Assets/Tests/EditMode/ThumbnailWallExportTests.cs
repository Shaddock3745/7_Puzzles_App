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
                .Append(",\"cornerCount\":").Append(CornerCountOf(shape.Puzzle.Walks))
                .Append(",\"longHypotenuseEdgeCount\":").Append(LongHypotenuseEdgeCountOf(shape.Puzzle.Walks));
            string swap = EquivalentSwapOf(shape.ReferenceSolution);
            text.Append(",\"equivalentSwap\":\"").Append(swap).Append('"')
                .Append(",\"hasEquivalentSwap\":").Append(swap == NoSwap ? 0 : 1)
                .Append(",\"convexHullFillRatio\":").Append(Number(ConvexHullFillRatioOf(shape.Puzzle.Walks)));
            (int fullContacts, int staggeredContacts) = EdgeContactCountsOf(shape.ReferenceSolution);
            text.Append(",\"fullEdgeContactCount\":").Append(fullContacts)
                .Append(",\"staggeredEdgeContactCount\":").Append(staggeredContacts);
            (int[] exposedCounts, int hiddenKeyVertices) = KeyPieceExposureOf(shape.ReferenceSolution);
            int piecesExposingOne = 0;
            foreach (int exposed in exposedCounts)
                if (exposed <= 1) piecesExposingOne++;
            text.Append(",\"keyPieceExposedVertexCounts\":[").Append(string.Join(",", exposedCounts)).Append(']')
                .Append(",\"keyPiecesExposingOneVertex\":").Append(piecesExposingOne)
                .Append(",\"hiddenKeyVertexCount\":").Append(hiddenKeyVertices);
            Dictionary<string, PieceKind> forcedFootprints = ForcedPiecesOf(shape.Puzzle.Walks);
            // 自我檢查：被逼出來的片「在每一種解裡都一樣」，參考解當然也不例外。對不上就是判斷寫錯了
            var referenceFootprints = new HashSet<string>();
            foreach (Placement piece in shape.ReferenceSolution) referenceFootprints.Add(FootprintKeyOf(piece));
            foreach (string footprint in forcedFootprints.Keys)
                if (!referenceFootprints.Contains(footprint))
                    Assert.Fail($"第 {index} 題：被逼出來的 {footprint} 不在參考解裡，ForcedPiecesOf 的判斷有漏洞");
            var forcedPieces = new List<PieceKind>(forcedFootprints.Values);
            string largeTriangleUnion = LargeTriangleUnionOf(shape.ReferenceSolution);
            text.Append(",\"largeTriangleUnion\":\"").Append(largeTriangleUnion).Append('"')
                .Append(",\"largeTrianglesJoined\":").Append(largeTriangleUnion == NotJoined ? 0 : 1);
            text.Append(",\"forcedPieceCount\":").Append(forcedPieces.Count)
                .Append(",\"forcedPieceKinds\":[");
            for (int forcedIndex = 0; forcedIndex < forcedPieces.Count; forcedIndex++)
                text.Append(forcedIndex == 0 ? "" : ",").Append('"').Append(forcedPieces[forcedIndex]).Append('"');
            text.Append(']');
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
        /// 輪廓上真正的轉角數。<b>探索用</b>（2026-09-11 使用者提議「總頂點數」）。
        ///
        /// 七片的頂點加起來固定是 23 個，所以「被藏起來或合併掉的頂點」= 23 − 這個數，兩者是同一件事。
        /// 捏點處共線的中繼點（相鄰兩條邊同方向）不算——玩家看不到那個點。
        ///
        /// ⚠️ <b>還不是正式的難度特徵</b>。P2-6 決定採用的話要搬進 <see cref="DifficultyFeatures"/>，
        /// 並補上「對 16 種整體變換不變」的測試，不要留在這裡——特徵只准有一個家。
        /// ⚠️ 它跟凹角數的相關係數是 0.88（預設門檻收的形狀），而且方向相反：
        /// 凹角多 → 轉角多 → 藏起來的頂點少。放進公式前要先想清楚兩者誰代表「難」。
        /// </summary>
        /// <param name="walks">正規化後的輪廓環。</param>
        /// <returns>所有環的轉角數加總。</returns>
        private static int CornerCountOf(Walk[] walks)
        {
            int corners = 0;
            foreach (Walk walk in walks)
            {
                Edge[] edges = walk.Edges;
                for (int index = 0; index < edges.Length; index++)
                {
                    int incoming = (index - 1 + edges.Length) % edges.Length;
                    if (edges[index].Direction != edges[incoming].Direction) corners++;
                }
            }
            return corners;
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
        /// 凸包填滿率：形狀面積 ÷ 凸包面積。<b>探索用</b>（2026-09-11 使用者提議「越接近圓越難」）。
        ///
        /// 1 = 完全凸；越小代表缺口越大、伸出去的手臂越多。「中間一大坨、只露出幾個小尖角」就是填滿率高。
        /// 凸包用精確座標算（單調鏈），最後才轉成浮點。形狀面積恆為 32。
        /// ⚠️ 跟周長的相關係數 −0.58（預設門檻收的形狀）：面積固定，周長短本來就代表胖，會重疊一部分但沒有到可以互相取代。
        /// </summary>
        /// <param name="walks">正規化後的輪廓環（洞環的頂點一定在凸包裡，一起丟進去無妨）。</param>
        /// <returns>0 到 1 之間的填滿率。</returns>
        private static double ConvexHullFillRatioOf(Walk[] walks)
        {
            var points = new List<ExactVector2>();
            foreach (Walk walk in walks) points.AddRange(walk.Vertices());
            points.Sort((left, right) => left.CompareTo(right));

            // 單調鏈：下半圈與上半圈各走一次，右轉或共線就把上一個點丟掉
            var hull = new List<ExactVector2>();
            for (int pass = 0; pass < 2; pass++)
            {
                int chainStart = hull.Count;
                for (int offset = 0; offset < points.Count; offset++)
                {
                    ExactVector2 point = pass == 0 ? points[offset] : points[points.Count - 1 - offset];
                    while (hull.Count >= chainStart + 2
                           && Geometry.Cross(hull[hull.Count - 2], hull[hull.Count - 1], point).Sign <= 0)
                        hull.RemoveAt(hull.Count - 1);
                    hull.Add(point);
                }
                hull.RemoveAt(hull.Count - 1);   // 終點就是下一半的起點
            }

            double hullDoubleArea = Geometry.SignedDoubleArea(hull).Abs.ToDouble();
            return DifficultyFeatures.ExpectedDoubleArea.ToDouble() / hullDoubleArea;
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

        /// <summary>
        /// 大三角 ×2 與平行四邊形這三片，各自有幾個頂點「露在外面」。<b>探索用</b>（2026-09-11 使用者提議）。
        ///
        /// 藏起來 = 跟別片的某個頂點重合，或落在別片的某條邊上（使用者的定義）；沒藏起來就是露在外面。
        /// <c>Geometry.IsOnSegment</c> 含端點，所以「重合」與「落在邊上」一次就涵蓋。
        /// 使用者的觀察：這三片的頂點都藏起來時明顯變難——大三角的尖角露在外面時，它的位置一眼就看得出來。
        ///
        /// 牆上用的是「<b>只露出一個頂點（或完全沒露）的片數</b>」，0～3——使用者看過「藏起的頂點總數」之後改成這個。
        /// 大三角有 3 個頂點、平行四邊形有 4 個，所以分別是藏起 ≥ 2、≥ 3 個。
        /// 總數仍然一起回報，給檢視窗對照（它的分布是 3～10、多在 5～7，跟其他特徵相關都在 0.21 以內）。
        /// 從解算的，依賴「一題 = 形狀 + 參考解」。
        /// </summary>
        /// <param name="pieces">參考解。</param>
        /// <returns>三片各自露出的頂點數（照參考解的槽順序），以及三片合計被藏起來的頂點數。</returns>
        private static (int[] exposed, int hidden) KeyPieceExposureOf(Placement[] pieces)
        {
            var exposed = new List<int>(3);
            int hidden = 0;
            for (int index = 0; index < pieces.Length; index++)
            {
                if (pieces[index].Kind != PieceKind.LargeTriangle && pieces[index].Kind != PieceKind.Parallelogram) continue;

                int exposedHere = 0;
                foreach (ExactVector2 vertex in pieces[index].Vertices())
                {
                    bool covered = false;
                    for (int other = 0; other < pieces.Length && !covered; other++)
                    {
                        if (other == index) continue;
                        ExactVector2[] otherVertices = pieces[other].Vertices();
                        for (int k = 0; k < otherVertices.Length && !covered; k++)
                            covered = Geometry.IsOnSegment(otherVertices[k], otherVertices[(k + 1) % otherVertices.Length], vertex);
                    }
                    if (covered) hidden++;
                    else exposedHere++;
                }
                exposed.Add(exposedHere);
            }
            return (exposed.ToArray(), hidden);
        }

        /// <summary><see cref="LargeTriangleUnionOf"/> 的「兩片大三角沒有共用整條邊」。</summary>
        private const string NotJoined = "";

        /// <summary>
        /// 參考解裡兩片大三角有沒有<b>共用一整條邊</b>、拼成一大塊。<b>探索用</b>（2026-09-11 使用者在 #219 發現）。
        ///
        /// 使用者的觀察：這種題意外地好拼——兩片大三角合起來是一個大而簡單的形狀，
        /// 看出來之後剩下五片的空間很規整。暫定難度卻把它們排得偏難（平均 1355，其他 1185），
        /// 因為合起來的那塊周長短、轉角少，正好踩中「越緊湊越難」的兩個主力特徵。
        ///
        /// 合起來只有三種可能（大三角是等腰直角三角形）：
        /// · 共用斜邊 → 正方形
        /// · 共用一條股、直角在同一端 → 另外兩條股接成一直線 → 更大的等腰直角三角形
        /// · 共用一條股、直角在兩端 → 平行四邊形
        ///
        /// ⚠️ 只認整條邊完全重合；錯開的貼合不算，那不會變成「一塊」。
        /// ⚠️ 從參考解算的：玩家的解可能沒把它們拼在一起（依賴「一題 = 形狀 + 參考解」）。
        /// </summary>
        /// <param name="pieces">參考解。</param>
        /// <returns><c>"square"</c>、<c>"triangle"</c>、<c>"parallelogram"</c>，沒拼在一起回 <see cref="NotJoined"/>。</returns>
        private static string LargeTriangleUnionOf(Placement[] pieces)
        {
            var largeTriangles = new List<ExactVector2[]>(2);
            foreach (Placement piece in pieces)
                if (piece.Kind == PieceKind.LargeTriangle) largeTriangles.Add(piece.Vertices());
            if (largeTriangles.Count != 2) return NotJoined;

            ExactVector2[] first = largeTriangles[0], second = largeTriangles[1];
            for (int i = 0; i < 3; i++)
            {
                ExactVector2 start = first[i], end = first[(i + 1) % 3];
                for (int j = 0; j < 3; j++)
                {
                    // 兩片都是 CCW，共用的邊在對方那裡是反著走的
                    if (second[j] != end || second[(j + 1) % 3] != start) continue;

                    ExactVector2 firstApex = first[(i + 2) % 3], secondApex = second[(j + 2) % 3];
                    // 對角是直角 ⇔ 共用的是斜邊
                    if (ExactVector2.Dot(start - firstApex, end - firstApex).IsZero) return "square";
                    // 共用股：兩個對角跟某個端點共線 ⇔ 直角在同一端，另外兩條股接成一直線
                    bool straight = Geometry.Cross(firstApex, start, secondApex).IsZero
                                 || Geometry.Cross(firstApex, end, secondApex).IsZero;
                    return straight ? "triangle" : "parallelogram";
                }
            }
            return NotJoined;
        }

        /// <summary>
        /// 五種片 × 8 旋轉 × 2 翻面 = 80 個姿態，平移量 0 時的頂點（CCW）。
        /// 先建好，<see cref="ForcedPiecesOf"/> 對每個角重複用。
        /// </summary>
        private static readonly (PieceKind kind, int rotation, bool flipped, ExactVector2[] vertices)[] AllPoses = BuildAllPoses();

        /// <returns>80 個姿態，順序是片種 → 旋轉 → 翻面。</returns>
        private static (PieceKind, int, bool, ExactVector2[])[] BuildAllPoses()
        {
            var poses = new List<(PieceKind, int, bool, ExactVector2[])>();
            foreach (PieceKind kind in Enum.GetValues(typeof(PieceKind)))
                for (int rotation = 0; rotation < 8; rotation++)
                    for (int flip = 0; flip < 2; flip++)
                        poses.Add((kind, rotation, flip == 1, new Placement(kind, rotation, flip == 1, ExactVector2.Zero).Vertices()));
            return poses.ToArray();
        }

        /// <summary>
        /// 「被外框逼出來的片」：外框上某個凸角，放得進去的擺法只有一種，那片就一定在那裡。<b>探索用</b>（2026-09-11 使用者提議）。
        ///
        /// 使用者的觀察：外框上一塊小三角大小的凸出，一眼就知道是小三角——它是最小的片，沒有別的組合能替代。
        /// 推廣成：每個凸角都一定有某片的頂點在上面（凸角附近的內部只能被「頂點在角上」的片蓋住），
        /// 所以把 80 個姿態的每個頂點對到角上、不出界的全部列出來；<b>只剩一種佔位</b>的話，任何解都得這樣放。
        ///
        /// 跟 <see cref="KeyPieceExposureOf"/> 的差別：<b>只看形狀、不看參考解</b>，
        /// 所以玩家拼出別的解時照樣成立（被逼出來的片在每一種解裡都一樣）。
        ///
        /// 幾個細節：
        /// · 「一種佔位」比的是片種 + 頂點集合，不是姿態——正方形轉 0 與轉 2 佔位相同，兩個小三角也不分彼此。
        /// · 90° 的角可能是兩個 45° 的尖角湊的，那時放得進去的至少有兩種，不會被誤判成逼出來的。
        /// · 同一片可能同時逼住好幾個角（小三角凸出的兩個尖角），所以數的是<b>片</b>，不是角。
        /// · 只算深度 1：放下逼出來的片之後，新的外框可能又逼出下一片——那是求解器（P2-9）的事。
        /// </summary>
        /// <param name="walks">正規化後的輪廓環（外環 CCW、洞 CW，兩種的內部都在走向的左手邊）。</param>
        /// <returns>
        /// 被逼出來的片：佔位的鍵（<see cref="FootprintKeyOf"/>）→ 片種，每片一筆（兩個小三角都被逼出來就有兩筆）。
        /// 回傳鍵是為了讓呼叫端拿去跟參考解對：逼出來的片一定也在參考解裡。
        /// </returns>
        private static Dictionary<string, PieceKind> ForcedPiecesOf(Walk[] walks)
        {
            Region region = Region.FromWalks(walks, ExactVector2.Zero);
            var forced = new Dictionary<string, PieceKind>();
            var fits = new Dictionary<string, PieceKind>();
            foreach (Walk walk in walks)
            {
                ExactVector2[] ring = walk.Vertices();
                for (int index = 0; index < ring.Length; index++)
                {
                    ExactVector2 previous = ring[(index - 1 + ring.Length) % ring.Length];
                    ExactVector2 corner = ring[index];
                    ExactVector2 next = ring[(index + 1) % ring.Length];
                    // 左轉 = 凸角（內部在左手邊）。共線的中繼點與凹角都跳過：凹角附近可以被好幾片的邊蓋住，不一定有頂點在上面
                    if (Geometry.Cross(previous, corner, next).Sign <= 0) continue;

                    ExactVector2 towardNext = next - corner, towardPrevious = previous - corner;
                    fits.Clear();
                    foreach ((PieceKind kind, int rotation, bool flipped, ExactVector2[] vertices) in AllPoses)
                    {
                        for (int k = 0; k < vertices.Length && fits.Count <= 1; k++)
                        {
                            // 便宜的先篩：片在這個頂點的兩條邊都要落在角的開口裡，才值得問 Region
                            ExactVector2 alongNext = vertices[(k + 1) % vertices.Length] - vertices[k];
                            ExactVector2 alongPrevious = vertices[(k - 1 + vertices.Length) % vertices.Length] - vertices[k];
                            if (!IsInsideCorner(towardNext, towardPrevious, alongNext) ||
                                !IsInsideCorner(towardNext, towardPrevious, alongPrevious)) continue;

                            var placement = new Placement(kind, rotation, flipped, corner - vertices[k]);
                            if (!region.Contains(placement)) continue;
                            fits[FootprintKeyOf(placement)] = kind;
                        }
                        if (fits.Count > 1) break;   // 已經有兩種以上，這個角不洩題
                    }
                    if (fits.Count == 1)
                        foreach (KeyValuePair<string, PieceKind> only in fits) forced[only.Key] = only.Value;
                }
            }
            return forced;
        }

        /// <summary>
        /// 從角出發的一個方向，有沒有落在角的開口裡（含兩條邊上）。只對凸角（開口 &lt; 180°）成立。
        /// </summary>
        /// <param name="towardNext">角 → 下一個頂點。內部在它的左手邊。</param>
        /// <param name="towardPrevious">角 → 上一個頂點。內部在它的右手邊。</param>
        /// <param name="direction">要測的方向。</param>
        /// <returns>在開口裡或貼著邊回 true。</returns>
        private static bool IsInsideCorner(ExactVector2 towardNext, ExactVector2 towardPrevious, ExactVector2 direction)
            => ExactVector2.Cross(towardNext, direction).Sign >= 0 && ExactVector2.Cross(direction, towardPrevious).Sign >= 0;

        /// <summary>「佔哪塊地」的鍵：片種 + 排序過的頂點。姿態不同但佔位相同的（正方形轉 0 與轉 2）會得到同一個鍵。</summary>
        /// <param name="placement">擺放。</param>
        /// <returns>比對用的字串。</returns>
        private static string FootprintKeyOf(Placement placement)
        {
            ExactVector2[] vertices = placement.Vertices();
            Array.Sort(vertices, (left, right) => left.CompareTo(right));
            var key = new StringBuilder(placement.Kind.ToString());
            foreach (ExactVector2 vertex in vertices) key.Append(' ').Append(vertex.ToKey());
            return key.ToString();
        }

        /// <summary>兩條邊的接觸方式。</summary>
        private enum EdgeContact
        {
            /// <summary>不共線、沒重疊，或只碰到一個點。</summary>
            None,

            /// <summary>重疊一段，但誰也沒把誰整條包住——會在輪廓上留下一個台階。</summary>
            Staggered,

            /// <summary>短的那條整條落在長的那條裡面（端點可以重合）。</summary>
            Full,
        }

        /// <summary>
        /// 參考解裡兩片的邊重疊一段以上的地方，分成「完全接觸」與「錯開的」。<b>探索用</b>（2026-09-11 使用者提議）。
        ///
        /// 錯開的接觸會在輪廓上留下一個台階，等於露出「這裡是兩片的交界」，是提示；
        /// 全部完全接觸時接縫藏得乾淨，比較難、也比較好看。
        /// 預設門檻一千題：平均完全 4.8 處、錯開 1.7 處；全部完全接觸的 10.8%。
        /// ⚠️ 錯開的處數跟轉角數相關 0.34——有重疊但沒到可以互相取代。
        /// 這是<b>從解算的</b>，依賴「一題 = 形狀 + 參考解」（`開發進度.md` G2 旁的說明）。
        /// </summary>
        /// <param name="pieces">參考解。</param>
        /// <returns>完全接觸與錯開接觸各有幾處。</returns>
        private static (int full, int staggered) EdgeContactCountsOf(Placement[] pieces)
        {
            int full = 0, staggered = 0;
            for (int first = 0; first < pieces.Length; first++)
            {
                ExactVector2[] firstVertices = pieces[first].Vertices();
                for (int second = first + 1; second < pieces.Length; second++)
                {
                    ExactVector2[] secondVertices = pieces[second].Vertices();
                    for (int i = 0; i < firstVertices.Length; i++)
                    {
                        for (int j = 0; j < secondVertices.Length; j++)
                        {
                            EdgeContact contact = ClassifyEdgeContact(
                                firstVertices[i], firstVertices[(i + 1) % firstVertices.Length],
                                secondVertices[j], secondVertices[(j + 1) % secondVertices.Length]);
                            if (contact == EdgeContact.Full) full++;
                            else if (contact == EdgeContact.Staggered) staggered++;
                        }
                    }
                }
            }
            return (full, staggered);
        }

        /// <summary>
        /// 兩條邊怎麼接觸。把第二條投影到第一條的方向上比區間——用內積、不做除法，全程精確。
        /// </summary>
        /// <param name="firstStart">第一條邊的起點。</param>
        /// <param name="firstEnd">第一條邊的終點。</param>
        /// <param name="secondStart">第二條邊的起點。</param>
        /// <param name="secondEnd">第二條邊的終點。</param>
        /// <returns>接觸方式。</returns>
        private static EdgeContact ClassifyEdgeContact(ExactVector2 firstStart, ExactVector2 firstEnd,
                                                       ExactVector2 secondStart, ExactVector2 secondEnd)
        {
            if (!Geometry.Cross(firstStart, firstEnd, secondStart).IsZero
                || !Geometry.Cross(firstStart, firstEnd, secondEnd).IsZero)
                return EdgeContact.None;   // 不共線

            ExactVector2 direction = firstEnd - firstStart;
            Scalar firstLow = Scalar.Zero;
            Scalar firstHigh = ExactVector2.Dot(direction, direction);
            Scalar projectedSecondStart = ExactVector2.Dot(secondStart - firstStart, direction);
            Scalar projectedSecondEnd = ExactVector2.Dot(secondEnd - firstStart, direction);
            Scalar secondLow = projectedSecondStart < projectedSecondEnd ? projectedSecondStart : projectedSecondEnd;
            Scalar secondHigh = projectedSecondStart < projectedSecondEnd ? projectedSecondEnd : projectedSecondStart;

            Scalar overlapLow = firstLow > secondLow ? firstLow : secondLow;
            Scalar overlapHigh = firstHigh < secondHigh ? firstHigh : secondHigh;
            if (!(overlapLow < overlapHigh)) return EdgeContact.None;   // 沒重疊，或只碰到一個點

            bool secondInsideFirst = secondLow >= firstLow && secondHigh <= firstHigh;
            bool firstInsideSecond = firstLow >= secondLow && firstHigh <= secondHigh;
            return secondInsideFirst || firstInsideSecond ? EdgeContact.Full : EdgeContact.Staggered;
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
