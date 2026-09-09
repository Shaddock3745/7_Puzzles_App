using System.Collections.Generic;

namespace Tangram.Data
{
    /// <summary>
    /// 一片在某個姿態下的節點位置，以**片自己的原點**為基準（還沒平移到世界座標）。
    ///
    /// 存在的理由是**重複使用**：枚舉候選擺放時，同一個姿態會被拿去對成千上萬個錨點，
    /// 每次重算 <see cref="Symmetry.Apply"/> 是白費的。出題器與求解器可以把
    /// 五種片 × 8 旋轉 × 2 翻面 = 80 個姿態全部先建好，整個搜尋期間重複用。
    ///
    /// ⚠️ <c>default(PieceNodeSet)</c> 的 <see cref="Nodes"/> 是 <c>null</c>，不能用。
    /// 這個型別只有一個建構式，正常路徑不會拿到未初始化的值。
    /// </summary>
    public readonly struct PieceNodeSet
    {
        /// <summary>片種。</summary>
        public readonly PieceKind Kind;

        /// <summary>旋轉值 0~7，已經 Modulo8 正規化過。</summary>
        public readonly int Rotation;

        /// <summary>是否翻面。</summary>
        public readonly bool IsFlipped;

        /// <summary>
        /// 這個姿態下每個節點相對於片原點的位置。
        ///
        /// **索引就是原型的節點索引**（<c>Arrangement.Links</c> 存的那個），翻面也不重排
        /// ——那是儲存契約，`Placement.Nodes()` 同樣不重排。
        /// ⚠️ 陣列本身不要改，外面拿到的是同一份參照。
        /// </summary>
        public readonly ExactVector2[] Nodes;

        /// <param name="kind">片種。</param>
        /// <param name="rotation">旋轉值，會用 Modulo8 正規化，所以傳 −1 或 9 都安全。</param>
        /// <param name="flipped">是否翻面。</param>
        public PieceNodeSet(PieceKind kind, int rotation, bool flipped)
        {
            Kind = kind;
            Rotation = Symmetry.Modulo8(rotation);
            IsFlipped = flipped;

            PiecePrototype prototype = PiecePrototype.Get(kind);
            Nodes = new ExactVector2[prototype.NodeCount];
            for (int nodeIndex = 0; nodeIndex < Nodes.Length; nodeIndex++)
                Nodes[nodeIndex] = Symmetry.Apply(Rotation, IsFlipped, prototype.Nodes[nodeIndex]);
        }

        /// <summary>這一片有幾個節點。</summary>
        public int NodeCount => Nodes.Length;

        /// <param name="translation">平移量。</param>
        /// <returns>這個姿態平移到指定位置後的擺放。</returns>
        public Placement AtTranslation(ExactVector2 translation)
            => new Placement(Kind, Rotation, IsFlipped, translation);
    }

    /// <summary>
    /// 一個「片的某個節點正好落在某個錨點上」的擺放，外加它是由哪一組（錨點, 節點）產生的。
    ///
    /// 兩個索引不是裝飾用的：吸附要拿 <see cref="PieceNodeIndex"/> 當旋轉軸心，
    /// 出題器要拿 <see cref="AnchorIndex"/> 回推 <c>Arrangement.Link</c>。
    /// </summary>
    public readonly struct PlacementCandidate
    {
        /// <summary>候選的精確擺放。</summary>
        public readonly Placement Placement;

        /// <summary>對到 anchors 清單裡的第幾個錨點。</summary>
        public readonly int AnchorIndex;

        /// <summary>用片的第幾個節點去對。索引是原型的節點索引。</summary>
        public readonly int PieceNodeIndex;

        /// <param name="placement">候選的精確擺放。</param>
        /// <param name="anchorIndex">對到 anchors 清單裡的第幾個錨點。</param>
        /// <param name="pieceNodeIndex">用片的第幾個節點去對。</param>
        public PlacementCandidate(Placement placement, int anchorIndex, int pieceNodeIndex)
        {
            Placement = placement;
            AnchorIndex = anchorIndex;
            PieceNodeIndex = pieceNodeIndex;
        }
    }

    /// <summary>
    /// **「貼合」的唯一定義**：給一組錨點與一個姿態，列出所有「片的某個節點落在某個錨點上」的擺放。
    ///
    /// <b>為什麼獨立成一支</b>（P2-1）：拖曳吸附、出題器、求解器三邊要的是同一件事。
    /// 各寫一份的話，哪天吸附規則改了，**玩家吸得到的擺放**與**出題器認得的擺放**會分家——
    /// 症狀是生出來的題目玩家拼不出來，而且不會報錯。
    ///
    /// <b>這裡只做幾何配對，不做任何合法性判斷</b>：出不出盒、重不重疊都不管。
    /// 那兩件事由呼叫端用 <see cref="Region.Contains(Placement)"/> 與
    /// <see cref="Geometry.ConvexOverlap"/> 決定，因為「什麼算合法」是每個呼叫端自己的事。
    ///
    /// 輸出全部是精確座標——「貼合」是整數相等，不是距離門檻（鐵則一）。
    /// </summary>
    public static class PlacementCandidates
    {
        /// <summary>
        /// 列出所有貼合的擺放，不做任何距離篩選。出題器與求解器用這一支。
        /// </summary>
        /// <param name="pose">要擺的片與它的姿態。</param>
        /// <param name="anchors">錨點集合（已放置片的節點、盒子輪廓的節點，或兩者）。</param>
        /// <param name="results">
        /// 結果**附加**到這個清單，不會先清空——呼叫端可以分批把多組錨點的結果收進同一份，
        /// 也可以重複使用同一個清單避免每次配置。
        /// </param>
        public static void Enumerate(in PieceNodeSet pose,
                                     IReadOnlyList<ExactVector2> anchors,
                                     List<PlacementCandidate> results)
            => EnumerateWithin(pose, anchors, results, 0.0, 0.0, double.PositiveInfinity);

        /// <summary>
        /// 同 <see cref="Enumerate"/>，但只留下**平移量**落在指定圓內的候選。拖曳吸附用這一支。
        ///
        /// ⚠️ 這個圓<b>只是剪枝</b>，不參與「什麼叫貼合」的定義——被篩掉的候選仍然是貼合的，
        /// 只是這次的呼叫端不想要。所以圓心與半徑用**模型單位**，
        /// 螢幕像素的換算留在互動層（<c>SnapSolver</c>），這一層不認識螢幕。
        /// </summary>
        /// <param name="pose">要擺的片與它的姿態。</param>
        /// <param name="anchors">錨點集合。</param>
        /// <param name="results">結果附加到這個清單，不會先清空。</param>
        /// <param name="centerX">圓心 X（模型單位）。</param>
        /// <param name="centerY">圓心 Y（模型單位）。</param>
        /// <param name="maximumDistanceSquared">
        /// 半徑的平方（模型單位）。傳 <c>double.PositiveInfinity</c> 等於不篩，
        /// 而且會**整段跳過**距離計算，不是算完再比。
        /// </param>
        public static void EnumerateWithin(in PieceNodeSet pose,
                                           IReadOnlyList<ExactVector2> anchors,
                                           List<PlacementCandidate> results,
                                           double centerX, double centerY,
                                           double maximumDistanceSquared)
        {
            ExactVector2[] pieceNodes = pose.Nodes;
            bool unlimited = double.IsPositiveInfinity(maximumDistanceSquared);

            // ⚠️ 這兩層迴圈的順序是契約，不是實作細節：SnapSolver 會把結果依距離排序，
            // 而 List.Sort 不穩定——距離相同時誰在前面就取決於這裡的產生順序。
            // 換了順序，吸附會選到不同的候選，而且沒有任何測試會紅（互動設計 §4）
            for (int anchorIndex = 0; anchorIndex < anchors.Count; anchorIndex++)
            {
                for (int pieceNodeIndex = 0; pieceNodeIndex < pieceNodes.Length; pieceNodeIndex++)
                {
                    // 精確：錨點減掉節點的相對位置，就是讓那個節點落在錨點上的平移量
                    ExactVector2 translation = anchors[anchorIndex] - pieceNodes[pieceNodeIndex];

                    if (!unlimited)
                    {
                        double deltaX = translation.X.ToDouble() - centerX;
                        double deltaY = translation.Y.ToDouble() - centerY;
                        if (deltaX * deltaX + deltaY * deltaY > maximumDistanceSquared) continue;
                    }

                    results.Add(new PlacementCandidate(
                        pose.AtTranslation(translation), anchorIndex, pieceNodeIndex));
                }
            }
        }
    }
}
