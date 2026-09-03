using System;
using System.Collections.Generic;

namespace Tangram.Data
{
    /// <summary>pieces[] 的一項：只有姿態，沒有座標（§1）。</summary>
    [Serializable]
    public struct PiecePose : IEquatable<PiecePose>
    {
        public PieceKind Kind;
        public int Rotation;      // 0..7
        public bool IsFlipped;

        public PiecePose(PieceKind kind, int rotation, bool flipped)
        {
            Kind = kind;
            Rotation = Symmetry.Modulo8(rotation);
            IsFlipped = flipped;
        }

        public bool Equals(PiecePose other)
            => Kind == other.Kind && Rotation == other.Rotation && IsFlipped == other.IsFlipped;

        public override bool Equals(object other) => other is PiecePose pose && Equals(pose);

        public override int GetHashCode()
            => ((int)Kind * 397 + Rotation) * 397 + (IsFlipped ? 1 : 0);

        public override string ToString()
            => $"{PieceKindNames.ToStorageString(Kind)}/{Rotation}/{(IsFlipped ? 'F' : '-')}";
    }

    /// <summary>兩片在某個節點重合：第一片的某節點 = 第二片的某節點。</summary>
    [Serializable]
    public struct Link : IEquatable<Link>
    {
        public int FirstPiece;
        public int FirstNode;
        public int SecondPiece;
        public int SecondNode;

        public Link(int firstPiece, int firstNode, int secondPiece, int secondNode)
        {
            FirstPiece = firstPiece;
            FirstNode = firstNode;
            SecondPiece = secondPiece;
            SecondNode = secondNode;
        }

        public bool Equals(Link other)
            => FirstPiece == other.FirstPiece && FirstNode == other.FirstNode
            && SecondPiece == other.SecondPiece && SecondNode == other.SecondNode;

        public override bool Equals(object other) => other is Link link && Equals(link);

        public override int GetHashCode()
            => ((FirstPiece * 397 + FirstNode) * 397 + SecondPiece) * 397 + SecondNode;

        public override string ToString()
            => $"[{FirstPiece},{FirstNode},{SecondPiece},{SecondNode}]";
    }

    /// <summary>
    /// 一種拼法（解）。相對編碼，不存任何座標（§1）。
    /// 方向已知時剛體只剩 2 個自由度，一個接觸點就固定住，
    /// 所以「每片的旋轉與翻面」加「生成樹上一條連接」就是完整資訊。
    /// links 依放置順序存成有序陣列，BFS 順序天然是合法的擺放順序（§1.4）。
    /// </summary>
    public sealed class Arrangement
    {
        public string Key;              // canonical hash（§1.3）
        public string PuzzleKey;        // 指向 Puzzle
        public PiecePose[] Pieces;      // 7 個
        public Link[] Links;            // 固定 6 條，必須是生成樹

        public Arrangement() { }

        public Arrangement(PiecePose[] pieces, Link[] links, string key = null, string puzzleKey = null)
        {
            Pieces = pieces;
            Links = links;
            Key = key;
            PuzzleKey = puzzleKey;
        }

        /// <summary>
        /// 還原成絕對座標（§1.2）。在 links 上跑 BFS，第 0 片放在原點：
        /// 平移 = 已知片上那個節點的世界座標 − 這片同一個節點的區域座標。
        /// 前置條件：links 必須是生成樹（連通）。
        /// </summary>
        public Placement[] Decode()
        {
            int pieceCount = Pieces.Length;
            var placements = new Placement[pieceCount];
            var isPositioned = new bool[pieceCount];

            // 姿態先就位，位置全部暫放原點
            for (int pieceIndex = 0; pieceIndex < pieceCount; pieceIndex++)
                placements[pieceIndex] = new Placement(
                    Pieces[pieceIndex].Kind,
                    Pieces[pieceIndex].Rotation,
                    Pieces[pieceIndex].IsFlipped,
                    ExactVector2.Zero);

            var adjacency = new List<(int otherPiece, int ownNode, int otherNode)>[pieceCount];
            for (int pieceIndex = 0; pieceIndex < pieceCount; pieceIndex++)
                adjacency[pieceIndex] = new List<(int, int, int)>();

            foreach (Link link in Links)
            {
                if (link.FirstPiece < 0 || link.FirstPiece >= pieceCount
                    || link.SecondPiece < 0 || link.SecondPiece >= pieceCount)
                    throw new InvalidOperationException("link 指向不存在的片：" + link);

                adjacency[link.FirstPiece].Add((link.SecondPiece, link.FirstNode, link.SecondNode));
                adjacency[link.SecondPiece].Add((link.FirstPiece, link.SecondNode, link.FirstNode));
            }

            var pendingPieces = new Queue<int>();
            isPositioned[0] = true;
            pendingPieces.Enqueue(0);
            int positionedCount = 1;

            while (pendingPieces.Count > 0)
            {
                int currentPiece = pendingPieces.Dequeue();
                foreach (var (otherPiece, ownNode, otherNode) in adjacency[currentPiece])
                {
                    if (isPositioned[otherPiece]) continue;

                    ExactVector2 anchorPoint = placements[currentPiece].Node(ownNode);
                    ExactVector2 localOffset = Symmetry.Apply(
                        placements[otherPiece].Rotation,
                        placements[otherPiece].IsFlipped,
                        PiecePrototype.Get(placements[otherPiece].Kind).Nodes[otherNode]);

                    placements[otherPiece] = placements[otherPiece]
                        .WithTranslation(anchorPoint - localOffset);

                    isPositioned[otherPiece] = true;
                    positionedCount++;
                    pendingPieces.Enqueue(otherPiece);
                }
            }

            if (positionedCount != pieceCount)
                throw new InvalidOperationException(
                    $"links 不是生成樹：只連到 {positionedCount}/{pieceCount} 片");

            return placements;
        }

        /// <summary>
        /// 由絕對擺放產生相對編碼：接觸圖上取一棵生成樹（BFS 順序 = 放置順序）。
        /// </summary>
        public static Arrangement FromPlacements(IReadOnlyList<Placement> pieces)
        {
            int pieceCount = pieces.Count;
            var poses = new PiecePose[pieceCount];
            var worldNodes = new ExactVector2[pieceCount][];

            for (int pieceIndex = 0; pieceIndex < pieceCount; pieceIndex++)
            {
                poses[pieceIndex] = new PiecePose(
                    pieces[pieceIndex].Kind,
                    pieces[pieceIndex].Rotation,
                    pieces[pieceIndex].IsFlipped);
                worldNodes[pieceIndex] = pieces[pieceIndex].Nodes();
            }

            // 節點座標 → 佔用它的 (片, 節點索引)。key 是精確座標，所以「重合」是字典查找
            var occupantsByPosition = new Dictionary<ExactVector2, List<(int piece, int node)>>();
            for (int pieceIndex = 0; pieceIndex < pieceCount; pieceIndex++)
            {
                for (int nodeIndex = 0; nodeIndex < worldNodes[pieceIndex].Length; nodeIndex++)
                {
                    ExactVector2 position = worldNodes[pieceIndex][nodeIndex];
                    if (!occupantsByPosition.TryGetValue(position, out var occupants))
                        occupantsByPosition[position] = occupants = new List<(int, int)>();
                    occupants.Add((pieceIndex, nodeIndex));
                }
            }

            var links = new List<Link>();
            var isReached = new bool[pieceCount];
            var pendingPieces = new Queue<int>();
            isReached[0] = true;
            pendingPieces.Enqueue(0);

            while (pendingPieces.Count > 0)
            {
                int currentPiece = pendingPieces.Dequeue();
                for (int nodeIndex = 0; nodeIndex < worldNodes[currentPiece].Length; nodeIndex++)
                {
                    ExactVector2 position = worldNodes[currentPiece][nodeIndex];
                    if (!occupantsByPosition.TryGetValue(position, out var occupants)) continue;

                    foreach (var (otherPiece, otherNode) in occupants)
                    {
                        if (otherPiece == currentPiece || isReached[otherPiece]) continue;
                        links.Add(new Link(currentPiece, nodeIndex, otherPiece, otherNode));
                        isReached[otherPiece] = true;
                        pendingPieces.Enqueue(otherPiece);
                    }
                }
            }

            for (int pieceIndex = 0; pieceIndex < pieceCount; pieceIndex++)
                if (!isReached[pieceIndex])
                    throw new InvalidOperationException($"第 {pieceIndex} 片沒有與其他片接觸，無法建生成樹");

            return new Arrangement(poses, links.ToArray());
        }

        /// <summary>重算並填入 Key / PuzzleKey（座標與輪廓都不入庫，需要時重算，§3）。</summary>
        public Arrangement WithKeys()
        {
            Placement[] placements = Decode();
            Key = Canonical.ArrangementKey(placements);
            PuzzleKey = Canonical.PuzzleKey(placements);
            return this;
        }
    }
}
