using System.Text;
using Tangram.Core;
using Tangram.Data;
using UnityEngine;
using UnityEngine.InputSystem;

/// <summary>
/// 開發期的除錯資訊（P1-12）。畫在畫面左上角。
///
/// <b>存在的理由</b>：模型層的狀態在畫面上完全看不見。
/// 「放手到底有沒有進 <c>BoardState</c>」這種事，沒有這個 overlay 就只能靠
/// 「拿另一片去撞撞看」來間接推測——這張卡本來排在 P1-10 之後，
/// 但沒有它的話，拖曳流程有一半是盲的，所以提前做。
///
/// <b>它絕對不碰任何狀態，只讀。</b> 除錯工具一旦會改東西，它自己就變成 bug 來源。
///
/// <b>為什麼用 <c>OnGUI</c> 而不是 Canvas 上的 Text</b>：零接線。
/// 不用建 UI 物件、不用拉參照、不依賴 Canvas 的設定，掛上去就有字。
/// 它慢、是舊 API，但那對一個 P4 要刪掉的東西完全無所謂。
/// （<c>OnGUI</c> 畫字不受「專案只開新 Input System」影響——那個限制是輸入，不是繪圖。）
/// </summary>
[DisallowMultipleComponent]
public class DebugOverlay : MonoBehaviour
{
    [Tooltip("關掉就不畫。正式版本來就會自動關（見 Awake）。")]
    [SerializeField] private bool showOverlay = true;

    [Tooltip("字級。手機上看不清楚就調大。")]
    [SerializeField] private int fontSize = 14;

    [Tooltip("座標與吸附參數的來源。留空的話 Awake 會自己找。")]
    [SerializeField] private BoardView boardView;

    [Tooltip("拖曳狀態的來源。留空的話 Awake 會自己找。沒有也能跑，只是少一行。")]
    [SerializeField] private TouchInputController dragController;

    [Tooltip("摺疊把手的邊長，單位 dp。抽屜的箭頭是 48 dp（可觸控的最小建議值），這個刻意小一點。")]
    [SerializeField] private float handleSizeDp = 32f;

    [Tooltip("一進場是展開還是收折的。調手感時通常收折，看模型狀態時展開。")]
    [SerializeField] private bool startExpanded = true;

    /// <summary>
    /// 現在是展開還是收折。收折時只畫把手，<see cref="BuildText"/> 連組字串都省下來。
    ///
    /// ⚠️ <b>刻意不存進 <c>PlayerPrefs</c></b>：抽屜也是每場重來（開場一律關著）。
    /// 這種一眼看得出狀態、一下就切回來的東西，記住它反而會變成「我上次明明關了」的困惑。
    /// </summary>
    private bool isExpanded;

    /// <summary>
    /// 註冊給 <see cref="TouchInputController"/> 的擋片區函式，<b>存起來是為了取消註冊時傳同一個</b>——
    /// 每次寫 <c>() =&gt; HandleScreenRect()</c> 都會產生新的委派，那樣就取消不掉了。
    /// </summary>
    private System.Func<Rect> blockingRectProvider;

    /// <summary>在 <c>Update</c> 組好、<c>OnGUI</c> 只負責畫。</summary>
    private string overlayText;

    private GUIStyle labelStyle;
    private readonly StringBuilder builder = new StringBuilder();

    private void Awake()
    {
        if (boardView == null) boardView = FindFirstObjectByType<BoardView>();
        if (dragController == null) dragController = FindFirstObjectByType<TouchInputController>();
        isExpanded = startExpanded;

#if !UNITY_EDITOR && !DEVELOPMENT_BUILD
        // 正式版直接關掉自己。**這樣就不必在 P4 記得刪** ——
        // 「記得拿掉」是靠人的規定，跟這個專案踩過的兩個坑同一類（除錯記錄 001、002）。
        // Development Build 仍然留著，所以在手機上調手感時看得到
        enabled = false;
#endif
    }

    /// <summary>
    /// 把手要登記成擋片區，不然<b>按下會穿過去抓到底下的片</b>——
    /// 那正是這個專案踩過的坑（見 <c>TouchInputController.RegisterPressBlockingArea</c> 的說明）。
    /// </summary>
    private void OnEnable()
    {
        if (dragController == null) return;
        if (blockingRectProvider == null) blockingRectProvider = () => HandleScreenRect();
        dragController.RegisterPressBlockingScreenRect(blockingRectProvider);
    }

    private void OnDisable()
    {
        if (dragController != null) dragController.UnregisterPressBlockingScreenRect(blockingRectProvider);
    }

    private void Update()
    {
        PollHandleTap();

        // ⚠️ OnGUI 一幀會被呼叫好幾次（layout 一次、repaint 一次）。
        // 字串在那裡組會每幀配置好幾份，所以組在這裡、那裡只畫
        overlayText = showOverlay && isExpanded ? BuildText() : null;
    }

    /// <summary>
    /// 把手在螢幕座標的矩形（<b>原點左下</b>，跟 <c>Pointer.position</c> 同一套）。
    ///
    /// ⚠️ <c>OnGUI</c> 的原點在左上，所以畫的時候要翻一次 Y（見 <see cref="HandleGuiRect"/>）。
    /// 兩邊算同一塊地方的兩份程式，一份寫錯就會變成「看得到但按不到」，而且不報錯——
    /// 所以只有這裡定義位置，畫的那邊從這裡換算過去。
    /// </summary>
    /// <returns>把手的矩形。</returns>
    private Rect HandleScreenRect()
    {
        float size = ScreenMetrics.ToPixels(handleSizeDp);
        float margin = ScreenMetrics.ToPixels(HandleMarginDp);
        return new Rect(margin, Screen.height - margin - size, size, size);
    }

    /// <param name="screenRect">螢幕座標的矩形（原點左下）。</param>
    /// <returns>同一塊地方在 <c>OnGUI</c> 座標的矩形（原點左上）。</returns>
    private static Rect HandleGuiRect(Rect screenRect)
        => new Rect(screenRect.x, Screen.height - screenRect.yMax, screenRect.width, screenRect.height);

    /// <summary>把手與畫面邊緣的距離，單位 dp。</summary>
    private const float HandleMarginDp = 6f;

    /// <summary>
    /// 按在把手上就開合。
    ///
    /// ⚠️ <b>用 <c>Pointer.current</c> 就夠了</b>，不像 <c>TouchInputController</c> 要鎖 touch id——
    /// 那支要追整段拖曳，所以第二根手指會咬它；這裡只認「按下的那一瞬間」，
    /// 而開發者不會用兩根手指去按一個除錯按鈕。
    /// </summary>
    private void PollHandleTap()
    {
        if (!showOverlay) return;

        Pointer pointer = Pointer.current;
        if (pointer == null || !pointer.press.wasPressedThisFrame) return;

        if (HandleScreenRect().Contains(pointer.position.ReadValue())) isExpanded = !isExpanded;
    }

    private void OnGUI()
    {
        if (!showOverlay) return;
        DrawHandle();
        if (string.IsNullOrEmpty(overlayText)) return;

        // GUIStyle 只能在 OnGUI 裡安全地建（GUI.skin 在別的時機無效）
        if (labelStyle == null)
        {
            labelStyle = new GUIStyle(GUI.skin.label)
            {
                alignment = TextAnchor.UpperLeft,
                richText = false
            };
            labelStyle.normal.textColor = Color.white;
        }
        labelStyle.fontSize = fontSize;

        var content = new GUIContent(overlayText);
        Vector2 size = labelStyle.CalcSize(content);

        const float padding = 6f;
        // 面板接在把手底下，跟把手切齊——抽屜也是「把手在外、本體從把手長出來」
        Rect handle = HandleGuiRect(HandleScreenRect());
        var box = new Rect(handle.x, handle.yMax + ScreenMetrics.ToPixels(HandleMarginDp),
                           size.x + padding * 2f, size.y + padding * 2f);

        Color previous = GUI.color;
        GUI.color = new Color(0f, 0f, 0f, 0.65f);
        GUI.DrawTexture(box, Texture2D.whiteTexture);
        GUI.color = previous;

        GUI.Label(new Rect(box.x + padding, box.y + padding, size.x, size.y), content, labelStyle);
    }

    /// <summary>
    /// 畫把手：一個小方塊加一個箭頭，展開時朝上（收起來）、收折時朝下（打開）。
    /// 跟抽屜的箭頭同一個意思——<b>箭頭指的是「按下去會往哪邊變」</b>。
    /// </summary>
    private void DrawHandle()
    {
        Rect handle = HandleGuiRect(HandleScreenRect());

        Color previous = GUI.color;
        GUI.color = new Color(0f, 0f, 0f, isExpanded ? 0.65f : 0.45f);
        GUI.DrawTexture(handle, Texture2D.whiteTexture);
        GUI.color = previous;

        if (handleStyle == null)
        {
            handleStyle = new GUIStyle(GUI.skin.label)
            {
                alignment = TextAnchor.MiddleCenter,
                richText = false
            };
            handleStyle.normal.textColor = Color.white;
        }
        handleStyle.fontSize = Mathf.RoundToInt(handle.height * 0.55f);

        GUI.Label(handle, isExpanded ? "▲" : "▼", handleStyle);
    }

    /// <summary>把手上那個箭頭的樣式。跟 <see cref="labelStyle"/> 一樣只能在 <c>OnGUI</c> 裡建。</summary>
    private GUIStyle handleStyle;

    /// <returns>這一幀要顯示的文字。全部唯讀，不會改到任何狀態。</returns>
    private string BuildText()
    {
        builder.Clear();

        LevelData level = LevelSession.Current?.Data;
        if (level == null)
        {
            builder.Append("關卡    尚未載入");
            return builder.ToString();
        }

        BoardState board = level.Board;
        builder.AppendLine($"盤面    {board.OnBoardCount}/{BoardState.PieceCount} 已放置");

        // 第一層中途回饋的不變式（互動設計 §8）：已放置片的面積和。
        // 七片放滿時必為 32，因為那就是盒子的面積
        builder.AppendLine($"面積    {PlacedArea(board):0.##} / 32");

        AppendDragLine(level);
        AppendSelectionLine(level);

        BoundingBox bounds = level.BoxRegion.Bounds;
        double boxWidth = (bounds.Maximum.X - bounds.Minimum.X).ToDouble();
        double boxHeight = (bounds.Maximum.Y - bounds.Minimum.Y).ToDouble();
        builder.AppendLine($"盒子    {boxWidth:0.##} × {boxHeight:0.##}   環 {level.BoxRegion.Rings.Length}   origin {level.BoxOrigin}");

        builder.AppendLine($"關卡    {(level.IsSolved ? "已過關" : "進行中")}   提示 {level.HintsUsed}/3");

        // P2-6：難度值。⚠️ 這裡只是「現在看得到它」——正式的顯示位置是 P2-15 的監控面板，
        // 而這個 overlay 在正式版會自動關掉
        builder.AppendLine($"難度    {level.Difficulty}   （1200 是基準批次的平均，一個標準差 200）");

        // P2-13：局內統計。⚠️ 前景要用 ForegroundSecondsAt 不是 ForegroundSeconds——
        // 後者只在「停止」時才累加，玩的時候一直是 0，看了會以為計時壞了
        double foreground = level.ForegroundSecondsAt(GameClock.NowSeconds);
        builder.AppendLine(
            $"時間    前景 {foreground:F1}s　拖曳 {level.DragSeconds:F1}s（{level.DragCount} 次）" +
            $"　思考 {level.ThinkingSecondsAt(GameClock.NowSeconds):F1}s");
        builder.AppendLine(
            $"局內    清空 {level.ClearCount} 次　提示 {level.HintsUsed} 次" +
            $"　{(level.IsAbandoned ? "⚠️ 已放棄" : level.IsSolved ? "已過關" : "進行中")}");

        AppendCameraLine();
        AppendPointerLine();

        return builder.ToString();
    }

    /// <param name="level">這一關的資料。</param>
    private void AppendDragLine(LevelData level)
    {
        PieceView pressed = dragController != null ? dragController.PressedPiece : null;
        if (pressed == null)
        {
            builder.AppendLine("指標    —");
            return;
        }

        // 「按住」與「拖曳」要分開印（互動設計 §4）。門檻內的那幾幀片一動也不動，
        // 沒有這一格的話會以為是輸入沒反應
        string phase = dragController.IsDragging ? "拖曳" : "按住";

        // 「畫面上吸住了」與「盤面登記了」刻意會在拖曳中不一致（互動設計 §13.10），
        // 所以兩個都印出來——不然那個差異在畫面上完全看不出來。
        // ⚠️ 這一格也是驗「點一下不會偷偷 Lift」最快的地方：點一下已放置的片，
        // 盤面那個字不該從「有」變「無」
        bool onBoard = level.Board[pressed.SlotIndex].OnBoard;
        builder.AppendLine($"{phase}    槽{pressed.SlotIndex} {pressed.Kind} " +
                           $"rot{pressed.Rotation}{(pressed.IsFlipped ? " 翻" : "")}   " +
                           $"畫面{(pressed.IsSnapped ? "吸住" : "自由")} / 盤面{(onBoard ? "有" : "無")}");
    }

    /// <summary>
    /// 被選取（高亮）的是哪一片。
    ///
    /// ⚠️ 依互動設計 §4，<b>沒在被拖的時候</b>被選取的片必定不在盤面上。
    /// 所以這一行同時是那條不變式的檢查點——沒在拖卻印出「盤面有」就代表規則破了，
    /// 而畫面上只會表現成「按旋轉鈕的時候片飛出去」，離原因很遠。
    ///
    /// <b>拖曳中「盤面有」是正常的</b>：拿起已放置的片開始拖，盤面要到放手才把它 <c>Lift</c>。
    /// 所以警告要扣掉這種情況，不然它每次拖都亮，就沒人看了。
    /// </summary>
    /// <param name="level">這一關的資料。</param>
    private void AppendSelectionLine(LevelData level)
    {
        PieceView selected = dragController != null ? dragController.SelectedPiece : null;
        if (selected == null)
        {
            builder.AppendLine("選取    —");
            return;
        }

        bool onBoard = level.Board[selected.SlotIndex].OnBoard;
        bool violated = onBoard && !selected.IsBeingDragged;
        builder.AppendLine($"選取    槽{selected.SlotIndex} {selected.Kind} " +
                           $"rot{selected.Rotation}{(selected.IsFlipped ? " 翻" : "")}   " +
                           $"盤面{(onBoard ? "有" : "無")}{(violated ? "  ⚠️不變式破了" : "")}");
    }

    private void AppendCameraLine()
    {
        if (boardView == null || !boardView.Solution.IsValid)
        {
            builder.AppendLine("相機    版面解無效");
            return;
        }

        ViewportSolution solution = boardView.Solution;
        SnapSolver.Settings settings = boardView.SnapSettings;
        builder.AppendLine(
            $"相機    ppu {solution.ScreenPixelsPerModelUnit:0.#}   " +
            $"size {solution.OrthographicSize:0.##}   roll {solution.QuarterTurns * 90}°");
        builder.AppendLine(
            $"吸附    {settings.SnapRadiusPixels:0}px / 脫離 {settings.ReleaseRadiusPixels:0}px   " +
            $"= {settings.SnapRadiusPixels / Mathf.Max(settings.ScreenPixelsPerModelUnit, 1e-6f):0.###} 模型單位");
    }

    private void AppendPointerLine()
    {
        Pointer pointer = Pointer.current;
        if (pointer == null || boardView == null || !boardView.Solution.IsValid)
        {
            builder.Append("手指    —");
            return;
        }

        Vector2 world = boardView.ScreenToWorld(pointer.position.ReadValue());
        builder.Append($"手指    ({world.x:0.##}, {world.y:0.##})");
    }

    /// <summary>
    /// 已放置片的面積和。片的面積只有 2 / 4 / 8 三種，全部放滿是 32。
    /// </summary>
    /// <param name="board">盤面。</param>
    /// <returns>面積和。</returns>
    private static double PlacedArea(BoardState board)
    {
        Scalar doubleArea = Scalar.Zero;
        for (int slotIndex = 0; slotIndex < BoardState.PieceCount; slotIndex++)
        {
            if (!board[slotIndex].OnBoard) continue;
            doubleArea = doubleArea + PiecePrototype.Get(board[slotIndex].Kind).DoubleArea;
        }
        return doubleArea.ToDouble() / 2.0;
    }
}
