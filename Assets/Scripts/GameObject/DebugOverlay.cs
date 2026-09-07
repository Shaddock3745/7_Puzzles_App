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

    /// <summary>在 <c>Update</c> 組好、<c>OnGUI</c> 只負責畫。</summary>
    private string overlayText;

    private GUIStyle labelStyle;
    private readonly StringBuilder builder = new StringBuilder();

    private void Awake()
    {
        if (boardView == null) boardView = FindFirstObjectByType<BoardView>();
        if (dragController == null) dragController = FindFirstObjectByType<TouchInputController>();

#if !UNITY_EDITOR && !DEVELOPMENT_BUILD
        // 正式版直接關掉自己。**這樣就不必在 P4 記得刪** ——
        // 「記得拿掉」是靠人的規定，跟這個專案踩過的兩個坑同一類（除錯記錄 001、002）。
        // Development Build 仍然留著，所以在手機上調手感時看得到
        enabled = false;
#endif
    }

    private void Update()
    {
        // ⚠️ OnGUI 一幀會被呼叫好幾次（layout 一次、repaint 一次）。
        // 字串在那裡組會每幀配置好幾份，所以組在這裡、那裡只畫
        overlayText = showOverlay ? BuildText() : null;
    }

    private void OnGUI()
    {
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

        const float margin = 8f;
        const float padding = 6f;
        var box = new Rect(margin, margin, size.x + padding * 2f, size.y + padding * 2f);

        Color previous = GUI.color;
        GUI.color = new Color(0f, 0f, 0f, 0.65f);
        GUI.DrawTexture(box, Texture2D.whiteTexture);
        GUI.color = previous;

        GUI.Label(new Rect(box.x + padding, box.y + padding, size.x, size.y), content, labelStyle);
    }

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
