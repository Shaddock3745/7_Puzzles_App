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
        private static SnapSolver.Settings FiftyPixelsPerUnit => SnapSolver.Settings.Default(50f);

        [Test]
        public void 吸附_接近合法落點時吸上去且座標精確()
        {
            var placed = new[] { BigTriangle };
            // 正確落點是 T = (0,2)，手指停在差 0.05 單位（= 3.5px）的地方
            SnapSolver.DragResult result = SnapSolver.Resolve(
                PieceKind.SmallTriangle, 4, false, new Vector2(0.05f, 1.95f), placed, FiftyPixelsPerUnit);

            Assert.IsTrue(result.Snapped);
            Assert.AreEqual(new ExactVector2(0, 2), result.Placement.Translation);
            Assert.AreEqual(0, result.Placement.Translation.X.Root2Coefficient, "吸附結果必須是精確整數，不能有殘留");
        }

        [Test]
        public void 吸附_太遠就保持自由拖曳()
        {
            var placed = new[] { BigTriangle };
            SnapSolver.DragResult result = SnapSolver.Resolve(
                PieceKind.SmallTriangle, 4, false, new Vector2(20f, 20f), placed, FiftyPixelsPerUnit);

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

            var looseCandidates = SnapSolver.Candidates(PieceKind.SmallTriangle, 4, false, fingerPosition, placed, FiftyPixelsPerUnit, false);
            var stickyCandidates = SnapSolver.Candidates(PieceKind.SmallTriangle, 4, false, fingerPosition, placed, FiftyPixelsPerUnit, true);

            Assert.IsFalse(ContainsTargetTranslation(looseCandidates), "還沒吸附時 25px 應該吸不到");
            Assert.IsTrue(ContainsTargetTranslation(stickyCandidates), "已經吸附時 25px 應該還黏著");
        }

        [Test]
        public void 吸附_不會吸到會重疊的位置()
        {
            var placed = new[] { BigTriangle };
            // 大三角內部：所有候選都會重疊，應該落空
            SnapSolver.DragResult result = SnapSolver.Resolve(
                PieceKind.SmallTriangle, 0, false, new Vector2(1f, 1f), placed, FiftyPixelsPerUnit);
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
                new Placement[0], FiftyPixelsPerUnit);
            Assert.AreEqual(0, withoutBox.Count, "沒有錨點來源時不該有候選");

            var withBox = SnapSolver.Candidates(
                PieceKind.SmallTriangle, 0, false, SnapSolver.ToUnityVector2(corner),
                new Placement[0], FiftyPixelsPerUnit, false, box.BoundaryNodes);
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
                new Placement[0], FiftyPixelsPerUnit, false, box.BoundaryNodes);

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
                new Placement[0], FiftyPixelsPerUnit, false, box.BoundaryNodes);

            Assert.IsTrue(unrestricted.Snapped, "不設限時應該吸得到最近的候選");
            Assert.IsFalse(box.Contains(unrestricted.Placement),
                "而且那個最近的候選確實在盒外——這樣下面的對照才有意義");

            SnapSolver.DragResult restricted = SnapSolver.Resolve(
                PieceKind.LargeTriangle, 4, false, finger,
                new Placement[0], FiftyPixelsPerUnit, false, box.BoundaryNodes,
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
    }
}
