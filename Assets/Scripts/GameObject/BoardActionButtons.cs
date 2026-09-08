using UnityEngine;

/// <summary>
/// 盤面層按鈕的顯示與轉接：**undo** 與 **清空**（P1-9，互動設計 §6）。
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

    private void Awake()
    {
        if (inputController == null) inputController = FindFirstObjectByType<TouchInputController>();
        if (inputController == null)
            Debug.LogError("BoardActionButtons 找不到 TouchInputController：按鈕永遠不會出現。", this);

        if (undoButton == null || clearButton == null)
            Debug.LogError("BoardActionButtons 的按鈕欄位沒填滿：沒填的那顆不會被顯示或隱藏。", this);

        // ⚠️ 這是會讓按鈕「永遠不出現而且不報錯」的接法，所以在這裡吵起來。
        // 停用的物件不跑 Update，掛在自己身上的話第一幀就把自己鎖死了（見類別說明）
        if (undoButton == gameObject || clearButton == gameObject)
            Debug.LogError(
                "BoardActionButtons 掛在它自己要開關的按鈕上了。第一幀條件不成立時它會把自己 " +
                "SetActive(false)，而停用的物件不再跑 Update——按鈕從此永遠不會出現。" +
                "請把這支腳本移到另一個物件上（照 PieceActionButtons 的做法）。", this);
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
    }

    private void OnDisable()
    {
        if (inputController == null) return;
        inputController.UnregisterPressBlockingArea(undoArea);
        inputController.UnregisterPressBlockingArea(clearArea);
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
    }

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
}
