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

            session.Load(puzzle, solution, difficulty: 1200, allowRoll: true);

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

            session.Load(puzzle, solution, difficulty: 1200, allowRoll: true);

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

            session.Load(puzzleA, solutionA, difficulty: 1200, allowRoll: true);
            LevelData dataA = session.Data;
            BoardState boardA = dataA.Board;

            // 把能動的全部動一遍
            session.Data.Board.Place(0, solutionA[0]);
            session.Data.Board.Place(1, solutionA[1]);
            session.Data.StartForegroundTiming(0.0);
            Assert.IsTrue(session.Data.TryUseHint(100.0, 45.0, out _));
            Assert.IsTrue(session.Data.TryUseHint(100.0, 45.0, out _));
            session.Data.MarkSolved();

            Assert.AreEqual(2, session.Data.Board.OnBoardCount, "前置動作沒生效的話這個測試沒有意義");

            session.Load(puzzleB, solutionB, difficulty: 1200, allowRoll: false);

            Assert.AreNotSame(dataA, session.Data, "換關必須換掉整包，不是就地清空");
            Assert.AreNotSame(boardA, session.Data.Board, "盤面也在那一包裡，要跟著換");

            Assert.AreEqual(0, session.Data.Board.OnBoardCount, "盤面沒清乾淨");
            Assert.AreEqual(0, session.Data.HintsUsed, "提示次數沒歸零");
            Assert.AreEqual(0, session.Data.HintedReferenceSlots.Count, "提示過的片沒歸零（覆蓋片會留到下一關）");
            Assert.AreEqual(0.0, session.Data.FirstHintForegroundSeconds, "第一次提示的秒數沒歸零");
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

            session.Load(puzzleA, solutionA, difficulty: 1200, allowRoll: true);
            BoardState staleBoard = session.Data.Board;   // 這正是「不准存成欄位」在講的東西

            session.Load(puzzleB, solutionB, difficulty: 1200, allowRoll: true);
            staleBoard.Place(0, solutionA[0]);

            Assert.AreEqual(0, session.Data.Board.OnBoardCount,
                            "往舊盤面寫不該影響新的一關——這就是快取內部物件會出事的原因");
        }

        [Test]
        public void 關卡_離開之後沒有關卡資料()
        {
            var session = new LevelSession();
            var (puzzle, solution) = ClassicLevel();

            session.Load(puzzle, solution, difficulty: 1200, allowRoll: true);
            session.Unload();

            Assert.IsFalse(session.HasLevel);
            Assert.IsNull(session.Data);
        }

        // ─────────────────────────────────────────────────────────
        // 待用區（P1-5）。「片在不在抽屜裡」的真相在 LevelData，
        // PieceView 的顯示與否只是它的投影
        // ─────────────────────────────────────────────────────────

        [Test]
        public void 待用區_一關開始時七片都在抽屜裡()
        {
            var session = new LevelSession();
            var (puzzle, solution) = ClassicLevel();
            session.Load(puzzle, solution, difficulty: 1200, allowRoll: true);

            for (int slotIndex = 0; slotIndex < BoardState.PieceCount; slotIndex++)
                Assert.IsTrue(session.Data.IsInTray(slotIndex), $"第 {slotIndex} 槽應該在待用區");

            Assert.AreEqual(0, session.Data.OutOfTrayCount);
        }

        [Test]
        public void 待用區_拿出來再放回去()
        {
            var session = new LevelSession();
            var (puzzle, solution) = ClassicLevel();
            session.Load(puzzle, solution, difficulty: 1200, allowRoll: true);

            session.Data.SetInTray(3, false);
            Assert.IsFalse(session.Data.IsInTray(3));
            Assert.AreEqual(1, session.Data.OutOfTrayCount, "只有第 3 槽被拿出來");

            session.Data.SetInTray(3, true);
            Assert.IsTrue(session.Data.IsInTray(3));
            Assert.AreEqual(0, session.Data.OutOfTrayCount);
        }

        [Test]
        public void 待用區_還在盤上的片不能直接收回抽屜()
        {
            var session = new LevelSession();
            var (puzzle, solution) = ClassicLevel();
            session.Load(puzzle, solution, difficulty: 1200, allowRoll: true);

            session.Data.SetInTray(0, false);
            session.Data.Board.Place(0, solution[0]);

            // 「在盤上」與「在待用區」互斥。默默修正的話症狀是「片在抽屜裡但盤面
            // 認為它還在盒內」，過關判定會跟著錯而且不報錯，所以這裡要吵
            Assert.Throws<System.InvalidOperationException>(
                () => session.Data.SetInTray(0, true),
                "先 Lift 再送回待用區才是對的順序");

            // 先 Lift 就可以了
            session.Data.Board.Lift(0);
            Assert.DoesNotThrow(() => session.Data.SetInTray(0, true));
            Assert.IsTrue(session.Data.IsInTray(0));
        }

        [Test]
        public void 待用區_換關之後七片自動回到抽屜()
        {
            var session = new LevelSession();
            var (puzzleA, solutionA) = ClassicLevel();
            session.Load(puzzleA, solutionA, difficulty: 1200, allowRoll: true);

            // 這一關把四片拿出來，其中兩片放上盤面
            for (int slotIndex = 0; slotIndex < 4; slotIndex++) session.Data.SetInTray(slotIndex, false);
            session.Data.Board.Place(0, solutionA[0]);
            session.Data.Board.Place(1, solutionA[1]);
            Assert.AreEqual(4, session.Data.OutOfTrayCount);

            var (puzzleB, solutionB) = TranslatedLevel(new ExactVector2(6, 0));
            session.Load(puzzleB, solutionB, difficulty: 1200, allowRoll: true);

            // 換關是整包換掉，所以待用區不必另外重置——這條測試就是釘住那件事。
            // 之後往 LevelData 新增 per-關 欄位時，這個性質也要跟著成立
            Assert.AreEqual(0, session.Data.OutOfTrayCount, "換關之後七片都該回到抽屜");
            Assert.AreEqual(0, session.Data.Board.OnBoardCount);
        }

        // ─────────────────────────────────────────────────────────────
        // undo（P1-9）。一步的定義是「只有放進盤面算」，記的是**動之前**那一槽的樣子。
        //
        // 這裡測得到的是模型那一半；畫面同步在 TouchInputController，dotnet 編不到。
        // ─────────────────────────────────────────────────────────────

        /// <summary>載一關，並把第 slotIndex 片從抽屜拿出來放到盤上（含推 undo 步驟）。</summary>
        /// <param name="session">要操作的 session。</param>
        /// <param name="slotIndex">槽索引。</param>
        /// <param name="placement">要放的擺放。</param>
        private static void PlaceFromTray(LevelSession session, int slotIndex, Placement placement)
        {
            session.Data.SetInTray(slotIndex, false);
            session.Data.PushUndoStep(session.Data.Board[slotIndex]);   // 動之前：不在盤上
            session.Data.Board.Place(slotIndex, placement);
        }

        [Test]
        public void undo_一關剛開始沒有東西可以退()
        {
            var session = new LevelSession();
            var (puzzle, solution) = ClassicLevel();
            session.Load(puzzle, solution, difficulty: 1200, allowRoll: true);

            Assert.IsFalse(session.Data.CanUndo);
            Assert.IsFalse(session.Data.TryUndo(out var restored));
            Assert.IsNull(restored);
        }

        [Test]
        public void undo_退回一次放置_片回到待用區而且姿態不動()
        {
            var session = new LevelSession();
            var (puzzle, solution) = ClassicLevel();
            session.Load(puzzle, solution, difficulty: 1200, allowRoll: true);

            PlaceFromTray(session, 0, solution[0]);
            Assert.IsTrue(session.Data.CanUndo);

            Assert.IsTrue(session.Data.TryUndo(out var restored));

            Assert.IsFalse(session.Data.Board[0].OnBoard, "退回去之後不該還在盤上");
            Assert.IsTrue(session.Data.IsInTray(0), "「放進盤面」的反面是回到待用區");
            Assert.AreEqual(1, restored.Count);
            Assert.IsFalse(restored[0].OnBoard, "回傳的是**動之前**的樣子，給控制器套用到畫面");
            Assert.IsFalse(session.Data.CanUndo, "只推了一步，退完就沒了");
        }

        [Test]
        public void undo_退回一次移動_片回到舊位置而且仍在盤上()
        {
            var session = new LevelSession();
            var (puzzle, solution) = ClassicLevel();
            session.Load(puzzle, solution, difficulty: 1200, allowRoll: true);

            Placement positionA = solution[0];
            Placement positionB = solution[0].Translated(new ExactVector2(3, 3));

            PlaceFromTray(session, 0, positionA);

            // 第二步：盤上 → 盤上另一個位置
            session.Data.PushUndoStep(session.Data.Board[0]);            // 動之前：在盤上 positionA
            session.Data.Board.Place(0, positionB);

            Assert.IsTrue(session.Data.TryUndo(out var restored));

            Assert.IsTrue(session.Data.Board[0].OnBoard, "移動的反面仍然在盤上，不是回抽屜");
            Assert.AreEqual(positionA, session.Data.Board[0].Placement);
            Assert.IsTrue(restored[0].OnBoard);
            Assert.IsTrue(session.Data.CanUndo, "還剩第一步沒退");
        }

        [Test]
        public void undo_退回移動時要順手清掉待用區旗標()
        {
            // ⚠️ 這一條釘住一個**安靜的**洞：Place 不檢查待用區旗標。
            // 少了那一步的話，模型認為片在盤上、旗標卻還是 true，
            // TrayView 會把它藏起來 → 症狀是「undo 之後片不見了，
            // 但過關判定認為盒子裡有它」，而且完全不報錯。
            var session = new LevelSession();
            var (puzzle, solution) = ClassicLevel();
            session.Load(puzzle, solution, difficulty: 1200, allowRoll: true);

            Placement positionA = solution[0];
            Placement positionB = solution[0].Translated(new ExactVector2(3, 3));

            PlaceFromTray(session, 0, positionA);
            session.Data.PushUndoStep(session.Data.Board[0]);
            session.Data.Board.Place(0, positionB);

            // 玩家把它拖進回收框：先 Lift 再收回抽屜（這一步不推，不算 undo 的一步）
            session.Data.Board.Lift(0);
            session.Data.SetInTray(0, true);

            Assert.IsTrue(session.Data.TryUndo(out _));

            Assert.IsTrue(session.Data.Board[0].OnBoard);
            Assert.IsFalse(session.Data.IsInTray(0),
                           "回到盤上的片一定不在待用區——這兩個狀態互斥");
        }

        [Test]
        public void undo_清空是一步七筆_退一次全部回來()
        {
            var session = new LevelSession();
            var (puzzle, solution) = ClassicLevel();
            session.Load(puzzle, solution, difficulty: 1200, allowRoll: true);

            for (int slotIndex = 0; slotIndex < BoardState.PieceCount; slotIndex++)
                PlaceFromTray(session, slotIndex, solution[slotIndex]);
            Assert.IsTrue(session.Data.Board.AllOnBoard);

            // 清空：七槽動之前的樣子當成**一步**推進去，然後整盤拿起來 + 送回抽屜
            var before = new PieceObservation[BoardState.PieceCount];
            for (int slotIndex = 0; slotIndex < BoardState.PieceCount; slotIndex++)
                before[slotIndex] = session.Data.Board[slotIndex];
            session.Data.PushUndoStep(before);
            session.Data.Board.Clear();
            for (int slotIndex = 0; slotIndex < BoardState.PieceCount; slotIndex++)
                session.Data.SetInTray(slotIndex, true);

            Assert.IsTrue(session.Data.TryUndo(out var restored));

            Assert.AreEqual(BoardState.PieceCount, restored.Count, "七筆是**一步**，不是七步");
            Assert.IsTrue(session.Data.Board.AllOnBoard, "退一次就全部回到盤上");
            Assert.AreEqual(BoardState.PieceCount, session.Data.OutOfTrayCount,
                            "回到盤上的片都該離開待用區");

            // 下面還剩七片各自的「放進盤面」那七步，清空只佔一步
            for (int step = 0; step < BoardState.PieceCount; step++)
                Assert.IsTrue(session.Data.TryUndo(out _), $"第 {step} 片的放置還沒退");
            Assert.IsFalse(session.Data.CanUndo);
        }

        [Test]
        public void undo_同一片在堆疊裡有多筆_每一筆各自獨立()
        {
            // 記「狀態」而不是「動作」的理由：撤銷較早那筆時，
            // 片可能已經不在當初的地方了，但每一筆自己就帶著完整答案
            var session = new LevelSession();
            var (puzzle, solution) = ClassicLevel();
            session.Load(puzzle, solution, difficulty: 1200, allowRoll: true);

            Placement positionA = solution[0];
            Placement positionB = solution[0].Translated(new ExactVector2(3, 3));

            PlaceFromTray(session, 0, positionA);                        // 第 1 步：{不在盤上}
            session.Data.PushUndoStep(session.Data.Board[0]);            // 第 2 步：{在盤上, A}
            session.Data.Board.Place(0, positionB);

            session.Data.TryUndo(out _);                                 // 退第 2 步 → 回到 A
            Assert.AreEqual(positionA, session.Data.Board[0].Placement);

            session.Data.TryUndo(out _);                                 // 退第 1 步 → 回抽屜
            Assert.IsFalse(session.Data.Board[0].OnBoard);
            Assert.IsTrue(session.Data.IsInTray(0));
        }

        [Test]
        public void undo_退一步只觸發一次Changed()
        {
            // 過關判定與中途回饋都要綁 Changed。一步發多次的話它們會對著
            // 「這一步的中間狀態」各算一次，而那些狀態在遊戲裡從來不存在
            var session = new LevelSession();
            var (puzzle, solution) = ClassicLevel();
            session.Load(puzzle, solution, difficulty: 1200, allowRoll: true);

            for (int slotIndex = 0; slotIndex < BoardState.PieceCount; slotIndex++)
                PlaceFromTray(session, slotIndex, solution[slotIndex]);

            var before = new PieceObservation[BoardState.PieceCount];
            for (int slotIndex = 0; slotIndex < BoardState.PieceCount; slotIndex++)
                before[slotIndex] = session.Data.Board[slotIndex];
            session.Data.PushUndoStep(before);
            session.Data.Board.Clear();
            for (int slotIndex = 0; slotIndex < BoardState.PieceCount; slotIndex++)
                session.Data.SetInTray(slotIndex, true);

            int changedCount = 0;
            session.Data.Board.Changed += (board, slotIndex) => changedCount++;

            session.Data.TryUndo(out _);

            Assert.AreEqual(1, changedCount, "七片一起回來也只算一次盤面變動");
        }

        [Test]
        public void undo_換關之後堆疊自動清空()
        {
            // 忘了清的症狀是「玩家在新的一關按 undo，上一關的片被拉回來」，不報錯。
            // 堆疊放 LevelData（換關整包換掉）就不可能忘記——這條釘住那件事
            var session = new LevelSession();
            var (puzzleA, solutionA) = ClassicLevel();
            session.Load(puzzleA, solutionA, difficulty: 1200, allowRoll: true);

            PlaceFromTray(session, 0, solutionA[0]);
            Assert.IsTrue(session.Data.CanUndo);

            var (puzzleB, solutionB) = TranslatedLevel(new ExactVector2(6, 0));
            session.Load(puzzleB, solutionB, difficulty: 1200, allowRoll: true);

            Assert.IsFalse(session.Data.CanUndo, "新的一關不該退得回上一關");
        }

        // ─────────────────────────────────────────────────────────────
        // 清空（P1-9）。定義是「把**盤面上**放好的片推回待用區」，暫放的片不動。
        //
        // 動到的集合＝記進 undo 那一步的集合，所以 undo 是精確還原。
        // ─────────────────────────────────────────────────────────────

        [Test]
        public void 清空_盤上的片回待用區_暫放的片不動()
        {
            var session = new LevelSession();
            var (puzzle, solution) = ClassicLevel();
            session.Load(puzzle, solution, difficulty: 1200, allowRoll: true);

            PlaceFromTray(session, 0, solution[0]);
            PlaceFromTray(session, 1, solution[1]);

            // 第 2 片拿出抽屜但沒放上盤面 = 暫放（純浮點，不進 BoardState）
            session.Data.SetInTray(2, false);

            Assert.IsTrue(session.Data.TryClearBoardToTray(out var cleared));

            Assert.AreEqual(2, cleared.Count, "只有盤上那兩片被清");
            Assert.AreEqual(0, session.Data.Board.OnBoardCount);
            Assert.IsTrue(session.Data.IsInTray(0));
            Assert.IsTrue(session.Data.IsInTray(1));
            Assert.IsFalse(session.Data.IsInTray(2), "暫放的片不在清空的定義裡，不該被收走");
        }

        [Test]
        public void 清空_盤面本來就空的時候不算一步()
        {
            var session = new LevelSession();
            var (puzzle, solution) = ClassicLevel();
            session.Load(puzzle, solution, difficulty: 1200, allowRoll: true);
            session.Data.SetInTray(3, false);           // 只有一片暫放，盤上是空的

            Assert.IsFalse(session.Data.TryClearBoardToTray(out var cleared));
            Assert.IsNull(cleared);
            Assert.IsFalse(session.Data.CanUndo, "不能壓進一個退了等於沒退的空步驟");
        }

        [Test]
        public void 清空_undo一次就整組還原回原位()
        {
            // 清空動到的集合＝記下來的集合，所以這裡是**精確**還原，
            // 不是「大致上回來了」。這一條就是那個設計選擇的價值
            var session = new LevelSession();
            var (puzzle, solution) = ClassicLevel();
            session.Load(puzzle, solution, difficulty: 1200, allowRoll: true);

            for (int slotIndex = 0; slotIndex < 3; slotIndex++)
                PlaceFromTray(session, slotIndex, solution[slotIndex]);
            session.Data.SetInTray(4, false);           // 一片暫放，清空不該碰它

            session.Data.TryClearBoardToTray(out _);
            Assert.IsTrue(session.Data.TryUndo(out var restored));

            Assert.AreEqual(3, restored.Count, "清空是**一步**，退一次就全回來");
            for (int slotIndex = 0; slotIndex < 3; slotIndex++)
            {
                Assert.IsTrue(session.Data.Board[slotIndex].OnBoard, $"第 {slotIndex} 片該回盤上");
                Assert.AreEqual(solution[slotIndex], session.Data.Board[slotIndex].Placement,
                                "位置要跟清空之前一模一樣");
                Assert.IsFalse(session.Data.IsInTray(slotIndex),
                               "回到盤上的片一定不在待用區——這兩個狀態互斥");
            }
            Assert.IsFalse(session.Data.IsInTray(4), "暫放的那片從頭到尾沒被動過");
        }

        [Test]
        public void 清空_只觸發一次Changed()
        {
            var session = new LevelSession();
            var (puzzle, solution) = ClassicLevel();
            session.Load(puzzle, solution, difficulty: 1200, allowRoll: true);
            for (int slotIndex = 0; slotIndex < BoardState.PieceCount; slotIndex++)
                PlaceFromTray(session, slotIndex, solution[slotIndex]);

            int changedCount = 0;
            session.Data.Board.Changed += (board, slotIndex) => changedCount++;

            session.Data.TryClearBoardToTray(out _);

            Assert.AreEqual(1, changedCount, "七片一起收走也只算一次盤面變動");
        }

        // ─────────────────────────────────────────────────────────────
        // 過關判定（P1-10）。判定就是 AllOnBoard——它等價於面積判定，
        // 靠的是吸附的硬限制（只吸得進盒內、不能重疊）。
        // ─────────────────────────────────────────────────────────────

        [Test]
        public void 過關_七片都在盤上才算過關()
        {
            var session = new LevelSession();
            var (puzzle, solution) = ClassicLevel();
            session.Load(puzzle, solution, difficulty: 1200, allowRoll: true);

            for (int slotIndex = 0; slotIndex < BoardState.PieceCount - 1; slotIndex++)
            {
                PlaceFromTray(session, slotIndex, solution[slotIndex]);
                Assert.IsFalse(session.Data.MarkSolvedIfAllOnBoard(),
                               $"才放了 {slotIndex + 1} 片，不該過關");
            }

            PlaceFromTray(session, BoardState.PieceCount - 1, solution[BoardState.PieceCount - 1]);
            Assert.IsTrue(session.Data.MarkSolvedIfAllOnBoard(), "第七片放上去就該過關");
            Assert.IsTrue(session.Data.IsSolved);
        }

        [Test]
        public void 過關_一關剛開始不算過關()
        {
            var session = new LevelSession();
            var (puzzle, solution) = ClassicLevel();
            session.Load(puzzle, solution, difficulty: 1200, allowRoll: true);

            Assert.IsFalse(session.Data.IsSolved);
            Assert.IsFalse(session.Data.MarkSolvedIfAllOnBoard());
        }

        [Test]
        public void 過關_是單向的_拿掉一片也不會變回未過關()
        {
            // 過關之後彈窗蓋滿畫面，玩家只剩「退出」與「下一關」兩個出口，
            // 所以不需要「退回未過關」那條路。這一條把那個假設釘住——
            // 之後要是加了「關掉彈窗繼續看」，這條會紅，提醒你回來重想
            var session = new LevelSession();
            var (puzzle, solution) = ClassicLevel();
            session.Load(puzzle, solution, difficulty: 1200, allowRoll: true);

            for (int slotIndex = 0; slotIndex < BoardState.PieceCount; slotIndex++)
                PlaceFromTray(session, slotIndex, solution[slotIndex]);
            Assert.IsTrue(session.Data.MarkSolvedIfAllOnBoard());

            session.Data.Board.Lift(0);

            Assert.IsFalse(session.Data.Board.AllOnBoard);
            Assert.IsTrue(session.Data.IsSolved, "單向：只會設 true，不會設回 false");
        }

        [Test]
        public void 過關_換關之後回到未過關()
        {
            // 這就是「彈窗不需要有人記得關掉它」的原理：換關整包換掉 → 旗標歸零
            // → 彈窗下一幀自己消失
            var session = new LevelSession();
            var (puzzleA, solutionA) = ClassicLevel();
            session.Load(puzzleA, solutionA, difficulty: 1200, allowRoll: true);

            for (int slotIndex = 0; slotIndex < BoardState.PieceCount; slotIndex++)
                PlaceFromTray(session, slotIndex, solutionA[slotIndex]);
            Assert.IsTrue(session.Data.MarkSolvedIfAllOnBoard());

            var (puzzleB, solutionB) = TranslatedLevel(new ExactVector2(6, 0));
            session.Load(puzzleB, solutionB, difficulty: 1200, allowRoll: true);

            Assert.IsFalse(session.Data.IsSolved, "新的一關不該一開始就是過關狀態");
        }

        [Test]
        public void 盤面_Clear只觸發一次Changed()
        {
            var session = new LevelSession();
            var (puzzle, solution) = ClassicLevel();
            session.Load(puzzle, solution, difficulty: 1200, allowRoll: true);

            for (int slotIndex = 0; slotIndex < BoardState.PieceCount; slotIndex++)
                session.Data.Board.Place(slotIndex, solution[slotIndex]);

            int changedCount = 0;
            session.Data.Board.Changed += (board, slotIndex) => changedCount++;

            session.Data.Board.Clear();

            Assert.AreEqual(1, changedCount, "原本是連呼叫七次 Lift，也就是連發七次");
            Assert.AreEqual(0, session.Data.Board.OnBoardCount);
            Assert.AreEqual(solution[0], session.Data.Board[0].Placement, "Clear 保留姿態");
        }

        // ─────────────────────────────────────────────────────────────
        // 跨元件的 UI 決定（P1-13）。右下角那塊區域的角色是**三態**，
        // 不是兩個平行布林——這一組就是釘住「不可能兩個同時成立」。
        //
        // 這條規則以前住在 GameObject/（TrayView 的 IsArrowShown 加
        // LayoutOverlay 的 dragging），dotnet 編不到，一個測試都沒有。
        // ─────────────────────────────────────────────────────────────

        [Test]
        public void 右下角_預設是箭頭()
        {
            var ui = new SharedUiState();
            Assert.AreEqual(TrayCornerRole.Arrow, ui.TrayCorner,
                            "什麼都還沒發生的時候那塊該是箭頭，不然一開局抽屜就打不開");
        }

        [Test]
        public void 右下角_拖曳中是回收框_沒在拖是箭頭()
        {
            var ui = new SharedUiState();

            ui.UpdateTrayCorner(isDragging: true, isLevelComplete: false);
            Assert.AreEqual(TrayCornerRole.RecycleBox, ui.TrayCorner);

            ui.UpdateTrayCorner(isDragging: false, isLevelComplete: false);
            Assert.AreEqual(TrayCornerRole.Arrow, ui.TrayCorner, "放手之後要變回箭頭");
        }

        [Test]
        public void 右下角_過關就什麼都不是()
        {
            var ui = new SharedUiState();
            ui.UpdateTrayCorner(isDragging: false, isLevelComplete: true);

            Assert.AreEqual(TrayCornerRole.Hidden, ui.TrayCorner,
                            "過關之後盤面整個鎖住，完成彈窗蓋滿畫面");
        }

        [Test]
        public void 右下角_過關優先於拖曳中()
        {
            // 理論上兩者不可能同時成立（過關是在放手那一格判定的），
            // 但三態的價值就是「萬一同時成立也只有一個答案」——
            // 換成兩個平行布林的話，答案會取決於哪個 if 先跑
            var ui = new SharedUiState();
            ui.UpdateTrayCorner(isDragging: true, isLevelComplete: true);

            Assert.AreEqual(TrayCornerRole.Hidden, ui.TrayCorner);
        }

        [Test]
        public void 右下角_任何輸入組合都只有一個角色()
        {
            // 「箭頭與回收框同時顯示」這種組合在形狀上就不存在——
            // 這正是待辦清單決定 9（不要用平行布林表達互斥狀態）要的東西
            var ui = new SharedUiState();

            foreach (bool dragging in new[] { false, true })
            {
                foreach (bool complete in new[] { false, true })
                {
                    ui.UpdateTrayCorner(dragging, complete);

                    int matchCount = 0;
                    if (ui.TrayCorner == TrayCornerRole.Arrow) matchCount++;
                    if (ui.TrayCorner == TrayCornerRole.RecycleBox) matchCount++;
                    if (ui.TrayCorner == TrayCornerRole.Hidden) matchCount++;

                    Assert.AreEqual(1, matchCount, $"dragging={dragging} complete={complete}");
                }
            }
        }

        [Test]
        public void 右下角_是投影所以不需要重置()
        {
            // store 不跟著 LevelData 整包換掉，所以「換關要不要重置」是每個值都要回答的問題。
            // 這個值的答案是「不用」——它每幀被無條件重寫，是自癒的。
            // 這條測試把那個性質釘住：不管之前是什麼，餵新的輸入就會覆蓋掉
            var ui = new SharedUiState();
            ui.UpdateTrayCorner(isDragging: false, isLevelComplete: true);
            Assert.AreEqual(TrayCornerRole.Hidden, ui.TrayCorner);

            // 換關之後控制器下一幀餵進來的就是「沒在拖、沒過關」
            ui.UpdateTrayCorner(isDragging: false, isLevelComplete: false);
            Assert.AreEqual(TrayCornerRole.Arrow, ui.TrayCorner, "不必有人記得重置它");
        }

        // ─────────────────────────────────────────────────────────────
        // 開發用的自動填入（P2-4）
        // ─────────────────────────────────────────────────────────────

        /// <returns>載入經典大正方形的一場。</returns>
        private static LevelSession LoadedClassicSession()
        {
            var session = new LevelSession();
            var (puzzle, solution) = ClassicLevel();
            session.Load(puzzle, solution, difficulty: 1200, allowRoll: true);
            return session;
        }

        /// <summary>填完之後：留的那一槽在待用區，其餘六片在盤上而且擺放就是參考解。</summary>
        [Test]
        public void 自動填入_六片上盤且留下的那一槽回到待用區()
        {
            LevelData level = LoadedClassicSession().Data;

            Assert.IsTrue(level.TryFillFromReferenceSolution(0, out _));

            Assert.AreEqual(6, level.Board.OnBoardCount);
            Assert.IsFalse(level.Board[0].OnBoard, "留的那一槽不該在盤上");
            Assert.IsTrue(level.IsInTray(0), "留的那一槽該在待用區");

            for (int slotIndex = 1; slotIndex < BoardState.PieceCount; slotIndex++)
            {
                Assert.IsTrue(level.Board[slotIndex].OnBoard, $"第 {slotIndex} 槽該在盤上");
                Assert.IsFalse(level.IsInTray(slotIndex), $"第 {slotIndex} 槽不該還在待用區");
                Assert.AreEqual(level.ReferenceSolution[slotIndex], level.Board[slotIndex].Placement);
            }
        }

        /// <summary>
        /// ⚠️ 填六片<b>不會過關</b>——判定要七片。
        /// 這條在守「開發捷徑不會意外把關卡推到完成狀態」。
        /// </summary>
        [Test]
        public void 自動填入_六片不會觸發過關()
        {
            LevelData level = LoadedClassicSession().Data;
            level.TryFillFromReferenceSolution(0, out _);

            Assert.IsFalse(level.MarkSolvedIfAllOnBoard());
            Assert.IsFalse(level.IsSolved);
        }

        /// <summary>整組算 undo 的一步，按一次就整個還原成填之前的樣子。</summary>
        [Test]
        public void 自動填入_按一次undo就整組還原()
        {
            LevelData level = LoadedClassicSession().Data;

            // 先自己放一片進去，這樣「還原」才有東西可還原，不是還原成空盤
            level.SetInTray(3, false);
            level.PushUndoStep(level.Board[3]);
            level.Board.Place(3, level.ReferenceSolution[3]);

            level.TryFillFromReferenceSolution(0, out _);
            Assert.AreEqual(6, level.Board.OnBoardCount);

            Assert.IsTrue(level.TryUndo(out _));

            Assert.AreEqual(1, level.Board.OnBoardCount, "應該退回只有第 3 槽在盤上");
            Assert.IsTrue(level.Board[3].OnBoard);
            Assert.IsTrue(level.CanUndo, "自己放的那一步還在，沒被吃掉");
        }

        /// <summary>已經過關就不給填——那時候彈窗蓋著，填下去只會讓狀態變得難解釋。</summary>
        [Test]
        public void 自動填入_已經過關時不作用()
        {
            LevelData level = LoadedClassicSession().Data;
            level.MarkSolved();

            Assert.IsFalse(level.TryFillFromReferenceSolution(0, out _));
            Assert.AreEqual(0, level.Board.OnBoardCount);
        }

        /// <summary>槽索引超出範圍要丟例外，不要默默填成別的樣子。</summary>
        [Test]
        public void 自動填入_槽索引超出範圍丟例外()
        {
            LevelData level = LoadedClassicSession().Data;

            Assert.Throws<System.ArgumentOutOfRangeException>(
                () => level.TryFillFromReferenceSolution(BoardState.PieceCount, out _));
        }
    }
}
