using Tangram.Core;
using Tangram.Data;
using UnityEngine;

/// <summary>
/// 一片七巧板的顯示（互動設計 §13）。場上七個實例。
///
/// <b>它不認識指揮者、不認識 <c>SnapSolver</c>、不認識 <c>BoardState</c>。</b>
/// 它只被指揮。這條邊界是刻意的：一旦它要自己問吸附器，
/// 就得知道其他六片在哪、盒子在哪——每一片都得認識全世界（§13.10）。
///
/// <b>記住的東西</b>：片種、槽索引（身分，生出來就不變）；
/// 旋轉格數、翻面、吸住了沒、正在被拖（會變，是真相）。
///
/// <b>刻意不記的</b>：
/// <list type="bullet">
/// <item><c>Placement</c> —— <c>BoardState</c> 已經存了，存兩份只會不同步</item>
/// <item>位置 —— 位置就是 <c>transform.position</c>，沒有第二份</item>
/// </list>
///
/// 為什麼位置可以放 <c>transform</c> 但旋轉不行（§13.2）：
/// <b>正方形 <c>rotation = 0</c> 與 <c>= 2</c> 的 transform 完全相同</b>，
/// 兩個不同的模型狀態產生同一個 transform，資訊被銷毀，再好的反推程式也拿不回來。
/// 位置則是浮點寫進去讀出來沒有損失——<b>前提是這片不能掛在有旋轉或縮放的父物件底下</b>，
/// 掛了的話 <c>position</c> 會變成從父物件的矩陣算出來的，round-trip 就不精確了。
/// </summary>
[DisallowMultipleComponent]
[RequireComponent(typeof(SpriteRenderer))]
[RequireComponent(typeof(PolygonCollider2D))]
public class PieceView : MonoBehaviour
{
    /// <summary>沒吸住時的不透明度（互動設計 §2.7「未放置的片顯示為半透明」）。</summary>
    private const float UnsnappedAlpha = 0.55f;

    /// <summary>片種。決定形狀、碰撞盒、旋轉循環長度。生成時設定，之後不變。</summary>
    public PieceKind Kind { get; private set; }

    /// <summary>
    /// 槽索引 0~6，對應 <c>PiecePrototype.TangramSet</c>。生成時設定，之後不變。
    ///
    /// <b><c>Kind</c> 不足以識別一片</b>——大三角有兩片、小三角有兩片。
    /// 這個索引同時決定顏色，也是交給 <c>BoardState.Place</c> 時要報的號碼。
    /// </summary>
    public int SlotIndex { get; private set; }

    private SpriteRenderer spriteRenderer;
    private PolygonCollider2D hitArea;
    private PieceSpriteLibrary library;

    private int rotation;
    private bool isFlipped;
    private bool isSnapped;
    private bool isBeingDragged;

    // ─────────────────────────────────────────────────────────────
    // 位置：直接讀寫 transform，沒有第二份，所以不可能不同步
    // ─────────────────────────────────────────────────────────────

    /// <summary>
    /// 畫面上的位置（世界座標）。模型:世界 = 1:1，所以這也就是模型座標。
    ///
    /// 拖曳中每幀寫這裡。位置忘了寫是**大聲的** bug（片不動，一眼看到），
    /// 所以它不需要下面那層「改完自動刷新」的保險。
    /// </summary>
    public Vector2 Position
    {
        get => transform.position;
        set
        {
            Vector3 current = transform.position;
            transform.position = new Vector3(value.x, value.y, current.z);
        }
    }

    /// <summary>
    /// 吸住了：把精確座標轉成浮點蓋到 transform 上，並把「吸住了」打開。
    ///
    /// <b>這只是畫面。</b>真正交給 <c>BoardState.Place</c> 是放手才做的事（§4），
    /// 所以拖曳過程中 <see cref="IsSnapped"/> 是 true 但盤面上那一槽還沒登記——
    /// <b>這個不一致是刻意的，不要想辦法弭平它</b>（§13.10）。
    /// </summary>
    /// <param name="exactTranslation">吸附解算出來的精確平移量，也就是原型原點該落在哪。</param>
    public void SnapTo(ExactVector2 exactTranslation)
    {
        Position = exactTranslation.ToVector2();
        IsSnapped = true;                       // setter 會順帶刷新
    }

    // ─────────────────────────────────────────────────────────────
    // 姿態與狀態：setter 一定刷新，呼叫端不可能忘記
    //
    // 這四個忘了刷新是**安靜的** bug——畫面只是有點怪，不會有任何錯誤訊息，
    // 所以保險做進 setter，而不是靠呼叫的人記得（§13.3）
    // ─────────────────────────────────────────────────────────────

    /// <summary>
    /// 旋轉格數 0~7，每格 45°。設進來的值會先取模，傳 −1 或 9 都安全。
    ///
    /// <b>這一定要是欄位，不能從 transform 反推</b>（見類別說明）。
    /// P1-8 的旋轉鈕要照片種的循環長度取餘數
    /// （<c>SnapSolver.RotationCycleLength</c>：三角 8 / 正方形 2 / 平行四邊形 4），
    /// 否則玩家會看到「按了沒反應」。
    /// </summary>
    public int Rotation
    {
        get => rotation;
        set
        {
            rotation = Symmetry.Modulo8(value);
            RefreshVisuals();
        }
    }

    /// <summary>
    /// 有沒有翻面。只有平行四邊形需要（<c>SnapSolver.CanFlip</c>），其餘片翻面等同旋轉。
    ///
    /// 模型的翻面是<b>對 X 軸鏡射</b>（y 取負），也就是**垂直**翻轉，
    /// 所以畫面上用 <c>SpriteRenderer.flipY</c>。⚠️ <b>不要用負的縮放</b>——
    /// 負縮放會反轉繞向，背面剔除會把整片吃掉。
    /// </summary>
    public bool IsFlipped
    {
        get => isFlipped;
        set
        {
            if (isFlipped == value) return;
            isFlipped = value;
            RebuildHitArea();                   // 碰撞盒只有這時候要重建
            RefreshVisuals();
        }
    }

    /// <summary>
    /// 畫面上吸住了沒。
    ///
    /// ⚠️ <b>這不是 <c>BoardState[slot].OnBoard</c>。</b>
    /// 拖曳中吸住只是預覽，那時候盤面上還沒登記（§13.10）。
    /// 由指揮者推進來，不要讓片自己去問盤面——問了會拿到錯的答案。
    /// </summary>
    public bool IsSnapped
    {
        get => isSnapped;
        set
        {
            isSnapped = value;
            RefreshVisuals();
        }
    }

    /// <summary>
    /// 是不是正在被拖。只影響排序與透明度，不影響幾何。
    ///
    /// 拖曳中永遠在最上層，否則拖一片小的經過另一片未吸附的大片時會鑽到它下面。
    /// </summary>
    public bool IsBeingDragged
    {
        get => isBeingDragged;
        set
        {
            isBeingDragged = value;
            RefreshVisuals();
        }
    }

    /// <summary>吸住了而且沒在被拖 —— 也就是「這個位置已經定案」。決定要不要半透明。</summary>
    public bool IsCommitted => isSnapped && !isBeingDragged;

    // ─────────────────────────────────────────────────────────────

    private void Awake()
    {
        spriteRenderer = GetComponent<SpriteRenderer>();
        hitArea = GetComponent<PolygonCollider2D>();
    }

    /// <summary>
    /// 生出來之後叫一次，設定身分並套上初始外觀。
    /// </summary>
    /// <param name="kind">片種。</param>
    /// <param name="slotIndex">槽索引 0~6，要跟 <c>PiecePrototype.TangramSet</c> 對得起來。</param>
    /// <param name="spriteLibrary">圖與顏色的來源。七片共用同一個實例，圖才不會生七次。</param>
    public void Initialize(PieceKind kind, int slotIndex, PieceSpriteLibrary spriteLibrary)
    {
        if (spriteRenderer == null) Awake();

        Kind = kind;
        SlotIndex = slotIndex;
        library = spriteLibrary;

        spriteRenderer.sprite = library.SpriteFor(kind);
        RebuildHitArea();
        RefreshVisuals();
    }

    /// <summary>
    /// 把四個欄位算成 Unity 的值並寫進去。
    ///
    /// <b>刻意不碰位置</b>——位置沒有第二份，也就沒有需要同步的東西（§13.4）。
    /// </summary>
    private void RefreshVisuals()
    {
        if (spriteRenderer == null) return;      // Initialize 之前被 setter 叫到

        // 模型的旋轉是逆時針，Unity 繞 Z 軸正向也是逆時針，方向天然一致
        transform.rotation = Quaternion.Euler(0f, 0f, rotation * 45f);

        // 對 X 軸鏡射 = y 取負 = 垂直翻。不是 flipX，也不是負縮放
        spriteRenderer.flipY = isFlipped;

        if (library != null)
        {
            Color color = library.ColorFor(SlotIndex);
            color.a = IsCommitted ? 1f : UnsnappedAlpha;
            spriteRenderer.color = color;
        }

        spriteRenderer.sortingOrder =
            PieceSorting.SortingOrder(Kind, SlotIndex, isSnapped, isBeingDragged);
    }

    /// <summary>
    /// 重建命中範圍。
    ///
    /// 頂點是<b>片自己的座標系</b>：不含旋轉（transform 會轉它）、不含平移
    /// （<c>transform.position</c> 會搬它）。所以**只有翻面狀態改變時要重建**，
    /// 旋轉和移動都不用。
    ///
    /// 用的是滿的原型頂點，而圖是內縮的，所以**可點範圍比看得到的形狀大一圈**。
    /// 這是刻意的：小三角在手機上只有 40～50 像素寬，本來就難點，這一圈剛好補上（§13.5）。
    /// </summary>
    private void RebuildHitArea()
    {
        if (hitArea == null) return;
        hitArea.points = PieceVertexCache.LocalVertices(Kind, isFlipped);
    }
}
