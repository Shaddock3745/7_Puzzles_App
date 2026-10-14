using System;
using System.Collections.Generic;
using System.Text;
using UnityEngine;

namespace Tangram.Data
{
    /// <summary>
    /// 逼近搜尋的參數（2026-10-14）。<b>全部都是猜的</b>，要用驗收工具（<c>ApproximatorTable</c>）跑過才知道該多少。
    /// </summary>
    public sealed class ContourApproximationSettings
    {
        /// <summary>每一步留幾條路（beam 的寬度）。越大越準、越慢，成本大約成正比。</summary>
        public readonly int BeamWidth;

        /// <summary>
        /// 打分用的格子邊長（模型單位）。1/8 的話大三角約蓋 512 格。
        /// 格子越細，分數越接近真的面積，但每個候選要數的格子是平方成長。
        /// </summary>
        public readonly double CellSize;

        /// <summary>伸到輪廓外的一格扣幾分（蓋到裡面的一格是 +1）。1 ＝ 裡外一樣重要。</summary>
        public readonly double OutsidePenalty;

        /// <summary>
        /// 第一片的平移每隔多少取一個（模型單位）。<b>對不準的量最多是它的一半</b>——
        /// 之後每一片都貼著前面的放，整組的位置就由這一步決定，後面不會再修正。
        /// </summary>
        public readonly double OffsetSpacing;

        /// <summary>最後回幾個相異的形狀。</summary>
        public readonly int ResultCount;

        /// <summary>
        /// ⭐ <b>平移微調做幾輪</b>（2026-10-14 第二版）。每一步留下 N 條路之後，各自在目前的平移附近試 8 個方向，
        /// 第一輪步長是平移間隔的一半，每輪減半。<b>0 ＝ 不微調，等於第一版</b>（拿來對照）。
        /// 預設 2 輪：再細就比格子小很多，分數只會跟著格子邊界隨機跳。
        /// </summary>
        public readonly int RefinementRounds;

        /// <summary>
        /// ⭐ <b>從第幾片開始微調</b>（2026-10-14 第三版），1 ～ 7：這一步放完之後，已放的片數 ≥ 它才微調。
        /// 1 ＝ 第一片就微調（第二版）、7 ＝ 只在七片都放完時微調。<see cref="RefinementRounds"/> 是 0 時這個沒有作用。
        ///
        /// <b>為什麼要有它</b>：第二版魚2 掉了，推測是只有一兩片時，每條路都能把輪廓移到「這幾片剛好全在裡面」的位置，
        /// 分數變得很接近、排名失去鑑別力，對的那條路被擠出前 N 名。片多了，輪廓才有足夠的形狀可以對。
        ///
        /// <b>預設 5</b>（2026-10-14 在 Release 掃了 1／3／5／7）：3 與 5 都讓魚2 回來（逼回 6 個、平均 IoU 0.923），
        /// 平手時照事先講好的規則選比較快的 5。1（第二版）逼回 5 個、平均 0.915；7 逼回 6 個但平均 0.921。
        /// </summary>
        public readonly int RefinementStartPieceCount;

        /// <param name="beamWidth">每一步留幾條路，至少 1。</param>
        /// <param name="cellSize">格子邊長，必須是正數。</param>
        /// <param name="outsidePenalty">伸到外面一格扣幾分，不能是負數。</param>
        /// <param name="offsetSpacing">第一片平移的取樣間隔，必須是正數。</param>
        /// <param name="resultCount">回幾個形狀，至少 1。</param>
        /// <param name="refinementRounds">平移微調做幾輪，不能是負數；0 ＝ 不微調。</param>
        /// <param name="refinementStartPieceCount">從第幾片開始微調，1 ～ 7。</param>
        /// <exception cref="ArgumentOutOfRangeException">任何一個參數超出範圍。</exception>
        public ContourApproximationSettings(int beamWidth = 100, double cellSize = 0.125, double outsidePenalty = 1.0,
                                            double offsetSpacing = 0.25, int resultCount = 5, int refinementRounds = 2,
                                            int refinementStartPieceCount = 5)
        {
            if (refinementStartPieceCount < 1 || refinementStartPieceCount > 7)
                throw new ArgumentOutOfRangeException(nameof(refinementStartPieceCount), refinementStartPieceCount, "從第幾片開始微調要在 1 ～ 7。");
            if (beamWidth < 1) throw new ArgumentOutOfRangeException(nameof(beamWidth), beamWidth, "至少要留 1 條路。");
            if (!(cellSize > 0.0)) throw new ArgumentOutOfRangeException(nameof(cellSize), cellSize, "格子邊長必須是正數。");
            if (!(outsidePenalty >= 0.0)) throw new ArgumentOutOfRangeException(nameof(outsidePenalty), outsidePenalty, "懲罰不能是負數。");
            if (!(offsetSpacing > 0.0)) throw new ArgumentOutOfRangeException(nameof(offsetSpacing), offsetSpacing, "平移間隔必須是正數。");
            if (resultCount < 1) throw new ArgumentOutOfRangeException(nameof(resultCount), resultCount, "至少要回 1 個。");
            if (refinementRounds < 0) throw new ArgumentOutOfRangeException(nameof(refinementRounds), refinementRounds, "微調輪數不能是負數。");

            BeamWidth = beamWidth;
            CellSize = cellSize;
            OutsidePenalty = outsidePenalty;
            OffsetSpacing = offsetSpacing;
            ResultCount = resultCount;
            RefinementRounds = refinementRounds;
            RefinementStartPieceCount = refinementStartPieceCount;
        }

        /// <summary>起始值：N = 100、格子 1/8、懲罰 1、平移間隔 0.25、回 5 個、微調 2 輪、從第 5 片開始。</summary>
        public static ContourApproximationSettings Default => new ContourApproximationSettings();

        /// <returns>給報告看的一行。</returns>
        public override string ToString() =>
            $"N={BeamWidth} 格子={CellSize} 懲罰={OutsidePenalty} 平移間隔={OffsetSpacing} " +
            (RefinementRounds == 0 ? "不微調" : $"微調={RefinementRounds}輪（從第{RefinementStartPieceCount}片）");
    }

    /// <summary>逼近出來的一個形狀。</summary>
    public sealed class ContourApproximation
    {
        /// <summary>七片的擺放，<b>索引是槽索引</b>（<see cref="PiecePrototype.TangramSet"/>），在題目自己的座標系。同時就是參考解。</summary>
        public readonly Placement[] PiecesBySlot;

        /// <summary>從七片直接描出來的題目。<b>沒有正規化</b>，方向跟輪廓一樣（貓是正的）。</summary>
        public readonly Puzzle Puzzle;

        /// <summary>跟輪廓的交集 ÷ 聯集，用格子數算的，0 ～ 1。</summary>
        public readonly double IntersectionOverUnion;

        /// <summary>
        /// 輪廓要平移多少才對得上這組片（模型單位）：題目座標裡的點 ＝ 輪廓座標裡的點 ＋ 這個量。
        /// 之後要把題目疊回照片上畫時用得到。
        /// </summary>
        public readonly Vector2 OutlineOffset;

        internal ContourApproximation(Placement[] piecesBySlot, Puzzle puzzle, double intersectionOverUnion, Vector2 outlineOffset)
        {
            PiecesBySlot = piecesBySlot;
            Puzzle = puzzle;
            IntersectionOverUnion = intersectionOverUnion;
            OutlineOffset = outlineOffset;
        }
    }

    /// <summary>
    /// ⭐ <b>逼近搜尋：最笨的 beam search</b>（2026-10-14）。給一條輪廓，找出長得最像它、而且一定拼得出來的七片擺法。
    ///
    /// <b>核心想法</b>：不去「修」輪廓——照片的輪廓面積不是 32、頂點不在格點上，修不成合法的題目。
    /// 改成在<b>拼得出來的擺法</b>裡搜尋離它最近的那一個：骨架就是出題器的貼片步驟
    /// （<see cref="PuzzleGenerator.CollectLegalPlacements"/>），只是把「隨機挑一個」換成「照分數留前 N 個」。
    /// 候選本來就全部合法，所以<b>結果永遠拼得出來</b>，只是有時候不夠像。
    ///
    /// <b>一步一步</b>：
    /// <list type="number">
    /// <item><b>鋪格子</b>：輪廓（已經縮到面積 32）的範圍鋪格子，每格中心在不在輪廓裡算一次存起來</item>
    /// <item><b>第一片</b>：大三角固定在原點、各種轉向都試；<b>改成移動輪廓</b>——輪廓是浮點可以隨便平移，
    ///   片的座標維持精確（鐵則一）。輪廓裡每隔一段取一個點，讓大三角的重心對上它</item>
    /// <item><b>之後每一片</b>：照固定順序（大到小）列出所有貼得上、不重疊的擺法（貼邊與碰點都收），
    ///   打分、去重、留前 N 個</item>
    /// <item><b>平移微調</b>（第二版）：每一步留下 N 條路之後，各自把輪廓往 8 個方向小移一點，
    ///   整組重算分數，有比較好就移過去。<b>只動輪廓、不動任何一片</b>，所以合法性不受影響</item>
    /// <item><b>收尾</b>：算 IoU、同一個形狀只留一個、回前幾名</item>
    /// </list>
    ///
    /// <b>打分</b>：一片 ＝ 蓋到輪廓裡的格數 − 伸到外面的格數 × 懲罰。片跟片保證不重疊，所以整組就是各片相加，
    /// IoU 也直接從兩個計數算得出來（交集 ＝ 裡面的格數，聯集 ＝ 輪廓的格數 ＋ 外面的格數）。
    ///
    /// ⚠️ <b>浮點只用來打分與排名</b>，挑出來的永遠是精確的 <see cref="Placement"/>。
    ///
    /// ⚠️ <b>刻意沒做的</b>（先看失敗長怎樣再決定加哪一樣）：重要部位加權、洞、審美門檻、非同步。
    /// 細節在 <c>待辦清單.md</c>〈掃描模式：還沒做的〉第 6 條。
    /// </summary>
    public sealed class BeamSearchContourPuzzleBuilder : IContourPuzzleBuilder
    {
        /// <summary>
        /// 放片的順序，<b>存的是槽索引</b>：大三角 → 大三角 → 中三角 → 正方形 → 平行四邊形 → 小三角 → 小三角。
        /// 大的先放，因為大片的位置最能決定像不像；小片最後拿來補縫。
        /// </summary>
        private static readonly int[] SlotOrder = { 0, 1, 2, 5, 6, 3, 4 };

        private readonly ContourApproximationSettings settings;

        /// <param name="settings">參數；不給就用 <see cref="ContourApproximationSettings.Default"/>。</param>
        public BeamSearchContourPuzzleBuilder(ContourApproximationSettings settings = null)
        {
            this.settings = settings ?? ContourApproximationSettings.Default;
        }

        /// <inheritdoc/>
        /// <remarks>只拿第 1 名。⚠️ 同步執行，在手機上可能要好幾秒。</remarks>
        public ContourPuzzle Build(RecognizedContour contour)
        {
            IReadOnlyList<ContourApproximation> results = Approximate(contour);
            if (results.Count == 0) return null;

            ContourApproximation best = results[0];
            return new ContourPuzzle($"掃描題（IoU {best.IntersectionOverUnion:0.00}）", best.Puzzle, best.PiecesBySlot);
        }

        /// <summary>
        /// 跑一次逼近，回前幾名（照 IoU 由高到低，形狀互不相同）。
        /// </summary>
        /// <param name="contour">辨識出來的輪廓。</param>
        /// <returns>前幾名，最多 <see cref="ContourApproximationSettings.ResultCount"/> 個；一個都逼不出來時是空的。</returns>
        /// <exception cref="ArgumentNullException"><paramref name="contour"/> 是 null。</exception>
        public IReadOnlyList<ContourApproximation> Approximate(RecognizedContour contour)
        {
            if (contour == null) throw new ArgumentNullException(nameof(contour));

            var raster = new OutlineRaster(contour.ModelOutline, settings.CellSize);
            List<SearchState> beam = Refine(FirstPieceStates(raster), raster);

            for (int depth = 1; depth < SlotOrder.Length && beam.Count > 0; depth++)
                beam = Refine(Expand(beam, depth, raster), raster);

            return Finish(beam, raster);
        }

        // ───────── 平移微調（第二版）─────────

        /// <summary>
        /// 微調時試的 8 個方向。<b>順序是固定的</b>：分數平手時取先試到的那個，搜尋才可重現。
        /// </summary>
        private static readonly (int x, int y)[] RefinementDirections =
        {
            (1, 0), (-1, 0), (0, 1), (0, -1), (1, 1), (1, -1), (-1, 1), (-1, -1)
        };

        /// <summary>
        /// 對<b>留下來的那 N 條路</b>各做一次平移微調。不對全部子狀態做：子狀態有幾萬個，成本差幾百倍。
        /// </summary>
        private List<SearchState> Refine(List<SearchState> beam, OutlineRaster raster)
        {
            if (settings.RefinementRounds == 0 || beam.Count == 0) return beam;

            // 同一步的路片數都一樣，看第一條就好
            if (beam[0].Placements.Length < settings.RefinementStartPieceCount) return beam;

            var refined = new List<SearchState>(beam.Count);
            foreach (SearchState state in beam) refined.Add(Refine(state, raster));
            return refined;
        }

        /// <summary>
        /// 一條路的微調：在目前的平移周圍試 8 個方向，<b>已經放好的每一片都用新的平移重算</b>，
        /// 有比現在高的就移到最高的那個；步長每輪減半。
        ///
        /// ⚠️ <b>只動輪廓，不動任何一片</b>——片的擺法是精確的，結果照樣保證拼得出來。
        /// 分數一樣時留在原地（嚴格大於才移），8 個方向平手時取 <see cref="RefinementDirections"/> 裡先出現的。
        /// </summary>
        private SearchState Refine(SearchState state, OutlineRaster raster)
        {
            var polygons = new PiecePolygon[state.Placements.Length];
            for (int i = 0; i < polygons.Length; i++) polygons[i] = PiecePolygon.From(state.Placements[i]);

            double currentX = state.OffsetX, currentY = state.OffsetY;
            int currentInside = state.InsideCells, currentOutside = state.OutsideCells;
            double currentScore = state.Score;
            double step = settings.OffsetSpacing / 2.0;

            for (int round = 0; round < settings.RefinementRounds; round++, step /= 2.0)
            {
                double bestX = currentX, bestY = currentY, bestScore = currentScore;
                int bestInside = currentInside, bestOutside = currentOutside;

                foreach ((int x, int y) direction in RefinementDirections)
                {
                    double candidateX = currentX + direction.x * step;
                    double candidateY = currentY + direction.y * step;

                    int inside = 0, outside = 0;
                    foreach (PiecePolygon polygon in polygons)
                    {
                        (int pieceInside, int pieceOutside) = raster.Count(polygon, candidateX, candidateY);
                        inside += pieceInside;
                        outside += pieceOutside;
                    }

                    double score = inside - outside * settings.OutsidePenalty;
                    if (score > bestScore)
                    {
                        bestX = candidateX; bestY = candidateY; bestScore = score;
                        bestInside = inside; bestOutside = outside;
                    }
                }

                currentX = bestX; currentY = bestY; currentScore = bestScore;
                currentInside = bestInside; currentOutside = bestOutside;
            }

            return new SearchState(state.Placements, state.OffsetIndex, currentX, currentY,
                                   currentInside, currentOutside, settings.OutsidePenalty);
        }

        // ───────── 第一片 ─────────

        /// <summary>
        /// 第一片的所有起始狀態，打完分只留前 N 個。
        /// 大三角放原點、每種轉向一次，再讓它的重心對上輪廓裡每一個取樣點。
        /// </summary>
        private List<SearchState> FirstPieceStates(OutlineRaster raster)
        {
            int firstSlot = SlotOrder[0];
            PieceKind kind = PiecePrototype.TangramSet[firstSlot];
            List<(double x, double y)> samplePoints = raster.SamplePointsInside(settings.OffsetSpacing);

            var states = new List<SearchState>();
            int rotationCycle = PiecePrototype.RotationCycleLength(kind);
            int flipCount = PiecePrototype.CanFlip(kind) ? 2 : 1;

            for (int flipIndex = 0; flipIndex < flipCount; flipIndex++)
                for (int rotation = 0; rotation < rotationCycle; rotation++)
                {
                    var placement = new Placement(kind, rotation, flipIndex == 1, ExactVector2.Zero);
                    PiecePolygon polygon = PiecePolygon.From(placement);

                    for (int sampleIndex = 0; sampleIndex < samplePoints.Count; sampleIndex++)
                    {
                        // 題目座標的點 ＝ 輪廓座標的點 ＋ 平移，要讓片的重心（題目座標）落在取樣點（輪廓座標）上
                        double offsetX = polygon.CentroidX - samplePoints[sampleIndex].x;
                        double offsetY = polygon.CentroidY - samplePoints[sampleIndex].y;

                        (int inside, int outside) = raster.Count(polygon, offsetX, offsetY);
                        states.Add(new SearchState(new[] { placement }, sampleIndex, offsetX, offsetY,
                                                   inside, outside, settings.OutsidePenalty));
                    }
                }

            return KeepBest(states);
        }

        // ───────── 之後每一片 ─────────

        /// <summary>每條留著的路都放下一片，所有子狀態打分，去重之後留前 N 個。</summary>
        private List<SearchState> Expand(List<SearchState> beam, int depth, OutlineRaster raster)
        {
            PieceKind kind = PiecePrototype.TangramSet[SlotOrder[depth]];
            var children = new List<SearchState>();

            foreach (SearchState parent in beam)
            {
                var (edgeContact, pointContact) = PuzzleGenerator.CollectLegalPlacements(kind, parent.Placements);

                // 貼邊與碰點都收：貓耳朵這種地方本來就要靠點接觸。偏不偏好貼邊交給分數決定
                AddChildren(parent, edgeContact, raster, children);
                AddChildren(parent, pointContact, raster, children);
            }

            return KeepBest(children);
        }

        private void AddChildren(SearchState parent, List<Placement> candidates, OutlineRaster raster, List<SearchState> children)
        {
            foreach (Placement candidate in candidates)
            {
                (int inside, int outside) = raster.Count(PiecePolygon.From(candidate), parent.OffsetX, parent.OffsetY);

                var placements = new Placement[parent.Placements.Length + 1];
                Array.Copy(parent.Placements, placements, parent.Placements.Length);
                placements[parent.Placements.Length] = candidate;

                children.Add(new SearchState(placements, parent.OffsetIndex, parent.OffsetX, parent.OffsetY,
                                             parent.InsideCells + inside, parent.OutsideCells + outside,
                                             settings.OutsidePenalty));
            }
        }

        /// <summary>
        /// 照分數由高到低，去重，留前 N 個。
        /// 💡 <b>去重的 key 只對真的要留的那些算</b>——子狀態可能有幾萬個，每個都組字串很浪費。
        /// </summary>
        private List<SearchState> KeepBest(List<SearchState> states)
        {
            // 分數一樣時照產生的順序（List.Sort 不穩定，所以用索引當第二鍵），結果才可重現
            var order = new int[states.Count];
            for (int i = 0; i < order.Length; i++) order[i] = i;
            Array.Sort(order, (left, right) =>
            {
                int byScore = states[right].Score.CompareTo(states[left].Score);
                return byScore != 0 ? byScore : left.CompareTo(right);
            });

            var kept = new List<SearchState>(settings.BeamWidth);
            var seenKeys = new HashSet<string>();
            foreach (int index in order)
            {
                if (kept.Count >= settings.BeamWidth) break;
                if (seenKeys.Add(states[index].DeduplicationKey())) kept.Add(states[index]);
            }
            return kept;
        }

        // ───────── 收尾 ─────────

        /// <summary>
        /// 七片都放完的狀態照 IoU 排，同一個形狀（<see cref="Puzzle.Key"/>）只留一個。
        /// ⚠️ 描外框要跑 16 次輪廓追蹤，所以照 IoU 順序一個一個描，湊滿就停，不全部描。
        /// </summary>
        private IReadOnlyList<ContourApproximation> Finish(List<SearchState> beam, OutlineRaster raster)
        {
            var finished = new List<(SearchState state, double intersectionOverUnion)>();
            foreach (SearchState state in beam)
                if (state.Placements.Length == SlotOrder.Length)
                    finished.Add((state, (double)state.InsideCells / (raster.InsideCellCount + state.OutsideCells)));

            finished.Sort((left, right) => right.intersectionOverUnion.CompareTo(left.intersectionOverUnion));

            var results = new List<ContourApproximation>();
            var seenPuzzleKeys = new HashSet<string>();
            foreach ((SearchState state, double intersectionOverUnion) in finished)
            {
                if (results.Count >= settings.ResultCount) break;

                var piecesBySlot = new Placement[SlotOrder.Length];
                for (int depth = 0; depth < SlotOrder.Length; depth++)
                    piecesBySlot[SlotOrder[depth]] = state.Placements[depth];

                // 不正規化：正規化會把形狀轉到標準方向，貓就歪了（跟具象題同一個理由）
                Puzzle puzzle = Puzzle.FromPlacements(piecesBySlot, PuzzleSource.UserDrawn);
                if (!seenPuzzleKeys.Add(puzzle.Key)) continue;

                results.Add(new ContourApproximation(piecesBySlot, puzzle, intersectionOverUnion,
                                                     new Vector2((float)state.OffsetX, (float)state.OffsetY)));
            }
            return results;
        }

        // ───────── 內部型別 ─────────

        /// <summary>搜尋中的一條路：已經放了哪幾片、輪廓平移多少、目前蓋到裡外各幾格。</summary>
        private sealed class SearchState
        {
            /// <summary>照 <see cref="SlotOrder"/> 的順序放下去的片。</summary>
            public readonly Placement[] Placements;

            /// <summary>第一片用的是第幾個取樣點。同一個平移的狀態才可能是「同一個狀態」。</summary>
            public readonly int OffsetIndex;

            public readonly double OffsetX;
            public readonly double OffsetY;
            public readonly int InsideCells;
            public readonly int OutsideCells;
            public readonly double Score;

            public SearchState(Placement[] placements, int offsetIndex, double offsetX, double offsetY,
                               int insideCells, int outsideCells, double outsidePenalty)
            {
                Placements = placements;
                OffsetIndex = offsetIndex;
                OffsetX = offsetX;
                OffsetY = offsetY;
                InsideCells = insideCells;
                OutsideCells = outsideCells;
                Score = insideCells - outsideCells * outsidePenalty;
            }

            /// <summary>
            /// 平移相同、而且<b>擺法的集合</b>相同的狀態算同一個——兩片大三角交換位置不是新的狀態。
            /// ⚠️ 用的是姿態相等（<see cref="Placement.Equals(Placement)"/>），所以正方形轉 0 與轉 2 會被當成兩個；
            /// 會多佔幾個名額，但不會錯。
            /// </summary>
            public string DeduplicationKey()
            {
                var parts = new string[Placements.Length];
                for (int i = 0; i < Placements.Length; i++) parts[i] = Placements[i].ToString();
                Array.Sort(parts, StringComparer.Ordinal);

                var builder = new StringBuilder();
                builder.Append(OffsetIndex);
                foreach (string part in parts) builder.Append('|').Append(part);
                return builder.ToString();
            }
        }

        /// <summary>一片的浮點頂點（逆時針）與重心。只拿來打分。</summary>
        private readonly struct PiecePolygon
        {
            public readonly double[] X;
            public readonly double[] Y;
            public readonly double CentroidX;
            public readonly double CentroidY;

            private PiecePolygon(double[] x, double[] y)
            {
                X = x;
                Y = y;
                double sumX = 0.0, sumY = 0.0;
                for (int i = 0; i < x.Length; i++) { sumX += x[i]; sumY += y[i]; }
                // 頂點平均不是面積重心，但三角形相同、正方形與平行四邊形也相同——七巧板的片剛好都成立
                CentroidX = sumX / x.Length;
                CentroidY = sumY / y.Length;
            }

            /// <param name="placement">一片的擺放。</param>
            /// <returns>浮點的多邊形。<see cref="Placement.Vertices"/> 保證逆時針。</returns>
            public static PiecePolygon From(Placement placement)
            {
                ExactVector2[] vertices = placement.Vertices();
                var x = new double[vertices.Length];
                var y = new double[vertices.Length];
                for (int i = 0; i < vertices.Length; i++)
                {
                    x[i] = vertices[i].X.ToDouble();
                    y[i] = vertices[i].Y.ToDouble();
                }
                return new PiecePolygon(x, y);
            }
        }

        /// <summary>
        /// 輪廓的格子：每格中心在不在輪廓裡。<b>格子外面一律當作輪廓外</b>，所以片伸出範圍也照樣扣分。
        /// </summary>
        private sealed class OutlineRaster
        {
            private readonly double cellSize;
            private readonly double originX;
            private readonly double originY;
            private readonly int columnCount;
            private readonly int rowCount;
            private readonly bool[] inside;

            /// <summary>輪廓裡有幾格。IoU 的聯集要用。</summary>
            public readonly int InsideCellCount;

            public OutlineRaster(IReadOnlyList<Vector2> outline, double cellSize)
            {
                this.cellSize = cellSize;

                double minimumX = double.MaxValue, minimumY = double.MaxValue;
                double maximumX = double.MinValue, maximumY = double.MinValue;
                foreach (Vector2 point in outline)
                {
                    minimumX = Math.Min(minimumX, point.x); maximumX = Math.Max(maximumX, point.x);
                    minimumY = Math.Min(minimumY, point.y); maximumY = Math.Max(maximumY, point.y);
                }

                // 格線對齊 cellSize 的整數倍，片那邊才能用同一套索引
                originX = Math.Floor(minimumX / cellSize) * cellSize;
                originY = Math.Floor(minimumY / cellSize) * cellSize;
                columnCount = (int)Math.Ceiling((maximumX - originX) / cellSize) + 1;
                rowCount = (int)Math.Ceiling((maximumY - originY) / cellSize) + 1;
                inside = new bool[columnCount * rowCount];

                for (int row = 0; row < rowCount; row++)
                    for (int column = 0; column < columnCount; column++)
                    {
                        bool isInside = ContainsPoint(outline, originX + (column + 0.5) * cellSize,
                                                               originY + (row + 0.5) * cellSize);
                        inside[row * columnCount + column] = isInside;
                        if (isInside) InsideCellCount++;
                    }
            }

            /// <summary>
            /// 數一片蓋到輪廓裡、外各幾格。片在題目座標，先減掉平移換到輪廓座標。
            /// 一格算不算被片蓋到，看<b>格子中心</b>在不在片裡（含邊上）。
            /// </summary>
            public (int inside, int outside) Count(PiecePolygon polygon, double offsetX, double offsetY)
            {
                int vertexCount = polygon.X.Length;
                var x = new double[vertexCount];
                var y = new double[vertexCount];
                double minimumX = double.MaxValue, minimumY = double.MaxValue;
                double maximumX = double.MinValue, maximumY = double.MinValue;
                for (int i = 0; i < vertexCount; i++)
                {
                    x[i] = polygon.X[i] - offsetX;
                    y[i] = polygon.Y[i] - offsetY;
                    minimumX = Math.Min(minimumX, x[i]); maximumX = Math.Max(maximumX, x[i]);
                    minimumY = Math.Min(minimumY, y[i]); maximumY = Math.Max(maximumY, y[i]);
                }

                int firstColumn = (int)Math.Floor((minimumX - originX) / cellSize);
                int lastColumn = (int)Math.Floor((maximumX - originX) / cellSize);
                int firstRow = (int)Math.Floor((minimumY - originY) / cellSize);
                int lastRow = (int)Math.Floor((maximumY - originY) / cellSize);

                int insideCount = 0, outsideCount = 0;
                for (int row = firstRow; row <= lastRow; row++)
                {
                    double centerY = originY + (row + 0.5) * cellSize;
                    for (int column = firstColumn; column <= lastColumn; column++)
                    {
                        double centerX = originX + (column + 0.5) * cellSize;
                        if (!ConvexContains(x, y, centerX, centerY)) continue;

                        bool inGrid = row >= 0 && row < rowCount && column >= 0 && column < columnCount;
                        if (inGrid && inside[row * columnCount + column]) insideCount++;
                        else outsideCount++;
                    }
                }
                return (insideCount, outsideCount);
            }

            /// <summary>
            /// 輪廓裡每隔 <paramref name="spacing"/> 取一個點（第一片的重心要對上它們）。
            /// 取樣點對齊間隔的整數倍，取「落在輪廓裡的格子」那些。
            /// </summary>
            public List<(double x, double y)> SamplePointsInside(double spacing)
            {
                var points = new List<(double x, double y)>();
                double width = columnCount * cellSize, height = rowCount * cellSize;
                for (double y = originY + spacing / 2.0; y < originY + height; y += spacing)
                    for (double x = originX + spacing / 2.0; x < originX + width; x += spacing)
                    {
                        int column = (int)Math.Floor((x - originX) / cellSize);
                        int row = (int)Math.Floor((y - originY) / cellSize);
                        if (row < rowCount && column < columnCount && inside[row * columnCount + column])
                            points.Add((x, y));
                    }
                return points;
            }

            /// <summary>
            /// 點在不在凸多邊形裡（逆時針）。
            ///
            /// ⚠️ <b>落在邊上的點只算給其中一片</b>（2026-10-14，第二版跑出 IoU 1.016 才發現）：
            /// 原本「含邊上」的寫法，格子中心剛好落在兩片共用的邊上時，兩片都算到那一格——同一格算兩次，
            /// IoU 會超過 1。平移微調會把輪廓移到 1/16、1/32 這種位置，讓格子中心大量對上整數座標的邊，
            /// <b>而且微調會偏好這種位置</b>（重複算到的格子讓分數變高），所以結果被系統性地污染。
            ///
            /// 規則：在邊上的點，只有邊的方向「往上，或水平往左」時才算在裡面。共用的邊在相鄰兩片裡方向剛好相反，
            /// 所以<b>恰好一片</b>拿到那一格。（三片以上共用的頂點剛好落在格子中心時還是可能重複，但要中心正好壓在頂點上，可以忽略。）
            /// </summary>
            private static bool ConvexContains(double[] x, double[] y, double pointX, double pointY)
            {
                const double tolerance = 1e-9;
                int count = x.Length;
                for (int i = 0; i < count; i++)
                {
                    int next = (i + 1) % count;
                    double edgeX = x[next] - x[i], edgeY = y[next] - y[i];
                    double cross = edgeX * (pointY - y[i]) - edgeY * (pointX - x[i]);
                    if (cross < -tolerance) return false;
                    if (cross <= tolerance && !OwnsBoundary(edgeX, edgeY, tolerance)) return false;
                }
                return true;
            }

            /// <summary>
            /// 落在這條邊上的點算不算這一片的：往上的邊、或水平往左的邊才算。
            /// 方向反過來的那條邊（相鄰那片的同一條）剛好相反，所以兩片之間只有一片拿到。
            /// </summary>
            private static bool OwnsBoundary(double edgeX, double edgeY, double tolerance) =>
                edgeY > tolerance || (Math.Abs(edgeY) <= tolerance && edgeX < 0.0);

            /// <summary>點在不在任意多邊形裡（射線法，奇偶規則）。輪廓不是凸的，所以不能用上面那支。</summary>
            private static bool ContainsPoint(IReadOnlyList<Vector2> polygon, double pointX, double pointY)
            {
                bool result = false;
                for (int i = 0, previous = polygon.Count - 1; i < polygon.Count; previous = i++)
                {
                    double xi = polygon[i].x, yi = polygon[i].y;
                    double xj = polygon[previous].x, yj = polygon[previous].y;
                    if ((yi > pointY) != (yj > pointY) &&
                        pointX < (xj - xi) * (pointY - yi) / (yj - yi) + xi)
                        result = !result;
                }
                return result;
            }
        }
    }
}
