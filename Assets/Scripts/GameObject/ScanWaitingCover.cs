using UnityEngine;

/// <summary>
/// ⭐ <b>掃描的等待遮罩</b>（2026-10-17）：掃描等待中就整個螢幕蓋住，不讓空的（或上一關的）盤面露出來。
///
/// <b>會露出來的四段</b>（沒有這張遮罩時）：選擇視窗開著時的背景、選擇視窗與拍照畫面之間（至少一幀，第一次還要等權限對話框）、
/// 按下快門到進關（辨識＋逼近，逼近時畫面還會凍住）、相簿選完到進關。
///
/// <b>它不持有真相</b>：每幀問 <see cref="LevelController.IsScanPending"/>，是就顯示、否就藏。
/// 什麼時候開始、什麼時候結束全由 <see cref="LevelController"/> 決定，這支只畫。
///
/// ⚠️ <b>一定要在 <c>LateUpdate</c> 問，不能在 <c>Update</c></b>：選擇視窗的按鈕在 EventSystem 的 <c>Update</c> 裡觸發，
/// 主畫面也在同一幀藏起來——遮罩的 <c>Update</c> 要是排在它前面，那一幀就是「沒有主畫面也沒有遮罩」，閃一下空盤面。
/// <c>LateUpdate</c> 排在所有 <c>Update</c> 與協程之後（假辨識的回呼、NativeGallery 的回呼都在那之前），
/// 開始與結束都能在同一幀跟上。
///
/// 💡 <b>逼近凍住時玩家看到的就是這張</b>：凍住之前畫出來的最後一幀，拍照畫面已經關了、遮罩還在。
/// 所以遮罩上放一行「處理中…」，玩家不會以為當掉。
///
/// <b>場景要做的事</b>：
/// <list type="number">
/// <item>在 Canvas 底下建一個<b>整個螢幕大小、不透明</b>的 Panel，上面放一個 TMP_Text 打「處理中…」（字由場景決定，程式不管）</item>
/// <item>⚠️ <b>Canvas 裡從底到頂的順序</b>：結果彈窗 → 主畫面 → <b>這張遮罩</b> → 選擇視窗 → 拍照畫面。
///   選擇視窗與拍照畫面要蓋在遮罩上面；遮罩要蓋住結果彈窗（「再來一題」時彈窗還開著）</item>
/// <item>⚠️ <b>這支腳本掛在另一個物件上，不能掛在 Panel 身上</b>（除錯記錄 004）</item>
/// <item>把 Panel 的 <c>GameObject</c> 填進 <see cref="panel"/></item>
/// </list>
/// </summary>
[DisallowMultipleComponent]
public class ScanWaitingCover : MonoBehaviour
{
    [Tooltip("要開關的遮罩 Panel。⚠️ 不能是這支腳本自己所在的物件。")]
    [SerializeField] private GameObject panel;

    [Tooltip("問它掃描是不是等待中。留空的話 Awake 會自己找。")]
    [SerializeField] private LevelController levelController;

    [Tooltip("擋片區的登記對象：遮罩蓋著時不要抓到底下的片。留空的話 Awake 會自己找。")]
    [SerializeField] private TouchInputController inputController;

    /// <summary>登記進擋片區的那塊矩形，離場時要取消登記。</summary>
    private RectTransform blockingArea;

    private void Awake()
    {
        if (levelController == null) levelController = FindFirstObjectByType<LevelController>();
        if (inputController == null) inputController = FindFirstObjectByType<TouchInputController>();

        if (panel == null)
            Debug.LogError("ScanWaitingCover 的 panel 欄位沒填：掃描等待時會露出盤面。", this);
        else if (panel == gameObject)
        {
            Debug.LogError("ScanWaitingCover 掛在它自己要開關的 Panel 上了。藏起來之後這支就跟著被停用，" +
                           "再也開不回來（除錯記錄 004）。請把這支移到另一個物件上。", this);
            panel = null;       // 不然 LateUpdate 第一次藏起它，就再也沒有 LateUpdate 了
        }

        if (levelController == null)
            Debug.LogError("ScanWaitingCover 找不到 LevelController：遮罩永遠不會出現。", this);
    }

    /// <summary>整塊 Panel 登記成擋片區。停用的物件本來就不擋，所以不用跟著顯示開關（跟主畫面同一個做法）。</summary>
    private void OnEnable()
    {
        if (panel == null || inputController == null) return;

        blockingArea = panel.GetComponent<RectTransform>();
        if (blockingArea == null)
        {
            Debug.LogError($"{panel.name} 上沒有 RectTransform，不是 UI 物件？遮罩蓋著的時候盤面還是抓得到片。", this);
            return;
        }

        inputController.RegisterPressBlockingArea(blockingArea);
    }

    private void OnDisable()
    {
        if (inputController == null || blockingArea == null) return;
        inputController.UnregisterPressBlockingArea(blockingArea);
    }

    private void LateUpdate()
    {
        if (panel == null) return;

        bool shown = levelController != null && levelController.IsScanPending;
        if (panel.activeSelf != shown) panel.SetActive(shown);
    }
}
