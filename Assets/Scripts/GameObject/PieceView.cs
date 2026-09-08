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
/// 旋轉格數、翻面、吸住了沒、正在被拖、被選取了沒（會變）。
///
/// ⚠️ 前四個是<b>真相</b>，「被選取了沒」不是——它的真相在輸入控制器手上
/// （那裡才知道「至多一片」），片上這個只是被推過來畫的快取，跟「吸住了沒」同一個模式。
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

    /// <summary>
    /// 被選取時往白色提亮多少。
    ///
    /// ⚠️ <b>這是佔位用的表現</b>——描邊、外框、發光之後再說。
    /// 換掉它不會動到任何資料流，所以先用最省事的方式讓選取看得見（P1-8）。
    /// </summary>
    private const float SelectedBrightenAmount = 0.45f;

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
    private bool isSelected;

    // 預設看得到。P1-5 的 TrayView 會在第一幀把七片推成「在抽屜裡」，
    // 但**沒有 TrayView 的場景照樣能用**（例如只想看拖曳的除錯場景）
    private bool isVisible = true;

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
    // 這五個忘了刷新是**安靜的** bug——畫面只是有點怪，不會有任何錯誤訊息，
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
            int normalized = Symmetry.Modulo8(value);
            if (normalized == rotation) return;
            ApplyPose(normalized, isFlipped);
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
            ApplyPose(rotation, value);
        }
    }

    /// <summary>
    /// 換姿態，並且**讓視覺中心留在原地**（互動設計 §5）。
    ///
    /// <c>Transform</c> 是繞**原型原點**轉的，而那是某個特定頂點——直接改角度的話，
    /// 片會繞著一個角甩出去，看起來完全不像在原地轉。所以這裡算一下補償：
    /// <code>
    /// 轉之前：中心的世界座標 = 位置 + 套姿態(舊)
    /// 轉之後：位置 = 那個中心 − 套姿態(新)
    /// </code>
    ///
    /// <b>這不是把畫面騙過去。</b>繞中心旋轉，原點本來就會跟著移，
    /// 所以「位置變了」是正確結果。
    ///
    /// ⚠️ <b>吸住時不補償。</b>那時候位置是從精確 <c>Placement</c> 轉來的，
    /// 補了會漂成浮點但 <see cref="IsSnapped"/> 還是 true，兩者就不一致了。
    /// §5 本來就規定旋轉一定發生在浮點狀態（按鈕 = 脫離吸附 → 轉一格 → 重新嘗試吸附），
    /// 所以這個守衛擋不到任何正常流程。
    ///
    /// 補償是浮點運算，連續旋轉會累積誤差——但未吸附時位置本來就是浮點，
    /// 而且**一吸上去就會被精確值整個蓋掉**，所以誤差有界。
    /// </summary>
    /// <param name="newRotation">新的旋轉格數，已經取過模。</param>
    /// <param name="newFlipped">新的翻面狀態。</param>
    private void ApplyPose(int newRotation, bool newFlipped)
    {
        bool flipChanged = newFlipped != isFlipped;
        bool compensate = !isSnapped;
        Vector2 centerWorld = Position + CenterOffsetFor(rotation, isFlipped);

        rotation = newRotation;
        isFlipped = newFlipped;

        if (compensate) Position = centerWorld - CenterOffsetFor(rotation, isFlipped);
        if (flipChanged) RebuildHitArea();      // 碰撞盒只有翻面時要重建
        RefreshVisuals();
    }

    /// <summary>
    /// 視覺中心相對於原型原點的位移，套用指定姿態之後。
    ///
    /// 順序跟模型一致：**先翻面再旋轉**（<c>Symmetry.Apply</c>）。
    /// 翻面與旋轉都是線性變換，所以直接套在中心那一點上就好，不必重算頂點平均。
    /// </summary>
    /// <param name="rotationSteps">旋轉格數 0~7，每格 45°。</param>
    /// <param name="flipped">是否翻面（對 X 軸鏡射）。</param>
    /// <returns>從原型原點指向視覺中心的向量。</returns>
    private Vector2 CenterOffsetFor(int rotationSteps, bool flipped)
    {
        Vector2 center = PieceVertexCache.LocalCenter(Kind);
        if (flipped) center.y = -center.y;

        float radians = rotationSteps * 45f * Mathf.Deg2Rad;
        float cos = Mathf.Cos(radians);
        float sin = Mathf.Sin(radians);
        return new Vector2(center.x * cos - center.y * sin,
                           center.x * sin + center.y * cos);
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

    /// <summary>
    /// 有沒有被選取（互動設計 §4「高亮」）。旋轉／翻面鈕作用的就是被選取的那一片。
    ///
    /// ⚠️ <b>片自己不判斷該不該被選取，也不知道有沒有別片被選。</b>
    /// 「至多一片」的不變式由輸入控制器維護，跟「正在被拖」完全一樣——
    /// 由指揮者推進來，不要讓片自己去問。
    ///
    /// 依 §4 的規則：<b>沒在被拖的時候</b>，被選取的片必定不在盤面上、必定沒吸住。
    /// §5 的軸心補償靠這條不變式才永遠成立——按鈕的顯示條件排除了拖曳中，
    /// 所以按鈕能被按到的每一刻，那片都是浮點的。
    /// </summary>
    public bool IsSelected
    {
        get => isSelected;
        set
        {
            isSelected = value;
            RefreshVisuals();
        }
    }

    /// <summary>
    /// 看得到嗎（P1-5）。<c>false</c> = 收在待用區的抽屜裡：<b>看不到，也點不到</b>。
    ///
    /// ⚠️ <b>這不是真相，是投影。</b>真相是 <c>LevelData.IsInTray(slot)</c>，
    /// 由 <c>TrayView</c> 每幀推過來——跟 <see cref="IsSnapped"/>、<see cref="IsSelected"/>
    /// 同一個模式。片自己不知道抽屜是什麼。
    ///
    /// <b>用關掉渲染與碰撞，不用 <c>SetActive</c></b>：物件要一直活著，
    /// 姿態（<see cref="Rotation"/> / <see cref="IsFlipped"/>）才會留在那裡不被動到——
    /// 「待用區記住上一次的角度」就是靠這個免費成立的，不必另外存一份。
    /// </summary>
    public bool IsVisible
    {
        get => isVisible;
        set
        {
            if (isVisible == value) return;
            isVisible = value;

            // 兩個一起關才是「看不到也點不到」。只關渲染的話片會變成隱形地雷——
            // 玩家點到空白處卻抓到一片看不見的片，而且完全不報錯
            if (spriteRenderer != null) spriteRenderer.enabled = isVisible;
            if (hitArea != null) hitArea.enabled = isVisible;
        }
    }

    /// <summary>吸住了而且沒在被拖 —— 也就是「這個位置已經定案」。決定要不要半透明。</summary>
    public bool IsCommitted => isSnapped && !isBeingDragged;

    /// <summary>
    /// 這片畫在第幾層。**命中測試要用它挑最上面那片**——
    /// <c>Physics2D</c> 完全不看 <c>sortingOrder</c>，回傳順序跟繪製層級無關。
    ///
    /// 每次重算而不是讀 <c>SpriteRenderer</c>：這樣呼叫端不必經過渲染元件，
    /// 而且不會有「刷新之前拿到舊值」的空窗。成本是四個整數運算。
    /// </summary>
    public int SortingOrder => PieceSorting.SortingOrder(Kind, SlotIndex, isSnapped, isBeingDragged);

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

        // ⚠️ 這兩行不能省。IsVisible 的 setter 在 Awake 之前被叫到時會靜靜地什麼都不做
        //（兩個元件還是 null），所以要在拿到元件之後把當下的值補推一次。
        // 少了它，開場設成隱藏的片會看得到也點得到，而且不報錯
        spriteRenderer.enabled = isVisible;
        hitArea.enabled = isVisible;

        RebuildHitArea();
        RefreshVisuals();
    }

    /// <summary>
    /// 目前姿態下，<b>視覺中心相對 transform 原點</b>的位移。
    ///
    /// 用途有二：§5 旋轉的軸心補償（內部用），以及 P1-5 從抽屜召喚時
    /// 「把片的中心對到手指」——後者要的位置是<c>手指座標 − 這個值</c>。
    ///
    /// ⚠️ <b>它會隨姿態改變</b>（片轉過之後中心的相對位置也轉了），所以不能拿原型的來代替。
    /// 開這個屬性就是為了讓召喚那一邊<b>共用同一份算法</b>——各算一份的話兩邊遲早不一致，
    /// 而症狀是「轉過的片從抽屜拿出來會偏一點」，很難聯想到原因。
    /// </summary>
    public Vector2 PosedCenterOffset => CenterOffsetFor(rotation, isFlipped);

    /// <summary>
    /// 把五個欄位算成 Unity 的值並寫進去。
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

            // ⚠️ 提亮要在設定 alpha **之前**做。Lerp 到 Color.white 連 alpha 也會被拉向 1，
            // 順序反過來的話被選取的片會突然變不透明，看起來像「已放置」
            if (isSelected) color = Color.Lerp(color, Color.white, SelectedBrightenAmount);

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
