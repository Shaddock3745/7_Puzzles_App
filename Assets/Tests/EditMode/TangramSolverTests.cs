using System;
using System.Collections.Generic;
using System.Diagnostics;
using NUnit.Framework;

namespace Tangram.Data.Tests
{
    /// <summary>
    /// ⭐ <b>求解器</b>（<see cref="TangramSolver"/>，2026-09-21）的測試。
    ///
    /// <b>素材全部借用校準那一批</b>（<see cref="CalibrationEntries"/>）：38 個手拼的形狀，每個都附一組已知的解。
    /// 「已知的解有沒有被找到」是最直接的漏解檢查，而那 38 組是人拼的，不是求解器自己生的——不是循環論證。
    ///
    /// ⚠️ <b>分成日常與 <c>[Explicit]</c> 兩層</b>（2026-09-21 實測之後改的）。窮舉在 Debug 建置下比 Release 慢大約 13 倍：
    /// 38 個形狀全部窮舉，Release 10 秒、<c>dotnet test</c> 2 分 13 秒，而 Unity 的 Test Runner 在 Editor 裡也是非最佳化模式。
    /// 所以日常只跑 <see cref="DailyShapes"/>（20 個窮舉很快的具象題＋正方形＋六邊形4），
    /// 38 個全部與 13 個凸多邊形的解數在 <see cref="求解器_全部形狀驗證並印出解數表"/>（英文入口 <c>ExplicitTools.SolverTable</c>）。
    ///
    /// ⚠️ <b>完整性沒有證明</b>。原本還有一條「不固定順序的慢版本交叉驗證」，實測連正方形都在 5 分鐘內跑不完
    /// （它要走遍所有部分盤面，固定順序那個「大的先放」的剪枝正是它為了完整而拿掉的東西），所以刪掉了。
    /// 使用者 2026-09-21 定：<b>評審不會看到這麼深，對外文件不寫確切的解數，要寫就寫「至少 N 種」</b>。
    ///
    /// 💡 窮舉結果<b>每個形狀只算一次</b>，存在 <see cref="enumerationByName"/> 裡給各條共用。
    /// </summary>
    public class TangramSolverTests
    {
        /// <summary>
        /// 日常測試用的形狀：Release 下窮舉不到 0.03 秒的 20 個具象題，加上正方形與六邊形4。
        ///
        /// <b>正方形一定要在裡面</b>：唯一解、對稱數 8，是最尖銳的檢查——少數一組或多數一組都會馬上露餡。
        /// 六邊形4 是凸多邊形裡最便宜、而且有對稱（對稱數 2）的一個。
        /// 在 Debug 下這一批合計約 10 秒，比這個再多，日常的 Run All 就會明顯變鈍。
        /// </summary>
        private static readonly string[] DailyShapes =
        {
            "正方形", "六邊形4",
            "貓1", "貓2", "貓3", "貓4", "狗1", "狗2", "狗3", "房子2", "魚1", "魚2", "魚3",
            "數字2", "數字3", "數字4", "數字5", "數字6", "數字8", "數字9", "數字0", "茶壺"
        };

        /// <summary>
        /// 13 個凸多邊形的解數：（固定方向, 本質不同）。2026-09-21 量的。
        ///
        /// ⭐ <b>這些是幾何事實，跟權重一點關係都沒有</b>——調權重、改難度都不該讓它們變。
        /// 變了只有兩種可能：求解器改壞了（漏解或重複），或是片的原型／吸附錨點的規則被動過。
        ///
        /// ⚠️ <b>它們是下限</b>：固定順序的搜法在理論上可能漏解（<see cref="TangramSolver"/> 的註解），
        /// 所以寫進對外文件只能說「至少 N 種」。
        /// </summary>
        private static readonly Dictionary<string, (int fixedOrientation, int distinct)> ConvexPolygonSolutionCounts =
            new Dictionary<string, (int, int)>
            {
                { "三角形", (12, 6) }, { "梯形1", (12, 6) }, { "平行四邊形1", (12, 6) }, { "梯形2", (36, 36) },
                { "梯形3", (12, 12) }, { "正方形", (8, 1) }, { "長方形", (36, 9) }, { "五邊形1", (20, 20) },
                { "五邊形2", (16, 8) }, { "六邊形1", (56, 28) }, { "六邊形2", (8, 2) }, { "六邊形3", (24, 6) },
                { "六邊形4", (4, 2) }
            };

        /// <summary>形狀名稱 → 從零開始的窮舉結果。第一次用到才算。</summary>
        private static readonly Dictionary<string, EnumerationResult> enumerationByName =
            new Dictionary<string, EnumerationResult>();

        // ─────────────────────────────────────────────────────────────
        // 一、日常：已知的解與合法性
        // ─────────────────────────────────────────────────────────────

        /// <summary>日常形狀的手拼解，每一組都要出現在窮舉結果裡。38 個全部的版本在 <c>[Explicit]</c> 那支。</summary>
        [Test]
        public void 已知解_日常形狀手拼的那組都在窮舉結果裡() => AssertKnownSolutionsFound(Entries(DailyShapes));

        /// <summary>日常形狀找到的每一組都是真的解。38 個全部的版本在 <c>[Explicit]</c> 那支。</summary>
        [Test]
        public void 合法_日常形狀找到的每一組都是真的解() => AssertEverySolutionLegal(Entries(DailyShapes));

        // ─────────────────────────────────────────────────────────────
        // 二、日常：解數
        // ─────────────────────────────────────────────────────────────

        /// <summary>正方形（8／1）與六邊形4（4／2）的兩種解數釘住。13 個全部的版本在 <c>[Explicit]</c> 那支。</summary>
        [Test]
        public void 解數_正方形與六邊形4的解數沒有變() => AssertConvexSolutionCounts(Entries("正方形", "六邊形4"));

        /// <summary>
        /// 固定方向的解數 ÷ 本質不同的解數 ＝ 形狀的對稱數（正方形 8、六邊形4 2）。
        ///
        /// <b>為什麼成立</b>：區域的每一個對稱（轉 90°、鏡射……）都把一組鋪法變成另一組鋪法，
        /// 所以每一組本質不同的解，在固定方向下剛好有「對稱數」那麼多個樣子——前提是那組解本身不對稱，這一批都滿足（量過）。
        ///
        /// <b>它驗的是什麼</b>：「固定方向的計數」與「<see cref="Canonical.ArrangementKey"/> 去重」這兩件<b>獨立</b>的事彼此一致。
        /// 對稱數用另一條路算（把形狀轉 16 種方向、比輪廓），不經過求解器也不經過 <c>ArrangementKey</c>。
        /// 求解器如果把同一組解數了兩次（例如同種片的先後規定失效），這條會紅。
        /// </summary>
        [Test]
        public void 解數_固定方向除以本質不同等於對稱數() => AssertSymmetryConsistent(Entries("正方形", "六邊形4"));

        // ─────────────────────────────────────────────────────────────
        // 三、日常：三種結果
        // ─────────────────────────────────────────────────────────────

        /// <summary>
        /// <b>確定無解</b>：正方形只有一組本質不同的解。在盒內放一片<b>合法但不屬於任何一組解</b>的大三角，再問——
        /// 要回「確定無解」，不是「被切斷」。⭐ 這正是中途回饋（步驟二）要問的問題。
        ///
        /// ⚠️ <b>「不屬於任何一組解」要排除 8 組，不是 1 組</b>：本質上只有一組，但固定方向下那一組轉過、鏡射過的樣子都是解
        /// （正方形的對稱數是 8）。只排除手拼那組的話，可能剛好挑到另外 7 組之一的大三角，求解器會正確地回「找到」，測試反而紅。
        /// 那 8 組是拿已知解套用形狀的對稱算出來的（<see cref="SymmetricImagesOf"/>），<b>不經過求解器</b>——不然就是拿它驗它自己。
        /// </summary>
        [Test]
        public void 無解_正方形放一片走錯的大三角就確定死了()
        {
            PuzzleLibrary.Entry square = FindEntry("正方形");
            Region region = RegionOf(square);

            List<Placement[]> everySolution = SymmetricImagesOf(square.Pieces);
            Assert.AreEqual(8, everySolution.Count, "正方形的對稱數應該是 8");
            Placement wrongLargeTriangle = LegalPlacementNotInAnySolution(region, PieceKind.LargeTriangle, everySolution);

            var solver = new TangramSolver(region, new Dictionary<int, Placement> { { 0, wrongLargeTriangle } });
            SolveResult result = solver.FindOne();

            Assert.AreEqual(SolveOutcome.NoSolution, result.Outcome);
            Assert.IsNull(result.Solution);
        }

        /// <summary><b>被切斷</b>：上限設得很小，要回「被切斷」，而且不能被說成無解。</summary>
        [Test]
        public void 切斷_上限很小時回報被切斷而不是無解()
        {
            var solver = new TangramSolver(RegionOf(FindEntry("三角形")));

            SolveResult one = solver.FindOne(10);
            Assert.AreEqual(SolveOutcome.SearchLimitReached, one.Outcome);
            Assert.IsNull(one.Solution);
            Assert.AreEqual(10, one.SearchNodeCount);

            EnumerationResult all = solver.EnumerateAll(10);
            Assert.IsFalse(all.IsComplete);
            Assert.AreEqual(10, all.SearchNodeCount);
        }

        /// <summary>
        /// <b>從殘局開始</b>：正方形先放好參考解的 5 片（兩片大三角、中三角、正方形、平行四邊形）→ 要找得到，
        /// 而且因為正方形只有一組解，剩下兩片小三角必須落在參考解的位置，已放的五片原封不動留在自己的槽。
        /// </summary>
        [Test]
        public void 殘局_正方形先放好五片也找得到而且位置對()
        {
            PuzzleLibrary.Entry square = FindEntry("正方形");
            var placed = new Dictionary<int, Placement>();
            for (int slot = 0; slot < square.Pieces.Length; slot++)
                if (PiecePrototype.TangramSet[slot] != PieceKind.SmallTriangle) placed[slot] = square.Pieces[slot];
            Assert.AreEqual(5, placed.Count);

            SolveResult result = new TangramSolver(RegionOf(square), placed).FindOne();
            Assert.AreEqual(SolveOutcome.Found, result.Outcome);

            foreach (KeyValuePair<int, Placement> pair in placed)
                Assert.AreEqual(pair.Value, result.Solution[pair.Key], $"已放的第 {pair.Key} 號槽被動到了");

            // 兩片小三角可能互換槽，所以比的是「兩個位置的集合」
            var expectedSmall = new HashSet<string>();
            var actualSmall = new HashSet<string>();
            for (int slot = 0; slot < square.Pieces.Length; slot++)
            {
                if (PiecePrototype.TangramSet[slot] != PieceKind.SmallTriangle) continue;
                expectedSmall.Add(FootprintKey(square.Pieces[slot]));
                actualSmall.Add(FootprintKey(result.Solution[slot]));
            }
            Assert.IsTrue(expectedSmall.SetEquals(actualSmall), "兩片小三角的位置跟唯一解不一樣");
        }

        /// <summary>
        /// <b>決定性</b>：同樣的輸入跑兩次（兩個物件、也同一個物件兩次），第一組解一模一樣。提示每次按都亮同一片靠的是這個。
        /// 用數字3：固定方向下有 4 組解，「第一組是哪一組」才有得選；而且很便宜。
        /// </summary>
        [Test]
        public void 決定性_同樣的輸入找到同一組()
        {
            Region region = RegionOf(FindEntry("數字3"));
            var solver = new TangramSolver(region);

            Placement[] first = solver.FindOne().Solution;
            Placement[] sameObjectAgain = solver.FindOne().Solution;
            Placement[] otherObject = new TangramSolver(region).FindOne().Solution;

            CollectionAssert.AreEqual(first, sameObjectAgain, "同一個物件第二次找到的不一樣——盤面沒有回溯乾淨？");
            CollectionAssert.AreEqual(first, otherObject, "兩個物件找到的不一樣——有東西依賴集合的列舉順序？");
        }

        // ─────────────────────────────────────────────────────────────
        // 四、日常：建立時的防呆
        // ─────────────────────────────────────────────────────────────

        /// <summary>面積不是 32 的區域要擋：七片放進大框框不等於「填滿」，找到的不是解。</summary>
        [Test]
        public void 防呆_區域面積不是三十二就拒絕()
        {
            var bigSquare = Region.FromPolygon(new[]
            {
                new ExactVector2(0, 0), new ExactVector2(16, 0), new ExactVector2(16, 16), new ExactVector2(0, 16)
            });
            Assert.Throws<ArgumentException>(() => new TangramSolver(bigSquare));
        }

        /// <summary>已放的片放錯槽（片種對不上）或槽號超出範圍要擋——那是呼叫端的 bug，放過去的話結果會安靜地錯掉。</summary>
        [Test]
        public void 防呆_已放的片種跟槽對不上就拒絕()
        {
            PuzzleLibrary.Entry square = FindEntry("正方形");
            Region region = RegionOf(square);

            // 0 號槽是大三角，塞一片中三角（2 號槽的）進去
            Assert.Throws<ArgumentException>(() =>
                new TangramSolver(region, new Dictionary<int, Placement> { { 0, square.Pieces[2] } }));
            Assert.Throws<ArgumentException>(() =>
                new TangramSolver(region, new Dictionary<int, Placement> { { 7, square.Pieces[0] } }));
        }

        // ─────────────────────────────────────────────────────────────
        // 五、[Explicit]：38 個全部
        // ─────────────────────────────────────────────────────────────

        /// <summary>
        /// ⭐ <b>完整的驗證＋解數表</b>：38 個形狀全部檢查「已知解找得到」「找到的都合法」，
        /// 13 個凸多邊形的解數全部釘住、對稱數全部對帳，最後印出每個形狀的解數、對稱數、搜尋節點與耗時。
        /// Debug 建置約 2 分半。英文入口 <c>ExplicitTools.SolverTable</c>，要看表記得加 <c>--logger "console;verbosity=detailed"</c>。
        ///
        /// <b>什麼時候要跑</b>：動到求解器、<see cref="PlacementCandidates"/>、<see cref="Region"/> 的判斷、或片的原型之後。
        /// </summary>
        [Test, Explicit("求解器完整驗證：Debug 約 2 分半")]
        public void 求解器_全部形狀驗證並印出解數表()
        {
            IReadOnlyList<PuzzleLibrary.Entry> all = CalibrationEntries.All();
            IReadOnlyList<PuzzleLibrary.Entry> convexPolygons = CalibrationEntries.ConvexPolygons();

            TestContext.WriteLine("名稱\t找一組(秒)\t窮舉(秒)\t固定方向解數\t本質不同解數\t對稱數\t搜尋節點\t搜完了嗎");
            foreach (PuzzleLibrary.Entry entry in all)
            {
                var solver = new TangramSolver(RegionOf(entry));
                var clock = Stopwatch.StartNew();
                solver.FindOne();
                double findSeconds = clock.Elapsed.TotalSeconds;
                clock.Restart();
                EnumerationResult result = solver.EnumerateAll();
                double enumerateSeconds = clock.Elapsed.TotalSeconds;
                enumerationByName[entry.Name] = result;

                TestContext.WriteLine($"{entry.Name}\t{findSeconds:0.000}\t{enumerateSeconds:0.000}\t" +
                                      $"{result.FixedOrientationSolutionCount}\t{result.DistinctSolutions.Count}\t" +
                                      $"{SymmetryOrderOf(entry.Pieces)}\t{result.SearchNodeCount}\t{(result.IsComplete ? "是" : "被切斷")}");
            }

            Assert.AreEqual(ConvexPolygonSolutionCounts.Count, convexPolygons.Count);
            AssertKnownSolutionsFound(all);
            AssertEverySolutionLegal(all);
            AssertConvexSolutionCounts(convexPolygons);
            AssertSymmetryConsistent(convexPolygons);
        }

        // ─────────────────────────────────────────────────────────────
        // 共用的斷言（日常與 [Explicit] 各拿不同的形狀清單來叫）
        // ─────────────────────────────────────────────────────────────

        /// <summary>
        /// 每一組手拼的解都要出現在它那個形狀的窮舉結果裡（用 <see cref="Canonical.ArrangementKey"/> 比）。
        /// 而且每個形狀都要搜完——被切斷的話「找不到」就不代表漏解。
        /// </summary>
        private static void AssertKnownSolutionsFound(IEnumerable<PuzzleLibrary.Entry> entries)
        {
            foreach (PuzzleLibrary.Entry entry in entries)
            {
                EnumerationResult result = EnumerationOf(entry);
                Assert.IsTrue(result.IsComplete, $"「{entry.Name}」在預設上限內沒搜完");

                string knownKey = Canonical.ArrangementKey(entry.Pieces);
                bool found = false;
                foreach (Placement[] solution in result.DistinctSolutions)
                    if (Canonical.ArrangementKey(solution) == knownKey) { found = true; break; }

                Assert.IsTrue(found, $"「{entry.Name}」手拼的那組解不在求解器的結果裡——漏解了");
            }
        }

        /// <summary>
        /// 每一組解都是真的解：片種對得上槽、七片都在區域內、兩兩不重疊、面積 32、連通、編碼往返。
        /// 防的是「剪枝寫錯，把不合法的東西當成解」。
        /// </summary>
        private static void AssertEverySolutionLegal(IEnumerable<PuzzleLibrary.Entry> entries)
        {
            foreach (PuzzleLibrary.Entry entry in entries)
            {
                Region region = RegionOf(entry);
                foreach (Placement[] solution in EnumerationOf(entry).DistinctSolutions)
                {
                    Assert.AreEqual(PiecePrototype.TangramSet.Length, solution.Length);
                    for (int slot = 0; slot < solution.Length; slot++)
                    {
                        Assert.AreEqual(PiecePrototype.TangramSet[slot], solution[slot].Kind,
                            $"「{entry.Name}」第 {slot} 號槽的片種不對");
                        Assert.IsTrue(region.Contains(solution[slot]), $"「{entry.Name}」第 {slot} 號槽出界");
                    }

                    var report = new Validation.Report();
                    Validation.CheckNoOverlap(solution, report);
                    Validation.CheckArea(solution, report);
                    Validation.CheckConnected(solution, report);
                    Validation.CheckRoundTrip(solution, report);
                    Assert.IsTrue(report.Ok, $"「{entry.Name}」有一組解不合法：{report}");
                }
            }
        }

        /// <summary>凸多邊形的兩種解數要等於 <see cref="ConvexPolygonSolutionCounts"/> 釘住的值。</summary>
        private static void AssertConvexSolutionCounts(IEnumerable<PuzzleLibrary.Entry> entries)
        {
            foreach (PuzzleLibrary.Entry entry in entries)
            {
                Assert.IsTrue(ConvexPolygonSolutionCounts.TryGetValue(entry.Name, out var expected),
                    $"「{entry.Name}」不在期待的解數表裡——素材是不是改名了？");

                EnumerationResult result = EnumerationOf(entry);
                Assert.AreEqual(expected.fixedOrientation, result.FixedOrientationSolutionCount,
                    $"「{entry.Name}」固定方向的解數變了");
                Assert.AreEqual(expected.distinct, result.DistinctSolutions.Count,
                    $"「{entry.Name}」本質不同的解數變了");
            }
        }

        /// <summary>固定方向的解數 ＝ 對稱數 × 本質不同的解數。理由見 <see cref="解數_固定方向除以本質不同等於對稱數"/>。</summary>
        private static void AssertSymmetryConsistent(IEnumerable<PuzzleLibrary.Entry> entries)
        {
            foreach (PuzzleLibrary.Entry entry in entries)
            {
                EnumerationResult result = EnumerationOf(entry);
                int symmetryOrder = SymmetryOrderOf(entry.Pieces);

                Assert.AreEqual(symmetryOrder * result.DistinctSolutions.Count, result.FixedOrientationSolutionCount,
                    $"「{entry.Name}」對稱數 {symmetryOrder}、本質解 {result.DistinctSolutions.Count}、" +
                    $"固定方向解 {result.FixedOrientationSolutionCount}，對不起來");
            }
        }

        // ─────────────────────────────────────────────────────────────
        // 共用的小工具
        // ─────────────────────────────────────────────────────────────

        private static EnumerationResult EnumerationOf(PuzzleLibrary.Entry entry)
        {
            if (enumerationByName.TryGetValue(entry.Name, out EnumerationResult cached)) return cached;
            EnumerationResult result = new TangramSolver(RegionOf(entry)).EnumerateAll();
            enumerationByName[entry.Name] = result;
            return result;
        }

        /// <summary>
        /// 手拼那組解的外框，放在<b>跟解同一個座標系</b>（原點不動）——這樣已知的解直接就是這個區域裡的一組鋪法，不用平移。
        /// </summary>
        private static Region RegionOf(PuzzleLibrary.Entry entry) =>
            Region.FromPuzzle(entry.Puzzle, ExactVector2.Zero);

        private static PuzzleLibrary.Entry FindEntry(string name)
        {
            foreach (PuzzleLibrary.Entry entry in CalibrationEntries.All())
                if (entry.Name == name) return entry;
            Assert.Fail($"素材裡找不到「{name}」——是不是改名了？");
            return null;
        }

        private static IReadOnlyList<PuzzleLibrary.Entry> Entries(params string[] names)
        {
            var entries = new List<PuzzleLibrary.Entry>();
            foreach (string name in names) entries.Add(FindEntry(name));
            return entries;
        }

        /// <summary>一片擺放佔住的位置（片種＋排序過的頂點），跟姿態無關。</summary>
        private static string FootprintKey(Placement placement)
        {
            ExactVector2[] vertices = placement.Vertices();
            Array.Sort(vertices);
            var parts = new string[vertices.Length];
            for (int i = 0; i < vertices.Length; i++) parts[i] = vertices[i].ToKey();
            return placement.Kind + "{" + string.Join(";", parts) + "}";
        }

        /// <summary>
        /// 形狀的對稱數：16 種整體變換（8 個 45° 轉向 × 翻或不翻）裡，有幾種把輪廓變回自己（平移不算）。
        /// ⚠️ <b>刻意不經過求解器也不經過 <see cref="Canonical"/></b>：是拿來跟它們對帳的，要走另一條路。
        /// </summary>
        private static int SymmetryOrderOf(Placement[] pieces)
        {
            string original = OutlineKey(pieces, 0, false);
            int count = 0;
            for (int flipIndex = 0; flipIndex < 2; flipIndex++)
                for (int rotation = 0; rotation < 8; rotation++)
                    if (OutlineKey(pieces, rotation, flipIndex == 1) == original) count++;
            return count;
        }

        /// <summary>把整組片做一次整體變換、追出輪廓，把輪廓頂點平移到「最小的頂點在原點」後排序串起來。</summary>
        private static string OutlineKey(Placement[] pieces, int globalRotation, bool globalFlipped)
        {
            var transformed = new Placement[pieces.Length];
            for (int i = 0; i < pieces.Length; i++) transformed[i] = pieces[i].ApplyGlobal(globalRotation, globalFlipped);

            var vertices = new List<ExactVector2>();
            foreach (Walk walk in BoundaryTracer.Trace(transformed)) vertices.AddRange(walk.Vertices());
            vertices.Sort();

            ExactVector2 smallest = vertices[0];
            var parts = new string[vertices.Count];
            for (int i = 0; i < vertices.Count; i++) parts[i] = (vertices[i] - smallest).ToKey();
            return string.Join(";", parts);
        }

        /// <summary>
        /// 一組解在區域的每一個對稱之下的樣子，<b>都對齊回原本的座標</b>（所以每一個都是同一個區域裡的一組鋪法）。
        /// 個數就是對稱數。做法：套用 16 種整體變換，輪廓對得回原形的留下，再平移到「輪廓最小的頂點」重合。
        /// </summary>
        private static List<Placement[]> SymmetricImagesOf(Placement[] pieces)
        {
            string original = OutlineKey(pieces, 0, false);
            ExactVector2 originalSmallest = SmallestOutlineVertex(pieces);

            var images = new List<Placement[]>();
            for (int flipIndex = 0; flipIndex < 2; flipIndex++)
                for (int rotation = 0; rotation < 8; rotation++)
                {
                    if (OutlineKey(pieces, rotation, flipIndex == 1) != original) continue;

                    var transformed = new Placement[pieces.Length];
                    for (int i = 0; i < pieces.Length; i++) transformed[i] = pieces[i].ApplyGlobal(rotation, flipIndex == 1);
                    ExactVector2 delta = originalSmallest - SmallestOutlineVertex(transformed);
                    for (int i = 0; i < transformed.Length; i++) transformed[i] = transformed[i].Translated(delta);
                    images.Add(transformed);
                }
            return images;
        }

        private static ExactVector2 SmallestOutlineVertex(Placement[] pieces)
        {
            var vertices = new List<ExactVector2>();
            foreach (Walk walk in BoundaryTracer.Trace(pieces)) vertices.AddRange(walk.Vertices());
            vertices.Sort();
            return vertices[0];
        }

        /// <summary>在區域裡找一個合法（在區域內、貼在邊界節點上）但<b>不屬於任何一組指定解</b>的擺放。</summary>
        private static Placement LegalPlacementNotInAnySolution(Region region, PieceKind kind, List<Placement[]> solutions)
        {
            var solutionFootprints = new HashSet<string>();
            foreach (Placement[] solution in solutions)
                foreach (Placement piece in solution) solutionFootprints.Add(FootprintKey(piece));

            var candidates = new List<PlacementCandidate>();
            for (int rotation = 0; rotation < PiecePrototype.RotationCycleLength(kind); rotation++)
                PlacementCandidates.Enumerate(new PieceNodeSet(kind, rotation, false), region.BoundaryNodes, candidates);

            foreach (PlacementCandidate candidate in candidates)
                if (region.Contains(candidate.Placement) && !solutionFootprints.Contains(FootprintKey(candidate.Placement)))
                    return candidate.Placement;

            Assert.Fail($"區域裡找不到不屬於解的合法 {kind}");
            return default;
        }
    }
}
