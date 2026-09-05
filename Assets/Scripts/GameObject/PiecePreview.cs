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

    private readonly PieceView[] pieces = new PieceView[BoardState.PieceCount];
    private PieceSpriteLibrary library;

    private void Start()
    {
        library = new PieceSpriteLibrary();

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
        ApplyInspectorValues();
    }

    /// <summary>把 Inspector 上那三個值套到七片身上，讓你在 Play 模式下能即時拉著看。</summary>
    private void ApplyInspectorValues()
    {
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
