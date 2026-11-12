using UnityEngine;

/// <summary>
/// UI 物件在螢幕上佔哪一塊（像素，原點左下）。<b>盒子區躲按鈕排與擋片區共用這一份換算</b>（2026-09-15）。
///
/// ⚠️ <b>為什麼不用 <c>RectTransformUtility.WorldToScreenPoint</c> ／ <c>RectangleContainsScreenPoint</c> 配畫布的相機</b>：
/// 這個專案的畫布是 <b>Screen Space - Camera</b>，它會跟著相機走，
/// 但要等到畫面要畫之前（<c>Canvas.willRenderCanvases</c>）才把自己搬到相機的新位置。
/// 在那之前相機已經被 <c>BoardView</c> 改過的話，「畫布的舊世界座標 × 相機的新矩陣」換算出來的就是錯的。
/// 而盒子區又是照按鈕排算的、相機又是照盒子區算的——<b>讀錯 → 改相機 → 又讀錯</b>，
/// 實測盒子連抖十幀才收斂，每一次重算還會把正在拖的片放回原位。
///
/// <b>這裡的做法完全不讀相機</b>：把四個角換成<b>根畫布自己的本地座標</b>，再照「在根畫布裡的比例 × 螢幕尺寸」換成像素。
/// 按鈕與根畫布永遠是同一個時間點的狀態，就算畫布還沒跟上相機，兩者的相對位置照樣是對的。
/// Screen Space - Overlay 也適用（根畫布的矩形一樣就是整個螢幕）。
///
/// ⚠️ <b>前提是根畫布蓋滿整個螢幕</b>：相機的 Viewport Rect 不是全螢幕的話，Camera 模式的畫布只蓋那一塊，這裡會算錯。
/// World Space 的畫布不在這個前提裡，退回用畫布的相機換算（那種畫布不跟相機走，沒有上面的問題）。
/// </summary>
public static class UiScreenRect
{
    /// <summary>四個角，重用免得每次配置。只在主執行緒用。</summary>
    private static readonly Vector3[] CornerBuffer = new Vector3[4];

    /// <summary>
    /// UI 物件在螢幕上的軸對齊矩形。
    /// ⚠️ 物件有旋轉的話回的是外接矩形——按鈕排與按鈕都沒有旋轉，所以沒差。
    /// </summary>
    /// <param name="area">UI 物件。</param>
    /// <param name="screenRect">螢幕上的矩形（像素，原點左下）；失敗時是 <c>Rect.zero</c>。</param>
    /// <returns>換算得出來的話 true。不在任何畫布底下、或根畫布大小是 0（初始化中）時 false。</returns>
    public static bool TryGet(RectTransform area, out Rect screenRect)
    {
        screenRect = Rect.zero;
        if (area == null) return false;

        Canvas canvas = area.GetComponentInParent<Canvas>();
        if (canvas == null) return false;
        Canvas rootCanvas = canvas.rootCanvas;

        area.GetWorldCorners(CornerBuffer);

        float minimumX = float.PositiveInfinity, minimumY = float.PositiveInfinity;
        float maximumX = float.NegativeInfinity, maximumY = float.NegativeInfinity;

        if (rootCanvas.renderMode == RenderMode.WorldSpace)
        {
            foreach (Vector3 corner in CornerBuffer)
            {
                Vector2 point = RectTransformUtility.WorldToScreenPoint(rootCanvas.worldCamera, corner);
                Include(point.x, point.y);
            }
        }
        else
        {
            var rootTransform = (RectTransform)rootCanvas.transform;
            Rect rootRect = rootTransform.rect;
            if (!(rootRect.width > 0f) || !(rootRect.height > 0f)) return false;

            float pixelsPerRootUnitX = Screen.width / rootRect.width;
            float pixelsPerRootUnitY = Screen.height / rootRect.height;

            foreach (Vector3 corner in CornerBuffer)
            {
                Vector3 local = rootTransform.InverseTransformPoint(corner);
                Include((local.x - rootRect.xMin) * pixelsPerRootUnitX,
                        (local.y - rootRect.yMin) * pixelsPerRootUnitY);
            }
        }

        screenRect = Rect.MinMaxRect(minimumX, minimumY, maximumX, maximumY);
        return true;

        void Include(float x, float y)
        {
            if (x < minimumX) minimumX = x;
            if (x > maximumX) maximumX = x;
            if (y < minimumY) minimumY = y;
            if (y > maximumY) maximumY = y;
        }
    }
}
