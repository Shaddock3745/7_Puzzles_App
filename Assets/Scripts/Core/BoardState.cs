using System;
using System.Collections.Generic;
using Tangram.Data;

namespace Tangram.Core
{
    /// <summary>
    /// 一片的觀測結果。OnBoard = false 代表還在待用區、或這一幀輸入來源沒看到它。
    /// 此時 Placement 仍然有值，存的是上次的姿態，方便拿回來時保留角度。
    /// </summary>
    public readonly struct PieceObservation
    {
        public readonly int Index;          // 0..6，對應 PiecePrototype.TangramSet
        public readonly PieceKind Kind;
        public readonly Placement Placement;
        public readonly bool OnBoard;

        /// <param name="index">槽索引 0~6。這是片的身分，不是片種。</param>
        /// <param name="kind">片種，必須與該槽固定的片種相同。</param>
        /// <param name="placement">精確擺放。OnBoard = false 時是上次的姿態。</param>
        /// <param name="onBoard">是否在盤上（也就是進了模型）。</param>
        public PieceObservation(int index, PieceKind kind, Placement placement, bool onBoard)
        {
            Index = index;
            Kind = kind;
            Placement = placement;
            OnBoard = onBoard;
        }

        /// <returns>給人看的字串，未放置時只印片種。</returns>
        public override string ToString()
            => $"#{Index} {(OnBoard ? Placement.ToString() : PieceKindNames.ToStorageString(Kind) + " (未放置)")}";
    }

    /// <summary>
    /// 盤面狀態：固定七片，每片可能在盤上或不在。
    /// 這是「所有輸入來源最終收斂成的東西」，座標全為精確值（§0）。
    /// 待用區與暫放的位置不在這裡，由 UI 自己管。
    /// </summary>
    public sealed class BoardState
    {
        public const int PieceCount = 7;

        private readonly PieceObservation[] _pieces = new PieceObservation[PieceCount];

        /// <summary>盤面內容有任何變動時觸發。參數就是這個 BoardState 自己。</summary>
        public event Action<BoardState> Changed;

        /// <summary>建一個七片都不在盤上的空盤面，片種依 TangramSet 固定。</summary>
        public BoardState()
        {
            for (int slotIndex = 0; slotIndex < PieceCount; slotIndex++)
            {
                PieceKind kind = PiecePrototype.TangramSet[slotIndex];
                _pieces[slotIndex] = new PieceObservation(
                    slotIndex, kind,
                    new Placement(kind, 0, false, ExactVector2.Zero), false);
            }
        }

        /// <summary>七個槽的觀測結果，索引就是槽索引。</summary>
        public IReadOnlyList<PieceObservation> Pieces => _pieces;

        /// <param name="slotIndex">槽索引 0~6。</param>
        /// <returns>該槽的觀測結果（是複本，改它不會影響盤面）。</returns>
        public PieceObservation this[int slotIndex] => _pieces[slotIndex];

        /// <param name="slotIndex">槽索引 0~6。</param>
        /// <returns>該槽固定的片種。</returns>
        public PieceKind KindOf(int slotIndex) => _pieces[slotIndex].Kind;

        /// <summary>
        /// 七片是不是都在盤上。
        /// <b>盒子模式下這就是過關判定</b>——吸附的硬限制保證每片都在盒內且不重疊，
        /// 面積 32 = 盒子面積，所以七片都放上去就必然填滿（互動設計 §7）。
        /// </summary>
        public bool AllOnBoard
        {
            get
            {
                for (int slotIndex = 0; slotIndex < PieceCount; slotIndex++)
                    if (!_pieces[slotIndex].OnBoard) return false;
                return true;
            }
        }

        /// <summary>目前有幾片在盤上，0~7。</summary>
        public int OnBoardCount
        {
            get
            {
                int placedCount = 0;
                for (int slotIndex = 0; slotIndex < PieceCount; slotIndex++)
                    if (_pieces[slotIndex].OnBoard) placedCount++;
                return placedCount;
            }
        }

        /// <summary>放下（或移動）指定的片。</summary>
        /// <param name="slotIndex">槽索引 0~6。</param>
        /// <param name="placement">精確擺放，片種必須與該槽相符。</param>
        /// <exception cref="ArgumentException">片種與槽不符。</exception>
        public void Place(int slotIndex, Placement placement)
        {
            if (placement.Kind != _pieces[slotIndex].Kind)
                throw new ArgumentException(
                    $"第 {slotIndex} 片是 {_pieces[slotIndex].Kind}，不能放 {placement.Kind}");

            _pieces[slotIndex] = new PieceObservation(slotIndex, placement.Kind, placement, true);
            Changed?.Invoke(this);
        }

        /// <summary>拿離盤面（回待用區／這一幀沒看到）。姿態保留，方便拿回來。</summary>
        /// <param name="slotIndex">槽索引 0~6。</param>
        public void Lift(int slotIndex)
        {
            PieceObservation observation = _pieces[slotIndex];
            _pieces[slotIndex] = new PieceObservation(
                slotIndex, observation.Kind, observation.Placement, false);
            Changed?.Invoke(this);
        }

        /// <summary>全部拿起。<b>注意：會連續觸發七次 Changed。</b></summary>
        public void Clear()
        {
            for (int slotIndex = 0; slotIndex < PieceCount; slotIndex++) Lift(slotIndex);
        }

        /// <summary>目前在盤上的片，給驗證與吸附用。</summary>
        /// <returns>新配置的陣列，只含 OnBoard 的片，順序照槽索引。長度是 0~7。</returns>
        public Placement[] OnBoardPlacements()
        {
            var placements = new List<Placement>(PieceCount);
            for (int slotIndex = 0; slotIndex < PieceCount; slotIndex++)
                if (_pieces[slotIndex].OnBoard) placements.Add(_pieces[slotIndex].Placement);
            return placements.ToArray();
        }

        /// <summary>在盤上、且不是指定那一片（拖曳中的片不該跟自己吸附）。</summary>
        /// <param name="excludedSlotIndex">要排除的槽索引，通常是拖曳中那一片。</param>
        /// <returns>新配置的陣列。這就是要餵給 <c>SnapSolver</c> 的 placedPieces。</returns>
        public Placement[] OnBoardPlacementsExcept(int excludedSlotIndex)
        {
            var placements = new List<Placement>(PieceCount);
            for (int slotIndex = 0; slotIndex < PieceCount; slotIndex++)
                if (slotIndex != excludedSlotIndex && _pieces[slotIndex].OnBoard)
                    placements.Add(_pieces[slotIndex].Placement);
            return placements.ToArray();
        }

        /// <summary>複製盤面內容，但不複製事件訂閱者。適合拿來做假設性試算。</summary>
        /// <returns>內容相同、沒有任何 Changed 訂閱者的新盤面。</returns>
        public BoardState Clone()
        {
            var copy = new BoardState();
            for (int slotIndex = 0; slotIndex < PieceCount; slotIndex++)
                copy._pieces[slotIndex] = _pieces[slotIndex];
            return copy;
        }

        /// <summary>整盤覆寫（輪詢式輸入來源每幀一次的用法）。只觸發一次 Changed。</summary>
        /// <param name="observations">七個槽的新內容，順序與片種都要對得上。</param>
        /// <exception cref="ArgumentException">數量不是七個，或某一槽的片種不符。</exception>
        public void ReplaceAll(IReadOnlyList<PieceObservation> observations)
        {
            if (observations.Count != PieceCount)
                throw new ArgumentException($"必須剛好 {PieceCount} 片");

            for (int slotIndex = 0; slotIndex < PieceCount; slotIndex++)
            {
                if (observations[slotIndex].Kind != _pieces[slotIndex].Kind)
                    throw new ArgumentException($"第 {slotIndex} 片種類不符");
                _pieces[slotIndex] = observations[slotIndex];
            }
            Changed?.Invoke(this);
        }
    }
}
