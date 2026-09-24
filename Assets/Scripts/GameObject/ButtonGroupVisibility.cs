using UnityEngine;

/// <summary>
/// ⭐ <b>抽屜打開時，整排按鈕退場</b>（2026-09-24）。
///
/// <b>為什麼需要它</b>：擋片區是「按在這塊矩形上就不抓片」，而抽屜的縮圖就排在按鈕底下。
/// 按鈕沒退場的話，被它蓋住的那塊縮圖<b>按下去沒有反應</b>——而且不報錯。
/// 實際踩到的是作弊鈕壓住大三角縮圖的下半（除錯記錄 013）。
///
/// <b>為什麼不是每顆按鈕自己判斷</b>：<see cref="BoardActionButtons"/> 那幾顆本來就有
/// <c>!IsTrayDrawerOpen</c> 的條件，但那是「每顆各自記得」——<see cref="DebugTools"/> 的作弊鈕
/// 就是漏掉的那一顆，而且漏了不會有任何徵兆。改成<b>整排一起藏</b>之後，
/// 新增按鈕只要放進既有的排裡就自動受保護，不必記得加條件。
///
/// ⚠️ <b>藏的是「排」，不是「按鈕」。</b> 按鈕自己的顯示還是歸原本那幾支管
/// （<see cref="BoardActionButtons"/> / <see cref="PieceActionButtons"/>）——
/// 這支去寫按鈕的話，同一個欄位就有兩個寫入者，那是 <c>PiecePreview</c> 那個教訓的形狀
/// （而且作弊鈕沒有人會把它開回來，關了就再也不出現）。
/// <b>層級不同、擁有者不同，兩邊不會打架。</b>
///
/// ⚠️ <b>版面不受影響是刻意的</b>：<see cref="BoardView"/> 躲按鈕排時<b>連停用的也躲</b>
/// （2026-09-24 一起改的）。不然盒子區會在抽屜開闔時變大變小，症狀是
/// 「每開關一次抽屜，盒子跟盤上的片就跳一下」。
///
/// <b>場景要做的事</b>：
/// <list type="number">
/// <item>把這支掛在一個<b>獨立物件</b>上（例如 <c>ButtonController</c>）</item>
/// <item>⚠️ <b>不能掛在任何一排按鈕底下</b>——它會把自己一起關掉，然後再也開不回來（除錯記錄 004）</item>
/// <item>把要一起藏的那幾排（<c>PieceButtonGroup</c> / <c>BoardButtonGroup</c> / <c>ProcessButtonGroup</c>）
/// 填進 <see cref="buttonGroups"/>，跟 <c>BoardView</c> 的「要躲開的按鈕排」填同一份</item>
/// </list>
/// </summary>
[DisallowMultipleComponent]
public class ButtonGroupVisibility : MonoBehaviour
{
    [Tooltip("抽屜打開時要一起隱藏的按鈕排（父物件）。⚠️ 跟 BoardView 的「盒子與暫放的片要躲開的按鈕排」填同一份。")]
    [SerializeField] private GameObject[] buttonGroups = System.Array.Empty<GameObject>();

    [Tooltip("去哪問抽屜開沒開。留空的話 Awake 會自己找。")]
    [SerializeField] private TouchInputController inputController;

    private void Awake()
    {
        if (inputController == null) inputController = FindFirstObjectByType<TouchInputController>();

        if (inputController == null)
        {
            Debug.LogError("ButtonGroupVisibility 找不到 TouchInputController：" +
                           "抽屜打開時按鈕排不會退場，被蓋住的縮圖會點不到。", this);
            return;
        }

        // ⚠️ 掛在自己要關的東西上，第一次隱藏之後就沒有下一次 Update 了（除錯記錄 004）
        foreach (GameObject group in buttonGroups)
            if (group == gameObject)
                Debug.LogError("ButtonGroupVisibility 掛在它自己要開關的按鈕排上了。" +
                               "抽屜一開它就跟著被停用，之後再也不會把按鈕排開回來。" +
                               "請把這支移到獨立的物件上。", this);
    }

    /// <summary>
    /// 每幀重算「這一排該不該在」。<b>跟 <see cref="BoardActionButtons"/> 同一個模式</b>：
    /// 只是抽屜狀態的函數，不訂閱任何東西，所以沒有人需要記得通知它。
    /// </summary>
    private void Update()
    {
        if (inputController == null) return;

        bool shown = !inputController.IsTrayDrawerOpen;
        foreach (GameObject group in buttonGroups)
        {
            if (group == null || group == gameObject) continue;
            if (group.activeSelf != shown) group.SetActive(shown);   // 免得每幀都送一次狀態變更
        }
    }
}
