using System;
using System.Collections.Generic;

namespace Tangram.Data
{
    public enum PieceKind
    {
        LargeTriangle = 0,
        MediumTriangle = 1,
        SmallTriangle = 2,
        Square = 3,
        Parallelogram = 4
    }

    /// <summary>片種與儲存字串的互轉。字串格式由儲存規格 §1 定義，不要自行更動。</summary>
    public static class PieceKindNames
    {
        /// <param name="kind">片種。</param>
        /// <returns>存檔用的字串，例如 <c>"LARGE_TRIANGLE"</c>。</returns>
        public static string ToStorageString(PieceKind kind)
        {
            switch (kind)
            {
                case PieceKind.LargeTriangle: return "LARGE_TRIANGLE";
                case PieceKind.MediumTriangle: return "MEDIUM_TRIANGLE";
                case PieceKind.SmallTriangle: return "SMALL_TRIANGLE";
                case PieceKind.Square: return "SQUARE";
                default: return "PARALLELOGRAM";
            }
        }

        /// <param name="text">存檔用的字串。</param>
        /// <returns>對應的片種。</returns>
        /// <exception cref="ArgumentException">字串不是五種片種之一。</exception>
        public static PieceKind FromStorageString(string text)
        {
            switch (text)
            {
                case "LARGE_TRIANGLE": return PieceKind.LargeTriangle;
                case "MEDIUM_TRIANGLE": return PieceKind.MediumTriangle;
                case "SMALL_TRIANGLE": return PieceKind.SmallTriangle;
                case "SQUARE": return PieceKind.Square;
                case "PARALLELOGRAM": return PieceKind.Parallelogram;
                default: throw new ArgumentException("未知的片種 " + text);
            }
        }
    }

    /// <summary>
    /// 片的原型（§0）。單位：小三角的股 = 2。
    /// 節點 = 頂點 + 邊上細分點，合法偏移集合 { 2a + 2b√2 | a,b ∈ ℕ } 且 ≤ 邊長。
    /// 節點索引 = 沿 CCW 邊界走訪的順序，這個順序就是 links 裡的節點索引，不可更動。
    /// </summary>
    public sealed class PiecePrototype
    {
        public readonly PieceKind Kind;
        public readonly ExactVector2[] Vertices;       // CCW
        public readonly ExactVector2[] Nodes;          // 邊界走訪順序
        public readonly int[] NodeDirections;          // 該節點所在邊的方向（頂點取其後繼邊）
        public readonly Scalar DoubleArea;             // shoelace，面積 ×2

        /// <param name="kind">片種。</param>
        /// <param name="vertices">原型頂點環，必須是逆時針。</param>
        /// <exception cref="InvalidOperationException">頂點環是順時針，或有邊不在 45° 網格上。</exception>
        private PiecePrototype(PieceKind kind, ExactVector2[] vertices)
        {
            Kind = kind;
            Vertices = vertices;
            DoubleArea = Geometry.SignedDoubleArea(vertices);
            if (DoubleArea.Sign <= 0)
                throw new InvalidOperationException($"原型 {kind} 必須是 CCW");
            BuildNodes(vertices, out Nodes, out NodeDirections);
        }

        /// <summary>節點數。小三角 4、正方形 4、平行四邊形 6、中三角 7、大三角 11。</summary>
        public int NodeCount => Nodes.Length;

        /// <summary>邊數（等於頂點數）。</summary>
        public int EdgeCount => Vertices.Length;

        /// <summary>沿頂點環走一圈，每條邊撒出合法偏移的節點。</summary>
        /// <param name="vertices">原型頂點環。</param>
        /// <param name="nodes">產生的節點，順序就是節點索引。</param>
        /// <param name="nodeDirections">與 nodes 同索引，該節點所在邊的方向。</param>
        /// <exception cref="InvalidOperationException">有邊不在 45° 網格上。</exception>
        private static void BuildNodes(ExactVector2[] vertices,
                                       out ExactVector2[] nodes,
                                       out int[] nodeDirections)
        {
            var nodePoints = new List<ExactVector2>();
            var edgeDirections = new List<int>();

            for (int vertexIndex = 0; vertexIndex < vertices.Length; vertexIndex++)
            {
                ExactVector2 edgeStart = vertices[vertexIndex];
                ExactVector2 edgeEnd = vertices[(vertexIndex + 1) % vertices.Length];

                if (!Symmetry.TryDecompose(edgeEnd - edgeStart, out int direction, out Scalar edgeLength))
                    throw new InvalidOperationException("原型的邊不在 45° 網格上");

                // 含偏移 0、不含偏移 = 邊長（那個點是下一條邊的偏移 0）
                foreach (Scalar offset in LegalOffsets(edgeLength))
                {
                    nodePoints.Add(edgeStart + Symmetry.Step(direction, offset));
                    edgeDirections.Add(direction);
                }
            }

            nodes = nodePoints.ToArray();
            nodeDirections = edgeDirections.ToArray();
        }

        /// <summary>{ 2a + 2b√2 | a,b ∈ ℕ } ∩ [0, 邊長)，由小到大。</summary>
        /// <param name="edgeLength">邊長。</param>
        /// <returns>
        /// 該邊上所有合法偏移，由小到大排序。**含 0、不含邊長本身**
        /// ——邊長那一點是下一條邊的偏移 0，這樣每個頂點只會被放進去一次。
        /// </returns>
        public static List<Scalar> LegalOffsets(Scalar edgeLength)
        {
            var offsets = new List<Scalar>();
            for (long integerSteps = 0; Scalar.FromInteger(2 * integerSteps) < edgeLength; integerSteps++)
            {
                for (long root2Steps = 0; ; root2Steps++)
                {
                    var offset = new Scalar(2 * integerSteps, 2 * root2Steps);
                    if (offset >= edgeLength) break;
                    offsets.Add(offset);
                }
            }
            offsets.Sort((left, right) => left.CompareTo(right));
            return offsets;
        }

        private static readonly PiecePrototype[] Prototypes = BuildAllPrototypes();

        /// <summary>回傳的是共用單例，裡面的陣列絕對不要改，否則會污染全域。</summary>
        /// <param name="kind">片種。</param>
        /// <returns>該片種的原型單例。</returns>
        public static PiecePrototype Get(PieceKind kind) => Prototypes[(int)kind];

        /// <summary>
        /// 一片實際有幾個**幾何上不同**的旋轉姿態。
        ///
        /// 片自己有對稱性，八個角度不是每個都不同：正方形轉 90° 回到自己、
        /// 平行四邊形轉 180° 回到自己。
        ///
        /// ⚠️ <b>這是幾何事實，不是 UI 規則</b>，所以住在原型這裡。兩個用途：
        /// 旋轉鈕照它循環（不然玩家會看到「按了沒反應」，互動設計 §5），
        /// 出題器照它枚舉（不然正方形與平行四邊形會產生佔位完全相同的重複候選，
        /// 隨機挑的時候等於給了它們額外的權重）。
        /// </summary>
        /// <param name="kind">片種。</param>
        /// <returns>三角形 8、正方形 2、平行四邊形 4。</returns>
        public static int RotationCycleLength(PieceKind kind)
        {
            switch (kind)
            {
                case PieceKind.Square: return 2;          // 轉 90° 回到自己
                case PieceKind.Parallelogram: return 4;   // 轉 180° 回到自己
                default: return 8;
            }
        }

        /// <summary>
        /// 翻面對這一片有沒有意義。
        ///
        /// ⚠️ 同樣是**幾何事實**：三角形與正方形鏡射之後等同於某個旋轉，
        /// 只有平行四邊形的鏡像是轉不出來的。所以翻面只對它開放。
        /// </summary>
        /// <param name="kind">片種。</param>
        /// <returns>只有平行四邊形回 true。</returns>
        public static bool CanFlip(PieceKind kind) => kind == PieceKind.Parallelogram;

        /// <returns>五種原型，索引就是 <see cref="PieceKind"/> 的整數值。</returns>
        private static PiecePrototype[] BuildAllPrototypes()
        {
            var prototypes = new PiecePrototype[5];

            // 大三角：股 4、斜邊 4√2、面積 8、11 個節點
            prototypes[(int)PieceKind.LargeTriangle] = new PiecePrototype(PieceKind.LargeTriangle, new[]
            {
                new ExactVector2(0, 0), new ExactVector2(4, 0), new ExactVector2(0, 4)
            });

            // 中三角：股 2√2、斜邊 4、面積 4、7 個節點
            prototypes[(int)PieceKind.MediumTriangle] = new PiecePrototype(PieceKind.MediumTriangle, new[]
            {
                new ExactVector2(0, 0), new ExactVector2(2, 2), new ExactVector2(-2, 2)
            });

            // 小三角：股 2、斜邊 2√2、面積 2、4 個節點
            prototypes[(int)PieceKind.SmallTriangle] = new PiecePrototype(PieceKind.SmallTriangle, new[]
            {
                new ExactVector2(0, 0), new ExactVector2(2, 0), new ExactVector2(0, 2)
            });

            // 正方形：邊 2、面積 4、4 個節點
            prototypes[(int)PieceKind.Square] = new PiecePrototype(PieceKind.Square, new[]
            {
                new ExactVector2(0, 0), new ExactVector2(2, 0), new ExactVector2(2, 2), new ExactVector2(0, 2)
            });

            // 平行四邊形：2 與 2√2、面積 4、6 個節點
            prototypes[(int)PieceKind.Parallelogram] = new PiecePrototype(PieceKind.Parallelogram, new[]
            {
                new ExactVector2(0, 0), new ExactVector2(2, 0), new ExactVector2(4, 2), new ExactVector2(2, 2)
            });

            return prototypes;
        }

        /// <summary>
        /// 一副七巧板的七片（大三角 ×2、小三角 ×2、其餘各 1）。
        /// 這個順序就是 <c>BoardState</c> 七個槽的固定對應。
        /// </summary>
        public static readonly PieceKind[] TangramSet =
        {
            PieceKind.LargeTriangle, PieceKind.LargeTriangle,
            PieceKind.MediumTriangle,
            PieceKind.SmallTriangle, PieceKind.SmallTriangle,
            PieceKind.Square,
            PieceKind.Parallelogram
        };
    }
}
