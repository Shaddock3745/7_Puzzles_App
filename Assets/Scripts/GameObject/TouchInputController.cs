using System.Collections.Generic;
using Tangram.Core;
using Tangram.Data;
using UnityEngine;
using UnityEngine.InputSystem;

/// <summary>
/// 拖曳控制（P1-6，互動設計 §4）。按下選取 → 每幀問吸附器 → 放手才交給盤面。
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

    private PieceView dragging;

    /// <summary>
    /// 按下的瞬間「片的原點」與「手指」的差。
    ///
    /// ⚠️ <b>沒有它片會在按下的瞬間跳到手指底下。</b>
    /// <see cref="SnapSolver.Resolve"/> 收的 <c>freePosition</c> 是「片的原點該在哪」，
    /// 不是手指在哪——sprite 的 pivot 就在原型原點，所以兩者差一段。
    /// </summary>
    private Vector2 grabOffset;

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
    }

    private void OnEnable()
    {
        // P1-1 刻意留的洞就是這裡補上的：版面重算會重排暫放區的片，
        // 手指底下那片會瞬間移位、抓取偏移歪掉，所以直接取消這次拖曳
        if (boardView != null) boardView.LayoutRecomputed += CancelDrag;
    }

    private void OnDisable()
    {
        if (boardView != null) boardView.LayoutRecomputed -= CancelDrag;
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

        Vector2 worldPoint = boardView.ScreenToWorld(pointer.position.ReadValue());

        if (dragging == null)
        {
            if (pointer.press.wasPressedThisFrame) BeginDrag(level, worldPoint);
            return;
        }

        if (pointer.press.wasReleasedThisFrame) { EndDrag(level); return; }

        // 沒收到放開事件也要收尾（例如視窗失焦），否則那片會永遠停在拖曳狀態
        if (!pointer.press.isPressed) { EndDrag(level); return; }

        UpdateDrag(level, worldPoint);
    }

    /// <summary>按下：挑一片、記下抓取偏移與還原點。</summary>
    /// <param name="level">這一關的資料。</param>
    /// <param name="worldPoint">手指的世界座標。</param>
    private void BeginDrag(LevelData level, Vector2 worldPoint)
    {
        PieceView piece = TopmostPieceAt(worldPoint);
        if (piece == null) return;

        dragging = piece;
        grabOffset = piece.Position - worldPoint;

        positionBeforeDrag = piece.Position;
        snappedBeforeDrag = piece.IsSnapped;
        onBoardBeforeDrag = level.Board[piece.SlotIndex].OnBoard;

        // 還沒動之前先當作「停在原地、沒吸住」，這樣一按就放開也不會誤觸發 Place
        lastResult = SnapSolver.DragResult.Free(piece.Position);

        piece.IsBeingDragged = true;
    }

    /// <summary>拖曳中：問吸附器，把結果寫回片。<b>不碰盤面。</b></summary>
    /// <param name="level">這一關的資料。</param>
    /// <param name="worldPoint">手指的世界座標。</param>
    private void UpdateDrag(LevelData level, Vector2 worldPoint)
    {
        Vector2 freePosition = worldPoint + grabOffset;

        lastResult = SnapSolver.Resolve(
            dragging.Kind, dragging.Rotation, dragging.IsFlipped, freePosition,
            // ⚠️ 一定要排除自己。拖曳中的片如果還留在清單裡，
            // 每個候選都會跟自己重疊，結果是「永遠吸不上」而且完全不報錯
            level.Board.OnBoardPlacementsExcept(dragging.SlotIndex),
            boardView.SnapSettings,
            currentlySnapped: dragging.IsSnapped,
            extraAnchors: level.BoxRegion.BoundaryNodes,
            isAllowed: placement => level.BoxRegion.Contains(placement));

        if (lastResult.Snapped)
        {
            dragging.SnapTo(lastResult.Placement.Translation);
        }
        else
        {
            dragging.Position = lastResult.FreePosition;
            dragging.IsSnapped = false;
        }
    }

    /// <summary>
    /// 放手：<b>這裡才動盤面</b>（互動設計 §4）。拖曳中吸住只是預覽。
    /// </summary>
    /// <param name="level">這一關的資料。</param>
    private void EndDrag(LevelData level)
    {
        if (lastResult.Snapped)
        {
            level.Board.Place(dragging.SlotIndex, lastResult.Placement);
        }
        else if (onBoardBeforeDrag)
        {
            // 本來在盤上、拖到沒吸住的地方 → 變成暫放，要從盤面拿下來。
            // 漏掉這行的話盤面會留著它舊的位置：畫面上片在盒外、模型認為它還在盒內，
            // 過關判定與中途回饋都會跟著錯，而且不報錯
            level.Board.Lift(dragging.SlotIndex);
        }
        // 沒吸住又本來就不在盤上 = 暫放，純浮點不進 BoardState

        dragging.IsBeingDragged = false;
        dragging = null;
    }

    /// <summary>
    /// 取消進行中的拖曳，把片放回抓起前的狀態。<b>不動盤面。</b>
    ///
    /// <b>有兩個入口，而且必須是同一個動作</b>：
    /// <list type="bullet">
    /// <item>版面重算（<c>BoardView.LayoutRecomputed</c>，這支自己訂閱）</item>
    /// <item>換關（<c>LevelController.EnterLevel</c> 第 5 步會叫這裡）</item>
    /// </list>
    /// 兩邊各寫一次的話遲早有一邊會漏。
    /// </summary>
    public void CancelDrag()
    {
        if (dragging == null) return;

        dragging.Position = positionBeforeDrag;
        dragging.IsSnapped = snappedBeforeDrag;
        dragging.IsBeingDragged = false;
        dragging = null;
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
