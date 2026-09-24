using Tangram.Core;
using Tangram.Data;
using TMPro;
using UnityEngine;

/// <summary>
/// 這一關結束之後的兩個彈窗：<b>過關</b>與<b>放棄</b>（P1-10 + P2-13，互動設計 §7）。
///
/// ⚠️ <b>2026-09-12 從 LevelCompletePanel 改名並改成一支管兩個</b>。
/// 兩個彈窗是同一類東西（都是「這一關結束了」）、機制一模一樣（滿版擋片區、每幀問條件、
/// 把按下轉給別人），照 <see cref="BoardActionButtons"/> 那個「一支腳本管一組同類 UI」的模式該放一起。
/// ⚠️ <b>互斥不是這裡保證的</b>——是 <c>LevelData</c>：已過關就不能放棄、已放棄就不能過關。
///
/// <b>跟 <see cref="PieceActionButtons"/> / <see cref="BoardActionButtons"/> 同一個形狀</b>：
/// 不做事，只負責「什麼時候顯示」與「把按下轉給別人」。
///
/// <b>它不訂閱任何東西</b>，每幀問一次「這一關結束了沒」。
/// 好處是<b>沒有人需要記得關掉它</b>——按下一關 → 換關 → <c>LevelData</c> 整包換掉
/// → 過關旗標變回 false → 下一幀它自己消失。少一條「誰通知誰」的線。
///
/// <b>為什麼是同場景的 panel，不是換場景</b>（2026-09-09 討論後定案）：
/// <list type="bullet">
/// <item>換場景（Single）會讓「下一關」從「叫一次 <c>EnterLevel</c>」變成「重建整個場景」，
/// 繞過那條已經實測過的五步順序</item>
/// <item><c>LevelSession.Current</c> 是 static，場景卸載不會清它，回來時
/// <c>LevelController.Awake</c> 會直接覆蓋掉——「這一關玩了什麼」傳不回去</item>
/// <item>之後想「把盤面截圖貼在視窗上」，同場景反而更簡單：<b>盤面根本沒消失</b></item>
/// </list>
/// 換場景這個工具留給「退出 → 回主畫面」用，那時候本來就該卸載盤面。
///
/// <b>場景要做的事</b>：
/// <list type="number">
/// <item>在 Canvas 底下建一個<b>整個螢幕大小</b>的 Panel（四邊 anchor 都拉滿），
/// 底下再放對話框與兩顆 Button</item>
/// <item>兩顆的 <c>onClick</c> 分別接到 <see cref="OnExitPressed"/> / <see cref="OnNextLevelPressed"/></item>
/// <item>⚠️ <b>這支腳本掛在另一個物件上，不能掛在 Panel 身上</b>（理由見下）</item>
/// <item>把 Panel 的 <c>GameObject</c> 填進 <see cref="completePanel"/></item>
/// <item>兩個對話框上各放一個 <c>TMP_Text</c>，拖進 <see cref="completeTrendText"/> /
/// <see cref="abandonedTrendText"/>。那是「下一題會難一點／差不多／簡單一點」（P2-14）</item>
/// </list>
///
/// ⭐ <b>P2-14 起下一題的難度跟著實力走</b>，P2-7 那個臨時輸入框整段拿掉了。
/// <b>玩家只看得到方向，看不到分數</b>（2026-09-13 定，A 案）：公式允許「解完了但比 T 慢」也扣分，
/// 那是數學上對的，但對小孩顯示負號很傷。⚠️ <b>不能只藏負號</b>——正的有數字、負的沒有，玩家馬上推得出來。
/// 完整的 E、S、ΔR 留給 P2-15 監控面板。
///
/// ⚠️ <b>Panel 的 <c>RectTransform</c> 一定要滿版</b>，因為擋片區擋的就是它那塊矩形。
/// 只做成中間一個小對話框的話，<b>對話框外面的盤面還是抓得到片</b>。
///
/// ⚠️ <b>為什麼腳本不能掛在 Panel 上</b>：隱藏是用 <c>SetActive(false)</c> 做的，
/// 而被停用的物件不會再跑 <c>Update</c>——一關開始還沒過關 → 第一幀就把自己關掉
/// → 從此再也開不回來，<b>而且不報錯</b>（除錯記錄 004 踩過一次）。
/// </summary>
[DisallowMultipleComponent]
public class LevelResultPanels : MonoBehaviour
{
    [Tooltip("過關狀態與擋片區的來源。留空的話 Awake 會自己找。")]
    [SerializeField] private TouchInputController inputController;

    [Tooltip("「退出」要叫它顯示主畫面。留空的話 Awake 會自己找；場景裡沒有主畫面時「退出」維持舊行為（關掉程式）。")]
    [SerializeField] private MainMenuPanel mainMenu;

    [Tooltip("「下一關」要叫它換關。留空的話 Awake 會自己找。")]
    [SerializeField] private LevelController levelController;

    [Tooltip("整個彈窗的根物件。⚠️ 它的 RectTransform 要滿版，擋片區擋的就是它。")]
    [SerializeField] private GameObject completePanel;

    [Tooltip("放棄彈窗的根物件（P2-13）。⚠️ 它的 RectTransform 也要滿版。")]
    [SerializeField] private GameObject abandonedPanel;

    [Tooltip("過關彈窗上的「下一題會難一點／差不多／簡單一點」（P2-14）。沒填也能跑，只是不顯示。")]
    [SerializeField] private TMP_Text completeTrendText;

    [Tooltip("放棄彈窗上的同一行（P2-14）。沒填也能跑，只是不顯示。")]
    [SerializeField] private TMP_Text abandonedTrendText;

    // ⚠️ 2026-09-13（P2-14）拿掉了 P2-7 的 targetDifficultyField。場景裡那個輸入框物件要手動刪掉，
    // 不刪的話它還會顯示在彈窗上、打了數字也沒有作用

    private void Awake()
    {
        if (inputController == null) inputController = FindFirstObjectByType<TouchInputController>();
        if (levelController == null) levelController = FindFirstObjectByType<LevelController>();

        // ⚠️ 找不到不報錯：回饋收集場景與出題工具場景都沒有主畫面，那裡「退出」維持舊行為
        if (mainMenu == null) mainMenu = FindFirstObjectByType<MainMenuPanel>();

        if (inputController == null)
            Debug.LogError("LevelResultPanels 找不到 TouchInputController：彈窗永遠不會出現。", this);
        if (levelController == null)
            Debug.LogError("LevelResultPanels 找不到 LevelController：「下一關」按了不會有反應。", this);
        if (completePanel == null)
            Debug.LogError("LevelResultPanels 的 completePanel 欄位沒填：彈窗不會被顯示或隱藏。", this);

        // ⚠️ 這是會讓彈窗「永遠不出現而且不報錯」的接法，所以在這裡吵起來。
        // 停用的物件不跑 Update，掛在自己身上的話第一幀就把自己鎖死了（見類別說明）
        else if (completePanel == gameObject)
            Debug.LogError(
                "LevelResultPanels 掛在它自己要開關的 Panel 上了。一關開始還沒過關時它會把自己 " +
                "SetActive(false)，而停用的物件不再跑 Update——彈窗從此永遠不會出現。" +
                "請把這支腳本移到另一個物件上（照 BoardActionButtons 的做法）。", this);
    }

    /// <summary>
    /// 把整個 Panel 登記成「按在這裡不要抓片」——<b>這就是「全螢幕鎖定」的實作</b>。
    ///
    /// ⚠️ <b>只登記一次就好，不用跟著顯示開關</b>：<c>IsOverBlockingArea</c> 本來就會
    /// 跳過停用的物件，所以彈窗隱藏時那塊自動不擋。跟四顆按鈕完全一樣的做法。
    ///
    /// 彈窗自己的兩顆 Button 不受影響——它們走 Unity 的 <c>EventSystem</c>，
    /// 跟 <see cref="TouchInputController"/> 的矩形判定是兩套獨立的東西。
    /// </summary>
    private void OnEnable()
    {
        completeArea = RegisterArea(completePanel);
        abandonedArea = RegisterArea(abandonedPanel);
    }

    private void OnDisable()
    {
        if (inputController == null) return;
        inputController.UnregisterPressBlockingArea(completeArea);
        inputController.UnregisterPressBlockingArea(abandonedArea);
    }

    /// <param name="panel">彈窗根物件，可以是 null。</param>
    /// <returns>登記進去的矩形，之後取消登記要用；沒登記成功時是 null。</returns>
    private RectTransform RegisterArea(GameObject panel)
    {
        if (panel == null || inputController == null) return null;

        RectTransform area = panel.GetComponent<RectTransform>();
        if (area == null)
        {
            Debug.LogError($"{panel.name} 上沒有 RectTransform，不是 UI 物件？" +
                           "彈窗蓋著的時候盤面還是抓得到片。", this);
            return null;
        }

        inputController.RegisterPressBlockingArea(area);
        return area;
    }

    private RectTransform completeArea;
    private RectTransform abandonedArea;

    /// <summary>
    /// 每幀重算「該不該顯示」。跟四顆按鈕同一個模式：只是過關狀態的函數，不訂閱任何東西。
    /// </summary>
    private void Update()
    {
        // ⚠️ 兩個彈窗互斥，但那不是這裡保證的——LevelData 那邊已經擋死
        // 「已過關不能放棄、已放棄不能過關」，所以這裡各問各的就好
        SetShown(completePanel, inputController != null && inputController.IsLevelComplete);
        SetShown(abandonedPanel, inputController != null && inputController.IsLevelAbandoned);

        // 方向。兩行都寫同一句——哪一個彈窗開著，看到的就是那一行
        string trend = TrendTextFor(levelController, LevelSession.Current?.Data?.Settlement);
        SetText(completeTrendText, trend);
        SetText(abandonedTrendText, trend);
    }

    /// <summary>
    /// 方向的文字。
    /// ⚠️ <b>還沒有明細時是空字串</b>：彈窗可能比結算早一幀出現（兩支 <c>Update</c> 誰先跑不固定），
    /// 那一幀留白，下一幀就補上。一直空白的話代表沒結算——看 <c>SkillRatingTracker.TrySettle</c> 的守衛。
    /// </summary>
    /// <param name="settlement">這一局的結算明細，可能沒有。</param>
    /// <returns>要顯示的文字。</returns>
    private static string TrendTextFor(LevelController levelController, RatingUpdate? settlement)
    {
        // ⭐ 具象題是隨機抽的，「下一題會難一點」在那個模式裡是假的（2026-09-23）。
        // ⚠️ 這一行不能省：實力照樣有結算（具象題的難度跟生成題在同一把尺上），
        // 所以 settlement 是有值的——不判模式的話它會理直氣壯地講一句不成立的話
        if (levelController != null && levelController.CurrentMode == GameMode.Figurative)
            return "再來一題";

        if (!settlement.HasValue) return string.Empty;

        switch (settlement.Value.Trend)
        {
            case NextPuzzleTrend.Harder: return "下一題會難一點";
            case NextPuzzleTrend.Easier: return "下一題會簡單一點";
            default: return "下一題差不多";
        }
    }

    /// <param name="label">文字元件，可以是 null。</param>
    /// <param name="text">要顯示的文字。</param>
    private static void SetText(TMP_Text label, string text)
    {
        if (label == null) return;
        if (label.text == text) return;     // 免得每幀都讓 TMP 重排版
        label.text = text;
    }

    /// <param name="panel">要顯示或隱藏的物件，可以是 null。</param>
    /// <param name="shown">要不要顯示。</param>
    private static void SetShown(GameObject panel, bool shown)
    {
        if (panel == null) return;
        if (panel.activeSelf == shown) return;      // 免得每幀都送一次狀態變更
        panel.SetActive(shown);
    }

    /// <summary>
    /// 「退出」被按下。接在 Unity <c>Button.onClick</c> 上。
    ///
    /// ⭐ <b>2026-09-23 起：場景裡有主畫面的話就回主畫面</b>（可以換模式），
    /// 沒有主畫面才維持舊行為關掉程式。⚠️ <b>盤面刻意不卸載</b>——主畫面是同場景的 Panel，
    /// 蓋在上面而已，所以那個 Panel 一定要不透明。真的要卸載盤面是換場景那條路，見類別說明。
    ///
    /// ⚠️ <c>Application.Quit()</c> <b>在 Editor 裡完全不做事</b>，
    /// 所以測試時會以為按鈕壞了。Editor 走「停止播放」那條才看得出反應。
    /// <c>UnityEditor</c> 這個命名空間<b>只存在於 Editor</b>，所以整段一定要包在
    /// <c>#if UNITY_EDITOR</c> 裡——沒包的話 build 會直接編不過。
    /// </summary>
    public void OnExitPressed()
    {
        if (mainMenu != null)
        {
            mainMenu.Show();
            return;
        }

#if UNITY_EDITOR
        UnityEditor.EditorApplication.isPlaying = false;
#else
        Application.Quit();
#endif
    }

    /// <summary>
    /// 「下一關」被按下。接在 Unity <c>Button.onClick</c> 上。
    ///
    /// <b>下一關的難度跟著玩家的實力走</b>（P2-14）——這裡不決定，<see cref="LevelController.EnterNextLevel()"/> 去問實力。
    /// 換關本身走的還是那條已經實測過的五步順序。
    ///
    /// ⚠️ <b>按下去會凍住畫面零點幾到一點多秒</b>（Editor 裡目標 1500 約 1.2 ～ 1.7 秒）。
    /// 那是生成在跑，不是當掉。<b>刻意不做「生成中…」</b>——Unity 在同一幀改 UI 文字
    /// 再做長運算，畫面根本不會更新，要做得先 <c>yield</c> 一幀，而現在不值得為它多一條協程。
    /// </summary>
    public void OnNextLevelPressed()
    {
        if (levelController != null) levelController.EnterNextLevel();
    }

    /// <summary>
    /// 放棄彈窗的「下一題」被按下（P2-13）。接在 Unity <c>Button.onClick</c> 上。
    ///
    /// <b>難度跟著實力走</b>，跟過關彈窗那顆是同一條路。
    /// ⭐ 這不是「送他一題簡單的」：放棄已經扣過分了（−K × E），
    /// 所以<b>「用放棄刷簡單題」不是漏洞</b>
    /// ——題目確實變簡單，但他的實力值也真的變低了。
    /// </summary>
    public void OnNextAfterAbandonPressed()
    {
        if (levelController != null) levelController.EnterNextLevel();
    }

}
