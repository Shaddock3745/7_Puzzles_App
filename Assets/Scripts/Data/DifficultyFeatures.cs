using System;
using System.Collections.Generic;

namespace Tangram.Data
{
    /// <summary>
    /// 一個題目的**原始**難度特徵（P2-2）。
    ///
    /// <b>這裡沒有分數，只有量出來的數字。</b>加權、加總、換算成 1200 為中心的尺度
    /// 全部是 <c>DifficultyEvaluator</c>（P2-6）的事。
    ///
    /// 兩件事分開是因為<b>權重要看過一批題目的分布才定得出來</b>：
    /// 特徵函數現在就能算、能測；權重要等出題器跑出上千題之後才知道該落在哪。
    ///
    /// ⚠️ <b>這些數字必須只由形狀決定</b>，因為 ELO 要求同一題永遠得到同一個難度值，
    /// 而 <see cref="Canonical"/> 把互為旋轉／鏡射的題目當成<b>同一題</b>——
    /// 所以特徵對 16 種整體變換全部不變是硬需求，不是巧合。
    /// 釘住它的是 <c>DifficultyFeatureTests.特徵_對十六種整體變換全部不變</c>。
    /// </summary>
    public readonly struct DifficultyFeatures
    {
        /// <summary>
        /// 一副七巧板七片的周長總和，恆為 <b>40 + 20√2</b>（單位：小三角的股 = 2）。
        ///
        /// 剛體變換不改變邊長，而七片永遠是同樣那七片，所以這是常數。
        /// <see cref="DoubledHiddenEdgeLength"/> 整條推論的地基就是它。
        ///
        /// ⚠️ 改了 <see cref="PiecePrototype"/> 的原型頂點（鐵則二那個坑）這個值就會變，
        /// 而藏起來的長度會<b>安靜地全部算錯</b>。所以有一條測試直接釘死它。
        /// </summary>
        public static readonly Scalar TotalPiecePerimeter = new Scalar(40, 20);

        /// <summary>七片的面積和 ×2。面積 32，所以是 64。</summary>
        public static readonly Scalar ExpectedDoubleArea = Scalar.FromInteger(64);

        /// <summary>內角大於 180° 的頂點有幾個。凹角多 → 難。</summary>
        public readonly int ConcaveAngleCount;

        /// <summary>輪廓上出現過幾種不同的邊方向，0~8。角度雜 → 難。</summary>
        public readonly int DirectionVarietyCount;

        /// <summary>
        /// 片與片貼合、玩家看不到的那些邊，**長度的兩倍**。藏得多 → 線索少 → 難。
        ///
        /// 存兩倍是為了留在 ℤ[√2] 裡不碰除法（跟 <c>SignedDoubleArea</c>、
        /// <c>Region</c> 內部的 ×2 同一招）。
        /// </summary>
        public readonly Scalar DoubledHiddenEdgeLength;

        /// <summary>兩片只碰到一個點的地方有幾處（相異的點，不是出現次數）。</summary>
        public readonly int PinchPointCount;

        /// <summary>形狀裡有幾個洞。P2-3 的審美篩選要用（建議篩成 0）。</summary>
        public readonly int HoleCount;

        /// <summary>
        /// 輪廓上真正的轉角有幾個（相鄰兩條邊方向不同）。轉角少 → 難。
        ///
        /// 七片的頂點加起來固定是 23 個，所以「被藏起來或合併掉的頂點」= 23 − 這個數。
        /// 捏點處共線的中繼點不算——玩家看不到那個點。
        /// ⚠️ 它跟 <see cref="ConcaveAngleCount"/> 的相關是 0.88 而且方向相反
        /// （凹角多 → 轉角多 → 藏起來的頂點少），所以 <c>DifficultyEvaluator</c> 的權重
        /// <b>只用其中一個</b>（2026-09-12 選了轉角數）。兩個都給分等於把同一件事算兩次。
        /// </summary>
        public readonly int CornerCount;

        /// <summary>
        /// 輪廓凸包的面積 ×2。<b>填滿率 = <see cref="ExpectedDoubleArea"/> ÷ 這個值</b>，越接近 1 越緊湊。
        ///
        /// ⚠️ <b>存面積不存比率</b>：比率是除法，存下來就變浮點（鐵則一）。
        /// 要比門檻時兩邊乘開成整數比較，見 <c>AestheticLimits.Violations</c>。
        /// 使用者 2026-09-11 的判斷：它當權重看不太出效果，但拿來當門檻，其他特徵會更有感。
        /// </summary>
        public readonly Scalar ConvexHullDoubleArea;

        /// <param name="concaveAngleCount">凹角數。</param>
        /// <param name="directionVarietyCount">相異邊方向的數量。</param>
        /// <param name="doubledHiddenEdgeLength">藏起來的邊長 ×2。</param>
        /// <param name="pinchPointCount">捏點數。</param>
        /// <param name="holeCount">洞數。</param>
        /// <param name="cornerCount">轉角數。</param>
        /// <param name="convexHullDoubleArea">凸包面積 ×2。</param>
        public DifficultyFeatures(int concaveAngleCount, int directionVarietyCount,
                                  Scalar doubledHiddenEdgeLength,
                                  int pinchPointCount, int holeCount,
                                  int cornerCount, Scalar convexHullDoubleArea)
        {
            ConcaveAngleCount = concaveAngleCount;
            DirectionVarietyCount = directionVarietyCount;
            DoubledHiddenEdgeLength = doubledHiddenEdgeLength;
            PinchPointCount = pinchPointCount;
            HoleCount = holeCount;
            CornerCount = cornerCount;
            ConvexHullDoubleArea = convexHullDoubleArea;
        }

        /// <summary>量一個題目的特徵。</summary>
        /// <param name="puzzle">題目。</param>
        /// <returns>這個題目的原始特徵。</returns>
        /// <exception cref="ArgumentNullException">題目或它的 <c>Walks</c> 是 null。</exception>
        /// <exception cref="InvalidOperationException">面積不是 32，或輪廓有零寬度的刺。</exception>
        public static DifficultyFeatures Measure(Puzzle puzzle)
        {
            if (puzzle == null) throw new ArgumentNullException(nameof(puzzle));
            return Measure(puzzle.Walks);
        }

        /// <summary>
        /// 量一組輪廓環的特徵。
        ///
        /// <b>外環與洞環用同一套規則</b>，不需要依繞向翻號——因為 <see cref="BoundaryTracer"/>
        /// 把每條邊界邊都定向成<b>內部在左</b>。洞環的有向面積是負的（<c>IsOuter</c> 回 false），
        /// 但走的時候材料一樣在左手邊，所以「右轉 = 凹」對兩者都成立。
        /// ⚠️ 這個性質經過 <see cref="Canonical.TransformWalk"/> 也還在——
        /// 它的鏡射分支會把頂點順序反轉回來（跟 <c>Placement.Vertices()</c> 同一招）。
        /// </summary>
        /// <param name="walks">輪廓環，外環與洞環混在一起沒關係。</param>
        /// <returns>原始特徵。</returns>
        /// <exception cref="ArgumentNullException"><paramref name="walks"/> 是 null。</exception>
        /// <exception cref="InvalidOperationException">
        /// 面積不是 32（代表這不是七片拼出來的），或某個頂點是 180° 折返。
        /// </exception>
        public static DifficultyFeatures Measure(IReadOnlyList<Walk> walks)
        {
            if (walks == null) throw new ArgumentNullException(nameof(walks));

            int concaveAngleCount = 0;
            int cornerCount = 0;
            int holeCount = 0;
            var seenDirections = new HashSet<int>();
            var vertexVisitCounts = new Dictionary<ExactVector2, int>();
            var allVertices = new List<ExactVector2>();
            Scalar totalPerimeter = Scalar.Zero;
            Scalar totalDoubleArea = Scalar.Zero;

            for (int walkIndex = 0; walkIndex < walks.Count; walkIndex++)
            {
                Walk walk = walks[walkIndex];
                Edge[] edges = walk.Edges;

                // 頂點只算一次：面積、洞的判定、捏點的計數全部共用這一份。
                // ⚠️ 不用 Walk.IsOuter——它每次讀都重走一遍頂點
                ExactVector2[] vertices = walk.Vertices();
                Scalar doubleArea = Geometry.SignedDoubleArea(vertices);
                totalDoubleArea = totalDoubleArea + doubleArea;
                if (doubleArea.Sign < 0) holeCount++;

                for (int edgeIndex = 0; edgeIndex < edges.Length; edgeIndex++)
                {
                    // 洞環的邊也要加進周長——洞的邊界一樣是外露的，玩家看得到
                    totalPerimeter = totalPerimeter + edges[edgeIndex].Length;
                    seenDirections.Add(edges[edgeIndex].Direction);

                    // 第 edgeIndex 個頂點是第 edgeIndex 條邊的起點，
                    // 所以「進來的邊」是前一條，第 0 個頂點要繞回最後一條
                    int incomingIndex = (edgeIndex - 1 + edges.Length) % edges.Length;
                    int turn = Symmetry.Modulo8(
                        edges[edgeIndex].Direction - edges[incomingIndex].Direction);

                    // 0 = 直走（捏點那裡會出現，因為共線邊只在邊界度數 = 2 時才合併）
                    // 1~3 = 左轉，內角小於 180°，凸角
                    // 5~7 = 右轉，內角大於 180°，凹角
                    if (turn != 0) cornerCount++;   // 0 是共線的中繼點，玩家看不到
                    if (turn >= 5) concaveAngleCount++;
                    else if (turn == 4)
                        throw new InvalidOperationException(
                            $"輪廓在 {vertices[edgeIndex]} 原地折返——那是一根零寬度的刺。" +
                            "合法輸入不可能出現：每個切好的線段只會被一片擁有，" +
                            "所以「沿原路走回去」的邊界邊根本不存在");
                }

                // ⚠️ 計數表跨環共用：捏點有可能是外環碰到洞環，只看單一環會漏掉
                foreach (ExactVector2 vertex in vertices)
                {
                    vertexVisitCounts.TryGetValue(vertex, out int visits);
                    vertexVisitCounts[vertex] = visits + 1;
                    allVertices.Add(vertex);   // 凸包用；洞環的頂點一定在外環裡面，加進去不影響結果
                }
            }

            // ⚠️ 守衛排在算完之後、回報之前：藏起來的長度那條公式的地基就是
            // 「圖形正好是那七片」。不驗的話，餵一個手繪題目或半成品進來會算出垃圾
            // 甚至負數，而且不報錯。丟例外不丟結果，是因為這代表呼叫端有 bug
            if (totalDoubleArea != ExpectedDoubleArea)
                throw new InvalidOperationException(
                    $"面積 ×2 是 {totalDoubleArea}，應該是 {ExpectedDoubleArea}——" +
                    "這組輪廓不是七巧板七片拼出來的，特徵算出來沒有意義");

            int pinchPointCount = 0;
            foreach (KeyValuePair<ExactVector2, int> visit in vertexVisitCounts)
                if (visit.Value >= 2) pinchPointCount++;

            return new DifficultyFeatures(
                concaveAngleCount,
                seenDirections.Count,
                TotalPiecePerimeter - totalPerimeter,
                pinchPointCount,
                holeCount,
                cornerCount,
                Geometry.ConvexHullDoubleArea(allVertices));
        }

        /// <summary>
        /// 五種片 × 8 旋轉 × 2 翻面 = 80 個姿態，平移量 0 時的頂點（CCW）。
        /// 建一次重複用——<see cref="ForcedPieceCountOf"/> 對每個角都要把整份掃過。
        /// </summary>
        private static readonly PieceAtOrigin[] AllPoses = BuildAllPoses();

        /// <summary>一個姿態擺在原點時的樣子。<see cref="AllPoses"/> 的元素。</summary>
        private readonly struct PieceAtOrigin
        {
            public readonly PieceKind Kind;
            public readonly int Rotation;
            public readonly bool IsFlipped;
            public readonly ExactVector2[] Vertices;

            public PieceAtOrigin(PieceKind kind, int rotation, bool flipped, ExactVector2[] vertices)
            {
                Kind = kind;
                Rotation = rotation;
                IsFlipped = flipped;
                Vertices = vertices;
            }
        }

        /// <returns>80 個姿態，順序是片種 → 旋轉 → 翻面。</returns>
        private static PieceAtOrigin[] BuildAllPoses()
        {
            var poses = new List<PieceAtOrigin>(80);
            foreach (PieceKind kind in (PieceKind[])Enum.GetValues(typeof(PieceKind)))
                for (int rotation = 0; rotation < 8; rotation++)
                    for (int flip = 0; flip < 2; flip++)
                        poses.Add(new PieceAtOrigin(kind, rotation, flip == 1,
                            new Placement(kind, rotation, flip == 1, ExactVector2.Zero).Vertices()));
            return poses.ToArray();
        }

        /// <summary>
        /// 被外框「逼出來」的片有幾片：某個凸角只放得進一種擺法，那片在任何解裡都在那裡。少 → 難。
        ///
        /// 使用者的觀察（2026-09-11）：外框上一塊小三角大小的凸出，一眼就知道要放什麼。
        /// ⚠️ 實測 2000 個形狀，<b>被逼出來的永遠是小三角</b>——任何放得下大片尖角的角，
        /// 小三角也塞得進去，所以大片不可能只剩一種擺法。
        /// ⚠️ 只算深度 1：放下逼出來的片之後可能又逼出下一片，那是求解器（P2-9）的事。
        ///
        /// ⚠️ <b>刻意不放進 <see cref="Measure"/></b>：它是這些特徵裡最貴的一個
        /// （整包 <c>Measure</c> 加上它是每個形狀 0.2 ms，而生一題平均才 3 ms），
        /// 而<b>審美門檻用不到它</b>。放進 <c>Measure</c> 的話，每次生成嘗試都要付這個成本，
        /// 連被門檻擋掉的形狀也要付。現在只有 <c>DifficultyEvaluator</c> 算分數時才呼叫。
        ///
        /// 做法：外框的每個<b>凸角</b>都一定有某片的頂點壓在上面（凸角附近的材料只能被
        /// 「頂點在角上」的片蓋住）。所以把 80 個姿態的每個頂點對到角上，
        /// 放得進去的<b>佔位</b>只剩一種時，任何解都得那樣放。
        ///
        /// 「一種佔位」比的是片種 + 頂點集合，不是姿態：正方形轉 0 與轉 2 佔位相同，
        /// 兩個小三角也不分彼此。同一片可能同時逼住好幾個角（小三角凸出的兩個尖角），
        /// 所以數的是<b>片</b>不是角。
        /// ⚠️ 90° 的角可能是兩個 45° 尖角湊的，那時放得進去的至少有兩種，不會被誤判。
        /// </summary>
        /// <param name="walks">輪廓環。</param>
        /// <returns>被逼出來的片數。</returns>
        /// <exception cref="ArgumentNullException"><paramref name="walks"/> 是 null。</exception>
        public static int ForcedPieceCountOf(IReadOnlyList<Walk> walks) => ForcedPiecesOf(walks).Count;

        /// <summary>
        /// 被外框逼出來的片本身（不只數量）。說明見 <see cref="ForcedPieceCountOf"/>。
        ///
        /// 回傳擺放而不是數字，是因為<b>它們在任何解裡都在同一個位置</b>：
        /// 測試可以拿它跟參考解對（逼出來的片一定在參考解裡），
        /// 提示功能（P2-9 之後）也可以直接把其中一片亮出來——那是保證不會錯的提示。
        /// </summary>
        /// <param name="walks">輪廓環。</param>
        /// <returns>被逼出來的片，每片一筆；沒有就是空清單。</returns>
        /// <exception cref="ArgumentNullException"><paramref name="walks"/> 是 null。</exception>
        public static List<Placement> ForcedPiecesOf(IReadOnlyList<Walk> walks)
        {
            if (walks == null) throw new ArgumentNullException(nameof(walks));

            Region region = Region.FromWalks(walks, ExactVector2.Zero);
            var forced = new Dictionary<string, Placement>();
            var fits = new Dictionary<string, Placement>();

            for (int walkIndex = 0; walkIndex < walks.Count; walkIndex++)
            {
                ExactVector2[] ring = walks[walkIndex].Vertices();
                for (int index = 0; index < ring.Length; index++)
                {
                    ExactVector2 previous = ring[(index - 1 + ring.Length) % ring.Length];
                    ExactVector2 corner = ring[index];
                    ExactVector2 next = ring[(index + 1) % ring.Length];

                    // 左轉 = 凸角（材料在左手邊，外環與洞環都成立）。
                    // 共線的中繼點與凹角都跳過：凹角附近可以被別片的邊蓋住，不一定有頂點在上面
                    if (Geometry.Cross(previous, corner, next).Sign <= 0) continue;

                    ExactVector2 towardNext = next - corner;
                    ExactVector2 towardPrevious = previous - corner;
                    fits.Clear();

                    for (int poseIndex = 0; poseIndex < AllPoses.Length && fits.Count <= 1; poseIndex++)
                    {
                        PieceAtOrigin pose = AllPoses[poseIndex];
                        ExactVector2[] vertices = pose.Vertices;
                        for (int k = 0; k < vertices.Length && fits.Count <= 1; k++)
                        {
                            // 便宜的先篩：片在這個頂點的兩條邊都要落在角的開口裡，才值得問 Region
                            ExactVector2 alongNext = vertices[(k + 1) % vertices.Length] - vertices[k];
                            ExactVector2 alongPrevious = vertices[(k - 1 + vertices.Length) % vertices.Length] - vertices[k];
                            if (!IsInsideCorner(towardNext, towardPrevious, alongNext) ||
                                !IsInsideCorner(towardNext, towardPrevious, alongPrevious)) continue;

                            var placement = new Placement(pose.Kind, pose.Rotation, pose.IsFlipped,
                                                          corner - vertices[k]);
                            if (!region.Contains(placement)) continue;

                            fits[FootprintKeyOf(placement)] = placement;
                        }
                    }

                    if (fits.Count == 1)
                        foreach (KeyValuePair<string, Placement> only in fits) forced[only.Key] = only.Value;
                }
            }
            return new List<Placement>(forced.Values);
        }

        /// <summary>從角出發的一個方向，有沒有落在角的開口裡（含貼著兩條邊）。只對凸角成立。</summary>
        /// <param name="towardNext">角 → 下一個頂點，材料在它的左手邊。</param>
        /// <param name="towardPrevious">角 → 上一個頂點，材料在它的右手邊。</param>
        /// <param name="direction">要測的方向。</param>
        /// <returns>在開口裡回 true。</returns>
        private static bool IsInsideCorner(ExactVector2 towardNext, ExactVector2 towardPrevious,
                                           ExactVector2 direction)
            => ExactVector2.Cross(towardNext, direction).Sign >= 0
            && ExactVector2.Cross(direction, towardPrevious).Sign >= 0;

        /// <summary>「佔哪塊地」的鍵：片種 + 排序過的頂點。姿態不同但佔位相同的會得到同一個鍵。</summary>
        /// <param name="placement">擺放。</param>
        /// <returns>比對用的字串。</returns>
        private static string FootprintKeyOf(Placement placement)
        {
            ExactVector2[] vertices = placement.Vertices();
            Array.Sort(vertices, (left, right) => left.CompareTo(right));
            var key = new System.Text.StringBuilder(placement.Kind.ToString());
            foreach (ExactVector2 vertex in vertices) key.Append(' ').Append(vertex.ToKey());
            return key.ToString();
        }

        /// <returns>給人看的字串，除錯與批次統計用。</returns>
        public override string ToString()
            => $"凹角={ConcaveAngleCount} 角度種類={DirectionVarietyCount} " +
               $"藏起來的長度×2={DoubledHiddenEdgeLength} 捏點={PinchPointCount} 洞={HoleCount} " +
               $"轉角={CornerCount} 凸包面積×2={ConvexHullDoubleArea}";
    }
}
