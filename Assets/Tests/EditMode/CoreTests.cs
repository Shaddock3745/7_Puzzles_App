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

        [Test]
        public void 驗證_拼出大正方形算過關()
        {
            BoardState board = SolvedBoard();
            Puzzle target = Puzzle.FromPlacements(Fixtures.ClassicSquare());
            ValidationResult result = PuzzleValidator.Check(board, target);
            Assert.AreEqual(ValidationState.Solved, result.State, result.ToString());
            Assert.AreEqual(target.Key, result.ShapeKey);
        }

        [Test]
        public void 驗證_整體旋轉鏡射後仍算過關()
        {
            Puzzle target = Puzzle.FromPlacements(Fixtures.ClassicSquare());
            foreach (var (rotation, flipped) in Symmetry.GlobalTransforms)
            {
                var moved = new List<Placement>();
                foreach (Placement piece in Fixtures.ClassicSquare()) moved.Add(piece.ApplyGlobal(rotation, flipped));
                Assert.AreEqual(ValidationState.Solved,
                    PuzzleValidator.Check(moved, target).State, $"rotation={rotation} flipped={flipped}");
            }
        }

        [Test]
        public void 驗證_少一片是Incomplete()
        {
            BoardState board = SolvedBoard();
            board.Lift(0);
            ValidationResult result = PuzzleValidator.Check(board, Puzzle.FromPlacements(Fixtures.ClassicSquare()));
            Assert.AreEqual(ValidationState.Incomplete, result.State);
            Assert.AreEqual(6, result.PlacedCount);
        }

        [Test]
        public void 驗證_有片飄走是Disconnected()
        {
            BoardState board = SolvedBoard();
            Placement piece = board[0].Placement;
            board.Place(0, piece.Translated(new ExactVector2(100, 100)));
            ValidationResult result = PuzzleValidator.Check(board, Puzzle.FromPlacements(Fixtures.ClassicSquare()));
            Assert.AreEqual(ValidationState.Disconnected, result.State);
        }

        [Test]
        public void 驗證_形狀不對是WrongShape()
        {
            // 拿另一個形狀當題目：把正方形的解當盤面，題目卻是別的
            BoardState board = SolvedBoard();
            var other = new Puzzle("不存在的題目key", new Walk[0], PuzzleSource.Generated);
            ValidationResult result = PuzzleValidator.Check(board, other);
            Assert.AreEqual(ValidationState.WrongShape, result.State);
            Assert.IsNotNull(result.ShapeKey);
        }

        [Test]
        public void 驗證_過關後可以收成一筆解()
        {
            BoardState board = SolvedBoard();
            Arrangement arrangement = PuzzleValidator.ToArrangement(board);
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
            int fired = 0;
            source.BoardChanged += _ => fired++;
            source.LoadPlacements(Fixtures.ClassicSquare());
            Assert.Greater(fired, 0);
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
                        Assert.GreaterOrEqual(order, PieceSorting.BoxSortingOrder + 1,
                                              "所有片都要在盒子之上");
                    }
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
