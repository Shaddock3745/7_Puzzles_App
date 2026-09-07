using Tangram.Core;
using UnityEngine;

/// <summary>
/// 旋轉／翻面鈕的顯示與轉接（P1-8，互動設計 §5）。
///
/// <b>這支不做事，它只負責兩件：什麼時候顯示，以及把按下轉給控制器。</b>
/// 轉一格的規則住在 <see cref="TouchInputController.RotateSelected"/>——
/// 按鈕只是眾多觸發器裡的一個，之後加鍵盤快捷鍵或手勢時規則不用再寫一份（2026-09-07 定案）。
///
/// <b>為什麼不引用 <c>UnityEngine.UI</c></b>：這裡收的是 <c>GameObject</c> 不是 <c>Button</c>，
/// 按下由 Unity 的 <c>Button.onClick</c> 在 Inspector 上接到
/// <see cref="OnRotatePressed"/> / <see cref="OnFlipPressed"/>。
/// 少一個組件相依，也表示按鈕之後換成別的做法（自繪、手勢）不用改這支。
///
/// <b>場景要做的事</b>（P1-8 的接線）：
/// <list type="number">
/// <item>建兩顆 UI Button（Unity 會自動補上 Canvas 與 EventSystem），擺右下角</item>
/// <item>兩顆的 <c>onClick</c> 分別接到這支的 <c>OnRotatePressed</c> / <c>OnFlipPressed</c></item>
/// <item>把兩顆的 <c>GameObject</c> 填進下面兩個欄位</item>
/// </list>
///
/// <b>「不給抓片的矩形」這支會自己註冊</b>（2026-09-07），不用在
/// <c>TouchInputController</c> 上手動填。原本是手動的，而漏填的症狀是
/// <b>按鈕完全沒反應，還不報錯</b>——按鈕的按下會先被當成「按在空白處」把選取清掉，
/// 等 <c>onClick</c> 送到時已經沒有作用對象了。
/// </summary>
[DisallowMultipleComponent]
public class PieceActionButtons : MonoBehaviour
{
    [Tooltip("選取狀態的來源。留空的話 Awake 會自己找。")]
    [SerializeField] private TouchInputController inputController;

    [Tooltip("旋轉鈕的 GameObject。它的 Button.onClick 要接到這支的 OnRotatePressed。")]
    [SerializeField] private GameObject rotateButton;

    [Tooltip("翻面鈕的 GameObject。它的 Button.onClick 要接到這支的 OnFlipPressed。")]
    [SerializeField] private GameObject flipButton;

    private void Awake()
    {
        if (inputController == null) inputController = FindFirstObjectByType<TouchInputController>();
        if (inputController == null)
            Debug.LogError("PieceActionButtons 找不到 TouchInputController：按鈕永遠不會出現。", this);

        if (rotateButton == null || flipButton == null)
            Debug.LogError("PieceActionButtons 的按鈕欄位沒填滿：沒填的那顆不會被顯示或隱藏。", this);
    }

    /// <summary>
    /// 把兩顆按鈕的矩形登記成「按在這裡不要抓片」。
    ///
    /// ⚠️ <b>這件事非做不可，不是防呆。</b>沒登記的話按鈕的按下會先被當成
    /// 「按在空白處」而把選取清掉，等 <c>onClick</c> 送到時已經沒有作用對象——
    /// <b>按鈕看起來完全沒反應，而且不報錯</b>。
    ///
    /// 在這裡做而不是叫人去 Inspector 填，是因為那是「要記得做才會動」的東西。
    /// </summary>
    private void OnEnable()
    {
        rotateArea = RegisterArea(rotateButton);
        flipArea = RegisterArea(flipButton);
    }

    private void OnDisable()
    {
        if (inputController == null) return;
        inputController.UnregisterPressBlockingArea(rotateArea);
        inputController.UnregisterPressBlockingArea(flipArea);
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
                           "按這顆會把選取清掉，按鈕會看起來沒反應。", this);
            return null;
        }

        inputController.RegisterPressBlockingArea(area);
        return area;
    }

    private RectTransform rotateArea;
    private RectTransform flipArea;

    /// <summary>
    /// 每幀重算「該不該顯示」。
    ///
    /// <b>按鈕不訂閱任何東西</b>——它只是選取狀態的函數，所以換關、版面重算
    /// 把選取清掉時，按鈕自己就消失了，不必有人記得去通知它。
    /// 代價是每幀算一次，而那是兩個布林比較。
    /// </summary>
    private void Update()
    {
        // ActionTarget 已經把「有選取 ＋ 那片沒在被拖」兩個條件包在一起了。
        // 刻意共用同一個算式：顯示條件與作用條件分岔的話，
        // 會出現「看得到但按了沒反應」或反過來，兩種都很難查
        PieceView target = inputController != null ? inputController.ActionTarget : null;

        SetShown(rotateButton, target != null);

        // 翻面鈕再多一層：其餘片的鏡像等於某個旋轉，翻了是做白工
        SetShown(flipButton, target != null && SnapSolver.CanFlip(target.Kind));
    }

    /// <param name="button">要顯示或隱藏的按鈕物件，可以是 null。</param>
    /// <param name="shown">要不要顯示。</param>
    private static void SetShown(GameObject button, bool shown)
    {
        if (button == null) return;
        if (button.activeSelf == shown) return;      // 免得每幀都送一次狀態變更
        button.SetActive(shown);
    }

    /// <summary>旋轉鈕被按下。接在 Unity <c>Button.onClick</c> 上。</summary>
    public void OnRotatePressed()
    {
        Debug.Log("Rotate");
        if (inputController != null) inputController.RotateSelected();
    }

    /// <summary>翻面鈕被按下。接在 Unity <c>Button.onClick</c> 上。</summary>
    public void OnFlipPressed()
    {
        Debug.Log("Flip");
        if (inputController != null) inputController.FlipSelected();
    }
}
