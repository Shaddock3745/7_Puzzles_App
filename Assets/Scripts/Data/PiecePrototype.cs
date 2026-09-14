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
        /// 從 <paramref name="rotation"/> 轉 <paramref name="steps"/> 格之後落在哪一格，<b>保證落在 0 ～ 循環長度−1</b>。
        /// 正數是逆時針（模型的旋轉方向），負數是順時針（2026-09-13 加了順時針鈕）。
        ///
        /// ⚠️ <b>負數要自己繞回來</b>：C# 的 <c>%</c> 對負數回負數（−1 % 8 = −1），直接拿去當旋轉值的話，
        /// <c>Placement</c> 的建構子會把它正規化成 7，但 <c>PieceView</c> 那邊存的還是 −1——
        /// 同一個外觀兩個編號，undo 與解的比對會踩到（跟「照循環長度取餘數，不是固定 8」同一個理由）。
        /// ⚠️ 也不能用 <see cref="Symmetry.Modulo8"/>：正方形循環是 2、平行四邊形是 4。
        /// </summary>
        /// <param name="kind">片種，決定循環長度。</param>
        /// <param name="rotation">目前的旋轉格數。</param>
        /// <param name="steps">要轉幾格，可以是負的。</param>
        /// <returns>轉完之後的旋轉格數。</returns>
        public static int RotateWithinCycle(PieceKind kind, int rotation, int steps)
        {
            int cycleLength = RotationCycleLength(kind);
            return ((rotation + steps) % cycleLength + cycleLength) % cycleLength;
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

        /// <summary>
        /// 這個姿態<b>在畫面上左右鏡射</b>之後，落在哪個姿態（2026-09-14，翻轉鈕對所有片開放）。
        ///
        /// <b>為什麼要有</b>：翻轉鈕原本只對平行四邊形顯示，給人玩過之後「少一顆感覺怪怪的」。
        /// 所以翻轉鈕改成對每一片都做「照現在的樣子左右鏡射」——三角形的鏡像等於某個旋轉，那就轉過去。
        ///
        /// <b>做法不分片種</b>：把現在的頂點左右鏡射（x 取負），然後在<b>這種片本來就能擺的姿態</b>裡
        /// 找形狀一樣的那一個（位置不算）。能擺的姿態：旋轉 0 ～ 循環長度−1，翻面只有 <see cref="CanFlip"/> 的片才有。
        /// 兩個結果不用另外寫：
        /// <list type="bullet">
        /// <item><b>三角形與正方形永遠不會出現翻面</b>——搜尋範圍裡本來就沒有。⚠️ 出現的話 <c>Placement.Equals</c> 那類比較會安靜地對不上</item>
        /// <item><b>答案只有一個</b>——循環長度已經把「同一個樣子兩個編號」排除掉了</item>
        /// </list>
        ///
        /// ⭐ <b>平行四邊形的行為跟以前一模一樣</b>。以前的翻面鈕是「翻面 ＋ 旋轉取負」，
        /// 那在畫面上是<b>上下</b>鏡射（推導見舊版 <c>TouchInputController.MirroredRotation</c> 的說明：
        /// 舊姿態 R(θ)、新姿態 R(θ')·M，兩者差 R(θ'+θ)·M，θ' = −θ 時就是對 x 軸鏡射）。
        /// 而平行四邊形轉 180° 長一樣，所以上下鏡射與左右鏡射是同一個形狀——測試釘住。
        ///
        /// <b>鏡射完跟現在一樣的情況</b>：正方形永遠是、每種三角形 8 格裡有 2 格（對稱軸剛好直立）。
        /// 翻轉鈕在那些時候要變灰，判斷就是「回傳值等於現在的姿態」（<see cref="IsMirrorSymmetric"/>）。
        ///
        /// ⚠️ <b>查表</b>：按鈕每幀都要問「能不能按」，每次現算要生八個 <c>Placement</c> 與雜湊集合（手機上是每幀的 GC）。
        /// 5 種片 × 2 × 8 格，第一次呼叫時算好。
        /// </summary>
        /// <param name="kind">片種。</param>
        /// <param name="rotation">目前的旋轉格數（會照循環長度正規化）。</param>
        /// <param name="isFlipped">目前有沒有翻面。</param>
        /// <returns>鏡射之後的姿態，旋轉保證落在 0 ～ 循環長度−1。</returns>
        public static PiecePose MirroredLeftRight(PieceKind kind, int rotation, bool isFlipped)
        {
            if (mirrorTable == null) mirrorTable = BuildMirrorTable();
            return mirrorTable[(int)kind, isFlipped ? 1 : 0, RotateWithinCycle(kind, rotation, 0)];
        }

        /// <summary>這個姿態左右鏡射之後是不是跟現在一樣（翻轉鈕變灰的條件）。</summary>
        /// <param name="kind">片種。</param>
        /// <param name="rotation">目前的旋轉格數。</param>
        /// <param name="isFlipped">目前有沒有翻面。</param>
        /// <returns>一樣的話 true——按了不會有任何變化。</returns>
        public static bool IsMirrorSymmetric(PieceKind kind, int rotation, bool isFlipped) =>
            MirroredLeftRight(kind, rotation, isFlipped).Equals(new PiecePose(kind, RotateWithinCycle(kind, rotation, 0), isFlipped));

        /// <summary>
        /// [片種, 翻面, 旋轉] → 左右鏡射之後的姿態。第一次用到時才建——
        /// ⚠️ 不能寫成靜態欄位的初始值：建表要用 <see cref="Prototypes"/>，而靜態欄位照文字順序初始化，排在它前面就是 null。
        /// </summary>
        private static PiecePose[,,] mirrorTable;

        /// <returns>建好的鏡射表。</returns>
        /// <exception cref="InvalidOperationException">某個姿態的鏡像在能擺的姿態裡找不到（原型或循環長度寫錯才會發生）。</exception>
        private static PiecePose[,,] BuildMirrorTable()
        {
            var table = new PiecePose[5, 2, 8];
            foreach (PieceKind kind in (PieceKind[])Enum.GetValues(typeof(PieceKind)))
            {
                int cycleLength = RotationCycleLength(kind);
                for (int flipIndex = 0; flipIndex < 2; flipIndex++)
                    for (int rotation = 0; rotation < cycleLength; rotation++)
                    {
                        ExactVector2[] vertices = new Placement(kind, rotation, flipIndex == 1, ExactVector2.Zero).Vertices();
                        var mirrored = new ExactVector2[vertices.Length];
                        for (int i = 0; i < vertices.Length; i++) mirrored[i] = new ExactVector2(-vertices[i].X, vertices[i].Y);

                        table[(int)kind, flipIndex, rotation] = FindReachablePose(kind, mirrored);
                    }
            }
            return table;
        }

        /// <summary>在這種片能擺的姿態裡，找形狀跟 <paramref name="vertices"/> 一樣的那一個（位置不算）。</summary>
        /// <param name="kind">片種。</param>
        /// <param name="vertices">要找的形狀。</param>
        /// <returns>那個姿態。</returns>
        /// <exception cref="InvalidOperationException">找不到。</exception>
        private static PiecePose FindReachablePose(PieceKind kind, ExactVector2[] vertices)
        {
            int flipCount = CanFlip(kind) ? 2 : 1;
            for (int flipIndex = 0; flipIndex < flipCount; flipIndex++)
                for (int rotation = 0; rotation < RotationCycleLength(kind); rotation++)
                {
                    ExactVector2[] candidate = new Placement(kind, rotation, flipIndex == 1, ExactVector2.Zero).Vertices();
                    if (Geometry.SameVerticesIgnoringPosition(candidate, vertices))
                        return new PiecePose(kind, rotation, flipIndex == 1);
                }
            throw new InvalidOperationException($"{kind} 的鏡像在能擺的姿態裡找不到——原型頂點或循環長度寫錯了？");
        }

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
