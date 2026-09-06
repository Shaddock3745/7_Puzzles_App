using System;
using Tangram.Data;
using UnityEngine;

namespace Tangram.Core
{
    /// <summary>
    /// 五種片 × 翻／沒翻，共十份**浮點頂點**，開場算一次之後不再變。
    ///
    /// 這裡的頂點在**片自己的座標系**：不含旋轉、不含平移。
    /// 旋轉交給 <c>Transform</c>、平移交給 <c>Transform.position</c>，
    /// 所以碰撞盒只有在**翻面狀態改變**時才需要重設（旋轉與移動都不用）。
    ///
    /// 存起來的動機是**安全，不是效能**：
    /// <c>PiecePrototype.Get(kind).Vertices</c> 是全域共用的單例陣列，
    /// 翻面需要把 y 取負，那正好是最容易順手就地改下去的地方——
    /// 改了會污染所有用到這種片的地方（吸附、驗證、輪廓）而且不會報錯。
    /// 這張表在靜態初始化時把那個單例讀完一次，之後整個專案的顯示層都不必再碰它。
    /// </summary>
    public static class PieceVertexCache
    {
        private const int FlipStateCount = 2;

        /// <summary>索引是 <c>(int)kind * 2 + (flipped ? 1 : 0)</c>。</summary>
        private static readonly Vector2[][] Table = BuildTable();

        /// <summary>
        /// 片自己座標系下的頂點，逆時針。
        ///
        /// 回傳的是**新的副本**，呼叫端怎麼改都不會影響這張表，也不會影響原型單例。
        /// 陣列只有三到四個元素，而且只在翻面狀態改變時才會呼叫到，複製的成本可以忽略。
        /// </summary>
        /// <param name="kind">片種。</param>
        /// <param name="flipped">是否翻面（對 X 軸鏡射，也就是 y 取負）。</param>
        /// <returns>逆時針的浮點頂點陣列，長度 3（三角）或 4（正方形、平行四邊形）。</returns>
        public static Vector2[] LocalVertices(PieceKind kind, bool flipped)
        {
            Vector2[] cached = Table[(int)kind * FlipStateCount + (flipped ? 1 : 0)];
            return (Vector2[])cached.Clone();
        }

        /// <summary>沒翻面時的視覺中心，索引就是片種。宣告在 <see cref="Table"/> 之後，靠它算。</summary>
        private static readonly Vector2[] LocalCenters = BuildCenters();

        /// <summary>
        /// 片的**視覺中心**（頂點平均），在片自己的座標系、沒有旋轉也沒有翻面。
        ///
        /// 用途是旋轉的軸心（互動設計 §5：「暫放狀態下旋轉的軸心是純視覺問題，
        /// 用片的視覺中心最自然」）。<c>Transform</c> 是繞**原型原點**轉的，
        /// 而那是某個特定頂點——不補償的話片會繞著一個角甩。
        ///
        /// 翻面與旋轉都是線性變換，所以**直接套在這個中心上就好**，不必重算頂點平均。
        /// </summary>
        /// <param name="kind">片種。</param>
        /// <returns>頂點平均。</returns>
        public static Vector2 LocalCenter(PieceKind kind) => LocalCenters[(int)kind];

        /// <returns>每種片沒翻面時的頂點平均。</returns>
        private static Vector2[] BuildCenters()
        {
            var kinds = (PieceKind[])Enum.GetValues(typeof(PieceKind));
            var centers = new Vector2[kinds.Length];

            foreach (PieceKind kind in kinds)
            {
                Vector2[] vertices = Table[(int)kind * FlipStateCount];
                Vector2 sum = Vector2.zero;
                foreach (Vector2 vertex in vertices) sum += vertex;
                centers[(int)kind] = sum / vertices.Length;
            }
            return centers;
        }

        /// <returns>十份頂點，索引方式見 <see cref="Table"/>。</returns>
        private static Vector2[][] BuildTable()
        {
            var kinds = (PieceKind[])Enum.GetValues(typeof(PieceKind));
            var table = new Vector2[kinds.Length * FlipStateCount][];

            foreach (PieceKind kind in kinds)
            {
                // 讀共用單例。整個專案就只有這裡讀它，而且只讀不改。
                ExactVector2[] prototypeVertices = PiecePrototype.Get(kind).Vertices;

                table[(int)kind * FlipStateCount] = Convert(prototypeVertices, false);
                table[(int)kind * FlipStateCount + 1] = Convert(prototypeVertices, true);
            }
            return table;
        }

        /// <summary>
        /// 精確頂點環 → 浮點頂點環，需要的話一併鏡射。
        ///
        /// 鏡射時**同時反轉順序**：y 取負會把逆時針變成順時針，反轉順序剛好轉回來。
        /// 單一路徑的凸多邊形其實不在乎繞向，這步是零成本的一致性——
        /// 做了之後就不必再問「這裡到底在不在乎繞向」。
        /// 模型層的 <c>Placement.Vertices()</c> 為了同一個理由做同一件事。
        /// </summary>
        /// <param name="prototypeVertices">原型的頂點環（逆時針）。不會被修改。</param>
        /// <param name="flipped">是否鏡射。</param>
        /// <returns>新配置的浮點頂點陣列，逆時針。</returns>
        private static Vector2[] Convert(ExactVector2[] prototypeVertices, bool flipped)
        {
            int vertexCount = prototypeVertices.Length;
            var converted = new Vector2[vertexCount];

            for (int vertexIndex = 0; vertexIndex < vertexCount; vertexIndex++)
            {
                ExactVector2 point = prototypeVertices[vertexIndex];
                if (flipped) point = Symmetry.MirrorAcrossXAxis(point);

                int writeIndex = flipped ? vertexCount - 1 - vertexIndex : vertexIndex;
                converted[writeIndex] = point.ToVector2();
            }
            return converted;
        }
    }
}
