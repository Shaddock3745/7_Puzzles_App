using UnityEngine;

/// <summary>
/// ⭐ <b>掃描模式的選擇視窗</b>（2026-10-17）：照片從哪來——「新拍一張」「從檔案選擇」「取消」。
///
/// <b>什麼時候出現</b>：<see cref="LevelController.ScanSourceRequested"/> 發出時。也就是
/// 主畫面按「掃描模式」，以及掃描題結果彈窗按「再來一題」（每一題都問一次，不沿用上一次的來源）。
///
/// <b>跟 <see cref="MainMenuPanel"/> 同一個形狀</b>：不做事，只負責「什麼時候顯示」與「把按下轉給別人」。
/// <b>按任何一顆都先藏起自己</b>，再轉給 <see cref="LevelController"/>——之後的拍照畫面、相簿都要整個螢幕，
/// 而取消或失敗時主畫面會經由 <see cref="LevelController.ScanAborted"/> 自己顯示回來，這支不用管。
///
/// <b>場景要做的事</b>：
/// <list type="number">
/// <item>在 Canvas 底下建一個 Panel，底下放三顆 Button。<b>做成小對話框就好</b>——
///   它開著的時候後面已經有等待遮罩（<see cref="ScanWaitingCover"/>）蓋住盤面</item>
/// <item>⚠️ <b>Panel 在 Canvas 裡要排在結果彈窗、主畫面、等待遮罩<u>後面</u></b>（sibling index 比較大），
///   拍照畫面的前面——不然會被遮罩或結果彈窗蓋住</item>
/// <item>三顆的 <c>onClick</c> 分別接到 <see cref="OnTakePhotoPressed"/> / <see cref="OnPickFromFilePressed"/> /
///   <see cref="OnCancelPressed"/></item>
/// <item>⚠️ <b>這支腳本掛在另一個物件上，不能掛在 Panel 身上</b>（藏起來就再也開不回來）</item>
/// <item>把 Panel 的 <c>GameObject</c> 填進 <see cref="panel"/>。一開始開著或關著都可以，<see cref="Awake"/> 會把它關掉</item>
/// </list>
/// </summary>
[DisallowMultipleComponent]
public class ScanSourcePanel : MonoBehaviour
{
    [Tooltip("要開關的選擇視窗 Panel。⚠️ 不能是這支腳本自己所在的物件。")]
    [SerializeField] private GameObject panel;

    [Tooltip("按下之後要叫它開始掃描。留空的話 Awake 會自己找。")]
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
            Debug.LogError("ScanSourcePanel 的 panel 欄位沒填：選擇視窗不會出現，掃描模式按了只會閃一下回主畫面。", this);
        else if (panel == gameObject)
            Debug.LogError("ScanSourcePanel 掛在它自己要開關的 Panel 上了。藏起來之後這支就跟著被停用，" +
                           "再也開不回來。請把這支移到另一個物件上。", this);
        else
            panel.SetActive(false);

        if (levelController == null)
        {
            Debug.LogError("ScanSourcePanel 找不到 LevelController：選擇視窗不會出現。", this);
            return;
        }

        // ⚠️ panel 沒接好就不訂閱：訂閱了 LevelController 會以為有人接，結果什麼都沒出現、玩家停在空畫面上。
        // 不訂閱的話它會報錯並回主畫面
        if (panel != null && panel != gameObject) levelController.ScanSourceRequested += Show;
    }

    private void OnDestroy()
    {
        if (levelController != null) levelController.ScanSourceRequested -= Show;
    }

    /// <summary>整塊 Panel 登記成擋片區。停用的物件本來就不擋，所以不用跟著顯示開關（跟主畫面同一個做法）。</summary>
    private void OnEnable()
    {
        if (panel == null || inputController == null) return;

        blockingArea = panel.GetComponent<RectTransform>();
        if (blockingArea == null)
        {
            Debug.LogError($"{panel.name} 上沒有 RectTransform，不是 UI 物件？選擇視窗開著的時候盤面還是抓得到片。", this);
            return;
        }

        inputController.RegisterPressBlockingArea(blockingArea);
    }

    private void OnDisable()
    {
        if (inputController == null || blockingArea == null) return;
        inputController.UnregisterPressBlockingArea(blockingArea);
    }

    private void Show() => panel.SetActive(true);

    private void Hide()
    {
        if (panel != null) panel.SetActive(false);
    }

    /// <summary>「新拍一張」被按下。接在 Unity <c>Button.onClick</c> 上。</summary>
    public void OnTakePhotoPressed() => Choose(ScanPhotoSource.Camera);

    /// <summary>「從檔案選擇」被按下。接在 Unity <c>Button.onClick</c> 上。</summary>
    public void OnPickFromFilePressed() => Choose(ScanPhotoSource.Gallery);

    /// <summary>「取消」被按下。接在 Unity <c>Button.onClick</c> 上。回主畫面。</summary>
    public void OnCancelPressed()
    {
        Hide();
        if (levelController != null) levelController.CancelScanSourceChoice();
    }

    private void Choose(ScanPhotoSource source)
    {
        Hide();
        if (levelController != null) levelController.BeginScan(source);
    }
}
