using System;
using System.Collections.Generic;
using Tangram.Core;
using Tangram.Data;
using UnityEngine;
using UnityEngine.InputSystem;

/// <summary>
/// 指標輸入（P1-6，互動設計 §4）。按下挑一片 → 每幀問吸附器 → 放手才交給盤面。
///
/// <b>一次按下有兩種結局</b>（§4「點選與拖曳的分界」，2026-09-07 加）：
/// 手指離按下點超過 <see cref="moveThresholdDp"/> 就是**拖曳**，沒超過就是**點選**。
/// <b>點選完全不碰盤面，也不碰位置</b>——P1-8 的「選一片起來轉」會掛在那條路上。
///
/// <b>刻意不實作 <c>IInputSource</c></b>（2026-09-04 定案）。那個介面的約定是
/// 「這個來源**維護**一個盤面」，但 P1-3 之後盤面的家是 <c>LevelSession.Data.Board</c>，
/// 觸控是**往別人的盤面寫**，不是維護自己的。硬套會讓介面語意變質，
/// 換來的只是多一個實作者而已。
///
/// <b>也沒有拆成「純邏輯 + Unity 驅動」</b>，跟 P1-1、P1-3 不一樣。
/// 因為這裡的數學早就被抽走了——候選、遲滯、盒內限制、重疊全在 <see cref="SnapSolver"/>
/// （純 C#、可離線測）。剩下的是讀指標、命中測試、把結果寫回 <c>PieceView</c>，
/// 全部是 Unity 相依，拆出去的那一半會是空的。
/// </summary>
[DisallowMultipleComponent]
public class TouchInputController : MonoBehaviour
{
    [Tooltip("座標換算與吸附參數的來源。留空的話 Awake 會自己找。")]
    [SerializeField] private BoardView boardView;

    [Tooltip("待用區抽屜。留空的話 Awake 會自己找；場景沒有它也能跑（就是不能從抽屜拿片）。")]
    [SerializeField] private TrayView trayView;

    [Tooltip("超過這個距離才算拖曳，沒超過就是點選。單位 dp。2026-09-09 實機調過。")]
    [SerializeField] private float moveThresholdDp = 4f;

    [Tooltip("落在這些矩形上的按下要整個忽略。旋轉鈕與翻面鈕會自己註冊，不用填；" +
             "這裡是留給之後其他 UI 的。")]
    [SerializeField] private RectTransform[] pressBlockingAreas;

    /// <summary>
    /// 實際在用的「不給抓片」矩形。Inspector 填的那些會在 <c>Awake</c> 併進來，
    /// 其餘由各自的 UI 在啟用時自己註冊。
    /// </summary>
    private readonly List<RectTransform> blockingAreas = new List<RectTransform>();

    /// <summary>
    /// 另一種「不給抓片」的矩形：<b>直接給螢幕座標</b>，不需要 <c>RectTransform</c>。
    ///
    /// 為了 <c>OnGUI</c> 畫的東西而加的（<c>DebugOverlay</c> 的摺疊把手，2026-09-12）——
    /// 那類東西沒有 <c>RectTransform</c>，但一樣不能讓按下穿過去抓到片。
    /// ⚠️ 收的是<b>函式</b>不是矩形：畫面尺寸一變矩形就換了，存值會過期而且不報錯。
    /// 座標系跟 <c>Pointer.position</c> 一致（<b>原點在左下</b>），不是 <c>OnGUI</c> 的左上。
    /// </summary>
    private readonly List<Func<Rect>> blockingScreenRects = new List<Func<Rect>>();

    /// <summary>手指底下那一片。<b>可能只是「按著」，還沒變成拖曳。</b></summary>
    private PieceView pressedPiece;

    /// <summary>
    /// 手指底下那一片，沒有就是 <c>null</c>。**唯讀，給除錯 overlay 用。**
    ///
    /// ⚠️ 它<b>不代表「正在拖」</b>——按下但還沒跨過門檻時也是它，
    /// 那時候要問 <see cref="IsDragging"/>。名字從 <c>DraggingPiece</c> 改過來就是為了這件事。
    ///
    /// 開這個屬性是因為<b>拖曳狀態正好是最難用眼睛看的東西</b>。
    /// 它只該被讀，不要變成別人改狀態的後門——要改請走
    /// <see cref="CancelInteraction"/> 或正常的指標流程。
    /// </summary>
    public PieceView PressedPiece => pressedPiece;

    /// <summary>這次按下已經跨過門檻、確定是拖曳了沒。**唯讀，給除錯 overlay 用。**</summary>
    public bool IsDragging => pressedPiece != null && movedPastThreshold;

    /// <summary>被選取（高亮）的那一片，沒有就是 <c>null</c>。</summary>
    private PieceView selectedPiece;

    /// <summary>
    /// 被選取（高亮）的那一片，沒有就是 <c>null</c>。**唯讀。**
    ///
    /// 旋轉／翻面鈕靠它決定<b>要不要顯示</b>（互動設計 §5：有選取 ＋ 那片沒在被拖）。
    /// 按鈕<b>不會</b>拿它去改姿態——轉的規則歸控制器，按鈕只負責戳一下（2026-09-07 定案）。
    ///
    /// 依 §4 的規則：<b>沒在被拖的時候</b>，它必定不在盤面上、必定沒吸住。
    /// ⚠️ 「沒在被拖的時候」這個但書不能省——拖曳期間盤面還留著它舊的位置，
    /// 要到 <see cref="CommitDrag"/> 才 <c>Lift</c>。
    /// </summary>
    public PieceView SelectedPiece => selectedPiece;

    /// <summary>
    /// 換選取的片，或傳 <c>null</c> 清掉。
    ///
    /// <b>「至多一片被選取」的不變式由這裡維護</b>，所以換選取只有這一個入口——
    /// 不要在別的地方直接寫 <see cref="PieceView.IsSelected"/>，
    /// 那樣會出現兩片同時高亮，而且沒有任何錯誤訊息。
    /// </summary>
    /// <param name="piece">要選取的片；<c>null</c> 表示清掉選取。</param>
    private void SetSelection(PieceView piece)
    {
        if (selectedPiece == piece) return;      // 省掉兩次沒必要的刷新

        // 換關重建片的話舊的參照會是「已銷毀」，而 Unity 的 == null 對它回 true，
        // 所以這裡不必額外防呆
        if (selectedPiece != null) selectedPiece.IsSelected = false;
        selectedPiece = piece;
        if (selectedPiece != null) selectedPiece.IsSelected = true;
    }

    /// <summary>
    /// 抽屜開著沒。<b>所有盤面上的按鈕在抽屜開著時一律退場</b>（2026-09-09）。
    ///
    /// <b>為什麼非做不可</b>：抽屜是整個螢幕寬 × 底部 22%，按鈕就在那個範圍裡。
    /// 而 <see cref="IsOverBlockingArea"/> 是 <see cref="BeginPress"/> 問的<b>第一層</b>，
    /// 排在抽屜前面——按鈕留在畫面上的話，它會把本來要打到縮圖的按下整個吃掉，
    /// 症狀是<b>抽屜裡有幾片點不動</b>（正好是被按鈕蓋住的那幾格）。
    ///
    /// 隱藏之後那塊就不擋了（<see cref="IsOverBlockingArea"/> 會跳過停用的物件），
    /// 按下自然往下落到抽屜那一層。
    ///
    /// ⚠️ 場景沒有 <c>TrayView</c> 時要回 <c>false</c>，不是 true——
    /// 沒有抽屜的話按鈕本來就不該被壓制。
    ///
    /// ⚠️ <b>2026-09-12 從 private 改成 public</b>（P2-13 的放棄鈕要它）。
    /// 不算破封裝：<c>TrayView.IsOpen</c> 本來就是公開的，這裡只是省掉每個按鈕自己去問抽屜。
    /// </summary>
    public bool IsTrayDrawerOpen => trayView != null && trayView.IsOpen;

    /// <summary>
    /// 這一關結束了沒（P1-10）。<b>過關之後盤面整個鎖住</b>——完成彈窗會蓋滿畫面，
    /// 玩家只剩「退出」與「下一關」兩個出口。
    ///
    /// 四個顯示條件（旋轉／翻面、undo、清空、抽屜箭頭）都要 AND 這一項。
    /// ⚠️ 但 <see cref="IsTrayDrawerOpen"/> <b>不能</b>套到箭頭上——
    /// 箭頭在抽屜開著時必須還按得到，不然抽屜關不起來。所以四個共用的只有這一項。
    ///
    /// 💡 漏掉其中一個不會造成功能性的錯：彈窗註冊了全螢幕擋片區，按下早就被吃掉了。
    /// 漏掉只會看到一顆按不動的鈕——但那正是 §5 要避免的「擺一個按不到的按鈕玩家會去戳」。
    /// </summary>
    public bool IsLevelComplete
    {
        get
        {
            LevelData level = LevelSession.Current?.Data;
            return level != null && level.IsSolved;
        }
    }

    /// <summary>玩家按了放棄沒（P2-13）。放棄彈窗靠它決定要不要顯示。</summary>
    public bool IsLevelAbandoned
    {
        get
        {
            LevelData level = LevelSession.Current?.Data;
            return level != null && level.IsAbandoned;
        }
    }

    /// <summary>
    /// 這一關<b>卡了多久</b>（前景秒數，含還在跑的那一段）。沒有關就是 0。
    ///
    /// 放棄鈕靠它決定要不要出現，之後三級提示鈕也是——⭐ <b>門檻幾秒、分幾級、誰來比，
    /// 全部是按鈕那邊的事</b>，這一層只回答「卡了多久」。之後怎麼調階梯都不用回頭改統計。
    ///
    /// ⚠️ 一定要用 <c>ForegroundSecondsAt</c> 而不是 <c>ForegroundSeconds</c>——
    /// 後者只在「停止」時才累加，整關玩下來一直是 0，問它等於什麼都問不到。
    /// </summary>
    public double StuckSeconds
    {
        get
        {
            LevelData level = LevelSession.Current?.Data;
            return level == null ? 0.0 : level.ForegroundSecondsAt(GameClock.NowSeconds);
        }
    }

    /// <summary>
    /// 放棄這一關（P2-13）。接在放棄鈕的 <c>onClick</c> 上。
    ///
    /// ⚠️ <b>盤面刻意不動</b>：片留在原地——讓玩家看著自己的殘局按「下一題」比空盤面合理，
    /// 而且換關時 <c>LevelData</c> 整包換掉本來就會重置。
    /// 放棄彈窗是滿版擋片區，所以這時候他動不了那些片。
    ///
    /// ⚠️ <b>扣分不在這裡</b>——這一層只記旗標，算輸與扣實力值是 P2-14 的事。
    /// </summary>
    public void AbandonLevel()
    {
        LevelData level = LevelSession.Current?.Data;
        if (level == null) return;

        // 已經過關的一局放棄不了。互斥由 LevelData 保證（見 MarkAbandoned），
        // 這裡照它的回傳值走就好
        if (!level.MarkAbandoned(GameClock.NowSeconds)) return;

        // 跟換關、版面重算、清空走同一條路：留著選取的話，
        // 彈窗蓋上去之後底下還高亮著一片
        SetSelection(null);
    }

    /// <summary>
    /// 按鈕現在能作用在哪一片，不能作用時是 <c>null</c>。
    ///
    /// 條件是互動設計 §5 的兩條加上 P1-5 的一條：
    /// <b>有選取</b> ＋ <b>那片沒在被拖</b> ＋ <b>抽屜沒開著</b>。
    /// 按鈕拿它決定要不要顯示，這裡也拿它當保險——
    /// <b>顯示條件與作用條件是同一個算式，不會分岔</b>。
    ///
    /// ⚠️ 第三條在今天<b>其實是多餘的</b>：打開抽屜的當下會清掉選取（決定 17，
    /// 做在 <see cref="BeginPress"/> 的箭頭分支裡），所以抽屜開著時本來就沒有選取。
    /// 寫出來是因為那條不變式<b>由另一段程式維護</b>——之後多一條打開抽屜的路徑
    ///（快捷鍵、換關預設開著）而忘了清選取的話，按鈕就會浮在抽屜上面，
    /// 然後把底下那幾格縮圖吃掉。顯示條件不該依賴遠處的某條規則還活著。
    /// </summary>
    public PieceView ActionTarget
        => selectedPiece != null && !selectedPiece.IsBeingDragged
           && !IsTrayDrawerOpen && !IsLevelComplete
            ? selectedPiece
            : null;

    /// <summary>
    /// 把選取的那一片<b>逆時針</b>轉一格（互動設計 §5）。模型的旋轉方向就是逆時針。
    /// 順時針那顆鈕走 <see cref="RotateSelectedClockwise"/>，兩顆共用 <see cref="RotateSelectedBy"/>。
    /// </summary>
    public void RotateSelected() => RotateSelectedBy(1, "RotateSelected");

    /// <summary>
    /// 把選取的那一片<b>順時針</b>轉一格（2026-09-13，排在收回饋資料之前）。
    ///
    /// <b>為什麼要有</b>：只有一個方向時，轉到想要的角度最壞要按 7 下，兩個方向最壞 4 下。
    /// ⚠️ 趕在收回饋資料之前做，是因為「一直按旋轉很煩」會被受試者算進「這題很難」——
    /// 需要大量旋轉的題目會被系統性高估，而那不是難度權重該量到的東西。
    ///
    /// ⚠️ 翻面過的平行四邊形也一樣是順時針：模型是「先翻面、再旋轉」，旋轉永遠作用在世界座標上，
    /// 所以格數 −1 在畫面上永遠是順時針，翻不翻面不會反過來。
    /// </summary>
    public void RotateSelectedClockwise() => RotateSelectedBy(-1, "RotateSelectedClockwise");

    /// <summary>
    /// 把選取的那一片轉 <paramref name="steps"/> 格。<b>轉的規則只有這一份</b>，兩顆旋轉鈕都叫這裡。
    ///
    /// <b>轉的規則住在這裡，不在按鈕上。</b>按鈕只是眾多觸發器裡的一個——
    /// 之後的鍵盤快捷鍵、手勢、提示想觸發旋轉都叫這裡，規則永遠只有這一份。
    ///
    /// <b>只改姿態</b>：不重新嘗試吸附，也不碰盤面（§5）。
    /// 進盤面永遠只發生在放手那一刻，不然轉一格就被吸走、高亮清掉，按鈕會在連轉中途消失。
    ///
    /// 軸心補償不用管——那做在 <see cref="PieceView.Rotation"/> 的設定值那一層，
    /// 而且作用對象必定沒吸住，補償一定會生效。
    /// </summary>
    /// <param name="steps">正數逆時針、負數順時針。</param>
    /// <param name="actionName">呼叫者的名字，只用在「沒有作用對象」的警告裡。</param>
    private void RotateSelectedBy(int steps, string actionName)
    {
        PieceView piece = RequireActionTarget(actionName);
        if (piece == null) return;

        // ⚠️ 一定要照片種的循環長度取餘數，不是固定 8，而且負數要繞回來——
        // **同一個外觀不要有兩個編號**：正方形 rot 0 與 rot 2 佔位一樣但 Placement.Equals 不相等，
        // 之後 undo 與解的比對會踩到。規則住在 PiecePrototype.RotateWithinCycle（離線測得到）
        piece.Rotation = PiecePrototype.RotateWithinCycle(piece.Kind, piece.Rotation, steps);
    }

    /// <summary>
    /// 把選取的那一片<b>照現在的樣子左右鏡射</b>（互動設計 §5；2026-09-14 改成對每一片都有效）。
    ///
    /// 鏡射之後落在哪個姿態由 <see cref="PiecePrototype.MirroredLeftRight"/> 決定——三角形會變成「沒翻面、轉另一格」，
    /// 平行四邊形跟以前一樣是「翻面 ＋ 旋轉取負」。<b>鏡射完跟現在一樣就什麼都不做</b>
    /// （正方形永遠是、三角形 8 格裡有 2 格）；按鈕在那些時候本來就是灰的，這裡再擋一次。
    ///
    /// <b>為什麼規則放在按鈕動作這一層，而不是 <see cref="PieceView.IsFlipped"/> 的設定值裡</b>（2026-09-07 就這樣定）：
    /// 這是<b>翻轉鈕的手感規則</b>，不是翻面本身的定義。
    /// 拖曳的還原、換關重設那些路徑要能單獨改翻面而不動旋轉，做進 setter 的話它們會被順便改掉，而且不報錯。
    /// </summary>
    public void FlipSelected()
    {
        PieceView piece = RequireActionTarget("FlipSelected");
        if (piece == null) return;
        if (PiecePrototype.IsMirrorSymmetric(piece.Kind, piece.Rotation, piece.IsFlipped)) return;

        PiecePose mirrored = PiecePrototype.MirroredLeftRight(piece.Kind, piece.Rotation, piece.IsFlipped);

        // 兩個 setter 各自都會把視覺中心留在原地，所以先後順序不影響結果。
        // 三角形的 IsFlipped 本來就是 false、結果也是 false，那個 setter 等於沒動
        piece.IsFlipped = mirrored.IsFlipped;
        piece.Rotation = mirrored.Rotation;
    }

    /// <summary>
    /// 取出按鈕的作用對象，沒有的話**大聲抱怨**。
    ///
    /// 按鈕在沒有作用對象時本來就該是隱藏的，所以「被按到卻沒有對象」表示接線出了問題。
    /// <b>這條路原本是安靜的</b>——按鈕會有按下的動畫、事件也會送出，但什麼都不會發生，
    /// 從畫面上完全看不出來哪裡壞了。訊息直接把最可能的原因寫出來。
    /// </summary>
    /// <param name="actionName">呼叫者的名字，只用在訊息裡。</param>
    /// <returns>作用對象；沒有時回 null 並印一則警告。</returns>
    private PieceView RequireActionTarget(string actionName)
    {
        PieceView piece = ActionTarget;
        if (piece == null)
            Debug.LogWarning(
                $"{actionName} 被呼叫，但現在沒有作用對象（沒有選取，或選取的片正在被拖）。\n" +
                "按鈕在那種時候本來就該是隱藏的，所以最可能的原因是：" +
                "**按鈕的按下被當成「按在空白處」，先把選取清掉了**。\n" +
                "檢查那顆按鈕的矩形有沒有進 blockingAreas——" +
                "PieceActionButtons 會自己註冊，其他 UI 要自己叫 RegisterPressBlockingArea。", this);
        return piece;
    }

    /// <summary>
    /// 按下的瞬間「片的原點」與「手指」的差。
    ///
    /// ⚠️ <b>沒有它片會在按下的瞬間跳到手指底下。</b>
    /// <see cref="SnapSolver.Resolve"/> 收的 <c>freePosition</c> 是「片的原點該在哪」，
    /// 不是手指在哪——sprite 的 pivot 就在原型原點，所以兩者差一段。
    /// </summary>
    private Vector2 grabOffset;

    /// <summary>
    /// 按下當下的**螢幕座標**，門檻就是拿它比的。
    ///
    /// ⚠️ <b>是螢幕像素，不是世界單位。</b>手指抖動是螢幕現象，跟相機縮放無關；
    /// 而且這樣完全不必經過 <see cref="BoardView.ScreenToWorld"/>，
    /// 也就不會被版面重算換掉係數影響。
    /// </summary>
    private Vector2 pressScreenPosition;

    /// <summary>
    /// 這次按下有沒有跨過門檻。
    ///
    /// ⚠️ <b>一旦打開就不再關回去</b>（同一次按下之內）。
    /// 手指繞一圈回到按下點附近才放開，那仍然是拖曳；少了這條，
    /// 放手瞬間會被誤判成點選，片停在半路而盤面沒更新。
    /// </summary>
    private bool movedPastThreshold;

    private SnapSolver.DragResult lastResult;

    /// <summary>
    /// 按下之前那一片在哪。取消互動要靠它還原，
    /// 而它<b>順帶記住了「這次按下是從抽屜來的」</b>——所以不需要另一個旗標。
    /// </summary>
    private enum PieceLocation
    {
        /// <summary>在待用區（抽屜裡，看不到也點不到）。</summary>
        Tray,

        /// <summary>暫放在盤面上：看得到、沒吸住、不在 <c>BoardState</c> 裡。</summary>
        Loose,

        /// <summary>在盤面上。</summary>
        OnBoard
    }

    // 取消互動時要還原到哪。位置與「吸住了沒」只有 Loose / OnBoard 時才有意義
    private PieceLocation locationBeforeDrag;
    private Vector2 positionBeforeDrag;
    private bool snappedBeforeDrag;

    private readonly List<Collider2D> hits = new List<Collider2D>();
    private ContactFilter2D hitFilter;

    private void Awake()
    {
        if (boardView == null) boardView = FindFirstObjectByType<BoardView>();
        if (boardView == null)
            Debug.LogError("TouchInputController 找不到 BoardView：拖曳完全不會有反應。" +
                           "請在場景裡建一個掛 BoardView 的物件，或直接拖到這支的欄位上。", this);

        // 這個是選用的，找不到不報錯——沒有抽屜的場景照樣能拖盤面上的片
        if (trayView == null) trayView = FindFirstObjectByType<TrayView>();

        // 用靜態屬性，不要用同名的 NoFilter() 方法——那支在 Unity 6 已經棄用
        hitFilter = ContactFilter2D.noFilter;

        if (pressBlockingAreas != null)
            foreach (RectTransform area in pressBlockingAreas) RegisterPressBlockingArea(area);
    }

    /// <summary>
    /// 登記一塊「按在這裡就不要抓片」的矩形。UI 在自己啟用時叫這裡。
    ///
    /// <b>為什麼要能自己註冊，而不是只讓人在 Inspector 上填</b>（2026-09-07）：
    /// 沒填的話症狀是**按鈕完全沒反應**，而且不報錯——
    /// 按鈕的按下會先被當成「按在空白處」把選取清掉，等 <c>onClick</c> 送到時
    /// 已經沒有作用對象了。這種「要記得填才會動」的東西，
    /// 這專案的作法是做進程式讓它自己成立。
    /// </summary>
    /// <param name="area">要擋的矩形。null 或重複登記都會被忽略。</param>
    public void RegisterPressBlockingArea(RectTransform area)
    {
        if (area == null || blockingAreas.Contains(area)) return;
        blockingAreas.Add(area);
    }

    /// <param name="area">要取消登記的矩形。null 或不在清單裡都安全。</param>
    public void UnregisterPressBlockingArea(RectTransform area)
    {
        if (area != null) blockingAreas.Remove(area);
    }

    /// <summary>
    /// 登記一塊用<b>螢幕座標</b>描述的擋片區，給沒有 <c>RectTransform</c> 的 UI 用
    /// （<c>OnGUI</c> 畫的東西）。說明見 <see cref="blockingScreenRects"/>。
    /// </summary>
    /// <param name="screenRect">
    /// 回傳當下矩形的函式，原點左下。傳函式是因為畫面尺寸一變矩形就換了。
    /// null 或重複登記都會被忽略。
    /// </param>
    public void RegisterPressBlockingScreenRect(Func<Rect> screenRect)
    {
        if (screenRect == null || blockingScreenRects.Contains(screenRect)) return;
        blockingScreenRects.Add(screenRect);
    }

    /// <param name="screenRect">要取消登記的那個函式。null 或不在清單裡都安全。</param>
    public void UnregisterPressBlockingScreenRect(Func<Rect> screenRect)
    {
        if (screenRect != null) blockingScreenRects.Remove(screenRect);
    }

    private void OnEnable()
    {
        // P1-1 刻意留的洞就是這裡補上的：版面重算會重排暫放區的片，
        // 手指底下那片會瞬間移位、抓取偏移歪掉，所以直接取消這次拖曳
        if (boardView != null) boardView.LayoutRecomputed += CancelInteraction;
    }

    private void OnDisable()
    {
        if (boardView != null) boardView.LayoutRecomputed -= CancelInteraction;
    }

    private void Update()
    {
        if (boardView == null) return;

        // ⚠️ 讀座標之前一定要先叫這一句。MonoBehaviour 之間的 Update 順序是未定義的，
        // 不叫的話「螢幕剛變、BoardView 這幀還沒輪到」那一幀會拿到舊的解
        boardView.EnsureCurrent();
        if (!boardView.Solution.IsValid) return;

        LevelData level = LevelSession.Current?.Data;
        if (level == null) return;

        // ⚠️ 這一句要排在任何命中測試**之前**，理由跟上面的 EnsureCurrent 完全一樣：
        // 同一格裡先寫後讀才拿得到新值。BeginPress 問 TrayView.HitTest 時，
        // TrayView 讀的就是這裡剛寫進去的角色——排在後面的話它會拿到上一幀的答案，
        // 而那正是這個專案為了 EventSystem 晚一幀吃過虧的同一類問題。
        //
        // **每幀無條件寫**，不是「變的時候才推」。漏推一次就永遠不同步而且不報錯
        SharedUiState.Current?.UpdateTrayCorner(IsDragging, IsLevelComplete);

        if (pressedPiece == null)
        {
            // 只有「這一幀剛落下」的指標才會被鎖定（待辦清單決定 12）。
            // 換關那一瞬間還按著的手指因此自動不合格，不需要另外維護排除清單
            if (TryAcquirePointer(out Vector2 pressPoint) && !BeginPress(level, pressPoint))
                activePointerId = NoPointerId;   // 這一下沒有變成互動（按鈕、抽屜、空白）
            return;
        }

        PointerState state = PollPointer(out Vector2 screenPoint);

        switch (state)
        {
            case PointerState.Held:
                UpdatePress(level, boardView.ScreenToWorld(screenPoint), screenPoint);
                return;

            case PointerState.Ended:
                EndPress(level, screenPoint);
                return;

            default:
                // 被系統取消（來電、下拉通知）或那根手指整個不見了。
                // ⚠️ 走還原，**不是結算**（待辦清單決定 13）——玩家並沒有真的放手，
                // 結算的話他回到遊戲會發現片自己被放進盒子了
                CancelInteraction();
                return;
        }
    }

    // ─────────────────────────────────────────────────────────────
    // 指標：滑鼠與觸控統一成一層（待辦清單決定 14）
    //
    // ⚠️ 不能只讀 Pointer.current。它在手機上反映的是**主要觸控**（第一根手指），
    // 第二根之後完全看不見——症狀是「手掌先碰到螢幕，真正要用的手指就完全沒反應」，
    // 而且在 Editor 用滑鼠永遠測不到。
    //
    // 作法是**按下時記住是哪一根，之後只聽那一根**。這不是做多點觸控，
    // 是「無視其他手指」的正確實作：只有一根手指時行為跟以前完全相同。
    // ─────────────────────────────────────────────────────────────

    /// <summary>滑鼠／觸控筆用的假 id。真的觸控 id 不會是負的。</summary>
    private const int MousePointerId = -1;

    /// <summary>沒有鎖定任何指標。</summary>
    private const int NoPointerId = int.MinValue;

    /// <summary>這次互動鎖定的指標。<see cref="NoPointerId"/> 表示現在沒有。</summary>
    private int activePointerId = NoPointerId;

    /// <summary>鎖定的那根手指這一幀的狀態。</summary>
    private enum PointerState
    {
        /// <summary>還按著。</summary>
        Held,

        /// <summary>正常放開 —— 要結算。</summary>
        Ended,

        /// <summary>被系統取消 —— 要還原。</summary>
        Canceled,

        /// <summary>整根不見了（裝置拔掉、觸控紀錄被回收）—— 當成取消處理比較安全。</summary>
        Lost
    }

    /// <summary>
    /// 找一根<b>這一幀剛落下</b>的指標並鎖定它。
    /// </summary>
    /// <param name="screenPoint">鎖定成功時是它的螢幕座標。</param>
    /// <returns>有沒有鎖定到。</returns>
    private bool TryAcquirePointer(out Vector2 screenPoint)
    {
        screenPoint = Vector2.zero;

        Touchscreen touchscreen = Touchscreen.current;
        if (touchscreen != null)
        {
            foreach (var touch in touchscreen.touches)
            {
                // ⚠️ 要完整寫出命名空間：UnityEngine.TouchPhase（舊版 Input）同名，
                // 只寫 TouchPhase 會編不過
                if (touch.phase.ReadValue() != UnityEngine.InputSystem.TouchPhase.Began) continue;

                activePointerId = touch.touchId.ReadValue();
                screenPoint = touch.position.ReadValue();
                return true;
            }
        }

        // 桌機（或編輯器）的滑鼠。手機上 Pointer.current 就是 Touchscreen 本身，
        // 那條已經在上面處理過了，再走一次會變成同一根手指被鎖兩次
        Pointer pointer = Pointer.current;
        if (pointer == null || pointer is Touchscreen) return false;
        if (!pointer.press.wasPressedThisFrame) return false;

        activePointerId = MousePointerId;
        screenPoint = pointer.position.ReadValue();
        return true;
    }

    /// <summary>讀鎖定的那根指標這一幀怎麼樣了。</summary>
    /// <param name="screenPoint">它的螢幕座標；讀不到時是零。</param>
    /// <returns>這一幀的狀態。</returns>
    private PointerState PollPointer(out Vector2 screenPoint)
    {
        screenPoint = Vector2.zero;

        if (activePointerId == MousePointerId)
        {
            Pointer pointer = Pointer.current;
            if (pointer == null) return PointerState.Lost;

            screenPoint = pointer.position.ReadValue();

            // 沒收到放開事件也要收尾（例如視窗失焦）。滑鼠沒有「被系統取消」這種狀態，
            // 所以這裡維持原本的行為：當成正常放開
            if (pointer.press.wasReleasedThisFrame || !pointer.press.isPressed)
                return PointerState.Ended;
            return PointerState.Held;
        }

        Touchscreen touchscreen = Touchscreen.current;
        if (touchscreen == null) return PointerState.Lost;

        foreach (var touch in touchscreen.touches)
        {
            if (touch.touchId.ReadValue() != activePointerId) continue;

            screenPoint = touch.position.ReadValue();
            UnityEngine.InputSystem.TouchPhase phase = touch.phase.ReadValue();

            if (phase == UnityEngine.InputSystem.TouchPhase.Canceled) return PointerState.Canceled;
            if (phase == UnityEngine.InputSystem.TouchPhase.Ended) return PointerState.Ended;

            // 收不到 Ended 也要收尾。用 press 而不是只看 phase，
            // 是因為 phase 在某些平台上結束後會先變成別的值
            if (!touch.press.isPressed) return PointerState.Ended;
            return PointerState.Held;
        }
        return PointerState.Lost;
    }

    /// <summary>
    /// 按下：先問「打到什麼」，再照類別分派（待辦清單〈分類與偏移的定案〉）。
    ///
    /// <b>分層命中測試取代了原本的「先擋 → 抓片」。</b>舊的擋法把兩件事綁成一件——
    /// 「不要做命中測試」與「不要開始拖曳」。按鈕要兩者都成立，
    /// 但<b>抽屜的縮圖要前者成立、後者不成立</b>（點下去要能直接拖出來），
    /// 舊機制表達不了這個區別，會卡成死結：按下被吃掉 → 沒有拖曳 →
    /// 抽屜不會自動收折 → 手指還在擋片區裡 → 再按一次又被吃掉。
    ///
    /// 問各層的順序是<b>畫在上面的先問</b>：螢幕座標那幾層 → 世界座標那層 → 空白。
    ///
    /// <b>命中結果只活這一幀</b>，不進跨幀狀態——分派完成之後，
    /// 從盤面抓的跟從抽屜召喚的後續完全一樣（門檻、吸附、放手、取消都共用）。
    /// </summary>
    /// <param name="level">這一關的資料。</param>
    /// <param name="screenPoint">按下的螢幕座標。</param>
    /// <returns>這一下有沒有變成一次互動（有的話 <see cref="pressedPiece"/> 就有值了）。</returns>
    private bool BeginPress(LevelData level, Vector2 screenPoint)
    {
        // 第一層：註冊過的 UI 矩形（旋轉鈕、翻面鈕）。整個不理——
        // 不進來就不會跑下面「按在空白處清選取」那一段，
        // 所以按旋轉鈕不會把自己的作用對象清掉
        if (IsOverBlockingArea(screenPoint)) return false;

        // 第二層：抽屜
        if (trayView != null)
        {
            switch (trayView.HitTest(screenPoint, out int traySlotIndex))
            {
                case TrayView.Hit.Arrow:
                    // ⚠️ 清選取搬到這裡了（2026-09-09）。原本是 TrayView.Toggle 自己叫
                    // 控制器的 ClearSelection——那是抽屜認識控制器的三條邊之一。
                    // 決定要開抽屜的本來就是這裡，順手把選取清掉才是對的歸屬。
                    //
                    // 為什麼開抽屜要清選取（決定 17）：抽屜整個蓋住盤面，
                    // 留著選取的話旋轉／翻面鈕會被蓋在底下，變成「看得到但按不到」
                    trayView.Toggle();
                    if (trayView.IsOpen) SetSelection(null);
                    return false;

                // ⚠️ 抽屜本體（含縮圖之間的空隙）要**明確地什麼都不做**，
                // 不能讓它掉到下面的「空白」去——否則玩家在抽屜裡點歪一下，
                // 盤面上選取的那片就莫名其妙不亮了
                case TrayView.Hit.DrawerBody:
                    return false;

                case TrayView.Hit.Thumbnail:
                    return BeginPressOnPiece(level, trayView.PieceAt(traySlotIndex),
                                             screenPoint, worldPoint: Vector2.zero, fromTray: true);
            }
        }

        // 第三層：盤面上的片
        Vector2 pieceWorldPoint = boardView.ScreenToWorld(screenPoint);
        PieceView piece = TopmostPieceAt(pieceWorldPoint);
        if (piece != null)
            return BeginPressOnPiece(level, piece, screenPoint, pieceWorldPoint, fromTray: false);

        // 都沒中 = 按在空白處 → 清掉選取。
        //
        // §4 的規則寫「放手時算」，但空白這一條**當場清是等價的**：
        // 按下與放開之間沒有任何跟選取有關的畫面，玩家分不出差別。
        // 換來的是省掉「這次按下有沒有抓到片」那個狀態
        SetSelection(null);
        return false;
    }

    /// <summary>
    /// 記下一次互動的狀態。<b>這裡還不知道是點選還是拖曳</b>，所以什麼都不動——
    /// 連 <c>IsBeingDragged</c> 都還不打開（打開的話點一下就會看到那片閃到最上層一下）。
    ///
    /// <b>從抽屜來的片這時候還隱藏著</b>，要到跨過門檻才召喚（決定 18）。
    /// 所以它的抓取偏移也要等到那時候才算得出來——在那之前它根本沒有位置。
    /// 規則是「<b>片有位置的那一刻就算偏移</b>」，兩條路只是時刻不同，公式只有一份。
    /// </summary>
    /// <param name="level">這一關的資料。</param>
    /// <param name="piece">這次要操作的片。null 表示分類器給了無效的槽，直接放棄。</param>
    /// <param name="screenPoint">按下的螢幕座標，門檻要用。</param>
    /// <param name="worldPoint">按下的世界座標。<paramref name="fromTray"/> 為 true 時不會被用到。</param>
    /// <param name="fromTray">是不是從抽屜的縮圖按下的。</param>
    /// <returns>有沒有成功開始一次互動。</returns>
    private bool BeginPressOnPiece(LevelData level, PieceView piece,
                                   Vector2 screenPoint, Vector2 worldPoint, bool fromTray)
    {
        if (piece == null) return false;

        pressedPiece = piece;
        pressScreenPosition = screenPoint;
        movedPastThreshold = false;
        positionBeforeDrag = piece.Position;

        // 局內統計（P2-13）。⚠️ **按下就開始計，不看移動門檻**——點選也算，
        // 這樣規則只有一條。每次約 0.1 秒，怎麼算都不影響大局，但規則得寫死才測得了
        level.StartDragTiming(GameClock.NowSeconds);

        if (fromTray)
        {
            locationBeforeDrag = PieceLocation.Tray;
            snappedBeforeDrag = false;
            grabOffset = Vector2.zero;      // 召喚時才算得出來
        }
        else
        {
            locationBeforeDrag = level.Board[piece.SlotIndex].OnBoard
                ? PieceLocation.OnBoard
                : PieceLocation.Loose;
            snappedBeforeDrag = piece.IsSnapped;
            grabOffset = piece.Position - worldPoint;
        }

        // 保險：跨過門檻那一幀 UpdatePress 一定會覆蓋它，所以這行其實用不到。
        // 留著是為了「Snapped = true 但 Placement 沒填」在任何時刻都不可能成立
        lastResult = SnapSolver.DragResult.Free(piece.Position);
        return true;
    }

    /// <summary>
    /// 把片從抽屜裡拿出來，放到手指底下。<b>只有跨過門檻那一幀會叫一次。</b>
    ///
    /// <b>為什麼延到跨門檻</b>（決定 18）：按下就召喚的話，玩家點一下不動就放開，
    /// 片會留在畫面上——那破壞了「<b>門檻內畫面完全靜止</b>」那條規則，
    /// 而那條是有代價換來的（它同時修掉了「點一下把片從盤面拿掉」那個 bug）。
    /// 代價只是按下到片出現有門檻那 10 像素的空窗，而手指本來就在動。
    /// </summary>
    /// <param name="level">這一關的資料。</param>
    /// <param name="worldPoint">手指的世界座標。</param>
    private void SummonFromTray(LevelData level, Vector2 worldPoint)
    {
        level.SetInTray(pressedPiece.SlotIndex, false);
        pressedPiece.IsVisible = true;
        pressedPiece.IsSnapped = false;

        // 把**視覺中心**對到手指。sprite 的 pivot 在原型原點（某個頂點）不是中心，
        // 所以要補一段——而那個補償量跟姿態有關，共用 PieceView 那一份算法
        pressedPiece.Position = worldPoint - pressedPiece.PosedCenterOffset;

        // 片有位置了，偏移就算得出來。從這一行之後，這條路跟「從盤面抓起來」
        // 完全一樣，每幀的更新、放手、取消都不必分辨來源
        grabOffset = pressedPiece.Position - worldPoint;
        positionBeforeDrag = pressedPiece.Position;
    }

    /// <summary>
    /// 按著的每一幀。門檻內什麼都不做；跨過之後才問吸附器、把結果寫回片。<b>不碰盤面。</b>
    /// </summary>
    /// <param name="level">這一關的資料。</param>
    /// <param name="worldPoint">手指的世界座標。</param>
    /// <param name="screenPoint">手指的螢幕座標，門檻要用。</param>
    private void UpdatePress(LevelData level, Vector2 worldPoint, Vector2 screenPoint)
    {
        if (!movedPastThreshold)
        {
            float threshold = Mathf.Max(ScreenMetrics.ToPixels(moveThresholdDp), 0f);
            if ((screenPoint - pressScreenPosition).sqrMagnitude < threshold * threshold)
            {
                // 還在門檻內：**片一動也不動**。這是刻意的——
                // 判定成點選時就完全沒有東西要還原（§4）
                return;
            }

            movedPastThreshold = true;

            // 從抽屜來的片到這一刻才真的被拿出來（決定 18）。
            // ⚠️ 一定要排在下面那些之前——片還隱藏著的時候，
            // 打開 IsBeingDragged 或選取它都是在對一片看不見的東西動手
            if (locationBeforeDrag == PieceLocation.Tray) SummonFromTray(level, worldPoint);

            // 抽屜自動收折（決定 19）。⚠️ **這一步 2026-09-09 才搬回這裡**——
            // 原本是 TrayView.Update 每幀看到「有在拖」就 Close()，也就是把一個
            // **一次性動作**寫成了「每幀重複執行的冪等操作」，而且那是抽屜認識控制器的
            // 三條邊之一。互動設計 §15.4 一直寫著「跨過門檻那一刻同時發生五件事」，
            // 搬回來之後文件跟程式才真的對得上
            if (trayView != null) trayView.Close();

            pressedPiece.IsBeingDragged = true;

            // 一開始移動就亮，不等放手（2026-09-07）。
            //
            // ⚠️ <b>這會讓「被選取的片不在盤面上」在拖曳期間暫時不成立</b>：
            // 拿起一片已放置的片開始拖，盤面要到 CommitDrag 才把它 Lift 掉。
            // 不變式因此收斂成「**沒在被拖的時候**，被選取的片必定不在盤面上、必定沒吸住」，
            // 而那樣就夠了——按鈕的顯示條件本來就排除拖曳中（§5），
            // 所以按鈕能被按到的每一刻，不變式都成立
            SetSelection(pressedPiece);

            // ⚠️ 抓取偏移**不**在這裡重算，所以這一幀片會往前補上門檻那一段距離
            // （10 螢幕像素，手機上約 0.5 mm）。重算的話沒有這個跳動，
            // 但片會永遠落後抓取點那一段。兩種都是手感，留到 P1-13 玩過再定
        }

        Vector2 freePosition = worldPoint + grabOffset;

        lastResult = SnapSolver.Resolve(
            pressedPiece.Kind, pressedPiece.Rotation, pressedPiece.IsFlipped, freePosition,
            // ⚠️ 一定要排除自己。拖曳中的片如果還留在清單裡，
            // 每個候選都會跟自己重疊，結果是「永遠吸不上」而且完全不報錯
            level.Board.OnBoardPlacementsExcept(pressedPiece.SlotIndex),
            boardView.SnapSettings,
            currentlySnapped: pressedPiece.IsSnapped,
            extraAnchors: level.BoxRegion.BoundaryNodes,
            isAllowed: placement => level.BoxRegion.Contains(placement));

        if (lastResult.Snapped)
        {
            pressedPiece.SnapTo(lastResult.Placement.Translation);
        }
        else
        {
            pressedPiece.Position = lastResult.FreePosition;
            pressedPiece.IsSnapped = false;
        }
    }

    /// <summary>
    /// 放手。分成兩條路：
    ///
    /// <list type="bullet">
    /// <item><b>點選</b>（沒跨過門檻）——完全不碰盤面，也不碰位置</item>
    /// <item><b>拖曳</b>——<see cref="CommitDrag"/>，<b>這裡才動盤面</b>（§4）</item>
    /// </list>
    ///
    /// ⚠️ <b>這個分岔就是 2026-09-07 那個 bug 的修法。</b>
    /// 在它之前，快速點一下已放置的片會走進「沒吸住 + 本來在盤上」那條去 <c>Lift</c>，
    /// 而畫面完全不變（片還是不透明、還是停在原地），只有 <c>BoardState</c> 少一片，不報錯。
    /// 而且時好時壞——按住幾幀再放，吸附跑過通常會重新吸回同一格就沒事。
    /// </summary>
    /// <param name="level">這一關的資料。</param>
    /// <param name="screenPoint">放手時的螢幕座標。回收框的判定要用它。</param>
    private void EndPress(LevelData level, Vector2 screenPoint)
    {
        // 局內統計（P2-13）：⚠️ **一定要排在過關判定之前**，
        // 這樣最後那一段拖曳才算得進去。
        // 💡 這裡與 CancelInteraction 是拖曳的兩個出口，兩邊都要叫——
        // 冪等規則②（沒在計時就什麼都不做）讓「兩邊都叫」變成安全的
        level.StopDragTiming(GameClock.NowSeconds);

        if (movedPastThreshold)
        {
            // 回收**優先於吸附**（決定 22）：兩者同時成立時，
            // 玩家把手指移進框裡的意圖比「這裡剛好是合法位置」明確得多。
            // 判定用**手指**的位置不是片的，這樣「看到的框」跟「判定的區域」是同一個
            if (trayView != null && trayView.IsOverRecycleArea(screenPoint)) RecycleDrag(level);
            else CommitDrag(level);

            pressedPiece.IsBeingDragged = false;

            // 過關判定（P1-10）。**鬆手這一格是唯一需要問的時機**：
            // CommitDrag 是全專案唯一會讓片進盤面的地方，而 undo 與回收只會讓片變少
            // 或原地不動——所以「還沒過關 → 過關」這個轉換只可能發生在這裡。
            // 不必每幀檢查，也不必訂閱 Board.Changed。
            //
            // ⚠️ 一定要在 movedPastThreshold 這個區塊**裡面**：點選那條完全不碰盤面，
            // 在外面問等於每次點一下都白算一次
            if (level.MarkSolvedIfAllOnBoard())
            {
                // 過關了，前景也停（拖曳上面已經停過，冪等規則讓這裡再叫一次無害）。
                // ⚠️ 一定要停：不停的話這一局的前景時間會一路算到玩家按「下一關」為止，
                // 而那段時間他只是在看彈窗——ELO 會把「慢慢欣賞」算成「解得很慢」
                level.StopAllTiming(GameClock.NowSeconds);
            }
        }

        // §4 的高亮規則，加上 P1-5 之後收成一句：
        // **片要看得到、而且不在盤上，才可能被選取。**
        //
        // ⚠️ 「在待用區」這個條件不能省。從抽屜按下但沒跨過門檻時，
        // pressedPiece 已經指向那一片，但**它還隱藏著**——少了這條就會高亮一片
        // 看不見的片，旋轉／翻面鈕跟著跳出來，玩家一按就轉了一片看不到的東西，
        // 而且完全不報錯。回收之後那片也在待用區，同一條規則一起涵蓋了。
        //
        // ⚠️ 一定要在 CommitDrag / RecycleDrag **之後**才讀——那兩步才剛把狀態寫進去。
        // 反過來的話「拖進盒子放好」會讀到舊值、把剛放好的片留成高亮，
        // 而 §5 的補償守衛假設高亮的片沒吸住，接著按旋轉鈕就會看到片飛出去
        int slotIndex = pressedPiece.SlotIndex;
        bool stillHidden = level.IsInTray(slotIndex);
        SetSelection(level.Board[slotIndex].OnBoard || stillHidden ? null : pressedPiece);

        pressedPiece = null;
        movedPastThreshold = false;
        activePointerId = NoPointerId;
    }

    /// <summary>
    /// 放手在回收框上：這一片收回待用區。
    ///
    /// ⚠️ <b>順序不能反。</b><c>SetInTray</c> 對還在盤上的片會丟例外（那是刻意的，
    /// 因為「在盤上」與「在待用區」互斥），所以一定先 <c>Lift</c>。
    /// 少了 <c>Lift</c> 的話症狀是「片收回抽屜了，但盤面認為它還在盒內」——
    /// 過關判定與中途回饋都會跟著錯。
    /// </summary>
    /// <param name="level">這一關的資料。</param>
    private void RecycleDrag(LevelData level)
    {
        int slotIndex = pressedPiece.SlotIndex;

        if (level.Board[slotIndex].OnBoard) level.Board.Lift(slotIndex);

        pressedPiece.IsSnapped = false;
        level.SetInTray(slotIndex, true);
        pressedPiece.IsVisible = false;
    }

    /// <summary>
    /// 拖曳結束時把結果交給盤面。拖曳中吸住只是預覽，<b>到這裡才是真的</b>。
    /// </summary>
    /// <param name="level">這一關的資料。</param>
    private void CommitDrag(LevelData level)
    {
        if (lastResult.Snapped)
        {
            // ⚠️ undo 的**唯一**推入點（P1-9）。一定要在 Place 之前取，
            // 記的是「動之前那一槽長什麼樣」——動之後才取的話記到的是新的位置，
            // 按 undo 會退到原地，看起來像「undo 沒反應」
            level.PushUndoStep(level.Board[pressedPiece.SlotIndex]);
            level.Board.Place(pressedPiece.SlotIndex, lastResult.Placement);
        }
        else if (locationBeforeDrag == PieceLocation.OnBoard)
        {
            // 本來在盤上、拖到沒吸住的地方 → 變成暫放，要從盤面拿下來。
            // 漏掉這行的話盤面會留著它舊的位置：畫面上片在盒外、模型認為它還在盒內，
            // 過關判定與中途回饋都會跟著錯，而且不報錯
            level.Board.Lift(pressedPiece.SlotIndex);
        }
        // 沒吸住又本來就不在盤上 = 暫放，純浮點不進 BoardState
    }

    /// <summary>
    /// 把互動整個歸零：清掉選取，並取消進行中的拖曳（片放回按下前的狀態）。
    /// <b>不動盤面。</b>
    ///
    /// <b>有兩個入口，而且必須是同一個動作</b>：
    /// <list type="bullet">
    /// <item>版面重算（<c>BoardView.LayoutRecomputed</c>，這支自己訂閱）</item>
    /// <item>換關（<c>LevelController.EnterLevel</c> 第 5 步會叫這裡）</item>
    /// </list>
    /// 兩邊各寫一次的話遲早有一邊會漏。
    ///
    /// ⚠️ <b>名字從 <c>CancelDrag</c> 改過來</b>（2026-09-07）：它現在也清選取，
    /// 而<b>清選取不能放在「手上沒片就回去」後面</b>——換關時多半沒有人在拖，
    /// 但上一關選取的那一片還高亮著，舊的寫法會整個跳過。
    ///
    /// 還沒跨過門檻的話什麼都沒動過，所以只要把手上那片放掉就好。
    /// </summary>
    public void CancelInteraction()
    {
        SetSelection(null);

        // ⚠️ 局內統計（P2-13）：**這裡是拖曳的第二個出口**。
        // 它接在 BoardView.LayoutRecomputed 上，所以**螢幕尺寸一變就會走到這裡**，
        // 而那條路不經過 EndPress。只把「停止」寫在 EndPress 裡的話，
        // 轉一次螢幕方向計時器就再也停不下來，那一局的拖曳時間會一路累加到過關，
        // **而且不報錯**。冪等規則②讓兩個出口都叫變成安全的
        LevelSession.Current?.Data?.StopDragTiming(GameClock.NowSeconds);

        // 鎖定的指標一定要跟著放掉。忘了的話症狀是「換關之後怎麼點都沒反應」——
        // 因為還鎖著上一關那根早就抬起來的手指
        activePointerId = NoPointerId;

        if (pressedPiece == null) return;

        if (movedPastThreshold)
        {
            if (locationBeforeDrag == PieceLocation.Tray)
            {
                // 從抽屜召喚出來的：收回去。
                // 它必定還沒進盤面（進盤面只發生在放手那一刻），所以 SetInTray 不會丟例外
                pressedPiece.IsSnapped = false;
                LevelSession.Current?.Data?.SetInTray(pressedPiece.SlotIndex, true);
                pressedPiece.IsVisible = false;
            }
            else
            {
                pressedPiece.Position = positionBeforeDrag;
                pressedPiece.IsSnapped = snappedBeforeDrag;
            }

            pressedPiece.IsBeingDragged = false;
        }

        pressedPiece = null;
        movedPastThreshold = false;
    }

    // ⚠️ 這裡原本有一個 public 的 ClearSelection()，唯一的呼叫者是 TrayView.Toggle
    //（開抽屜時清選取）。2026-09-09 那件事搬進 BeginPress 的箭頭分支之後它就沒有
    // 呼叫者了，所以一併刪掉——留著的話是一個「別人可以繞過正常流程改選取」的後門，
    // 而「至多一片被選取」的不變式只該由 SetSelection 維護。
    // 之後真的有第二個外部觸發點（快捷鍵之類）再開回來。

    // ─────────────────────────────────────────────────────────────
    // undo（P1-9）
    //
    // 模型那半在 LevelData.TryUndo，這裡只做**畫面同步**那半。
    // 分成兩半是因為 BoardState.Changed 沒有任何訂閱者，而且它也不帶
    // 「哪一槽變了」——所以「片要跟著動回去」沒有人會自動做。
    //
    // 放這支而不是新開一個投影者：寫 PieceView 的仍然只有控制器一個
    // （跟 P1-5 的結論一致，也是除錯記錄 003 那個坑的直接教訓）。
    // ─────────────────────────────────────────────────────────────

    /// <summary>
    /// 現在能不能退一步。<b>按鈕的顯示條件與作用條件刻意是同一個算式</b>，
    /// 跟 <see cref="ActionTarget"/> 同一個模式——分岔的話會出現
    /// 「看得到但按了沒反應」或反過來，兩種都很難查。
    ///
    /// 條件是**沒在拖 ＋ 抽屜沒開著 ＋ 堆疊非空**（2026-09-09 定案）。
    ///
    /// 「沒在拖」在單指模型下其實是多餘的（拖曳中手指佔著，不會有第二次按下），
    /// 留著是因為它是**視覺表達**：拖曳中按鈕消失，跟抽屜箭頭同一個性質。
    ///
    /// ⚠️ <b>「抽屜沒開著」則是非做不可的</b>，見 <see cref="IsTrayDrawerOpen"/>：
    /// 這顆鈕就在抽屜蓋住的範圍裡，留著會把本來要打到縮圖的按下吃掉。
    /// 而且它跟另外兩顆不同——<b>它不看選取</b>，所以沒有「開抽屜順便清選取」
    /// 那條規則可以順帶救它，不明寫就真的會擋住。
    /// 語意上也說得通：挑片的當下不會有人想退上一步。
    /// </summary>
    public bool CanUndo
    {
        get
        {
            LevelData level = LevelSession.Current?.Data;
            return level != null && !IsDragging && !IsTrayDrawerOpen && !IsLevelComplete
                   && level.CanUndo;
        }
    }

    /// <summary>
    /// 退一步：先讓模型退，再把退回去的那幾槽套用到畫面上。
    ///
    /// <b>規則住在這裡，按鈕只是眾多觸發器裡的一個</b>——之後加鍵盤快捷鍵或手勢
    /// 都叫這裡，跟 <see cref="RotateSelected"/> 同一個安排（2026-09-07 定案）。
    /// </summary>
    public void UndoLastPlacement()
    {
        if (!CanUndo) return;

        LevelData level = LevelSession.Current.Data;
        if (!level.TryUndo(out IReadOnlyList<PieceObservation> restored)) return;

        foreach (PieceObservation before in restored) ApplyRestoredPiece(before);

        // 跟換關、版面重算走同一條路：退完一律清選取（2026-09-09 定案）。
        // 留著的話那片可能剛被送回抽屜——高亮一片看不見的片，旋轉鈕跟著跳出來
        SetSelection(null);
    }

    /// <summary>
    /// 把一槽「退回去之後該有的樣子」寫到它的 <c>PieceView</c> 上。
    ///
    /// ⚠️ <b>兩條分支對姿態的處理剛好相反，而且兩邊都不能寫錯：</b>
    /// <list type="bullet">
    /// <item><b>回到盤上</b>——位置、旋轉、翻面<b>全部</b>以記下來的擺放為準。
    /// 少寫姿態的話「放進盤面 → 拖出來 → 轉三次 → undo」會看到片回到舊位置但角度是新的，
    /// 而模型記的是舊角度，畫面與模型不一致而且不報錯</item>
    /// <item><b>離開盤面</b>——姿態<b>一個都不要動</b>。那一槽的 <c>Placement</c> 存的是
    /// 「上次在盤上時」的姿態，玩家在暫放狀態轉過的角度它看不到；
    /// 「順手」拿來用的話症狀是<b>undo 之後片的角度莫名其妙轉回去了</b></item>
    /// </list>
    /// </summary>
    /// <param name="before">這一槽動之前的樣子，也就是它現在該有的樣子。</param>
    private void ApplyRestoredPiece(PieceObservation before)
    {
        PieceView piece = trayView != null ? trayView.PieceAt(before.Index) : null;
        if (piece == null) return;

        if (before.OnBoard)
        {
            piece.IsVisible = true;
            piece.Rotation = before.Placement.Rotation;
            piece.IsFlipped = before.Placement.IsFlipped;
            piece.SnapTo(before.Placement.Translation);   // 位置 ＋ IsSnapped 一起
        }
        else
        {
            HidePieceToTray(before.Index);
        }
    }

    /// <summary>
    /// 把一片收進抽屜的**畫面那半**：藏起來、取消吸附，<b>姿態一個都不動</b>。
    ///
    /// 「片離開盤面時姿態一律不動，只有換關會歸零」是 2026-09-09 定的一條總規則，
    /// 涵蓋回收框、清空、undo 三條路徑。三條共用這支就不可能只改到其中一條。
    ///
    /// <c>IsVisible</c> 寫在這裡只是為了<b>不閃一幀</b>——<c>TrayView</c> 下一幀也會
    /// 從 <c>LevelData</c> 投影出同樣的答案。兩邊不會打架，因為真相只有一份，
    /// 而這裡是在改完真相的同一刻順手同步，不是第二個真相（對照除錯記錄 003）。
    /// </summary>
    /// <param name="slotIndex">槽索引。</param>
    private void HidePieceToTray(int slotIndex)
    {
        PieceView piece = trayView != null ? trayView.PieceAt(slotIndex) : null;
        if (piece == null) return;

        piece.IsSnapped = false;
        piece.IsVisible = false;
    }

    /// <summary>
    /// 現在能不能清空。<b>顯示條件與作用條件共用同一個算式</b>，跟
    /// <see cref="CanUndo"/>、<see cref="ActionTarget"/> 同一個模式。
    ///
    /// 條件是**沒在拖 ＋ 抽屜沒開 ＋ 盤上有片**。
    ///
    /// ⚠️ 第三條問的是 <c>OnBoardCount</c>，<b>不是「有片不在抽屜裡」</b>——
    /// 清空的定義只涵蓋盤上的片，所以桌面上散著暫放的片時按鈕不該亮：
    /// 亮了按下去什麼都不會發生，那正是 §5 在避免的「看得到但按了沒反應」。
    /// </summary>
    public bool CanClearBoard
    {
        get
        {
            LevelData level = LevelSession.Current?.Data;
            return level != null && !IsDragging && !IsTrayDrawerOpen && !IsLevelComplete
                   && level.Board.OnBoardCount > 0;
        }
    }

    /// <summary>
    /// 清空：把盤面上放好的片全部推回待用區。<b>暫放的片不動</b>（見
    /// <see cref="LevelData.TryClearBoardToTray"/> 說明的理由）。
    ///
    /// 整組算 undo 的**一步**，所以按一次 undo 就整個還原。
    /// </summary>
    public void ClearBoard()
    {
        if (!CanClearBoard) return;

        LevelData level = LevelSession.Current.Data;
        if (!level.TryClearBoardToTray(out IReadOnlyList<PieceObservation> cleared)) return;

        // 局內統計（P2-13）。⚠️ 要在真的清成功之後才記——上面那個 return 是
        // 「盤面本來就空的」，那不算一次清空
        level.RecordClear();

        foreach (PieceObservation observation in cleared) HidePieceToTray(observation.Index);

        // 跟換關、版面重算、undo 走同一條路。留著的話那片剛被送回抽屜，
        // 會變成高亮一片看不見的片，旋轉鈕跟著跳出來
        SetSelection(null);
    }

    /// <summary>
    /// ⚠️ <b>開發用的捷徑（P2-4）</b>：照參考解填六片，留大三角自己拼。
    ///
    /// <b>為什麼留大三角</b>：它面積最大、資訊量最大，留下來還驗得到吸附與旋轉的手感；
    /// 留小三角的話幾乎是「按一下就過關」，什麼都測不到。
    ///
    /// ⚠️ <b>正式版不會有這支的呼叫者</b>——按鍵在 <c>DebugTools</c> 裡，
    /// 那支跟 <c>DebugOverlay</c> 用同一個編譯符號自動關掉。
    ///
    /// 模型那半在 <see cref="LevelData.TryFillFromReferenceSolution"/>，
    /// 這裡只做畫面同步，跟 undo／清空走同一條路。
    /// </summary>
    public void FillFromReferenceSolutionForDebug()
    {
        LevelData level = LevelSession.Current?.Data;
        if (level == null || IsDragging) return;

        // 大三角是 TangramSet 的第 0 槽
        if (!level.TryFillFromReferenceSolution(0, out _)) return;

        RefreshAllPiecesFromBoard();
    }

    /// <summary>
    /// 照<b>現在的盤面</b>把七片的畫面整組重畫一次，然後清掉選取。
    ///
    /// ⚠️ <b>一定是照現在的盤面，不是照「動了哪幾片」的回傳值</b>——那些回傳的是
    /// 「動之前」的樣子，拿來當「現在該長什麼樣」會整組畫錯。
    ///
    /// 給那些<b>一次改掉一整盤</b>的動作用：除錯用的照參考解填、
    /// 出題工具載入之前存過的題目。一般的拖曳不走這裡，它只動被拖的那一片。
    /// </summary>
    public void RefreshAllPiecesFromBoard()
    {
        LevelData level = LevelSession.Current?.Data;
        if (level == null) return;

        for (int slotIndex = 0; slotIndex < BoardState.PieceCount; slotIndex++)
        {
            PieceObservation current = level.Board[slotIndex];
            if (current.OnBoard) ShowPieceOnBoard(slotIndex, current.Placement);
            else HidePieceToTray(slotIndex);
        }

        // 跟換關、undo、清空同一條路：一律清選取
        SetSelection(null);
    }

    /// <summary>
    /// 把一槽「現在在盤上的樣子」寫到它的 <c>PieceView</c> 上。
    /// 姿態<b>全部</b>以擺放為準——少寫的話畫面與模型會不一致而且不報錯
    /// （同 <see cref="ApplyRestoredPiece"/> 的第一條分支）。
    /// </summary>
    /// <param name="slotIndex">槽索引。</param>
    /// <param name="placement">這一槽現在的擺放。</param>
    private void ShowPieceOnBoard(int slotIndex, Placement placement)
    {
        PieceView piece = trayView != null ? trayView.PieceAt(slotIndex) : null;
        if (piece == null) return;

        piece.IsVisible = true;
        piece.Rotation = placement.Rotation;
        piece.IsFlipped = placement.IsFlipped;
        piece.SnapTo(placement.Translation);   // 位置 ＋ IsSnapped 一起
    }

    /// <summary>
    /// 這一下有沒有落在「不給抓片」的矩形上（也就是按鈕）。
    ///
    /// <b>為什麼不用 <c>EventSystem.IsPointerOverGameObject()</c></b>：
    /// 那支的答案由 UI 的輸入模組在它自己的 <c>Update</c> 裡算，
    /// 而 <c>MonoBehaviour</c> 之間的 <c>Update</c> 順序是未定義的——
    /// **按下的那一幀很可能拿到上一幀的答案**，結果就是「按按鈕的同時也抓到了片」，
    /// 而且時好時壞、完全不報錯。矩形自己算沒有這個順序問題，
    /// 滑鼠與觸控也是同一套（那支還要看平台傳 pointerId）。
    ///
    /// 代價是它只認得填進來的那幾個矩形。P4 加更多 UI 時記得一起填，
    /// 或那時候再換成一般性的 UI 命中測試。
    ///
    /// ⚠️ 按鈕是**隱藏中**（物件沒啟用）就不擋——不然按鈕消失之後那塊區域
    /// 會永遠變成點不到片的死角。顯示與否由按鈕自己在 <c>Update</c> 切換，
    /// 所以最壞情況是切換那一幀慢半拍，影響僅止於那 16 ms。
    /// </summary>
    /// <param name="screenPoint">按下的螢幕座標。</param>
    /// <returns>落在其中任一個矩形上就回 true。</returns>
    private bool IsOverBlockingArea(Vector2 screenPoint)
    {
        foreach (RectTransform area in blockingAreas)
        {
            if (area == null || !area.gameObject.activeInHierarchy) continue;

            // ⚠️ 相機參數不能亂傳：Screen Space - Overlay 的畫布要傳 null，
            // 其他模式要傳畫布自己的相機。傳錯不會報錯，只會變成永遠命中或永遠不命中
            Canvas canvas = area.GetComponentInParent<Canvas>();
            Camera canvasCamera =
                canvas != null && canvas.renderMode != RenderMode.ScreenSpaceOverlay
                    ? canvas.worldCamera
                    : null;

            if (RectTransformUtility.RectangleContainsScreenPoint(area, screenPoint, canvasCamera))
                return true;
        }

        foreach (Func<Rect> screenRect in blockingScreenRects)
            if (screenRect != null && screenRect().Contains(screenPoint)) return true;

        return false;
    }

    /// <summary>
    /// 點到哪一片。**畫在最上面的優先。**
    ///
    /// ⚠️ <c>Physics2D</c> 完全不看 <c>sortingOrder</c>，回傳順序跟繪製層級無關。
    /// 片會重疊（暫放的片可以疊在一起），所以要自己挑
    /// <see cref="PieceView.SortingOrder"/> 最大的那一片——
    /// 不挑的話「看得到的那片點不到」，而且每次點到誰是隨機的。
    /// </summary>
    /// <param name="worldPoint">手指的世界座標。</param>
    /// <returns>命中而且畫在最上面的片；沒點到任何片時回 null。</returns>
    private PieceView TopmostPieceAt(Vector2 worldPoint)
    {
        hits.Clear();
        Physics2D.OverlapPoint(worldPoint, hitFilter, hits);

        PieceView topmost = null;
        foreach (Collider2D hit in hits)
        {
            PieceView piece = hit.GetComponent<PieceView>();
            if (piece == null) continue;
            if (topmost == null || piece.SortingOrder > topmost.SortingOrder) topmost = piece;
        }
        return topmost;
    }
}
