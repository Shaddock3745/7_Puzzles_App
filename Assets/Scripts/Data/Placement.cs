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

        public Placement(PieceKind kind, int rotation, bool flipped, ExactVector2 translation)
        {
            Kind = kind;
            Rotation = Symmetry.Modulo8(rotation);
            IsFlipped = flipped;
            Translation = translation;
        }

        public PiecePrototype Prototype => PiecePrototype.Get(Kind);
        public int NodeCount => Prototype.NodeCount;

        public Placement WithTranslation(ExactVector2 translation)
            => new Placement(Kind, Rotation, IsFlipped, translation);

        public Placement Translated(ExactVector2 delta)
            => new Placement(Kind, Rotation, IsFlipped, Translation + delta);

        /// <summary>原型座標 → 世界座標。順序：翻面 → 旋轉 → 平移。</summary>
        public ExactVector2 ToWorld(ExactVector2 prototypePoint)
            => Translation + Symmetry.Apply(Rotation, IsFlipped, prototypePoint);

        /// <summary>指定節點的世界座標。索引即 links 裡的節點索引。</summary>
        public ExactVector2 Node(int nodeIndex) => ToWorld(Prototype.Nodes[nodeIndex]);

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
        /// </summary>
        public Placement WithPose(int rotation, bool flipped, int pivotNodeIndex)
        {
            ExactVector2 anchorPoint = Node(pivotNodeIndex);
            ExactVector2 localOffset = Symmetry.Apply(rotation, flipped, Prototype.Nodes[pivotNodeIndex]);
            return new Placement(Kind, rotation, flipped, anchorPoint - localOffset);
        }

        /// <summary>套用 16 種整體變換之一（canonical key 用）。</summary>
        public Placement ApplyGlobal(int globalRotation, bool globalFlipped)
        {
            var (rotation, flipped) = Symmetry.Compose(globalRotation, globalFlipped, Rotation, IsFlipped);
            return new Placement(Kind, rotation, flipped,
                                 Symmetry.Apply(globalRotation, globalFlipped, Translation));
        }

        public bool Equals(Placement other)
            => Kind == other.Kind
            && Rotation == other.Rotation
            && IsFlipped == other.IsFlipped
            && Translation == other.Translation;

        public override bool Equals(object other) => other is Placement placement && Equals(placement);

        public override int GetHashCode()
            => unchecked((((int)Kind * 397 + Rotation) * 397 + (IsFlipped ? 1 : 0)) * 397
                         ^ Translation.GetHashCode());

        public override string ToString()
            => $"{PieceKindNames.ToStorageString(Kind)} rotation={Rotation} flipped={IsFlipped} translation={Translation}";
    }
}
