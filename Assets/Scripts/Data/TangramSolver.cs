using System;
using System.Collections.Generic;

namespace Tangram.Data
{
    /// <summary>「找一組解」的結果是哪一種。</summary>
    public enum SolveOutcome
    {
        /// <summary>找到了一組解。</summary>
        Found,

        /// <summary><b>確定</b>沒有解：所有分支都試完了。</summary>
        NoSolution,

        /// <summary>
        /// 搜尋節點數到了上限，<b>不知道</b>有沒有解。
        /// ⚠️ 絕對不能當成 <see cref="NoSolution"/> 處理——中途回饋會因此把活的盤面標成死局（<c>互動設計.md</c> §8「寧可沉默也不要閃錯顏色」）。
        /// </summary>
        SearchLimitReached
    }

    /// <summary><see cref="TangramSolver.FindOne"/> 的結果。</summary>
    public sealed class SolveResult
    {
        /// <summary>找到、確定無解、或被上限切斷。</summary>
        public readonly SolveOutcome Outcome;

        /// <summary>
        /// 七片的擺放，<b>索引就是槽號</b>（跟 <c>BoardState</c>、<see cref="Arrangement.FromPlacements"/> 同一套對應）。
        /// 已經放好的片原封不動地出現在它自己的槽裡。沒找到時是 null。每次回傳的都是新的陣列。
        /// </summary>
        public readonly Placement[] Solution;

        /// <summary>這次搜尋放上去了幾次片（通過剪枝的才算）。</summary>
        public readonly long SearchNodeCount;

        internal SolveResult(SolveOutcome outcome, Placement[] solution, long searchNodeCount)
        {
            Outcome = outcome;
            Solution = solution;
            SearchNodeCount = searchNodeCount;
        }
    }

    /// <summary><see cref="TangramSolver.EnumerateAll"/> 的結果。</summary>
    public sealed class EnumerationResult
    {
        /// <summary>
        /// true = 所有分支都試完了，下面的數字就是全部；
        /// false = 被上限切斷，下面的解<b>只是一部分</b>，不能拿來說「只有 N 種」。
        /// </summary>
        public readonly bool IsComplete;

        /// <summary>
        /// <b>本質不同</b>的解，每組七片、索引是槽號。
        /// 「本質不同」＝ <see cref="Canonical.ArrangementKey"/> 不同：轉過角度、鏡射過、平移過、同種片互換，都算同一組。
        /// 順序是找到的先後。
        /// </summary>
        public readonly IReadOnlyList<Placement[]> DistinctSolutions;

        /// <summary>
        /// <b>固定方向</b>下的解數：區域不動，每一種不同的鋪法各算一次。
        /// 💡 除以 <see cref="DistinctSolutions"/> 的個數，應該剛好是區域的對稱數（正方形 8、長方形 4、沒有對稱的形狀 1）——
        /// 這是搜尋與去重彼此一致的檢查，<c>TangramSolverTests</c> 釘住了。
        /// </summary>
        public readonly int FixedOrientationSolutionCount;

        /// <summary>這次搜尋放上去了幾次片。</summary>
        public readonly long SearchNodeCount;

        internal EnumerationResult(bool isComplete, IReadOnlyList<Placement[]> distinctSolutions,
                                   int fixedOrientationSolutionCount, long searchNodeCount)
        {
            IsComplete = isComplete;
            DistinctSolutions = distinctSolutions;
            FixedOrientationSolutionCount = fixedOrientationSolutionCount;
            SearchNodeCount = searchNodeCount;
        }
    }

    /// <summary>
    /// ⭐ <b>求解器</b>（P2-10 找一組、P2-11 列出全部；2026-09-21）：給一個要填滿的區域，
    /// 加上已經放好的片（可以是零片），回答「能不能填滿、怎麼填、有幾種填法」。
    ///
    /// <b>做法刻意是最笨的那種</b>——深度優先、大的先放、每一步只試「有一個節點貼在錨點上」的位置。
    /// 開工前用一次性的試作版量過 38 個手拼形狀：找第一組最慢 2.2 秒、13 個凸多邊形的窮舉全部在 6 秒內跑完，
    /// 所以面積剪枝、「必須覆蓋某一點」那類聰明做法<b>都沒做</b>，等量過真的太慢再說。
    ///
    /// <b>合法的定義跟遊戲一模一樣</b>：錨點＝區域邊界節點＋已放片的節點（就是吸附用的那一組），
    /// 合法＝在區域內（<see cref="Region.ContainsPolygon"/>）而且不重疊（<see cref="Geometry.ConvexOverlap"/>）。
    /// 所以求解器找到的每一組解，<b>玩家照它的順序一片一片拖，都吸得上去</b>——提示不會亮出一個吸不上的位置。
    /// ⚠️ 這裡不能叫 <c>SnapSolver.OverlapsAny</c>：它在 <c>Core</c>，而依賴方向是 <c>Core → Data</c>。底下是同一個判斷。
    ///
    /// ⚠️ <b>完整性有一個理論上的漏洞，而且沒有證明</b>：片照固定順序放，所以每一片放下去時都要碰得到區域邊界或比它早放的片。
    /// 如果某組解裡有一片「排在前面、只碰到比它晚放的片、也沒碰到邊界」（例如中三角夾在一群小片中間），那組解會漏掉。
    /// 實務上手拼的 38 組一組都沒漏，但那不是證明。
    /// 原本打算用「不固定順序」的慢版本交叉驗證，實測連正方形都在 5 分鐘內跑不完，所以放棄了；
    /// 使用者 2026-09-21 定：<b>對外文件不寫確切的解數，要寫就寫「至少 N 種」</b>。
    /// 💡 真的需要證明的話，完整又快的做法是「每一步只補最前面那個還沒蓋滿的角」（角度都是 45° 的整數倍，蓋了多少算得精確），那是另一個設計。
    ///
    /// 💡 同一個物件可以重複呼叫：每次搜尋結束（不管是找到、搜完或被切斷）盤面都會回溯回建立時的樣子。
    /// ⚠️ 但<b>不是執行緒安全的</b>——搜尋中的盤面就存在物件裡。
    /// </summary>
    public sealed class TangramSolver
    {
        /// <summary>
        /// 預設的搜尋節點上限。量到的最大值約 6200（三角形的窮舉），留十幾倍的餘裕。
        /// <b>用節點數不用時間</b>：時間上限會讓測試隨機器快慢時好時壞，節點數每次都一樣。
        /// </summary>
        public const long DefaultSearchNodeLimit = 100000;

        /// <summary>
        /// 放片的先後：大三角 → 中三角 → 正方形 → 平行四邊形 → 小三角。
        /// <b>大的先放</b>是因為大片能放的位置少，走錯路會在很淺的地方就被剪掉。
        /// ⚠️ 同種的兩片會因此排在相鄰的位置，<see cref="Search"/> 的「同種片規定先後」靠的就是這一點。
        /// </summary>
        private static readonly PieceKind[] PlacementPriority =
        {
            PieceKind.LargeTriangle, PieceKind.MediumTriangle, PieceKind.Square,
            PieceKind.Parallelogram, PieceKind.SmallTriangle
        };

        /// <summary>
        /// 每種片的所有姿態（轉向，能翻面的再加翻面），第一次用到時算好就共用。
        /// 轉向數照 <see cref="PiecePrototype.RotationCycleLength"/>，所以<b>姿態之間佔的位置不會重複</b>（正方形轉 90° 回到自己，只列兩種）。
        /// </summary>
        private static readonly Dictionary<PieceKind, PieceNodeSet[]> posesByKind = new Dictionary<PieceKind, PieceNodeSet[]>();

        private readonly Region region;

        /// <summary>還沒放的槽，<b>照搜尋的先後排好</b>。同種片之間照槽號小的先。</summary>
        private readonly int[] searchSlots;

        // ---- 搜尋中的盤面，索引是槽號 ----
        private readonly Placement[] placementBySlot = new Placement[PiecePrototype.TangramSet.Length];
        private readonly ExactVector2[][] verticesBySlot = new ExactVector2[PiecePrototype.TangramSet.Length][];
        private readonly BoundingBox[] boundsBySlot = new BoundingBox[PiecePrototype.TangramSet.Length];

        /// <summary>現在盤上有哪些槽，照放上去的先後。重疊比對走這一份，不用每次掃七個槽。</summary>
        private readonly List<int> occupiedSlots = new List<int>();

        // ---- 錨點：清單保順序、集合擋重複 ----
        /// <summary>
        /// ⚠️ <b>順序是決定性的來源</b>：先區域的邊界節點（照區域給的順序），再照槽號加入已放片的節點，重複的只留第一次。
        /// 候選的列舉順序跟著它走，而「找到的第一組解是哪一組」又跟著候選順序走——提示每次按都亮同一片，靠的就是這裡。
        /// 所以這裡刻意不用集合列舉（<c>HashSet</c> 的列舉順序不是契約）。
        /// </summary>
        private readonly List<ExactVector2> anchors = new List<ExactVector2>();
        private readonly HashSet<ExactVector2> anchorSet = new HashSet<ExactVector2>();

        /// <summary>每放一片新增了幾個錨點。拿下來時照這個數字從清單尾端刪掉（放與拿一定是後進先出）。</summary>
        private readonly Stack<int> addedAnchorCounts = new Stack<int>();

        // ---- 每一層各自的暫存，遞迴時不會互相蓋掉 ----
        private readonly List<PlacementCandidate>[] candidateBufferByDepth;
        private readonly HashSet<Placement>[] seenPlacementBufferByDepth;
        private readonly HashSet<Footprint>[] seenFootprintBufferByDepth;
        private readonly Footprint[] footprintByDepth;

        // ---- 這一次搜尋的狀態，每次公開呼叫開頭重設 ----
        private long searchNodeLimit;
        private long searchNodeCount;
        private bool limitReached;
        private bool stopAtFirstSolution;
        private Placement[] firstSolution;
        private int fixedOrientationSolutionCount;
        private HashSet<string> distinctKeys;
        private List<Placement[]> distinctSolutions;

        /// <summary>建立一個求解器。</summary>
        /// <param name="region">
        /// 要填滿的區域。⚠️ <b>面積必須剛好是 32</b>（七片的總面積）——
        /// 比較大的區域（例如出題工具的 16×16 空框）會讓「七片都放進去」不等於「填滿」，找到的東西就不是解。
        /// </param>
        /// <param name="placedPiecesBySlot">
        /// 已經放好的片：槽號 → 擺放。null 或空的代表從零開始。
        /// ⚠️ <b>不檢查它們合不合法</b>（在不在區域內、有沒有重疊）——<c>BoardState</c> 裡本來就只有合法擺放，由呼叫端保證。
        /// 只檢查槽號在範圍內、片種跟那個槽對得上，因為那兩種錯是呼叫端的 bug，而且會讓結果安靜地錯掉。
        /// </param>
        /// <exception cref="ArgumentNullException">區域是 null。</exception>
        /// <exception cref="ArgumentException">區域面積不是 32、槽號超出範圍、或片種跟槽對不上。</exception>
        public TangramSolver(Region region, IReadOnlyDictionary<int, Placement> placedPiecesBySlot = null)
        {
            this.region = region ?? throw new ArgumentNullException(nameof(region));

            Scalar doubleArea = DoubleAreaOf(region);
            if (!doubleArea.Equals(DifficultyFeatures.ExpectedDoubleArea))
                throw new ArgumentException(
                    $"TangramSolver：區域面積的兩倍是 {doubleArea}，不是 64——七片放不滿或放不下，找到的東西不會是解",
                    nameof(region));

            PieceKind[] tangramSet = PiecePrototype.TangramSet;
            int slotCount = tangramSet.Length;

            // 錨點先放區域的邊界節點（照區域給的順序）
            foreach (ExactVector2 node in region.BoundaryNodes) AddAnchor(node);

            // 已放的片照槽號加進來，錨點也照槽號加（決定性，見 anchors 的註解）
            var isPlaced = new bool[slotCount];
            if (placedPiecesBySlot != null)
            {
                // 先擋超出範圍的槽號：下面的迴圈只走 0 ～ 6，超出的會被默默略過
                foreach (int slot in placedPiecesBySlot.Keys)
                    if (slot < 0 || slot >= slotCount)
                        throw new ArgumentException($"TangramSolver：槽號 {slot} 超出 0 ～ {slotCount - 1}",
                                                    nameof(placedPiecesBySlot));

                for (int slot = 0; slot < slotCount; slot++)
                {
                    if (!placedPiecesBySlot.TryGetValue(slot, out Placement placement)) continue;
                    if (placement.Kind != tangramSet[slot])
                        throw new ArgumentException(
                            $"TangramSolver：第 {slot} 號槽應該是 {tangramSet[slot]}，給的是 {placement.Kind}",
                            nameof(placedPiecesBySlot));
                    isPlaced[slot] = true;
                    ExactVector2[] vertices = placement.Vertices();
                    PutOnBoard(slot, placement, vertices, BoundingBox.FromPoints(vertices));
                }
            }
            // 建立時放的片不會被拿下來，所以它們加的錨點數不用記
            addedAnchorCounts.Clear();

            searchSlots = BuildSearchOrder(isPlaced);

            int depthCount = searchSlots.Length;
            candidateBufferByDepth = new List<PlacementCandidate>[depthCount];
            seenPlacementBufferByDepth = new HashSet<Placement>[depthCount];
            seenFootprintBufferByDepth = new HashSet<Footprint>[depthCount];
            footprintByDepth = new Footprint[depthCount];
            for (int depth = 0; depth < depthCount; depth++)
            {
                candidateBufferByDepth[depth] = new List<PlacementCandidate>();
                seenPlacementBufferByDepth[depth] = new HashSet<Placement>();
                seenFootprintBufferByDepth[depth] = new HashSet<Footprint>();
            }
        }

        /// <summary>
        /// 找一組解，找到就停。
        /// </summary>
        /// <param name="searchNodeLimit">搜尋節點上限，≥ 0。</param>
        /// <returns>找到（附七片擺放）、確定無解、或被上限切斷。</returns>
        /// <exception cref="ArgumentOutOfRangeException">上限是負的。</exception>
        public SolveResult FindOne(long searchNodeLimit = DefaultSearchNodeLimit)
        {
            BeginSearch(searchNodeLimit, stopAtFirst: true);
            Search(0);

            SolveOutcome outcome = firstSolution != null ? SolveOutcome.Found
                                 : limitReached ? SolveOutcome.SearchLimitReached
                                 : SolveOutcome.NoSolution;
            return new SolveResult(outcome, firstSolution, searchNodeCount);
        }

        /// <summary>
        /// 列出全部本質不同的解。
        /// 被上限切斷時，已經找到的解照樣給出來，但 <see cref="EnumerationResult.IsComplete"/> 是 false。
        /// </summary>
        /// <param name="searchNodeLimit">搜尋節點上限，≥ 0。</param>
        /// <returns>本質不同的解、固定方向的解數、搜完了沒有。</returns>
        /// <exception cref="ArgumentOutOfRangeException">上限是負的。</exception>
        public EnumerationResult EnumerateAll(long searchNodeLimit = DefaultSearchNodeLimit)
        {
            BeginSearch(searchNodeLimit, stopAtFirst: false);
            Search(0);
            return new EnumerationResult(!limitReached, distinctSolutions, fixedOrientationSolutionCount, searchNodeCount);
        }

        // ─────────────────────────────────────────────────────────────
        // 搜尋
        // ─────────────────────────────────────────────────────────────

        private void BeginSearch(long limit, bool stopAtFirst)
        {
            if (limit < 0) throw new ArgumentOutOfRangeException(nameof(limit), "搜尋節點上限不能是負的");
            searchNodeLimit = limit;
            searchNodeCount = 0;
            limitReached = false;
            stopAtFirstSolution = stopAtFirst;
            firstSolution = null;
            fixedOrientationSolutionCount = 0;
            distinctKeys = new HashSet<string>();
            distinctSolutions = new List<Placement[]>();
        }

        /// <summary>
        /// 放第 <paramref name="depth"/> 片（照 <see cref="searchSlots"/> 的順序），往下遞迴。
        ///
        /// 每一層做的事：列出錨點上所有貼得上的擺放 → 同一個擺放只留一份 → 同種片規定先後 →
        /// 剪掉出界與重疊的 → 放上去、往下一層、拿下來。
        /// </summary>
        /// <param name="depth">第幾片（0 起算）。</param>
        /// <returns>true ＝ 整個搜尋要停（找到第一組、或到了上限）。</returns>
        private bool Search(int depth)
        {
            if (depth == searchSlots.Length)
            {
                RecordSolution();
                return stopAtFirstSolution;
            }

            int slot = searchSlots[depth];
            PieceKind kind = PiecePrototype.TangramSet[slot];

            // ⭐ 同種片規定先後：跟上一片同種，而且兩片都是這次搜尋放的，這片的身分就必須排在上一片之後。
            // 同一組解才不會因為兩片大三角互換而被找兩次（搜尋量少 4 倍）。
            // 其中一片是呼叫端先放的話，它根本不在 searchSlots 裡，自然不會套到這條——那片的位置是固定的，沒有互換可言
            Footprint mustComeAfter = depth > 0 && PiecePrototype.TangramSet[searchSlots[depth - 1]] == kind
                ? footprintByDepth[depth - 1]
                : null;

            List<PlacementCandidate> candidates = candidateBufferByDepth[depth];
            candidates.Clear();
            foreach (PieceNodeSet pose in PosesOf(kind))
                PlacementCandidates.Enumerate(pose, anchors, candidates);

            HashSet<Placement> seenPlacements = seenPlacementBufferByDepth[depth];
            HashSet<Footprint> seenFootprints = seenFootprintBufferByDepth[depth];
            seenPlacements.Clear();
            seenFootprints.Clear();

            foreach (PlacementCandidate candidate in candidates)
            {
                Placement placement = candidate.Placement;

                // 很多（錨點, 節點）組合會湊出同一個擺放。先用姿態＋平移擋掉，這一步不配置任何東西
                if (!seenPlacements.Add(placement)) continue;

                // ⚠️ 真正的身分是「佔住哪些頂點」，不是姿態：正方形轉 0 與轉 2 姿態不相等、位置一模一樣
                // （CLAUDE.md 坑表第一條）。轉向數照 RotationCycleLength 列的話這種情況不會發生，
                // 但同種片的先後規定要一個跟姿態無關的全序，所以這裡照樣用頂點集合
                ExactVector2[] vertices = placement.Vertices();
                var footprint = new Footprint(kind, vertices);
                if (!seenFootprints.Add(footprint)) continue;
                if (mustComeAfter != null && footprint.CompareTo(mustComeAfter) <= 0) continue;

                if (!region.ContainsPolygon(vertices)) continue;
                BoundingBox bounds = BoundingBox.FromPoints(vertices);
                if (OverlapsAnyOnBoard(vertices, bounds)) continue;

                // 通過剪枝才算一個搜尋節點。到上限就整個停，而且記下「是被切斷的」
                if (searchNodeCount >= searchNodeLimit)
                {
                    limitReached = true;
                    return true;
                }
                searchNodeCount++;

                PutOnBoard(slot, placement, vertices, bounds);
                footprintByDepth[depth] = footprint;
                bool halt = Search(depth + 1);
                TakeOffBoard(slot);

                // ⚠️ 先拿下來再回傳：不管為什麼停，盤面都要回到這一層開始時的樣子
                if (halt) return true;
            }
            return false;
        }

        /// <summary>七片都放好了：記下這組解。</summary>
        private void RecordSolution()
        {
            var solution = (Placement[])placementBySlot.Clone();

            if (stopAtFirstSolution)
            {
                firstSolution = solution;
                return;
            }

            fixedOrientationSolutionCount++;
            if (distinctKeys.Add(Canonical.ArrangementKey(solution))) distinctSolutions.Add(solution);
        }

        // ─────────────────────────────────────────────────────────────
        // 盤面
        // ─────────────────────────────────────────────────────────────

        private void PutOnBoard(int slot, Placement placement, ExactVector2[] vertices, BoundingBox bounds)
        {
            placementBySlot[slot] = placement;
            verticesBySlot[slot] = vertices;
            boundsBySlot[slot] = bounds;
            occupiedSlots.Add(slot);

            int added = 0;
            foreach (ExactVector2 node in placement.Nodes())
                if (AddAnchor(node)) added++;
            addedAnchorCounts.Push(added);
        }

        /// <summary>拿下最後放上去的那片。⚠️ 只能後進先出，<see cref="Search"/> 的遞迴保證了這一點。</summary>
        private void TakeOffBoard(int slot)
        {
            int added = addedAnchorCounts.Pop();
            for (int i = 0; i < added; i++)
            {
                int last = anchors.Count - 1;
                anchorSet.Remove(anchors[last]);
                anchors.RemoveAt(last);
            }

            occupiedSlots.RemoveAt(occupiedSlots.Count - 1);
            verticesBySlot[slot] = null;
            placementBySlot[slot] = default;
        }

        /// <returns>這個點是新的錨點（之前沒有）就回 true。</returns>
        private bool AddAnchor(ExactVector2 node)
        {
            if (!anchorSet.Add(node)) return false;
            anchors.Add(node);
            return true;
        }

        private bool OverlapsAnyOnBoard(ExactVector2[] vertices, BoundingBox bounds)
        {
            foreach (int slot in occupiedSlots)
            {
                if (AreDefinitelyApart(bounds, boundsBySlot[slot])) continue;
                if (Geometry.ConvexOverlap(vertices, verticesBySlot[slot])) return true;
            }
            return false;
        }

        /// <summary>
        /// 兩個包圍盒是不是<b>確定</b>分開的，只用來省下 SAT。
        /// ⚠️ 用嚴格比較：盒子剛好貼在一起還是要走 SAT——那正是邊貼合，SAT 會正確地回不重疊。
        /// （跟 <c>PuzzleGenerator</c> 裡那支是同一個判斷。它是 private 的，而這一張卡約定不改出題器，所以各留一份。）
        /// </summary>
        private static bool AreDefinitelyApart(BoundingBox first, BoundingBox second)
            => first.Maximum.X < second.Minimum.X
            || second.Maximum.X < first.Minimum.X
            || first.Maximum.Y < second.Minimum.Y
            || second.Maximum.Y < first.Minimum.Y;

        // ─────────────────────────────────────────────────────────────
        // 建立時用的
        // ─────────────────────────────────────────────────────────────

        /// <summary>還沒放的槽，照 <see cref="PlacementPriority"/> 排，同種的照槽號小的先。</summary>
        private static int[] BuildSearchOrder(bool[] isPlaced)
        {
            var order = new List<int>();
            foreach (PieceKind kind in PlacementPriority)
                for (int slot = 0; slot < isPlaced.Length; slot++)
                    if (!isPlaced[slot] && PiecePrototype.TangramSet[slot] == kind) order.Add(slot);
            return order.ToArray();
        }

        private static PieceNodeSet[] PosesOf(PieceKind kind)
        {
            lock (posesByKind)
            {
                if (posesByKind.TryGetValue(kind, out PieceNodeSet[] cached)) return cached;

                var poses = new List<PieceNodeSet>();
                int flipCount = PiecePrototype.CanFlip(kind) ? 2 : 1;
                for (int flipIndex = 0; flipIndex < flipCount; flipIndex++)
                    for (int rotation = 0; rotation < PiecePrototype.RotationCycleLength(kind); rotation++)
                        poses.Add(new PieceNodeSet(kind, rotation, flipIndex == 1));

                PieceNodeSet[] result = poses.ToArray();
                posesByKind[kind] = result;
                return result;
            }
        }

        /// <summary>
        /// 區域面積的兩倍。外環算正、洞算負，<b>不管各條環原本的繞向</b>——
        /// <see cref="Region.FromRings"/> 收什麼繞向都可以，所以不能直接把有向面積加起來。
        /// </summary>
        private static Scalar DoubleAreaOf(Region region)
        {
            Scalar total = Scalar.Zero;
            for (int ringIndex = 0; ringIndex < region.Rings.Length; ringIndex++)
            {
                Scalar ringArea = Geometry.SignedDoubleArea(region.Rings[ringIndex]);
                if (ringArea.Sign < 0) ringArea = -ringArea;
                total = region.RingIsOuter[ringIndex] ? total + ringArea : total - ringArea;
            }
            return total;
        }

        /// <summary>
        /// 一片擺放<b>佔住的位置</b>：片種＋排序過的頂點。跟姿態無關，所以可以當身分，也可以排出全序。
        /// </summary>
        private sealed class Footprint : IEquatable<Footprint>, IComparable<Footprint>
        {
            private readonly PieceKind kind;
            private readonly ExactVector2[] sortedVertices;
            private readonly int hash;

            public Footprint(PieceKind kind, ExactVector2[] vertices)
            {
                this.kind = kind;
                sortedVertices = (ExactVector2[])vertices.Clone();
                Array.Sort(sortedVertices);

                int accumulated = (int)kind;
                foreach (ExactVector2 vertex in sortedVertices)
                    accumulated = unchecked(accumulated * 397 ^ vertex.GetHashCode());
                hash = accumulated;
            }

            public bool Equals(Footprint other)
            {
                if (other is null || other.kind != kind || other.sortedVertices.Length != sortedVertices.Length)
                    return false;
                for (int i = 0; i < sortedVertices.Length; i++)
                    if (!sortedVertices[i].Equals(other.sortedVertices[i])) return false;
                return true;
            }

            public override bool Equals(object other) => other is Footprint footprint && Equals(footprint);

            public override int GetHashCode() => hash;

            /// <summary>先比片種，再逐一比排序過的頂點。只要是全序就夠用，意義不重要。</summary>
            public int CompareTo(Footprint other)
            {
                int byKind = kind.CompareTo(other.kind);
                if (byKind != 0) return byKind;
                int length = Math.Min(sortedVertices.Length, other.sortedVertices.Length);
                for (int i = 0; i < length; i++)
                {
                    int byVertex = sortedVertices[i].CompareTo(other.sortedVertices[i]);
                    if (byVertex != 0) return byVertex;
                }
                return sortedVertices.Length.CompareTo(other.sortedVertices.Length);
            }
        }
    }
}
