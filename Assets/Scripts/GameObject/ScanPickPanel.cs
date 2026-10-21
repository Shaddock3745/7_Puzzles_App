using System.Collections.Generic;
using Tangram.Data;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// ⭐ <b>掃描題的選題畫面</b>（2026-10-21，使用者定「不進關、先選題」）：辨識與逼近跑完之後、進關之前，
/// 顯示那張照片與<b>一個</b>候選題目的剪影，玩家用「上一個／下一個」翻頁、按「拼這一題」才進關；
/// 不滿意可以重拍（或重選）、或回主畫面。（使用者定：一個版面只貼一份結果，用按鈕翻頁和選擇。）
///
/// <b>候選從哪來</b>：<see cref="LevelController"/> 用幾個不同的逼近預設各跑一次（第五版、不找特徵、貼邊 ×32……，之前的廢案變成選項），
/// 合起來去重，見 <c>LevelController.BuildScanCandidates</c>。這支只負責畫與轉達玩家的選擇。
///
/// <b>跟選擇視窗同一個形狀</b>：訂閱 <see cref="LevelController.ScanPickRequested"/> 顯示自己，按「拼／重拍／回主畫面」都<b>先藏自己</b>再轉給 <c>LevelController</c>；
/// 腳本<b>不能</b>掛在 Panel 上（除錯記錄 004）；整塊登記成擋片區（雖然是不透明的全螢幕，還是不要讓底下的片被抓到）。
///
/// <b>誰保管什麼</b>：照片是 <c>LevelController</c> 的（進關時交給照片對照、不進關就釋放），這支只借畫；
/// 剪影貼圖是這支自己生的（<see cref="PuzzleSilhouette"/>，顯示時一次把所有候選的都生好），藏起來時釋放。
///
/// <b>Canvas 順序（底 → 頂）</b>：結果彈窗 → 主畫面 → 等待遮罩 → <b>選題畫面</b> → 選擇視窗 → 拍照畫面。
/// 選題期間 <c>IsScanPending</c> 一直是 true、遮罩一直蓋著盤面，這支在遮罩上面；按「重拍」之後選擇視窗在它上面出來。
///
/// <b>場景要做的事</b>：
/// <list type="number">
/// <item>Canvas 底下建一個<b>全螢幕不透明</b> Panel，排在等待遮罩之後、選擇視窗之前</item>
/// <item>裡面放：一個 RawImage 放照片（掛 <c>AspectRatioFitter</c>、Fit In Parent，這支會設比例）；一個 RawImage 放候選的剪影（正方形）；
///   一行 TMP 文字顯示「第幾個／共幾個」（選填）；五顆 Button：上一個／下一個／拼這一題／重拍或重選／回主畫面</item>
/// <item>另一個物件掛這支，填 panel、兩個 RawImage、頁數文字、上一個與下一個兩顆 Button（只有 1 個候選時會變灰）；
///   五顆 Button 的 <c>onClick</c> 分別接 <see cref="OnPreviousPressed"/>／<see cref="OnNextPressed"/>／<see cref="OnPlayPressed"/>／
///   <see cref="OnRetryPressed"/>／<see cref="OnCancelPressed"/></item>
/// </list>
/// </summary>
[DisallowMultipleComponent]
public class ScanPickPanel : MonoBehaviour
{
    [Tooltip("要開關的選題畫面 Panel（全螢幕不透明）。⚠️ 不能是這支腳本自己所在的物件。")]
    [SerializeField] private GameObject panel;

    [Tooltip("放照片的 RawImage。掛 AspectRatioFitter（Fit In Parent）的話這支會把比例設成照片的。")]
    [SerializeField] private RawImage photoImage;

    [Tooltip("放目前這一個候選剪影的 RawImage（正方形）。")]
    [SerializeField] private RawImage candidateImage;

    [Tooltip("顯示「第幾個／共幾個」的文字。選填。")]
    [SerializeField] private TMP_Text pageLabel;

    [Tooltip("「上一個」按鈕。選填：填了的話只有 1 個候選時會變灰。")]
    [SerializeField] private Button previousButton;

    [Tooltip("「下一個」按鈕。選填：填了的話只有 1 個候選時會變灰。")]
    [SerializeField] private Button nextButton;

    [Tooltip("剪影貼圖的邊長（像素）。")]
    [Min(8)]
    [SerializeField] private int silhouetteSize = 512;

    [Tooltip("選了之後要叫它進關。留空的話 Awake 會自己找。")]
    [SerializeField] private LevelController levelController;

    [Tooltip("擋片區的登記對象。留空的話 Awake 會自己找。")]
    [SerializeField] private TouchInputController inputController;

    private RectTransform blockingArea;

    /// <summary>目前翻到第幾個候選。</summary>
    private int currentIndex;

    /// <summary>這一輪生的剪影貼圖，跟候選同順序；藏起來時釋放。</summary>
    private readonly List<Texture2D> silhouettes = new List<Texture2D>();

    private void Awake()
    {
        if (levelController == null) levelController = FindFirstObjectByType<LevelController>();
        if (inputController == null) inputController = FindFirstObjectByType<TouchInputController>();

        if (panel == null)
            Debug.LogError("ScanPickPanel 的 panel 欄位沒填：選題畫面不會出現，掃描完會直接拿第 1 個候選進關。", this);
        else if (panel == gameObject)
            Debug.LogError("ScanPickPanel 掛在它自己要開關的 Panel 上了。藏起來之後這支就跟著被停用，" +
                           "再也開不回來（除錯記錄 004）。請把這支移到另一個物件上。", this);
        else
            panel.SetActive(false);

        if (candidateImage == null)
            Debug.LogError("ScanPickPanel 的候選剪影 RawImage 沒填：玩家看不到候選長什麼樣。", this);

        if (levelController == null)
        {
            Debug.LogError("ScanPickPanel 找不到 LevelController：選題畫面不會出現。", this);
            return;
        }

        // ⚠️ panel 沒接好就不訂閱：LevelController 看到沒人接會直接拿第 1 個候選進關，不會讓玩家停在空畫面上
        if (panel != null && panel != gameObject) levelController.ScanPickRequested += Show;
    }

    private void OnDestroy()
    {
        if (levelController != null) levelController.ScanPickRequested -= Show;
        ReleaseSilhouettes();
    }

    private void OnEnable()
    {
        if (panel == null || inputController == null) return;
        blockingArea = panel.GetComponent<RectTransform>();
        if (blockingArea == null)
        {
            Debug.LogError($"{panel.name} 上沒有 RectTransform，不是 UI 物件？選題畫面開著的時候盤面還是抓得到片。", this);
            return;
        }
        inputController.RegisterPressBlockingArea(blockingArea);
    }

    private void OnDisable()
    {
        if (inputController == null || blockingArea == null) return;
        inputController.UnregisterPressBlockingArea(blockingArea);
    }

    /// <summary>LevelController 算好候選了：擺照片、把每個候選的剪影生好、翻到第 1 個、顯示。</summary>
    private void Show()
    {
        ScanCandidateSet pending = levelController.PendingScan;
        if (pending == null)
        {
            Debug.LogError("ScanPickPanel 被叫出來，但 LevelController 沒有待選的掃描結果。", this);
            return;
        }

        if (photoImage != null)
        {
            photoImage.texture = pending.Photo;
            AspectRatioFitter fitter = photoImage.GetComponent<AspectRatioFitter>();
            if (fitter != null && pending.Photo != null && pending.Photo.height > 0)
                fitter.aspectRatio = (float)pending.Photo.width / pending.Photo.height;
        }

        ReleaseSilhouettes();
        foreach (ContourPuzzle candidate in pending.Candidates)
            silhouettes.Add(PuzzleSilhouette.Draw(candidate.Puzzle, silhouetteSize));

        bool canPage = pending.Candidates.Count > 1;
        if (previousButton != null) previousButton.interactable = canPage;
        if (nextButton != null) nextButton.interactable = canPage;

        ShowPage(0);
        panel.SetActive(true);
    }

    /// <summary>翻到第幾個：換剪影、更新頁數文字。</summary>
    private void ShowPage(int index)
    {
        currentIndex = index;
        if (candidateImage != null) candidateImage.texture = index < silhouettes.Count ? silhouettes[index] : null;
        if (pageLabel != null) pageLabel.text = $"{index + 1} / {silhouettes.Count}";
    }

    private void Hide()
    {
        if (panel != null) panel.SetActive(false);
        if (photoImage != null) photoImage.texture = null;      // 照片不是這支的，不釋放
        if (candidateImage != null) candidateImage.texture = null;
        ReleaseSilhouettes();
    }

    private void ReleaseSilhouettes()
    {
        foreach (Texture2D texture in silhouettes)
            if (texture != null) Destroy(texture);
        silhouettes.Clear();
    }

    /// <summary>「上一個」：翻到前一個候選，第 1 個再往前是最後一個。</summary>
    public void OnPreviousPressed()
    {
        if (silhouettes.Count == 0) return;
        ShowPage((currentIndex - 1 + silhouettes.Count) % silhouettes.Count);
    }

    /// <summary>「下一個」：翻到下一個候選，最後一個再往後是第 1 個。</summary>
    public void OnNextPressed()
    {
        if (silhouettes.Count == 0) return;
        ShowPage((currentIndex + 1) % silhouettes.Count);
    }

    /// <summary>「拼這一題」：用目前這一個候選進關。</summary>
    public void OnPlayPressed()
    {
        int index = currentIndex;
        Hide();
        if (levelController != null) levelController.PickScanCandidate(index);
    }

    /// <summary>「重拍／重選」：丟掉這一輪，回到選擇視窗。</summary>
    public void OnRetryPressed()
    {
        Hide();
        if (levelController != null) levelController.RetryScan();
    }

    /// <summary>「回主畫面」：丟掉這一輪，回主畫面。</summary>
    public void OnCancelPressed()
    {
        Hide();
        if (levelController != null) levelController.CancelScanPick();
    }
}
