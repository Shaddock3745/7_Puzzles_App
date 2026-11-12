using System;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// ⭐ <b>新手教學</b>（2026-10-30，第二輪回饋 A 的「新手教學？」）：主畫面上一顆「教學」鈕，點進去是幾頁「一張圖＋一句話」，
/// 上一頁／下一頁翻、最底下一顆「退出」回主畫面。
///
/// <b>範圍</b>：
/// <list type="bullet">
/// <item><b>只從主畫面進</b>，不在第一次開 App 時自動跳</item>
/// <item><b>不記有沒有看過</b>（不寫 <c>PlayerPrefs</c>、不動進度存檔）</item>
/// <item><b>完全不碰遊戲裡的東西</b>：不認識 <see cref="LevelController"/>、<see cref="TouchInputController"/>，
///   也不登記擋片區——它只會從主畫面打開，那時主畫面（不透明、已經登記成擋片區）就蓋在盤面上，片本來就抓不到</item>
/// </list>
///
/// <b>內容在 Inspector</b>（<see cref="pages"/>）：每頁一句話、一張圖（圖可以不填，不填就藏起來）。
/// 字與圖是美術的事，之後直接在 Inspector 改，不用動程式。預設的五頁只是講哪幾件事的草稿。
///
/// <b>場景要做的事</b>：
/// <list type="number">
/// <item>Canvas 底下建一個<b>全螢幕不透明</b> Panel，排在主畫面<b>之後</b>（sibling index 比較大，才蓋得住主畫面）</item>
/// <item>裡面放：一個 Image 放圖（建議勾 Preserve Aspect）、一行 TMP 文字放說明、一行 TMP 文字放頁數（選填）、
///   三顆 Button：上一頁／下一頁／最底下的退出</item>
/// <item>另一個物件掛這支（⚠️ <b>不能</b>掛在 Panel 上，藏起來就開不回來），填好欄位；
///   三顆 Button 的 <c>onClick</c> 分別接 <see cref="OnPreviousPressed"/>／<see cref="OnNextPressed"/>／<see cref="OnExitPressed"/></item>
/// <item>主畫面加一顆「教學」Button，<c>onClick</c> 接這支的 <see cref="Show"/></item>
/// </list>
/// </summary>
[DisallowMultipleComponent]
public class TutorialPanel : MonoBehaviour
{
    /// <summary>教學的一頁。</summary>
    [Serializable]
    public class Page
    {
        [Tooltip("這一頁的圖（截圖之類）。可以不填，不填的話圖那格藏起來。")]
        public Sprite image;

        [Tooltip("這一頁的說明。")]
        [TextArea(2, 5)]
        public string text;
    }

    [Tooltip("要開關的教學 Panel（全螢幕不透明，排在主畫面之後）。⚠️ 不能是這支腳本自己所在的物件。")]
    [SerializeField] private GameObject panel;

    [Tooltip("放這一頁圖的 Image。這一頁沒有圖時會藏起來。")]
    [SerializeField] private Image pageImage;

    [Tooltip("放這一頁說明的文字。")]
    [SerializeField] private TMP_Text pageText;

    [Tooltip("顯示「第幾頁／共幾頁」的文字。選填。")]
    [SerializeField] private TMP_Text pageLabel;

    [Tooltip("「上一頁」按鈕。選填：填了的話在第一頁時變灰。")]
    [SerializeField] private Button previousButton;

    [Tooltip("「下一頁」按鈕。選填：填了的話在最後一頁時變灰（離開走最底下的退出鈕）。")]
    [SerializeField] private Button nextButton;

    [Tooltip("教學的每一頁，照順序。字與圖之後再調；預設的五頁是「要講哪幾件事」的草稿。")]
    [SerializeField] private Page[] pages =
    {
        new Page { text = "把下面抽屜裡的片拖進盒子裡，七片都放進去就過關！" },
        new Page { text = "點一下片，旁邊會出現旋轉和翻面的按鈕。" },
        new Page { text = "片要整片放在盒子裡、不能疊在別的片上面，不然會放不上去。" },
        new Page { text = "卡住了嗎？等一下會出現提示鈕，告訴你一片該放哪裡。真的拼不出來，也可以放棄換一題。" },
        new Page { text = "掃描模式：拍一樣東西，它就會變成一題七巧板！" },
    };

    /// <summary>現在翻到第幾頁。</summary>
    private int currentIndex;

    private void Awake()
    {
        if (panel == null)
            Debug.LogError("TutorialPanel 的 panel 欄位沒填：按教學鈕不會有任何反應。", this);
        else if (panel == gameObject)
            Debug.LogError("TutorialPanel 掛在它自己要開關的 Panel 上了。藏起來之後這支就跟著被停用，" +
                           "再也開不回來。請把這支移到另一個物件上。", this);
        else
            panel.SetActive(false);

        if (pageText == null)
            Debug.LogError("TutorialPanel 的說明文字欄位沒填：教學只會有圖、沒有字。", this);
        if (pages == null || pages.Length == 0)
            Debug.LogError("TutorialPanel 一頁都沒有：按教學鈕會打開一個空的畫面。", this);
    }

    /// <summary>打開教學，從第一頁開始。接在主畫面「教學」鈕的 <c>onClick</c> 上。</summary>
    public void Show()
    {
        if (panel == null || panel == gameObject) return;
        ShowPage(0);
        panel.SetActive(true);
    }

    /// <summary>「上一頁」。第一頁時什麼都不做（按鈕本來也是灰的）。</summary>
    public void OnPreviousPressed()
    {
        if (currentIndex > 0) ShowPage(currentIndex - 1);
    }

    /// <summary>「下一頁」。最後一頁時什麼都不做——<b>不繞回第一頁</b>：教學是有頭有尾的，繞回去會讓人以為還沒講完。</summary>
    public void OnNextPressed()
    {
        if (pages != null && currentIndex < pages.Length - 1) ShowPage(currentIndex + 1);
    }

    /// <summary>「退出」：收起教學，底下的主畫面還在。不記有沒有看過。</summary>
    public void OnExitPressed()
    {
        if (panel != null && panel != gameObject) panel.SetActive(false);
    }

    /// <summary>翻到第幾頁：換圖、換字、更新頁數與兩顆翻頁鈕。</summary>
    /// <param name="index">頁的索引，超出範圍時夾回來。</param>
    private void ShowPage(int index)
    {
        int count = pages == null ? 0 : pages.Length;
        currentIndex = count == 0 ? 0 : Mathf.Clamp(index, 0, count - 1);
        Page page = count == 0 ? null : pages[currentIndex];

        if (pageImage != null)
        {
            // 沒圖的頁整格藏起來，不留一塊白方塊
            bool hasImage = page != null && page.image != null;
            pageImage.sprite = hasImage ? page.image : null;
            pageImage.gameObject.SetActive(hasImage);
        }
        if (pageText != null) pageText.text = page != null ? page.text : string.Empty;
        if (pageLabel != null) pageLabel.text = count == 0 ? string.Empty : $"{currentIndex + 1} / {count}";

        if (previousButton != null) previousButton.interactable = currentIndex > 0;
        if (nextButton != null) nextButton.interactable = currentIndex < count - 1;
    }
}
