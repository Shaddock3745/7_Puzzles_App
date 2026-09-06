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

        /// <summary>這一關的盤面。undo 快照堆疊之後也會放在它裡面（P1-9）。</summary>
        public readonly BoardState Board;

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
        }

        /// <summary>用掉一次提示。</summary>
        public void UseHint() => HintsUsed++;

        /// <summary>標記過關。</summary>
        public void MarkSolved() => IsSolved = true;
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
