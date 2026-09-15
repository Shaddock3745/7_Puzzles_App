using Tangram.Core;
using Tangram.Data;
using UnityEngine;

/// <summary>
/// 提示的覆蓋片（2026-09-15，待辦清單〈提示〉、互動設計 §9）：在參考解的位置蓋一片<b>半透明、一直閃</b>的片。
///
/// <b>它只畫，不記任何東西。</b>提示過哪幾片的真相在 <c>LevelData.HintedReferenceSlots</c>，
/// 這裡每幀照它畫——跟 undo 鈕、放棄鈕同一個做法，<b>不訂閱任何東西</b>：
/// 換關時清單整包換掉、覆蓋片自己就消失，不必有人記得通知它。
///
/// <b>每一個提示畫不畫</b>（2026-09-15 使用者定「乙」）：
/// <list type="bullet">
/// <item>那一片<b>已經擺對</b>（<c>LevelData.IsReferenceSlotSatisfied</c>：同片種、頂點集合一樣，不綁槽）→ 不畫</item>
/// <item>被拿走 → 又出現。「畫不畫」是盤面的函數，所以這件事免費成立</item>
/// <item>這一局結束了 → 全部不畫（彈窗蓋上來了）</item>
/// </list>
///
/// ⚠️ <b>三件做錯不會報錯的事</b>：
/// <list type="number">
/// <item><b>sorting layer 一定要是 <c>Piece</c>、order 用 <c>PieceSorting.HintSortingOrder</c></b>：
///   夾在「未吸附」與「拖曳中」之間，所以拖著的片會蓋在提示上面。
///   沒設層會留在 <c>Default</c>，被盒子和片蓋住——症狀是「按了提示什麼都沒出現」（除錯記錄 005）</item>
/// <item><b>不能有 collider、不能登記成擋片區</b>：覆蓋片常常蓋在玩家的片上面，擋住的話那片就點不到了</item>
/// <item><b>這個物件不能有縮放</b>：覆蓋片的位置與角度是直接寫世界座標，但大小會吃父物件的縮放，
///   症狀是「提示比片大一圈」。<c>Awake</c> 會檢查並拉回來</item>
/// </list>
///
/// <b>場景要做的事</b>：隨便一個物件掛上這支（不用任何子物件，三片覆蓋片是程式生的），
/// <c>TrayView</c> 留空會自己找。
/// </summary>
[DisallowMultipleComponent]
public class HintOverlayView : MonoBehaviour
{
    [Tooltip("圖的來源（跟七片共用同一份圖，內縮與圓角才會一樣）。留空的話 Awake 會自己找。")]
    [SerializeField] private TrayView trayView;

    [Tooltip("覆蓋片的顏色，alpha 會被下面的閃爍蓋掉。"
             + "預設白色：不帶片的顏色，免得暗示「只有這個顏色的那一片才對」（兩片大三角可以互換）。")]
    [SerializeField] private Color tint = Color.white;

    [Tooltip("閃一次（暗 → 亮 → 暗）幾秒。")]
    [Min(0.1f)]
    [SerializeField] private float pulsePeriodSeconds = 1.2f;

    [Tooltip("最暗的時候的不透明度。")]
    [Range(0f, 1f)]
    [SerializeField] private float minimumAlpha = 0.15f;

    [Tooltip("最亮的時候的不透明度。")]
    [Range(0f, 1f)]
    [SerializeField] private float maximumAlpha = 0.7f;

    /// <summary>三片覆蓋片，第 i 個畫第 i 個提示。一次生好、從頭到尾重複用，不在遊玩中生或刪物件。</summary>
    private readonly SpriteRenderer[] overlays = new SpriteRenderer[LevelData.MaximumHints];

    private void Awake()
    {
        if (trayView == null) trayView = FindFirstObjectByType<TrayView>();
        if (trayView == null)
            Debug.LogError("HintOverlayView 找不到 TrayView：拿不到片的圖，提示永遠不會出現。", this);

        EnforceNoScale();

        for (int i = 0; i < overlays.Length; i++)
        {
            var overlayObject = new GameObject($"Hint{i}");
            overlayObject.transform.SetParent(transform, worldPositionStays: false);

            SpriteRenderer overlay = overlayObject.AddComponent<SpriteRenderer>();
            // 跟七片同一層，夾在「未吸附」與「拖曳中」之間（理由見 PieceSorting.HintSortingOrder）。
            // + i：同時蓋在同一個位置是不可能的（每槽只提示一次），順序只是讓它有定義
            overlay.sortingLayerName = PieceSorting.PieceSortingLayer;
            overlay.sortingOrder = PieceSorting.HintSortingOrder + i;
            overlay.enabled = false;
            overlays[i] = overlay;
        }
    }

    /// <summary>
    /// 每幀照 <c>LevelData</c> 重畫。
    ///
    /// 💡 成本：每個提示問一次「擺對了沒」＝最多 7 次頂點集合比對，一幀最多 21 次、每次配置兩個小陣列。
    /// 夠便宜，所以不去訂閱 <c>BoardState.Changed</c> 做快取——多一個訂閱者就多一條「換關要重新訂閱」的規矩。
    /// </summary>
    private void Update()
    {
        LevelData level = LevelSession.Current?.Data;
        PieceSpriteLibrary library = trayView != null ? trayView.SpriteLibrary : null;

        bool canDraw = level != null && level.ReferenceSolution != null && !level.IsFinished && library != null;
        int hintCount = canDraw ? level.HintedReferenceSlots.Count : 0;

        // 正弦波：0 → 1 → 0。用 unscaledTime，將來做暫停（timeScale = 0）時提示照樣閃
        float phase = 0.5f - 0.5f * Mathf.Cos(Time.unscaledTime * 2f * Mathf.PI / pulsePeriodSeconds);
        Color color = tint;
        color.a = Mathf.Lerp(minimumAlpha, maximumAlpha, phase);

        for (int i = 0; i < overlays.Length; i++)
        {
            SpriteRenderer overlay = overlays[i];
            int referenceSlot = i < hintCount ? level.HintedReferenceSlots[i] : -1;

            bool shown = referenceSlot >= 0 && !level.IsReferenceSlotSatisfied(referenceSlot);
            if (overlay.enabled != shown) overlay.enabled = shown;
            if (!shown) continue;

            Placement placement = level.ReferenceSolution[referenceSlot];

            // 跟 PieceView 同一套換算：圖的軸心是原型原點，所以位置＝平移量；
            // 模型的翻面是對 X 軸鏡射＝flipY（不能用負縮放，見 CLAUDE.md〈背面剔除吃掉翻面的平行四邊形〉）
            overlay.sprite = library.SpriteFor(placement.Kind);
            overlay.flipY = placement.IsFlipped;
            overlay.transform.SetPositionAndRotation(
                placement.Translation.ToVector2(),
                Quaternion.Euler(0f, 0f, placement.Rotation * 45f));
            overlay.color = color;
        }
    }

    /// <summary>
    /// 覆蓋片的大小會吃這個物件的縮放（位置與角度是直接寫世界座標，不受影響）。
    /// 照 <c>TrayView.EnforcePieceRootIdentity</c> 的做法：自己拉回來並警告，不只寫在註解裡。
    /// </summary>
    private void EnforceNoScale()
    {
        if (transform.lossyScale == Vector3.one) return;

        Debug.LogWarning("HintOverlayView 所在的物件（或它的父物件）有縮放，提示會跟片不一樣大。" +
                         "已經把自己的 localScale 拉回 1，父物件的縮放要自己拿掉。", this);
        transform.localScale = Vector3.one;
    }
}
