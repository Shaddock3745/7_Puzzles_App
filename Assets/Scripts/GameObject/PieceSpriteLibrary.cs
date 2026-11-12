using System.Collections.Generic;
using Tangram.Core;
using Tangram.Data;
using UnityEngine;

/// <summary>
/// 片的圖與顏色（互動設計 §13.7）。**五張圖照片種、七個顏色照槽索引。**
///
/// 圖是**執行時從原型頂點生出來的**，不是匯入的美術資源。這樣做換到三件事：
/// <list type="bullet">
/// <item>形狀與模型保證一致，不可能畫歪</item>
/// <item>§13.7 那兩個「做錯不會報錯」的匯入陷阱整個消失——
///       每單位像素數與軸心都由程式算，不靠人在 Inspector 填對</item>
/// <item>P1 不用花任何時間在美術上</item>
/// </list>
///
/// 描邊不另外畫：形狀從頂點**往內縮**一點再加圓角，片與片之間留下的縫隙就是邊（§13.7）。
/// 內縮寬度是模型單位，所以會跟著片一起縮放——題目很大、相機拉遠時縫會變細，
/// 那是已知的取捨，看到再處理。
///
/// <b>這支必須放在 <c>Assets/Scripts/GameObject/</c>。</b>
/// 它用到 <c>Texture2D</c> / <c>Sprite</c>，而 <c>Tools/DotnetTests</c> 的
/// <c>UnityStub.cs</c> 只補了 <c>Vector2</c> 與 <c>JsonUtility</c>——
/// 放進 <c>Core</c> 的話離線測試會整包編不過。
/// </summary>
public sealed class PieceSpriteLibrary
{
    /// <summary>
    /// 七個槽的顏色。順序對應 <c>PiecePrototype.TangramSet</c>：
    /// 大三角 ×2、中三角、小三角 ×2、正方形、平行四邊形。
    ///
    /// **兩片大三角刻意不同色**（2026-09-04 定）。這不影響任何邏輯，
    /// 因為提示的完成判定是按**片種**比對、不綁死槽索引（互動設計 §9），
    /// 而覆蓋框是白色虛線不帶顏色——不會反過來暗示玩家「同種片不能互換」。
    /// </summary>
    private static readonly Color[] SlotColors =
    {
        new Color(0.910f, 0.380f, 0.235f),   // 0 大三角
        new Color(0.914f, 0.631f, 0.231f),   // 1 大三角
        new Color(0.659f, 0.757f, 0.247f),   // 2 中三角
        new Color(0.247f, 0.710f, 0.541f),   // 3 小三角
        new Color(0.243f, 0.608f, 0.831f),   // 4 小三角
        new Color(0.420f, 0.447f, 0.788f),   // 5 正方形
        new Color(0.831f, 0.345f, 0.608f)    // 6 平行四邊形
    };

    private readonly float texturePixelsPerModelUnit;
    private readonly float insetModelUnits;
    private readonly float cornerRadiusModelUnits;
    private readonly Dictionary<PieceKind, Sprite> generatedSprites = new Dictionary<PieceKind, Sprite>();

    /// <param name="texturePixelsPerModelUnit">
    /// 貼圖解析度：一個模型單位畫幾個紋理像素。大三角跨 4 個模型單位，
    /// 所以 128 會產生約 512 px 的圖。七巧板一半的角度是 45°，
    /// sprite 轉 45° 會被雙線性濾波，畫大一點斜邊才不會糊。
    /// </param>
    /// <param name="insetModelUnits">往內縮多少（模型單位）。這就是片與片之間那條縫的一半寬。</param>
    /// <param name="cornerRadiusModelUnits">圓角半徑（模型單位）。</param>
    public PieceSpriteLibrary(float texturePixelsPerModelUnit = 128f,
                              float insetModelUnits = 0.02f,
                              float cornerRadiusModelUnits = 0.005f)
    {
        this.texturePixelsPerModelUnit = texturePixelsPerModelUnit;
        this.insetModelUnits = insetModelUnits;
        this.cornerRadiusModelUnits = cornerRadiusModelUnits;
    }

    /// <summary>
    /// 這一槽的顏色。
    /// </summary>
    /// <param name="slotIndex">槽索引 0~6。</param>
    /// <returns>不透明的顏色。透明度由 <c>PieceView</c> 依「吸住了沒」另外套。</returns>
    public Color ColorFor(int slotIndex) => SlotColors[slotIndex];

    /// <summary>
    /// 這一種片的圖，第一次呼叫時才生成，之後重用。
    ///
    /// 圖永遠是**沒有翻面、旋轉 0** 的姿態：翻面交給 <c>SpriteRenderer.flipY</c>、
    /// 旋轉交給 <c>Transform</c>（互動設計 §13.4）。
    /// </summary>
    /// <param name="kind">片種。</param>
    /// <returns>軸心已經對到**原型原點**、每單位像素數已經對到模型單位的 Sprite。</returns>
    public Sprite SpriteFor(PieceKind kind)
    {
        if (!generatedSprites.TryGetValue(kind, out Sprite sprite))
        {
            sprite = GenerateSprite(kind);
            generatedSprites[kind] = sprite;
        }
        return sprite;
    }

    /// <summary>
    /// 釋放生成出來的貼圖。執行時 <c>new</c> 出來的 <c>Texture2D</c> 不會被
    /// 場景切換自動回收，換場景或重開題庫時記得呼叫。
    /// </summary>
    public void ReleaseGeneratedSprites()
    {
        foreach (Sprite sprite in generatedSprites.Values)
        {
            if (sprite == null) continue;
            Object.Destroy(sprite.texture);
            Object.Destroy(sprite);
        }
        generatedSprites.Clear();
    }

    /// <param name="kind">片種。</param>
    /// <returns>新生成的 Sprite。</returns>
    private Sprite GenerateSprite(PieceKind kind)
    {
        // 原型頂點，逆時針，片自己的座標系（原型原點就是 (0,0)）
        Vector2[] vertices = PieceVertexCache.LocalVertices(kind, false);

        Vector2 minimum = vertices[0];
        Vector2 maximum = vertices[0];
        foreach (Vector2 vertex in vertices)
        {
            minimum = Vector2.Min(minimum, vertex);
            maximum = Vector2.Max(maximum, vertex);
        }

        // 抗鋸齒需要邊緣外面留一點空間，否則圓角會被切在貼圖邊上
        const int paddingPixels = 2;
        int width = Mathf.CeilToInt((maximum.x - minimum.x) * texturePixelsPerModelUnit) + paddingPixels * 2;
        int height = Mathf.CeilToInt((maximum.y - minimum.y) * texturePixelsPerModelUnit) + paddingPixels * 2;

        // 先侵蝕「內縮 + 圓角半徑」，再把結果膨脹回圓角半徑，就得到「內縮且圓角」的形狀。
        // 這是形態學的標準做法，比直接算多邊形偏移好寫太多——
        // 因為膨脹只要用「到侵蝕後多邊形的距離 ≤ 半徑」判斷，不用真的把圓角的弧construct 出來
        Vector2[] erodedPolygon = ErodeConvexPolygon(vertices, insetModelUnits + cornerRadiusModelUnits);

        // mipChain 開著：七巧板一半的角度是 45°，相機拉遠時沒有 mipmap 的斜邊會閃
        var texture = new Texture2D(width, height, TextureFormat.RGBA32, mipChain: true)
        {
            filterMode = FilterMode.Bilinear,
            wrapMode = TextureWrapMode.Clamp
        };

        var pixels = new Color32[width * height];
        for (int y = 0; y < height; y++)
        {
            for (int x = 0; x < width; x++)
            {
                // 像素中心 → 模型座標
                var modelPoint = new Vector2(
                    minimum.x + (x + 0.5f - paddingPixels) / texturePixelsPerModelUnit,
                    minimum.y + (y + 0.5f - paddingPixels) / texturePixelsPerModelUnit);

                float distance = DistanceToConvexPolygon(modelPoint, erodedPolygon);

                // 邊界落在 distance == cornerRadius，前後各半個像素做漸變
                float coverage = Mathf.Clamp01(
                    (cornerRadiusModelUnits - distance) * texturePixelsPerModelUnit + 0.5f);

                // 貼圖畫白色，顏色靠 SpriteRenderer.color 上色（同一個欄位也負責透明度）
                pixels[y * width + x] = new Color32(255, 255, 255, (byte)(coverage * 255f));
            }
        }
        texture.SetPixels32(pixels);
        texture.Apply();

        // 軸心對到**原型原點**（模型座標的 (0,0)），不是圖的中心。
        // 這是 §13.7 那個「錯了不會報錯、只會覺得吸附怪怪的」的陷阱，這裡用算的所以不會錯
        var pivot = new Vector2(
            (-minimum.x * texturePixelsPerModelUnit + paddingPixels) / width,
            (-minimum.y * texturePixelsPerModelUnit + paddingPixels) / height);

        // 模型:世界 = 1:1，所以 Sprite 的每單位像素數就是貼圖解析度，
        // 這樣圖在世界裡的大小自動等於模型的大小
        return Sprite.Create(texture, new Rect(0f, 0f, width, height),
                             pivot, texturePixelsPerModelUnit);
    }

    /// <summary>
    /// 把凸多邊形往內縮。凸多邊形的侵蝕就是「每條邊的半平面往內推」，
    /// 新頂點是相鄰兩條推過的邊延長線的交點——不會有一般多邊形偏移那種頂點分裂問題。
    ///
    /// 縮太多會讓多邊形翻面或消失（小三角的內切圓半徑只有約 0.586 個模型單位），
    /// 所以縮出來不合法就對半砍再試。
    /// </summary>
    /// <param name="vertices">原始頂點環，必須逆時針且凸。</param>
    /// <param name="distance">要往內縮的距離（模型單位）。</param>
    /// <returns>侵蝕後的頂點環，仍然逆時針。距離太大時是縮得比較少的版本。</returns>
    private static Vector2[] ErodeConvexPolygon(Vector2[] vertices, float distance)
    {
        for (int attempt = 0; attempt < 8; attempt++)
        {
            Vector2[] eroded = TryErode(vertices, distance);
            if (eroded != null) return eroded;
            distance *= 0.5f;
        }
        return vertices;   // 退無可退就不縮，總比回傳壞掉的形狀好
    }

    /// <param name="vertices">原始頂點環（逆時針、凸）。</param>
    /// <param name="distance">往內縮的距離。</param>
    /// <returns>侵蝕後的頂點環；退化（面積非正）時回 null。</returns>
    private static Vector2[] TryErode(Vector2[] vertices, float distance)
    {
        int vertexCount = vertices.Length;
        var offsetOrigins = new Vector2[vertexCount];
        var edgeDirections = new Vector2[vertexCount];

        for (int i = 0; i < vertexCount; i++)
        {
            Vector2 edge = vertices[(i + 1) % vertexCount] - vertices[i];
            edgeDirections[i] = edge.normalized;

            // 逆時針 ⇒ 內部在有向邊的左側，左法線是 (−dy, dx)
            var inwardNormal = new Vector2(-edgeDirections[i].y, edgeDirections[i].x);
            offsetOrigins[i] = vertices[i] + inwardNormal * distance;
        }

        var eroded = new Vector2[vertexCount];
        for (int i = 0; i < vertexCount; i++)
        {
            int previous = (i + vertexCount - 1) % vertexCount;
            if (!TryIntersectLines(offsetOrigins[previous], edgeDirections[previous],
                                   offsetOrigins[i], edgeDirections[i], out eroded[i]))
                return null;
        }

        return SignedDoubleArea(eroded) > 1e-6f ? eroded : null;
    }

    /// <param name="firstPoint">第一條線上的一點。</param>
    /// <param name="firstDirection">第一條線的方向（單位向量）。</param>
    /// <param name="secondPoint">第二條線上的一點。</param>
    /// <param name="secondDirection">第二條線的方向（單位向量）。</param>
    /// <param name="intersection">交點；平行時是 <c>Vector2.zero</c>。</param>
    /// <returns>兩條線是否相交（不平行）。</returns>
    private static bool TryIntersectLines(Vector2 firstPoint, Vector2 firstDirection,
                                          Vector2 secondPoint, Vector2 secondDirection,
                                          out Vector2 intersection)
    {
        intersection = Vector2.zero;
        float denominator = firstDirection.x * secondDirection.y - firstDirection.y * secondDirection.x;
        if (Mathf.Abs(denominator) < 1e-6f) return false;

        Vector2 delta = secondPoint - firstPoint;
        float travel = (delta.x * secondDirection.y - delta.y * secondDirection.x) / denominator;
        intersection = firstPoint + firstDirection * travel;
        return true;
    }

    /// <param name="polygon">頂點環。</param>
    /// <returns>面積 ×2，正 = 逆時針。</returns>
    private static float SignedDoubleArea(Vector2[] polygon)
    {
        float accumulated = 0f;
        for (int i = 0; i < polygon.Length; i++)
        {
            Vector2 current = polygon[i];
            Vector2 next = polygon[(i + 1) % polygon.Length];
            accumulated += current.x * next.y - next.x * current.y;
        }
        return accumulated;
    }

    /// <summary>
    /// 點到凸多邊形的距離。在裡面回 0，在外面回到最近邊的歐氏距離。
    ///
    /// 用真正的歐氏距離（不是「半平面裡最大的那個值」）才會得到**圓角**；
    /// 半平面那種算法會把角推成尖的，方向剛好相反。
    /// </summary>
    /// <param name="point">要測的點。</param>
    /// <param name="polygon">凸多邊形，逆時針。</param>
    /// <returns>距離，非負。</returns>
    private static float DistanceToConvexPolygon(Vector2 point, Vector2[] polygon)
    {
        bool inside = true;
        float nearestDistance = float.MaxValue;

        for (int i = 0; i < polygon.Length; i++)
        {
            Vector2 start = polygon[i];
            Vector2 end = polygon[(i + 1) % polygon.Length];
            Vector2 edge = end - start;

            // 逆時針 ⇒ 內部在左側 ⇒ 叉積為負代表在這條邊外面
            if (edge.x * (point.y - start.y) - edge.y * (point.x - start.x) < 0f) inside = false;

            nearestDistance = Mathf.Min(nearestDistance, DistanceToSegment(point, start, end));
        }
        return inside ? 0f : nearestDistance;
    }

    /// <param name="point">要測的點。</param>
    /// <param name="start">線段起點。</param>
    /// <param name="end">線段終點。</param>
    /// <returns>點到線段的距離。</returns>
    private static float DistanceToSegment(Vector2 point, Vector2 start, Vector2 end)
    {
        Vector2 segment = end - start;
        float lengthSquared = segment.sqrMagnitude;
        if (lengthSquared < 1e-12f) return Vector2.Distance(point, start);

        float projection = Mathf.Clamp01(Vector2.Dot(point - start, segment) / lengthSquared);
        return Vector2.Distance(point, start + segment * projection);
    }
}
