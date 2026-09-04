using System;

namespace Tangram.Data
{
    /// <summary>
    /// 一片的絕對擺放：原型 + 旋轉 + 翻面 + 平移。
    /// 這是「重算出來的暫態」，不入庫（§3）——庫裡只有 Arrangement 的相對編碼。
    /// </summary>
    public readonly struct Placement : IEquatable<Placement>
    {
        public readonly PieceKind Kind;
        public readonly int Rotation;              // 0..7，×45°
        public readonly bool IsFlipped;
        public readonly ExactVector2 Translation;

        /// <param name="kind">片種。</param>
        /// <param name="rotation">旋轉值，會用 Modulo8 正規化，所以傳 −1 或 9 都安全。</param>
        /// <param name="flipped">是否翻面（對 X 軸鏡射）。</param>
        /// <param name="translation">平移量，也就是片自己的原點在世界座標的位置。</param>
        public Placement(PieceKind kind, int rotation, bool flipped, ExactVector2 translation)
        {
            Kind = kind;
            Rotation = Symmetry.Modulo8(rotation);
            IsFlipped = flipped;
            Translation = translation;
        }

        /// <summary>對應的原型。只是陣列索引，不配置記憶體，但回傳的是共用單例，不要改它。</summary>
        public PiecePrototype Prototype => PiecePrototype.Get(Kind);

        /// <summary>這一片有幾個節點。</summary>
        public int NodeCount => Prototype.NodeCount;

        /// <param name="translation">新的平移量。</param>
        /// <returns>姿態不變、只換位置的新擺放。</returns>
        public Placement WithTranslation(ExactVector2 translation)
            => new Placement(Kind, Rotation, IsFlipped, translation);

        /// <param name="delta">位移量。</param>
        /// <returns>平移一段之後的新擺放。</returns>
        public Placement Translated(ExactVector2 delta)
            => new Placement(Kind, Rotation, IsFlipped, Translation + delta);

        /// <summary>原型座標 → 世界座標。順序：翻面 → 旋轉 → 平移。</summary>
        /// <param name="prototypePoint">原型座標系的點。</param>
        /// <returns>世界座標的點。</returns>
        public ExactVector2 ToWorld(ExactVector2 prototypePoint)
            => Translation + Symmetry.Apply(Rotation, IsFlipped, prototypePoint);

        /// <summary>指定節點的世界座標。索引即 links 裡的節點索引。</summary>
        /// <param name="nodeIndex">節點索引，0 到 NodeCount−1。</param>
        /// <returns>該節點的世界座標。</returns>
        public ExactVector2 Node(int nodeIndex) => ToWorld(Prototype.Nodes[nodeIndex]);

        /// <summary>全部節點的世界座標。索引與原型的節點索引一一對應（翻面也不變）。</summary>
        /// <returns>新配置的陣列。放在每幀的最內層迴圈會有 GC 壓力。</returns>
        public ExactVector2[] Nodes()
        {
            ExactVector2[] prototypeNodes = Prototype.Nodes;
            var worldNodes = new ExactVector2[prototypeNodes.Length];
            for (int nodeIndex = 0; nodeIndex < prototypeNodes.Length; nodeIndex++)
                worldNodes[nodeIndex] = ToWorld(prototypeNodes[nodeIndex]);
            return worldNodes;
        }

        /// <summary>
        /// 世界座標的頂點環，永遠 CCW。
        /// 翻面會反轉繞向，這裡重新正規化（§6 陷阱：翻轉反轉繞向）。
        /// 副作用：翻面時 Vertices()[0] 不對應 Prototype.Vertices[0]。節點刻意不做這個反轉。
        /// </summary>
        /// <returns>
        /// 新配置的頂點陣列，逆時針。翻面時的對應關係是
        /// <c>Vertices()[i] == ToWorld(Prototype.Vertices[n − 1 − i])</c>。
        /// </returns>
        public ExactVector2[] Vertices()
        {
            ExactVector2[] prototypeVertices = Prototype.Vertices;
            int vertexCount = prototypeVertices.Length;
            var worldVertices = new ExactVector2[vertexCount];

            for (int vertexIndex = 0; vertexIndex < vertexCount; vertexIndex++)
            {
                ExactVector2 worldPoint = ToWorld(prototypeVertices[vertexIndex]);
                int writeIndex = IsFlipped ? vertexCount - 1 - vertexIndex : vertexIndex;
                worldVertices[writeIndex] = worldPoint;
            }
            return worldVertices;
        }

        /// <summary>
        /// 換一個姿態，但讓指定節點停在原地。
        /// 旋轉軸心用目前吸附的節點，結果仍是精確座標（附錄 B）。
        ///
        /// 盒子模式下互動層**不走這支**——旋轉只發生在浮點的暫放狀態（互動設計 §5）。
        /// 出題器與測試仍然會用到。
        /// </summary>
        /// <param name="rotation">新的旋轉值 0~7。</param>
        /// <param name="flipped">新的翻面狀態。</param>
        /// <param name="pivotNodeIndex">要釘住不動的**節點**索引（不是頂點索引）。</param>
        /// <returns>換完姿態、且該節點位置不變的新擺放。</returns>
        public Placement WithPose(int rotation, bool flipped, int pivotNodeIndex)
        {
            ExactVector2 anchorPoint = Node(pivotNodeIndex);
            ExactVector2 localOffset = Symmetry.Apply(rotation, flipped, Prototype.Nodes[pivotNodeIndex]);
            return new Placement(Kind, rotation, flipped, anchorPoint - localOffset);
        }

        /// <summary>套用 16 種整體變換之一（canonical key 用）。</summary>
        /// <param name="globalRotation">整體旋轉值 0~7。</param>
        /// <param name="globalFlipped">整體是否翻面。</param>
        /// <returns>整片連同位置一起被變換之後的新擺放。</returns>
        public Placement ApplyGlobal(int globalRotation, bool globalFlipped)
        {
            var (rotation, flipped) = Symmetry.Compose(globalRotation, globalFlipped, Rotation, IsFlipped);
            return new Placement(Kind, rotation, flipped,
                                 Symmetry.Apply(globalRotation, globalFlipped, Translation));
        }

        /// <summary>
        /// <b>姿態</b>相等，不是<b>形狀</b>相等。正方形 Rotation = 0 與 = 2 佔位一模一樣但回 false。
        /// 要問「是不是同一件事」用 Canonical；要比幾何位置比 Vertices() 的集合。
        /// </summary>
        /// <param name="other">要比較的擺放。</param>
        /// <returns>四個欄位是否全部相同。</returns>
        public bool Equals(Placement other)
            => Kind == other.Kind
            && Rotation == other.Rotation
            && IsFlipped == other.IsFlipped
            && Translation == other.Translation;

        /// <param name="other">任意物件。</param>
        /// <returns>是否為姿態相等的 Placement。</returns>
        public override bool Equals(object other) => other is Placement placement && Equals(placement);

        /// <returns>雜湊值，與 Equals 一致（同樣是姿態語意）。</returns>
        public override int GetHashCode()
            => unchecked((((int)Kind * 397 + Rotation) * 397 + (IsFlipped ? 1 : 0)) * 397
                         ^ Translation.GetHashCode());

        /// <returns>給人看的字串，例如 <c>"SQUARE rotation=1 flipped=False translation=(2√2, 2√2)"</c>。</returns>
        public override string ToString()
            => $"{PieceKindNames.ToStorageString(Kind)} rotation={Rotation} flipped={IsFlipped} translation={Translation}";
    }
}
