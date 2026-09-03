using System;
using System.Collections.Generic;
using Tangram.Data;

namespace Tangram.Core
{
    /// <summary>
    /// 輸入來源的統一介面（企劃書 5.3-1）。
    /// 觸控與未來的影像辨識都實作這個，遊戲核心不需要知道資料從哪來。
    ///
    /// 約定：Board 裡的座標永遠是精確值。浮點只存在於實作內部
    /// （手指位置、辨識結果），送進 Board 之前一定要先過
    /// SnapSolver 吸附成 ℤ[√2] 座標（§0、附錄 B）。
    /// </summary>
    public interface IInputSource
    {
        /// <summary>顯示用名稱，例如「觸控」。</summary>
        string Name { get; }

        /// <summary>目前是否正在接受輸入。</summary>
        bool IsActive { get; }

        /// <summary>這個來源維護的盤面。切換來源時由 GameManager 決定要不要沿用。</summary>
        BoardState Board { get; }

        /// <summary>盤面有變動時觸發（等同 Board.Changed，轉一層方便訂閱與取消）。</summary>
        event Action<BoardState> BoardChanged;

        void Activate();
        void Deactivate();

        /// <summary>每幀呼叫。輪詢式的來源在這裡更新盤面；事件式的可以留空。</summary>
        void Tick(float deltaTime);
    }

    /// <summary>
    /// 最簡單的輸入來源：由程式直接寫入盤面。
    /// 用途：P1 的固定測試資料、EditMode 測試、以及觸控控制器完成前的暫代。
    /// </summary>
    public sealed class ScriptedInputSource : IInputSource
    {
        public string Name { get; }
        public bool IsActive { get; private set; }
        public BoardState Board { get; }

        public event Action<BoardState> BoardChanged;

        public ScriptedInputSource(string name = "腳本", BoardState board = null)
        {
            Name = name;
            Board = board ?? new BoardState();
            Board.Changed += changedBoard => BoardChanged?.Invoke(changedBoard);
        }

        public void Activate() => IsActive = true;
        public void Deactivate() => IsActive = false;
        public void Tick(float deltaTime) { }

        /// <summary>
        /// 把一組已知的擺放整個灌進盤面（例如 Fixtures.ClassicSquare()）。
        /// 配對方式：每個槽從剩下的擺放裡取第一個片種相符的，所以行為是確定的。
        /// </summary>
        public void LoadPlacements(IReadOnlyList<Placement> placements)
        {
            Board.Clear();
            var remainingPlacements = new List<Placement>(placements);

            for (int slotIndex = 0; slotIndex < BoardState.PieceCount; slotIndex++)
            {
                PieceKind slotKind = Board.KindOf(slotIndex);
                int matchIndex = remainingPlacements.FindIndex(
                    placement => placement.Kind == slotKind);

                if (matchIndex < 0) continue;
                Board.Place(slotIndex, remainingPlacements[matchIndex]);
                remainingPlacements.RemoveAt(matchIndex);
            }
        }
    }
}
