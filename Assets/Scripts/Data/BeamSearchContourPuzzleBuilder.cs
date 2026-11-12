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

        /// <summary>
        /// ⭐ <b>找特徵的半徑</b>（2026-10-20），模型單位：比 2 倍細的突出部位、比 2 倍窄的缺口才算特徵，
        /// 見 <see cref="OutlineFeatureMap"/>。<b>0 ＝ 不找特徵，等於第三版</b>（拿來對照）。
        /// 預設 0.75（小三角的腰長是 2）。2026-10-20 試過 0.5／0.75／1.0：0.5 會把魚身與魚尾之間的缺口填掉、1.0 缺口被填回到跟不找特徵差不多，照片類的形狀上 0.75 最好。
        /// </summary>
        public readonly double FeatureRadius;

        /// <summary>
        /// ⭐ 蓋到<b>突出部位</b>的一格<b>多</b>給幾分（一般的一格是 +1）。預設 2，所以耳朵、尾巴一格 +3。
        /// 試過 4：會為了碰耳朵尖把身體拼歪（IoU 0.845 → 0.822）。
        /// </summary>
        public readonly double ProtrusionWeight;

        /// <summary>
        /// ⭐ 填進<b>缺口</b>的一格<b>多</b>扣幾倍（一般伸出去的一格是 −<see cref="OutsidePenalty"/>）。預設 2，所以兩耳之間一格 −3。
        /// </summary>
        public readonly double NotchWeight;

        /// <summary>
        /// ⭐ <b>貼邊獎勵</b>（2026-10-21 第五版）：新放的片跟已放的片<b>貼合的邊長</b>，每 1 模型單位加幾格分數
        /// （格子是 1/8，所以 16 ＝ 貼 1 單位的邊值 1/4 單位面積）。<b>0 ＝ 不獎勵，等於第四版</b>（拿來對照）。
        ///
        /// <b>為什麼要有它</b>：生活物品的細長部位（柄、桿、腳）用 IoU 挑出來的拼法一律是小片的鋸齒鏈——面積上最好，人看起來不像。
        /// 鋸齒鏈的共同點是外露周長長（片跟片只碰一點、只貼一小段），獎勵貼合的邊就是在壓外露周長，也就是在壓鋸齒。
        ///
        /// <b>預設 16</b>（2026-10-21，掃 4／8／16／32，看過結果圖後定 16）：40 張生成的生活物品 IoU 不變、外露周長 36.3 → 34.2、轉角 14.6 → 13.4；
        /// MPEG-7 的 116 張真實剪影 IoU −0.002、周長 35.3 → 33.8；Caltech-101 的 300 張真照片經 U2Net，IoU −0.001、周長 33.0 → 31.6、轉角 14.4 → 13.2。
        /// 32 會為了貼邊放棄輪廓（湯匙 0.797 → 0.712），8 幾乎沒動。時間不變。
        /// </summary>
        public readonly double EdgeContactBonus;

        /// <summary>
        /// 有沒有開特徵：半徑 &gt; 0，而且兩個權重至少一個 &gt; 0。
        /// <b>沒開的話只跑一次搜尋、照 IoU 挑</b>，結果跟第三版一模一樣。
        /// </summary>
        public bool UsesFeatures => FeatureRadius > 0.0 && (ProtrusionWeight > 0.0 || NotchWeight > 0.0);

        /// <summary>有沒有開貼邊獎勵：<see cref="EdgeContactBonus"/> &gt; 0。沒開的話收尾照 IoU 排，跟第四版一模一樣。</summary>
        public bool UsesEdgeContactBonus => EdgeContactBonus > 0.0;

        /// <param name="beamWidth">每一步留幾條路，至少 1。</param>
        /// <param name="cellSize">格子邊長，必須是正數。</param>
        /// <param name="outsidePenalty">伸到外面一格扣幾分，不能是負數。</param>
        /// <param name="offsetSpacing">第一片平移的取樣間隔，必須是正數。</param>
        /// <param name="resultCount">回幾個形狀，至少 1。</param>
        /// <param name="refinementRounds">平移微調做幾輪，不能是負數；0 ＝ 不微調。</param>
        /// <param name="refinementStartPieceCount">從第幾片開始微調，1 ～ 7。</param>
        /// <param name="featureRadius">找特徵的半徑，不能是負數；0 ＝ 不找特徵。</param>
        /// <param name="protrusionWeight">突出部位多給的分，不能是負數。</param>
        /// <param name="notchWeight">缺口多扣的倍數，不能是負數。</param>
        /// <param name="edgeContactBonus">貼合的邊每 1 模型單位加幾格分數，不能是負數；0 ＝ 不獎勵。</param>
        /// <exception cref="ArgumentOutOfRangeException">任何一個參數超出範圍。</exception>
        public ContourApproximationSettings(int beamWidth = 100, double cellSize = 0.125, double outsidePenalty = 1.0,
                                            double offsetSpacing = 0.25, int resultCount = 5, int refinementRounds = 2,
                                            int refinementStartPieceCount = 5, double featureRadius = 0.75,
                                            double protrusionWeight = 2.0, double notchWeight = 2.0,
                                            double edgeContactBonus = 16.0)
        {
            if (!(edgeContactBonus >= 0.0)) throw new ArgumentOutOfRangeException(nameof(edgeContactBonus), edgeContactBonus, "貼邊獎勵不能是負數。");
            if (!(featureRadius >= 0.0)) throw new ArgumentOutOfRangeException(nameof(featureRadius), featureRadius, "找特徵的半徑不能是負數。");
            if (!(protrusionWeight >= 0.0)) throw new ArgumentOutOfRangeException(nameof(protrusionWeight), protrusionWeight, "突出的權重不能是負數。");
            if (!(notchWeight >= 0.0)) throw new ArgumentOutOfRangeException(nameof(notchWeight), notchWeight, "缺口的權重不能是負數。");
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
            FeatureRadius = featureRadius;
            ProtrusionWeight = protrusionWeight;
            NotchWeight = notchWeight;
            EdgeContactBonus = edgeContactBonus;
        }

        /// <summary>
        /// 起始值：N = 100、格子 1/8、懲罰 1、平移間隔 0.25、回 5 個、微調 2 輪、從第 5 片開始、
        /// ⭐ 特徵半徑 0.75、突出與缺口各 ×2（2026-10-20）、⭐ 貼邊獎勵 16（2026-10-21）。
        /// </summary>
        public static ContourApproximationSettings Default => new ContourApproximationSettings();

        /// <summary>
        /// 同一組參數、但<b>不用特徵搜尋</b>（＝第三版加貼邊獎勵；再 <see cref="WithoutEdgeContactBonus"/> 才是純第三版）。
        /// ⚠️ <b>只把兩個權重歸零，半徑照留</b>：搜尋與排名跟第三版一模一樣，但結果的「突出蓋到／缺口被填」照樣量得出來，
        /// 對照組跟實驗組才比得了。
        /// </summary>
        /// <returns>兩個權重是 0、其他照抄的一組參數。</returns>
        public ContourApproximationSettings WithoutFeatures() =>
            new ContourApproximationSettings(BeamWidth, CellSize, OutsidePenalty, OffsetSpacing, ResultCount,
                                             RefinementRounds, RefinementStartPieceCount, FeatureRadius, 0.0, 0.0, EdgeContactBonus);

        /// <summary>
        /// 同一組參數、但<b>不獎勵貼邊</b>（＝第四版）。⭐ 2026-10-21 起驗收工具拿它當對照組。
        /// </summary>
        /// <returns>貼邊獎勵是 0、其他照抄的一組參數。</returns>
        public ContourApproximationSettings WithoutEdgeContactBonus() =>
            new ContourApproximationSettings(BeamWidth, CellSize, OutsidePenalty, OffsetSpacing, ResultCount,
                                             RefinementRounds, RefinementStartPieceCount, FeatureRadius, ProtrusionWeight, NotchWeight, 0.0);

        /// <returns>給報告看的一行。</returns>
        public override string ToString() =>
            $"N={BeamWidth} 格子={CellSize} 懲罰={OutsidePenalty} 平移間隔={OffsetSpacing} " +
            (RefinementRounds == 0 ? "不微調" : $"微調={RefinementRounds}輪（從第{RefinementStartPieceCount}片）") +
            (UsesFeatures ? $" 特徵 r={FeatureRadius} 突出×{ProtrusionWeight} 缺口×{NotchWeight}" : " 不找特徵") +
            (UsesEdgeContactBonus ? $" 貼邊×{EdgeContactBonus}" : " 不獎勵貼邊");
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

        /// <summary>
        /// ⭐ <b>特徵分</b>（2026-10-20）：裡面一格 +1、突出一格 +1 + 突出權重、外面一格 −懲罰、缺口一格再乘 1 + 缺口權重，
        /// 換算成面積單位（格數 × 格子面積）。<b>開特徵時結果照它由高到低排</b>；沒開特徵時就是 裡面 − 外面 × 懲罰。
        /// </summary>
        public readonly double FeatureScore;

        /// <summary>
        /// 突出部位被蓋到幾成，0 ～ 1（2026-10-20）。輪廓沒有突出部位、或特徵半徑是 0 時是 1。給 log 與驗收看。
        /// 權重是 0（沒開特徵）時照樣量，只是不拿來排名。
        /// </summary>
        public readonly double ProtrusionCoverage;

        /// <summary>
        /// 缺口被填掉幾成，0 ～ 1，<b>越低越好</b>（2026-10-20）。輪廓沒有缺口、或特徵半徑是 0 時是 0。
        /// </summary>
        public readonly double NotchFill;

        /// <summary>
        /// ⭐ 七片彼此<b>貼合的邊長總和</b>（模型單位，2026-10-21）：片跟片共線又重疊的邊，每一段算一次。
        /// 越長外露周長越短、越不鋸齒（外露周長 ＝ 七片周長總和 40 + 20√2 − 2 × 這個數）。給 log 與驗收看。
        /// </summary>
        public readonly double SharedEdgeLength;

        internal ContourApproximation(Placement[] piecesBySlot, Puzzle puzzle, double intersectionOverUnion, Vector2 outlineOffset,
                                      double featureScore, double protrusionCoverage, double notchFill, double sharedEdgeLength)
        {
            PiecesBySlot = piecesBySlot;
            Puzzle = puzzle;
            IntersectionOverUnion = intersectionOverUnion;
            OutlineOffset = outlineOffset;
            FeatureScore = featureScore;
            ProtrusionCoverage = protrusionCoverage;
            NotchFill = notchFill;
            SharedEdgeLength = sharedEdgeLength;
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
    /// <item><b>鋪格子</b>：輪廓（已經縮到面積 32）的範圍鋪格子，每格中心在不在輪廓裡算一次存起來；
    ///   ⭐ 開特徵時順便標出突出與缺口（<see cref="OutlineFeatureMap"/>）</item>
    /// <item><b>第一片</b>：大三角固定在原點、各種轉向都試；<b>改成移動輪廓</b>——輪廓是浮點可以隨便平移，
    ///   片的座標維持精確（鐵則一）。輪廓裡每隔一段取一個點，讓大三角的重心對上它</item>
    /// <item><b>之後每一片</b>：照固定順序（大到小）列出所有貼得上、不重疊的擺法（貼邊與碰點都收），
    ///   打分、去重、留前 N 個</item>
    /// <item><b>平移微調</b>（第二版）：每一步留下 N 條路之後，各自把輪廓往 8 個方向小移一點，
    ///   整組重算分數，有比較好就移過去。<b>只動輪廓、不動任何一片</b>，所以合法性不受影響</item>
    /// <item><b>收尾</b>：同一個形狀只留一個、回前幾名</item>
    /// </list>
    ///
    /// <b>打分</b>：一片 ＝ 蓋到輪廓裡的格數 − 伸到外面的格數 × 懲罰。片跟片保證不重疊，所以整組就是各片相加，
    /// IoU 也直接從兩個計數算得出來（交集 ＝ 裡面的格數，聯集 ＝ 輪廓的格數 ＋ 外面的格數）。
    ///
    /// ⭐ <b>特徵（2026-10-20，設計重點是「要有特徵」）</b>：
    /// <list type="bullet">
    /// <item><b>特徵分</b>：突出的格子（耳朵、尾巴、角）一格 +1 + 突出權重，缺口的格子（兩耳之間）一格 −懲罰 ×（1 + 缺口權重），其他照舊</item>
    /// <item><b>跑兩次搜尋</b>：一次每一步照原本的分數排（＝第三版）、一次每一步照特徵分排；
    ///   兩次的收尾一樣，都是<b>照 IoU 留前 <see cref="ContourApproximationSettings.ResultCount"/> 個</b>相異的形狀</item>
    /// <item><b>最後兩邊的前幾名合起來照特徵分挑</b>。只跑特徵分那次的話，原本逼得回來的數字4、數字6、衣服會掉——
    ///   特徵分讓前幾步走偏了。合起來之後第一次找到的已知答案還在候選裡，而它全部蓋滿、零伸出、突出全部蓋到，<b>特徵分一定最高</b>，
    ///   所以撿得回來（2026-10-20 驗收，25 個具象題：逼回 13 → 15、平均 IoU 0.955 → 0.963、突出蓋到 0.93 → 0.96、缺口被填 0.10 → 0.06；數字修正過）。
    ///   ⚠️ 試過「兩次留下的全部 N 條路裡直接挑特徵分最高」：特徵更多但 IoU 掉一點，所以照確認過的邏輯先用 IoU 篩一輪</item>
    /// <item>代價：<b>時間大約兩倍</b>。兩邊各用一半的 N 試過，逼回數與特徵都變差，所以不省</item>
    /// </list>
    /// 沒開特徵（<see cref="ContourApproximationSettings.UsesFeatures"/> 是 false）時只跑第一次、照 IoU 挑，跟第三版一模一樣。
    ///
    /// ⭐ <b>貼邊獎勵（2026-10-21 第五版，改從審美下手）</b>：
    /// <list type="bullet">
    /// <item>起因：生活物品的細長部位（柄、桿、腳）用面積挑出來的拼法一律是小片的鋸齒鏈，人看起來不像。六個搜尋方法
    ///   （拿一片／兩片重放、換放片順序合併、軟邊界、對齊 45°、轉 5 角）與輪廓預處理在真實剪影上都動不了 IoU（±0.005），
    ///   搜尋已在七片的天花板附近；要變「像」得換目標</item>
    /// <item><b>做法</b>：每放一片，算它跟已放的每一片<b>貼合的邊長</b>（共線、重疊的部分，見 <see cref="SharedEdgeLength(Placement, Placement)"/>），
    ///   乘 <see cref="ContourApproximationSettings.EdgeContactBonus"/> 累計進這條路；<b>兩種排名分數都加上累計</b>；平移微調只動輪廓、累計照帶；
    ///   收尾改照「排名分數（含獎勵）」排，兩次搜尋合起來挑時特徵分也含獎勵</item>
    /// <item>獎勵是 0 時收尾退回照 IoU 排，跟第四版一模一樣（日常測試釘住）</item>
    /// <item>成本：每個子狀態最多 6 片 × 4 邊 × 4 邊的比對，時間不變</item>
    /// </list>
    ///
    /// ⚠️ <b>浮點只用來打分與排名</b>，挑出來的永遠是精確的 <see cref="Placement"/>。
    ///
    /// ⚠️ <b>刻意沒做的</b>：洞、審美門檻、非同步。
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
        /// <remarks>只拿第 1 名。⚠️ 同步執行，在手機上可能要好幾秒（開特徵之後大約兩倍）。</remarks>
        public ContourPuzzle Build(RecognizedContour contour)
        {
            IReadOnlyList<ContourApproximation> results = Approximate(contour);
            return results.Count == 0 ? null : ToContourPuzzle(results[0]);
        }

        /// <summary>
        /// 把一個逼近結果包成給遊戲用的題目（名字裡帶 IoU、特徵數字與貼合邊長，log 印得出來）。
        /// ⭐ 2026-10-21 公開：選題畫面要把前幾名都包起來，不只第 1 名。
        /// </summary>
        /// <param name="approximation">一個逼近結果（<see cref="Approximate"/> 回的）。</param>
        /// <returns>題目＋參考解＋輪廓的平移量。</returns>
        /// <exception cref="ArgumentNullException"><paramref name="approximation"/> 是 null。</exception>
        public ContourPuzzle ToContourPuzzle(ContourApproximation approximation)
        {
            if (approximation == null) throw new ArgumentNullException(nameof(approximation));

            string name = settings.UsesFeatures
                ? $"掃描題（IoU {approximation.IntersectionOverUnion:0.00}，突出蓋到 {approximation.ProtrusionCoverage:0.00}，缺口被填 {approximation.NotchFill:0.00}，貼合邊長 {approximation.SharedEdgeLength:0.0}）"
                : $"掃描題（IoU {approximation.IntersectionOverUnion:0.00}，貼合邊長 {approximation.SharedEdgeLength:0.0}）";
            // ⭐ 2026-10-23：IoU 也帶過去，選題畫面翻頁時顯示（原本只寫在名字字串裡）
            return new ContourPuzzle(name, approximation.Puzzle, approximation.PiecesBySlot, approximation.OutlineOffset,
                                     approximation.IntersectionOverUnion);
        }

        /// <summary>
        /// 跑一次逼近，回前幾名，形狀互不相同。
        /// <b>開特徵時照特徵分由高到低</b>，沒開時照 IoU 由高到低（見類別說明）。
        /// </summary>
        /// <param name="contour">辨識出來的輪廓。</param>
        /// <returns>前幾名，最多 <see cref="ContourApproximationSettings.ResultCount"/> 個；一個都逼不出來時是空的。</returns>
        /// <exception cref="ArgumentNullException"><paramref name="contour"/> 是 null。</exception>
        public IReadOnlyList<ContourApproximation> Approximate(RecognizedContour contour)
        {
            if (contour == null) throw new ArgumentNullException(nameof(contour));

            var raster = new OutlineRaster(contour.ModelOutline, settings);

            // 第一次：照原本的分數排（＝第三版）。開特徵時它負責把「已知答案」那類結果留在候選裡
            List<ContourApproximation> results = Finish(Search(raster, rankByFeatures: false), raster);
            if (!settings.UsesFeatures) return results;

            // 第二次照特徵分搜，兩邊各自的前幾名（照 IoU）合起來，再照特徵分挑
            results.AddRange(Finish(Search(raster, rankByFeatures: true), raster));
            return MergeByFeatureScore(results);
        }

        /// <summary>
        /// 兩次搜尋的前幾名照特徵分由高到低重排，同一個形狀只留第一次出現的，回前 <see cref="ContourApproximationSettings.ResultCount"/> 個。
        /// <b>平手時保留原本的順序</b>（第一次搜尋的排在前面），結果才可重現。
        /// </summary>
        private IReadOnlyList<ContourApproximation> MergeByFeatureScore(List<ContourApproximation> candidates)
        {
            var order = new int[candidates.Count];
            for (int i = 0; i < order.Length; i++) order[i] = i;
            Array.Sort(order, (left, right) =>
            {
                int byScore = candidates[right].FeatureScore.CompareTo(candidates[left].FeatureScore);
                return byScore != 0 ? byScore : left.CompareTo(right);
            });

            var merged = new List<ContourApproximation>();
            var seenPuzzleKeys = new HashSet<string>();
            foreach (int index in order)
            {
                if (merged.Count >= settings.ResultCount) break;
                if (seenPuzzleKeys.Add(candidates[index].Puzzle.Key)) merged.Add(candidates[index]);
            }
            return merged;
        }

        /// <summary>一次完整的搜尋：第一片 → 之後每一片，每一步都留前 N 個再微調。回最後留下的那些路。</summary>
        private List<SearchState> Search(OutlineRaster raster, bool rankByFeatures)
        {
            List<SearchState> beam = Refine(FirstPieceStates(raster, rankByFeatures), raster, rankByFeatures);

            for (int depth = 1; depth < SlotOrder.Length && beam.Count > 0; depth++)
                beam = Refine(Expand(beam, depth, raster, rankByFeatures), raster, rankByFeatures);

            return beam;
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
        private List<SearchState> Refine(List<SearchState> beam, OutlineRaster raster, bool rankByFeatures)
        {
            if (settings.RefinementRounds == 0 || beam.Count == 0) return beam;

            // 同一步的路片數都一樣，看第一條就好
            if (beam[0].Placements.Length < settings.RefinementStartPieceCount) return beam;

            var refined = new List<SearchState>(beam.Count);
            foreach (SearchState state in beam) refined.Add(Refine(state, raster, rankByFeatures));
            return refined;
        }

        /// <summary>
        /// 一條路的微調：在目前的平移周圍試 8 個方向，<b>已經放好的每一片都用新的平移重算</b>，
        /// 有比現在高的就移到最高的那個；步長每輪減半。比的是這次搜尋排名用的那個分數。
        ///
        /// ⚠️ <b>只動輪廓，不動任何一片</b>——片的擺法是精確的，結果照樣保證拼得出來。
        /// 分數一樣時留在原地（嚴格大於才移），8 個方向平手時取 <see cref="RefinementDirections"/> 裡先出現的。
        /// </summary>
        private SearchState Refine(SearchState state, OutlineRaster raster, bool rankByFeatures)
        {
            var polygons = new PiecePolygon[state.Placements.Length];
            for (int i = 0; i < polygons.Length; i++) polygons[i] = PiecePolygon.From(state.Placements[i]);

            SearchState current = state;
            double step = settings.OffsetSpacing / 2.0;

            for (int round = 0; round < settings.RefinementRounds; round++, step /= 2.0)
            {
                SearchState best = current;

                foreach ((int x, int y) direction in RefinementDirections)
                {
                    double candidateX = current.OffsetX + direction.x * step;
                    double candidateY = current.OffsetY + direction.y * step;

                    CellCount total = default;
                    foreach (PiecePolygon polygon in polygons) total += raster.Count(polygon, candidateX, candidateY);

                    // 只動輪廓不動片，貼合的邊長不變，累計的獎勵照帶
                    var candidate = new SearchState(state.Placements, state.OffsetIndex, candidateX, candidateY, total,
                                                    settings.OutsidePenalty, rankByFeatures, state.EdgeBonus, state.SharedEdgeLength);
                    if (candidate.Score > best.Score) best = candidate;
                }

                current = best;
            }

            return current;
        }

        // ───────── 第一片 ─────────

        /// <summary>
        /// 第一片的所有起始狀態，打完分只留前 N 個。
        /// 大三角放原點、每種轉向一次，再讓它的重心對上輪廓裡每一個取樣點。
        /// </summary>
        private List<SearchState> FirstPieceStates(OutlineRaster raster, bool rankByFeatures)
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

                        states.Add(new SearchState(new[] { placement }, sampleIndex, offsetX, offsetY,
                                                   raster.Count(polygon, offsetX, offsetY), settings.OutsidePenalty, rankByFeatures));
                    }
                }

            return KeepBest(states);
        }

        // ───────── 之後每一片 ─────────

        /// <summary>每條留著的路都放下一片，所有子狀態打分，去重之後留前 N 個。</summary>
        private List<SearchState> Expand(List<SearchState> beam, int depth, OutlineRaster raster, bool rankByFeatures)
        {
            PieceKind kind = PiecePrototype.TangramSet[SlotOrder[depth]];
            var children = new List<SearchState>();

            foreach (SearchState parent in beam)
            {
                var (edgeContact, pointContact) = PuzzleGenerator.CollectLegalPlacements(kind, parent.Placements);

                // 已放的片的浮點頂點只算一次，每個候選都要跟它們算貼合的邊長
                var parentPolygons = new PiecePolygon[parent.Placements.Length];
                for (int i = 0; i < parentPolygons.Length; i++) parentPolygons[i] = PiecePolygon.From(parent.Placements[i]);

                // 貼邊與碰點都收：貓耳朵這種地方本來就要靠點接觸。偏不偏好貼邊交給分數決定
                AddChildren(parent, parentPolygons, edgeContact, raster, rankByFeatures, children);
                AddChildren(parent, parentPolygons, pointContact, raster, rankByFeatures, children);
            }

            return KeepBest(children);
        }

        private void AddChildren(SearchState parent, PiecePolygon[] parentPolygons, List<Placement> candidates, OutlineRaster raster,
                                 bool rankByFeatures, List<SearchState> children)
        {
            foreach (Placement candidate in candidates)
            {
                PiecePolygon candidatePolygon = PiecePolygon.From(candidate);
                CellCount count = raster.Count(candidatePolygon, parent.OffsetX, parent.OffsetY);

                // ⭐ 貼邊獎勵（第五版）：跟已放的每一片貼合的邊長 × 獎勵，累計進這條路。獎勵是 0 時連算都不算，第四版那條路一步都不多
                double sharedEdgeLength = parent.SharedEdgeLength;
                if (settings.UsesEdgeContactBonus)
                    foreach (PiecePolygon placed in parentPolygons) sharedEdgeLength += SharedEdgeLength(candidatePolygon, placed);
                double edgeBonus = settings.UsesEdgeContactBonus ? sharedEdgeLength * settings.EdgeContactBonus : 0.0;

                var placements = new Placement[parent.Placements.Length + 1];
                Array.Copy(parent.Placements, placements, parent.Placements.Length);
                placements[parent.Placements.Length] = candidate;

                children.Add(new SearchState(placements, parent.OffsetIndex, parent.OffsetX, parent.OffsetY,
                                             parent.Cells + count, settings.OutsidePenalty, rankByFeatures, edgeBonus, sharedEdgeLength));
            }
        }

        /// <summary>
        /// ⭐ 兩片<b>貼合的邊長</b>（模型單位，2026-10-21）：一片的每條邊對另一片的每條邊，兩個端點都落在這條邊所在的直線上（容差 1e-9）
        /// 就是共線，再取另一片那條邊投影到這條邊上跟這條邊範圍重疊的長度。片保證不重疊，所以共線又重疊的一定是貼合（方向相反）。
        /// 只碰一點是 0。給搜尋打分、測試與驗收用；浮點，跟打分同一層（鐵則一：片本身仍然是精確的）。
        /// </summary>
        /// <param name="first">一片。</param>
        /// <param name="second">另一片。</param>
        /// <returns>貼合的邊長總和。</returns>
        public static double SharedEdgeLength(Placement first, Placement second) =>
            SharedEdgeLength(PiecePolygon.From(first), PiecePolygon.From(second));

        private static double SharedEdgeLength(PiecePolygon first, PiecePolygon second)
        {
            const double tolerance = 1e-9;
            double shared = 0.0;
            int firstCount = first.X.Length, secondCount = second.X.Length;
            for (int i = 0; i < firstCount; i++)
            {
                double startX = first.X[i], startY = first.Y[i];
                double directionX = first.X[(i + 1) % firstCount] - startX, directionY = first.Y[(i + 1) % firstCount] - startY;
                double length = Math.Sqrt(directionX * directionX + directionY * directionY);
                directionX /= length;
                directionY /= length;

                for (int j = 0; j < secondCount; j++)
                {
                    double endpointX1 = second.X[j] - startX, endpointY1 = second.Y[j] - startY;
                    double endpointX2 = second.X[(j + 1) % secondCount] - startX, endpointY2 = second.Y[(j + 1) % secondCount] - startY;

                    // 兩個端點到這條邊所在直線的距離（外積除以單位方向 ＝ 距離）都要在容差內才算共線
                    if (Math.Abs(directionX * endpointY1 - directionY * endpointX1) > tolerance) continue;
                    if (Math.Abs(directionX * endpointY2 - directionY * endpointX2) > tolerance) continue;

                    double projection1 = directionX * endpointX1 + directionY * endpointY1;
                    double projection2 = directionX * endpointX2 + directionY * endpointY2;
                    double low = Math.Max(0.0, Math.Min(projection1, projection2));
                    double high = Math.Min(length, Math.Max(projection1, projection2));
                    if (high > low) shared += high - low;
                }
            }
            return shared;
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
        /// 一次搜尋的收尾：七片都放完的狀態<b>照 IoU 排</b>（第三版的規則，兩次搜尋都一樣），同一個形狀（<see cref="Puzzle.Key"/>）只留一個，
        /// 回前 <see cref="ContourApproximationSettings.ResultCount"/> 個。開特徵時再由 <see cref="MergeByFeatureScore"/> 照特徵分重排。
        /// ⭐ 開貼邊獎勵時改<b>照排名分數（含獎勵）排</b>——照 IoU 排的話獎勵只影響途中留誰、不影響最後挑誰，鋸齒照樣選回來。
        /// ⚠️ 描外框要跑 16 次輪廓追蹤，所以照順序一個一個描，湊滿就停，不全部描。
        /// </summary>
        private List<ContourApproximation> Finish(List<SearchState> beam, OutlineRaster raster)
        {
            var finished = new List<(SearchState state, double intersectionOverUnion)>();
            foreach (SearchState state in beam)
                if (state.Placements.Length == SlotOrder.Length)
                    finished.Add((state, (double)state.Cells.Inside / (raster.InsideCellCount + state.Cells.Outside)));

            if (settings.UsesEdgeContactBonus)
            {
                // 分數一樣時照產生的順序（跟 KeepBest 同一招），結果才可重現
                var order = new int[finished.Count];
                for (int i = 0; i < order.Length; i++) order[i] = i;
                Array.Sort(order, (left, right) =>
                {
                    int byScore = finished[right].state.Score.CompareTo(finished[left].state.Score);
                    return byScore != 0 ? byScore : left.CompareTo(right);
                });
                var byScoreOrder = new List<(SearchState state, double intersectionOverUnion)>(finished.Count);
                foreach (int index in order) byScoreOrder.Add(finished[index]);
                finished = byScoreOrder;
            }
            else
            {
                // ⚠️ 一定要用跟第三版同一支排序（List.Sort 不穩定，換成別的排序法平手的順序可能會變）
                finished.Sort((left, right) => right.intersectionOverUnion.CompareTo(left.intersectionOverUnion));
            }

            double cellArea = settings.CellSize * settings.CellSize;
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

                (double protrusionCoverage, double notchFill) = raster.FeatureCoverage(state.Cells);
                // 特徵分含貼邊獎勵：兩次搜尋合起來挑時用的就是它（獎勵是 0 時加的是 0，第四版的數字不變）
                results.Add(new ContourApproximation(piecesBySlot, puzzle, intersectionOverUnion,
                                                     new Vector2((float)state.OffsetX, (float)state.OffsetY),
                                                     (state.Cells.Feature + state.EdgeBonus) * cellArea, protrusionCoverage, notchFill,
                                                     state.SharedEdgeLength));
            }
            return results;
        }

        // ───────── 內部型別 ─────────

        /// <summary>
        /// 一片（或一整組）蓋到的格子：裡面幾格、外面幾格、其中突出與缺口各幾格，以及特徵分（格數單位）。
        /// 片跟片保證不重疊，所以整組就是各片直接相加。
        /// </summary>
        private readonly struct CellCount
        {
            public readonly int Inside;
            public readonly int Outside;
            public readonly int Protrusion;
            public readonly int Notch;
            public readonly double Feature;

            public CellCount(int inside, int outside, int protrusion, int notch, double feature)
            {
                Inside = inside;
                Outside = outside;
                Protrusion = protrusion;
                Notch = notch;
                Feature = feature;
            }

            public static CellCount operator +(CellCount left, CellCount right) =>
                new CellCount(left.Inside + right.Inside, left.Outside + right.Outside,
                              left.Protrusion + right.Protrusion, left.Notch + right.Notch, left.Feature + right.Feature);
        }

        /// <summary>搜尋中的一條路：已經放了哪幾片、輪廓平移多少、目前蓋到的格子。</summary>
        private sealed class SearchState
        {
            /// <summary>照 <see cref="SlotOrder"/> 的順序放下去的片。</summary>
            public readonly Placement[] Placements;

            /// <summary>第一片用的是第幾個取樣點。同一個平移的狀態才可能是「同一個狀態」。</summary>
            public readonly int OffsetIndex;

            public readonly double OffsetX;
            public readonly double OffsetY;
            public readonly CellCount Cells;

            /// <summary>
            /// 這次搜尋排名用的分數：照原本的排是 裡面 − 外面 × 懲罰，照特徵排是特徵分；⭐ 兩種都再加上 <see cref="EdgeBonus"/>。
            /// ⚠️ 原本那個<b>一定要照第三版的算式重算</b>，不能拿特徵分代替——浮點相加的順序不同，
            /// 平手的狀態可能會分出高下，第三版的結果就重現不了。
            /// ⚠️ 獎勵是 0 時<b>不能多做一次「+ 0.0」以外的運算</b>，不然同樣的理由會讓第四版重現不了。
            /// </summary>
            public readonly double Score;

            /// <summary>⭐ 累計的貼邊獎勵（格數單位）：到目前為止貼合的邊長 × 獎勵。獎勵是 0 時恆為 0。</summary>
            public readonly double EdgeBonus;

            /// <summary>⭐ 累計貼合的邊長（模型單位）。獎勵是 0 時不算，恆為 0。</summary>
            public readonly double SharedEdgeLength;

            public SearchState(Placement[] placements, int offsetIndex, double offsetX, double offsetY,
                               CellCount cells, double outsidePenalty, bool rankByFeatures,
                               double edgeBonus = 0.0, double sharedEdgeLength = 0.0)
            {
                Placements = placements;
                OffsetIndex = offsetIndex;
                OffsetX = offsetX;
                OffsetY = offsetY;
                Cells = cells;
                EdgeBonus = edgeBonus;
                SharedEdgeLength = sharedEdgeLength;
                double baseScore = rankByFeatures ? cells.Feature : cells.Inside - cells.Outside * outsidePenalty;
                Score = edgeBonus == 0.0 ? baseScore : baseScore + edgeBonus;
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
        /// 輪廓的格子：每格是裡面、外面、突出還是缺口（<see cref="OutlineFeatureMap"/>），加上每一種格子值幾分。
        /// <b>格子外面一律當作輪廓外</b>（不是缺口），所以片伸出範圍也照樣扣分。
        /// </summary>
        private sealed class OutlineRaster
        {
            private readonly OutlineFeatureMap map;

            /// <summary>四種格子在特徵分裡各值幾分，索引是 <see cref="OutlineCellKind"/>。</summary>
            private readonly double[] featureValue;

            /// <summary>輪廓裡有幾格。IoU 的聯集要用。</summary>
            public int InsideCellCount => map.InsideCellCount;

            public OutlineRaster(IReadOnlyList<Vector2> outline, ContourApproximationSettings settings)
            {
                // 半徑 > 0 就找特徵，權重是 0 也找：那時特徵不進排名，只拿來量結果的突出蓋到幾成、缺口被填幾成。
                // 留白不會移動格子中心、取樣點從不留白的原點起算，所以找不找特徵都不影響第三版那條路
                map = new OutlineFeatureMap(outline, settings.CellSize, settings.FeatureRadius);

                featureValue = new double[4];
                featureValue[(int)OutlineCellKind.Inside] = 1.0;
                featureValue[(int)OutlineCellKind.Protrusion] = 1.0 + settings.ProtrusionWeight;
                featureValue[(int)OutlineCellKind.Outside] = -settings.OutsidePenalty;
                featureValue[(int)OutlineCellKind.Notch] = -settings.OutsidePenalty * (1.0 + settings.NotchWeight);
            }

            /// <summary>
            /// 數一片蓋到的格子。片在題目座標，先減掉平移換到輪廓座標。
            /// 一格算不算被片蓋到，看<b>格子中心</b>在不在片裡（含邊上）。
            /// </summary>
            public CellCount Count(PiecePolygon polygon, double offsetX, double offsetY)
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

                double cellSize = map.CellSize, originX = map.OriginX, originY = map.OriginY;
                int firstColumn = (int)Math.Floor((minimumX - originX) / cellSize);
                int lastColumn = (int)Math.Floor((maximumX - originX) / cellSize);
                int firstRow = (int)Math.Floor((minimumY - originY) / cellSize);
                int lastRow = (int)Math.Floor((maximumY - originY) / cellSize);

                int insideCount = 0, outsideCount = 0, protrusionCount = 0, notchCount = 0;
                double feature = 0.0;
                for (int row = firstRow; row <= lastRow; row++)
                {
                    double centerY = originY + (row + 0.5) * cellSize;
                    for (int column = firstColumn; column <= lastColumn; column++)
                    {
                        double centerX = originX + (column + 0.5) * cellSize;
                        if (!ConvexContains(x, y, centerX, centerY)) continue;

                        OutlineCellKind kind = map.KindOfCell(column, row);
                        feature += featureValue[(int)kind];
                        switch (kind)
                        {
                            case OutlineCellKind.Inside: insideCount++; break;
                            case OutlineCellKind.Protrusion: insideCount++; protrusionCount++; break;
                            case OutlineCellKind.Notch: outsideCount++; notchCount++; break;
                            default: outsideCount++; break;
                        }
                    }
                }
                return new CellCount(insideCount, outsideCount, protrusionCount, notchCount, feature);
            }

            /// <returns>突出被蓋到幾成（沒有突出時是 1）、缺口被填掉幾成（沒有缺口時是 0）。</returns>
            public (double protrusionCoverage, double notchFill) FeatureCoverage(CellCount cells) =>
                (map.ProtrusionCellCount == 0 ? 1.0 : (double)cells.Protrusion / map.ProtrusionCellCount,
                 map.NotchCellCount == 0 ? 0.0 : (double)cells.Notch / map.NotchCellCount);

            /// <summary>
            /// 輪廓裡每隔 <paramref name="spacing"/> 取一個點（第一片的重心要對上它們）。
            /// 取樣點對齊間隔的整數倍，取「落在輪廓裡的格子」那些。
            /// ⚠️ 取樣從<b>不留空白的原點</b>起算（2026-10-20）：開特徵時格子四周多一圈空白，
            /// 從留白之後的原點起算的話取樣點會整組平移，第一片的起點就跟第三版不一樣了。
            /// </summary>
            public List<(double x, double y)> SamplePointsInside(double spacing)
            {
                var points = new List<(double x, double y)>();
                double cellSize = map.CellSize;
                double limitX = map.OriginX + map.ColumnCount * cellSize, limitY = map.OriginY + map.RowCount * cellSize;
                for (double y = map.UnpaddedOriginY + spacing / 2.0; y < limitY; y += spacing)
                    for (double x = map.UnpaddedOriginX + spacing / 2.0; x < limitX; x += spacing)
                    {
                        int column = (int)Math.Floor((x - map.OriginX) / cellSize);
                        int row = (int)Math.Floor((y - map.OriginY) / cellSize);
                        OutlineCellKind kind = map.KindOfCell(column, row);
                        if (kind == OutlineCellKind.Inside || kind == OutlineCellKind.Protrusion) points.Add((x, y));
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
        }
    }
}
