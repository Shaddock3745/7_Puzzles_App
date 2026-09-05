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

    [Tooltip("吸附半徑，螢幕像素。P1-13 調參用。")]
    [SerializeField] private float snapRadiusPixels = 18f;

    [Tooltip("脫離半徑，螢幕像素。要比吸附半徑大，這個差距就是遲滯。")]
    [SerializeField] private float releaseRadiusPixels = 30f;

    /// <summary>
    /// 這一題的座標狀態。P1-3（<c>BoxView</c>）要拿 <c>BoxOrigin</c> 畫盒子。
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

    private void Awake()
    {
        Layout = new BoardLayout(LayoutRule.Portrait);
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
    /// <param name="boxOrigin">盒子原點，精確座標。</param>
    /// <param name="allowRoll">允不允許 roll 相機。精選題傳 false。</param>
    public void SetBox(BoundingBox absoluteBoxBounds, ExactVector2 boxOrigin, bool allowRoll)
    {
        Layout.SetBox(absoluteBoxBounds, boxOrigin, allowRoll);
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
        SnapSettings = new SnapSolver.Settings
        {
            ScreenPixelsPerModelUnit = solution.ScreenPixelsPerModelUnit,
            SnapRadiusPixels = snapRadiusPixels,
            ReleaseRadiusPixels = releaseRadiusPixels
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
