using System.Collections.Generic;
using Tangram.Core;
using Tangram.Data;
using UnityEngine;

/// <summary>
/// 盒子的顯示（互動設計 §14.5）。填色 + 輪廓，兩者放在同一個 mesh 裡，用頂點顏色分開。
///
/// <b>填色不做「有洞多邊形的三角化」。</b>盒子就是七片的聯集
/// （<c>Puzzle.FromPlacements</c> 的定義），而每一片都是凸的（3 或 4 個頂點），
/// 凸多邊形從頂點 0 拉扇形就是三角化，永遠正確。
/// <b>洞會自動出現</b>——那些地方本來就沒有片蓋住。
/// 代價是這支需要參考解，不能只靠輪廓畫。
///
/// <b>不記資料。</b>每次重畫都要給全套，所以不會有「畫的是上一關」這種狀態。
/// </summary>
[DisallowMultipleComponent]
[RequireComponent(typeof(MeshFilter))]
[RequireComponent(typeof(MeshRenderer))]
public class BoxView : MonoBehaviour
{
    [Tooltip("盒子內部的底色。P1 先用不透明的深色，與半透明片的混色暫不處理。")]
    [SerializeField] private Color fillColor = new Color(0.16f, 0.17f, 0.20f, 1f);

    [Tooltip("輪廓顏色。")]
    [SerializeField] private Color outlineColor = new Color(0.85f, 0.87f, 0.90f, 1f);

    [Tooltip("輪廓寬度，模型單位（固定世界寬度）。只往盒子外面畫，不會吃到內部空間。" +
             "相機拉遠時看起來會變細，P1 先這樣。")]
    [SerializeField] private float outlineWidth = 0.08f;

    [Tooltip("留空的話會用 Sprites/Default 自己建一個。指定的材質要吃頂點顏色。")]
    [SerializeField] private Material boxMaterial;

    private MeshFilter meshFilter;
    private MeshRenderer meshRenderer;
    private Mesh mesh;

    private readonly List<Vector3> vertices = new List<Vector3>();
    private readonly List<Color> colors = new List<Color>();
    private readonly List<int> triangles = new List<int>();

    private void Awake()
    {
        EnforceIdentityTransform();

        meshFilter = GetComponent<MeshFilter>();
        meshRenderer = GetComponent<MeshRenderer>();

        mesh = new Mesh { name = "BoxMesh" };
        mesh.MarkDynamic();
        meshFilter.sharedMesh = mesh;

        if (boxMaterial == null) boxMaterial = new Material(Shader.Find("Sprites/Default"));
        meshRenderer.sharedMaterial = boxMaterial;

        // 盒子在所有片之下（互動設計 §13.6）。
        // ⚠️ 決定它在片之下的是 **sorting layer**，不是 sortingOrder——
        // 兩者在不同層，order 根本不會拿來比。設 order 只是讓層內有個確定的值
        meshRenderer.sortingLayerName = PieceSorting.BoxSortingLayer;
        meshRenderer.sortingOrder = PieceSorting.BoxSortingOrder;
    }

    /// <summary>
    /// mesh 直接建在**世界座標**，所以這個物件的 transform 必須是 identity。
    ///
    /// <b>這件事不能只寫在註解裡。</b>物件被隨手拖過、或從別的物件複製出來時，
    /// transform 就不是原點了，而症狀是「盒子畫在離相機很遠的地方」——
    /// 相機其實對得好好的（它 fit 的是模型認定的位置），被推走的是畫面，
    /// 而且**完全不會報錯**。所以這裡直接改回來，並說明原因。
    /// </summary>
    private void EnforceIdentityTransform()
    {
        if (transform.localPosition == Vector3.zero
            && transform.localRotation == Quaternion.identity
            && transform.localScale == Vector3.one) return;

        Debug.LogWarning(
            $"BoxView 的 transform 不是 identity（位置 {transform.localPosition}、" +
            $"縮放 {transform.localScale}），已經改回原點。" +
            "盒子的 mesh 是建在世界座標的，transform 有偏移會讓整個盒子畫到別的地方去。", this);

        transform.localPosition = Vector3.zero;
        transform.localRotation = Quaternion.identity;
        transform.localScale = Vector3.one;
    }

    /// <summary>
    /// 重畫盒子。mesh 建在世界座標（見 <see cref="EnforceIdentityTransform"/>）。
    /// </summary>
    /// <param name="boxRegion">盒子區域。輪廓從它的環拿，外環與洞環都要畫。</param>
    /// <param name="referenceSolution">
    /// 這一關的參考解（已平移到盤面座標）。填色靠它三角化——
    /// 傳 null 或空的話只會畫出輪廓，內部是空的。
    /// </param>
    public void Redraw(Region boxRegion, IReadOnlyList<Placement> referenceSolution)
    {
        if (mesh == null) Awake();

        vertices.Clear();
        colors.Clear();
        triangles.Clear();

        if (referenceSolution != null) AppendFill(referenceSolution);
        if (boxRegion != null) AppendOutline(boxRegion);

        mesh.Clear();
        mesh.SetVertices(vertices);
        mesh.SetColors(colors);
        mesh.SetTriangles(triangles, 0);
        mesh.RecalculateBounds();
    }

    /// <summary>
    /// 填色：參考解每一片各自拉扇形。七片加起來約 14 個三角形。
    /// </summary>
    /// <param name="referenceSolution">參考解，已平移到盤面座標。</param>
    private void AppendFill(IReadOnlyList<Placement> referenceSolution)
    {
        foreach (Placement piece in referenceSolution)
        {
            ExactVector2[] pieceVertices = piece.Vertices();   // 永遠 CCW，3 或 4 個
            int baseIndex = vertices.Count;

            foreach (ExactVector2 vertex in pieceVertices)
            {
                vertices.Add(vertex.ToVector2());
                colors.Add(fillColor);
            }

            // 扇形：(0,1,2)、(0,2,3)……凸多邊形這樣一定對，不用耳切也不用判繞向
            for (int i = 1; i < pieceVertices.Length - 1; i++)
            {
                triangles.Add(baseIndex);
                triangles.Add(baseIndex + i);
                triangles.Add(baseIndex + i + 1);
            }
        }
    }

    /// <summary>
    /// 輪廓：每個環的每條邊各鋪一個四邊形。外環與洞環都畫。
    ///
    /// <b>⚠️ 只往「外」畫，不跨過邊界線。</b>置中畫的話會往內吃掉半個線寬，
    /// 片吸到邊上時就會看起來差一截——而那個誤差跟吸附完全無關，
    /// 純粹是畫出來的東西佔了位置（見 `除錯記錄.md` 002）。
    ///
    /// 「外」的方向可以統一算：<see cref="Region"/> 的外環是 CCW、洞環是 CW，
    /// <b>所以區域內部永遠在有向邊的左側</b>——右側就是外，兩種環都適用。
    /// 洞的框線因此畫在洞裡面，一樣不吃可用空間。
    ///
    /// ⚠️ <b>轉角靠 miter，不靠把邊延長，也不另外補角</b>（2026-09-10 修了兩次）。
    ///
    /// <b>第一版錯在哪</b>：每條邊兩端各多伸「一個線寬」讓相鄰兩條重疊。
    /// 那個長度<b>只在 90° 轉角剛好正確</b>——兩條垂直的帶子各伸一個線寬，
    /// 剛好補滿角落那個 w×w 的正方形。💡 所以經典大正方形永遠不會露餡（四個角全是 90°），
    /// 這個 bug 要到 P2-4 第一次看到生成的題目才出現。
    ///
    /// <b>第二版也錯</b>：改成「邊不延長，只在凸角補三角形」。
    /// ⚠️ 那個版本假設<b>凹角不用處理，因為兩條帶子本來就重疊</b>——
    /// <b>那只在鈍的凹角成立。</b>凹角很銳（notch 很窄）時，帶子的角落是從頂點
    /// <b>垂直</b>推出去的，而那個垂直方向會<b>跨過另一條邊戳進材料裡</b>——
    /// 看起來就是一根往盒子<b>內部</b>戳的刺。
    ///
    /// <b>現在的做法</b>：相鄰兩條帶子<b>共用同一個 miter 點</b>
    /// （兩條偏移線的交點）。同一條式子兩種情況都對：
    /// <list type="bullet">
    /// <item><b>凸角</b>——交點落在外面，自動把楔形缺口補滿</item>
    /// <item><b>凹角</b>——交點落在<b>比垂直推出去更靠內</b>的地方，自動把帶子<b>裁短</b>，不會戳出去</item>
    /// </list>
    /// 所以連「哪些角要補」都不必判斷，一條邊一個四邊形就結束。
    /// </summary>
    /// <param name="boxRegion">盒子區域。</param>
    private void AppendOutline(Region boxRegion)
    {
        foreach (ExactVector2[] ring in boxRegion.Rings)
        {
            int cornerCount = ring.Length;
            if (cornerCount < 3) continue;

            // 一、每個頂點算一次 miter 位移（相鄰兩條邊共用它，接縫才不會有洞或重疊）
            var miterOffsets = new Vector2[cornerCount];
            for (int i = 0; i < cornerCount; i++)
                miterOffsets[i] = MiterOffset(ring, i, cornerCount);

            // 二、一條邊一個四邊形，兩端都用共用的 miter 點
            for (int i = 0; i < cornerCount; i++)
            {
                int next = (i + 1) % cornerCount;
                Vector2 start = ring[i].ToVector2();
                Vector2 end = ring[next].ToVector2();

                AppendQuad(start, start + miterOffsets[i],
                           end + miterOffsets[next], end, outlineColor);
            }
        }
    }

    /// <summary>
    /// 一個頂點的 miter 位移：相鄰兩條邊各自往外偏移一個線寬之後，那兩條線的交點。
    ///
    /// 公式是 <c>(n₁ + n₂) · w / (1 + n₁·n₂)</c>。長度等於 <c>w / cos(半夾角)</c>，
    /// 45° 網格上最壞是 135° 轉角的約 <b>2.6 w</b>，有界。
    ///
    /// ⚠️ 兩條邊接近<b>原地折返</b>時分母會趨近 0、交點飛到無限遠。
    /// 那種頂點在合法的輪廓上不存在（每個線段只被一片擁有），
    /// 但真的碰到就退回單邊法線，寧可畫得難看也不要生出一根射到畫面外的刺。
    ///
    /// ⚠️ 另一個理論上的限制：邊長如果比 miter 位移還短，偏移出來的環會自我相交。
    /// 這裡不會發生——線寬預設 0.08，而模型的邊最短是 2，差了 25 倍。
    /// </summary>
    /// <param name="ring">頂點環。</param>
    /// <param name="cornerIndex">要算哪一個頂點。</param>
    /// <param name="cornerCount">環的頂點數。</param>
    /// <returns>從頂點往外的位移量。</returns>
    private Vector2 MiterOffset(ExactVector2[] ring, int cornerIndex, int cornerCount)
    {
        Vector2 previous = ring[(cornerIndex - 1 + cornerCount) % cornerCount].ToVector2();
        Vector2 corner = ring[cornerIndex].ToVector2();
        Vector2 next = ring[(cornerIndex + 1) % cornerCount].ToVector2();

        Vector2 incoming = corner - previous;
        Vector2 outgoing = next - corner;
        if (incoming.sqrMagnitude < 1e-12f || outgoing.sqrMagnitude < 1e-12f) return Vector2.zero;
        incoming.Normalize();
        outgoing.Normalize();

        // 右法線 = 離開區域的方向（外環 CCW、洞環 CW，內部永遠在左）
        Vector2 normalIncoming = RightNormal(incoming);
        Vector2 normalOutgoing = RightNormal(outgoing);

        float denominator = 1f + Vector2.Dot(normalIncoming, normalOutgoing);
        if (denominator <= MinimumMiterDenominator) return normalIncoming * outlineWidth;

        return (normalIncoming + normalOutgoing) * (outlineWidth / denominator);
    }

    /// <summary>miter 的分母小於這個值就退回單邊法線，避免長度爆掉。</summary>
    private const float MinimumMiterDenominator = 0.05f;

    /// <param name="direction">單位方向。</param>
    /// <returns>它的右法線。內部在左，所以右邊就是「外」。</returns>
    private static Vector2 RightNormal(Vector2 direction) => new Vector2(direction.y, -direction.x);

    /// <summary>把一個四邊形加進 mesh（拆成兩個三角形）。</summary>
    /// <param name="first">第一個角。</param>
    /// <param name="second">第二個角。</param>
    /// <param name="third">第三個角。</param>
    /// <param name="fourth">第四個角。</param>
    /// <param name="color">四個頂點的顏色。</param>
    private void AppendQuad(Vector2 first, Vector2 second, Vector2 third, Vector2 fourth, Color color)
    {
        int baseIndex = vertices.Count;

        vertices.Add(first);
        vertices.Add(second);
        vertices.Add(third);
        vertices.Add(fourth);
        for (int i = 0; i < 4; i++) colors.Add(color);

        triangles.Add(baseIndex);
        triangles.Add(baseIndex + 1);
        triangles.Add(baseIndex + 2);

        triangles.Add(baseIndex);
        triangles.Add(baseIndex + 2);
        triangles.Add(baseIndex + 3);
    }

    private void OnDestroy()
    {
        // 執行時 new 出來的 Mesh 不會被場景切換自動回收
        if (mesh != null) Destroy(mesh);
    }
}
