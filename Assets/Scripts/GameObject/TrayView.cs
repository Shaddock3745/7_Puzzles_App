using Tangram.Core;
using Tangram.Data;
using UnityEngine;

/// <summary>
/// 待用區「抽屜」（P1-5，待辦清單 B 區的 P1-5 設計討論）。
///
/// <b>它做三件事，而只有第三件跟輸入有關</b>：
/// <list type="number">
/// <item><b>生出並持有那七片</b>，並把 <c>LevelData.IsInTray</c> 投影成
///       <see cref="PieceView.IsVisible"/></item>
/// <item><b>畫</b>抽屜、縮圖、箭頭、回收框</item>
/// <item><b>回答命中</b>：「這個螢幕座標歸你嗎、是哪一槽」</item>
/// </list>
///
/// ⚠️ <b>它不動片的姿態、位置、吸附、選取。</b>那些的寫入者只有
/// <see cref="TouchInputController"/> 一個。這裡只寫 <see cref="PieceView.IsVisible"/>，
/// 兩邊的欄位完全不重疊，所以<b>不是 <c>PiecePreview</c> 那個坑</b>——
/// 那支的問題是每幀無條件寫控制器也在寫的同一批欄位（除錯記錄 003）。
///
/// <b>取代了 <c>PiecePreview</c></b>（P1-5 做好就刪，2026-09-08 執行）。
///
/// <b>接線</b>：場景裡建一個空物件掛上這支就好，其餘欄位留空會自己找。
/// 視覺是程序生成的灰白方塊，**是佔位用的**——美術往後放（CLAUDE.md 的協作規則）。
/// </summary>
[DisallowMultipleComponent]
public class TrayView : MonoBehaviour
{
    /// <summary>命中測試的答案（待辦清單「分類與偏移的定案」那張表的抽屜部分）。</summary>
    public enum Hit
    {
        /// <summary>沒打到抽屜的任何東西。</summary>
        None,

        /// <summary>打到某一格縮圖。<b>要產生拖曳</b>，拖的是那一槽的片。</summary>
        Thumbnail,

        /// <summary>打到抽屜本體（含縮圖之間的空隙）。<b>什麼都不做</b>——連清選取都不做。</summary>
        DrawerBody,

        /// <summary>打到開合的箭頭。<b>開或關抽屜</b>，不產生拖曳。</summary>
        Arrow
    }

    [Tooltip("座標換算的來源（要它的「一個模型單位佔幾像素」）。留空的話 Awake 會自己找。")]
    [SerializeField] private BoardView boardView;

    [Tooltip("拖曳狀態的來源。抽屜靠它決定要不要自動收折、回收框要不要出現。留空會自己找。")]
    [SerializeField] private TouchInputController inputController;

    [Tooltip("抽屜開著時佔畫面高度的比例。")]
    [Range(0.1f, 0.5f)]
    [SerializeField] private float drawerHeightFraction = 0.22f;

    [Tooltip("箭頭（也是回收框）的邊長，螢幕像素。")]
    [SerializeField] private float arrowSizePixels = 96f;

    [Tooltip("縮圖佔一格的比例。1 = 塞滿，會貼在一起。")]
    [Range(0.3f, 1f)]
    [SerializeField] private float thumbnailFillRatio = 0.72f;

    [Header("片的圖")]
    [Tooltip("往內縮多少（模型單位）。片與片之間那條縫的一半寬就是它。")]
    [Range(0f, 0.25f)]
    [SerializeField] private float insetModelUnits = 0.02f;

    [Tooltip("圓角半徑（模型單位）。")]
    [Range(0f, 0.25f)]
    [SerializeField] private float cornerRadiusModelUnits = 0.005f;

    /// <summary>抽屜開著嗎。</summary>
    public bool IsOpen { get; private set; }

    private readonly PieceView[] pieces = new PieceView[BoardState.PieceCount];
    private readonly SpriteRenderer[] thumbnails = new SpriteRenderer[BoardState.PieceCount];

    private PieceSpriteLibrary library;

    /// <summary>七片的父物件。<b>必須維持 identity</b>，見 <see cref="EnforcePieceRootIdentity"/>。</summary>
    private Transform pieceRoot;

    /// <summary>
    /// 抽屜視覺的父物件。<b>每幀跟著相機轉</b>，所以底下的東西在螢幕上永遠是正的。
    ///
    /// ⚠️ 沒有它的話，題目 roll 90° 時（互動設計 §2.6 轉相機不轉模型）
    /// 整個抽屜會跟著躺下來。而那不會報錯，只會很怪。
    /// </summary>
    private Transform overlayRoot;

    private SpriteRenderer drawerPanel;
    private SpriteRenderer arrowTab;
    private SpriteRenderer recycleBox;

    /// <summary>程序生成的 1×1 白色 sprite，抽屜／箭頭／回收框都用它加 tint。</summary>
    private Sprite blockSprite;
    private Texture2D blockTexture;

    private void Awake()
    {
        if (boardView == null) boardView = FindFirstObjectByType<BoardView>();
        if (inputController == null) inputController = FindFirstObjectByType<TouchInputController>();

        if (boardView == null)
            Debug.LogError("TrayView 找不到 BoardView：算不出「一個模型單位佔幾像素」，" +
                           "抽屜與縮圖不會出現在正確的位置。", this);

        BuildBlockSprite();
        BuildRoots();
        BuildPieces();          // ⚠️ 一定要在 Awake，見下面的註解
        BuildOverlay();
    }

    /// <summary>
    /// 七片在 <c>Awake</c> 生，不是 <c>Start</c>。
    ///
    /// ⚠️ <b>因為 <c>LevelController.Start</c> 會叫 <see cref="ResetPiecePoses"/></b>，
    /// 而兩個 <c>MonoBehaviour</c> 的 <c>Start</c> 誰先跑是**未定義**的。
    /// 生在 <c>Start</c> 的話有一半的機率片還不存在，那一關的姿態就沒被重設——
    /// 症狀是「換關之後片還帶著上一關轉過的角度」，時好時壞而且不報錯。
    /// 所有 <c>Awake</c> 都保證跑在所有 <c>Start</c> 之前，所以放這裡就沒有這個問題。
    /// </summary>
    private void BuildPieces()
    {
        library = new PieceSpriteLibrary(insetModelUnits: insetModelUnits,
                                         cornerRadiusModelUnits: cornerRadiusModelUnits);

        for (int slotIndex = 0; slotIndex < BoardState.PieceCount; slotIndex++)
        {
            PieceKind kind = PiecePrototype.TangramSet[slotIndex];

            var pieceObject = new GameObject($"Piece{slotIndex}_{kind}");
            pieceObject.transform.SetParent(pieceRoot, worldPositionStays: false);

            PieceView piece = pieceObject.AddComponent<PieceView>();
            piece.Initialize(kind, slotIndex, library);

            // 開場全部收在抽屜裡。真正的真相是 LevelData，但那一關要到
            // LevelController.Start 才載入，所以這裡先給一個一致的初值
            piece.IsVisible = false;
            pieces[slotIndex] = piece;

            var thumbnailObject = new GameObject($"Thumbnail{slotIndex}_{kind}");
            thumbnailObject.transform.SetParent(overlayRoot, worldPositionStays: false);

            var thumbnail = thumbnailObject.AddComponent<SpriteRenderer>();
            thumbnail.sprite = library.SpriteFor(kind);
            thumbnail.sortingLayerName = PieceSorting.GameUiSortingLayer;
            thumbnail.sortingOrder = ThumbnailSortingOrder;
            thumbnails[slotIndex] = thumbnail;
        }
    }

    private void BuildRoots()
    {
        pieceRoot = new GameObject("Pieces").transform;
        pieceRoot.SetParent(transform, worldPositionStays: false);

        overlayRoot = new GameObject("TrayOverlay").transform;
        overlayRoot.SetParent(transform, worldPositionStays: false);
    }

    private void BuildOverlay()
    {
        drawerPanel = BuildBlock("DrawerPanel", DrawerSortingOrder, new Color(0.12f, 0.13f, 0.16f, 0.92f));
        arrowTab = BuildBlock("ArrowTab", OverlayTopSortingOrder, new Color(0.75f, 0.78f, 0.82f, 0.85f));
        recycleBox = BuildBlock("RecycleBox", OverlayTopSortingOrder, new Color(0.85f, 0.35f, 0.35f, 0.45f));
    }

    /// <param name="objectName">物件名稱，只影響 Hierarchy 好不好讀。</param>
    /// <param name="sortingOrder">在 <c>GameUI</c> 層裡的先後。<b>只跟同層的東西比。</b></param>
    /// <param name="tint">顏色。</param>
    /// <returns>掛在 <see cref="overlayRoot"/> 底下的一個純色方塊。</returns>
    private SpriteRenderer BuildBlock(string objectName, int sortingOrder, Color tint)
    {
        var blockObject = new GameObject(objectName);
        blockObject.transform.SetParent(overlayRoot, worldPositionStays: false);

        var renderer = blockObject.AddComponent<SpriteRenderer>();
        renderer.sprite = blockSprite;
        renderer.color = tint;
        renderer.sortingLayerName = PieceSorting.GameUiSortingLayer;
        renderer.sortingOrder = sortingOrder;
        return renderer;
    }

    /// <summary>
    /// 一張 1×1 的白圖，<c>pixelsPerUnit = 1</c>，所以 <c>localScale</c> 直接就是世界單位的寬高。
    ///
    /// 這樣抽屜、箭頭、回收框三個矩形共用一張圖，不必匯入任何美術資源——
    /// 跟片的圖一樣是執行時生成的（互動設計 §13.7 的同一個理由）。
    /// </summary>
    private void BuildBlockSprite()
    {
        blockTexture = new Texture2D(1, 1, TextureFormat.RGBA32, mipChain: false);
        blockTexture.SetPixel(0, 0, Color.white);
        blockTexture.Apply();

        blockSprite = Sprite.Create(blockTexture, new Rect(0f, 0f, 1f, 1f),
                                    new Vector2(0.5f, 0.5f), pixelsPerUnit: 1f);
    }

    // 抽屜這三個東西的先後。⚠️ **只在 GameUI 層裡有意義**——
    // 它們跟片、盒子、彈窗的先後是 sorting layer 決定的，不會拿來比大小。
    //
    // 2026-09-09 之前這三個是 2000 / 2001 / 2002，因為當時所有東西都在 Default 層，
    // 要蓋過片（0～226）就只能挑一個「夠大」的數字。那不是排序是巧合：
    // 沒有東西會在它被破壞時報錯，而症狀是「某個東西被蓋住了」。
    private const int DrawerSortingOrder = 0;
    private const int ThumbnailSortingOrder = 1;
    private const int OverlayTopSortingOrder = 2;

    // ─────────────────────────────────────────────────────────────
    // 螢幕上的三塊矩形。每幀從 Screen 重算，不快取——
    // 快取的話轉螢幕方向就要有人記得失效，而那是 P1-1 早就決定不做的事
    //（BoardLayout 的「持有輸入而不是算好的係數」同一個理由）
    // ─────────────────────────────────────────────────────────────

    /// <summary>抽屜本體在螢幕上的矩形（原點左下）。抽屜關著時是空的。</summary>
    private Rect DrawerRect => new Rect(0f, 0f, Screen.width, Screen.height * drawerHeightFraction);

    /// <summary>
    /// 箭頭（也是回收框）在螢幕上的矩形。
    ///
    /// 兩者<b>共用同一塊</b>是刻意的（決定 21）：平時是箭頭，拖曳中是回收框，
    /// <b>永遠只有一個在</b>。分時互斥，玩家不會搞混，也不會有兩個功能搶同一塊像素。
    /// </summary>
    private Rect ArrowRect
    {
        get
        {
            float size = arrowSizePixels;
            float y = IsOpen ? DrawerRect.height : 0f;
            return new Rect(Screen.width - size, y, size, size);
        }
    }

    // ─────────────────────────────────────────────────────────────
    // 對外：命中回答與開合
    // ─────────────────────────────────────────────────────────────

    /// <summary>
    /// 這個螢幕座標打到抽屜的什麼。<b>只回答，不做任何事</b>——
    /// 要怎麼反應由 <see cref="TouchInputController"/> 決定（決定 3）。
    /// </summary>
    /// <param name="screenPoint">按下的螢幕座標，原點左下。</param>
    /// <param name="slotIndex">
    /// 回傳 <see cref="Hit.Thumbnail"/> 時是那一格對應的槽索引，其餘情況是 −1。
    /// </param>
    /// <returns>打到什麼。</returns>
    public Hit HitTest(Vector2 screenPoint, out int slotIndex)
    {
        slotIndex = -1;

        // 拖曳中箭頭是隱藏的（那時候那塊是回收框），過關之後也是（整個盤面鎖住，
        // P1-10）。隱藏的東西不該擋——跟按鈕「隱藏中就不擋」是同一條規則
        //（互動設計 §5），不然那塊區域會變成永遠點不到片的死角
        if (IsArrowShown && ArrowRect.Contains(screenPoint)) return Hit.Arrow;

        if (!IsOpen) return Hit.None;
        if (!DrawerRect.Contains(screenPoint)) return Hit.None;

        int cell = CellIndexAt(screenPoint);
        if (cell >= 0 && !IsCellEmpty(cell))
        {
            slotIndex = cell;
            return Hit.Thumbnail;
        }

        // 打在抽屜上但不是縮圖（空隙、或那一格的片已經被拿出去了）。
        // ⚠️ 回 DrawerBody 而不是 None 很重要：None 會讓控制器走「點到空白 → 清選取」，
        // 玩家在抽屜裡點歪一下，盤面上選取的那片就莫名其妙不亮了
        return Hit.DrawerBody;
    }

    /// <summary>
    /// 箭頭現在該不該出現。<b>顯示條件與命中條件共用同一個算式</b>，
    /// 跟四顆按鈕同一個模式（互動設計 §5）——分岔的話會出現
    /// 「看得到但按了沒反應」或反過來，兩種都很難查。
    ///
    /// 兩個條件：
    /// <list type="bullet">
    /// <item><b>沒在拖</b>——拖曳中那塊是回收框，兩者分時互斥（決定 21）</item>
    /// <item><b>沒過關</b>——過關之後整個盤面鎖住，完成彈窗蓋滿畫面（P1-10）</item>
    /// </list>
    ///
    /// ⚠️ 這裡<b>不看抽屜開著沒</b>。抽屜開著時箭頭必須還按得到，不然關不起來——
    /// 這是它跟那四顆鈕唯一不同的地方。
    /// </summary>
    private bool IsArrowShown
    {
        get
        {
            if (inputController == null) return true;
            return !inputController.IsDragging && !inputController.IsLevelComplete;
        }
    }

    /// <summary>拖曳中才顯示的回收框。放手時由控制器問一次。</summary>
    /// <param name="screenPoint">手指的螢幕座標。</param>
    /// <returns>手指在不在回收框裡。</returns>
    public bool IsOverRecycleArea(Vector2 screenPoint) => ArrowRect.Contains(screenPoint);

    /// <summary>
    /// 開或關抽屜。控制器在分類到 <see cref="Hit.Arrow"/> 時叫這裡。
    ///
    /// <b>打開的當下清掉選取</b>（決定 17）：抽屜整個蓋住盤面，
    /// 留著選取的話旋轉／翻面鈕會被蓋在抽屜底下，變成「看得到但按不到」。
    /// </summary>
    public void Toggle()
    {
        IsOpen = !IsOpen;
        if (IsOpen && inputController != null) inputController.ClearSelection();
    }

    /// <summary>關上抽屜。</summary>
    public void Close() => IsOpen = false;

    /// <summary>
    /// 把七片的姿態重設成開局值（決定 7：關卡開始時旋轉 0、沒翻面）。
    /// <c>LevelController.EnterLevel</c> 第 4 步叫這裡。
    ///
    /// <b>位置不用管</b>——七片這時候全在抽屜裡（看不到），
    /// 真正的位置要到玩家把它拉出來、召喚到手指底下時才有意義。
    /// </summary>
    public void ResetPiecePoses()
    {
        foreach (PieceView piece in pieces)
        {
            if (piece == null) continue;
            piece.IsSnapped = false;
            piece.IsBeingDragged = false;
            piece.Rotation = 0;
            piece.IsFlipped = false;
        }
        Close();
    }

    /// <param name="slotIndex">槽索引 0~6。</param>
    /// <returns>那一槽的片，越界時回 null。</returns>
    public PieceView PieceAt(int slotIndex)
        => slotIndex >= 0 && slotIndex < pieces.Length ? pieces[slotIndex] : null;

    // ─────────────────────────────────────────────────────────────

    private void Update()
    {
        EnforcePieceRootIdentity();
        ProjectTrayStateToPieces();

        bool dragging = inputController != null && inputController.IsDragging;

        // 決定 19：跨過門檻就自動收折。抽屜自己看拖曳狀態，不必控制器來通知——
        // 這樣它跟旋轉鈕一樣「不訂閱任何東西，只是狀態的函數」（互動設計 §5）
        if (dragging) Close();

        LayoutOverlay(dragging);
    }

    /// <summary>
    /// 把「片在不在待用區」投影到片的顯示與否。
    ///
    /// <b>真相在 <c>LevelData</c></b>，這裡只是每幀推過去，跟旋轉鈕讀選取狀態同一個模式。
    /// 沒有載入關卡時什麼都不做——場上可能只是還沒 <c>Start</c>。
    /// </summary>
    private void ProjectTrayStateToPieces()
    {
        LevelData level = LevelSession.Current?.Data;
        if (level == null) return;

        for (int slotIndex = 0; slotIndex < pieces.Length; slotIndex++)
        {
            if (pieces[slotIndex] == null) continue;
            pieces[slotIndex].IsVisible = !level.IsInTray(slotIndex);
        }
    }

    /// <param name="cellIndex">抽屜裡第幾格。</param>
    /// <returns>那一格是空的嗎（那一片已經被拿出抽屜了）。</returns>
    private bool IsCellEmpty(int cellIndex)
    {
        LevelData level = LevelSession.Current?.Data;
        return level == null || !level.IsInTray(cellIndex);
    }

    /// <param name="screenPoint">螢幕座標。</param>
    /// <returns>落在第幾格，不在抽屜裡時回 −1。</returns>
    private int CellIndexAt(Vector2 screenPoint)
    {
        Rect drawer = DrawerRect;
        if (drawer.width <= 0f) return -1;

        int cell = Mathf.FloorToInt((screenPoint.x - drawer.x) / drawer.width * BoardState.PieceCount);
        return cell >= 0 && cell < BoardState.PieceCount ? cell : -1;
    }

    /// <summary>
    /// 一個模型單位佔幾個世界單位下的螢幕像素。抽屜的版面全靠它把像素換成世界座標。
    /// </summary>
    /// <returns>每個模型單位的螢幕像素數；還算不出來時回 0。</returns>
    private float PixelsPerUnit()
        => boardView != null ? boardView.SnapSettings.ScreenPixelsPerModelUnit : 0f;

    /// <summary>
    /// 把抽屜、縮圖、箭頭、回收框擺到位置。
    ///
    /// <b>整組掛在一個「跟著相機轉」的父物件底下</b>，所以這裡只要算螢幕座標，
    /// 不必自己處理相機的 roll（互動設計 §2.6：題目旋轉是轉相機）。
    /// </summary>
    /// <param name="dragging">現在是不是正在拖曳。決定箭頭與回收框誰出現。</param>
    private void LayoutOverlay(bool dragging)
    {
        float pixelsPerUnit = PixelsPerUnit();
        Camera camera = Camera.main;

        // 相機還沒準備好或版面還沒算出來的那幾幀，整組先藏起來。
        // ⚠️ 不能硬算——pixelsPerUnit 是 0 的話下面每個除法都會變成 Infinity，
        // 一路寫進 transform 之後 Unity 會丟一堆「invalid position」而且很難追回源頭
        if (pixelsPerUnit <= 0f || camera == null)
        {
            SetOverlayShown(false, false, false);
            return;
        }

        overlayRoot.SetPositionAndRotation(
            camera.transform.position + camera.transform.forward * OverlayCameraDistance,
            camera.transform.rotation);

        // ⚠️ arrow 用 IsArrowShown 而不是 !dragging——顯示條件與 HitTest 用的
        // 必須是同一個算式，不然會出現「箭頭不見了但那塊還是擋著片」
        SetOverlayShown(drawer: IsOpen, arrow: IsArrowShown, recycle: dragging);

        Rect arrow = ArrowRect;
        PlaceBlock(arrowTab, arrow, pixelsPerUnit);
        PlaceBlock(recycleBox, arrow, pixelsPerUnit);

        if (!IsOpen) return;

        Rect drawer = DrawerRect;
        PlaceBlock(drawerPanel, drawer, pixelsPerUnit);
        LayoutThumbnails(drawer, pixelsPerUnit);
    }

    /// <summary>抽屜視覺離相機多遠。正交相機下距離不影響大小，只要在近裁面之外、在片的前面。</summary>
    private const float OverlayCameraDistance = 9f;

    /// <param name="drawer">抽屜在螢幕上的矩形。</param>
    /// <param name="pixelsPerUnit">一個模型單位佔幾像素。</param>
    private void LayoutThumbnails(Rect drawer, float pixelsPerUnit)
    {
        float cellWidth = drawer.width / BoardState.PieceCount;

        // 縮圖要塞得進一格，而最大的片（大三角）跨 4 個模型單位。
        // 用最大的那片決定縮放，七片才會是同一個比例——各自塞滿的話
        // 小三角會被放大到跟大三角一樣，玩家就失去「哪片比較大」的線索
        const float LargestPieceExtentInModelUnits = 4f;
        float thumbnailPixelsPerUnit =
            Mathf.Min(cellWidth, drawer.height) * thumbnailFillRatio / LargestPieceExtentInModelUnits;
        float scale = thumbnailPixelsPerUnit / pixelsPerUnit;

        for (int slotIndex = 0; slotIndex < thumbnails.Length; slotIndex++)
        {
            SpriteRenderer thumbnail = thumbnails[slotIndex];
            PieceView piece = pieces[slotIndex];
            if (thumbnail == null || piece == null) continue;

            bool inTray = !IsCellEmpty(slotIndex);
            thumbnail.enabled = IsOpen && inTray;
            if (!thumbnail.enabled) continue;

            // 縮圖反映片目前的姿態（決定 7 的「角度記憶」要看得見，
            // 不然玩家拖出來才發現它是斜的）
            thumbnail.color = library.ColorFor(slotIndex);
            thumbnail.flipY = piece.IsFlipped;

            Vector2 cellCenter = new Vector2(drawer.x + (slotIndex + 0.5f) * cellWidth,
                                             drawer.y + drawer.height * 0.5f);

            // 片的 pivot 在原型原點（某個頂點），不是視覺中心，所以要補一段才會置中。
            // ⚠️ 補償量跟姿態有關，共用 PieceView 那一份算法——各算一份遲早不一致
            Vector2 centerOffset = piece.PosedCenterOffset * scale;

            thumbnail.transform.localPosition =
                (Vector3)(ScreenToOverlayLocal(cellCenter, pixelsPerUnit) - centerOffset);
            thumbnail.transform.localRotation = Quaternion.Euler(0f, 0f, piece.Rotation * 45f);
            thumbnail.transform.localScale = new Vector3(scale, scale, 1f);
        }
    }

    /// <param name="renderer">要擺的方塊。</param>
    /// <param name="screenRect">它在螢幕上的矩形。</param>
    /// <param name="pixelsPerUnit">一個模型單位佔幾像素。</param>
    private void PlaceBlock(SpriteRenderer renderer, Rect screenRect, float pixelsPerUnit)
    {
        if (renderer == null || !renderer.enabled) return;

        renderer.transform.localPosition =
            ScreenToOverlayLocal(screenRect.center, pixelsPerUnit);
        renderer.transform.localRotation = Quaternion.identity;
        renderer.transform.localScale =
            new Vector3(screenRect.width / pixelsPerUnit, screenRect.height / pixelsPerUnit, 1f);
    }

    /// <summary>
    /// 螢幕座標 → <see cref="overlayRoot"/> 的區域座標。
    ///
    /// 因為那個父物件跟相機同方向、而相機是正交的，所以區域座標的 X/Y
    /// 就是螢幕的右/上，換算只是「減掉畫面中心再除以像素密度」。
    /// <b>刻意不用 <c>Camera.ScreenToWorldPoint</c></b>——理由跟 §2.8 那條一樣：
    /// 相機這一幀可能還沒更新，而這裡要的是「照現在的版面算」的答案。
    /// </summary>
    /// <param name="screenPoint">螢幕座標，原點左下。</param>
    /// <param name="pixelsPerUnit">一個模型單位佔幾像素。</param>
    /// <returns>相對抽屜父物件的區域座標。</returns>
    private static Vector2 ScreenToOverlayLocal(Vector2 screenPoint, float pixelsPerUnit)
        => new Vector2((screenPoint.x - Screen.width * 0.5f) / pixelsPerUnit,
                       (screenPoint.y - Screen.height * 0.5f) / pixelsPerUnit);

    /// <param name="drawer">抽屜本體要不要顯示。</param>
    /// <param name="arrow">箭頭要不要顯示。</param>
    /// <param name="recycle">回收框要不要顯示。</param>
    private void SetOverlayShown(bool drawer, bool arrow, bool recycle)
    {
        if (drawerPanel != null) drawerPanel.enabled = drawer;
        if (arrowTab != null) arrowTab.enabled = arrow;
        if (recycleBox != null) recycleBox.enabled = recycle;

        if (drawer) return;
        foreach (SpriteRenderer thumbnail in thumbnails)
            if (thumbnail != null) thumbnail.enabled = false;
    }

    /// <summary>
    /// 七片的父物件必須是 identity。
    ///
    /// ⚠️ <b>片掛在有旋轉或縮放的父物件底下，<c>transform.position</c> 就不再是
    /// 寫進去的那個值</b>（互動設計 §13.2）——round-trip 失真，吸附會慢慢對不準。
    /// 待辦清單的「<c>PieceView</c> 要不要檢查父物件」那條待決定的是
    /// 「把七片統一掛在一個鎖死的父元件底下，一個物件保證七片，比七個各自驗划算」，
    /// 這裡就是那個父元件。
    ///
    /// 照 <c>BoxView</c> 的作法自己拉回來並警告，而不是只寫在註解裡——
    /// 寫在註解的要求等於沒有保護（除錯記錄 001 的教訓）。
    /// </summary>
    private void EnforcePieceRootIdentity()
    {
        if (pieceRoot == null) return;
        if (pieceRoot.localRotation == Quaternion.identity && pieceRoot.localScale == Vector3.one)
            return;

        Debug.LogWarning("Pieces 這個父物件被加上了旋轉或縮放，已經拉回 identity。" +
                         "片的位置 round-trip 會因此失真，吸附會慢慢對不準（互動設計 §13.2）。", this);
        pieceRoot.localRotation = Quaternion.identity;
        pieceRoot.localScale = Vector3.one;
    }

    private void OnDestroy()
    {
        library?.ReleaseGeneratedSprites();

        // 程序生成的東西不會被 Unity 自動回收，離開 Play 模式時會留在記憶體裡
        if (blockSprite != null) Destroy(blockSprite);
        if (blockTexture != null) Destroy(blockTexture);
    }
}
