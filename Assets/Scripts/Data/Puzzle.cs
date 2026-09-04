using System;
using System.Collections.Generic;
using System.Text;

namespace Tangram.Data
{
    /// <summary>邊序列的一段（§2.2）。方向為 45° 的倍數。</summary>
    [Serializable]
    public struct Edge : IEquatable<Edge>
    {
        public int Direction;      // 0..7
        public Scalar Length;

        /// <param name="direction">0~7 的方向，會用 Modulo8 正規化。</param>
        /// <param name="length">邊長。</param>
        public Edge(int direction, Scalar length)
        {
            Direction = Symmetry.Modulo8(direction);
            Length = length;
        }

        /// <returns>沿這條邊走完的位移向量。</returns>
        public ExactVector2 Step() => Symmetry.Step(Direction, Length);

        /// <param name="other">要比較的邊。</param>
        /// <returns>方向與長度是否都相同。</returns>
        public bool Equals(Edge other) => Direction == other.Direction && Length.Equals(other.Length);

        /// <param name="other">任意物件。</param>
        /// <returns>是否為相等的 Edge。</returns>
        public override bool Equals(object other) => other is Edge edge && Equals(edge);

        /// <returns>雜湊值。</returns>
        public override int GetHashCode() => Direction * 397 ^ Length.GetHashCode();

        /// <returns>序列化用的穩定字串，形如 <c>"3,0|2"</c>。</returns>
        public string ToKey() => Direction.ToString() + "," + Length.ToKey();

        /// <returns>給人看的字串，例如 <c>"3×45° length=2√2"</c>。</returns>
        public override string ToString() => $"{Direction}×45° length={Length}";
    }

    /// <summary>
    /// 一條封閉環。用邊序列而非頂點清單：平移自動消掉，旋轉是方向全部加同一個值，
    /// 鏡射是反轉序列加方向取負（§2.2）。
    /// Anchor 必須保留 —— 兩條環在哪個點捏在一起，只能靠它加邊序列還原。
    /// </summary>
    public sealed class Walk
    {
        public ExactVector2 Anchor;
        public Edge[] Edges;

        /// <summary>給序列化用的空建構式。</summary>
        public Walk() { }

        /// <param name="anchor">環的起點。</param>
        /// <param name="edges">從起點開始的邊序列，走完要回到起點。</param>
        public Walk(ExactVector2 anchor, Edge[] edges) { Anchor = anchor; Edges = edges; }

        /// <summary>沿邊走一圈算出所有頂點。走不回起點會丟例外，所以它也是封閉性檢查。</summary>
        /// <returns>與 Edges 同索引的頂點，第 i 個是第 i 條邊的起點。起點不重複放在結尾。</returns>
        /// <exception cref="InvalidOperationException">邊序列走不回 Anchor，環沒有封閉。</exception>
        public ExactVector2[] Vertices()
        {
            var vertices = new ExactVector2[Edges.Length];
            ExactVector2 currentPoint = Anchor;
            for (int edgeIndex = 0; edgeIndex < Edges.Length; edgeIndex++)
            {
                vertices[edgeIndex] = currentPoint;
                currentPoint = currentPoint + Edges[edgeIndex].Step();
            }
            if (currentPoint != Anchor) throw new InvalidOperationException("環沒有封閉");
            return vertices;
        }

        /// <summary>正 = 外環（CCW），負 = 洞（CW）。不存旗標，用 shoelace 判斷（§2.1）。</summary>
        /// <returns>有向面積的兩倍。</returns>
        public Scalar SignedDoubleArea() => Geometry.SignedDoubleArea(Vertices());

        /// <summary>是外環還是洞。每次讀都重新走一遍頂點，不要放在迴圈裡。</summary>
        public bool IsOuter => SignedDoubleArea().Sign > 0;

        /// <param name="delta">位移量。</param>
        /// <returns>平移後的新環，邊序列複製一份（原本的不會被動到）。</returns>
        public Walk Translated(ExactVector2 delta) => new Walk(Anchor + delta, (Edge[])Edges.Clone());

        /// <summary>把起點旋轉到指定頂點（邊序列同步旋轉）。</summary>
        /// <param name="startIndex">要當新起點的頂點索引，超出範圍會取模。</param>
        /// <returns>幾何完全相同、只是換了起點的新環。</returns>
        public Walk RotatedTo(int startIndex)
        {
            int edgeCount = Edges.Length;
            ExactVector2[] vertices = Vertices();
            var rotatedEdges = new Edge[edgeCount];
            for (int offset = 0; offset < edgeCount; offset++)
                rotatedEdges[offset] = Edges[(startIndex + offset) % edgeCount];
            return new Walk(vertices[startIndex % edgeCount], rotatedEdges);
        }

        /// <returns>邊序列的字串，不含 anchor。用空白隔開。</returns>
        public string EdgesKey()
        {
            var builder = new StringBuilder();
            for (int edgeIndex = 0; edgeIndex < Edges.Length; edgeIndex++)
            {
                if (edgeIndex > 0) builder.Append(' ');
                builder.Append(Edges[edgeIndex].ToKey());
            }
            return builder.ToString();
        }

        /// <returns>anchor 加邊序列的完整字串，canonical key 的一部分。</returns>
        public string ToKey() => Anchor.ToKey() + "@" + EdgesKey();
    }

    public enum PuzzleSource { Generated, UserDrawn }

    /// <summary>
    /// 題目（形狀）。與 Arrangement 是 1:N。
    /// key 從七片聯集算出來，不是人工指定（§2）。
    /// 外環與洞環都在 Walks 裡，用 shoelace 有向面積分辨。
    /// </summary>
    public sealed class Puzzle
    {
        public string Key;
        public Walk[] Walks;
        public PuzzleSource Source;

        [NonSerialized] private Region region;

        /// <summary>
        /// 題目自身座標系的區域（不含盒子原點的平移），第一次存取時建好並快取。
        /// 盤面上那個盒子要用 Region.FromPuzzle(puzzle, boxOrigin)，不是這個。
        /// </summary>
        public Region Region
        {
            get
            {
                if (region == null) region = Tangram.Data.Region.FromWalks(Walks, ExactVector2.Zero);
                return region;
            }
        }

        /// <summary>給序列化用的空建構式。</summary>
        public Puzzle() { }

        /// <param name="key">canonical hash。</param>
        /// <param name="walks">外環與洞環，順序不拘。</param>
        /// <param name="source">題目來源：演算法生成或使用者手繪。</param>
        public Puzzle(string key, Walk[] walks, PuzzleSource source)
        {
            Key = key;
            Walks = walks;
            Source = source;
        }

        /// <param name="pieces">七片的絕對擺放。</param>
        /// <param name="source">題目來源。</param>
        /// <returns>七片聯集的外框與 canonical key。</returns>
        /// <remarks>
        /// <b>會跑 17 次輪廓追蹤</b>（1 次拿 walks、16 次算 key）。出題時算一次就存起來。
        /// </remarks>
        public static Puzzle FromPlacements(IReadOnlyList<Placement> pieces,
                                            PuzzleSource source = PuzzleSource.Generated)
        {
            List<Walk> walks = BoundaryTracer.Trace(pieces);
            return new Puzzle(Canonical.PuzzleKey(pieces), walks.ToArray(), source);
        }

        /// <summary>所有環的有向面積總和 ×2（洞是負的）。七片時應為 64（面積 32）。</summary>
        /// <returns>有向面積總和的兩倍。</returns>
        public Scalar TotalSignedDoubleArea()
        {
            Scalar accumulated = Scalar.Zero;
            foreach (Walk walk in Walks) accumulated = accumulated + walk.SignedDoubleArea();
            return accumulated;
        }

        /// <returns>逐一列出外環。是延遲求值的迭代器，每次列舉都會重算 IsOuter。</returns>
        public IEnumerable<Walk> OuterWalks()
        {
            foreach (Walk walk in Walks) if (walk.IsOuter) yield return walk;
        }

        /// <returns>逐一列出洞環。是延遲求值的迭代器，每次列舉都會重算 IsOuter。</returns>
        public IEnumerable<Walk> HoleWalks()
        {
            foreach (Walk walk in Walks) if (!walk.IsOuter) yield return walk;
        }
    }
}
