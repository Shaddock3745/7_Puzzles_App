using System;
using Tangram.Data;

namespace Tangram.Core
{
    /// <summary>
    /// 片的繪製順序（互動設計 §13.6）。純算術，沒有 Unity 相依，可以離線測。
    ///
    /// 規則是三層加兩個排序鍵：
    /// <code>
    /// 正在被拖的   最上面
    /// 未吸附的     中間
    /// 已吸附的     最下面（盒子還要在這之下）
    /// </code>
    /// 同一層裡**面積小的在上**——小三角壓在大三角上面才點得到。
    /// 面積也一樣的（兩片大三角、兩片小三角）用槽索引破同分，
    /// 否則同層的兩片 sortingOrder 相同，繪製順序未定義，畫面會閃爍。
    ///
    /// 一個省事的事實：**已吸附的片彼此保證不重疊**（吸附的硬限制，互動設計 §4），
    /// 所以最下面那層誰上誰下其實無所謂。面積規則只在未吸附的片之間、
    /// 以及未吸附壓在已吸附上面時才真的有意義。
    /// </summary>
    public static class PieceSorting
    {
        /// <summary>層之間的間距。要大於「面積階數 × <see cref="SizeRankStride"/> + 最大槽索引」。</summary>
        public const int TierStride = 100;

        /// <summary>同一層裡面積階數之間的間距。要大於最大槽索引（6）。</summary>
        public const int SizeRankStride = 10;

        /// <summary>盒子在自己那層裡的順序。<b>它跟片的先後由 sorting layer 決定，不靠這個數字。</b></summary>
        public const int BoxSortingOrder = 0;

        // ─────────────────────────────────────────────────────────────
        // Sorting layer：跨系統的先後由**一個地方**決定（2026-09-09）
        //
        // ⚠️ 在這之前所有 renderer 都在 Default 層，先後全靠各自挑的數字硬撐——
        // 抽屜挑了 2000～2002、片是 0～226、Canvas 是 0。那不是排序，是巧合：
        // 沒有任何東西會在它被破壞時報錯，而症狀是「某個東西被蓋住了」。
        //
        // 現在改成由 ProjectSettings 的 sorting layer 清單決定，**清單的順序就是層級**：
        //
        //     Default → Box → Piece → GameUI → CompleteUI
        //               盒子   七片    抽屜與   完成彈窗
        //                             四顆鈕
        //
        // 各系統的 sortingOrder 從此**只在自己那層裡有意義**，不必再跟別人比大小。
        //
        // ⚠️ 這些字串必須跟 ProjectSettings/TagManager.asset 對得上。
        // 打錯字的話 Unity 會把那個 renderer 留在 Default 層，**症狀就是被蓋住**。
        // ─────────────────────────────────────────────────────────────

        /// <summary>盒子。畫在所有片之下。</summary>
        public const string BoxSortingLayer = "Box";

        /// <summary>七片。層內的先後才用 <see cref="SortingOrder"/> 算。</summary>
        public const string PieceSortingLayer = "Piece";

        /// <summary>遊戲中的 UI：待用區抽屜、箭頭、回收框，以及四顆按鈕的 Canvas。</summary>
        public const string GameUiSortingLayer = "GameUI";

        /// <summary>完成彈窗。<b>蓋在所有東西上面</b>，因為它出現時整個盤面都鎖住了。</summary>
        public const string CompleteUiSortingLayer = "CompleteUI";

        /// <param name="kind">片種，決定面積階數。</param>
        /// <param name="slotIndex">槽索引 0~6，只用來破同分。</param>
        /// <param name="isSnapped">畫面上吸住了沒。注意這**不是** <c>BoardState.OnBoard</c>——
        /// 拖曳中吸住只是預覽，那時候盤面上還沒登記（互動設計 §13.10）。</param>
        /// <param name="isBeingDragged">是不是正在被拖。拖曳中永遠在最上層，
        /// 否則拖一片小的經過另一片未吸附的大片時會鑽到它下面。</param>
        /// <returns>可以直接寫進 <c>SpriteRenderer.sortingOrder</c> 的值，範圍 0~226。</returns>
        public static int SortingOrder(PieceKind kind, int slotIndex,
                                       bool isSnapped, bool isBeingDragged)
        {
            int tier = isBeingDragged ? 2
                     : !isSnapped ? 1
                     : 0;
            return tier * TierStride + SizeRank(kind) * SizeRankStride + slotIndex;
        }

        /// <summary>
        /// 面積階數：最大的片是 0，越小的數字越大（也就是畫得越上面）。
        ///
        /// 不寫死對照表，改成「數有幾種片比我大」——之後如果片的定義變了，
        /// 這裡會自己跟著對，不會留下一張沒人記得要改的表。
        /// </summary>
        /// <param name="kind">片種。</param>
        /// <returns>0（最大）到 種類數−1（最小）。七巧板實際只會用到 0、1、2。</returns>
        public static int SizeRank(PieceKind kind)
        {
            Scalar myDoubleArea = PiecePrototype.Get(kind).DoubleArea;
            int strictlyLargerCount = 0;

            foreach (PieceKind other in (PieceKind[])Enum.GetValues(typeof(PieceKind)))
            {
                Scalar otherDoubleArea = PiecePrototype.Get(other).DoubleArea;
                if (otherDoubleArea > myDoubleArea && !AlreadyCounted(other, otherDoubleArea))
                    strictlyLargerCount++;
            }
            return strictlyLargerCount;

            // 面積相同的片（中三角／正方形／平行四邊形都是 4）只能算一次，
            // 否則階數會被灌水，間距 SizeRankStride 就不夠用
            bool AlreadyCounted(PieceKind candidate, Scalar candidateDoubleArea)
            {
                foreach (PieceKind earlier in (PieceKind[])Enum.GetValues(typeof(PieceKind)))
                {
                    if (earlier == candidate) break;
                    if (PiecePrototype.Get(earlier).DoubleArea == candidateDoubleArea) return true;
                }
                return false;
            }
        }
    }
}
