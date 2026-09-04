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

        /// <param name="kind">片種。</param>
        /// <param name="rotation">旋轉值，會用 Modulo8 正規化。</param>
        /// <param name="flipped">是否翻面。</param>
        public PiecePose(PieceKind kind, int rotation, bool flipped)
        {
            Kind = kind;
            Rotation = Symmetry.Modulo8(rotation);
            IsFlipped = flipped;
        }

        /// <param name="other">要比較的姿態。</param>
        /// <returns>三個欄位是否全部相同。</returns>
        public bool Equals(PiecePose other)
            => Kind == other.Kind && Rotation == other.Rotation && IsFlipped == other.IsFlipped;

        /// <param name="other">任意物件。</param>
        /// <returns>是否為相等的 PiecePose。</returns>
        public override bool Equals(object other) => other is PiecePose pose && Equals(pose);

        /// <returns>雜湊值。</returns>
        public override int GetHashCode()
            => ((int)Kind * 397 + Rotation) * 397 + (IsFlipped ? 1 : 0);

        /// <returns>給人看的字串，例如 <c>"SQUARE/1/-"</c>。</returns>
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

        /// <param name="firstPiece">第一片在 pieces[] 裡的索引。</param>
        /// <param name="firstNode">第一片的節點索引。</param>
        /// <param name="secondPiece">第二片在 pieces[] 裡的索引。</param>
        /// <param name="secondNode">第二片的節點索引。</param>
        public Link(int firstPiece, int firstNode, int secondPiece, int secondNode)
        {
            FirstPiece = firstPiece;
            FirstNode = firstNode;
            SecondPiece = secondPiece;
            SecondNode = secondNode;
        }

        /// <param name="other">要比較的連接。</param>
        /// <returns>四個欄位是否全部相同。注意兩端對調算不同。</returns>
        public bool Equals(Link other)
            => FirstPiece == other.FirstPiece && FirstNode == other.FirstNode
            && SecondPiece == other.SecondPiece && SecondNode == other.SecondNode;

        /// <param name="other">任意物件。</param>
        /// <returns>是否為相等的 Link。</returns>
        public override bool Equals(object other) => other is Link link && Equals(link);

        /// <returns>雜湊值。</returns>
        public override int GetHashCode()
            => ((FirstPiece * 397 + FirstNode) * 397 + SecondPiece) * 397 + SecondNode;

        /// <returns>給人看的字串，例如 <c>"[0,3,2,5]"</c>。</returns>
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

        /// <summary>給序列化用的空建構式。</summary>
        public Arrangement() { }

        /// <param name="pieces">七片的姿態。</param>
        /// <param name="links">六條連接，必須構成生成樹。</param>
        /// <param name="key">排列的 canonical hash，可以之後再用 WithKeys 補。</param>
        /// <param name="puzzleKey">題目的 canonical hash，可以之後再用 WithKeys 補。</param>
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
        /// <returns>
        /// 與 <c>Pieces</c> 同索引的絕對擺放，第 0 片的平移固定是原點。
        /// 整個結果只確定到一個整體平移——要比對時記得先對齊。
        /// </returns>
        /// <exception cref="InvalidOperationException">
        /// link 指向不存在的片，或 links 不連通（走不到全部七片）。
        /// 不會檢查重疊或合法性，那是 <see cref="Validation"/> 的事；
        /// 多餘的 link 會被靜默忽略，不檢查是否自相矛盾。
        /// </exception>
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

            // 同一條 link 要從兩端各走一次，所以插兩筆、節點對調
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

                    // 這裡刻意不用 placements[otherPiece].Node(...)，因為它會把還沒算出來的
                    // 平移加進去。要的是「相對於片自己原點」的位置，所以直接對原型節點套姿態。
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
        /// <param name="pieces">絕對擺放，全部必須連成一塊。</param>
        /// <returns>
        /// 新的 Arrangement，links 有 pieces.Count − 1 條，順序就是一個合法的擺放順序。
        /// Key 與 PuzzleKey 還是 null，要的話再呼叫 <see cref="WithKeys"/>。
        /// 同一組擺放在不同的輸入順序下會產生不同的 links ——
        /// 這正是「相對編碼不是 canonical form」的意思。
        /// </returns>
        /// <exception cref="InvalidOperationException">有片沒有跟任何其他片接觸。</exception>
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
                        // 產生 link 的當下就標記，保證每片只被連上一次 → 一定是樹
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
        /// <returns>自己，方便串接。</returns>
        /// <remarks>
        /// 內部會 Decode 一次再算兩個 key，而 PuzzleKey 要跑 16 次輪廓追蹤。
        /// <b>不要放在每幀的程式碼裡。</b>
        /// </remarks>
        public Arrangement WithKeys()
        {
            Placement[] placements = Decode();
            Key = Canonical.ArrangementKey(placements);
            PuzzleKey = Canonical.PuzzleKey(placements);
            return this;
        }
    }
}
