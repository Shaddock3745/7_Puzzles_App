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
