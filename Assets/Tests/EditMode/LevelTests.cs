using System.Collections.Generic;
using NUnit.Framework;
using Tangram.Core;
using Tangram.Data;

namespace Tangram.Core.Tests
{
    public class LevelTests
    {
        /// <returns>經典大正方形的七片與對應題目。</returns>
        private static (Puzzle puzzle, Placement[] solution) ClassicLevel()
        {
            Placement[] solution = Fixtures.ClassicSquare();
            return (Puzzle.FromPlacements(solution), solution);
        }

        /// <summary>把經典大正方形整組平移，得到一個形狀相同但位置不同的第二關。</summary>
        /// <param name="delta">平移量。</param>
        /// <returns>平移後的七片與對應題目。</returns>
        private static (Puzzle puzzle, Placement[] solution) TranslatedLevel(ExactVector2 delta)
        {
            Placement[] original = Fixtures.ClassicSquare();
            var moved = new Placement[original.Length];
            for (int i = 0; i < original.Length; i++) moved[i] = original[i].Translated(delta);
            return (Puzzle.FromPlacements(moved), moved);
        }

        [Test]
        public void 關卡_一開始沒有載入任何關()
        {
            var session = new LevelSession();
            Assert.IsFalse(session.HasLevel);
            Assert.IsNull(session.Data);
        }

        [Test]
        public void 關卡_載入之後盤面是空的而且區域對得上()
        {
            var session = new LevelSession();
            var (puzzle, solution) = ClassicLevel();

            session.Load(puzzle, solution, allowRoll: true);

            Assert.IsTrue(session.HasLevel);
            Assert.AreEqual(0, session.Data.Board.OnBoardCount, "剛載入的盤面必須是空的");
            Assert.AreEqual(7, session.Data.ReferenceSolution.Length);
            Assert.IsTrue(session.Data.BoxRegion.Contains(solution[0]),
                          "參考解的片必須落在盒子區域內——對不上的話是原點或平移錯了");
        }

        [Test]
        public void 關卡_盒子原點目前是零而且不依賴任何狀態()
        {
            var (puzzle, _) = ClassicLevel();

            // 連續呼叫必須給同一個答案：它是純函式，不准讀任何欄位
            Assert.AreEqual(ExactVector2.Zero, LevelSession.BoxOriginFor(puzzle));
            Assert.AreEqual(ExactVector2.Zero, LevelSession.BoxOriginFor(puzzle));
        }

        [Test]
        public void 關卡_原點是零時參考解不會被平移()
        {
            var session = new LevelSession();
            var (puzzle, solution) = ClassicLevel();

            session.Load(puzzle, solution, allowRoll: true);

            for (int i = 0; i < solution.Length; i++)
                Assert.AreEqual(solution[i].Translation, session.Data.ReferenceSolution[i].Translation);
        }

        /// <summary>
        /// 換關必須把每一項都歸零。**這是「重置不是重建」那個決定的保險**——
        /// 之後有人往 <c>LevelData</c> 加了欄位卻忘記納入重置，這個測試會紅。
        /// </summary>
        [Test]
        public void 關卡_換關之後所有狀態都回到初始()
        {
            var session = new LevelSession();
            var (puzzleA, solutionA) = ClassicLevel();
            var (puzzleB, solutionB) = TranslatedLevel(new ExactVector2(20, 20));

            session.Load(puzzleA, solutionA, allowRoll: true);
            LevelData dataA = session.Data;
            BoardState boardA = dataA.Board;

            // 把能動的全部動一遍
            session.Data.Board.Place(0, solutionA[0]);
            session.Data.Board.Place(1, solutionA[1]);
            session.Data.UseHint();
            session.Data.UseHint();
            session.Data.MarkSolved();

            Assert.AreEqual(2, session.Data.Board.OnBoardCount, "前置動作沒生效的話這個測試沒有意義");

            session.Load(puzzleB, solutionB, allowRoll: false);

            Assert.AreNotSame(dataA, session.Data, "換關必須換掉整包，不是就地清空");
            Assert.AreNotSame(boardA, session.Data.Board, "盤面也在那一包裡，要跟著換");

            Assert.AreEqual(0, session.Data.Board.OnBoardCount, "盤面沒清乾淨");
            Assert.AreEqual(0, session.Data.HintsUsed, "提示次數沒歸零");
            Assert.IsFalse(session.Data.IsSolved, "過關旗標沒歸零");
            Assert.IsFalse(session.Data.AllowRoll, "allowRoll 沒換成新關的值");
            Assert.AreNotEqual(dataA.BoxRegion.Bounds.Minimum,
                               session.Data.BoxRegion.Bounds.Minimum, "區域沒換成新關的");
        }

        [Test]
        public void 關卡_舊的盤面在換關後與新的完全無關()
        {
            var session = new LevelSession();
            var (puzzleA, solutionA) = ClassicLevel();
            var (puzzleB, solutionB) = TranslatedLevel(new ExactVector2(20, 20));

            session.Load(puzzleA, solutionA, allowRoll: true);
            BoardState staleBoard = session.Data.Board;   // 這正是「不准存成欄位」在講的東西

            session.Load(puzzleB, solutionB, allowRoll: true);
            staleBoard.Place(0, solutionA[0]);

            Assert.AreEqual(0, session.Data.Board.OnBoardCount,
                            "往舊盤面寫不該影響新的一關——這就是快取內部物件會出事的原因");
        }

        [Test]
        public void 關卡_離開之後沒有關卡資料()
        {
            var session = new LevelSession();
            var (puzzle, solution) = ClassicLevel();

            session.Load(puzzle, solution, allowRoll: true);
            session.Unload();

            Assert.IsFalse(session.HasLevel);
            Assert.IsNull(session.Data);
        }
    }
}
