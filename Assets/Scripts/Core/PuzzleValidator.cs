using System.Collections.Generic;
using Tangram.Data;

namespace Tangram.Core
{
    /// <summary>驗證結果。給視覺／音效回饋分流用，不需要文字敘述（企劃書 5.2）。</summary>
    public enum ValidationState
    {
        /// <summary>還有片沒放上盤面。</summary>
        Incomplete,
        /// <summary>有兩片重疊。</summary>
        Overlapping,
        /// <summary>片沒有全部連在一起。</summary>
        Disconnected,
        /// <summary>七片都放好、沒問題，但形狀不是這一題。</summary>
        WrongShape,
        /// <summary>過關。</summary>
        Solved
    }

    public readonly struct ValidationResult
    {
        public readonly ValidationState State;
        public readonly int PlacedCount;

        /// <summary>State = Overlapping 時，重疊的那兩片在 BoardState 裡的槽索引。</summary>
        public readonly int FirstOverlappingPiece;
        public readonly int SecondOverlappingPiece;

        /// <summary>七片都放好時算得出來的形狀 key，可直接拿去查 puzzles 表。</summary>
        public readonly string ShapeKey;

        public ValidationResult(ValidationState state, int placedCount,
                                int firstOverlappingPiece = -1, int secondOverlappingPiece = -1,
                                string shapeKey = null)
        {
            State = state;
            PlacedCount = placedCount;
            FirstOverlappingPiece = firstOverlappingPiece;
            SecondOverlappingPiece = secondOverlappingPiece;
            ShapeKey = shapeKey;
        }

        public bool Solved => State == ValidationState.Solved;
        public override string ToString() => $"{State} ({PlacedCount}/7)";
    }

    /// <summary>
    /// 統一驗證：所有輸入模式共用同一套邏輯（企劃書 5.2）。
    ///
    /// 核心只有一行 —— 玩家排列的 canonical puzzle key 等於題目的 key 就是過關。
    /// 因為 key 已經對 16 種整體變換取過最小值，玩家整個圖案轉 45°
    /// 或鏡射過來拼，一樣算對（§2.4）。
    ///
    /// ⚠️ 這是「自由拼」版本，已被架構 review 取代。
    /// 定案改為盒子模式：題目是盤面上固定位置的容器，玩家填滿它。
    /// 盒子模式下過關條件退化成「七片都合法放置」，不需要 canonical key。
    /// 詳見 互動設計.md，改寫排在 P1-7。
    /// </summary>
    public static class PuzzleValidator
    {
        public static ValidationResult Check(BoardState board, Puzzle target)
        {
            if (!board.AllOnBoard)
                return new ValidationResult(ValidationState.Incomplete, board.OnBoardCount);

            // 重疊要回報「哪兩個槽」，所以這裡自己組陣列，讓索引對得上 BoardState
            var pieces = new Placement[BoardState.PieceCount];
            for (int slotIndex = 0; slotIndex < BoardState.PieceCount; slotIndex++)
                pieces[slotIndex] = board[slotIndex].Placement;

            return CheckPlacements(pieces, target);
        }

        /// <summary>沒有 BoardState 時的版本（例如驗證出題器產生的擺法）。</summary>
        public static ValidationResult Check(IReadOnlyList<Placement> pieces, Puzzle target)
        {
            if (pieces.Count < BoardState.PieceCount)
                return new ValidationResult(ValidationState.Incomplete, pieces.Count);
            return CheckPlacements(pieces, target);
        }

        private static ValidationResult CheckPlacements(IReadOnlyList<Placement> pieces, Puzzle target)
        {
            if (!Geometry.NoOverlap(pieces, out int firstOverlapping, out int secondOverlapping))
                return new ValidationResult(ValidationState.Overlapping, pieces.Count,
                                            firstOverlapping, secondOverlapping);

            if (!Geometry.AllConnected(pieces))
                return new ValidationResult(ValidationState.Disconnected, pieces.Count);

            string shapeKey = Canonical.PuzzleKey(pieces);
            bool solved = target != null && shapeKey == target.Key;

            return new ValidationResult(
                solved ? ValidationState.Solved : ValidationState.WrongShape,
                pieces.Count, -1, -1, shapeKey);
        }

        /// <summary>
        /// 把玩家目前的擺法收成一筆可入庫的解（§1）。
        /// 不檢查是否放滿，所以只有在 Solved 之後呼叫才有意義。
        /// </summary>
        public static Arrangement ToArrangement(BoardState board)
        {
            var pieces = new Placement[BoardState.PieceCount];
            for (int slotIndex = 0; slotIndex < BoardState.PieceCount; slotIndex++)
                pieces[slotIndex] = board[slotIndex].Placement;
            return Arrangement.FromPlacements(pieces).WithKeys();
        }
    }
}
