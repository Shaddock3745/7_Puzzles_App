using Tangram.Core;
using Tangram.Data;
using UnityEngine;

/// <summary>
/// ⚠️ <b>P1-2 的驗證鷹架，不是產品程式。</b>
///
/// 存在的理由只有一個：P1-2 的完成定義是「畫面上形狀正確」，
/// 但在 P1-5（待用區／初始散佈）與 P1-6（拖曳）做出來之前，
/// 場上根本沒有東西會生出 <c>PieceView</c>，也就沒辦法檢查。
///
/// <b>用法</b>：場景裡建一個空物件掛上它，按 Play。七片會排成一列，
/// 在 Inspector 拉 <c>rotation</c> / <c>flipped</c> / <c>showAsSnapped</c> 就能即時看效果。
///
/// <b>P1-5 做好之後把這支刪掉。</b>
/// </summary>
[DisallowMultipleComponent]
public class PiecePreview : MonoBehaviour
{
    [Tooltip("七片之間的間距（模型單位）。大三角跨 4 個單位，所以 5 大概剛好不重疊。")]
    [SerializeField] private float spacing = 5f;

    [Tooltip("套在全部七片上的旋轉格數 0~7，每格 45°。")]
    [Range(0, 7)]
    [SerializeField] private int rotation;

    [Tooltip("套在全部七片上的翻面。只有平行四邊形看得出差別。")]
    [SerializeField] private bool flipped;

    [Tooltip("勾起來 = 畫成已吸附（不透明）；不勾 = 未吸附（半透明）。")]
    [SerializeField] private bool showAsSnapped;

    [Header("片的圖（Play 模式下改了會即時重生）")]
    [Tooltip("往內縮多少（模型單位）。片與片之間那條縫的一半寬就是它。\n" +
             "盒子的框線只往外畫，不會再額外吃掉空間，所以「片跟盒子邊的縫」就是這個值。")]
    [Range(0f, 0.25f)]
    [SerializeField] private float insetModelUnits = 0.02f;

    [Tooltip("圓角半徑（模型單位）。⚠️ 銳角被吃掉的比直角多很多，" +
             "所以大三角的斜角看起來會比正方形的直角縮更多。")]
    [Range(0f, 0.25f)]
    [SerializeField] private float cornerRadiusModelUnits = 0.005f;

    private readonly PieceView[] pieces = new PieceView[BoardState.PieceCount];
    private PieceSpriteLibrary library;

    // 目前這批圖是用哪組參數生的，用來偵測 Inspector 有沒有被拉動
    private float builtInset;
    private float builtCornerRadius;

    // 上一次推給七片的那組姿態值。⚠️ **只有真的被拉動才推**，見 ApplyInspectorValues
    private int appliedRotation = -1;
    private bool appliedFlipped;
    private bool appliedShowAsSnapped;

    private void Start()
    {
        library = BuildLibrary();

        for (int slotIndex = 0; slotIndex < BoardState.PieceCount; slotIndex++)
        {
            PieceKind kind = PiecePrototype.TangramSet[slotIndex];

            var pieceObject = new GameObject($"Piece{slotIndex}_{kind}");
            pieceObject.transform.SetParent(transform, worldPositionStays: false);

            PieceView piece = pieceObject.AddComponent<PieceView>();
            piece.Initialize(kind, slotIndex, library);

            // 排成一列並置中。⚠️ 這裡刻意讓父物件維持 identity（不旋轉、不縮放），
            // 否則 transform.position 讀回來就不再是寫進去的值（互動設計 §13.2）
            float offsetX = (slotIndex - (BoardState.PieceCount - 1) * 0.5f) * spacing;
            piece.Position = new Vector2(offsetX, 0f);

            pieces[slotIndex] = piece;
        }
        ApplyInspectorValues();
    }

    private void Update()
    {
        RebuildSpritesIfParametersChanged();
        ApplyInspectorValues();
    }

    /// <returns>用目前 Inspector 上那組參數生出來的圖庫。</returns>
    private PieceSpriteLibrary BuildLibrary()
    {
        builtInset = insetModelUnits;
        builtCornerRadius = cornerRadiusModelUnits;
        return new PieceSpriteLibrary(insetModelUnits: insetModelUnits,
                                      cornerRadiusModelUnits: cornerRadiusModelUnits);
    }

    /// <summary>
    /// Inspector 上的內縮或圓角被拉動就重生五張圖，讓你在 Play 模式下拉著看。
    ///
    /// ⚠️ <b>重生五張圖是逐像素運算，會頓一下。</b>只在真的變動時做，
    /// 所以拉滑桿的過程中每一格都會頓——這是調參鷹架，不是產品行為。
    /// </summary>
    private void RebuildSpritesIfParametersChanged()
    {
        const float epsilon = 1e-4f;
        if (Mathf.Abs(insetModelUnits - builtInset) < epsilon
            && Mathf.Abs(cornerRadiusModelUnits - builtCornerRadius) < epsilon) return;

        library?.ReleaseGeneratedSprites();
        library = BuildLibrary();

        // Initialize 只重設身分、圖與碰撞盒，不碰姿態與位置，所以可以安全地再叫一次
        foreach (PieceView piece in pieces)
        {
            if (piece == null) continue;
            piece.Initialize(piece.Kind, piece.SlotIndex, library);
        }
    }

    /// <summary>
    /// 把 Inspector 上那三個值套到七片身上，讓你在 Play 模式下能即時拉著看。
    ///
    /// ⚠️ <b>只有那三個值真的被拉動才推，不是每幀推</b>（2026-09-07 修）。
    ///
    /// 原本是每幀無條件推，而這支<b>目前是場上唯一會生出七片的東西</b>（P1-5 還沒做），
    /// 所以它等於每幀把七片的姿態重設回滑桿上的值：
    /// <list type="bullet">
    /// <item>旋轉鈕按下去改了旋轉格數，<b>下一幀就被蓋回去</b>——按鈕看起來完全沒反應</item>
    /// <item>翻面鈕同理</item>
    /// <item><c>IsSnapped</c> 每幀強寫，等於在跟拖曳控制器搶。拖曳之所以看起來正常，
    /// 只是因為 <c>TouchInputController.Update</c> 剛好排在這支後面，
    /// 而 <c>MonoBehaviour</c> 之間的順序是<b>未定義</b>的</item>
    /// </list>
    ///
    /// 三個症狀都不會有任何錯誤訊息。<b>P1-5 做好之後這支要整個刪掉</b>，
    /// 它已經在跟真正的系統搶同一批片了。
    /// </summary>
    private void ApplyInspectorValues()
    {
        if (rotation == appliedRotation
            && flipped == appliedFlipped
            && showAsSnapped == appliedShowAsSnapped) return;

        appliedRotation = rotation;
        appliedFlipped = flipped;
        appliedShowAsSnapped = showAsSnapped;

        foreach (PieceView piece in pieces)
        {
            if (piece == null) continue;

            // 照片種的循環長度取餘數，不然正方形拉到 3 會看起來跟 1 一樣，
            // 讓人以為是 bug（SnapSolver.RotationCycleLength：三角 8 / 正方形 2 / 平行四邊形 4）
            piece.Rotation = rotation % SnapSolver.RotationCycleLength(piece.Kind);
            piece.IsFlipped = flipped && SnapSolver.CanFlip(piece.Kind);
            piece.IsSnapped = showAsSnapped;
        }
    }

    private void OnDestroy()
    {
        library?.ReleaseGeneratedSprites();
    }
}
