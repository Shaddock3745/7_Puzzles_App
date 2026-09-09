using System;
using System.Collections.Generic;
using NUnit.Framework;
using Tangram.Data;

namespace Tangram.Data.Tests
{
    /// <summary>
    /// 共用的擺法枚舉原語（P2-1，issue #22）。
    ///
    /// <b>這批測試守的是三件事</b>：
    /// <list type="number">
    /// <item>姿態的節點座標與 <see cref="Placement"/> 算出來的一致
    ///       ——兩邊分家的話，玩家吸得到的擺放與出題器認得的擺放會不同</item>
    /// <item><b>產生順序是「錨點外層、節點內層」</b>——<c>SnapSolver</c> 依距離排序而
    ///       <c>List.Sort</c> 不穩定，距離相同時的先後完全取決於這個順序。
    ///       ⚠️ 這一條沒有別的東西在保證：輸入層沒有測試，順序變了只會「玩起來覺得怪」</item>
    /// <item>距離窗只是剪枝，不改變「什麼叫貼合」</item>
    /// </list>
    /// </summary>
    public class PlacementCandidateTests
    {
        private static Region ClassicSquareBox()
            => Region.FromPuzzle(Puzzle.FromPlacements(Fixtures.ClassicSquare()), ExactVector2.Zero);

        private static PieceKind[] AllKinds()
            => (PieceKind[])Enum.GetValues(typeof(PieceKind));

        // ─────────────────────────────────────────────────────────────
        // 一、姿態的節點與 Placement 一致
        // ─────────────────────────────────────────────────────────────

        /// <summary>
        /// <see cref="PieceNodeSet"/> 是把 <c>Placement</c> 在原點時的節點抽出來重複用，
        /// 所以兩邊必須逐一相等——包含翻面。
        ///
        /// ⚠️ 特別注意<b>翻面不重排索引</b>：<c>Vertices()</c> 會反轉寫入順序把繞向正規化回 CCW，
        /// 但 <c>Nodes()</c> 刻意不做，因為節點索引是儲存契約（鐵則二）。
        /// 這裡跟的是 <c>Nodes()</c> 那一邊。
        /// </summary>
        [Test]
        public void 姿態_節點座標與擺放在原點時完全一致()
        {
            foreach (PieceKind kind in AllKinds())
            {
                for (int rotation = 0; rotation < 8; rotation++)
                {
                    foreach (bool flipped in new[] { false, true })
                    {
                        var pose = new PieceNodeSet(kind, rotation, flipped);
                        var atOrigin = new Placement(kind, rotation, flipped, ExactVector2.Zero);

                        Assert.AreEqual(atOrigin.NodeCount, pose.NodeCount,
                                        $"{kind} rotation={rotation} flipped={flipped} 節點數不一致");

                        for (int nodeIndex = 0; nodeIndex < pose.NodeCount; nodeIndex++)
                            Assert.AreEqual(atOrigin.Node(nodeIndex), pose.Nodes[nodeIndex],
                                            $"{kind} rotation={rotation} flipped={flipped} " +
                                            $"第 {nodeIndex} 個節點不一致");
                    }
                }
            }
        }

        /// <summary>旋轉值超出 0~7 要被 Modulo8 正規化，跟 <c>Placement</c> 的行為一致。</summary>
        [Test]
        public void 姿態_旋轉值超出範圍會正規化()
        {
            var negative = new PieceNodeSet(PieceKind.LargeTriangle, -1, false);
            var wrapped = new PieceNodeSet(PieceKind.LargeTriangle, 7, false);

            Assert.AreEqual(7, negative.Rotation);
            Assert.AreEqual(wrapped.Nodes, negative.Nodes);
        }

        // ─────────────────────────────────────────────────────────────
        // 二、枚舉的定義與順序
        // ─────────────────────────────────────────────────────────────

        /// <summary>不篩的時候，每個錨點都要跟每個節點配到，一個不漏也不重複。</summary>
        [Test]
        public void 枚舉_不篩時是錨點數乘節點數()
        {
            var pose = new PieceNodeSet(PieceKind.MediumTriangle, 3, false);
            ExactVector2[] anchors = ClassicSquareBox().BoundaryNodes;

            var results = new List<PlacementCandidate>();
            PlacementCandidates.Enumerate(pose, anchors, results);

            Assert.AreEqual(anchors.Length * pose.NodeCount, results.Count);
        }

        /// <summary>
        /// 「貼合」的定義本身：每個候選的那個節點，必須<b>正好</b>落在那個錨點上。
        /// 精確座標，所以這是整數相等不是容差比較（鐵則一）。
        /// </summary>
        [Test]
        public void 枚舉_每個候選都讓指定節點落在指定錨點上()
        {
            ExactVector2[] anchors = ClassicSquareBox().BoundaryNodes;
            var results = new List<PlacementCandidate>();

            foreach (PieceKind kind in AllKinds())
            {
                for (int rotation = 0; rotation < 8; rotation++)
                {
                    results.Clear();
                    PlacementCandidates.Enumerate(new PieceNodeSet(kind, rotation, false), anchors, results);

                    foreach (PlacementCandidate candidate in results)
                        Assert.AreEqual(anchors[candidate.AnchorIndex],
                                        candidate.Placement.Node(candidate.PieceNodeIndex),
                                        $"{kind} rotation={rotation} " +
                                        $"錨點 {candidate.AnchorIndex} 節點 {candidate.PieceNodeIndex} 沒對上");
                }
            }
        }

        /// <summary>
        /// ⚠️ <b>順序是契約</b>：錨點在外層、節點在內層。
        ///
        /// <c>SnapSolver</c> 把候選依螢幕距離排序後取第一個通過驗證的，而 <c>List.Sort</c>
        /// <b>不穩定</b>——距離相同的候選誰在前面，完全由這裡的產生順序決定。
        /// 順序換掉的話，吸附會選到不同的落點；而輸入層沒有測試，
        /// 症狀只會是「玩起來覺得怪」，不會有任何東西變紅（P2-1 的卡片就是為了這個寫「是搬移不是重寫」）。
        /// </summary>
        [Test]
        public void 枚舉_順序是錨點外層節點內層()
        {
            var pose = new PieceNodeSet(PieceKind.SmallTriangle, 1, false);
            var anchors = new[]
            {
                new ExactVector2(0, 0),
                new ExactVector2(10, 0),
                new ExactVector2(0, 10)
            };

            var results = new List<PlacementCandidate>();
            PlacementCandidates.Enumerate(pose, anchors, results);

            int position = 0;
            for (int anchorIndex = 0; anchorIndex < anchors.Length; anchorIndex++)
            {
                for (int nodeIndex = 0; nodeIndex < pose.NodeCount; nodeIndex++)
                {
                    Assert.AreEqual(anchorIndex, results[position].AnchorIndex, $"第 {position} 個的錨點索引");
                    Assert.AreEqual(nodeIndex, results[position].PieceNodeIndex, $"第 {position} 個的節點索引");
                    position++;
                }
            }
            Assert.AreEqual(position, results.Count);
        }

        /// <summary>結果是**附加**不是覆蓋，這樣呼叫端可以把多組錨點收進同一份清單。</summary>
        [Test]
        public void 枚舉_結果是附加而不是清空重寫()
        {
            var pose = new PieceNodeSet(PieceKind.Square, 0, false);
            var anchors = new[] { ExactVector2.Zero };

            var results = new List<PlacementCandidate>();
            PlacementCandidates.Enumerate(pose, anchors, results);
            int afterFirst = results.Count;

            PlacementCandidates.Enumerate(pose, anchors, results);
            Assert.AreEqual(afterFirst * 2, results.Count);
        }

        // ─────────────────────────────────────────────────────────────
        // 三、距離窗只是剪枝
        // ─────────────────────────────────────────────────────────────

        /// <summary>
        /// 距離窗篩出來的，必須跟「不篩之後自己手動濾一遍」一模一樣（含順序）。
        /// 也就是說窗只是剪枝，不會改變任何候選的內容或先後。
        /// </summary>
        [Test]
        public void 距離窗_等於不篩之後手動濾一遍()
        {
            var pose = new PieceNodeSet(PieceKind.LargeTriangle, 2, false);
            ExactVector2[] anchors = ClassicSquareBox().BoundaryNodes;

            const double centerX = 3.5;
            const double centerY = 2.25;
            const double maximumDistanceSquared = 4.0;

            var everything = new List<PlacementCandidate>();
            PlacementCandidates.Enumerate(pose, anchors, everything);

            var expected = new List<PlacementCandidate>();
            foreach (PlacementCandidate candidate in everything)
            {
                double deltaX = candidate.Placement.Translation.X.ToDouble() - centerX;
                double deltaY = candidate.Placement.Translation.Y.ToDouble() - centerY;
                if (deltaX * deltaX + deltaY * deltaY <= maximumDistanceSquared) expected.Add(candidate);
            }

            var actual = new List<PlacementCandidate>();
            PlacementCandidates.EnumerateWithin(pose, anchors, actual,
                                                centerX, centerY, maximumDistanceSquared);

            Assert.AreEqual(expected.Count, actual.Count, "篩出來的數量不一致");
            Assert.Greater(expected.Count, 0, "測試本身要有東西可比，窗開太小就沒在驗了");
            Assert.Less(expected.Count, everything.Count, "窗要真的有篩掉東西，不然這條等於沒驗");

            for (int i = 0; i < expected.Count; i++)
            {
                Assert.AreEqual(expected[i].Placement, actual[i].Placement, $"第 {i} 個擺放不一致");
                Assert.AreEqual(expected[i].AnchorIndex, actual[i].AnchorIndex, $"第 {i} 個錨點索引不一致");
                Assert.AreEqual(expected[i].PieceNodeIndex, actual[i].PieceNodeIndex, $"第 {i} 個節點索引不一致");
            }
        }

        // ─────────────────────────────────────────────────────────────
        // 四、卡片的完成定義：對古典大正方形的合法擺放數量
        // ─────────────────────────────────────────────────────────────

        /// <summary>
        /// <b>issue #22 的完成定義</b>：對古典大正方形的盒子，盤面全空時，
        /// 「吸得到盒子輪廓、而且整片落在盒內」的相異擺放共 <b>392</b> 個。
        ///
        /// 這個數字原本只被 <c>CellGridTests</c> 印出來（沒有斷言），現在釘死。
        /// 它同時證明**新的原語與原本 <c>SnapSolver</c> 的枚舉方式產生同一個集合**——
        /// 那正是 P2-1 要保證的事。
        ///
        /// ⚠️ 只掃到每種片自己的旋轉循環長度為止：掃滿 0~7 的話，
        /// 正方形（循環 2）與平行四邊形（循環 4）會產生幾何上完全相同的重複擺放，
        /// 數字就不代表任何東西了。
        /// </summary>
        [Test]
        public void 古典大正方形_盤面全空的合法擺放共三百九十二個()
        {
            Region box = ClassicSquareBox();
            ExactVector2[] anchors = box.BoundaryNodes;

            var distinct = new HashSet<Placement>();
            var results = new List<PlacementCandidate>();

            foreach (PieceKind kind in AllKinds())
            {
                int flipCount = kind == PieceKind.Parallelogram ? 2 : 1;
                int rotationCycle = kind == PieceKind.Square ? 2
                                  : kind == PieceKind.Parallelogram ? 4
                                  : 8;

                for (int flipIndex = 0; flipIndex < flipCount; flipIndex++)
                {
                    for (int rotation = 0; rotation < rotationCycle; rotation++)
                    {
                        results.Clear();
                        PlacementCandidates.Enumerate(
                            new PieceNodeSet(kind, rotation, flipIndex == 1), anchors, results);

                        foreach (PlacementCandidate candidate in results)
                            if (box.Contains(candidate.Placement))   // 出盒的不算合法
                                distinct.Add(candidate.Placement);
                    }
                }
            }

            Assert.AreEqual(392, distinct.Count);
        }
    }
}
