using UnityEngine;

/// <summary>
/// ⭐ <b>簡易主畫面</b>（2026-09-23）：兩顆按鈕，選這一場要玩哪一種題目。
///
/// <list type="bullet">
/// <item><b>挑戰模式</b>——照實力當場生題，就是原本的遊玩迴圈（P2-7 ＋ P2-14）</item>
/// <item><b>圖形模式</b>——從題庫隨機抽具象題（貓、船之類），一輪之內不重複</item>
/// <item><b>掃描模式</b>（2026-10-14，名稱暫定）——拍照、取輪廓、照輪廓出題。⚠️ 現在三段都是假的，
/// 見 <see cref="GameMode.Scan"/>。掃描取消或失敗時這裡會自己顯示回來（<see cref="LevelController.ScanAborted"/>）</item>
/// </list>
///
/// <b>跟 <see cref="LevelResultPanels"/> 同一個形狀</b>：不做事，只負責「什麼時候顯示」與
/// 「把按下轉給別人」。模式的真相在 <see cref="LevelController.CurrentMode"/>，這裡不留第二份——
/// 留了的話「下一題要抽還是要生」就有兩個答案。
///
/// <b>為什麼是同場景的 Panel，不是換場景</b>（2026-09-23 定案）：換場景要動 build 設定、
/// 要處理 <c>LevelSession.Current</c> 這個 static 的生死，而 9/27 畫面就要凍結了。
/// 同場景的 Panel 只多一個物件，風險最小。⏭️ 真的要卸載盤面時再換場景，
/// <see cref="LevelResultPanels"/> 的說明裡已經留了那條路。
///
/// <b>場景要做的事</b>：
/// <list type="number">
/// <item>在 Canvas 底下建一個<b>整個螢幕大小</b>的 Panel（四邊 anchor 拉滿），底下放兩顆 Button</item>
/// <item>⚠️ <b>Panel 要不透明</b>——它蓋著的時候後面的盤面還在，半透明的話會看到上一關</item>
/// <item>⚠️ <b>Panel 在 Canvas 裡要排在兩個結果彈窗<u>後面</u></b>（sibling index 比較大），
/// 不然從彈窗按「回主畫面」之後，彈窗會蓋在主畫面上</item>
/// <item>兩顆的 <c>onClick</c> 分別接到 <see cref="OnChallengeModePressed"/> /
/// <see cref="OnFigurativeModePressed"/></item>
/// <item>（2026-10-14）第三顆「掃描模式」的 <c>onClick</c> 接到 <see cref="OnScanModePressed"/></item>
/// <item>⚠️ <b>這支腳本掛在另一個物件上，不能掛在 Panel 身上</b>——
/// 停用的物件不跑 <c>Update</c> 也收不到別人叫它的機會，藏起來就再也開不回來
/// （除錯記錄 004 踩過一次）</item>
/// <item>把 Panel 的 <c>GameObject</c> 填進 <see cref="panel"/></item>
/// </list>
/// </summary>
[DisallowMultipleComponent]
public class MainMenuPanel : MonoBehaviour
{
    [Tooltip("要開關的主畫面 Panel。⚠️ 不能是這支腳本自己所在的物件。")]
    [SerializeField] private GameObject panel;

    [Tooltip("按下按鈕之後要叫它進關。留空的話 Awake 會自己找。")]
    [SerializeField] private LevelController levelController;

    [Tooltip("擋片區的登記對象。留空的話 Awake 會自己找。")]
    [SerializeField] private TouchInputController inputController;

    /// <summary>登記進擋片區的那塊矩形，離場時要取消登記。</summary>
    private RectTransform blockingArea;

    private void Awake()
    {
        if (levelController == null) levelController = FindFirstObjectByType<LevelController>();
        if (inputController == null) inputController = FindFirstObjectByType<TouchInputController>();

        if (panel == null)
            Debug.LogError("MainMenuPanel 的 panel 欄位沒填：主畫面不會被顯示或隱藏。", this);
        else if (panel == gameObject)
            Debug.LogError(
                "MainMenuPanel 掛在它自己要開關的 Panel 上了。藏起來之後這支就跟著被停用，" +
                "再也沒辦法把自己開回來。請把這支腳本移到另一個物件上（照 LevelResultPanels 的做法）。", this);

        if (levelController == null)
        {
            Debug.LogError("MainMenuPanel 找不到 LevelController：兩顆按鈕按了不會有反應。", this);
            return;
        }

        // ⚠️ 一定要在 Awake 裡講。自動進關是在 LevelController.Start 裡，
        // 而 Unity 保證所有 Awake 跑完才開始跑 Start——在 Start 裡講就賭上了兩支誰先跑
        levelController.SuppressAutoStart();

        // 掃描沒進關就結束（取消、失敗）時把主畫面顯示回來。不訂閱的話玩家會停在一個被藏起來的主畫面後面
        levelController.ScanAborted += Show;
    }

    private void OnDestroy()
    {
        if (levelController != null) levelController.ScanAborted -= Show;
    }

    /// <summary>
    /// 把整個 Panel 登記成「按在這裡不要抓片」。
    ///
    /// ⚠️ <b>只登記一次，不用跟著顯示開關</b>：擋片區本來就會跳過停用的物件，
    /// 所以主畫面藏起來時那塊自動不擋。跟結果彈窗完全一樣的做法。
    /// </summary>
    private void OnEnable()
    {
        if (panel == null || inputController == null) return;

        blockingArea = panel.GetComponent<RectTransform>();
        if (blockingArea == null)
        {
            Debug.LogError($"{panel.name} 上沒有 RectTransform，不是 UI 物件？" +
                           "主畫面蓋著的時候盤面還是抓得到片。", this);
            return;
        }

        inputController.RegisterPressBlockingArea(blockingArea);
    }

    private void OnDisable()
    {
        if (inputController == null || blockingArea == null) return;
        inputController.UnregisterPressBlockingArea(blockingArea);
    }

    /// <summary>開場先顯示主畫面，不進任何一關。</summary>
    private void Start() => Show();

    /// <summary>顯示主畫面。從結果彈窗按「回主畫面」時也走這支。</summary>
    public void Show()
    {
        if (panel != null) panel.SetActive(true);
    }

    /// <summary>藏起主畫面。</summary>
    public void Hide()
    {
        if (panel != null) panel.SetActive(false);
    }

    /// <summary>「挑戰模式」被按下。接在 Unity <c>Button.onClick</c> 上。</summary>
    /// <remarks>
    /// ⚠️ <b>按下去會凍住畫面零點幾到一點多秒</b>，那是出題在跑不是當掉——
    /// 跟結果彈窗那顆「下一題」一模一樣，理由見 <see cref="LevelResultPanels.OnNextLevelPressed"/>。
    /// </remarks>
    public void OnChallengeModePressed() => StartMode(GameMode.GeneratedByRating);

    /// <summary>「圖形模式」被按下。接在 Unity <c>Button.onClick</c> 上。</summary>
    /// <remarks>第一次按會多花零點幾秒讀題庫，之後就不會了。</remarks>
    public void OnFigurativeModePressed() => StartMode(GameMode.Figurative);

    /// <summary>「掃描模式」被按下。接在 Unity <c>Button.onClick</c> 上（2026-10-14）。</summary>
    /// <remarks>
    /// 進關要等掃描回來（假的掃描是下一幀），這中間主畫面已經藏起來了。
    /// 掃描取消或失敗會經由 <see cref="LevelController.ScanAborted"/> 叫回 <see cref="Show"/>。
    /// </remarks>
    public void OnScanModePressed() => StartMode(GameMode.Scan);

    /// <summary>
    /// 先藏起自己再進關。
    /// <b>順序不能反</b>：<c>StartMode</c> 會換關，換關的五步裡有相機 fit 與擺七片，
    /// 那時候主畫面還蓋著的話，玩家會看到畫面在主畫面後面動。
    /// </summary>
    /// <param name="mode">要玩哪一種。</param>
    private void StartMode(GameMode mode)
    {
        if (levelController == null) return;

        Hide();
        levelController.StartMode(mode);
    }
}
