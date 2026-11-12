using Tangram.Core;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// ⭐ <b>掃描題的照片對照</b>（2026-10-14）：把拍到的照片半透明疊在盤面上，讓玩家對照「我拍的」跟「拼出來的」。
///
/// <b>什麼時候看得到</b>：只有掃描題有照片按鈕，<b>按一下顯示、再按一下隱藏</b>（開關）；
/// <b>過關後自動顯示</b>。照片正立、不畫輪廓線，蓋在片上面、在抽屜與按鈕下面（<see cref="PieceSorting.ScanPhotoSortingLayer"/>）。
///
/// <b>它記住「這張照片屬於哪一關」</b>（<c>LevelData</c> 的參照），每幀拿目前這一關跟它比：
/// 換關之後 <c>LevelData</c> 是新的一包、對不上，<b>照片與按鈕下一幀就自己消失，開關也自然回到「關」</b>——
/// 不需要有人記得清。跟監控面板快取同一個做法（比參照不比內容）。
///
/// <b>照片它自己保管</b>：新的進來時把舊的釋放掉，自己被銷毀時也釋放。執行時建的貼圖不釋放會一直佔記憶體。
///
/// <b>照片畫在哪</b>：由 <c>LevelController</c> 算好四個角（盤面座標）交進來，這支只負責畫。
/// 換算鏈是 像素 →（<c>RecognizedContour.PixelToModel</c>）輪廓座標 →（<c>ContourPuzzle.OutlineOffset</c>）題目座標
/// →（盒子原點）盤面座標。盤面座標就是世界座標（跟 <see cref="BoxView"/> 一樣，mesh 直接建在世界座標）。
///
/// <b>場景要做的事</b>：
/// <list type="number">
/// <item>Tag Manager → Sorting Layers：在 <c>Piece</c> 與 <c>GameUI</c> 中間加一層 <c>ScanPhoto</c></item>
/// <item>建一個空物件掛這支（會自動加 MeshFilter／MeshRenderer），<b>放在原點、不轉不縮放</b>（會檢查）</item>
/// <item>在一個按鈕排裡加一顆「照片」Button，<c>onClick</c> 接到 <see cref="OnPhotoTogglePressed"/>，
///   再把那顆按鈕拖進 <see cref="photoToggleButton"/>。⚠️ 這支腳本<b>不能</b>掛在那顆按鈕上（停用的物件不跑 Update）</item>
/// </list>
/// </summary>
[DisallowMultipleComponent]
[RequireComponent(typeof(MeshFilter))]
[RequireComponent(typeof(MeshRenderer))]
public class ScanPhotoView : MonoBehaviour
{
    [Tooltip("照片開關按鈕。只有掃描題、而且還沒過關時才顯示。⚠️ 不能是這支腳本自己所在的物件。")]
    [SerializeField] private GameObject photoToggleButton;

    [Tooltip("擋片區的登記對象：按照片鈕時不要抓到底下的片。留空的話 Awake 會自己找。")]
    [SerializeField] private TouchInputController inputController;

    [Tooltip("照片的不透明度。太高會看不到底下的片，太低看不出照片。")]
    [Range(0f, 1f)]
    [SerializeField] private float opacity = 0.6f;

    [Tooltip("照片關著時按鈕底圖的顏色（會跟 Button 的 Color Tint 相乘）。")]
    [SerializeField] private Color offColor = new Color(0.55f, 0.55f, 0.55f, 1f);

    [Tooltip("照片開著時按鈕底圖的顏色。")]
    [SerializeField] private Color onColor = Color.white;

    /// <summary>照片鈕上要染色的那張圖。第一次要用時才找。</summary>
    private Graphic toggleGraphic;

    private MeshRenderer meshRenderer;
    private Mesh mesh;
    private Material material;

    /// <summary>照片屬於哪一關。跟目前這一關不是同一個就不畫。</summary>
    private LevelData photoLevel;

    /// <summary>照片。這支負責釋放。</summary>
    private Texture2D photo;

    /// <summary>玩家把開關打開了沒。換關時跟著 <see cref="photoLevel"/> 一起失效，不必重設。</summary>
    private bool toggledOn;

    /// <summary>登記進擋片區的那塊矩形，離場時要取消登記。</summary>
    private RectTransform blockingArea;

    private void Awake()
    {
        EnforceIdentityTransform();

        if (inputController == null) inputController = FindFirstObjectByType<TouchInputController>();
        if (photoToggleButton == gameObject)
            Debug.LogError("ScanPhotoView 掛在照片按鈕自己身上了。按鈕藏起來時這支會跟著被停用，再也開不回來。" +
                           "請把這支移到另一個物件上。", this);

        mesh = new Mesh { name = "ScanPhotoMesh" };
        GetComponent<MeshFilter>().sharedMesh = mesh;

        material = new Material(Shader.Find("Sprites/Default"));
        meshRenderer = GetComponent<MeshRenderer>();
        meshRenderer.sharedMaterial = material;
        meshRenderer.enabled = false;

        // ⚠️ 層不存在時 Unity 不報錯，會默默掉回 Default、被盒子蓋住。所以自己檢查
        if (!SortingLayer.IsValid(SortingLayer.NameToID(PieceSorting.ScanPhotoSortingLayer)))
            Debug.LogError($"Sorting layer「{PieceSorting.ScanPhotoSortingLayer}」不存在：照片會被畫在 Default 層、" +
                           "被盒子蓋住而看不到。請在 Tag Manager 的 Sorting Layers 裡，於 Piece 與 GameUI 中間加這一層。", this);
        meshRenderer.sortingLayerName = PieceSorting.ScanPhotoSortingLayer;
        meshRenderer.sortingOrder = 0;
    }

    /// <summary>mesh 建在世界座標，所以 transform 必須是 identity——理由同 <see cref="BoxView"/>。</summary>
    private void EnforceIdentityTransform()
    {
        if (transform.localPosition == Vector3.zero
            && transform.localRotation == Quaternion.identity
            && transform.localScale == Vector3.one) return;

        Debug.LogWarning($"ScanPhotoView 的 transform 不是 identity，已經改回原點（照片是用世界座標畫的）。", this);
        transform.localPosition = Vector3.zero;
        transform.localRotation = Quaternion.identity;
        transform.localScale = Vector3.one;
    }

    /// <summary>照片鈕登記成擋片區。只登記一次——隱藏中的矩形本來就會被跳過（跟其他按鈕同一套）。</summary>
    private void OnEnable()
    {
        if (photoToggleButton == null || inputController == null) return;
        blockingArea = photoToggleButton.GetComponent<RectTransform>();
        if (blockingArea != null) inputController.RegisterPressBlockingArea(blockingArea);
    }

    private void OnDisable()
    {
        if (inputController != null && blockingArea != null) inputController.UnregisterPressBlockingArea(blockingArea);
    }

    private void OnDestroy()
    {
        ReleasePhoto();
        if (material != null) Destroy(material);
        if (mesh != null) Destroy(mesh);
    }

    /// <summary>
    /// 這一關的照片是這張，畫在這裡。<b>掃描題進關之後 <c>LevelController</c> 叫一次。</b>
    /// 照片的保管權交給這支（舊的會被釋放）。
    /// </summary>
    /// <param name="level">照片屬於哪一關（進關之後的 <c>LevelSession.Current.Data</c>）。</param>
    /// <param name="newPhoto">照片，正立（Unity 貼圖的第 0 列是最下面）。</param>
    /// <param name="minimum">照片左下角的盤面座標。</param>
    /// <param name="maximum">照片右上角的盤面座標。</param>
    public void SetPhoto(LevelData level, Texture2D newPhoto, Vector2 minimum, Vector2 maximum)
    {
        if (newPhoto != photo) ReleasePhoto();

        photoLevel = level;
        photo = newPhoto;
        toggledOn = false;

        material.mainTexture = photo;
        material.color = new Color(1f, 1f, 1f, opacity);

        mesh.Clear();
        mesh.vertices = new[]
        {
            new Vector3(minimum.x, minimum.y, 0f), new Vector3(maximum.x, minimum.y, 0f),
            new Vector3(maximum.x, maximum.y, 0f), new Vector3(minimum.x, maximum.y, 0f),
        };
        // 左下角對貼圖的 (0, 0)：Unity 貼圖的第 0 列就是最下面，所以照片是正的
        mesh.uv = new[] { new Vector2(0f, 0f), new Vector2(1f, 0f), new Vector2(1f, 1f), new Vector2(0f, 1f) };
        mesh.triangles = new[] { 0, 2, 1, 0, 3, 2 };
        mesh.RecalculateBounds();
    }

    /// <summary>照片鈕被按下：開 ↔ 關。接在 Unity <c>Button.onClick</c> 上。</summary>
    public void OnPhotoTogglePressed()
    {
        if (BelongsToCurrentLevel(out _)) toggledOn = !toggledOn;
    }

    /// <summary>
    /// 每幀重算要不要畫、按鈕要不要出現。跟其他按鈕同一個模式：只是目前狀態的函數，不訂閱任何東西。
    /// </summary>
    private void Update()
    {
        bool mine = BelongsToCurrentLevel(out LevelData level);

        // Play 中從 Inspector 調透明度也要馬上看得到
        if (mine) material.color = new Color(1f, 1f, 1f, opacity);

        meshRenderer.enabled = mine && (toggledOn || level.IsSolved);

        // 過關後按鈕退場：彈窗是滿版的，按鈕在後面也按不到；照片那時候已經自動顯示了
        if (photoToggleButton != null)
        {
            bool showButton = mine && !level.IsFinished;
            if (photoToggleButton.activeSelf != showButton) photoToggleButton.SetActive(showButton);
            if (showButton) ShowToggleState(toggledOn);
        }
    }

    /// <summary>
    /// 開關的狀態畫在按鈕上：關著是灰的、開著是亮的（2026-10-17 定）。
    ///
    /// ⚠️ <b>不用 <c>Button.interactable</c></b>：那會連點擊一起擋掉，關著的時候就再也打不開了。
    /// 改的是按鈕的<b>目標圖</b>（<c>Button.targetGraphic</c>，通常是按鈕底圖）的顏色——Button 的 Color Tint
    /// 會跟它相乘，所以按下、滑過的效果照舊。⚠️ 只染底圖，按鈕上的圖示或文字不會跟著變；要一起變是美術的事，之後再調。
    /// </summary>
    /// <param name="on">照片現在開著沒。</param>
    private void ShowToggleState(bool on)
    {
        if (toggleGraphic == null)
        {
            Button button = photoToggleButton.GetComponent<Button>();
            toggleGraphic = button != null && button.targetGraphic != null
                ? button.targetGraphic
                : photoToggleButton.GetComponent<Graphic>();
            if (toggleGraphic == null) return;      // 沒有圖可以染，就只是看不出狀態，不會壞
        }

        Color wanted = on ? onColor : offColor;
        if (toggleGraphic.color != wanted) toggleGraphic.color = wanted;
    }

    /// <param name="level">目前這一關；沒有時是 null。</param>
    /// <returns>手上的照片是不是目前這一關的。</returns>
    private bool BelongsToCurrentLevel(out LevelData level)
    {
        level = LevelSession.Current?.Data;
        return level != null && photo != null && ReferenceEquals(level, photoLevel);
    }

    private void ReleasePhoto()
    {
        if (photo != null) Destroy(photo);
        photo = null;
        photoLevel = null;
        if (material != null) material.mainTexture = null;
    }
}
