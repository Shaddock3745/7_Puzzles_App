using System.Collections.Generic;
using Tangram.Core;
using Tangram.Data;
using UnityEngine;
using UnityEngine.InputSystem;

/// <summary>
/// 指標輸入（P1-6，互動設計 §4）。按下挑一片 → 每幀問吸附器 → 放手才交給盤面。
///
/// <b>一次按下有兩種結局</b>（§4「點選與拖曳的分界」，2026-09-07 加）：
/// 手指離按下點超過 <see cref="moveThresholdPixels"/> 就是**拖曳**，沒超過就是**點選**。
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

    [Tooltip("超過這個距離才算拖曳，沒超過就是點選。單位是螢幕像素。實際手感玩過再調（P1-13）。")]
    [SerializeField] private float moveThresholdPixels = 10f;

    [Tooltip("落在這些矩形上的按下要整個忽略。旋轉鈕與翻面鈕會自己註冊，不用填；" +
             "這裡是留給之後其他 UI 的。")]
    [SerializeField] private RectTransform[] pressBlockingAreas;

    /// <summary>
    /// 實際在用的「不給抓片」矩形。Inspector 填的那些會在 <c>Awake</c> 併進來，
    /// 其餘由各自的 UI 在啟用時自己註冊。
    /// </summary>
    private readonly List<RectTransform> blockingAreas = new List<RectTransform>();

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
    /// 按鈕現在能作用在哪一片，不能作用時是 <c>null</c>。
    ///
    /// 條件就是互動設計 §5 的兩條：<b>有選取</b> ＋ <b>那片沒在被拖</b>。
    /// 按鈕拿它決定要不要顯示，這裡也拿它當保險——
    /// <b>顯示條件與作用條件是同一個算式，不會分岔</b>。
    /// </summary>
    public PieceView ActionTarget
        => selectedPiece != null && !selectedPiece.IsBeingDragged ? selectedPiece : null;

    /// <summary>
    /// 把選取的那一片轉一格（互動設計 §5）。
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
    public void RotateSelected()
    {
        PieceView piece = RequireActionTarget("RotateSelected");
        if (piece == null) return;

        // ⚠️ 一定要照片種的循環長度取餘數，不是固定 8。
        // 理由不是「按了沒反應」（正方形用 8 每按還是轉 45°），而是
        // **同一個外觀不要有兩個編號**——正方形 rot 0 與 rot 2 佔位一樣但
        // Placement.Equals 不相等，之後 undo 與解的比對會踩到
        int cycleLength = SnapSolver.RotationCycleLength(piece.Kind);
        piece.Rotation = (piece.Rotation + 1) % cycleLength;
    }

    /// <summary>
    /// 把選取的那一片翻面（互動設計 §5）。
    ///
    /// <b>只有平行四邊形有效</b>（<see cref="SnapSolver.CanFlip"/>）。
    /// 其餘片的鏡像等於某個旋轉，翻了是做白工——畫面跳一下但可達的姿態集合完全沒變。
    /// 按鈕本來就不該對那些片顯示，這裡再擋一次。
    ///
    /// <b>⚠️ 翻面會順便把旋轉格數取負</b>，理由見 <see cref="MirroredRotation"/>。
    /// </summary>
    public void FlipSelected()
    {
        PieceView piece = RequireActionTarget("FlipSelected");
        if (piece == null) return;
        if (!SnapSolver.CanFlip(piece.Kind)) return;

        // 兩個 setter 各自都會把視覺中心留在原地，所以先後順序不影響結果
        piece.IsFlipped = !piece.IsFlipped;
        piece.Rotation = MirroredRotation(piece.Rotation, SnapSolver.RotationCycleLength(piece.Kind));
    }

    /// <summary>
    /// 翻面之後該落在哪一格旋轉，好讓畫面上的鏡射軸是水平或垂直的（2026-09-07）。
    ///
    /// <b>問題</b>：模型的翻面是對**片自己的 x 軸**鏡射，然後才旋轉。
    /// 所以從畫面看，翻面等於「繞一條通過中心、角度 θ 的直線鏡射」——
    /// θ 是奇數格（45°、135°）時那條軸是斜的，看起來不像翻面，像被扭了一下。
    ///
    /// <b>推導</b>：舊姿態 <c>R(θ)</c>、新姿態 <c>R(θ')·M</c>，
    /// 兩者之間的變換是 <c>R(θ'+θ)·M</c>，那是「繞角度 <c>(θ'+θ)/2</c> 的直線鏡射」。
    /// 目前 <c>θ' = θ</c> 所以軸就是 θ。要讓軸落在 0° 或 90°，需要 <c>θ' = −θ</c>。
    ///
    /// <b>為什麼這樣改不會少掉姿態</b>：翻兩次回到原狀（<c>−(−θ) = θ</c>），
    /// 而且「轉到任一格 → 翻 → 再轉到任一格」照樣走得到全部八種姿態。
    ///
    /// <b>為什麼放在這裡而不是 <see cref="PieceView.IsFlipped"/> 的設定值那一層</b>：
    /// 這是**翻面鈕的手感規則**，不是翻面本身的定義。
    /// 拖曳的還原、換關重設那些路徑要能單獨改翻面而不動旋轉，
    /// 做進 setter 的話它們會被順便改掉，而且不報錯。
    /// </summary>
    /// <param name="rotation">目前的旋轉格數。</param>
    /// <param name="cycleLength">這一片的旋轉循環長度。</param>
    /// <returns>取負之後的旋轉格數，保證落在 0 ~ cycleLength−1。</returns>
    private static int MirroredRotation(int rotation, int cycleLength)
        => (cycleLength - rotation % cycleLength) % cycleLength;

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

    // 取消拖曳時要還原到哪
    private Vector2 positionBeforeDrag;
    private bool snappedBeforeDrag;
    private bool onBoardBeforeDrag;

    private readonly List<Collider2D> hits = new List<Collider2D>();
    private ContactFilter2D hitFilter;

    private void Awake()
    {
        if (boardView == null) boardView = FindFirstObjectByType<BoardView>();
        if (boardView == null)
            Debug.LogError("TouchInputController 找不到 BoardView：拖曳完全不會有反應。" +
                           "請在場景裡建一個掛 BoardView 的物件，或直接拖到這支的欄位上。", this);

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

        Pointer pointer = Pointer.current;
        if (pointer == null) return;

        // 螢幕座標留著給門檻用，世界座標給命中測試與吸附用
        Vector2 screenPoint = pointer.position.ReadValue();
        Vector2 worldPoint = boardView.ScreenToWorld(screenPoint);

        if (pressedPiece == null)
        {
            // 落在按鈕上的按下**整個不理**——連 BeginPress 都不進去。
            // 這正是我們要的：不進去就不會跑「按在空白處清選取」那一段，
            // 所以按旋轉鈕不會把自己的作用對象清掉
            if (pointer.press.wasPressedThisFrame && !IsOverBlockingArea(screenPoint))
                BeginPress(level, worldPoint, screenPoint);
            return;
        }

        if (pointer.press.wasReleasedThisFrame) { EndPress(level); return; }

        // 沒收到放開事件也要收尾（例如視窗失焦），否則那片會永遠停在按著的狀態
        if (!pointer.press.isPressed) { EndPress(level); return; }

        UpdatePress(level, worldPoint, screenPoint);
    }

    /// <summary>
    /// 按下：挑一片、記下抓取偏移與還原點。<b>這裡還不知道是點選還是拖曳</b>，
    /// 所以什麼都不動——連 <c>IsBeingDragged</c> 都還不打開
    /// （打開的話點一下就會看到那片閃到最上層一下）。
    /// </summary>
    /// <param name="level">這一關的資料。</param>
    /// <param name="worldPoint">手指的世界座標。</param>
    /// <param name="screenPoint">手指的螢幕座標，門檻要用。</param>
    private void BeginPress(LevelData level, Vector2 worldPoint, Vector2 screenPoint)
    {
        PieceView piece = TopmostPieceAt(worldPoint);
        if (piece == null)
        {
            // 按在空白處 → 清掉選取。
            //
            // §4 的規則寫「放手時算」，但空白這一條**當場清是等價的**：
            // 按下與放開之間沒有任何跟選取有關的畫面，玩家分不出差別。
            // 換來的是省掉「這次按下有沒有抓到片」那個狀態，以及 Update 裡
            // 「手上沒片但要等放開」的另一條路
            SetSelection(null);
            return;
        }

        pressedPiece = piece;
        grabOffset = piece.Position - worldPoint;
        pressScreenPosition = screenPoint;
        movedPastThreshold = false;

        positionBeforeDrag = piece.Position;
        snappedBeforeDrag = piece.IsSnapped;
        onBoardBeforeDrag = level.Board[piece.SlotIndex].OnBoard;

        // 保險：跨過門檻那一幀 UpdatePress 一定會覆蓋它，所以這行其實用不到。
        // 留著是為了「Snapped = true 但 Placement 沒填」在任何時刻都不可能成立
        lastResult = SnapSolver.DragResult.Free(piece.Position);
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
            float threshold = Mathf.Max(moveThresholdPixels, 0f);
            if ((screenPoint - pressScreenPosition).sqrMagnitude < threshold * threshold)
            {
                // 還在門檻內：**片一動也不動**。這是刻意的——
                // 判定成點選時就完全沒有東西要還原（§4）
                return;
            }

            movedPastThreshold = true;
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
    private void EndPress(LevelData level)
    {
        if (movedPastThreshold)
        {
            CommitDrag(level);
            pressedPiece.IsBeingDragged = false;
        }

        // §4 的高亮規則：手上那片不在盤面上就選它，否則清掉。
        // **點選與拖曳兩條路共用**，所以寫在分岔外面。
        //
        // ⚠️ 一定要在 CommitDrag **之後**才讀 OnBoard——那一步才剛把它寫進去。
        // 反過來的話「拖進盒子放好」會讀到舊值、把剛放好的片留成高亮，
        // 而 §5 的補償守衛假設高亮的片沒吸住，接著按旋轉鈕就會看到片飛出去
        SetSelection(level.Board[pressedPiece.SlotIndex].OnBoard ? null : pressedPiece);

        pressedPiece = null;
        movedPastThreshold = false;
    }

    /// <summary>
    /// 拖曳結束時把結果交給盤面。拖曳中吸住只是預覽，<b>到這裡才是真的</b>。
    /// </summary>
    /// <param name="level">這一關的資料。</param>
    private void CommitDrag(LevelData level)
    {
        if (lastResult.Snapped)
        {
            level.Board.Place(pressedPiece.SlotIndex, lastResult.Placement);
        }
        else if (onBoardBeforeDrag)
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

        if (pressedPiece == null) return;

        if (movedPastThreshold)
        {
            pressedPiece.Position = positionBeforeDrag;
            pressedPiece.IsSnapped = snappedBeforeDrag;
            pressedPiece.IsBeingDragged = false;
        }

        pressedPiece = null;
        movedPastThreshold = false;
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
