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

        /// <summary>提示用掉幾次。上限 3（互動設計 §9）。</summary>
        public int HintsUsed { get; private set; }

        /// <summary>過關了沒。</summary>
        public bool IsSolved { get; private set; }

        /// <param name="puzzle">這一關的題目。</param>
        /// <param name="boxOrigin">盒子原點。</param>
        /// <param name="boxRegion">盒子在盤面座標的區域。</param>
        /// <param name="referenceSolution">參考解，已平移到盤面座標。</param>
        /// <param name="allowRoll">允不允許 roll 相機。</param>
        public LevelData(Puzzle puzzle, ExactVector2 boxOrigin, Region boxRegion,
                         Placement[] referenceSolution, bool allowRoll)
        {
            Puzzle = puzzle;
            BoxOrigin = boxOrigin;
            BoxRegion = boxRegion;
            ReferenceSolution = referenceSolution;
            AllowRoll = allowRoll;
            Board = new BoardState();
            HintsUsed = 0;
            IsSolved = false;

            // 一關的開頭七片都在抽屜裡。換關是整包換掉（見類別說明），
            // 所以「換關要把七片送回待用區」不必另外寫一步，也就不可能忘記
            for (int slotIndex = 0; slotIndex < BoardState.PieceCount; slotIndex++)
                inTray[slotIndex] = true;
        }

        /// <summary>用掉一次提示。</summary>
        public void UseHint() => HintsUsed++;

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
        /// </summary>
        /// <returns>這一關過關了沒（呼叫之後的狀態）。</returns>
        public bool MarkSolvedIfAllOnBoard()
        {
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
        /// <param name="allowRoll">允不允許 roll 相機。精選題傳 false。</param>
        public void Load(Puzzle puzzle, IReadOnlyList<Placement> referenceSolution, bool allowRoll)
        {
            ExactVector2 boxOrigin = BoxOriginFor(puzzle);
            Region boxRegion = Region.FromPuzzle(puzzle, boxOrigin);

            // 參考解跟 walks 一樣要平移到盤面座標。目前 boxOrigin 是 Zero，所以這是 no-op——
            // 但寫在這裡，之後改了原點規則就不會忘記（忘了的話覆蓋框會整組偏移，而且不報錯）
            var translated = new Placement[referenceSolution.Count];
            for (int i = 0; i < referenceSolution.Count; i++)
                translated[i] = referenceSolution[i].Translated(boxOrigin);

            Data = new LevelData(puzzle, boxOrigin, boxRegion, translated, allowRoll);
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
