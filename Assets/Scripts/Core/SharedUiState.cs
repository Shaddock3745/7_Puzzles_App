namespace Tangram.Core
{
    /// <summary>
    /// 右下角那一塊區域現在是什麼角色。
    ///
    /// <b>三態，不是兩個平行布林。</b>這三個是互斥的——用兩個布林表達的話，
    /// 會產生「箭頭與回收框同時顯示」這種本來不該存在的組合，而且不報錯
    /// （分層命中也是照同一條規則設計的）。
    /// </summary>
    public enum TrayCornerRole
    {
        /// <summary>箭頭：點了開／關抽屜。沒在拖、也沒過關的時候。</summary>
        Arrow,

        /// <summary>回收框：拖曳中，放手在上面就把片收回待用區。</summary>
        RecycleBox,

        /// <summary>什麼都不是：過關之後整個盤面鎖住，完成彈窗蓋滿畫面。</summary>
        Hidden
    }

    /// <summary>
    /// <b>跨元件的 UI 決定</b>（2026-09-09）。純 C#、可離線測。
    ///
    /// ⚠️ <b>它不是「所有 UI 狀態的家」。</b>名字容易讓人誤會，所以判準寫在這裡，
    /// 加東西之前照著問一遍：
    ///
    /// <list type="number">
    /// <item><b>模型層需要知道嗎？</b>要 → 放 <see cref="LevelData"/>
    /// （待用區、過關旗標、undo 堆疊都是這一類）</item>
    /// <item><b>是「每一片各一份」嗎？</b>是 → 放 <c>PieceView</c>
    /// （姿態、吸住、選取、可見）</item>
    /// <item><b>只有一個直接讀者嗎？</b>是 → 住在那個讀者身上
    /// （抓取偏移、跨門檻沒、選取哪一片，都只有控制器自己讀）</item>
    /// <item><b>讀者去跟寫入者拿會造成環嗎？</b><b>會 → 才放這裡。</b>
    /// 不會的話住寫入者身上，對外只開「規則的答案」</item>
    /// </list>
    ///
    /// <b>第四題是這個類別存在的唯一理由。</b>不是「它是 UI」就進來——
    /// 是「放在寫入者身上會成環」才進來。所以它會長得很慢，那是刻意的：
    /// 判準放寬成「全域的 UI 狀態都放進來」的話，Q3 那道防線就沒了，
    /// 三個月後這裡會裝滿彼此無關的布林，而且沒有人敢刪。
    ///
    /// <b>怎麼算「直接讀者」</b>：讀<b>原始值</b>自己判斷才算；
    /// 讀<b>規則的答案</b>不算。例如按鈕讀的是 <c>ActionTarget</c>（「現在能作用在哪一片」），
    /// 那是規則的答案，所以 <c>selectedPiece</c> 仍然只有控制器一個讀者。
    ///
    /// <b>登記在這裡的每個值都必須註明三件事</b>：是真相還是投影、唯一的寫入者是誰、
    /// 換關要不要重置。
    /// </summary>
    public sealed class SharedUiState
    {
        /// <summary>
        /// 全域存取點。由 <c>LevelController.Awake</c> 設定一次，
        /// 跟 <see cref="LevelSession.Current"/> 同一個模式。
        ///
        /// ⚠️ <b>刻意不用惰性單例</b>（第一次讀到才 new）。Unity 關掉 Domain Reload 之後
        /// static 欄位會跨越播放session 存活，惰性單例會把上一次播放的狀態帶進來，
        /// 而且完全不報錯。由 <c>Awake</c> 指定就沒有這個問題。
        ///
        /// 測試不會走這條路——它們自己 <c>new</c> 一個。
        /// </summary>
        public static SharedUiState Current { get; set; }

        /// <summary>
        /// 右下角那一塊現在是什麼角色。
        ///
        /// <b>投影，不是真相</b>：它算得出來（拖曳中沒、過關沒），
        /// 存在這裡純粹是為了<b>斷開 <c>TrayView</c> 對控制器的依賴</b>——
        /// 那兩個輸入都在控制器身上，而 <c>TrayView</c> 不該認識控制器。
        ///
        /// <b>唯一寫入者</b>：<c>TouchInputController</c>，每幀無條件叫
        /// <see cref="UpdateTrayCorner"/>。⚠️「變的時候才推」是會出事的那種——
        /// 漏推一次就永遠不同步而且不報錯，所以一定要無條件寫。
        ///
        /// <b>換關要不要重置</b>：⚠️ <b>不用</b>。每幀重寫的投影是自癒的——
        /// 換關之後下一幀就會依「沒在拖、沒過關」寫回 <see cref="TrayCornerRole.Arrow"/>。
        /// 需要重置的是<b>真相</b>類的值（例如抽屜開合，那個住在 <c>TrayView</c>，
        /// 由 <c>ResetPiecePoses</c> 一併關上）。
        /// </summary>
        public TrayCornerRole TrayCorner { get; private set; } = TrayCornerRole.Arrow;

        /// <summary>
        /// 重算右下角的角色。<b>規則住在這裡，不在控制器裡</b>——
        /// 放控制器的話這條規則就落在 <c>GameObject/</c>，<c>dotnet</c> 編不到、測不了，
        /// 而「顯示條件」正是最容易安靜出錯的東西。
        ///
        /// 控制器只餵輸入，不決定答案。
        /// </summary>
        /// <param name="isDragging">現在有沒有一片正在被拖（已經跨過移動門檻）。</param>
        /// <param name="isLevelComplete">這一關過關了沒。</param>
        public void UpdateTrayCorner(bool isDragging, bool isLevelComplete)
        {
            // ⚠️ 過關要排在最前面。理論上「過關」與「拖曳中」不可能同時成立
            // （過關是在放手那一格判定的），但這個順序讓「萬一同時成立」也只有一個答案，
            // 而不是看兩個 if 誰先跑。三態的價值就在這裡
            if (isLevelComplete) TrayCorner = TrayCornerRole.Hidden;
            else if (isDragging) TrayCorner = TrayCornerRole.RecycleBox;
            else TrayCorner = TrayCornerRole.Arrow;
        }
    }
}
