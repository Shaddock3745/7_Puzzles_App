using UnityEngine;
using UnityEngine.InputSystem;

/// <summary>
/// 開發期的**捷徑**：一顆「填 6 片」按鈕，外加一個鍵盤按鍵。
/// 跟 <see cref="DebugOverlay"/> 是一對——那支只讀、這支會改東西。
///
/// <b>為什麼不做進 <c>DebugOverlay</c></b>：那支的契約寫死了
/// 「它絕對不碰任何狀態，只讀。除錯工具一旦會改東西，它自己就變成 bug 來源」。
/// 自動填入會寫盤面。<b>兩支的分界就是「會不會改東西」。</b>
///
/// <b>為什麼要有螢幕按鈕</b>：手機沒有鍵盤，而<b>手感只有實機測得準</b>。
/// 鍵盤那條留著是因為在 Editor 裡比較快，兩者共用同一個進入點所以行為不會分家。
///
/// <b>場景要做的事</b>（跟 <see cref="BoardActionButtons"/> 同一套）：
/// <list type="number">
/// <item>建一顆 UI Button，<c>onClick</c> 接到 <see cref="OnFillPressed"/></item>
/// <item>⚠️ <b>這支腳本掛在另一個物件上，不要掛在那顆按鈕身上</b>（理由見下）</item>
/// <item>把按鈕的 <c>GameObject</c> 填進 <see cref="fillButton"/></item>
/// </list>
///
/// ⚠️ <b>它自己會在正式版關掉</b>（同 <c>DebugOverlay</c> 的編譯符號），
/// 但**按鈕物件是場景裡的，關不掉它自己**——所以 <c>Awake</c> 會順手把按鈕也隱藏。
/// P4 檢查正式版時記得看一眼右上角沒有這顆。
///
/// ⚠️ <b>為什麼腳本不能掛在它要開關的那顆按鈕上</b>：隱藏是 <c>SetActive(false)</c>，
/// 而<b>被停用的物件不會再跑 <c>Update</c></b>——掛在自己身上就再也開不回來，
/// 而且不報錯。<see cref="BoardActionButtons"/> 2026-09-09 真的踩到過。
/// </summary>
[DisallowMultipleComponent]
public class DebugTools : MonoBehaviour
{
    [Tooltip("關掉就不作用。正式版本來就會自動關（見 Awake）。")]
    [SerializeField] private bool enableShortcuts = true;

    [Tooltip("拖曳控制。留空的話 Awake 會自己找。")]
    [SerializeField] private TouchInputController inputController;

    [Tooltip("「填 6 片」鈕的 GameObject。它的 Button.onClick 要接到這支的 OnFillPressed。")]
    [SerializeField] private GameObject fillButton;

    [Tooltip("鍵盤捷徑，跟按鈕做同一件事。手機上沒有鍵盤，這條會自動失效。")]
    [SerializeField] private Key fillKey = Key.F;

    private RectTransform fillArea;

    private void Awake()
    {
        if (inputController == null) inputController = FindFirstObjectByType<TouchInputController>();
        if (inputController == null)
            Debug.LogError("DebugTools 找不到 TouchInputController：捷徑不會有作用。", this);

        if (fillButton == null)
            Debug.LogWarning("DebugTools 的 fillButton 沒填，螢幕上不會有按鈕（鍵盤捷徑仍然有效）。", this);

        // ⚠️ 跟 BoardActionButtons 同一個會「永遠不出現而且不報錯」的接法，所以在這裡吵起來
        if (fillButton == gameObject)
            Debug.LogError(
                "DebugTools 掛在它自己要開關的按鈕上了。停用的物件不再跑 Update，" +
                "按鈕從此永遠不會出現。請把這支腳本移到另一個物件上。", this);

#if !UNITY_EDITOR && !DEVELOPMENT_BUILD
        // 正式版關掉自己，跟 DebugOverlay 同一條規則。
        // ⚠️ 按鈕物件在場景裡，不會跟著這支一起消失，所以要明確藏掉
        if (fillButton != null) fillButton.SetActive(false);
        enabled = false;
#endif
    }

    /// <summary>
    /// 把按鈕的矩形登記成「按在這裡不要抓片」。
    ///
    /// ⚠️⚠️ <b>這件事非做不可，不是防呆。</b>沒登記的話按下按鈕會<b>同時把手指底下的片抓起來</b>。
    /// <c>pressBlockingAreas</c> 只認得填進去的矩形，之後加更多 UI 時要記得一起填
    /// ——<b>這是那條規則定下來之後的第一顆新按鈕</b>。
    /// </summary>
    private void OnEnable()
    {
        if (fillButton == null || inputController == null) return;

        fillArea = fillButton.GetComponent<RectTransform>();
        if (fillArea == null)
        {
            Debug.LogError($"{fillButton.name} 上沒有 RectTransform，不是 UI 物件？" +
                           "按這顆會連手指底下的片一起抓起來。", this);
            return;
        }
        inputController.RegisterPressBlockingArea(fillArea);
    }

    private void OnDisable()
    {
        if (inputController == null || fillArea == null) return;
        inputController.UnregisterPressBlockingArea(fillArea);
    }

    /// <summary>
    /// 按鈕的 <c>onClick</c> 接這裡。
    /// <b>這支不做事</b>，只把按下轉給控制器——規則住在
    /// <see cref="TouchInputController.FillFromReferenceSolutionForDebug"/>，
    /// 按鈕與鍵盤只是兩個觸發器（跟 <see cref="BoardActionButtons"/> 同一個模式）。
    /// </summary>
    public void OnFillPressed()
    {
        if (!enableShortcuts || inputController == null) return;
        inputController.FillFromReferenceSolutionForDebug();
    }

    private void Update()
    {
        if (!enableShortcuts || inputController == null) return;

        // ⚠️ 專案只開了新 Input System，舊的 Input.GetKeyDown 在 runtime 會直接丟例外。
        // 手機沒有鍵盤，Keyboard.current 會是 null
        Keyboard keyboard = Keyboard.current;
        if (keyboard == null) return;

        if (keyboard[fillKey].wasPressedThisFrame) OnFillPressed();
    }
}
