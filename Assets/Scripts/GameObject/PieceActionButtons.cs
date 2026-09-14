using Tangram.Core;
using Tangram.Data;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// 旋轉／翻面鈕的顯示與轉接（P1-8，互動設計 §5）。
///
/// <b>這支不做事，它只負責兩件：什麼時候顯示（與能不能按），以及把按下轉給控制器。</b>
/// 轉一格的規則住在 <see cref="TouchInputController.RotateSelected"/>——
/// 按鈕只是眾多觸發器裡的一個，之後加鍵盤快捷鍵或手勢時規則不用再寫一份（2026-09-07 定案）。
///
/// <b>欄位收的是 <c>GameObject</c> 不是 <c>Button</c></b>：按下由 Unity 的 <c>Button.onClick</c> 在 Inspector 上接到
/// <see cref="OnRotatePressed"/> / <see cref="OnFlipPressed"/>。
/// ⚠️ 原本刻意不引用 <c>UnityEngine.UI</c>，<b>2026-09-14 為了翻轉鈕變灰破例</b>：
/// 「能不能按」要用 <c>Button.interactable</c>，它同時擋掉 <c>onClick</c> 又套上 Disabled Color，
/// 自己做一套的話等於重寫 Button。按鈕上沒有 <c>Button</c> 元件時就只是不會變灰，不會壞。
///
/// <b>場景要做的事</b>（P1-8 的接線）：
/// <list type="number">
/// <item>建三顆 UI Button（Unity 會自動補上 Canvas 與 EventSystem），擺右下角</item>
/// <item>三顆的 <c>onClick</c> 分別接到這支的 <c>OnRotatePressed</c>（逆時針）/
/// <c>OnRotateClockwisePressed</c>（順時針，2026-09-13 加）/ <c>OnFlipPressed</c></item>
/// <item>把三顆的 <c>GameObject</c> 填進下面三個欄位</item>
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

    [Tooltip("逆時針旋轉鈕的 GameObject。它的 Button.onClick 要接到這支的 OnRotatePressed。")]
    [SerializeField] private GameObject rotateButton;

    [Tooltip("順時針旋轉鈕的 GameObject（2026-09-13）。它的 Button.onClick 要接到這支的 OnRotateClockwisePressed。")]
    [SerializeField] private GameObject rotateClockwiseButton;

    [Tooltip("翻面鈕的 GameObject。它的 Button.onClick 要接到這支的 OnFlipPressed。")]
    [SerializeField] private GameObject flipButton;

    private void Awake()
    {
        if (inputController == null) inputController = FindFirstObjectByType<TouchInputController>();
        if (inputController == null)
            Debug.LogError("PieceActionButtons 找不到 TouchInputController：按鈕永遠不會出現。", this);

        if (rotateButton == null || rotateClockwiseButton == null || flipButton == null)
            Debug.LogError("PieceActionButtons 的按鈕欄位沒填滿（逆時針、順時針、翻面三顆）：" +
                           "沒填的那顆不會被顯示或隱藏。", this);
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
        rotateClockwiseArea = RegisterArea(rotateClockwiseButton);
        flipArea = RegisterArea(flipButton);
    }

    private void OnDisable()
    {
        if (inputController == null) return;
        inputController.UnregisterPressBlockingArea(rotateArea);
        inputController.UnregisterPressBlockingArea(rotateClockwiseArea);
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
    private RectTransform rotateClockwiseArea;
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

        // 兩顆旋轉鈕的顯示條件刻意是同一個算式——分岔的話會出現「只看得到一個方向」
        SetShown(rotateClockwiseButton, target != null);

        // 翻轉鈕（2026-09-14）：顯示條件跟旋轉鈕同一個算式——三顆一起出現，給人玩過之後「少一顆感覺怪怪的」。
        // 鏡射完跟現在一樣（正方形永遠、三角形 8 格裡 2 格）時變灰。
        // ⚠️ 變灰不是隱藏：物件還開著，所以照樣是擋片區、位置也不跳——那正是要的（坑表〈隱藏就不擋〉的反面）。
        // ⚠️ 能不能按跟 FlipSelected 裡的擋法是同一支函數，分岔的話會出現「亮著但按了沒反應」
        SetShown(flipButton, target != null);
        SetInteractable(flipButton, target != null
                                    && !PiecePrototype.IsMirrorSymmetric(target.Kind, target.Rotation, target.IsFlipped));
    }

    /// <param name="button">按鈕物件，可以是 null；上面沒有 <c>Button</c> 元件時不做事。</param>
    /// <param name="interactable">能不能按。不能按時 Unity 會套上按鈕的 Disabled Color。</param>
    private static void SetInteractable(GameObject button, bool interactable)
    {
        if (button == null) return;
        Button component = button.GetComponent<Button>();
        if (component == null || component.interactable == interactable) return;
        component.interactable = interactable;
    }

    /// <param name="button">要顯示或隱藏的按鈕物件，可以是 null。</param>
    /// <param name="shown">要不要顯示。</param>
    private static void SetShown(GameObject button, bool shown)
    {
        if (button == null) return;
        if (button.activeSelf == shown) return;      // 免得每幀都送一次狀態變更
        button.SetActive(shown);
    }

    /// <summary>逆時針旋轉鈕被按下。接在 Unity <c>Button.onClick</c> 上。</summary>
    public void OnRotatePressed()
    {
        if (inputController != null) inputController.RotateSelected();
    }

    /// <summary>順時針旋轉鈕被按下。接在 Unity <c>Button.onClick</c> 上。</summary>
    public void OnRotateClockwisePressed()
    {
        if (inputController != null) inputController.RotateSelectedClockwise();
    }

    /// <summary>翻面鈕被按下。接在 Unity <c>Button.onClick</c> 上。</summary>
    public void OnFlipPressed()
    {
        if (inputController != null) inputController.FlipSelected();
    }
}
