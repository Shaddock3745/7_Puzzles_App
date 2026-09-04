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

    /// <summary>一次驗證的結果。</summary>
    public readonly struct ValidationResult
    {
        /// <summary>停在哪一關。列舉的順序就是檢查順序。</summary>
        public readonly ValidationState State;

        /// <summary>已經放上盤面的片數，0~7。</summary>
        public readonly int PlacedCount;

        /// <summary>State = Overlapping 時，重疊的那兩片在 BoardState 裡的槽索引；否則 −1。</summary>
        public readonly int FirstOverlappingPiece;
        public readonly int SecondOverlappingPiece;

        /// <summary>七片都放好時算得出來的形狀 key，可直接拿去查 puzzles 表；否則 null。</summary>
        public readonly string ShapeKey;

        /// <param name="state">停在哪一關。</param>
        /// <param name="placedCount">已放置的片數。</param>
        /// <param name="firstOverlappingPiece">重疊的第一片槽索引，沒有就傳 −1。</param>
        /// <param name="secondOverlappingPiece">重疊的第二片槽索引，沒有就傳 −1。</param>
        /// <param name="shapeKey">算得出來的形狀 key，沒有就傳 null。</param>
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

        /// <summary>是不是過關了。</summary>
        public bool Solved => State == ValidationState.Solved;

        /// <returns>給人看的字串，例如 <c>"Incomplete (5/7)"</c>。</returns>
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
    /// 盒子模式下過關條件退化成「七片都合法放置」（<c>board.AllOnBoard</c>），
    /// 不需要 canonical key、也不需要連通性檢查，
    /// <c>Overlapping</c> / <c>Disconnected</c> / <c>WrongShape</c> 三種狀態會消失。
    /// 詳見 互動設計.md，改寫排在 P1-10。
    /// </summary>
    public static class PuzzleValidator
    {
        /// <param name="board">目前的盤面。</param>
        /// <param name="target">要比對的題目，傳 null 就永遠不會 Solved（可以拿來單純算形狀 key）。</param>
        /// <returns>
        /// 驗證結果。七片沒放滿時第一步就回傳，不會碰到昂貴的 PuzzleKey；
        /// 真正貴的只有「七片剛好都放上去」的那一瞬間。
        /// <b>綁在 Board.Changed，不要綁 Update()。</b>
        /// </returns>
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
        /// <param name="pieces">要驗證的擺放。少於七片會直接回 Incomplete，多於七片不會被擋。</param>
        /// <param name="target">要比對的題目，可以傳 null。</param>
        /// <returns>驗證結果。重疊回報的索引是這個清單的索引。</returns>
        public static ValidationResult Check(IReadOnlyList<Placement> pieces, Puzzle target)
        {
            if (pieces.Count < BoardState.PieceCount)
                return new ValidationResult(ValidationState.Incomplete, pieces.Count);
            return CheckPlacements(pieces, target);
        }

        /// <summary>兩個 Check 共用的主體：重疊 → 連通 → 形狀 key。</summary>
        /// <param name="pieces">要驗證的擺放。</param>
        /// <param name="target">要比對的題目，可以是 null。</param>
        /// <returns>驗證結果，一定會帶上算好的 ShapeKey（除非提早在重疊或連通就回傳）。</returns>
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
        /// </summary>
        /// <param name="board">目前的盤面。</param>
        /// <returns>算好 Key 與 PuzzleKey 的 Arrangement。</returns>
        /// <remarks>
        /// <b>不檢查是否放滿</b>，會直接讀七個槽的 Placement。有片還沒放的話讀到的是
        /// 上次的姿態，結果會是垃圾——所以一定要先確認 Solved 再呼叫。
        /// </remarks>
        public static Arrangement ToArrangement(BoardState board)
        {
            var pieces = new Placement[BoardState.PieceCount];
            for (int slotIndex = 0; slotIndex < BoardState.PieceCount; slotIndex++)
                pieces[slotIndex] = board[slotIndex].Placement;
            return Arrangement.FromPlacements(pieces).WithKeys();
        }
    }
}
