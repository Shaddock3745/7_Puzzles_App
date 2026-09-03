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

        public Edge(int direction, Scalar length)
        {
            Direction = Symmetry.Modulo8(direction);
            Length = length;
        }

        public ExactVector2 Step() => Symmetry.Step(Direction, Length);

        public bool Equals(Edge other) => Direction == other.Direction && Length.Equals(other.Length);
        public override bool Equals(object other) => other is Edge edge && Equals(edge);
        public override int GetHashCode() => Direction * 397 ^ Length.GetHashCode();
        public string ToKey() => Direction.ToString() + "," + Length.ToKey();
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

        public Walk() { }
        public Walk(ExactVector2 anchor, Edge[] edges) { Anchor = anchor; Edges = edges; }

        /// <summary>沿邊走一圈算出所有頂點。走不回起點會丟例外，所以它也是封閉性檢查。</summary>
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
        public Scalar SignedDoubleArea() => Geometry.SignedDoubleArea(Vertices());

        public bool IsOuter => SignedDoubleArea().Sign > 0;

        public Walk Translated(ExactVector2 delta) => new Walk(Anchor + delta, (Edge[])Edges.Clone());

        /// <summary>把起點旋轉到指定頂點（邊序列同步旋轉）。</summary>
        public Walk RotatedTo(int startIndex)
        {
            int edgeCount = Edges.Length;
            ExactVector2[] vertices = Vertices();
            var rotatedEdges = new Edge[edgeCount];
            for (int offset = 0; offset < edgeCount; offset++)
                rotatedEdges[offset] = Edges[(startIndex + offset) % edgeCount];
            return new Walk(vertices[startIndex % edgeCount], rotatedEdges);
        }

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

        public Puzzle() { }

        public Puzzle(string key, Walk[] walks, PuzzleSource source)
        {
            Key = key;
            Walks = walks;
            Source = source;
        }

        public static Puzzle FromPlacements(IReadOnlyList<Placement> pieces,
                                            PuzzleSource source = PuzzleSource.Generated)
        {
            List<Walk> walks = BoundaryTracer.Trace(pieces);
            return new Puzzle(Canonical.PuzzleKey(pieces), walks.ToArray(), source);
        }

        /// <summary>所有環的有向面積總和 ×2（洞是負的）。七片時應為 64（面積 32）。</summary>
        public Scalar TotalSignedDoubleArea()
        {
            Scalar accumulated = Scalar.Zero;
            foreach (Walk walk in Walks) accumulated = accumulated + walk.SignedDoubleArea();
            return accumulated;
        }

        public IEnumerable<Walk> OuterWalks()
        {
            foreach (Walk walk in Walks) if (walk.IsOuter) yield return walk;
        }

        public IEnumerable<Walk> HoleWalks()
        {
            foreach (Walk walk in Walks) if (!walk.IsOuter) yield return walk;
        }
    }
}
