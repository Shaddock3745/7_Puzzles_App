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
/// ⚠️ <b>它不動片的姿態、位置、吸附、選取。</b>這裡只寫 <see cref="PieceView.IsVisible"/>。
///
/// ⚠️ 這裡原本寫「兩邊的欄位完全不重疊」——<b>那是錯的</b>（2026-09-09 更正）：
/// 控制器也寫 <c>IsVisible</c>（召喚、回收、undo 與清空的收尾）。
/// <b>真正的差別是真相在哪</b>：這支的真相是 <c>LevelData.IsInTray</c>，它只是每幀投影；
/// <c>PiecePreview</c> 的真相是它自己的 Inspector 滑桿，那才是第二份真相（除錯記錄 003）。
///
/// <b>它不認識 <c>TouchInputController</c></b>（2026-09-09）。原本有三條邊
///（拖曳中沒、過關沒、清選取），現在前兩個收進 <see cref="SharedUiState"/> 的三態、
/// 第三個歸控制器自己做，所以那個雙向依賴斷成單向了。
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


    [Tooltip("抽屜開著時佔畫面高度的比例。")]
    [Range(0.1f, 0.5f)]
    [SerializeField] private float drawerHeightFraction = 0.22f;

    [Tooltip("箭頭（也是回收框）的邊長，單位 dp。48 dp 是 Android 建議的最小可觸控尺寸。")]
    [SerializeField] private float arrowSizeDp = 56f;

    [Tooltip("箭頭方塊上的「▲／▼」與回收框上的「×」佔方塊邊長的比例。")]
    [Range(0.2f, 0.9f)]
    [SerializeField] private float glyphFillRatio = 0.5f;

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

    /// <summary>
    /// 箭頭方塊上的三角形：抽屜關著朝上（按下去往上長出來）、開著朝下（按下去收回去）。
    /// 跟 <c>DebugOverlay</c> 把手的意思一樣——<b>箭頭指的是按下去會往哪邊變</b>。
    /// </summary>
    private SpriteRenderer arrowGlyph;

    /// <summary>回收框上的「×」：拖到這裡放手，片會回抽屜。</summary>
    private SpriteRenderer recycleGlyph;

    /// <summary>
    /// 兩個符號的圖，<b>執行時生成</b>，不匯入美術資源（跟方塊、片的圖同一個理由，互動設計 §13.7）。
    ///
    /// ⚠️ <b>刻意不照 <c>DebugOverlay</c> 用 <c>OnGUI</c> 畫文字</b>（2026-09-14）：<c>OnGUI</c> 不吃 sorting layer，
    /// 永遠疊在最上面——過關／放棄彈窗、回饋場景的滿版面板打開時，右下角還會浮著一個箭頭，而且不報錯。
    /// DebugOverlay 可以那樣做，是因為它本來就該在最上面，而且正式版會被關掉。
    /// </summary>
    private Sprite triangleSprite;
    private Texture2D triangleTexture;
    private Sprite crossSprite;
    private Texture2D crossTexture;

    /// <summary>程序生成的 1×1 白色 sprite，抽屜／箭頭／回收框都用它加 tint。</summary>
    private Sprite blockSprite;
    private Texture2D blockTexture;

    private void Awake()
    {
        if (boardView == null) boardView = FindFirstObjectByType<BoardView>();

        if (boardView == null)
            Debug.LogError("TrayView 找不到 BoardView：算不出「一個模型單位佔幾像素」，" +
                           "抽屜與縮圖不會出現在正確的位置。", this);

        BuildBlockSprite();
        BuildGlyphSprites();
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

        // 顏色是佔位的，美術往後放。只求在方塊上看得清楚：淺灰方塊配深色、半透明紅框配白色
        arrowGlyph = BuildBlock("ArrowGlyph", GlyphSortingOrder, new Color(0.15f, 0.16f, 0.2f, 1f));
        arrowGlyph.sprite = triangleSprite;
        recycleGlyph = BuildBlock("RecycleGlyph", GlyphSortingOrder, new Color(1f, 1f, 1f, 0.9f));
        recycleGlyph.sprite = crossSprite;
    }

    /// <summary>符號貼圖的邊長（像素）。放大到方塊一半大時仍然夠平滑。</summary>
    private const int GlyphTextureSize = 64;

    /// <summary>
    /// 生成兩張符號貼圖：朝上的三角形、叉叉。白色，顏色由 <c>SpriteRenderer.color</c> 決定。
    ///
    /// <b>邊緣做反鋸齒</b>：每個像素算「離形狀邊界幾個像素」，換成 0 ～ 1 的透明度。
    /// 不做的話縮放之後邊緣是一格一格的鋸齒。
    ///
    /// <c>pixelsPerUnit</c> 設成貼圖邊長，所以一張圖是 1 個世界單位——<c>localScale</c> 直接就是大小。
    /// </summary>
    private void BuildGlyphSprites()
    {
        // 朝上的三角形，逆時針。上下邊界對稱於 0.5（0.19 ～ 0.81），轉 180° 變成朝下時才不會上下跳一格。
        // 留邊是為了反鋸齒那半個像素不被裁掉
        Vector2[] triangle = { new Vector2(0.12f, 0.19f), new Vector2(0.88f, 0.19f), new Vector2(0.5f, 0.81f) };
        triangleTexture = BuildGlyphTexture("TriangleGlyph", pixel => ConvexCoverage(pixel, triangle));

        // 叉叉：兩條粗線
        const float HalfThickness = 0.09f;
        crossTexture = BuildGlyphTexture("CrossGlyph", pixel => Mathf.Max(
            StrokeCoverage(pixel, new Vector2(0.2f, 0.2f), new Vector2(0.8f, 0.8f), HalfThickness),
            StrokeCoverage(pixel, new Vector2(0.2f, 0.8f), new Vector2(0.8f, 0.2f), HalfThickness)));

        triangleSprite = Sprite.Create(triangleTexture, new Rect(0f, 0f, GlyphTextureSize, GlyphTextureSize),
                                       new Vector2(0.5f, 0.5f), pixelsPerUnit: GlyphTextureSize);
        crossSprite = Sprite.Create(crossTexture, new Rect(0f, 0f, GlyphTextureSize, GlyphTextureSize),
                                    new Vector2(0.5f, 0.5f), pixelsPerUnit: GlyphTextureSize);
    }

    /// <param name="textureName">貼圖名稱，只影響除錯時好不好認。</param>
    /// <param name="coverageOf">給像素中心（0 ～ 1 的正規化座標），回傳 0 ～ 1 的覆蓋率。</param>
    /// <returns>白色、透明度照覆蓋率的貼圖。</returns>
    private static Texture2D BuildGlyphTexture(string textureName, System.Func<Vector2, float> coverageOf)
    {
        var texture = new Texture2D(GlyphTextureSize, GlyphTextureSize, TextureFormat.RGBA32, mipChain: false)
        {
            name = textureName,
            wrapMode = TextureWrapMode.Clamp,
            filterMode = FilterMode.Bilinear,
        };

        var pixels = new Color32[GlyphTextureSize * GlyphTextureSize];
        for (int y = 0; y < GlyphTextureSize; y++)
            for (int x = 0; x < GlyphTextureSize; x++)
            {
                var center = new Vector2((x + 0.5f) / GlyphTextureSize, (y + 0.5f) / GlyphTextureSize);
                byte alpha = (byte)Mathf.RoundToInt(Mathf.Clamp01(coverageOf(center)) * 255f);
                pixels[y * GlyphTextureSize + x] = new Color32(255, 255, 255, alpha);
            }

        texture.SetPixels32(pixels);
        texture.Apply();
        return texture;
    }

    /// <summary>
    /// 凸多邊形（逆時針）對一個點的覆蓋率。取「離每條邊的有號距離」的最小值——在裡面是正的——
    /// 換成像素之後加 0.5，邊界上剛好是半透明。
    /// </summary>
    /// <param name="point">正規化座標。</param>
    /// <param name="vertices">逆時針的頂點，正規化座標。</param>
    /// <returns>0 ～ 1（還沒夾）。</returns>
    private static float ConvexCoverage(Vector2 point, Vector2[] vertices)
    {
        float nearest = float.MaxValue;
        for (int i = 0; i < vertices.Length; i++)
        {
            Vector2 start = vertices[i];
            Vector2 edge = (vertices[(i + 1) % vertices.Length] - start).normalized;
            Vector2 toPoint = point - start;

            // 二維外積：逆時針時，點在邊的左邊（多邊形裡面）是正的
            float signedDistance = edge.x * toPoint.y - edge.y * toPoint.x;
            if (signedDistance < nearest) nearest = signedDistance;
        }
        return nearest * GlyphTextureSize + 0.5f;
    }

    /// <summary>一條有粗細的線段對一個點的覆蓋率。</summary>
    /// <param name="point">正規化座標。</param>
    /// <param name="from">線段起點。</param>
    /// <param name="to">線段終點。</param>
    /// <param name="halfThickness">半個線寬（正規化單位）。</param>
    /// <returns>0 ～ 1（還沒夾）。</returns>
    private static float StrokeCoverage(Vector2 point, Vector2 from, Vector2 to, float halfThickness)
    {
        Vector2 segment = to - from;
        float along = Mathf.Clamp01(Vector2.Dot(point - from, segment) / segment.sqrMagnitude);
        float distance = Vector2.Distance(point, from + segment * along);
        return (halfThickness - distance) * GlyphTextureSize + 0.5f;
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
    private const int DrawerSortingOrder = PieceSorting.TrayDrawerSortingOrder;
    private const int ThumbnailSortingOrder = PieceSorting.TrayThumbnailSortingOrder;
    private const int OverlayTopSortingOrder = PieceSorting.TrayOverlaySortingOrder;
    private const int GlyphSortingOrder = PieceSorting.TrayOverlayGlyphSortingOrder;

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
            // dp → 螢幕像素每次重算，跟三塊矩形一樣不快取（見上面那段說明）
            float size = ScreenMetrics.ToPixels(arrowSizeDp);
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
        if (CornerRole == TrayCornerRole.Arrow && ArrowRect.Contains(screenPoint)) return Hit.Arrow;

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
    /// 右下角那一塊現在是什麼角色。<b>顯示與命中共用同一個答案</b>——
    /// 分岔的話會出現「看得到但按了沒反應」或反過來，兩種都很難查。
    ///
    /// ⚠️ <b>2026-09-09 之前這是兩個平行布林</b>（箭頭顯不顯示、回收框顯不顯示），
    /// 而且各自去問控制器 <c>IsDragging</c> / <c>IsLevelComplete</c>。
    /// 那有兩個問題：兩個布林表達三個互斥狀態（違反決定 9），
    /// 而且它是<b>抽屜認識控制器</b>的來源。現在規則住在 <see cref="SharedUiState"/>，
    /// 這支只讀答案。
    ///
    /// ⚠️ <b>角色不看抽屜開著沒</b>。抽屜開著時箭頭必須還按得到，不然關不起來——
    /// 這是它跟四顆鈕唯一不同的地方。
    ///
    /// store 還沒設好時退回 <see cref="TrayCornerRole.Arrow"/>：那是「什麼都還沒發生」
    /// 時該有的樣子，也讓場景單獨跑（沒有 <c>LevelController</c>）時抽屜仍然打得開。
    /// </summary>
    private TrayCornerRole CornerRole =>
        SharedUiState.Current?.TrayCorner ?? TrayCornerRole.Arrow;

    /// <summary>拖曳中才顯示的回收框。放手時由控制器問一次。</summary>
    /// <param name="screenPoint">手指的螢幕座標。</param>
    /// <returns>手指在不在回收框裡。</returns>
    public bool IsOverRecycleArea(Vector2 screenPoint)
        => CornerRole == TrayCornerRole.RecycleBox && ArrowRect.Contains(screenPoint);

    /// <summary>
    /// 開或關抽屜。控制器在分類到 <see cref="Hit.Arrow"/> 時叫這裡。
    ///
    /// <b>打開的當下清掉選取</b>（決定 17）：抽屜整個蓋住盤面，
    /// 留著選取的話旋轉／翻面鈕會被蓋在抽屜底下，變成「看得到但按不到」。
    /// </summary>
    public void Toggle()
    {
        IsOpen = !IsOpen;
        // ⚠️ 清選取**不在這裡**（2026-09-09 搬走）。決定要開抽屜的是控制器，
        // 由它順手清才是對的歸屬——而且那原本是這支認識控制器的三條邊之一
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

        // ⚠️ 自動收折**不在這裡**（2026-09-09 搬走）。原本是每幀看到「有在拖」就 Close()，
        // 把一個**一次性動作**寫成了每幀重複執行的冪等操作。
        // 它現在是控制器「跨過門檻那一格」的第 2 件事，跟 §15.4 的敘述一致
        LayoutOverlay();
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
    private void LayoutOverlay()
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

        // ⚠️ 兩個都從**同一個三態**推出來，不是兩個各自算的布林——
        // 這樣「箭頭與回收框同時顯示」在形狀上就不可能，而不是靠算式互相排除
        TrayCornerRole role = CornerRole;
        SetOverlayShown(drawer: IsOpen,
                        arrow: role == TrayCornerRole.Arrow,
                        recycle: role == TrayCornerRole.RecycleBox);

        Rect arrow = ArrowRect;
        PlaceBlock(arrowTab, arrow, pixelsPerUnit);
        PlaceBlock(recycleBox, arrow, pixelsPerUnit);

        // 符號擺在方塊正中央。三角形：關著朝上、開著朝下（按下去會往哪邊變）
        PlaceGlyph(arrowGlyph, arrow, pixelsPerUnit, IsOpen ? 180f : 0f);
        PlaceGlyph(recycleGlyph, arrow, pixelsPerUnit, 0f);

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

    /// <summary>把一個符號擺在方塊正中央，大小是方塊短邊乘上 <see cref="glyphFillRatio"/>。</summary>
    /// <param name="renderer">符號。</param>
    /// <param name="blockScreenRect">它所在的方塊在螢幕上的矩形。</param>
    /// <param name="pixelsPerUnit">一個模型單位佔幾像素。</param>
    /// <param name="angleDegrees">繞 Z 轉幾度（0 = 圖原本的方向）。</param>
    private void PlaceGlyph(SpriteRenderer renderer, Rect blockScreenRect, float pixelsPerUnit, float angleDegrees)
    {
        if (renderer == null || !renderer.enabled) return;

        float size = Mathf.Min(blockScreenRect.width, blockScreenRect.height) * glyphFillRatio / pixelsPerUnit;
        renderer.transform.localPosition = ScreenToOverlayLocal(blockScreenRect.center, pixelsPerUnit);
        renderer.transform.localRotation = Quaternion.Euler(0f, 0f, angleDegrees);
        renderer.transform.localScale = new Vector3(size, size, 1f);
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

        // 符號跟著它的方塊，條件一模一樣——分開算的話會出現「方塊不見了但箭頭還浮著」
        if (arrowGlyph != null) arrowGlyph.enabled = arrow;
        if (recycleGlyph != null) recycleGlyph.enabled = recycle;

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
        if (triangleSprite != null) Destroy(triangleSprite);
        if (triangleTexture != null) Destroy(triangleTexture);
        if (crossSprite != null) Destroy(crossSprite);
        if (crossTexture != null) Destroy(crossTexture);
    }
}
