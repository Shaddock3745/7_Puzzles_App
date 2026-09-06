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

        // 盒子在所有片之下（互動設計 §13.6）
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
    /// </summary>
    /// <param name="boxRegion">盒子區域。</param>
    private void AppendOutline(Region boxRegion)
    {
        foreach (ExactVector2[] ring in boxRegion.Rings)
        {
            for (int i = 0; i < ring.Length; i++)
            {
                Vector2 start = ring[i].ToVector2();
                Vector2 end = ring[(i + 1) % ring.Length].ToVector2();

                Vector2 along = end - start;
                if (along.sqrMagnitude < 1e-12f) continue;
                along.Normalize();

                // 右法線 = 離開區域的方向
                var outward = new Vector2(along.y, -along.x) * outlineWidth;

                // 兩端各多伸一個線寬，讓相鄰兩條邊在轉角處重疊。
                // 多伸出去的部分全在區域外面，所以不會吃到可用空間；
                // 很銳的外角還是可能露出小缺口，但那也在外面，不影響擺放
                Vector2 extendedStart = start - along * outlineWidth;
                Vector2 extendedEnd = end + along * outlineWidth;

                AppendQuad(extendedStart, extendedStart + outward,
                           extendedEnd + outward, extendedEnd, outlineColor);
            }
        }
    }

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
