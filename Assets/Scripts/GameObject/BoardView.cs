using System;
using Tangram.Core;
using Tangram.Data;
using UnityEngine;

/// <summary>
/// 角色三：Unity 驅動（互動設計 §2.4）。<b>它不做數學。</b>
///
/// 它只做四件事：
/// <list type="number">
/// <item>讀 <c>Screen.width</c> / <c>Screen.height</c></item>
/// <item>偵測尺寸或題目變化 → 觸發重算</item>
/// <item>把算出來的相機中心、<c>orthographicSize</c>、roll <b>寫進</b> <c>Camera</c></item>
/// <item>把算出來的 <c>screenPixelsPerModelUnit</c> <b>寫進</b> <see cref="SnapSettings"/></item>
/// </list>
///
/// <b>它是唯一測不了的部分，所以壓到只剩搬運。</b>所有數學都在
/// <see cref="ViewportMath"/>（無狀態、可離線測），所有狀態都在
/// <see cref="BoardLayout"/>（無 Unity 相依、可離線測）。
///
/// <b>相機是輸出，不是真相。</b>這支只往 <c>Camera</c> 寫，從來不讀它回來當資料。
/// 座標查詢一律走 <see cref="ScreenToWorld"/>，它算的是 <see cref="Solution"/>，
/// 不是相機當下的狀態——所以就算相機這幀還沒更新，答案也是對的（順序契約 §2.5 第 6 條）。
/// </summary>
[DisallowMultipleComponent]
public class BoardView : MonoBehaviour
{
    [Tooltip("要驅動的正交相機。留空的話 Awake 會抓 Camera.main。")]
    [SerializeField] private Camera targetCamera;

    [Tooltip("吸附半徑，單位 dp（1 dp = 1/160 英寸）。2026-09-09 實機調出來的值。" +
             "⚠️ 改了要重進 Play 才生效，見 SnapSettings 的說明。")]
    [SerializeField] private float snapRadiusDp = 12f;

    [Tooltip("脫離半徑，單位 dp。⚠️ 要比吸附半徑大，這個差距就是遲滯——" +
             "太接近的話片會在邊界反覆吸上／甩開，看起來像在抖。")]
    [SerializeField] private float releaseRadiusDp = 18f;

    [Header("盒子與暫放的片要躲開的按鈕排")]
    [Tooltip("按鈕排的父物件（例如掛 Horizontal Layout Group 的那個），它的矩形就是按鈕區（2026-09-15）。"
             + "盒子區：中心在螢幕上半的壓低上緣、下半的抬高下緣（整條）。暫放的片：只躲這個矩形本身。停用的跳過。"
             + "⚠️ 這個物件的大小要固定：跟著按鈕出現／消失變大變小的話，盒子會當場縮放、正在拖的片會被放回原位。")]
    [SerializeField] private RectTransform[] avoidedButtonAreas = Array.Empty<RectTransform>();

    [Tooltip("盒子與暫放的片跟按鈕排之間留多少，單位 dp。")]
    [Min(0f)]
    [SerializeField] private float buttonAreaMarginDp = 8f;

    [Tooltip("暫放的片離螢幕邊緣至少留多少，單位 dp（2026-09-15）。放手、旋轉、翻面、版面重算之後超出的片會被推回來。")]
    [Min(0f)]
    [SerializeField] private float floatingPieceEdgeMarginDp = 8f;

    /// <summary>
    /// 這一題的座標狀態。相機參數的來源；盒子原點**不在這裡**，在 <c>LevelSession</c>。
    /// </summary>
    public BoardLayout Layout { get; private set; }

    /// <summary>
    /// 目前生效的版面解。<b>拿它去做座標換算，不要去讀 <c>Camera</c>。</b>
    /// 還沒算過或算不出來時 <c>IsValid</c> 是 false。
    /// </summary>
    public ViewportSolution Solution { get; private set; }

    /// <summary>
    /// 目前生效的吸附手感參數，餵給 <see cref="SnapSolver.Resolve"/>。
    ///
    /// <b>唯讀是刻意的。</b><c>ScreenPixelsPerModelUnit</c> 只有這支算得出來（只有它讀 <c>Screen</c>），
    /// 開放外面寫的話，哪天有人改了它，吸附半徑就跟畫面對不上——
    /// 而症狀只是「手感突然很怪」，沒有任何錯誤訊息，是最難查的一種 bug。
    /// </summary>
    /// <remarks>
    /// ⚠️ <b>兩個半徑是在版面重算那一格算一次存起來的，而版面重算只在螢幕尺寸變了才跑。</b>
    /// 所以在 Play 模式下改 Inspector 的值<b>不會即時生效，而且不報錯</b>——
    /// 要重進 Play（或轉一次螢幕方向）才看得到。
    ///
    /// 2026-09-09 曾經為了「拉滑桿調手感」改成每次讀都現算，
    /// 調完 P1-13 的數值之後改回來——那個需求是一次性的，
    /// 而現算會讓「dp → 像素」每幀跑很多次。**數值已經定了（12 / 18），不再需要即時調。**
    /// </remarks>
    public SnapSolver.Settings SnapSettings { get; private set; }

    /// <summary>
    /// 版面重算完、而且已經推送給 <c>Camera</c> 之後觸發。
    ///
    /// <b>⚠️ P1-1 階段這個事件沒有訂閱者，這不是漏了，是刻意的。</b>
    /// 互動設計 §2.8 第四項決定「重算版面時取消進行中的拖曳」——真正會出事的是
    /// 重算會重排暫放區的七片，手指底下那片會瞬間移位、抓取偏移歪掉。
    /// 但「取消拖曳」需要拖曳控制器，而它是 P1-6 才存在的東西。
    ///
    /// <b>P1-6 要做的事</b>：<c>TouchInputController</c> 訂閱這個事件，
    /// 收到就把拖曳中那片放回抓起前的狀態。沒接的話症狀是「轉螢幕方向或拉 Game 視窗時，
    /// 正在拖的片會突然黏歪」，而且不會有任何錯誤訊息。
    /// </summary>
    public event Action LayoutRecomputed;

    /// <summary>
    /// 盒子區規則的起點：寫死的直式版面。按鈕排只會從它往內縮（見 <see cref="LayoutRule.AvoidingButtonAreas"/>）。
    /// </summary>
    private static readonly LayoutRule BaseRule = LayoutRule.Portrait;

    /// <summary>
    /// 規則差多少像素才算「變了」。⚠️ 不能是 0：矩形每幀從 Canvas 換算，浮點抖一下就會重算版面，
    /// 而重算會把正在拖的片放回原位（見 <see cref="LayoutRule.ApproximatelyEquals"/>）。
    /// </summary>
    private const float RuleChangeTolerancePixels = 0.5f;

    /// <summary>擠不下的警告只吵一次，擠得下了再解除——不然每幀都印一行。</summary>
    private bool hasWarnedButtonAreasDoNotFit;

    /// <summary>每幀重用，不在 Update 裡配置。</summary>
    private readonly System.Collections.Generic.List<ScreenVerticalRange> buttonAreaRanges =
        new System.Collections.Generic.List<ScreenVerticalRange>();

    /// <summary>
    /// 照按鈕排現在的位置算出盒子區規則，<b>跟現在的差超過容差才</b>換（2026-09-15）。
    ///
    /// <b>每幀都算</b>，所以轉螢幕方向、改解析度、Canvas 縮放變了都會自己跟上，不必有人通知。
    ///
    /// ⚠️ <b>按鈕排的螢幕位置一定要走 <see cref="UiScreenRect"/>，不能用畫布的相機換算</b>（除錯記錄 012）：
    /// 畫布是 Screen Space - Camera，要等到畫面要畫之前才跟上相機；而這裡算出來的規則會改相機——
    /// 用相機換算的話「讀錯 → 改相機 → 又讀錯」，實測盒子連抖十幀，每次重算還會把正在拖的片放回原位。
    /// </summary>
    /// <param name="screenWidth">螢幕寬（像素）。</param>
    /// <param name="screenHeight">螢幕高（像素）。</param>
    private void RefreshRuleFromButtonAreas(int screenWidth, int screenHeight)
    {
        if (screenWidth <= 0 || screenHeight <= 0) return;

        CollectButtonAreaRanges();
        LayoutRule rule = BaseRule.AvoidingButtonAreas(buttonAreaRanges, screenHeight,
                                                       ScreenMetrics.ToPixels(buttonAreaMarginDp), out bool fits);

        if (!fits && !hasWarnedButtonAreasDoNotFit)
            Debug.LogWarning("盒子區躲開按鈕排之後沒有高度了（按鈕排太大，或畫面太扁），先照原本的版面放。" +
                             "檢查 BoardView 的「盒子要躲開的按鈕排」與那幾個物件的大小。", this);
        hasWarnedButtonAreasDoNotFit = !fits;

        if (!rule.ApproximatelyEquals(Layout.Rule, screenWidth, screenHeight, RuleChangeTolerancePixels))
            Layout.SetRule(rule);
    }

    /// <summary>
    /// 把 Inspector 上的按鈕排換成螢幕上下範圍，填進 <see cref="buttonAreaRanges"/>。
    /// ⚠️ 一定走 <see cref="UiScreenRect"/>，理由見 <see cref="RefreshRuleFromButtonAreas"/>。
    /// </summary>
    private void CollectButtonAreaRanges()
    {
        buttonAreaRanges.Clear();
        foreach (RectTransform area in avoidedButtonAreas)
        {
            if (area == null || !area.gameObject.activeInHierarchy) continue;
            if (!UiScreenRect.TryGet(area, out Rect screenRect)) continue;
            buttonAreaRanges.Add(new ScreenVerticalRange(screenRect.yMin, screenRect.yMax));
        }
    }

    /// <summary>
    /// <b>暫放的片可以待的範圍</b>與<b>要躲開的按鈕排</b>（都是世界座標，2026-09-15）。
    ///
    /// <list type="bullet">
    /// <item>範圍：整個螢幕四邊各留 <see cref="floatingPieceEdgeMarginDp"/>。<b>不扣盒子</b>——暫放的片本來就可以蓋在盒子上</item>
    /// <item>按鈕排：Inspector 上那份清單（跟盒子區同一份），各自外擴 <see cref="buttonAreaMarginDp"/>。停用的跳過</item>
    /// </list>
    ///
    /// ⭐ <b>為什麼要躲按鈕排</b>（使用者 2026-09-15 指出）：手指會蓋住自己要點的地方，觸控目標要大約 48 dp 才點得穩，
    /// 所以片被按鈕蓋住大半、只露出一小條，在手機上就等於救不回來（滑鼠點得到，所以在 Editor 看不出來）。
    /// ⭐ <b>看按鈕排的父物件，不看個別按鈕顯示了沒</b>：提示鈕、放棄鈕是玩到一半才冒出來的，
    /// 看顯示中的按鈕的話，片暫放在那裡之後按鈕出現就把它蓋住了；而且選取時旋轉鈕出現也不會讓片突然被推走。
    /// ⭐ <b>只躲那個矩形，不是整條螢幕寬</b>：左下兩排只佔左半邊，右下角照樣可以放片。
    ///
    /// 每次叫都現算、不存：版面解與按鈕排都會變，存了就多一份要同步的東西。
    /// 相機 roll 只會是 90° 的倍數，所以螢幕上的軸對齊矩形換到世界裡還是軸對齊的，對角兩點取包圍盒就是它。
    /// </summary>
    /// <param name="area">可放區（世界座標）；失敗時是 default。</param>
    /// <param name="buttonAreasInWorld">要躲開的按鈕排（世界座標、已外擴間距）。會先清空再填。</param>
    /// <returns>算得出來的話 true。版面解還不能用，或留白比螢幕還寬時 false——呼叫端就不要推。</returns>
    public bool TryGetFloatingArea(out FloatBoundingBox area,
                                   System.Collections.Generic.List<FloatBoundingBox> buttonAreasInWorld)
    {
        area = default;
        buttonAreasInWorld.Clear();
        if (!Solution.IsValid) return false;

        float screenWidth = Solution.ScreenWidth;
        float screenHeight = Solution.ScreenHeight;
        float edgeMarginPixels = ScreenMetrics.ToPixels(floatingPieceEdgeMarginDp);

        var rule = new LayoutRule(edgeMarginPixels / screenWidth, edgeMarginPixels / screenHeight,
                                  1f - edgeMarginPixels / screenWidth, 1f - edgeMarginPixels / screenHeight);
        if (!rule.IsValid) return false;

        area = ScreenRectToWorld(new Rect(rule.BoxAreaMinimumX * screenWidth, rule.BoxAreaMinimumY * screenHeight,
                                          rule.BoxAreaWidthRatio * screenWidth, rule.BoxAreaHeightRatio * screenHeight));

        float buttonMarginPixels = ScreenMetrics.ToPixels(buttonAreaMarginDp);
        foreach (RectTransform buttonArea in avoidedButtonAreas)
        {
            if (buttonArea == null || !buttonArea.gameObject.activeInHierarchy) continue;
            if (!UiScreenRect.TryGet(buttonArea, out Rect screenRect)) continue;   // ⚠️ 不能用畫布的相機換算，除錯記錄 012

            screenRect.xMin -= buttonMarginPixels;
            screenRect.yMin -= buttonMarginPixels;
            screenRect.xMax += buttonMarginPixels;
            screenRect.yMax += buttonMarginPixels;
            buttonAreasInWorld.Add(ScreenRectToWorld(screenRect));
        }
        return true;
    }

    /// <summary>螢幕上的軸對齊矩形 → 世界裡的軸對齊矩形（照現在的版面解）。</summary>
    /// <param name="screenRect">螢幕矩形（像素，原點左下）。</param>
    /// <returns>世界座標的包圍盒。</returns>
    private FloatBoundingBox ScreenRectToWorld(Rect screenRect)
    {
        Vector2 first = ViewportMath.ScreenToWorld(screenRect.min, Solution);
        Vector2 second = ViewportMath.ScreenToWorld(screenRect.max, Solution);

        // 對角兩點就夠：roll 90° 的倍數時，另外兩個角的座標是這兩點的 x、y 交換組合
        return new FloatBoundingBox(Vector2.Min(first, second), Vector2.Max(first, second));
    }

    private void Awake()
    {
        Layout = new BoardLayout(BaseRule);
        if (targetCamera == null) targetCamera = Camera.main;
    }

    private void Update()
    {
        EnsureCurrent();
    }

    /// <summary>
    /// 換題目：設定盒子並立刻重算推送。
    /// </summary>
    /// <param name="absoluteBoxBounds">盒子的絕對包圍盒，通常是 <c>Region.Bounds</c>。</param>
    /// <param name="allowRoll">允不允許 roll 相機。精選題傳 false。</param>
    public void SetBox(BoundingBox absoluteBoxBounds, bool allowRoll)
    {
        Layout.SetBox(absoluteBoxBounds, allowRoll);
        EnsureCurrent();
    }

    /// <summary>
    /// 螢幕尺寸變了就重算並推送，沒變就什麼都不做（兩個 int 比較，可以隨便呼叫）。
    ///
    /// <c>Update()</c> 每幀會呼叫一次，但 <c>MonoBehaviour</c> 之間的 <c>Update</c> 順序
    /// 是未定義的。<b>P1-6 的拖曳控制器在讀座標之前應該先呼叫這一句</b>，
    /// 才不會在「螢幕剛變、BoardView 還沒輪到」的那一幀拿到舊的解。
    /// </summary>
    public void EnsureCurrent()
    {
        int screenWidth = Screen.width;
        int screenHeight = Screen.height;

        // 按鈕排的位置變了就換規則。SetRule 會把「已推送的螢幕尺寸」清掉，所以下一行一定會往下重算
        RefreshRuleFromButtonAreas(screenWidth, screenHeight);

        if (!Layout.HasScreenSizeChanged(screenWidth, screenHeight)) return;

        // Camera.main 是靠 tag 搜尋的，場景載入順序不對時 Awake 當下可能還拿不到，
        // 所以這裡再試一次。拿不到就整個放棄這一幀——重點是**不能往下走到 RecordScreenSize**，
        // 否則這組尺寸會被標記成「處理過了」，但相機根本沒收到值
        if (targetCamera == null) targetCamera = Camera.main;
        if (targetCamera == null) return;

        // 初始化中或視窗最小化時 Screen 會回 0。除以 0 會讓 orthographicSize 變 NaN，
        // 一路寫進 Camera 之後畫面全黑而且不丟任何例外，所以這裡直接不動作、等下一幀
        if (screenWidth <= 0 || screenHeight <= 0) return;

        ViewportSolution solution = Layout.Solve(screenWidth, screenHeight);
        if (!solution.IsValid) return;

        Solution = solution;
        PushToCamera(solution);

        // dp → 螢幕像素在這裡轉一次。SnapSolver 收的仍然是螢幕像素——
        // 它是純 C#、測得到，不該知道 Screen.dpi 這種東西的存在
        SnapSettings = new SnapSolver.Settings
        {
            ScreenPixelsPerModelUnit = solution.ScreenPixelsPerModelUnit,
            SnapRadiusPixels = ScreenMetrics.ToPixels(snapRadiusDp),
            ReleaseRadiusPixels = ScreenMetrics.ToPixels(releaseRadiusDp)
        };

        // ⚠️ 這一行永遠是最後一步。LastScreenWidth/Height 的語意是
        // 「**已經成功推送過**的螢幕尺寸」，不是「上次看到的螢幕尺寸」。
        // 只要上面任何一個 return 生效，這組尺寸就不該被記起來——記了的話
        // HasScreenSizeChanged 從此回 false，永遠不再重算，畫面卡在錯的縮放而且不報錯
        Layout.RecordScreenSize(screenWidth, screenHeight);

        LayoutRecomputed?.Invoke();
    }

    /// <summary>
    /// 把解寫進相機。<b>資料流的終點，箭頭只進不出。</b>
    /// </summary>
    /// <param name="solution">要推送的版面解。</param>
    private void PushToCamera(ViewportSolution solution)
    {
        if (targetCamera == null) return;

        targetCamera.orthographic = true;
        targetCamera.orthographicSize = solution.OrthographicSize;

        // Z 要保留（2D 預設 −10）。把 Z 一起寫成 0 的話相機會跑到物件平面上，
        // 什麼都看不到，而且不會有任何錯誤訊息
        Transform cameraTransform = targetCamera.transform;
        Vector3 position = cameraTransform.position;
        cameraTransform.position =
            new Vector3(solution.CameraCenter.x, solution.CameraCenter.y, position.z);

        cameraTransform.rotation = Quaternion.Euler(0f, 0f, solution.RollDegrees);
    }

    /// <summary>
    /// 手指在模型的哪裡。
    /// <b>走 <see cref="ViewportMath"/> 算，不讀 <c>Camera</c></b>（順序契約 §2.5 第 6 條）。
    /// </summary>
    /// <param name="screenPoint">
    /// 螢幕座標，像素，原點左下。新 Input System 傳
    /// <c>Pointer.current.position.ReadValue()</c> 進來即可。
    /// </param>
    /// <returns>對應的世界座標（浮點）。解還沒算好時回 <c>Vector2.zero</c>。</returns>
    public Vector2 ScreenToWorld(Vector2 screenPoint)
        => ViewportMath.ScreenToWorld(screenPoint, Solution);

    /// <summary>
    /// 世界座標 → 螢幕座標。<b>只給除錯用</b>——提示的覆蓋框走 world-space sprite，
    /// 正常繪製路徑不需要這個方向（互動設計 §2.8）。
    /// </summary>
    /// <param name="worldPoint">世界座標。</param>
    /// <returns>對應的螢幕座標（像素，原點左下）。</returns>
    public Vector2 WorldToScreen(Vector2 worldPoint)
        => ViewportMath.WorldToScreen(worldPoint, Solution);
}
