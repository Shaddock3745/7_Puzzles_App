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

        public PieceObservation(int index, PieceKind kind, Placement placement, bool onBoard)
        {
            Index = index;
            Kind = kind;
            Placement = placement;
            OnBoard = onBoard;
        }

        public override string ToString()
            => $"#{Index} {(OnBoard ? Placement.ToString() : PieceKindNames.ToStorageString(Kind) + " (未放置)")}";
    }

    /// <summary>
    /// 盤面狀態：固定七片，每片可能在盤上或不在。
    /// 這是「所有輸入來源最終收斂成的東西」，座標全為精確值（§0）。
    /// 待用區的位置不在這裡，由 UI 自己管。
    /// </summary>
    public sealed class BoardState
    {
        public const int PieceCount = 7;

        private readonly PieceObservation[] _pieces = new PieceObservation[PieceCount];

        /// <summary>盤面內容有任何變動時觸發。</summary>
        public event Action<BoardState> Changed;

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

        public IReadOnlyList<PieceObservation> Pieces => _pieces;
        public PieceObservation this[int slotIndex] => _pieces[slotIndex];
        public PieceKind KindOf(int slotIndex) => _pieces[slotIndex].Kind;

        public bool AllOnBoard
        {
            get
            {
                for (int slotIndex = 0; slotIndex < PieceCount; slotIndex++)
                    if (!_pieces[slotIndex].OnBoard) return false;
                return true;
            }
        }

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
        public void Place(int slotIndex, Placement placement)
        {
            if (placement.Kind != _pieces[slotIndex].Kind)
                throw new ArgumentException(
                    $"第 {slotIndex} 片是 {_pieces[slotIndex].Kind}，不能放 {placement.Kind}");

            _pieces[slotIndex] = new PieceObservation(slotIndex, placement.Kind, placement, true);
            Changed?.Invoke(this);
        }

        /// <summary>拿離盤面（回待用區／這一幀沒看到）。姿態保留，方便拿回來。</summary>
        public void Lift(int slotIndex)
        {
            PieceObservation observation = _pieces[slotIndex];
            _pieces[slotIndex] = new PieceObservation(
                slotIndex, observation.Kind, observation.Placement, false);
            Changed?.Invoke(this);
        }

        /// <summary>全部拿起。注意：會連續觸發七次 Changed。</summary>
        public void Clear()
        {
            for (int slotIndex = 0; slotIndex < PieceCount; slotIndex++) Lift(slotIndex);
        }

        /// <summary>目前在盤上的片，給驗證與吸附用。</summary>
        public Placement[] OnBoardPlacements()
        {
            var placements = new List<Placement>(PieceCount);
            for (int slotIndex = 0; slotIndex < PieceCount; slotIndex++)
                if (_pieces[slotIndex].OnBoard) placements.Add(_pieces[slotIndex].Placement);
            return placements.ToArray();
        }

        /// <summary>在盤上、且不是指定那一片（拖曳中的片不該跟自己吸附）。</summary>
        public Placement[] OnBoardPlacementsExcept(int excludedSlotIndex)
        {
            var placements = new List<Placement>(PieceCount);
            for (int slotIndex = 0; slotIndex < PieceCount; slotIndex++)
                if (slotIndex != excludedSlotIndex && _pieces[slotIndex].OnBoard)
                    placements.Add(_pieces[slotIndex].Placement);
            return placements.ToArray();
        }

        /// <summary>複製盤面內容，但不複製事件訂閱者。適合拿來做假設性試算。</summary>
        public BoardState Clone()
        {
            var copy = new BoardState();
            for (int slotIndex = 0; slotIndex < PieceCount; slotIndex++)
                copy._pieces[slotIndex] = _pieces[slotIndex];
            return copy;
        }

        /// <summary>整盤覆寫（輪詢式輸入來源每幀一次的用法）。只觸發一次 Changed。</summary>
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
