using System.Text;
using Tangram.Core;
using Tangram.Data;
using UnityEngine;
using UnityEngine.InputSystem;

/// <summary>
/// ⭐ <b>App 裡的監控面板</b>（issue #38，2026-10-23）：左上角一個把手，按一下展開、再按收起，展開後上面一排分頁。
/// 跟 Editor 的監控面板（<c>MonitorWindow</c>）<b>同一份資料、同一份字</b>——報告是 <see cref="LevelMonitor"/> 組的，
/// 每一個字是 <see cref="LevelMonitorText"/> 組的，這支只負責「畫在哪、按哪裡換頁」。
///
/// <b>名字沿用 <c>DebugOverlay</c></b>（2026-10-23 取代原本的除錯 overlay）：改名的話場景裡的元件要重接，跟 <c>ScanWaitingCover</c> 同一個理由。
/// 原本那幾行除錯資訊（盤面、拖曳、選取不變式、相機與吸附的像素、手指位置）<b>留著但預設不顯示</b>——
/// 勾 <see cref="showDebugPage"/> 就多一個「除錯」分頁，在 T20 上調手感時用得到。
///
/// <b>什麼時候出現</b>（2026-10-23 定）：<b>展示版</b>（<see cref="DemonstrationBuild"/>）——Editor、Development Build，或 <b>Player Settings 的 Scripting Define Symbols 有 <c>TANGRADE_MONITOR</c></b>。
/// 不只認 Development Build 的理由：它會連作弊鈕（<c>DebugTools</c> 的「填 6 片」）與浮水印一起帶出來，而展示用的 build 要看得到面板、不能有作弊鈕。
/// 其他 build 直接關掉自己（不靠人記得刪，跟原本的除錯 overlay 同一個原則）。
///
/// <b>它絕對不碰任何狀態，只讀。</b> 除錯工具一旦會改東西，它自己就變成 bug 來源。
///
/// <b>為什麼用 <c>OnGUI</c> 而不是 Canvas</b>：零接線，掛上去就有字。<b>按哪裡</b>用 <c>Pointer.current</c> 自己比矩形，不用 GUI 的按鈕——
/// 跟原本的把手同一套，手機上不用賭 IMGUI 收不收得到觸控。<b>為什麼分頁不捲動</b>：IMGUI 的捲動在手機上用手指拖不動。
///
/// ⚠️ <b>擋片區只登記把手與分頁那一排</b>，字的那一塊不擋——面板開著也要能拼（看著算式玩），點在字上就是點在盤面上。
/// ⭐ 但 <b>UI 按鈕整塊都擋</b>（2026-10-30）：OnGUI 擋不住 UI 事件，原本點分頁會順便按到底下的放棄／提示／返回，
/// 所以另外蓋一塊透明的 UI 擋板跟著面板走（<see cref="EnsureButtonBlocker"/>）。拖片與按鈕是兩套命中測試，這裡刻意一套擋、一套不擋。
/// </summary>
[DisallowMultipleComponent]
public class DebugOverlay : MonoBehaviour
{
    /// <summary>分頁。前四頁跟 <see cref="MonitorPage"/> 一一對應，最後一頁是舊的除錯資訊。</summary>
    private enum OverlayPage
    {
        Puzzle,
        Round,
        History,
        Settings,
        Debug,
    }

    /// <summary>分頁上的字，順序跟 <see cref="OverlayPage"/> 一樣。</summary>
    private static readonly string[] PageTitles = { "這一題", "這一局", "走勢", "參數", "除錯" };

    [Tooltip("關掉就不畫。不該出現的 build 本來就會自動關（見 Awake）。")]
    [SerializeField] private bool showOverlay = true;

    [Tooltip("⭐ 舊的除錯資訊（盤面、拖曳、選取不變式、相機與吸附的像素、手指位置）要不要多一頁。預設不顯示，調手感時再打開。")]
    [SerializeField] private bool showDebugPage = false;

    [Tooltip("字級，單位 dp。")]
    [SerializeField] private float fontSizeDp = 11f;

    [Tooltip("走勢最多列幾局（最新的那幾局）。")]
    [Min(1)]
    [SerializeField] private int maximumHistoryRows = 8;

    [Tooltip("報告與權重的來源。留空的話 Awake 會自己找。")]
    [SerializeField] private LevelController levelController;

    [Tooltip("除錯頁的座標與吸附參數來源。留空的話 Awake 會自己找。")]
    [SerializeField] private BoardView boardView;

    [Tooltip("擋片區的登記對象，也是除錯頁的拖曳狀態來源。留空的話 Awake 會自己找。")]
    [SerializeField] private TouchInputController dragController;

    [Tooltip("把手與分頁的高度，單位 dp。")]
    [SerializeField] private float handleSizeDp = 32f;

    [Tooltip("分頁的寬度，單位 dp。螢幕放不下時會自動縮。")]
    [SerializeField] private float tabWidthDp = 64f;

    [Tooltip("一進場是展開還是收起的。")]
    [SerializeField] private bool startExpanded = false;

    /// <summary>把手、分頁與畫面邊緣的距離，單位 dp。</summary>
    private const float MarginDp = 6f;

    /// <summary>字幾秒重組一次。數字是給人看的，一秒 10 次跟 Editor 的面板一樣。</summary>
    private const float RefreshIntervalSeconds = 0.1f;

    /// <summary>
    /// 現在是展開還是收起。⚠️ <b>刻意不存進 <c>PlayerPrefs</c></b>：一眼看得出狀態、一下就切回來的東西，記住它反而會變成「我上次明明關了」的困惑。
    /// </summary>
    private bool isExpanded;

    private OverlayPage page = OverlayPage.Puzzle;

    /// <summary>報告的資料那半，持有難度拆解的快取（換關才重算）。</summary>
    private readonly LevelMonitor monitor = new LevelMonitor();

    /// <summary>註冊給擋片區的函式，存起來是為了取消註冊時傳同一個（每次寫 lambda 都是新的委派）。</summary>
    private System.Func<Rect> blockingRectProvider;

    /// <summary>在 <c>Update</c> 組好、<c>OnGUI</c> 只負責畫（<c>OnGUI</c> 一幀會被叫好幾次）。</summary>
    private string overlayText;

    /// <summary>上一次組字的時刻（不受時間縮放影響）。</summary>
    private float lastRefreshTime = float.NegativeInfinity;

    private GUIStyle labelStyle;
    private GUIStyle tabStyle;
    private readonly StringBuilder builder = new StringBuilder();

    // ───────── 擋按鈕（2026-10-30，Xperia 實機測試發現）─────────
    //
    // OnGUI 畫的東西 Unity 的 UI 事件系統看不到：點在面板上會穿過去按到下面的按鈕（右上角的放棄／提示／返回正好在分頁那一排底下）。
    // 所以在最上層自己蓋一塊透明的 UI 擋板，範圍每幀跟著面板走。
    // ⚠️ 它<b>只擋 UI 按鈕、不擋拖片</b>：拖片那邊看的是 TouchInputController 自己的擋片區（上面 BlockingScreenRect，字那塊刻意不擋），
    // 擋板沒有登記進去——面板開著照樣能看著算式拼。

    /// <summary>擋板的根物件（自己一個 Screen Space - Overlay 的 Canvas，排在所有 UI 最上面）。第一次 <c>OnEnable</c> 才建。</summary>
    private GameObject buttonBlocker;

    /// <summary>擋板那一塊透明圖的矩形，每幀照面板的範圍擺。</summary>
    private RectTransform buttonBlockerArea;

    /// <summary>
    /// 字那一塊上一次畫在哪（螢幕座標，原點左下）。高度要量字才知道，只有 <c>OnGUI</c> 量得到，所以畫的時候記下來給擋板用——
    /// 位置只在畫的那裡算一次，不另外算一份（同「版面」那段的原則）。沒有字時是 <see cref="Rect.zero"/>。
    /// </summary>
    private Rect lastTextBoxScreenRect;

    private void Awake()
    {
        if (levelController == null) levelController = FindFirstObjectByType<LevelController>();
        if (boardView == null) boardView = FindFirstObjectByType<BoardView>();
        if (dragController == null) dragController = FindFirstObjectByType<TouchInputController>();
        isExpanded = startExpanded;

        // 不該出現的 build 直接關掉自己。**這樣就不必在交出去之前記得刪**——
        // 「記得拿掉」是靠人的規定，跟這個專案踩過的兩個坑同一類。
        // 什麼叫展示版只在 DemonstrationBuild 定義一次（2026-10-30，清除紀錄鈕與 K 倍率也問它）
        if (!DemonstrationBuild.IsActive) enabled = false;
    }

    private void OnEnable()
    {
        // 不是展示版時 Awake 已經把 enabled 關掉，這裡不會跑到——擋板也就不會建
        EnsureButtonBlocker();
        buttonBlocker.SetActive(true);

        if (dragController == null) return;
        if (blockingRectProvider == null) blockingRectProvider = () => BlockingScreenRect();
        dragController.RegisterPressBlockingScreenRect(blockingRectProvider);
    }

    private void OnDisable()
    {
        if (buttonBlocker != null) buttonBlocker.SetActive(false);
        if (dragController != null) dragController.UnregisterPressBlockingScreenRect(blockingRectProvider);
    }

    private void OnDestroy()
    {
        if (buttonBlocker != null) Destroy(buttonBlocker);
    }

    /// <summary>
    /// 建擋板：一個獨立的 Screen Space - Overlay Canvas（排序 32767，蓋在所有 UI 上面）加一塊全透明、會接射線的 Image。
    /// 沒有 CanvasScaler，所以 1 單位＝1 螢幕像素、原點在左下，跟面板的螢幕座標同一套，直接照抄矩形就好。
    /// 放在場景根部、不掛在這支底下：這支的物件如果在別的 Canvas 裡，巢狀 Canvas 的排序會被父層牽著走。
    /// </summary>
    private void EnsureButtonBlocker()
    {
        if (buttonBlocker != null) return;

        buttonBlocker = new GameObject("監控面板的按鈕擋板", typeof(RectTransform), typeof(Canvas), typeof(UnityEngine.UI.GraphicRaycaster));
        Canvas canvas = buttonBlocker.GetComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = short.MaxValue;

        var area = new GameObject("擋板", typeof(RectTransform), typeof(CanvasRenderer), typeof(UnityEngine.UI.Image));
        area.transform.SetParent(buttonBlocker.transform, false);
        UnityEngine.UI.Image image = area.GetComponent<UnityEngine.UI.Image>();
        image.color = new Color(0f, 0f, 0f, 0f);    // 全透明；alpha 0 照樣接得到射線（沒設 alphaHitTestMinimumThreshold）
        image.raycastTarget = true;
        area.GetComponent<CanvasRenderer>().cullTransparentMesh = true;     // 不畫，只擋

        buttonBlockerArea = area.GetComponent<RectTransform>();
        buttonBlockerArea.anchorMin = Vector2.zero;
        buttonBlockerArea.anchorMax = Vector2.zero;
        buttonBlockerArea.pivot = Vector2.zero;
    }

    /// <summary>
    /// 擋板擺到面板現在佔的地方：收起時只有把手，展開時把手、整排分頁、加上字那一塊。
    /// 字的範圍是上一次 <c>OnGUI</c> 量的，所以換頁時會晚一幀跟上——那一幀的手指不會剛好按在新多出來的那一條上，不值得為它在這裡再量一次字。
    /// </summary>
    private void LateUpdate()
    {
        if (buttonBlockerArea == null) return;

        bool shown = showOverlay;
        if (buttonBlocker.activeSelf != shown) buttonBlocker.SetActive(shown);
        if (!shown) return;

        Rect area = BlockingScreenRect();
        if (isExpanded && lastTextBoxScreenRect.width > 0f)
            area = Rect.MinMaxRect(Mathf.Min(area.xMin, lastTextBoxScreenRect.xMin), Mathf.Min(area.yMin, lastTextBoxScreenRect.yMin),
                                   Mathf.Max(area.xMax, lastTextBoxScreenRect.xMax), Mathf.Max(area.yMax, lastTextBoxScreenRect.yMax));

        buttonBlockerArea.anchoredPosition = area.position;
        buttonBlockerArea.sizeDelta = area.size;
    }

    private void Update()
    {
        PollTaps();

        if (!showOverlay || !isExpanded)
        {
            overlayText = null;
            return;
        }

        // 換頁、展開的那一格 PollTaps 會把 lastRefreshTime 歸零，所以馬上就換字
        if (Time.unscaledTime - lastRefreshTime < RefreshIntervalSeconds) return;
        lastRefreshTime = Time.unscaledTime;
        overlayText = BuildPageText();
    }

    // ───────── 版面（螢幕座標，原點左下，跟 Pointer.position 同一套）─────────
    //
    // ⚠️ OnGUI 的原點在左上，畫的時候翻一次 Y（ToGuiRect）。位置只在這裡定義，畫的那邊從這裡換算——
    // 兩份各算一次的話，寫錯一份就變成「看得到但按不到」，而且不報錯。

    /// <returns>把手的矩形。</returns>
    private Rect HandleScreenRect()
    {
        float size = ScreenMetrics.ToPixels(handleSizeDp);
        float margin = ScreenMetrics.ToPixels(MarginDp);
        return new Rect(margin, Screen.height - margin - size, size, size);
    }

    /// <returns>現在看得到幾個分頁（除錯頁關著就少一個）。</returns>
    private int VisibleTabCount => showDebugPage ? PageTitles.Length : PageTitles.Length - 1;

    /// <param name="index">第幾個分頁。</param>
    /// <returns>那個分頁的矩形：接在把手右邊排成一列，螢幕放不下時一起縮。</returns>
    private Rect TabScreenRect(int index)
    {
        Rect handle = HandleScreenRect();
        float margin = ScreenMetrics.ToPixels(MarginDp);
        float start = handle.xMax + margin;
        float available = Screen.width - margin - start;
        float width = Mathf.Min(ScreenMetrics.ToPixels(tabWidthDp), available / VisibleTabCount);
        return new Rect(start + index * width, handle.y, width, handle.height);
    }

    /// <returns>擋片區：收起時只有把手，展開時把手加整排分頁。字的那一塊刻意不擋（見類別說明）。</returns>
    private Rect BlockingScreenRect()
    {
        Rect handle = HandleScreenRect();
        if (!showOverlay || !isExpanded) return handle;
        Rect lastTab = TabScreenRect(VisibleTabCount - 1);
        return Rect.MinMaxRect(handle.xMin, handle.yMin, lastTab.xMax, handle.yMax);
    }

    /// <param name="screenRect">螢幕座標的矩形（原點左下）。</param>
    /// <returns>同一塊地方在 <c>OnGUI</c> 座標的矩形（原點左上）。</returns>
    private static Rect ToGuiRect(Rect screenRect)
        => new Rect(screenRect.x, Screen.height - screenRect.yMax, screenRect.width, screenRect.height);

    /// <summary>
    /// 按在把手上就開合，展開時按在分頁上就換頁。
    /// ⚠️ <b>用 <c>Pointer.current</c> 就夠了</b>：這裡只認「按下的那一瞬間」，不用像拖曳那樣鎖 touch id。
    /// </summary>
    private void PollTaps()
    {
        if (!showOverlay) return;

        Pointer pointer = Pointer.current;
        if (pointer == null || !pointer.press.wasPressedThisFrame) return;
        Vector2 position = pointer.position.ReadValue();

        if (HandleScreenRect().Contains(position))
        {
            isExpanded = !isExpanded;
            lastRefreshTime = float.NegativeInfinity;
            return;
        }

        if (!isExpanded) return;
        for (int i = 0; i < VisibleTabCount; i++)
        {
            if (!TabScreenRect(i).Contains(position)) continue;
            page = (OverlayPage)i;
            lastRefreshTime = float.NegativeInfinity;
            return;
        }
    }

    // ───────── 畫 ─────────

    private void OnGUI()
    {
        if (!showOverlay) return;

        // GUIStyle 只能在 OnGUI 裡安全地建（GUI.skin 在別的時機無效）
        EnsureStyles();

        // 先當作沒有字，畫了字那一塊才記下來（給擋板用）
        lastTextBoxScreenRect = Rect.zero;

        DrawBox(ToGuiRect(HandleScreenRect()), isExpanded ? 0.65f : 0.45f);
        GUI.Label(ToGuiRect(HandleScreenRect()), isExpanded ? "▲" : "▼", tabStyle);
        if (!isExpanded) return;

        // 除錯頁被關掉的時候，萬一停在那一頁就退回第一頁
        if (page == OverlayPage.Debug && !showDebugPage) page = OverlayPage.Puzzle;

        for (int i = 0; i < VisibleTabCount; i++)
        {
            Rect tab = ToGuiRect(TabScreenRect(i));
            DrawBox(tab, (int)page == i ? 0.85f : 0.45f);
            GUI.Label(tab, PageTitles[i], tabStyle);
        }

        if (string.IsNullOrEmpty(overlayText)) return;

        // 字的那一塊接在把手底下，寬度到螢幕右緣，高度照字算
        float margin = ScreenMetrics.ToPixels(MarginDp);
        float padding = margin;
        Rect handle = ToGuiRect(HandleScreenRect());
        float width = Screen.width - margin * 2f;
        var content = new GUIContent(overlayText);
        float height = labelStyle.CalcHeight(content, width - padding * 2f);

        var box = new Rect(margin, handle.yMax + margin, width, height + padding * 2f);
        lastTextBoxScreenRect = new Rect(box.x, Screen.height - box.yMax, box.width, box.height);   // GUI 座標翻回螢幕座標
        DrawBox(box, 0.65f);
        GUI.Label(new Rect(box.x + padding, box.y + padding, width - padding * 2f, height), content, labelStyle);
    }

    private void EnsureStyles()
    {
        int fontSize = Mathf.Max(8, Mathf.RoundToInt(ScreenMetrics.ToPixels(fontSizeDp)));

        if (labelStyle == null)
        {
            labelStyle = new GUIStyle(GUI.skin.label) { alignment = TextAnchor.UpperLeft, richText = false, wordWrap = true };
            labelStyle.normal.textColor = Color.white;
        }
        labelStyle.fontSize = fontSize;

        if (tabStyle == null)
        {
            tabStyle = new GUIStyle(GUI.skin.label) { alignment = TextAnchor.MiddleCenter, richText = false, clipping = TextClipping.Clip };
            tabStyle.normal.textColor = Color.white;
        }
        tabStyle.fontSize = fontSize;
    }

    /// <param name="rect">GUI 座標的矩形。</param>
    /// <param name="alpha">黑底的不透明度。</param>
    private static void DrawBox(Rect rect, float alpha)
    {
        Color previous = GUI.color;
        GUI.color = new Color(0f, 0f, 0f, alpha);
        GUI.DrawTexture(rect, Texture2D.whiteTexture);
        GUI.color = previous;
    }

    // ───────── 字 ─────────

    /// <returns>這一頁要顯示的字。全部唯讀，不會改到任何狀態。</returns>
    private string BuildPageText()
    {
        if (page == OverlayPage.Debug) return BuildDebugText();

        if (levelController == null) return "場景裡沒有 LevelController，沒有關卡可以看。";

        // ⭐ 權重跟 LevelController 拿，不自己讀檔——要跟出題用的是同一個實例，理由見 LevelMonitor.Build
        LevelMonitorReport report = monitor.Build(LevelSession.Current, levelController.CurrentDifficultyWeights,
                                                  GameClock.NowSeconds, levelController.IsLevelPaused);
        try
        {
            return LevelMonitorText.PageText(report, (MonitorPage)(int)page, maximumHistoryRows);
        }
        catch (System.Exception exception)
        {
            // 拆解會丟例外的情況（權重在記憶體裡被改壞之類）。顯示出來就好，不要每次重組都洗 Console
            return $"組監控報告時出錯：{exception.Message}";
        }
    }

    // ───────── 除錯頁（原本的除錯 overlay，2026-10-23 起預設不顯示）─────────

    /// <returns>除錯頁的字。</returns>
    private string BuildDebugText()
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

        // 已放置片的面積和。七片放滿時必為 32，因為那就是盒子的面積
        builder.AppendLine($"面積    {PlacedArea(board):0.##} / 32");

        AppendDragLine(level);
        AppendSelectionLine(level);

        BoundingBox bounds = level.BoxRegion.Bounds;
        double boxWidth = (bounds.Maximum.X - bounds.Minimum.X).ToDouble();
        double boxHeight = (bounds.Maximum.Y - bounds.Minimum.Y).ToDouble();
        builder.AppendLine($"盒子    {boxWidth:0.##} × {boxHeight:0.##}   環 {level.BoxRegion.Rings.Length}   origin {level.BoxOrigin}");

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

        // 「畫面上吸住了」與「盤面登記了」刻意會在拖曳中不一致（互動設計 §13.10），所以兩個都印出來。
        // ⚠️ 這一格也是驗「點一下不會偷偷 Lift」最快的地方：點一下已放置的片，盤面那個字不該從「有」變「無」
        bool onBoard = level.Board[pressed.SlotIndex].OnBoard;
        builder.AppendLine($"{phase}    槽{pressed.SlotIndex} {pressed.Kind} " +
                           $"rot{pressed.Rotation}{(pressed.IsFlipped ? " 翻" : "")}   " +
                           $"畫面{(pressed.IsSnapped ? "吸住" : "自由")} / 盤面{(onBoard ? "有" : "無")}");
    }

    /// <summary>
    /// 被選取（高亮）的是哪一片。⚠️ 依互動設計 §4，<b>沒在被拖的時候</b>被選取的片必定不在盤面上——
    /// 這一行同時是那條不變式的檢查點。拖曳中「盤面有」是正常的（放手才 <c>Lift</c>），警告要扣掉那種情況。
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

    /// <summary>已放置片的面積和。片的面積只有 2 / 4 / 8 三種，全部放滿是 32。</summary>
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
