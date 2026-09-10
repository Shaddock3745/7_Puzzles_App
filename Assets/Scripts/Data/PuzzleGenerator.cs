using System;
using System.Collections.Generic;

namespace Tangram.Data
{
    /// <summary>審美篩選的四個門檻（P2-3）。全部**暫定**，等 P2-5 看過分布再調。</summary>
    public readonly struct AestheticLimits
    {
        /// <summary>洞最多幾個。有洞的形狀難看也難拼。</summary>
        public readonly int MaximumHoleCount;

        /// <summary>捏點最多幾個。捏點是「快斷掉」的細腰。</summary>
        public readonly int MaximumPinchPointCount;

        /// <summary>凹角最多幾個。太多就是鋸齒。</summary>
        public readonly int MaximumConcaveAngleCount;

        /// <summary>
        /// 周長上限。面積恆為 32，所以**周長就是緊湊度**——周長越長越細長多刺。
        ///
        /// 參考值：古典大正方形的周長是 16√2 ≈ 22.6，那大概是下限；
        /// 七片排成一長串的極端情況接近 40 + 20√2 ≈ 68.3。
        /// </summary>
        public readonly Scalar MaximumPerimeter;

        /// <param name="maximumHoleCount">洞最多幾個。</param>
        /// <param name="maximumPinchPointCount">捏點最多幾個。</param>
        /// <param name="maximumConcaveAngleCount">凹角最多幾個。</param>
        /// <param name="maximumPerimeter">周長上限。</param>
        public AestheticLimits(int maximumHoleCount, int maximumPinchPointCount,
                               int maximumConcaveAngleCount, Scalar maximumPerimeter)
        {
            MaximumHoleCount = maximumHoleCount;
            MaximumPinchPointCount = maximumPinchPointCount;
            MaximumConcaveAngleCount = maximumConcaveAngleCount;
            MaximumPerimeter = maximumPerimeter;
        }

        /// <summary>
        /// 暫定門檻，取自 <b>2026-09-10 對 300 個隨機形狀的實測分布</b>（門檻全開時的取樣）：
        ///
        /// <list type="bullet">
        /// <item>凹角：中位 8、90% 9、最大 11 → 取 <b>8</b></item>
        /// <item>捏點：中位 0、90% 2、最大 4 → 取 <b>1</b></item>
        /// <item>洞：中位 0、最大 1 → 取 <b>0</b></item>
        /// <item>周長：最小 29.0、中位 41.7、90% 46.6 → 取 <b>44</b></item>
        /// </list>
        ///
        /// ⚠️ <b>第一版的門檻是憑感覺猜的，四個裡有兩個錯得離譜</b>——
        /// 周長猜 44 而當時實測的最小值是 44.3（等於全部篩掉），捏點猜 1 而中位數是 4。
        /// 那次 1027 次嘗試只中 3 題（0.3%）。真正的修法不是放寬門檻，
        /// 是讓生成本身偏向緊湊（見 <c>PuzzleGenerator.CollectLegalPlacements</c>）。
        ///
        /// ⚠️ P2-5 跑一千題之後回來重訂，**而且要順便看周長的分布**——
        /// 周長同時是審美的門檻與難度的特徵（藏起來的邊），篩太兇會把難度的變化範圍一起削掉。
        /// </summary>
        public static AestheticLimits Default => new AestheticLimits(
            maximumHoleCount: 0,
            maximumPinchPointCount: 1,
            maximumConcaveAngleCount: 8,
            maximumPerimeter: Scalar.FromInteger(44));
    }

    /// <summary>生一次的結果。</summary>
    public enum GenerationOutcome
    {
        /// <summary>生出來了。</summary>
        Generated,

        /// <summary>七片拼不起來（回溯用完或超過上限）。</summary>
        PackingFailed,

        /// <summary>拼起來了但太醜。</summary>
        RejectedByAesthetics,

        /// <summary>拼起來也夠好看，但這個形狀之前生過。</summary>
        Duplicate
    }

    /// <summary>
    /// 生一次的完整結果。
    ///
    /// ⚠️ <b>失敗的三種原因刻意分開</b>，因為 P2-5 要量的就是它們的比例：
    /// 拼不起來太多代表回溯策略有問題，太醜太多代表門檻太嚴，重複太多代表隨機性不夠散。
    /// 合成一個 bool 的話這三件事就分不出來了。
    /// </summary>
    public readonly struct GenerationResult
    {
        /// <summary>這次的結果。</summary>
        public readonly GenerationOutcome Outcome;

        /// <summary>成功時的題目；其餘情況是 null。</summary>
        public readonly Puzzle Puzzle;

        /// <summary>
        /// 成功時的參考解，**索引就是槽索引**（對應 <see cref="PiecePrototype.TangramSet"/>）。
        /// 已經跟題目一起正規化過，兩者保證同一個座標系。其餘情況是 null。
        /// </summary>
        public readonly Placement[] ReferenceSolution;

        /// <summary>
        /// 這個形狀的特徵。**被審美篩掉時也會帶回來**——那正是調門檻要看的東西。
        /// 拼不起來時是 default。
        /// </summary>
        public readonly DifficultyFeatures Features;

        /// <param name="outcome">這次的結果。</param>
        /// <param name="puzzle">題目，失敗時傳 null。</param>
        /// <param name="referenceSolution">參考解，失敗時傳 null。</param>
        /// <param name="features">形狀的特徵。</param>
        public GenerationResult(GenerationOutcome outcome, Puzzle puzzle,
                                Placement[] referenceSolution, DifficultyFeatures features)
        {
            Outcome = outcome;
            Puzzle = puzzle;
            ReferenceSolution = referenceSolution;
            Features = features;
        }
    }

    /// <summary>
    /// 出題器（P2-3）：**先把七片隨便拼在一起，拼出來的外框就是題目。**
    ///
    /// <b>為什麼是倒過來做</b>：正著做（先畫形狀再想辦法拼）既難又不保證拼得出來。
    /// 倒過來做的話**題目天生就有解**——它本來就是從一個解長出來的，
    /// 所以完全不需要求解器來確認「這題拼得出來嗎」。
    ///
    /// <b>這一版不管難度</b>。「照指定難度生」是 P2-7 的事，它會在這一版外面
    /// 多包一層「算難度、不合就丟掉重生」。分開是因為那需要 P2-6 的絕對尺度才有門檻可比。
    ///
    /// ⚠️ <b>放 `Data` 不放 `Core`</b>：它碰到的每一個型別都在 `Data`
    /// （<see cref="PlacementCandidates"/>、<see cref="Geometry"/>、<see cref="BoundaryTracer"/>、
    /// <see cref="Canonical"/>、<see cref="DifficultyFeatures"/>），沒有任何一關的狀態。
    /// 放 `Core` 的話 `Data` 這層的工具就用不到它了。企劃書 §5.2 的模組樹要跟著改一行。
    /// </summary>
    public sealed class PuzzleGenerator
    {
        /// <summary>一次拼裝最多試幾個候選就放棄。防的是病態情況卡很久，不是正確性。</summary>
        private const int MaximumPlacementAttempts = 4000;

        private readonly Random random;
        private readonly AestheticLimits limits;
        private readonly HashSet<string> seenKeys = new HashSet<string>();

        /// <param name="seed">亂數種子。<b>同一個種子必須生出同一串題目</b>，批次統計才可重現。</param>
        /// <param name="limits">審美門檻。不傳就用暫定值。</param>
        public PuzzleGenerator(int seed, AestheticLimits? limits = null)
        {
            random = new Random(seed);
            this.limits = limits ?? AestheticLimits.Default;
        }

        /// <summary>到目前為止生出過幾個相異的形狀。</summary>
        public int DistinctPuzzleCount => seenKeys.Count;

        /// <summary>
        /// 生一題。
        ///
        /// ⚠️ <b>失敗就直接回報，不會自己重試到成功為止。</b>
        /// 自己重試的話「試幾次中一題」這個數字就消失了，而那正是 P2-5 要量的東西之一。
        /// 呼叫端要幾題就自己叫幾次。
        /// </summary>
        /// <returns>這次的結果，含失敗的原因。</returns>
        public GenerationResult TryGenerate()
        {
            Placement[] piecesBySlot = TryPackSevenPieces();
            if (piecesBySlot == null)
                return new GenerationResult(GenerationOutcome.PackingFailed, null, null, default);

            // 先描一次外框算特徵（便宜），醜的在這裡就丟掉，
            // 省下正規化那 16 次追蹤（貴）。順序反過來的話每一題都要先付那筆錢
            List<Walk> walks = BoundaryTracer.Trace(piecesBySlot);
            DifficultyFeatures features = DifficultyFeatures.Measure(walks);

            if (!PassesAesthetics(features))
                return new GenerationResult(GenerationOutcome.RejectedByAesthetics, null, null, features);

            // 存正規化的樣子：同一個形狀不管從哪一種擺法生出來，存進去都一模一樣。
            // ⚠️ 參考解由同一支一起回報，所以它跟外框保證在同一個座標系——
            // 分兩次算的話會出現「題目在一個角度、答案在另一個角度」而且不報錯
            CanonicalPuzzle normalized = Canonical.NormalizePuzzle(piecesBySlot);

            if (!seenKeys.Add(normalized.Key))
                return new GenerationResult(GenerationOutcome.Duplicate, null, null, features);

            var puzzle = new Puzzle(normalized.Key, normalized.Walks, PuzzleSource.Generated);
            return new GenerationResult(GenerationOutcome.Generated, puzzle, normalized.Pieces, features);
        }

        /// <param name="features">這個形狀的特徵。</param>
        /// <returns>四個門檻是否全部通過。</returns>
        private bool PassesAesthetics(DifficultyFeatures features)
        {
            if (features.HoleCount > limits.MaximumHoleCount) return false;
            if (features.PinchPointCount > limits.MaximumPinchPointCount) return false;
            if (features.ConcaveAngleCount > limits.MaximumConcaveAngleCount) return false;

            // 周長 = 七片周長總和 − 藏起來的長度 ×2（DifficultyFeatures 的那條恆等式反解）
            Scalar perimeter = DifficultyFeatures.TotalPiecePerimeter - features.DoubledHiddenEdgeLength;
            return perimeter <= limits.MaximumPerimeter;
        }

        /// <summary>
        /// 把七片拼在一起，回傳**依槽索引排好**的擺放。
        ///
        /// <b>為什麼直接寫進槽索引</b>：放置順序是洗牌過的，但盤面的七個槽與片種是固定對應的
        /// （<see cref="PiecePrototype.TangramSet"/>）。邊放邊寫進正確的槽，
        /// 「同種片放錯槽」這個錯誤就不可能發生——事後再對應的話，
        /// 兩片大三角、兩片小三角很容易配錯，而症狀要到載入遊戲才看得出來。
        /// </summary>
        /// <returns>七片的擺放，索引即槽索引；拼不起來時回 null。</returns>
        private Placement[] TryPackSevenPieces()
        {
            PieceKind[] tangramSet = PiecePrototype.TangramSet;
            int pieceCount = tangramSet.Length;

            int[] slotOrder = ShuffledSlotOrder(pieceCount);
            var piecesBySlot = new Placement[pieceCount];
            var placedSoFar = new List<Placement>(pieceCount);

            // 每一層記住「洗好的候選」與「試到第幾個」，退回來才不會一直重試同一個
            var candidatesByDepth = new List<Placement>[pieceCount];
            var nextCandidateByDepth = new int[pieceCount];

            // ⚠️ 第一片直接放原點、不轉、不翻。整組轉幾度、翻不翻最後都會被正規化吃掉，
            // 所以第一片的姿態是一個**沒有意義的自由度**——固定它把搜尋空間縮掉十六分之一，
            // 而且一個形狀都不會少
            int firstSlot = slotOrder[0];
            piecesBySlot[firstSlot] = new Placement(tangramSet[firstSlot], 0, false, ExactVector2.Zero);
            placedSoFar.Add(piecesBySlot[firstSlot]);

            int depth = 1;
            int attemptsLeft = MaximumPlacementAttempts;

            while (depth < pieceCount)
            {
                if (candidatesByDepth[depth] == null)
                {
                    var (edgeContact, pointContact) =
                        CollectLegalPlacements(tangramSet[slotOrder[depth]], placedSoFar);

                    // 兩層各自洗牌再接起來：貼著邊的先試完，才退到只碰一點的。
                    // 這樣「偏好緊湊」是一條明確的規則，不是靠機率偏斜
                    Shuffle(edgeContact);
                    Shuffle(pointContact);
                    edgeContact.AddRange(pointContact);

                    candidatesByDepth[depth] = edgeContact;
                    nextCandidateByDepth[depth] = 0;
                }

                if (nextCandidateByDepth[depth] >= candidatesByDepth[depth].Count || attemptsLeft <= 0)
                {
                    // 這一層走完了，退回上一層換它的下一個候選
                    candidatesByDepth[depth] = null;
                    depth--;
                    if (depth < 1) return null;          // 第一片沒有別的選擇，整題放棄
                    placedSoFar.RemoveAt(placedSoFar.Count - 1);
                    continue;
                }

                Placement chosen = candidatesByDepth[depth][nextCandidateByDepth[depth]];
                nextCandidateByDepth[depth]++;
                attemptsLeft--;

                piecesBySlot[slotOrder[depth]] = chosen;
                placedSoFar.Add(chosen);
                depth++;
            }

            return piecesBySlot;
        }

        /// <summary>
        /// 列出這一片所有「貼得上去而且不重疊」的擺放。
        ///
        /// <b>回傳分成兩層：貼著邊的、只碰到一點的。</b>
        ///
        /// ⚠️ <b>這個分層是實測逼出來的，不是先驗的偏好。</b>
        /// 原本一視同仁隨機挑，結果 300 次取樣的周長中位數是 <b>59.3</b>
        /// （古典大正方形是 22.6，理論上限 68.3），捏點中位數是 <b>4</b>——
        /// 也就是說<b>「隨便拼」幾乎必然生出細長、靠點接觸串起來的東西</b>。
        /// 原因很直接：新的一片只要碰到任何一個節點就算合法，而點接觸的機會遠多於邊貼合。
        ///
        /// 那時的選擇有兩個：放寬審美門檻去接受那些形狀，或者讓生成本身偏向緊湊。
        /// <b>放寬門檻等於讓審美篩選失去意義</b>，所以改成分層——
        /// 先試貼著邊的，都走不通才退到點接觸（保留它是為了不漏掉真的需要點接觸的形狀）。
        /// </summary>
        /// <param name="kind">要放的片種。</param>
        /// <param name="placedSoFar">已經放好的片，同時是錨點來源與重疊比對對象。</param>
        /// <returns>兩層合法擺放：貼著邊的、只碰到一點的。都可能是空的。</returns>
        private static (List<Placement> edgeContact, List<Placement> pointContact)
            CollectLegalPlacements(PieceKind kind, IReadOnlyList<Placement> placedSoFar)
        {
            // ⚠️ 錨點要去重。相鄰的片共用貼合處的節點，不去重的話同一個點會被算好幾次，
            // 候選數量跟著膨脹（那些重複的候選在下面還是會被 distinct 濾掉，白算）
            var anchorSet = new HashSet<ExactVector2>();
            foreach (Placement placed in placedSoFar)
                foreach (ExactVector2 node in placed.Nodes()) anchorSet.Add(node);
            var anchors = new List<ExactVector2>(anchorSet);

            var candidates = new List<PlacementCandidate>();
            int rotationCycle = PiecePrototype.RotationCycleLength(kind);
            int flipCount = PiecePrototype.CanFlip(kind) ? 2 : 1;

            for (int flipIndex = 0; flipIndex < flipCount; flipIndex++)
                for (int rotation = 0; rotation < rotationCycle; rotation++)
                    PlacementCandidates.Enumerate(
                        new PieceNodeSet(kind, rotation, flipIndex == 1), anchors, candidates);

            // 已放置片的頂點與包圍盒只算一次。⚠️ 放在候選迴圈裡算的話，
            // Placement.Vertices() 會為每一個候選重新配置一次陣列——實測那是最大的一筆成本
            var placedPolygons = new ExactVector2[placedSoFar.Count][];
            var placedBounds = new BoundingBox[placedSoFar.Count];
            for (int pieceIndex = 0; pieceIndex < placedSoFar.Count; pieceIndex++)
            {
                placedPolygons[pieceIndex] = placedSoFar[pieceIndex].Vertices();
                placedBounds[pieceIndex] = BoundingBox.FromPoints(placedPolygons[pieceIndex]);
            }

            // ⚠️ 重複的候選不是丟掉就好，**要數**。同一個擺放被幾組（錨點, 節點）湊出來，
            // 就等於它跟已放置的片重合了幾個節點——
            // **1 個 = 只碰到一點（捏點），2 個以上 = 真的貼著一條邊**。
            // 這個數字是免費的，枚舉的時候本來就會產生
            var contactCounts = new Dictionary<Placement, int>();
            foreach (PlacementCandidate candidate in candidates)
            {
                contactCounts.TryGetValue(candidate.Placement, out int count);
                contactCounts[candidate.Placement] = count + 1;
            }

            var edgeContact = new List<Placement>();
            var pointContact = new List<Placement>();

            foreach (KeyValuePair<Placement, int> entry in contactCounts)
            {
                ExactVector2[] candidatePolygon = entry.Key.Vertices();
                BoundingBox candidateBounds = BoundingBox.FromPoints(candidatePolygon);

                bool overlaps = false;
                for (int pieceIndex = 0; pieceIndex < placedPolygons.Length && !overlaps; pieceIndex++)
                {
                    // 包圍盒完全分開就不必跑 SAT。⚠️ 用嚴格比較，
                    // 「盒子剛好貼在一起」還是要走 SAT——那正是邊貼合的情況，SAT 會正確地回不重疊
                    if (AreDefinitelyApart(candidateBounds, placedBounds[pieceIndex])) continue;
                    overlaps = Geometry.ConvexOverlap(candidatePolygon, placedPolygons[pieceIndex]);
                }

                if (overlaps) continue;
                if (entry.Value >= 2) edgeContact.Add(entry.Key);
                else pointContact.Add(entry.Key);
            }
            return (edgeContact, pointContact);
        }

        /// <summary>
        /// 兩個包圍盒是不是**確定**分開的。只用來省下 SAT，回 false 不代表重疊。
        /// </summary>
        /// <param name="first">一個包圍盒。</param>
        /// <param name="second">另一個包圍盒。</param>
        /// <returns>確定沒有交集就回 true；貼在一起或有交集都回 false。</returns>
        private static bool AreDefinitelyApart(BoundingBox first, BoundingBox second)
            => first.Maximum.X < second.Minimum.X
            || second.Maximum.X < first.Minimum.X
            || first.Maximum.Y < second.Minimum.Y
            || second.Maximum.Y < first.Minimum.Y;

        /// <summary>
        /// 槽索引洗牌。
        /// ⚠️ 不洗的話第一片永遠是大三角（<c>TangramSet</c> 的第 0 個），生出來的形狀會偏。
        /// </summary>
        /// <param name="pieceCount">片數。</param>
        /// <returns>洗過的槽索引順序。</returns>
        private int[] ShuffledSlotOrder(int pieceCount)
        {
            var order = new int[pieceCount];
            for (int i = 0; i < pieceCount; i++) order[i] = i;

            for (int i = pieceCount - 1; i > 0; i--)
            {
                int swapWith = random.Next(i + 1);
                (order[i], order[swapWith]) = (order[swapWith], order[i]);
            }
            return order;
        }

        /// <summary>Fisher-Yates 就地洗牌。</summary>
        /// <param name="items">要洗的清單，會被就地修改。</param>
        private void Shuffle(List<Placement> items)
        {
            for (int i = items.Count - 1; i > 0; i--)
            {
                int swapWith = random.Next(i + 1);
                (items[i], items[swapWith]) = (items[swapWith], items[i]);
            }
        }
    }
}
