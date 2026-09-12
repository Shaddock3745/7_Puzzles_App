using TMPro;
using UnityEngine;

/// <summary>
/// 過關之後的完成彈窗（P1-10，互動設計 §7）。
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
/// <item>⚠️ <b>P2-7 臨時的</b>：對話框上再放一個 <c>TMP_InputField</c>，拖進
/// <see cref="targetDifficultyField"/>。那是下一關的目標難度（手動輸入）</item>
/// </list>
///
/// ⚠️ <b>輸入框那一段是臨時的，P2-14 接上 ELO 時整段拿掉。</b>
/// 但<b>位置是對的、不會換</b>——「下一關要多難」本來就是在這一刻決定的
/// （玩家剛打完一題、實力值剛更新完）。到時候只是把「讀輸入框」換成「讀 <c>SkillRating</c>」，
/// 資料流一行都不用改。
///
/// ⚠️ <b>Panel 的 <c>RectTransform</c> 一定要滿版</b>，因為擋片區擋的就是它那塊矩形。
/// 只做成中間一個小對話框的話，<b>對話框外面的盤面還是抓得到片</b>。
///
/// ⚠️ <b>為什麼腳本不能掛在 Panel 上</b>：隱藏是用 <c>SetActive(false)</c> 做的，
/// 而被停用的物件不會再跑 <c>Update</c>——一關開始還沒過關 → 第一幀就把自己關掉
/// → 從此再也開不回來，<b>而且不報錯</b>（除錯記錄 004 踩過一次）。
/// </summary>
[DisallowMultipleComponent]
public class LevelCompletePanel : MonoBehaviour
{
    [Tooltip("過關狀態與擋片區的來源。留空的話 Awake 會自己找。")]
    [SerializeField] private TouchInputController inputController;

    [Tooltip("「下一關」要叫它換關。留空的話 Awake 會自己找。")]
    [SerializeField] private LevelController levelController;

    [Tooltip("整個彈窗的根物件。⚠️ 它的 RectTransform 要滿版，擋片區擋的就是它。")]
    [SerializeField] private GameObject completePanel;

    // ───────── 以下是 P2-7 的臨時接法，P2-14 接上 ELO 時整段拿掉 ─────────

    [Tooltip("⚠️ P2-7 臨時的。下一關的目標難度。留空或打錯字就沿用上一次的值。")]
    [SerializeField] private TMP_InputField targetDifficultyField;

    /// <summary>
    /// ⚠️ <b>P2-7 臨時的</b>：上一次用過的目標難度，輸入框讀不出數字時的退路。
    /// 初始值跟 <c>LevelController.startingTargetDifficulty</c> 一樣是 1200，
    /// 但<b>兩邊刻意不互相參照</b>——這一整段之後要拿掉，牽線只會讓拿掉變麻煩。
    /// </summary>
    private int lastTargetDifficulty = 1200;

    private void Awake()
    {
        if (inputController == null) inputController = FindFirstObjectByType<TouchInputController>();
        if (levelController == null) levelController = FindFirstObjectByType<LevelController>();

        if (inputController == null)
            Debug.LogError("LevelCompletePanel 找不到 TouchInputController：彈窗永遠不會出現。", this);
        if (levelController == null)
            Debug.LogError("LevelCompletePanel 找不到 LevelController：「下一關」按了不會有反應。", this);
        if (completePanel == null)
            Debug.LogError("LevelCompletePanel 的 completePanel 欄位沒填：彈窗不會被顯示或隱藏。", this);

        // ⚠️ 這是會讓彈窗「永遠不出現而且不報錯」的接法，所以在這裡吵起來。
        // 停用的物件不跑 Update，掛在自己身上的話第一幀就把自己鎖死了（見類別說明）
        else if (completePanel == gameObject)
            Debug.LogError(
                "LevelCompletePanel 掛在它自己要開關的 Panel 上了。一關開始還沒過關時它會把自己 " +
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
        panelArea = RegisterArea(completePanel);
    }

    private void OnDisable()
    {
        if (inputController == null) return;
        inputController.UnregisterPressBlockingArea(panelArea);
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

    private RectTransform panelArea;

    /// <summary>
    /// 每幀重算「該不該顯示」。跟四顆按鈕同一個模式：只是過關狀態的函數，不訂閱任何東西。
    /// </summary>
    private void Update()
    {
        SetShown(completePanel, inputController != null && inputController.IsLevelComplete);
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
    /// ⚠️ <b>目前是關掉程式，之後會變成回主畫面</b>（那時候才真的需要換場景）。
    ///
    /// ⚠️ <c>Application.Quit()</c> <b>在 Editor 裡完全不做事</b>，
    /// 所以測試時會以為按鈕壞了。Editor 走「停止播放」那條才看得出反應。
    /// <c>UnityEditor</c> 這個命名空間<b>只存在於 Editor</b>，所以整段一定要包在
    /// <c>#if UNITY_EDITOR</c> 裡——沒包的話 build 會直接編不過。
    /// </summary>
    public void OnExitPressed()
    {
#if UNITY_EDITOR
        UnityEditor.EditorApplication.isPlaying = false;
#else
        Application.Quit();
#endif
    }

    /// <summary>
    /// 「下一關」被按下。接在 Unity <c>Button.onClick</c> 上。
    ///
    /// <b>下一關的難度來自 <see cref="targetDifficultyField"/></b>（P2-7）。
    /// 換關本身走的還是 <see cref="LevelController.EnterNextLevel(int)"/>，
    /// 而它內部走的還是那條已經實測過的五步順序。
    ///
    /// ⚠️ <b>按下去會凍住畫面零點幾到一點多秒</b>（Editor 裡目標 1500 約 1.2 ～ 1.7 秒）。
    /// 那是生成在跑，不是當掉。<b>刻意不做「生成中…」</b>——Unity 在同一幀改 UI 文字
    /// 再做長運算，畫面根本不會更新，要做得先 <c>yield</c> 一幀，而現在不值得為它多一條協程。
    /// </summary>
    public void OnNextLevelPressed()
    {
        if (levelController == null) return;
        levelController.EnterNextLevel(ReadTargetDifficulty());
    }

    /// <summary>
    /// 讀輸入框裡的目標難度。
    ///
    /// ⚠️ <b>刻意寫得很寬鬆</b>（2026-09-12 定）：這是內部測試與拍片用的入口，
    /// 不是玩家會碰的東西，所以<b>壞輸入不提示、不擋，直接退回上一次用過的值</b>。
    /// 空白、打錯字、沒有輸入框都走同一條路。
    /// 真的變成玩家介面時要補的是提示，不是這裡的判斷。
    ///
    /// 範圍不在這裡管——超出去由 <c>TargetedPuzzleGenerator.ClampTarget</c> 夾住，
    /// <b>兩個地方都夾的話哪一邊生效會變成要查的問題</b>。
    /// </summary>
    /// <returns>目標難度。讀不到就是上一次用過的值。</returns>
    private int ReadTargetDifficulty()
    {
        if (targetDifficultyField != null
            && int.TryParse(targetDifficultyField.text, out int parsed))
            lastTargetDifficulty = parsed;

        return lastTargetDifficulty;
    }
}
