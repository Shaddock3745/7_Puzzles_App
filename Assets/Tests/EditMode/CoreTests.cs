using System.Collections.Generic;
using NUnit.Framework;
using Tangram.Core;
using Tangram.Data;
using UnityEngine;

namespace Tangram.Core.Tests
{
    public class CoreTests
    {
        private static BoardState SolvedBoard()
        {
            var source = new ScriptedInputSource();
            source.LoadPlacements(Fixtures.ClassicSquare());
            return source.Board;
        }

        [Test]
        public void 盤面_初始沒有任何片在盤上()
        {
            var board = new BoardState();
            Assert.AreEqual(0, board.OnBoardCount);
            Assert.IsFalse(board.AllOnBoard);
        }

        [Test]
        public void 盤面_放上七片後全部在盤上()
        {
            BoardState board = SolvedBoard();
            Assert.AreEqual(7, board.OnBoardCount);
            Assert.IsTrue(board.AllOnBoard);
        }

        [Test]
        public void 盤面_不能把片放到種類不符的槽()
        {
            var board = new BoardState();
            int squareSlot = -1;
            for (int i = 0; i < BoardState.PieceCount; i++)
                if (board.KindOf(i) == PieceKind.Square) squareSlot = i;

            Assert.Throws<System.ArgumentException>(() =>
                board.Place(squareSlot, new Placement(PieceKind.Parallelogram, 0, false, ExactVector2.Zero)));
        }

        // ⚠️ 原本這裡有五個 PuzzleValidator 的測試（Solved / Incomplete / Disconnected /
        // WrongShape / 整體旋轉鏡射）。**2026-09-09 隨 PuzzleValidator 一起刪掉**——
        // 那支是自由拼版本，盒子模式下整套狀態機都不成立了：
        //
        //   Disconnected 不可能——吸附的硬限制不准片吸到盒外，所以盤上的片必定相連
        //   WrongShape   不可能——盒子就是題目，填得進去就是對的形狀
        //   整體旋轉鏡射 不需要——盒子在盤面上有固定位置，玩家不會整組轉
        //
        // 剩下的 Solved / Incomplete 退化成 board.AllOnBoard 一個布林，
        // 判定本身測在 LevelTests 的〈過關判定〉那一組。

        [Test]
        public void 盤面_放滿之後可以收成一筆解()
        {
            // 這一條原本掛在 PuzzleValidator.ToArrangement 上。那支只是個四行包裝，
            // 而且**它的替代寫法更安全**：它會無條件讀七個槽（沒放的片讀到的是上次的
            // 姿態，是垃圾），改成拿「在盤上的那些」就沒有這個陷阱。
            //
            // 留著這條測試是因為 P2 出題器與 P3 存檔都會用到「盤面 → 一筆解」這件事
            BoardState board = SolvedBoard();
            Arrangement arrangement =
                Arrangement.FromPlacements(board.OnBoardPlacements()).WithKeys();

            Assert.AreEqual(7, arrangement.Pieces.Length);
            Assert.AreEqual(6, arrangement.Links.Length);
            Assert.AreEqual(Puzzle.FromPlacements(Fixtures.ClassicSquare()).Key, arrangement.PuzzleKey);
        }

        // --- 吸附（附錄 B）---

        private static readonly Placement BigTriangle = new Placement(PieceKind.LargeTriangle, 0, false, ExactVector2.Zero);
        private static SnapSolver.Settings FiftyScreenPixelsPerModelUnit => SnapSolver.Settings.Default(50f);

        [Test]
        public void 吸附_接近合法落點時吸上去且座標精確()
        {
            var placed = new[] { BigTriangle };
            // 正確落點是 T = (0,2)，手指停在差 0.05 單位（= 3.5px）的地方
            SnapSolver.DragResult result = SnapSolver.Resolve(
                PieceKind.SmallTriangle, 4, false, new Vector2(0.05f, 1.95f), placed, FiftyScreenPixelsPerModelUnit);

            Assert.IsTrue(result.Snapped);
            Assert.AreEqual(new ExactVector2(0, 2), result.Placement.Translation);
            Assert.AreEqual(0, result.Placement.Translation.X.Root2Coefficient, "吸附結果必須是精確整數，不能有殘留");
        }

        [Test]
        public void 吸附_太遠就保持自由拖曳()
        {
            var placed = new[] { BigTriangle };
            SnapSolver.DragResult result = SnapSolver.Resolve(
                PieceKind.SmallTriangle, 4, false, new Vector2(20f, 20f), placed, FiftyScreenPixelsPerModelUnit);

            Assert.IsFalse(result.Snapped);
            Assert.AreEqual(20f, result.FreePosition.x);
        }

        [Test]
        public void 吸附_遲滯_吸附半徑外但脫離半徑內只有已吸附時才成立()
        {
            var placed = new[] { BigTriangle };
            var target = new ExactVector2(0, 2);
            var fingerPosition = new Vector2(0f, 2.5f);           // 距離 0.5 單位 = 25px（在 18 與 30 之間）

            bool ContainsTargetTranslation(List<SnapSolver.Candidate> list)
            {
                foreach (SnapSolver.Candidate candidate in list) if (candidate.Placement.Translation == target) return true;
                return false;
            }

            var looseCandidates = SnapSolver.Candidates(PieceKind.SmallTriangle, 4, false, fingerPosition, placed, FiftyScreenPixelsPerModelUnit, false);
            var stickyCandidates = SnapSolver.Candidates(PieceKind.SmallTriangle, 4, false, fingerPosition, placed, FiftyScreenPixelsPerModelUnit, true);

            Assert.IsFalse(ContainsTargetTranslation(looseCandidates), "還沒吸附時 25px 應該吸不到");
            Assert.IsTrue(ContainsTargetTranslation(stickyCandidates), "已經吸附時 25px 應該還黏著");
        }

        [Test]
        public void 吸附_不會吸到會重疊的位置()
        {
            var placed = new[] { BigTriangle };
            // 大三角內部：所有候選都會重疊，應該落空
            SnapSolver.DragResult result = SnapSolver.Resolve(
                PieceKind.SmallTriangle, 0, false, new Vector2(1f, 1f), placed, FiftyScreenPixelsPerModelUnit);
            Assert.IsFalse(result.Snapped);
        }

        [Test]
        public void 旋轉_角度換算成離散rotation()
        {
            Assert.AreEqual(0, SnapSolver.RotationFromDegrees(3f));
            Assert.AreEqual(1, SnapSolver.RotationFromDegrees(40f));
            Assert.AreEqual(1, SnapSolver.RotationFromDegrees(50f));
            Assert.AreEqual(0, SnapSolver.RotationFromDegrees(360f));
            Assert.AreEqual(7, SnapSolver.RotationFromDegrees(-45f));
        }

        [Test]
        public void 旋轉_以吸附節點為軸心時該節點不動()
        {
            var piece = new Placement(PieceKind.SmallTriangle, 0, false, new ExactVector2(2, 2));
            for (int node = 0; node < piece.NodeCount; node++)
            {
                ExactVector2 before = piece.Node(node);
                Placement turned = piece.WithPose(2, false, node);
                Assert.AreEqual(before, turned.Node(node), $"node {node} 動了");
            }
        }

        [Test]
        public void 翻面只對平行四邊形開放()
        {
            Assert.IsTrue(SnapSolver.CanFlip(PieceKind.Parallelogram));
            Assert.IsFalse(SnapSolver.CanFlip(PieceKind.SmallTriangle));
            Assert.IsFalse(SnapSolver.CanFlip(PieceKind.Square));
        }

        [Test]
        public void 旋轉_循環長度依片種()
        {
            Assert.AreEqual(8, SnapSolver.RotationCycleLength(PieceKind.LargeTriangle));
            Assert.AreEqual(8, SnapSolver.RotationCycleLength(PieceKind.MediumTriangle));
            Assert.AreEqual(8, SnapSolver.RotationCycleLength(PieceKind.SmallTriangle));
            Assert.AreEqual(2, SnapSolver.RotationCycleLength(PieceKind.Square));
            Assert.AreEqual(4, SnapSolver.RotationCycleLength(PieceKind.Parallelogram));
        }

        /// <summary>
        /// 順時針鈕（2026-09-13）：從 0 往回轉一格要落在<b>最後一格</b>，不是 −1。
        /// ⚠️ C# 的 % 對負數回負數，直接用的話 PieceView 會存著 −1，同一個外觀兩個編號。
        /// </summary>
        [TestCase(PieceKind.SmallTriangle, 7)]
        [TestCase(PieceKind.Square, 1)]
        [TestCase(PieceKind.Parallelogram, 3)]
        public void 旋轉_從零順時針轉一格落在最後一格(PieceKind kind, int expected)
        {
            Assert.AreEqual(expected, PiecePrototype.RotateWithinCycle(kind, 0, -1));
        }

        /// <summary>
        /// 每一種片、每一個起點、正反各轉 N 格：結果永遠在循環範圍內，而且逆轉 N 格一定回到原點。
        /// N 刻意包含比循環長度大的值（按很多下）。
        /// </summary>
        [Test]
        public void 旋轉_順轉再逆轉回到原點而且永遠在範圍內()
        {
            foreach (PieceKind kind in PiecePrototype.TangramSet)
            {
                int cycleLength = PiecePrototype.RotationCycleLength(kind);
                for (int start = 0; start < cycleLength; start++)
                    foreach (int steps in new[] { 1, -1, 3, -5, 17, -17 })
                    {
                        int turned = PiecePrototype.RotateWithinCycle(kind, start, steps);
                        Assert.That(turned, Is.InRange(0, cycleLength - 1), $"{kind} 從 {start} 轉 {steps} 格");
                        Assert.AreEqual(start, PiecePrototype.RotateWithinCycle(kind, turned, -steps),
                            $"{kind} 從 {start} 轉 {steps} 格再轉回來沒有回到原點");
                    }
            }
        }

        // --- 翻轉鈕：左右鏡射（2026-09-14）---

        /// <returns>五種片、每一種能擺的每一個姿態（旋轉 0 ～ 循環長度−1，翻面只有平行四邊形）。</returns>
        private static IEnumerable<PiecePose> AllReachablePoses()
        {
            foreach (PieceKind kind in (PieceKind[])System.Enum.GetValues(typeof(PieceKind)))
                for (int flipIndex = 0; flipIndex < (PiecePrototype.CanFlip(kind) ? 2 : 1); flipIndex++)
                    for (int rotation = 0; rotation < PiecePrototype.RotationCycleLength(kind); rotation++)
                        yield return new PiecePose(kind, rotation, flipIndex == 1);
        }

        /// <summary>
        /// 每一個姿態鏡射之後的形狀，<b>就是現在的形狀把 x 取負</b>（位置不算）。
        /// </summary>
        [Test]
        public void 鏡射_結果的形狀就是現在左右鏡射()
        {
            foreach (PiecePose pose in AllReachablePoses())
            {
                ExactVector2[] current = new Placement(pose.Kind, pose.Rotation, pose.IsFlipped, ExactVector2.Zero).Vertices();
                var expected = new ExactVector2[current.Length];
                for (int i = 0; i < current.Length; i++) expected[i] = new ExactVector2(-current[i].X, current[i].Y);

                PiecePose mirrored = PiecePrototype.MirroredLeftRight(pose.Kind, pose.Rotation, pose.IsFlipped);
                ExactVector2[] actual = new Placement(mirrored.Kind, mirrored.Rotation, mirrored.IsFlipped, ExactVector2.Zero).Vertices();

                Assert.IsTrue(Geometry.SameVerticesIgnoringPosition(expected, actual), $"{pose} 鏡射成 {mirrored}，形狀不對");
            }
        }

        /// <summary>
        /// 不靠搜尋的錨點：小三角原型直角在原點、兩股朝 +x 與 +y。左右鏡射之後兩股朝 −x 與 +y，
        /// 那是逆時針轉 90°，也就是第 2 格。對稱軸（45°）直立的是第 1 格（90°）與第 5 格（270°）。
        /// </summary>
        [Test]
        public void 鏡射_小三角的錨點()
        {
            Assert.AreEqual(new PiecePose(PieceKind.SmallTriangle, 2, false),
                            PiecePrototype.MirroredLeftRight(PieceKind.SmallTriangle, 0, false));

            var symmetric = new List<int>();
            for (int rotation = 0; rotation < 8; rotation++)
                if (PiecePrototype.IsMirrorSymmetric(PieceKind.SmallTriangle, rotation, false)) symmetric.Add(rotation);
            CollectionAssert.AreEqual(new[] { 1, 5 }, symmetric);
        }

        /// <summary>鏡射兩次回到原本的姿態（翻轉鈕按兩下要回到原狀）。</summary>
        [Test]
        public void 鏡射_兩次回到原姿態()
        {
            foreach (PiecePose pose in AllReachablePoses())
            {
                PiecePose once = PiecePrototype.MirroredLeftRight(pose.Kind, pose.Rotation, pose.IsFlipped);
                Assert.AreEqual(pose, PiecePrototype.MirroredLeftRight(once.Kind, once.Rotation, once.IsFlipped), $"{pose} 翻兩次");
            }
        }

        /// <summary>
        /// ⚠️ <b>三角形與正方形鏡射之後永遠沒有翻面</b>，旋轉永遠在循環範圍內。
        /// 出現翻面的三角形的話，<c>Placement.Equals</c> 那類比較會安靜地對不上。
        /// </summary>
        [Test]
        public void 鏡射_只有平行四邊形會翻面而且旋轉在範圍內()
        {
            foreach (PiecePose pose in AllReachablePoses())
            {
                PiecePose mirrored = PiecePrototype.MirroredLeftRight(pose.Kind, pose.Rotation, pose.IsFlipped);
                Assert.AreEqual(pose.Kind, mirrored.Kind);
                Assert.That(mirrored.Rotation, Is.InRange(0, PiecePrototype.RotationCycleLength(pose.Kind) - 1), pose.ToString());
                if (!PiecePrototype.CanFlip(pose.Kind)) Assert.IsFalse(mirrored.IsFlipped, $"{pose} 鏡射出翻面");
            }
        }

        /// <summary>
        /// ⭐ <b>平行四邊形的行為跟以前一模一樣</b>：以前的翻面鈕是「翻面切換 ＋ 旋轉取負」（上下鏡射），
        /// 平行四邊形轉 180° 長一樣，所以跟左右鏡射是同一個姿態。
        /// </summary>
        [Test]
        public void 鏡射_平行四邊形跟舊的翻面規則一樣()
        {
            foreach (bool flipped in new[] { false, true })
                for (int rotation = 0; rotation < 4; rotation++)
                {
                    var oldRule = new PiecePose(PieceKind.Parallelogram, (4 - rotation) % 4, !flipped);
                    Assert.AreEqual(oldRule, PiecePrototype.MirroredLeftRight(PieceKind.Parallelogram, rotation, flipped),
                                    $"轉 {rotation} 格、翻面 {flipped}");
                }
        }

        /// <summary>
        /// 按了沒變化（翻轉鈕變灰）的次數：正方形 2／2、每種三角形 2／8、平行四邊形 0／8。
        /// </summary>
        [TestCase(PieceKind.Square, 2)]
        [TestCase(PieceKind.LargeTriangle, 2)]
        [TestCase(PieceKind.MediumTriangle, 2)]
        [TestCase(PieceKind.SmallTriangle, 2)]
        [TestCase(PieceKind.Parallelogram, 0)]
        public void 鏡射_按了沒變化的姿態數(PieceKind kind, int expectedCount)
        {
            int count = 0;
            foreach (PiecePose pose in AllReachablePoses())
                if (pose.Kind == kind && PiecePrototype.IsMirrorSymmetric(pose.Kind, pose.Rotation, pose.IsFlipped)) count++;
            Assert.AreEqual(expectedCount, count);
        }

        /// <summary>旋轉值超出循環（正方形傳 2、傳負數）也照循環正規化，跟旋轉鈕同一條規則。</summary>
        [Test]
        public void 鏡射_旋轉值會先照循環正規化()
        {
            Assert.AreEqual(PiecePrototype.MirroredLeftRight(PieceKind.Square, 0, false),
                            PiecePrototype.MirroredLeftRight(PieceKind.Square, 2, false));
            Assert.IsTrue(PiecePrototype.IsMirrorSymmetric(PieceKind.Square, 2, false), "正方形轉 2 格跟 0 格一樣，要算對稱");
            Assert.AreEqual(PiecePrototype.MirroredLeftRight(PieceKind.SmallTriangle, 7, false),
                            PiecePrototype.MirroredLeftRight(PieceKind.SmallTriangle, -1, false));
        }

        // --- P1-7 盒子模式的吸附 ---

        /// <summary>經典大正方形當盒子，原點放在 (0,0)。</summary>
        private static Region ClassicSquareBox()
            => Region.FromPuzzle(Puzzle.FromPlacements(Fixtures.ClassicSquare()), ExactVector2.Zero);

        [Test]
        public void 盒子_輪廓節點可以當錨點_盤面全空時第一片就有候選()
        {
            Region box = ClassicSquareBox();
            ExactVector2 corner = box.Rings[0][0];

            // 盤面完全沒有片，只靠盒子輪廓的節點
            var withoutBox = SnapSolver.Candidates(
                PieceKind.SmallTriangle, 0, false, SnapSolver.ToUnityVector2(corner),
                new Placement[0], FiftyScreenPixelsPerModelUnit);
            Assert.AreEqual(0, withoutBox.Count, "沒有錨點來源時不該有候選");

            var withBox = SnapSolver.Candidates(
                PieceKind.SmallTriangle, 0, false, SnapSolver.ToUnityVector2(corner),
                new Placement[0], FiftyScreenPixelsPerModelUnit, false, box.BoundaryNodes);
            Assert.Greater(withBox.Count, 0, "盒子輪廓的節點該讓第一片就有磁性");
            Assert.IsTrue(withBox[0].IsBoxAnchor);
        }

        [Test]
        public void 盒子_輪廓節點含邊上的細分點而且不重複()
        {
            Region box = ClassicSquareBox();
            ExactVector2[] nodes = box.BoundaryNodes;

            // 大正方形的輪廓是四條長 4√2 的邊，節點一定比四個頂點多
            Assert.Greater(nodes.Length, 4);
            CollectionAssert.AllItemsAreUnique(nodes);

            foreach (ExactVector2 node in nodes)
                Assert.AreEqual(RegionPosition.OnBoundary, box.Classify(node),
                    $"邊界節點 {node} 應該落在邊界上");
        }

        /// <summary>
        /// ⚠️ <b>候選的產生順序是契約</b>（P2-1，issue #22）。
        ///
        /// <c>Candidates</c> 依螢幕距離排序，而 <c>List.Sort</c> <b>不穩定</b>——
        /// 距離相同的候選誰在前面，完全由產生順序決定，而 <c>Resolve</c> 取的是
        /// 「由近到遠第一個通過驗證的」。所以順序換掉會讓吸附選到不同落點，
        /// 而輸入層沒有測試，症狀只會是「玩起來覺得怪」，不會有東西變紅。
        ///
        /// ⚠️ <b>但這一條沒辦法在這裡驗</b>：回傳前已經排序過，而不穩定排序對同距離的
        /// 候選會怎麼重排是實作決定的，斷言它等於在斷言 introsort 的巧合。
        /// <b>產生順序的契約釘在原語那一層</b>（<c>PlacementCandidateTests.枚舉_順序是錨點外層節點內層</c>），
        /// 那裡拿得到還沒排序的結果。
        ///
        /// 所以這裡守的是另外兩件抽離時最容易弄丟的事：
        /// <b>候選一個都不能少</b>（extraAnchors 那一趟很容易在重構時漏掉），
        /// 以及<b>來源片索引標對</b>（盒子錨點必須是 −1，不能被當成第 0 片）。
        /// 用「大到不會篩掉任何東西的吸附半徑」把整份攤開來比。
        /// </summary>
        [Test]
        public void 候選_不多不少而且來源標對()
        {
            Region box = ClassicSquareBox();
            Placement[] solution = Fixtures.ClassicSquare();
            var placed = new[] { solution[0], solution[1] };

            // 半徑大到不會篩掉任何候選，這樣比到的就是完整的產生順序
            var noFiltering = new SnapSolver.Settings
            {
                ScreenPixelsPerModelUnit = 1f,
                SnapRadiusPixels = 100000f,
                ReleaseRadiusPixels = 100000f
            };

            const PieceKind kind = PieceKind.SmallTriangle;
            const int rotation = 1;

            List<SnapSolver.Candidate> candidates = SnapSolver.Candidates(
                kind, rotation, false, Vector2.zero, placed, noFiltering, false, box.BoundaryNodes);

            var pose = new PieceNodeSet(kind, rotation, false);
            var expected = new List<(int targetPiece, int targetNode, int draggedNode)>();

            for (int pieceIndex = 0; pieceIndex < placed.Length; pieceIndex++)
                for (int anchorIndex = 0; anchorIndex < placed[pieceIndex].NodeCount; anchorIndex++)
                    for (int nodeIndex = 0; nodeIndex < pose.NodeCount; nodeIndex++)
                        expected.Add((pieceIndex, anchorIndex, nodeIndex));

            for (int anchorIndex = 0; anchorIndex < box.BoundaryNodes.Length; anchorIndex++)
                for (int nodeIndex = 0; nodeIndex < pose.NodeCount; nodeIndex++)
                    expected.Add((-1, anchorIndex, nodeIndex));

            Assert.AreEqual(expected.Count, candidates.Count, "半徑夠大時不該篩掉任何候選");

            var produced = new List<(int, int, int)>();
            foreach (SnapSolver.Candidate candidate in candidates)
                produced.Add((candidate.TargetPiece, candidate.TargetNode, candidate.DraggedNode));

            CollectionAssert.AreEquivalent(expected, produced,
                "候選的（來源片, 錨點, 拖曳節點）三元組必須不多不少，而且來源標對");
        }

        [Test]
        public void 盒子_Candidates只做距離篩選_不管盒內限制()
        {
            Region box = ClassicSquareBox();
            Vector2 finger = SnapSolver.ToUnityVector2(box.Rings[0][0]);

            var candidates = SnapSolver.Candidates(
                PieceKind.LargeTriangle, 0, false, finger,
                new Placement[0], FiftyScreenPixelsPerModelUnit, false, box.BoundaryNodes);

            bool anyOutside = false;
            foreach (SnapSolver.Candidate candidate in candidates)
                if (!box.Contains(candidate.Placement)) anyOutside = true;

            Assert.IsTrue(anyOutside,
                "Candidates 不該過濾盒內限制——出盒的候選要留給 Resolve 的迴圈懶惰檢查");
        }

        [Test]
        public void 盒子_Resolve會跳過出盒的候選()
        {
            Region box = ClassicSquareBox();
            ExactVector2 origin = new ExactVector2(0, 0);
            Assert.AreEqual(RegionPosition.OnBoundary, box.Classify(origin),
                "這個測試預設 (0,0) 是盒子的一個角");

            // 大三角 rotation = 4 是往左下開口，放在盒子左下角必定整片戳到盒外
            Vector2 finger = SnapSolver.ToUnityVector2(origin);

            SnapSolver.DragResult unrestricted = SnapSolver.Resolve(
                PieceKind.LargeTriangle, 4, false, finger,
                new Placement[0], FiftyScreenPixelsPerModelUnit, false, box.BoundaryNodes);

            Assert.IsTrue(unrestricted.Snapped, "不設限時應該吸得到最近的候選");
            Assert.IsFalse(box.Contains(unrestricted.Placement),
                "而且那個最近的候選確實在盒外——這樣下面的對照才有意義");

            SnapSolver.DragResult restricted = SnapSolver.Resolve(
                PieceKind.LargeTriangle, 4, false, finger,
                new Placement[0], FiftyScreenPixelsPerModelUnit, false, box.BoundaryNodes,
                placement => box.Contains(placement));

            if (restricted.Snapped)
                Assert.IsTrue(box.Contains(restricted.Placement), "吸到的位置一定要在盒內");
            Assert.AreNotEqual(unrestricted.Placement, restricted.Placement,
                "加了 isAllowed 之後不該再吸到那個出盒的落點");
        }

        [Test]
        public void 盒子_TrySnapToOrigin會擋掉盒外的落點()
        {
            Region box = ClassicSquareBox();

            Assert.IsFalse(SnapSolver.TrySnapToOrigin(
                PieceKind.SmallTriangle, 0, false, new Vector2(100f, 100f),
                placement => box.Contains(placement), out _),
                "遠在盒外的落點要被擋下來");

            Assert.IsTrue(SnapSolver.TrySnapToOrigin(
                PieceKind.SmallTriangle, 0, false, new Vector2(100f, 100f),
                null, out Placement unrestricted),
                "不傳判斷式時不做限制");
            Assert.AreEqual(new ExactVector2(100, 100), unrestricted.Translation);
        }

        [Test]
        public void 輸入來源_盤面變動會發事件()
        {
            var source = new ScriptedInputSource();
            var changedSlots = new List<int>();
            source.BoardChanged += (board, changedSlotIndex) => changedSlots.Add(changedSlotIndex);

            source.LoadPlacements(Fixtures.ClassicSquare());

            Assert.Greater(changedSlots.Count, 0);

            // 「動到哪一槽」也要一起轉發（2026-09-09 加的第二個參數）。
            // LoadPlacements 是先 Clear 再逐槽 Place，所以第一次是整盤、其餘是單槽
            Assert.AreEqual(BoardState.AllSlots, changedSlots[0], "Clear 要回報整盤");
            for (int i = 1; i < changedSlots.Count; i++)
                Assert.That(changedSlots[i], Is.InRange(0, BoardState.PieceCount - 1),
                            "逐槽 Place 要回報那一槽的索引");
        }

        [Test]
        public void 頂點快取_沒翻面時就是原型頂點()
        {
            foreach (PieceKind kind in System.Enum.GetValues(typeof(PieceKind)))
            {
                ExactVector2[] prototypeVertices = PiecePrototype.Get(kind).Vertices;
                Vector2[] cached = PieceVertexCache.LocalVertices(kind, false);

                Assert.AreEqual(prototypeVertices.Length, cached.Length, $"{kind} 頂點數不符");
                for (int i = 0; i < prototypeVertices.Length; i++)
                {
                    Assert.AreEqual(prototypeVertices[i].ToVector2().x, cached[i].x, 1e-5f);
                    Assert.AreEqual(prototypeVertices[i].ToVector2().y, cached[i].y, 1e-5f);
                }
            }
        }

        [Test]
        public void 頂點快取_翻面是y取負而且繞向仍是CCW()
        {
            foreach (PieceKind kind in System.Enum.GetValues(typeof(PieceKind)))
            {
                ExactVector2[] prototypeVertices = PiecePrototype.Get(kind).Vertices;
                Vector2[] flipped = PieceVertexCache.LocalVertices(kind, true);
                int vertexCount = prototypeVertices.Length;

                // 鏡射的同時反轉順序，所以 flipped[i] 對應 prototype[n−1−i]
                for (int i = 0; i < vertexCount; i++)
                {
                    Vector2 expected = Symmetry
                        .MirrorAcrossXAxis(prototypeVertices[vertexCount - 1 - i])
                        .ToVector2();
                    Assert.AreEqual(expected.x, flipped[i].x, 1e-5f, $"{kind} 第 {i} 點的 x");
                    Assert.AreEqual(expected.y, flipped[i].y, 1e-5f, $"{kind} 第 {i} 點的 y");
                }

                // shoelace 為正 = 逆時針。反轉順序就是為了保住這件事
                float doubleArea = 0f;
                for (int i = 0; i < vertexCount; i++)
                {
                    Vector2 current = flipped[i];
                    Vector2 next = flipped[(i + 1) % vertexCount];
                    doubleArea += current.x * next.y - next.x * current.y;
                }
                Assert.Greater(doubleArea, 0f, $"{kind} 翻面後繞向不是 CCW");
            }
        }

        [Test]
        public void 頂點快取_回傳副本_改了不會污染下一次()
        {
            Vector2[] first = PieceVertexCache.LocalVertices(PieceKind.Square, false);
            first[0] = new Vector2(999f, 999f);

            Vector2[] second = PieceVertexCache.LocalVertices(PieceKind.Square, false);
            Assert.AreNotEqual(999f, second[0].x, "回傳的必須是副本，不能是共用的那份");
        }

        [Test]
        public void 排序_面積階數是大的在下小的在上()
        {
            Assert.AreEqual(0, PieceSorting.SizeRank(PieceKind.LargeTriangle), "大三角最大，階數 0");
            Assert.AreEqual(2, PieceSorting.SizeRank(PieceKind.SmallTriangle), "小三角最小，階數最大");

            // 中三角、正方形、平行四邊形面積都是 4，階數必須一樣，
            // 否則同面積的片會被拆到不同層，間距就不夠用了
            Assert.AreEqual(PieceSorting.SizeRank(PieceKind.MediumTriangle),
                            PieceSorting.SizeRank(PieceKind.Square));
            Assert.AreEqual(PieceSorting.SizeRank(PieceKind.MediumTriangle),
                            PieceSorting.SizeRank(PieceKind.Parallelogram));
        }

        [Test]
        public void 排序_拖曳中在未吸附之上_未吸附在已吸附之上()
        {
            int dragging = PieceSorting.SortingOrder(PieceKind.LargeTriangle, 0, true, true);
            int free = PieceSorting.SortingOrder(PieceKind.SmallTriangle, 3, false, false);
            int snapped = PieceSorting.SortingOrder(PieceKind.SmallTriangle, 3, true, false);

            // 用最大的片當拖曳中、最小的片當未吸附，確認層的優先權蓋過面積
            Assert.Greater(dragging, free, "拖曳中要壓過所有未吸附的，否則會鑽到別片下面");
            Assert.Greater(free, snapped, "未吸附的要壓過已吸附的");
        }

        [Test]
        public void 排序_同層小片壓在大片上面()
        {
            int large = PieceSorting.SortingOrder(PieceKind.LargeTriangle, 0, false, false);
            int medium = PieceSorting.SortingOrder(PieceKind.MediumTriangle, 2, false, false);
            int small = PieceSorting.SortingOrder(PieceKind.SmallTriangle, 3, false, false);

            Assert.Greater(small, medium);
            Assert.Greater(medium, large);
        }

        [Test]
        public void 排序_同種片用槽索引破同分()
        {
            // 兩片大三角是槽 0 與 1，兩片小三角是槽 3 與 4。
            // 值相同的話同層兩片繪製順序未定義，畫面會閃爍
            Assert.AreNotEqual(PieceSorting.SortingOrder(PieceKind.LargeTriangle, 0, false, false),
                               PieceSorting.SortingOrder(PieceKind.LargeTriangle, 1, false, false));
            Assert.AreNotEqual(PieceSorting.SortingOrder(PieceKind.SmallTriangle, 3, false, false),
                               PieceSorting.SortingOrder(PieceKind.SmallTriangle, 4, false, false));
        }

        [Test]
        public void 排序_七片任意狀態都不會撞號()
        {
            var seen = new HashSet<int>();
            for (int slotIndex = 0; slotIndex < BoardState.PieceCount; slotIndex++)
            {
                PieceKind kind = PiecePrototype.TangramSet[slotIndex];
                foreach (bool snapped in new[] { false, true })
                {
                    foreach (bool dragged in new[] { false, true })
                    {
                        int order = PieceSorting.SortingOrder(kind, slotIndex, snapped, dragged);

                        // 同一片的不同狀態本來就可能同號（拖曳中的層不看吸附與否），
                        // 這裡要確認的是「同一個狀態組合下七片互不相同」，所以用 (片, 狀態) 當鍵
                        Assert.IsTrue(seen.Add(order * 4 + (snapped ? 2 : 0) + (dragged ? 1 : 0)),
                                      $"槽 {slotIndex} 的排序值撞號了");

                        // ⚠️ 這裡原本還斷言「order 要大於 BoxSortingOrder」，
                        // **2026-09-09 拿掉了**：片跟盒子已經不在同一個 sorting layer
                        //（Box → Piece），兩者的 order 根本不會被拿來比。
                        // 留著的話它測的是一個不再成立的前提，而且會擋住
                        // 「把層內的 order 從 0 開始算」這種正常的整理
                        Assert.GreaterOrEqual(order, 0, "sortingOrder 不該是負的");
                    }
                }
            }
        }

        [Test]
        public void 頂點快取_視覺中心就是頂點平均()
        {
            foreach (PieceKind kind in System.Enum.GetValues(typeof(PieceKind)))
            {
                Vector2[] vertices = PieceVertexCache.LocalVertices(kind, false);
                Vector2 expected = Vector2.zero;
                foreach (Vector2 vertex in vertices) expected += vertex;
                expected /= vertices.Length;

                Vector2 actual = PieceVertexCache.LocalCenter(kind);
                Assert.AreEqual(expected.x, actual.x, 1e-5f, $"{kind} 的中心 x");
                Assert.AreEqual(expected.y, actual.y, 1e-5f, $"{kind} 的中心 y");
            }
        }

        [Test]
        public void 頂點快取_視覺中心真的在片內部()
        {
            // 中心是旋轉的軸心。跑到形狀外面的話，轉起來會像繞著遠處的一個點公轉
            foreach (PieceKind kind in System.Enum.GetValues(typeof(PieceKind)))
            {
                Vector2[] vertices = PieceVertexCache.LocalVertices(kind, false);
                Vector2 center = PieceVertexCache.LocalCenter(kind);

                for (int i = 0; i < vertices.Length; i++)
                {
                    Vector2 edge = vertices[(i + 1) % vertices.Length] - vertices[i];
                    float cross = edge.x * (center.y - vertices[i].y)
                                - edge.y * (center.x - vertices[i].x);
                    // 頂點環是 CCW，所以內部在每條邊的左側（叉積為正）
                    Assert.Greater(cross, 0f, $"{kind} 的中心跑到第 {i} 條邊外面了");
                }
            }
        }

        [Test]
        public void 頂點快取_沒有污染原型單例()
        {
            // 建表時讀過共用單例，讀完之後原型必須原封不動（翻面是產生新陣列，不是就地改）
            ExactVector2[] vertices = PiecePrototype.Get(PieceKind.Parallelogram).Vertices;
            Assert.AreEqual(1, Geometry.SignedDoubleArea(vertices).Sign,
                "原型仍必須是 CCW——被就地鏡射過的話這裡會變成負的");
        }
    }
}
