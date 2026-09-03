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

        private PiecePrototype(PieceKind kind, ExactVector2[] vertices)
        {
            Kind = kind;
            Vertices = vertices;
            DoubleArea = Geometry.SignedDoubleArea(vertices);
            if (DoubleArea.Sign <= 0)
                throw new InvalidOperationException($"原型 {kind} 必須是 CCW");
            BuildNodes(vertices, out Nodes, out NodeDirections);
        }

        public int NodeCount => Nodes.Length;
        public int EdgeCount => Vertices.Length;

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
        public static PiecePrototype Get(PieceKind kind) => Prototypes[(int)kind];

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

        /// <summary>一副七巧板的七片（大三角 ×2、小三角 ×2、其餘各 1）。</summary>
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
