using System.Collections.Generic;
using Tangram.Data;

namespace Tangram.Core
{
    /// <summary>
    /// 一關的全部資料（互動設計 §14.2）。
    ///
    /// <b>這是「換關時整包被換掉」的那一包。</b>所有 per-關 的狀態都必須放在這裡，
    /// 不要放到 <see cref="LevelSession"/> 的外殼上——放這裡才會被重置自動涵蓋，
    /// 之後新增欄位也不必回頭改重置流程。
    ///
    /// ⚠️ <b>外面的人不准把這裡面的東西存成欄位。</b>換關時整包會被換掉，
    /// 存了 <see cref="Board"/> 的人就會往死掉的那一份寫——片在畫面上吸得好好的，
    /// 盤面永遠是空的、過關永遠不觸發、<c>Changed</c> 訂閱也安靜停掉，
    /// <b>而且完全不會報錯</b>。要用就每次跟 <see cref="LevelSession.Data"/> 拿。
    /// </summary>
    public sealed class LevelData
    {
        /// <summary>這一關的題目。</summary>
        public readonly Puzzle Puzzle;

        /// <summary>盒子原點。目前恆為 <c>Zero</c>，見 <see cref="LevelSession.BoxOriginFor"/>。</summary>
        public readonly ExactVector2 BoxOrigin;

        /// <summary>盒子在盤面座標的區域。盒內判定、吸附錨點、相機 fit 都用它。</summary>
        public readonly Region BoxRegion;

        /// <summary>
        /// 參考解，**已經平移到盤面座標**（加過 <see cref="BoxOrigin"/>）。
        /// P1-3 用它三角化盒子的填色；P2 的提示覆蓋框也用它。
        /// </summary>
        public readonly Placement[] ReferenceSolution;

        /// <summary>允不允許 roll 相機。精選題要傳 false（互動設計 §2.6）。</summary>
        public readonly bool AllowRoll;

        /// <summary>這一關的盤面。</summary>
        public readonly BoardState Board;

        /// <summary>
        /// undo 的步驟堆疊（P1-9）。每一步是**動之前**那幾槽的樣子，
        /// 一般一步一筆，清空是一步七筆。
        ///
        /// <b>放這裡而不是放長生的 <see cref="LevelSession"/> 外殼上</b>：換關是整包換掉，
        /// 所以「換關要把堆疊清空」自動成立。忘記清的症狀是
        /// <b>玩家在新的一關按 undo，上一關的片被拉回來</b>，而且不報錯。
        /// </summary>
        private readonly List<PieceObservation[]> undoSteps = new List<PieceObservation[]>();

        /// <summary>
        /// 每一片在不在待用區。索引就是槽索引。
        ///
        /// <b>這是「片在不在抽屜裡」的唯一真相</b>（P1-5，待辦清單 A-4）。
        /// <c>PieceView</c> 的顯示與否是它的投影，不是第二份狀態。
        /// </summary>
        private readonly bool[] inTray = new bool[BoardState.PieceCount];

        /// <summary>
        /// 這一關的難度值（P2-6）。1200 是基準批次的平均，一個標準差 200 分。
        /// <b>沒有題目的空方框（出題工具）是 0。</b>
        ///
        /// ⭐ <b>這一層不算，由出題的人算好傳進來</b>（2026-09-13 起）。
        /// 原本建構子會自己再算一次，而出題器生題時已經算過——同一題算兩次，
        /// 權重改成必填之後兩邊得各拿一份權重，<b>就有了「出題器說 1210、這裡說 1185」的可能</b>，
        /// 而 ELO 用的是這裡的。只算一次，那件事就在結構上不可能發生；順便省掉
        /// 「被外框逼出來的片數」那段重算（每題約 0.2 ms）。
        /// ⚠️ 同一題永遠要得到同一個數字（ELO 的前提），細節見 <see cref="DifficultyEvaluator"/>。
        /// </summary>
        public readonly int Difficulty;

        /// <summary>
        /// 這一題是怎麼生出來的（P2-15）。<b>不是出題器生的題目時是 null</b>——回饋場景的題單、練習題、出題工具。
        /// ⚠️ 生成失敗退回大正方形時<b>不是 null</b>，下場是失敗，理由見 <see cref="GenerationRecord"/>。
        /// 每關一份，換關整包換掉，所以不會殘留上一題的。
        /// </summary>
        public readonly GenerationRecord? Generation;

        /// <summary>
        /// 這一局<b>要不要結算實力</b>（2026-10-14，掃描模式）。預設 true，<b>只有掃描題是 false</b>。
        ///
        /// 掃描題不算的理由：難度是逼近出來的，不是照實力挑的；而且玩家可以一直掃同一個東西刷分。
        ///
        /// ⭐ <b>為什麼記在這一關身上，不看 <c>LevelController.CurrentMode</c></b>：結算是在<b>換關前</b>補做的，
        /// 而那時候模式可能已經換了——玩完掃描題回主畫面按挑戰模式，<c>StartMode</c> 先改模式、
        /// 再進下一關、進關前才補結算，看模式的話那一局掃描題就會被當成挑戰題算進去，而且不報錯。
        /// 每關一份，換關整包換掉，所以不會殘留。
        /// </summary>
        public readonly bool CountsTowardRating;

        // ───────── 提示（2026-09-15，互動設計 §9、待辦清單〈提示〉）─────────

        /// <summary>一關最多幾次提示。</summary>
        public const int MaximumHints = 3;

        /// <summary>
        /// 提示過的<b>參考解槽</b>，照按的順序。
        ///
        /// ⭐ <b>記槽不記「蓋在哪」</b>：參考解就在這一包裡，位置隨時查得到，存兩份只會不同步。
        /// ⚠️ 清空與 undo 都不動它——提示已經用掉了（2026-09-14 定）。換關整包換掉，所以會自己歸零。
        /// </summary>
        private readonly List<int> hintedReferenceSlots = new List<int>(MaximumHints);

        /// <summary>提示過的參考解槽，照按的順序。畫面層每幀讀它畫覆蓋片。</summary>
        public IReadOnlyList<int> HintedReferenceSlots => hintedReferenceSlots;

        /// <summary>
        /// 提示用掉幾次。<b>衍生值</b>：就是 <see cref="HintedReferenceSlots"/> 的長度，不另外存一份數字。
        /// ELO 與回饋紀錄讀的是它。
        /// </summary>
        public int HintsUsed => hintedReferenceSlots.Count;

        /// <summary>
        /// <b>第一次按提示時的前景秒數</b>（回饋收集用：他在第幾秒卡住）。<b>沒按過是 0</b>——
        /// 提示最早要等一個間隔才能按，所以 0 不會跟真的值撞在一起。
        /// </summary>
        public double FirstHintForegroundSeconds { get; private set; }

        /// <summary>過關了沒。</summary>
        public bool IsSolved { get; private set; }

        // ───────── 局內統計（P2-13）。ELO 的輸入就是這幾個數字 ─────────

        /// <summary>
        /// <b>單次拖曳最多算幾秒</b>，超過的部分算進<see cref="ThinkingSeconds">思考時間</see>。
        ///
        /// ⭐ <b>這個數字不是亂訂的</b>：「移動」本質上很快——手指從抽屜拖到盤面另一端，
        /// 手機約 1 秒、平板 1 ～ 2 秒頂天。<b>超過那個就不是在移動，是拿著在想</b>。
        /// 而「拖著片在盤面上試位置」本來就該算思考，上限剛好把它重新分類。
        ///
        /// ⚠️ <b>3 秒目前仍然是猜的</b>，但之後量得出來：回饋收集場景會給一堆真人的
        /// 單次拖曳時長分布，<b>看長尾從哪裡開始就是它</b>。
        ///
        /// ⚠️ <b>要老實：短促的邊拖邊想抓不到</b>——拿著片想 2 秒、放下、再拿起來想 2 秒，
        /// 每次都在上限內，看起來跟果斷的人一樣。這是降低污染，不是排除。
        /// </summary>
        public const double MaximumSingleDragSeconds = 3.0;

        /// <summary>
        /// 拖曳累積秒數（<b>已經套過 <see cref="MaximumSingleDragSeconds"/> 上限</b>）。
        /// <b>按下就開始計，不看移動門檻</b>——點選也算（2026-09-12 定，規則只有一條才測得了）。
        /// </summary>
        public double DragSeconds { get; private set; }

        /// <summary>
        /// 拖曳了幾次。⭐ <b>記著但不進公式</b>——它讓「平均每次拖曳多久」算得出來，
        /// 而那是<b>「邊拖邊想」的指紋</b>：果斷 = 次數少每次短、邊拖邊想 = <b>次數少每次長</b>、
        /// 反覆試 = 次數多每次短。只有這個量分得出中間那種。
        /// </summary>
        public int DragCount { get; private set; }

        /// <summary>前景累積秒數——App 在前面、而且這一關還沒結束的那段時間。</summary>
        public double ForegroundSeconds { get; private set; }

        /// <summary>
        /// 思考時間 = 前景 − 拖曳。<b>衍生值，不另外存。</b>
        ///
        /// ⭐ <b>ELO 的公式用兩項：<c>w₁ × 拖曳 + w₂ × 思考</c>，而 w₂ &gt; w₁</b>——
        /// 「花在想的時間，比花在動手的時間更能說明你不會」。整把都在拖的人思考時間接近 0，
        /// 只付便宜的那個權重；拖很久的人拖曳時間很大，<b>還是會被扣，只是每秒比較便宜</b>。
        ///
        /// ⚠️ <b>它永遠 ≥ 0</b>：背景時不可能拖曳，而前景涵蓋整關，所以拖曳 ≤ 前景。
        /// <b>一旦為負就代表某個呼叫點接錯了</b>（測試 <c>思考時間_永遠不為負</c> 釘住）。
        ///
        /// ⚠️ <b>但那只在「兩個計時都停了」的時候成立</b>——遊玩途中
        /// <see cref="ForegroundSeconds"/> 還沒累加（它只在停止時加）而
        /// <see cref="DragSeconds"/> 已經有值，所以<b>這一支會是負的</b>。
        /// 這是給 <b>ELO 在一局結束後</b>讀的；要在遊玩途中看就用
        /// <see cref="ThinkingSecondsAt"/>。
        /// </summary>
        public double ThinkingSeconds => ForegroundSeconds - DragSeconds;

        /// <summary>
        /// <b>到這一刻為止</b>的思考時間，含還在跑的那一段前景。
        /// 遊玩途中要看（除錯 overlay、之後的監控面板）就用這一支。
        /// </summary>
        /// <param name="nowSeconds">現在的時刻（秒）。</param>
        /// <returns>思考秒數。</returns>
        public double ThinkingSecondsAt(double nowSeconds) =>
            ForegroundSecondsAt(nowSeconds) - DragSeconds;

        /// <summary>清空了幾次。<b>記著但不進公式</b>（undo 不記，所以它沒有對照組）。</summary>
        public int ClearCount { get; private set; }

        /// <summary>
        /// 玩家按了放棄沒。⚠️ <b>它是 ELO 唯一的「輸」訊號</b>——沒有它實力值只會單調上升。
        /// <b>扣分是 P2-14 的事，這一層只記旗標。</b>
        /// </summary>
        public bool IsAbandoned { get; private set; }

        /// <summary>
        /// 這一局結束了沒（過關或放棄）。<b>第三條冪等規則問的就是它。</b>
        /// </summary>
        public bool IsFinished => IsSolved || IsAbandoned;

        /// <summary>
        /// 拖曳或前景有沒有哪一個還在計時。
        /// ⚠️ <b>結算前要確認它是 false</b>——<see cref="ForegroundSeconds"/> 只在停止時累加，還在跑就讀會少算。
        /// </summary>
        public bool IsTiming => dragStartedAt.HasValue || foregroundStartedAt.HasValue;

        /// <summary>
        /// 這一局的 ELO 結算明細（P2-14）。<b>沒有 = 還沒結算</b>。
        ///
        /// 放這裡是因為它每關一份、讀者不只一個（兩個彈窗讀方向、之後的監控面板讀全部）。
        /// ⭐ 換關時整包換掉，<b>「還沒結算」就自動重置</b>，不必有人記得清。
        /// </summary>
        public RatingUpdate? Settlement { get; private set; }

        /// <summary>
        /// 記下這一局的結算。
        ///
        /// ⚠️ <b>兩道守衛在這一層</b>，跟 <see cref="MarkAbandoned"/> 同一個形狀——
        /// 這一格的真相在這裡，所以「一局只結算一次」由這裡保證，不靠呼叫端小心：
        /// 還沒結束不收（不然會有「結算了但還在玩」的一局）；已經記過不收（不然 ELO 會把同一局算兩遍）。
        /// </summary>
        /// <param name="update">結算明細。</param>
        /// <returns>這一次有沒有真的記下來。</returns>
        public bool RecordSettlement(RatingUpdate update)
        {
            if (!IsFinished || Settlement.HasValue) return false;
            Settlement = update;
            return true;
        }

        /// <summary>這一段拖曳的開始時刻；<c>null</c> = 現在沒在計。</summary>
        private double? dragStartedAt;

        /// <summary>這一段前景的開始時刻；<c>null</c> = 現在沒在計。</summary>
        private double? foregroundStartedAt;

        /// <param name="puzzle">這一關的題目。</param>
        /// <param name="boxOrigin">盒子原點。</param>
        /// <param name="boxRegion">盒子在盤面座標的區域。</param>
        /// <param name="referenceSolution">參考解，已平移到盤面座標。</param>
        /// <param name="difficulty">這一題的難度值，由出題的人算好，見 <see cref="Difficulty"/>。</param>
        /// <param name="allowRoll">允不允許 roll 相機。</param>
        /// <param name="generation">出題紀錄，不是出題器生的題目就不給。</param>
        /// <param name="countsTowardRating">要不要結算實力，見 <see cref="CountsTowardRating"/>。</param>
        public LevelData(Puzzle puzzle, ExactVector2 boxOrigin, Region boxRegion,
                         Placement[] referenceSolution, int difficulty, bool allowRoll,
                         GenerationRecord? generation = null, bool countsTowardRating = true)
        {
            Puzzle = puzzle;
            BoxOrigin = boxOrigin;
            BoxRegion = boxRegion;
            ReferenceSolution = referenceSolution;
            AllowRoll = allowRoll;
            Board = new BoardState();
            Difficulty = difficulty;
            Generation = generation;
            CountsTowardRating = countsTowardRating;
            IsSolved = false;

            // 一關的開頭七片都在抽屜裡。換關是整包換掉（見類別說明），
            // 所以「換關要把七片送回待用區」不必另外寫一步，也就不可能忘記
            for (int slotIndex = 0; slotIndex < BoardState.PieceCount; slotIndex++)
                inTray[slotIndex] = true;
        }

        /// <summary>
        /// 現在還能按幾次提示＝ min(3, ⌊前景秒數 ÷ 間隔⌋) − 已用次數。
        ///
        /// ⭐ <b>沒按的次數會累積</b>（2026-09-15 使用者定）：135 秒都沒按的人可以連按三次。
        /// ⭐ <b>放棄鈕的門檻是間隔 × 3</b>，跟第三次提示同時出現——間隔只有一個數字，住在按鈕那邊
        /// （<c>BoardActionButtons</c>），這一層照呼叫端給的算，跟「現在幾點」同一個模式。
        /// </summary>
        /// <param name="nowSeconds">現在的時刻（秒）。</param>
        /// <param name="intervalSeconds">每隔幾秒多開放一次，必須 &gt; 0。</param>
        /// <returns>還能按幾次，最少 0。沒有參考解（出題工具）或這一局已經結束時是 0。</returns>
        /// <exception cref="System.ArgumentOutOfRangeException">間隔不是正數。</exception>
        public int HintsAvailableAt(double nowSeconds, double intervalSeconds)
        {
            // 不擋的話間隔 0 會除出無限大、負數會讓次數變負——都是 Inspector 填錯，大聲比較好
            if (!(intervalSeconds > 0.0))
                throw new System.ArgumentOutOfRangeException(nameof(intervalSeconds), intervalSeconds, "提示間隔必須是正數");

            if (ReferenceSolution == null || IsFinished) return 0;

            double unlocked = System.Math.Floor(ForegroundSecondsAt(nowSeconds) / intervalSeconds);
            int unlockedCount = unlocked >= MaximumHints ? MaximumHints : (int)unlocked;
            int available = unlockedCount - HintsUsed;
            return available > 0 ? available : 0;
        }

        /// <summary>
        /// 參考解的這一槽<b>現在算不算已經擺對</b>：盤上有沒有<b>同片種、頂點集合一樣</b>的片。
        ///
        /// ⚠️ <b>不比 <c>Placement.Equals</c>、也不綁槽</b>（互動設計 §9〈完成判定的陷阱〉）——
        /// 正方形轉 0／2、兩片大三角互換、兩片小三角互換都要算對。綁槽的話玩家拿「另一片」大三角放對了，
        /// 提示還一直閃，他會以為自己擺錯。
        ///
        /// 畫面層每幀拿它決定「這個提示還要不要畫」（擺對時不畫，被拿走又出現）。
        /// ⚠️ <b>看的是盤面，不是畫面</b>：拖曳中吸住只是預覽，所以提示要等鬆手那一格才消失。
        /// </summary>
        /// <param name="referenceSlot">參考解的槽索引 0~6。</param>
        /// <returns>擺對了 true。沒有參考解時永遠 false。</returns>
        /// <exception cref="System.ArgumentOutOfRangeException">槽索引超出範圍。</exception>
        public bool IsReferenceSlotSatisfied(int referenceSlot)
        {
            if (referenceSlot < 0 || referenceSlot >= BoardState.PieceCount)
                throw new System.ArgumentOutOfRangeException(nameof(referenceSlot), referenceSlot, "槽索引超出範圍");
            if (ReferenceSolution == null) return false;

            Placement target = ReferenceSolution[referenceSlot];
            ExactVector2[] targetVertices = null;       // 盤上沒有同片種的片時就不必算

            for (int slotIndex = 0; slotIndex < BoardState.PieceCount; slotIndex++)
            {
                PieceObservation observation = Board[slotIndex];
                if (!observation.OnBoard || observation.Kind != target.Kind) continue;

                targetVertices ??= target.Vertices();
                if (Geometry.SameVertexSet(targetVertices, observation.Placement.Vertices())) return true;
            }
            return false;
        }

        /// <summary>
        /// 按一次提示：挑參考解裡的一片記下來，畫面層就會把它蓋上去。
        ///
        /// <b>挑哪一片</b>（沒有求解器，挑不到「跟玩家現在盤面相容的解」，所以只看參考解）：
        /// <list type="number">
        /// <item>拿掉已經提示過的、已經擺對的（<see cref="IsReferenceSlotSatisfied"/>）</item>
        /// <item><b>位置是空的優先</b>（跟盤上每一片都不重疊）；空的沒有才挑被佔住的——
        ///   蓋在玩家的片上面本身就是「這裡擺錯了」的訊號</item>
        /// <item>同一組裡<b>面積大的先</b>（大片資訊量最大，§9）；一樣大就挑槽號小的，
        ///   同一個盤面永遠挑出同一片，才測得了</item>
        /// </list>
        ///
        /// ⚠️ <b>一個候選都沒有就不扣次數</b>（例如四片擺對、三片提示過）——扣了的話玩家按了沒反應還被算一次。
        /// </summary>
        /// <param name="nowSeconds">現在的時刻（秒）。</param>
        /// <param name="intervalSeconds">提示間隔，見 <see cref="HintsAvailableAt"/>。</param>
        /// <param name="referenceSlot">挑中的參考解槽；沒有提示時是 −1。</param>
        /// <returns>有沒有真的用掉一次。</returns>
        public bool TryUseHint(double nowSeconds, double intervalSeconds, out int referenceSlot)
        {
            referenceSlot = -1;
            if (HintsAvailableAt(nowSeconds, intervalSeconds) <= 0) return false;

            int bestSlot = PickHintSlot();
            if (bestSlot < 0) return false;

            // 第一次按的那一刻記前景秒數（HintsUsed 還沒加上去之前問）
            if (HintsUsed == 0) FirstHintForegroundSeconds = ForegroundSecondsAt(nowSeconds);
            hintedReferenceSlots.Add(bestSlot);
            referenceSlot = bestSlot;
            return true;
        }

        /// <summary>
        /// 提示鈕現在按下去<b>會不會真的有東西</b>：還有次數，而且挑得到一片。
        /// 提示鈕的顯示條件就是它——跟 <see cref="TryUseHint"/> 共用同一個挑片算式，所以「看得到鈕、按了沒反應」不會發生。
        /// </summary>
        /// <param name="nowSeconds">現在的時刻（秒）。</param>
        /// <param name="intervalSeconds">提示間隔。</param>
        /// <returns>按下去會用掉一次的話 true。</returns>
        public bool CanUseHintAt(double nowSeconds, double intervalSeconds) =>
            HintsAvailableAt(nowSeconds, intervalSeconds) > 0 && PickHintSlot() >= 0;

        /// <summary>照 <see cref="TryUseHint"/> 說明的規則挑一片，只算不記。</summary>
        /// <returns>挑中的參考解槽；一個候選都沒有時是 −1。</returns>
        private int PickHintSlot()
        {
            Placement[] onBoard = Board.OnBoardPlacements();
            int bestSlot = -1;
            bool bestIsEmpty = false;
            Scalar bestDoubleArea = Scalar.Zero;

            for (int slotIndex = 0; slotIndex < BoardState.PieceCount; slotIndex++)
            {
                if (hintedReferenceSlots.Contains(slotIndex) || IsReferenceSlotSatisfied(slotIndex)) continue;

                Placement candidate = ReferenceSolution[slotIndex];
                bool isEmpty = !OverlapsAny(candidate, onBoard);
                Scalar doubleArea = candidate.Prototype.DoubleArea;

                // 由小到大掃槽號，所以「一樣好」時不換 = 槽號小的贏
                bool better = bestSlot < 0
                              || (isEmpty && !bestIsEmpty)
                              || (isEmpty == bestIsEmpty && doubleArea > bestDoubleArea);
                if (!better) continue;

                bestSlot = slotIndex;
                bestIsEmpty = isEmpty;
                bestDoubleArea = doubleArea;
            }
            return bestSlot;

            static bool OverlapsAny(Placement candidate, Placement[] placements)
            {
                ExactVector2[] candidateVertices = candidate.Vertices();
                foreach (Placement placement in placements)
                    if (Geometry.ConvexOverlap(candidateVertices, placement.Vertices())) return true;
                return false;
            }
        }

        // ───────── 局內統計的四件事（P2-13）─────────
        //
        // ⚠️ 三條冪等規則，整段最重要的東西：
        //   ① 已經在計時再叫「開始」→ 什麼都不做
        //      （少了它，連續兩次「開始」會蓋掉第一段的開始時刻，那段時間永遠漏掉而且不報錯）
        //   ② 沒在計時叫「停止」→ 什麼都不做
        //      （這條讓「兩個出口都叫停止」變成安全的，見 TouchInputController 的呼叫點）
        //   ③ 這一局已經結束 → 所有計時請求一律忽略
        //      （放棄那一幀彈窗還沒出現，玩家還碰得到螢幕；他剛好按下一片就會重新開始計時）
        //
        // ⚠️ 現在幾點由呼叫端傳進來，這一層不知道——這樣測試傳假時間就驗得完，
        // 完全不用開 Unity，而且「用哪個時鐘」整個住在呼叫端。
        // 💡 同一個模式：Serialization.ToDto 的 createdAtUtc。

        /// <summary>開始計拖曳。已經在計、或這一局已經結束時什麼都不做。</summary>
        /// <param name="nowSeconds">現在的時刻（秒）。來源見 <c>LevelController</c>。</param>
        public void StartDragTiming(double nowSeconds)
        {
            if (IsFinished || dragStartedAt.HasValue) return;
            dragStartedAt = nowSeconds;
            DragCount++;
        }

        /// <summary>
        /// 停止計拖曳並累加。沒在計時就什麼都不做。
        /// ⚠️ <b>這裡是套上限的唯一地方</b>——超過 <see cref="MaximumSingleDragSeconds"/> 的部分
        /// 不計進拖曳，而因為思考時間是「前景 − 拖曳」算出來的，<b>那段自動落到思考那邊</b>。
        /// </summary>
        /// <param name="nowSeconds">現在的時刻（秒）。</param>
        public void StopDragTiming(double nowSeconds)
        {
            if (!dragStartedAt.HasValue) return;

            double elapsed = nowSeconds - dragStartedAt.Value;
            dragStartedAt = null;

            // 時刻倒退代表呼叫端換了時鐘或傳錯值。不擋的話累積會變小甚至變負，
            // 而「思考時間為負」的斷言會在很遠的地方才炸，查起來看不出源頭
            if (elapsed <= 0.0) return;

            DragSeconds += elapsed > MaximumSingleDragSeconds ? MaximumSingleDragSeconds : elapsed;
        }

        /// <summary>開始計前景。已經在計、或這一局已經結束時什麼都不做。</summary>
        /// <param name="nowSeconds">現在的時刻（秒）。</param>
        public void StartForegroundTiming(double nowSeconds)
        {
            if (IsFinished || foregroundStartedAt.HasValue) return;
            foregroundStartedAt = nowSeconds;
        }

        /// <summary>
        /// 停止計前景並累加。沒在計時就什麼都不做。
        /// ⚠️ <b>前景沒有上限</b>——上限是為了把「拿著片在想」歸類成思考，而前景本來就包含思考。
        /// </summary>
        /// <param name="nowSeconds">現在的時刻（秒）。</param>
        public void StopForegroundTiming(double nowSeconds)
        {
            if (!foregroundStartedAt.HasValue) return;

            double elapsed = nowSeconds - foregroundStartedAt.Value;
            foregroundStartedAt = null;
            if (elapsed <= 0.0) return;

            ForegroundSeconds += elapsed;
        }

        /// <summary>
        /// 這一局結束了，兩個計時都停。<b>過關與放棄都走這一支</b>，
        /// 這樣「結束時要停哪些計時」只寫在一個地方。
        /// </summary>
        /// <param name="nowSeconds">現在的時刻（秒）。</param>
        public void StopAllTiming(double nowSeconds)
        {
            StopDragTiming(nowSeconds);
            StopForegroundTiming(nowSeconds);
        }

        /// <summary>
        /// <b>到這一刻為止</b>的前景時間——含<b>還在跑的那一段</b>。
        ///
        /// ⚠️ <see cref="ForegroundSeconds"/> 只在「停止」時才累加，所以整關玩下來它一直是 0，
        /// <b>問它等於什麼都問不到</b>。要即時知道「這一關卡了多久」（放棄鈕與之後的提示鈕
        /// 靠它決定要不要出現）就得用這一支。
        ///
        /// <b>只讀，不改任何狀態</b>——每幀問也沒關係。
        /// </summary>
        /// <param name="nowSeconds">現在的時刻（秒）。</param>
        /// <returns>前景累積秒數，含還在跑的那一段。</returns>
        public double ForegroundSecondsAt(double nowSeconds)
        {
            if (!foregroundStartedAt.HasValue) return ForegroundSeconds;

            double running = nowSeconds - foregroundStartedAt.Value;
            return running > 0.0 ? ForegroundSeconds + running : ForegroundSeconds;
        }

        /// <summary>記一次清空。</summary>
        public void RecordClear() => ClearCount++;

        /// <summary>
        /// 標記放棄，並把兩個計時停掉。
        ///
        /// ⚠️ <b>已經過關就不做事</b>——互斥由這一層保證，不是由 UI 保證。
        /// 不擋的話這一局會<b>同時</b>是「放棄」與「過關」，而 ELO 會把它算成兩件事。
        /// 💡 跟 <see cref="MarkSolvedIfAllOnBoard"/> 那邊的守衛是同一對。
        /// </summary>
        /// <param name="nowSeconds">現在的時刻（秒）。</param>
        /// <returns>這一次有沒有真的標記成放棄。</returns>
        public bool MarkAbandoned(double nowSeconds)
        {
            if (IsFinished) return false;

            // ⚠️ 順序：先停計時再標旗標。反過來的話 IsFinished 已經是 true，
            // 而 StopDragTiming 雖然不看它、StartXxx 卻會——留著這個順序比較不容易寫錯
            StopAllTiming(nowSeconds);
            IsAbandoned = true;
            return true;
        }

        /// <summary>標記過關。</summary>
        public void MarkSolved() => IsSolved = true;

        /// <summary>
        /// 七片都在盤上就標記過關（P1-10，互動設計 §7）。
        ///
        /// <b>判定就是 <see cref="BoardState.AllOnBoard"/>，不需要算面積、也不需要比形狀。</b>
        /// ⚠️ 這個等價關係<b>是靠吸附的硬限制撐著的</b>：片只吸得進盒內、而且不能重疊
        /// （<c>SnapSolver</c> 的 <c>isAllowed</c> 與重疊排除），所以「在盤上」⟹「合法擺放」。
        /// 七片面積總和 32 = 盒子面積，於是七片都在盤上就必然填滿。
        ///
        /// <b>哪天硬限制鬆掉（例如開放盒外自由拼），這一條就不成立了</b>——
        /// 那時候要回來改判定，而這段註解是唯一的警訊。
        ///
        /// <b>單向的</b>：只會把 <see cref="IsSolved"/> 設成 true，不會設回 false。
        /// 過關之後彈窗會蓋住整個畫面，玩家只剩「退出」與「下一關」兩個出口，
        /// 所以不需要「退回未過關」那條路；真正的重置是換關（整包資料換掉）。
        ///
        /// ⚠️ <b>沒有題目的盤面永遠不會過關</b>（出題工具那個空方框，見
        /// <see cref="LevelSession.LoadBoxOnly"/>）。那裡的盒子面積遠大於 32，
        /// 上面那條「七片都在盤上 ⟹ 填滿」在工具裡剛好就是不成立的那個情況。
        /// 而且真讓它標成過關的話，<c>TouchInputController.IsLevelComplete</c> 會把
        /// 旋轉／翻面鈕鎖掉——工具正好要在七片都放上去之後繼續微調。
        /// </summary>
        /// <returns>這一關過關了沒（呼叫之後的狀態）。</returns>
        public bool MarkSolvedIfAllOnBoard()
        {
            if (Puzzle == null) return false;

            // ⚠️ 已經放棄的一局不能再過關（P2-13）。互斥由這一層保證，不是由 UI——
            // 放棄彈窗雖然會蓋住整個畫面，但那要等下一幀 Update 才出現。
            // 不擋的話這一局同時是「放棄」與「過關」，ELO 會算成兩件事
            if (IsAbandoned) return false;

            if (Board.AllOnBoard) MarkSolved();
            return IsSolved;
        }

        /// <param name="slotIndex">槽索引 0~6。</param>
        /// <returns>這一片在不在待用區（也就是抽屜裡、畫面上看不到）。</returns>
        public bool IsInTray(int slotIndex) => inTray[slotIndex];

        /// <summary>已經被拿出抽屜的片有幾片。除錯 overlay 用。</summary>
        public int OutOfTrayCount
        {
            get
            {
                int count = 0;
                for (int slotIndex = 0; slotIndex < BoardState.PieceCount; slotIndex++)
                    if (!inTray[slotIndex]) count++;
                return count;
            }
        }

        /// <summary>
        /// 把一片放進或拿出待用區。
        ///
        /// ⚠️ <b>「在盤上」與「在待用區」是互斥的</b>，違反會丟例外而不是默默修正——
        /// 默默修正的話症狀是「片在抽屜裡但盤面認為它還在盒內」，
        /// 過關判定與中途回饋會跟著錯而且沒有任何訊息。
        /// 所以回收一片盤上的片時，順序一定是**先 <c>Lift</c> 再送回待用區**。
        /// </summary>
        /// <param name="slotIndex">槽索引 0~6。</param>
        /// <param name="value">true = 收進待用區；false = 拿出來（畫面上會出現）。</param>
        /// <exception cref="System.InvalidOperationException">
        /// 想把一片<b>還在盤上</b>的片收進待用區時丟出。
        /// </exception>
        public void SetInTray(int slotIndex, bool value)
        {
            if (value && Board[slotIndex].OnBoard)
                throw new System.InvalidOperationException(
                    $"第 {slotIndex} 槽還在盤面上，不能直接收進待用區。先 Lift 再送回去。");

            inTray[slotIndex] = value;
        }

        // ─────────────────────────────────────────────────────────────
        // undo（P1-9）
        //
        // 一步的定義是**只有「放進盤面」算**，清空另外算一步七筆。
        // 旋轉、翻面、暫放、從抽屜拿出來、丟進回收框都不算——它們不動 BoardState。
        //
        // ⚠️ 所以它是「拿回上一片」不是「退回上一步」。玩家最後做的如果是旋轉，
        // 按下去會跳過它去撤銷更早的入盤動作。UI 上不要叫「復原」。
        // ─────────────────────────────────────────────────────────────

        /// <summary>還有東西可以退嗎。按鈕拿它決定要不要顯示。</summary>
        public bool CanUndo => undoSteps.Count > 0;

        /// <summary>
        /// 記下一步。傳進來的是**動之前**那幾槽的樣子，不是動之後。
        ///
        /// ⚠️ <b>推入點由呼叫端決定，不能搬進 <see cref="BoardState"/> 自動推</b>——
        /// 「哪些盤面變動算一步」是政策不是機制。自動推的話
        /// <see cref="TryUndo"/> 自己執行的 <c>Place</c> / <c>Lift</c> 也會被記進去，
        /// 那會變成按一次 undo 就無限循環。
        ///
        /// 呼叫的樣子是在 <c>Place</c> <b>之前</b>先取 <c>Board[slotIndex]</c> 傳進來。
        /// <see cref="PieceObservation"/> 是 <c>readonly struct</c>，而 <c>Board[i]</c>
        /// 回傳的就是複本，所以不會有「記下去之後又被改掉」的別名問題。
        /// </summary>
        /// <param name="observationsBefore">
        /// 動之前那幾槽的樣子。一般一筆；清空是七筆，會被當成**一步**退回去。
        /// 空陣列或 null 會被忽略（不會壓進一個退了等於沒退的空步驟）。
        /// </param>
        public void PushUndoStep(params PieceObservation[] observationsBefore)
        {
            if (observationsBefore == null || observationsBefore.Length == 0) return;
            undoSteps.Add(observationsBefore);
        }

        /// <summary>
        /// 退一步。<b>模型的部分在這裡做完，畫面的部分不在</b>——
        /// 回傳的那幾筆就是「每一槽該變回什麼樣子」，由控制器套用到 <c>PieceView</c>
        /// （待辦清單〈畫面同步歸控制器〉）。
        ///
        /// 兩條分支看的是<b>動之前在不在盤上</b>：
        /// <list type="bullet">
        /// <item><c>true</c>＝這一步原本是「移動」→ 放回舊的擺放，仍在盤上</item>
        /// <item><c>false</c>＝這一步原本是「放進去」→ 從盤面拿下來，送回待用區</item>
        /// </list>
        ///
        /// <b>為什麼記狀態不記動作</b>：記動作的話要處理「同一片在堆疊裡有多筆，
        /// 撤銷較早那筆時它已經不在盤上」。記 <see cref="PieceObservation"/> 記的是
        /// 「回到什麼樣子」，每一筆跟其他筆無關，那個情況根本不存在。
        /// 而且是免費的——那個 <c>OnBoard</c> 本來就在 struct 裡。
        /// </summary>
        /// <param name="restored">
        /// 退回去的那幾槽**動之前的樣子**，也就是它們現在該有的樣子。
        /// 沒東西可退時是 null。
        /// </param>
        /// <returns>有沒有真的退了一步。</returns>
        public bool TryUndo(out IReadOnlyList<PieceObservation> restored)
        {
            restored = null;
            if (undoSteps.Count == 0) return false;

            PieceObservation[] step = undoSteps[undoSteps.Count - 1];
            undoSteps.RemoveAt(undoSteps.Count - 1);

            // ⚠️ 三段的順序是為了讓「在盤上」與「在待用區」互斥這條**在每個時刻都成立**，
            // 包括 ReplaceAll 觸發 Changed 的那一刻。順序換掉的話有兩種壞法：
            //
            //   先設待用區再改盤面 → 要送回待用區的那幾片此刻還在盤上 → SetInTray 丟例外
            //   先改盤面再設待用區 → Changed 送出去的那一瞬間，回到盤上的片
            //                        待用區旗標還是 true（TrayView 會把它藏起來）
            //
            // 第一段只做 false 方向，而 SetInTray(_, false) 永遠不會丟例外
            // （它只檢查 value == true 的情況），所以放在改盤面之前是安全的
            foreach (PieceObservation before in step)
                if (before.OnBoard) SetInTray(before.Index, false);

            // 第二段：整盤一次寫完，只發一次 Changed。
            // ⚠️ before 本身就是「該變回去的樣子」，所以這裡不需要任何轉換——
            // 沒被這一步動到的槽保持現狀
            PieceObservation[] next = new PieceObservation[BoardState.PieceCount];
            for (int slotIndex = 0; slotIndex < BoardState.PieceCount; slotIndex++)
                next[slotIndex] = Board[slotIndex];
            foreach (PieceObservation before in step) next[before.Index] = before;
            Board.ReplaceAll(next);

            // 第三段：這時候它們在盤面上已經是「不在盤上」了，所以設得進去
            foreach (PieceObservation before in step)
                if (!before.OnBoard) SetInTray(before.Index, true);

            restored = step;
            return true;
        }

        /// <summary>
        /// 清空：<b>把盤面上放好的片全部推回待用區</b>（2026-09-09 定案）。
        ///
        /// ⚠️ <b>暫放的片不動。</b>清空的定義刻意只涵蓋「在盤上」那一組，
        /// 好處是<b>動到的集合跟記下來的集合完全一樣</b>，所以 undo 是精確還原。
        ///
        /// 反過來（連暫放的一起收）就記不回去了：<see cref="PieceObservation"/> 只知道
        /// 「在不在盤上」，分不出「在待用區」與「暫放」，而暫放的位置是純浮點、
        /// 只活在 <c>PieceView</c> 上，<see cref="BoardState"/> 根本不知道。
        /// 那會變成「undo 之後桌面上散著的那幾片跑進抽屜了」，而且沒辦法靠加欄位修。
        ///
        /// 跟 <see cref="TryUndo"/> 一樣，<b>模型做完就回傳，畫面由控制器套用</b>。
        /// </summary>
        /// <param name="cleared">
        /// 被收回去的那幾槽**清空之前的樣子**（每一筆的 <c>OnBoard</c> 都是 true）。
        /// 盤面本來就是空的時候是 null。
        /// </param>
        /// <returns>有沒有真的清掉東西。盤面空的時候回 false，而且<b>不會壓進一個空步驟</b>。</returns>
        /// <summary>
        /// ⚠️ <b>開發用的捷徑（P2-4）</b>：照參考解把片填上去，只留一槽自己拼。
        ///
        /// <b>為什麼在模型層而不是在除錯工具裡</b>：它會**寫盤面**。
        /// <c>DebugOverlay</c> 的契約是「絕對不碰任何狀態，只讀」，
        /// 除錯工具一旦會改東西，它自己就變成 bug 來源。
        /// 而寫盤面這件事的規則（順序、不變式、undo）只能有一份，所以放這裡，
        /// 跟 <see cref="TryClearBoardToTray"/> 並排——畫面那半一樣歸控制器。
        ///
        /// <b>整組算 undo 的一步</b>，按一次 undo 就整個還原。
        ///
        /// ⚠️ <b>快照存的是七槽全部</b>，不是只有被動到的那幾槽。填之前要先清空，
        /// 所以哪幾槽會變其實取決於當下的盤面；全部存下來就不必推理，
        /// 而 <see cref="TryUndo"/> 對「動之前不在盤上」的槽本來就會處理成送回待用區。
        /// </summary>
        /// <param name="slotToLeaveEmpty">留哪一槽不填。</param>
        /// <param name="affected">
        /// 動之前七槽的樣子。<b>呼叫端要照現在的盤面重畫全部七槽</b>，不是只重畫這幾筆。
        /// </param>
        /// <returns>有沒有真的填。已經過關時回 false。</returns>
        /// <exception cref="System.ArgumentOutOfRangeException">槽索引超出範圍。</exception>
        public bool TryFillFromReferenceSolution(int slotToLeaveEmpty,
                                                 out IReadOnlyList<PieceObservation> affected)
        {
            affected = null;
            if (slotToLeaveEmpty < 0 || slotToLeaveEmpty >= BoardState.PieceCount)
                throw new System.ArgumentOutOfRangeException(nameof(slotToLeaveEmpty));
            if (IsSolved) return false;

            var before = new PieceObservation[BoardState.PieceCount];
            for (int slotIndex = 0; slotIndex < BoardState.PieceCount; slotIndex++)
                before[slotIndex] = Board[slotIndex];
            PushUndoStep(before);

            // ⚠️ 順序跟清空同一組陷阱：先讓盤面放手，再設待用區。
            // 反過來的話 SetInTray 會對著還在盤上的片丟例外
            Board.Clear();
            for (int slotIndex = 0; slotIndex < BoardState.PieceCount; slotIndex++)
                SetInTray(slotIndex, true);

            for (int slotIndex = 0; slotIndex < BoardState.PieceCount; slotIndex++)
            {
                if (slotIndex == slotToLeaveEmpty) continue;
                SetInTray(slotIndex, false);
                Board.Place(slotIndex, ReferenceSolution[slotIndex]);
            }

            affected = before;
            return true;
        }

        /// <summary>
        /// <b>開局就先擺好幾片</b>：照參考解把片放上去，只留指定的幾槽在抽屜裡。
        /// 給回饋收集的<b>練習題</b>用（2026-09-13，見 <c>FeedbackPractice</c>）。
        ///
        /// 跟 <see cref="TryFillFromReferenceSolution"/> 的差別有兩個，都是刻意的：
        /// <list type="number">
        /// <item><b>不壓 undo</b>：這幾片是「題目本來的樣子」，不是玩家的一步。壓了的話受試者按 undo，
        ///   擺好的五片會整組飛回抽屜——剛好在教操作的那一刻出現一個莫名其妙的畫面</item>
        /// <item><b>只能在一關剛開始用</b>：盤面必須是空的、這一關還沒結束。中途用的話會蓋掉玩家已經放的片，
        ///   而且因為不壓 undo，那些片再也拿不回來</item>
        /// </list>
        /// </summary>
        /// <param name="slotsToLeaveEmpty">留在抽屜裡的槽。</param>
        /// <returns>有沒有真的擺。沒有題目、已經結束、或盤面不是空的時回 false。</returns>
        /// <exception cref="System.ArgumentOutOfRangeException">槽索引超出範圍。</exception>
        public bool TryPrefillAtStart(IReadOnlyCollection<int> slotsToLeaveEmpty)
        {
            foreach (int slotIndex in slotsToLeaveEmpty)
                if (slotIndex < 0 || slotIndex >= BoardState.PieceCount)
                    throw new System.ArgumentOutOfRangeException(nameof(slotsToLeaveEmpty), slotIndex, "槽索引超出範圍");

            if (Puzzle == null || ReferenceSolution == null || IsFinished) return false;
            for (int slotIndex = 0; slotIndex < BoardState.PieceCount; slotIndex++)
                if (Board[slotIndex].OnBoard) return false;

            var leaveEmpty = new HashSet<int>(slotsToLeaveEmpty);
            for (int slotIndex = 0; slotIndex < BoardState.PieceCount; slotIndex++)
            {
                if (leaveEmpty.Contains(slotIndex)) continue;

                // ⚠️ 順序：先離開待用區再上盤面（同其他填盤面的方法）
                SetInTray(slotIndex, false);
                Board.Place(slotIndex, ReferenceSolution[slotIndex]);
            }
            return true;
        }

        /// <summary>
        /// 用一組指定的擺放蓋掉整個盤面。<b>給出題工具的「載入之前存過的題目」用</b>
        /// （遊玩迴圈不會呼叫它——玩家的片只能自己拖）。
        ///
        /// 跟 <see cref="TryFillFromReferenceSolution"/> 的差別只有一個：擺放是呼叫端給的，
        /// 不是這一關的參考解。工具那邊沒有參考解，它的來源是存檔解碼出來的七片。
        ///
        /// <b>整組算一步 undo</b>，所以載入之後按一次 undo 會回到載入前的盤面。
        /// </summary>
        /// <param name="placementsBySlot">
        /// 七個擺放，<b>索引就是槽索引</b>，而且每一槽的片種必須跟 <see cref="BoardState"/>
        /// 那一槽的片種相同。順序不對的話會丟例外——默默放錯槽的症狀是
        /// 「畫面上是正方形，模型卻以為那是小三角」，之後每一步都跟著錯而且不報錯。
        /// </param>
        /// <param name="affected">動之前的七槽（undo 用）。</param>
        /// <returns>有沒有真的填。已經過關時回 false。</returns>
        /// <exception cref="System.ArgumentException">數量不是七片，或某一槽的片種對不上。</exception>
        public bool TryFillFromPlacements(IReadOnlyList<Placement> placementsBySlot,
                                          out IReadOnlyList<PieceObservation> affected)
        {
            affected = null;
            if (placementsBySlot == null || placementsBySlot.Count != BoardState.PieceCount)
                throw new System.ArgumentException(
                    $"要給滿 {BoardState.PieceCount} 片，現在是 {placementsBySlot?.Count ?? 0} 片",
                    nameof(placementsBySlot));

            for (int slotIndex = 0; slotIndex < BoardState.PieceCount; slotIndex++)
                if (placementsBySlot[slotIndex].Kind != Board.KindOf(slotIndex))
                    throw new System.ArgumentException(
                        $"第 {slotIndex} 槽是 {Board.KindOf(slotIndex)}，"
                        + $"但給的是 {placementsBySlot[slotIndex].Kind}", nameof(placementsBySlot));

            if (IsSolved) return false;

            var before = new PieceObservation[BoardState.PieceCount];
            for (int slotIndex = 0; slotIndex < BoardState.PieceCount; slotIndex++)
                before[slotIndex] = Board[slotIndex];
            PushUndoStep(before);

            // ⚠️ 順序跟清空、undo 同一組陷阱：先讓盤面放手，再設待用區。
            // 反過來的話 SetInTray 會對著還在盤上的片丟例外
            Board.Clear();
            for (int slotIndex = 0; slotIndex < BoardState.PieceCount; slotIndex++)
                SetInTray(slotIndex, true);

            for (int slotIndex = 0; slotIndex < BoardState.PieceCount; slotIndex++)
            {
                SetInTray(slotIndex, false);
                Board.Place(slotIndex, placementsBySlot[slotIndex]);
            }

            affected = before;
            return true;
        }

        public bool TryClearBoardToTray(out IReadOnlyList<PieceObservation> cleared)
        {
            cleared = null;

            var onBoard = new List<PieceObservation>(BoardState.PieceCount);
            for (int slotIndex = 0; slotIndex < BoardState.PieceCount; slotIndex++)
                if (Board[slotIndex].OnBoard) onBoard.Add(Board[slotIndex]);

            if (onBoard.Count == 0) return false;

            // 整組當成**一步**，所以按一次 undo 就整個還原（不是一片一片退）
            PieceObservation[] before = onBoard.ToArray();
            PushUndoStep(before);

            // ⚠️ 順序跟 TryUndo 同一組陷阱：一定先讓盤面放手，再設待用區，
            // 不然 SetInTray 會對著還在盤上的片丟例外。
            // Clear 送出 Changed 的那一刻，這幾片是「不在盤上也不在待用區」＝暫放，
            // 那是合法狀態，不變式沒有破
            Board.Clear();
            foreach (PieceObservation observation in before) SetInTray(observation.Index, true);

            cleared = before;
            return true;
        }
    }

    /// <summary>
    /// 這一關的真相（互動設計 §14.1）。純 C#、<b>長生</b>、可離線測。
    ///
    /// <b>它不是 <c>LevelController</c> 的從屬。</b>兩者是平行的：
    /// 這裡的消費者有一大票（<c>BoxView</c>、拖曳指揮者、中途回饋、除錯 overlay、
    /// 過關判定、之後的提示），而 <c>LevelController</c> 只是其中一個——
    /// 它只負責「進來／出去」那兩個時機。掛在它底下的話所有人都要穿過一個
    /// <c>MonoBehaviour</c> 拿資料，而那是測不了的。
    ///
    /// <b>分界線是消費者數量</b>：對照 <c>BoardView</c> 持有 <see cref="BoardLayout"/>
    /// 就沒問題，因為後者只有前者自己讀。
    ///
    /// <b>換關是原地重置，不是重建。</b>這個物件本身不死，所以別人可以安心持有它的參照；
    /// 換掉的是 <see cref="Data"/> 那一整包。重建的話所有快取了 <c>Board</c> 的人
    /// 都會寫到死掉的那一份，而且不會報錯（見 <see cref="LevelData"/> 的警告）。
    /// </summary>
    public sealed class LevelSession
    {
        /// <summary>
        /// 目前這一場。由 <c>LevelController</c> 在 <c>Awake</c> 設定一次。
        ///
        /// <b>這只是方便的取用點，不是唯一的取得方式</b>——這個型別可以直接 <c>new</c>，
        /// 測試就是這樣用的，不要去碰這個靜態欄位。
        /// </summary>
        public static LevelSession Current { get; set; }

        /// <summary>
        /// 玩家的實力（P2-14）。由 <c>LevelController</c> 在 Awake 讀完設定之後建一次。
        ///
        /// <b>放外殼上不放 <see cref="LevelData"/></b>：實力跨關延續，而 <see cref="Data"/> 換關時整包換掉。
        /// ⚠️ <b>可能是 null</b>——出題工具場景不給（那裡沒有題目、不結算），設定檔讀不到時也是 null。
        /// </summary>
        public SkillRatingTracker Rating { get; set; }

        /// <summary>
        /// 這一關的資料。還沒載入任何一關時是 <c>null</c>。
        ///
        /// ⚠️ <b>每次要用都跟這裡拿，不要把回傳的東西存成欄位</b>——換關時整包會被換掉。
        /// </summary>
        public LevelData Data { get; private set; }

        /// <summary>有沒有載入中的關卡。</summary>
        public bool HasLevel => Data != null;

        /// <summary>
        /// 載入一關。**舊的那一包整個丟掉，不是逐欄位清空**——
        /// 之後往 <see cref="LevelData"/> 新增欄位會自動被涵蓋，不必回頭改這裡。
        /// </summary>
        /// <param name="puzzle">題目。</param>
        /// <param name="referenceSolution">
        /// 這個題目的一組解，**在題目自己的座標系**。這裡會平移到盤面座標再存起來。
        /// </param>
        /// <param name="difficulty">
        /// 這一題的難度值。⚠️ <b>由出題的人算好傳進來，這裡不重算</b>——
        /// 照難度生的話就是 <c>TargetedGenerationResult.Difficulty</c>，理由見 <see cref="LevelData.Difficulty"/>。
        /// </param>
        /// <param name="allowRoll">允不允許 roll 相機。精選題傳 false。</param>
        /// <param name="generation">
        /// 出題紀錄（P2-15）。<b>選填</b>：只有照難度生的那條路會給，回饋場景、練習題不給，呼叫端一行都不用改。
        /// </param>
        /// <param name="countsTowardRating">
        /// 要不要結算實力（2026-10-14）。<b>選填、預設 true</b>，只有掃描題傳 false，見 <see cref="LevelData.CountsTowardRating"/>。
        /// </param>
        public void Load(Puzzle puzzle, IReadOnlyList<Placement> referenceSolution, int difficulty, bool allowRoll,
                         GenerationRecord? generation = null, bool countsTowardRating = true)
        {
            ExactVector2 boxOrigin = BoxOriginFor(puzzle);
            Region boxRegion = Region.FromPuzzle(puzzle, boxOrigin);

            // 參考解跟 walks 一樣要平移到盤面座標。目前 boxOrigin 是 Zero，所以這是 no-op——
            // 但寫在這裡，之後改了原點規則就不會忘記（忘了的話覆蓋框會整組偏移，而且不報錯）
            var translated = new Placement[referenceSolution.Count];
            for (int i = 0; i < referenceSolution.Count; i++)
                translated[i] = referenceSolution[i].Translated(boxOrigin);

            Data = new LevelData(puzzle, boxOrigin, boxRegion, translated, difficulty, allowRoll, generation,
                                 countsTowardRating);
        }

        /// <summary>
        /// <b>只載入一個空的盒子，沒有題目也沒有參考解。</b>
        ///
        /// ⚠️ <b>這一支是給出題工具用的，遊玩迴圈不會呼叫它。</b>
        /// 工具（把古典七巧板圖形拼出來、存成題目的那個獨立場景）需要一個「很大的空方框」
        /// 當作業區：七片在框內自由拼，拼完才知道形狀是什麼。反過來說，
        /// <see cref="Load"/> 的前提是「題目已經存在」——它從題目的輪廓反推盒子區域，
        /// 工具那邊沒有東西可以反推。
        ///
        /// 用同一份 <see cref="LevelData"/> 而不是另外寫一套狀態，是為了讓吸附、抽屜、
        /// 旋轉翻面、undo 全部原樣沿用；它們都是跟 <c>LevelSession.Current</c> 拿資料的。
        ///
        /// ⚠️ 這樣載入的 <c>Data</c> 有三個欄位是空的或沒有意義：
        /// <c>Puzzle</c> 與 <c>ReferenceSolution</c> 是 null、<c>Difficulty</c> 是 0。
        /// 所以<b>任何會碰參考解的功能在工具場景都不能用</b>（提示、盒子填色、
        /// <c>TryFillFromReferenceSolution</c>），呼叫端要自己避開。
        /// </summary>
        /// <param name="boxRegion">作業區。工具目前給的是一個邊長 12 的正方形。</param>
        public void LoadBoxOnly(Region boxRegion)
        {
            // allowRoll 傳 false：作業區是正方形，轉相機只會讓「拼出來的圖形是正的嗎」難以判斷
            // 難度傳 0：沒有題目就沒有難度（見 LevelData.Difficulty）
            Data = new LevelData(null, ExactVector2.Zero, boxRegion, null, difficulty: 0, allowRoll: false);
        }

        /// <summary>離開這一關。之後 <see cref="HasLevel"/> 回 false。</summary>
        public void Unload() => Data = null;

        /// <summary>
        /// 這個題目的盒子原點。
        ///
        /// 目前一律是 <c>Zero</c>——用題目自己的座標，不平移（互動設計 §14.2）。
        /// 因為**置中是相機的事**，原點取什麼值玩家完全看不到，所以這不是功能問題，
        /// 是「除錯時哪個數字好讀」。選 <c>Zero</c> 的實質好處是
        /// <b>P2 畫參考解不用平移</b>。
        ///
        /// ⚠️ <b>必須保持無狀態。</b>靜態方法可以從任何地方呼叫，包括初始化之前；
        /// 一旦它開始讀某個物件的欄位，就會變成「有時候拿到空值」的隱形 bug。
        /// </summary>
        /// <param name="puzzle">題目。目前沒用到，留著是因為之後的規則可能依題目而定。</param>
        /// <returns>盒子原點。</returns>
        public static ExactVector2 BoxOriginFor(Puzzle puzzle) => ExactVector2.Zero;
    }
}
