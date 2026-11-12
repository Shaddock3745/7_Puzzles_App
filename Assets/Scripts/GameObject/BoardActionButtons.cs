using Tangram.Core;
using UnityEngine;

/// <summary>
/// 盤面層按鈕的顯示與轉接：**undo** 與 **清空**（互動設計 §6）。
///
/// <b>跟 <see cref="PieceActionButtons"/> 是一對</b>，分界線是**作用對象**：
/// <list type="bullet">
/// <item><see cref="PieceActionButtons"/>——作用在<b>選取的那一片</b>上（旋轉、翻面）</item>
/// <item>這一支——作用在<b>整個盤面</b>上（undo、清空）</item>
/// </list>
/// 所以顯示條件的來源也不同：那一支看**選取**，這一支看**盤面與步驟堆疊**。
///
/// <b>兩支都不做事</b>，只負責「什麼時候顯示」與「把按下轉給控制器」。
/// 規則住在 <see cref="TouchInputController.UndoLastPlacement"/> 與
/// <see cref="TouchInputController.ClearBoard"/>——按鈕只是眾多觸發器裡的一個，
/// 之後加鍵盤快捷鍵或手勢時規則不用再寫一份（2026-09-07 定案）。
///
/// <b>場景要做的事</b>：
/// <list type="number">
/// <item>建兩顆 UI Button，<c>onClick</c> 分別接到 <see cref="OnUndoPressed"/> /
/// <see cref="OnClearPressed"/></item>
/// <item>⚠️ <b>這支腳本掛在另一個物件上，不能掛在那兩顆按鈕身上</b>（理由見下）</item>
/// <item>把兩顆的 <c>GameObject</c> 填進下面兩個欄位</item>
/// <item>⭐ 放棄的確認小窗（2026-10-29）：Canvas 底下一個全螢幕 Panel（蓋在按鈕排上面、結果彈窗下面），
/// 裡面「繼續拼」與「放棄」兩顆鈕，分別接 <see cref="OnAbandonCancelPressed"/>／<see cref="OnAbandonConfirmPressed"/>，
/// Panel 填進 <c>abandonConfirmPanel</c></item>
/// </list>
///
/// ⚠️ <b>擺位：四顆鈕不要排在會互相推擠的群組裡。</b>
/// 它們出現／消失的時機**兩兩都不同**——旋轉／翻面看選取、undo 看步驟堆疊、
/// 清空看盤上有沒有片。綁在一起的話玩家每放一片、每退一步都會看到按鈕位移，
/// 很容易誤觸。§5 已經為了翻面鈕記過同一條（兩顆按鈕時不要把整組置中）。
///
/// ⚠️ <b>為什麼腳本不能掛在它要開關的那顆按鈕上。</b>
/// 隱藏是用 <c>SetActive(false)</c> 做的，而<b>被停用的物件不會再跑 <c>Update</c></b>——
/// 掛在自己身上的話，一關開始堆疊是空的 → 第一幀就把自己關掉 → 從此再也沒有機會開回來，
/// <b>按鈕永遠不出現，而且不報錯</b>。（2026-09-09 真的踩到了。）
///
/// <b>為什麼非用 <c>SetActive</c> 不可</b>（不能只關掉圖片）：
/// <c>TouchInputController.IsOverBlockingArea</c> 是靠 <c>activeInHierarchy</c> 決定要不要跳過的。
/// 只關渲染的話，看不見的按鈕<b>還是會擋住點擊</b>，那塊就變成點不到片的死角（互動設計 §5）。
/// </summary>
[DisallowMultipleComponent]
public class BoardActionButtons : MonoBehaviour
{
    [Tooltip("盤面與步驟堆疊的來源。留空的話 Awake 會自己找。")]
    [SerializeField] private TouchInputController inputController;

    [Tooltip("undo 鈕的 GameObject。它的 Button.onClick 要接到這支的 OnUndoPressed。")]
    [SerializeField] private GameObject undoButton;

    [Tooltip("清空鈕的 GameObject。它的 Button.onClick 要接到這支的 OnClearPressed。")]
    [SerializeField] private GameObject clearButton;

    [Tooltip("放棄鈕的 GameObject。onClick 接到這支的 OnAbandonPressed。"
             + "⚠️ 它跟另外兩顆不一樣：卡夠久才會出現，見下面那個門檻。")]
    [SerializeField] private GameObject abandonButton;

    [Tooltip("⭐ 放棄的確認小窗（2026-10-29，第二輪回饋）。全螢幕、要擋住底下的按鈕（Image 勾 Raycast Target）；"
             + "裡面兩顆鈕：「繼續拼」接 OnAbandonCancelPressed、「放棄」接 OnAbandonConfirmPressed。"
             + "留空的話按放棄就直接放棄（回饋收集場景是這樣）。⚠️ 不能是這支腳本自己所在的物件。")]
    [SerializeField] private GameObject abandonConfirmPanel;

    [Tooltip("提示鈕的 GameObject（2026-09-15）。onClick 接到這支的 OnHintPressed。"
             + "還有次數可以按時才出現。")]
    [SerializeField] private GameObject hintButton;

    [Tooltip("⭐「回主畫面」鈕（中途離開，2026-10-23）。onClick 接到這支的 OnLeavePressed。"
             + "這一關還在進行中才出現；場景裡沒有主畫面（回饋收集）就永遠不出現。")]
    [SerializeField] private GameObject leaveButton;

    [Tooltip("按「回主畫面」時要叫它出來。留空的話 Awake 會自己找；找不到就不顯示那顆鈕。")]
    [SerializeField] private MainMenuPanel mainMenu;

    [Tooltip("⭐ 提示與放棄共用的唯一門檻（秒）：每隔這麼久多開放一次提示，最多 3 次；"
             + "放棄鈕跟第三次提示同時出現（＝ 這個數字 × 3）。"
             + "⚠️ 展示時一位玩家只玩 2~3 局，展示版調小（例如 5 → 5／10／15 秒）。")]
    [Min(1f)]
    [SerializeField] private float hintIntervalSeconds = 45f;

    /// <summary>
    /// 放棄鈕的門檻＝提示間隔 × 3，<b>不另外設</b>（2026-09-15 定）。
    /// ⚠️ 原本放棄門檻是自己一個 Inspector 數字——兩個數字的話 demo 時漏改一個，
    /// 玩家就會<b>先看到「投降」才看到「幫忙」</b>，那正是設計上要避免的順序。
    /// </summary>
    private float StuckSecondsBeforeAbandon => hintIntervalSeconds * LevelData.MaximumHints;

    /// <summary>現在的提示間隔（秒）。主畫面的「快速提示」開場時讀一次，記下場景原本的值（2026-10-30）。</summary>
    public float HintIntervalSeconds => hintIntervalSeconds;

    /// <summary>
    /// ⭐ 改提示間隔（2026-10-30，展示版主畫面的「快速提示」勾選叫這支）。<b>真相還是這個欄位</b>，勾選不另存一份。
    ///
    /// 改了<b>馬上生效</b>：按鈕每幀拿「卡了幾秒」跟間隔比，所以玩到一半回主畫面改小、再回去，
    /// 已經累積的提示與放棄鈕會一起出現——同一套規則照算，不是 bug。
    /// </summary>
    /// <param name="seconds">新的間隔（秒），小於 1 時夾到 1（同 Inspector 的下限）。</param>
    public void SetHintIntervalSeconds(float seconds) => hintIntervalSeconds = Mathf.Max(1f, seconds);

    private void Awake()
    {
        if (inputController == null) inputController = FindFirstObjectByType<TouchInputController>();
        if (inputController == null)
            Debug.LogError("BoardActionButtons 找不到 TouchInputController：按鈕永遠不會出現。", this);

        // 找不到不報錯：回饋收集場景本來就沒有主畫面，那裡「回主畫面」鈕不該出現
        if (mainMenu == null) mainMenu = FindFirstObjectByType<MainMenuPanel>();

        if (undoButton == null || clearButton == null)
            Debug.LogError("BoardActionButtons 的按鈕欄位沒填滿：沒填的那顆不會被顯示或隱藏。", this);

        // ⚠️ 這是會讓按鈕「永遠不出現而且不報錯」的接法，所以在這裡吵起來。
        // 停用的物件不跑 Update，掛在自己身上的話第一幀就把自己鎖死了（見類別說明）
        if (undoButton == gameObject || clearButton == gameObject)
            Debug.LogError(
                "BoardActionButtons 掛在它自己要開關的按鈕上了。第一幀條件不成立時它會把自己 " +
                "SetActive(false)，而停用的物件不再跑 Update——按鈕從此永遠不會出現。" +
                "請把這支腳本移到另一個物件上（照 PieceActionButtons 的做法）。", this);

        if (abandonConfirmPanel == gameObject)
        {
            Debug.LogError("BoardActionButtons 掛在放棄的確認小窗上了。小窗藏起來時這支會跟著被停用，" +
                           "所有按鈕從此不再更新。請把這支腳本移到另一個物件上；這一局按放棄會直接放棄。", this);
            abandonConfirmPanel = null;
        }
        else if (abandonConfirmPanel != null)
            abandonConfirmPanel.SetActive(false);
        else if (abandonButton != null && mainMenu != null)
            // 有主畫面＝正式的遊戲場景，那裡應該要有確認小窗；回饋收集場景沒有主畫面，不吵
            Debug.LogWarning("BoardActionButtons 的放棄確認小窗沒填：按放棄會直接放棄，" +
                             "跟提示鈕按錯就回不去（第二輪回饋）。", this);
    }

    /// <summary>
    /// 把兩顆按鈕的矩形登記成「按在這裡不要抓片」。
    ///
    /// ⚠️ <b>這件事非做不可，不是防呆。</b>沒登記的話按鈕的按下會同時
    /// <b>把手指底下的片抓起來</b>，變成「按 undo 順便拖了一片」。
    /// 對旋轉／翻面鈕來說還會更糟——那一下會先被當成「按在空白處」清掉選取，
    /// 等 <c>onClick</c> 送到時已經沒有作用對象了。
    /// </summary>
    private void OnEnable()
    {
        undoArea = RegisterArea(undoButton);
        clearArea = RegisterArea(clearButton);
        abandonArea = RegisterArea(abandonButton);
        hintArea = RegisterArea(hintButton);
        leaveArea = RegisterArea(leaveButton);
        // 小窗開著時底下的片不能被抓到；藏起來時擋片區會自己跳過它
        abandonConfirmArea = RegisterArea(abandonConfirmPanel);
    }

    private void OnDisable()
    {
        if (inputController == null) return;
        inputController.UnregisterPressBlockingArea(undoArea);
        inputController.UnregisterPressBlockingArea(clearArea);
        inputController.UnregisterPressBlockingArea(abandonArea);
        inputController.UnregisterPressBlockingArea(hintArea);
        inputController.UnregisterPressBlockingArea(leaveArea);
        inputController.UnregisterPressBlockingArea(abandonConfirmArea);
    }

    /// <param name="button">按鈕物件，可以是 null。</param>
    /// <returns>登記進去的矩形，之後取消登記要用；沒登記成功時是 null。</returns>
    private RectTransform RegisterArea(GameObject button)
    {
        if (button == null || inputController == null) return null;

        RectTransform area = button.GetComponent<RectTransform>();
        if (area == null)
        {
            Debug.LogError($"{button.name} 上沒有 RectTransform，不是 UI 物件？" +
                           "按這顆會連手指底下的片一起抓起來。", this);
            return null;
        }

        inputController.RegisterPressBlockingArea(area);
        return area;
    }

    private RectTransform undoArea;
    private RectTransform clearArea;
    private RectTransform abandonArea;
    private RectTransform hintArea;
    private RectTransform leaveArea;
    private RectTransform abandonConfirmArea;

    /// <summary>
    /// 每幀重算「該不該顯示」。
    ///
    /// <b>按鈕不訂閱任何東西</b>——它只是控制器那兩個算式的函數，
    /// 所以換關把堆疊換掉、退到沒得退、清完盤面時，按鈕自己就消失了，
    /// 不必有人記得通知它。
    ///
    /// <b>條件不成立是「隱藏」不是「變灰」</b>（2026-09-09 定案）：擺一個按不到的按鈕
    /// 玩家會去戳，§5 為了旋轉鈕定過同一條規矩。
    /// 而隱藏中的矩形 <c>IsOverBlockingArea</c> 會跳過，所以那塊不會變成點不到片的死角——
    /// <b>抽屜開著時這兩顆退場，靠的就是這件事</b>（不然它們會把底下那幾格縮圖吃掉）。
    /// </summary>
    private void Update()
    {
        // 顯示條件與作用條件刻意共用同一個算式（見那兩個屬性的說明）
        SetShown(undoButton, inputController != null && inputController.CanUndo);
        SetShown(clearButton, inputController != null && inputController.CanClearBoard);
        SetShown(abandonButton, CanAbandon);
        SetShown(hintButton, CanUseHint);
        SetShown(leaveButton, CanLeave);

        // 小窗開著、但這一局已經放棄不了了（例如被別的路結束掉）：一起收掉，不留一個按了沒反應的窗。
        // 只關不開——打開只有按放棄鈕那一條路
        if (abandonConfirmPanel != null && abandonConfirmPanel.activeSelf && !CanAbandon)
            abandonConfirmPanel.SetActive(false);
    }

    /// <summary>
    /// 提示鈕該不該出現（2026-09-15）：<b>按下去真的會有東西，而且抽屜沒開</b>。
    /// 「這一關結束了」不用另外寫——結束之後模型層回的次數就是 0。
    ///
    /// 💡 跟放棄鈕同一個形狀：前一個間隔根本不佔位。
    /// ⭐ 顯示條件跟作用條件是同一個算式（<c>LevelData.CanUseHintAt</c> 與 <c>TryUseHint</c> 共用挑片），
    /// 所以次數還有、但七片裡已經沒東西可提示時，鈕會自己退場，不會擺一顆按了沒反應的鈕。
    /// </summary>
    private bool CanUseHint =>
        inputController != null
        && !inputController.IsTrayDrawerOpen
        && inputController.CanUseHint(hintIntervalSeconds);

    /// <summary>
    /// 放棄鈕該不該出現：<b>卡夠久，而且這一關還沒結束</b>。
    ///
    /// ⭐ <b>「卡夠久才出現」一次解掉兩個問題</b>：
    /// ① 按鈕已經很擠（旋轉／翻面、undo／清空、抽屜箭頭、回收框），而它前
    /// <see cref="StuckSecondsBeforeAbandon"/> 秒<b>根本不佔位</b>；
    /// ② 放棄不可逆而且會扣實力值，但<b>只有真的卡住的人看得到它</b>。
    ///
    /// ⚠️ <b>原本因為 ② 刻意不做二次確認（2026-09-12），2026-10-29 推翻，改成跳確認小窗</b>（第二輪回饋）：
    /// 門檻擋得掉「沒卡住的人誤觸」，擋不掉「卡住的人要按提示、按到旁邊的放棄」——
    /// 而看得到放棄鈕的人，正好是一直在按提示的人。
    ///
    /// ⭐ 這個算式同時是<b>小窗能不能留著</b>的條件（見 <see cref="Update"/>）：顯示、作用、收窗三件事共用一個。
    ///
    /// 💡 它跟 undo／清空是同一個形狀：<b>按鈕只是某個算式的函數，不訂閱任何東西</b>。
    /// ⭐ 之後三級提示鈕照同一個框架接上來，各自有自己的門檻（而且要比放棄早）。
    /// </summary>
    private bool CanAbandon =>
        inputController != null
        && !inputController.IsLevelComplete
        && !inputController.IsLevelAbandoned
        && !inputController.IsTrayDrawerOpen
        && inputController.StuckSeconds >= StuckSecondsBeforeAbandon;

    /// <param name="button">要顯示或隱藏的按鈕物件，可以是 null。</param>
    /// <param name="shown">要不要顯示。</param>
    private static void SetShown(GameObject button, bool shown)
    {
        if (button == null) return;
        if (button.activeSelf == shown) return;      // 免得每幀都送一次狀態變更
        button.SetActive(shown);
    }

    /// <summary>undo 鈕被按下。接在 Unity <c>Button.onClick</c> 上。</summary>
    public void OnUndoPressed()
    {
        if (inputController != null) inputController.UndoLastPlacement();
    }

    /// <summary>清空鈕被按下。接在 Unity <c>Button.onClick</c> 上。</summary>
    public void OnClearPressed()
    {
        if (inputController != null) inputController.ClearBoard();
    }

    /// <summary>提示鈕被按下（2026-09-15）。接在 Unity <c>Button.onClick</c> 上。</summary>
    public void OnHintPressed()
    {
        if (inputController != null) inputController.UseHint(hintIntervalSeconds);
    }

    /// <summary>
    /// 放棄鈕被按下。接在 Unity <c>Button.onClick</c> 上。
    ///
    /// ⭐ 2026-10-29 起<b>只打開確認小窗</b>，真的放棄要在小窗裡再按一次（理由見 <see cref="CanAbandon"/>）。
    /// 小窗沒接的話照舊直接放棄——放棄鈕不能因為少接一個物件就壞掉。
    /// </summary>
    public void OnAbandonPressed()
    {
        if (!CanAbandon) return;        // 跟顯示同一個算式：鈕消失的那一幀剛好被按到，不開窗

        if (abandonConfirmPanel == null)
        {
            inputController.AbandonLevel();
            return;
        }
        abandonConfirmPanel.SetActive(true);
    }

    /// <summary>確認小窗的「放棄」（2026-10-29）。接在 Unity <c>Button.onClick</c> 上。先收窗、再真的放棄。</summary>
    public void OnAbandonConfirmPressed()
    {
        if (abandonConfirmPanel != null) abandonConfirmPanel.SetActive(false);
        if (CanAbandon) inputController.AbandonLevel();
    }

    /// <summary>確認小窗的「繼續拼」（2026-10-29）。接在 Unity <c>Button.onClick</c> 上。只收窗，什麼都不改。</summary>
    public void OnAbandonCancelPressed()
    {
        if (abandonConfirmPanel != null) abandonConfirmPanel.SetActive(false);
    }

    /// <summary>
    /// ⭐「回主畫面」的顯示條件（中途離開，2026-10-23）：有主畫面、有一關、還沒結束。
    /// 打完了不出現——結果彈窗本來就有出口，兩顆並存會讓人分不清哪一顆「算數」。
    /// 抽屜打開時整排一起藏（<c>ButtonGroupVisibility</c>），這裡不另外判斷。
    /// </summary>
    private bool CanLeave =>
        mainMenu != null
        && inputController != null
        && LevelSession.Current?.Data?.Puzzle != null
        && !inputController.IsLevelComplete
        && !inputController.IsLevelAbandoned;

    /// <summary>
    /// 「回主畫面」被按下（中途離開，2026-10-23）。接在 Unity <c>Button.onClick</c> 上。
    /// 就是叫主畫面出來——暫停這一關是主畫面 <c>Show</c> 的事，跟結果彈窗的出口走同一條，兩邊不會分家。
    /// </summary>
    public void OnLeavePressed()
    {
        if (mainMenu != null) mainMenu.Show();
    }
}
