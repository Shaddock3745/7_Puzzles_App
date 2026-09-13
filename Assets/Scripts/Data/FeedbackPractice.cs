using System;
using System.Collections.Generic;

namespace Tangram.Data
{
    /// <summary>
    /// 回饋收集的<b>練習題</b>（2026-09-13）：一題已經擺好 5 片的殘局，只剩<b>平行四邊形（要翻面）＋ 一片三角形（要旋轉）</b>。
    ///
    /// <b>為什麼要有</b>：沒有練習的話，第一題同時在「學操作」與「解題」，時間與主觀難度都混進了「還不會操作」。
    /// 順序打散只是把這個誤差平均到五題上，每人只有五題，多一層雜訊很傷。
    /// ⭐ 而且受試者旁邊有人（甲案），規則是<b>練習題可以教、正式題不教</b>——「有沒有被教過」就不會汙染正式資料。
    ///
    /// <b>為什麼是殘局不是一整題簡單的</b>（使用者定）：七巧板最難自己發現的操作是<b>翻面</b>，只有平行四邊形需要。
    /// 「最簡單的一整題」不保證需要翻面，新手也可能要花好幾分鐘；殘局 30 秒內保證碰到每一種操作。
    ///
    /// ⚠️ <b>剩下那兩片從抽屜拿出來時是預設姿勢</b>（旋轉 0、不翻面，<c>TrayView.ResetPiecePoses</c>），
    /// 所以一定要確認「預設姿勢跟答案不一樣」，否則放上去就對了，什麼都沒練到。這一支負責保證這件事。
    /// </summary>
    public static class FeedbackPractice
    {
        /// <summary>
        /// 練習題的解：經典大正方形，<b>必要時整題鏡射一次</b>，讓平行四邊形一定是翻面的。
        ///
        /// 平行四邊形有 180° 旋轉對稱，但<b>沒有鏡射對稱</b>——翻面的姿勢不管怎麼轉都對不上不翻面的。
        /// 所以只要它在答案裡是翻面的，受試者就一定得按翻面鈕。
        /// 鏡射之後把整題平移回原本的範圍（y 從 0 ～ −4√2 移回 0 ～ 4√2），跟生成題一樣落在正象限。
        /// </summary>
        /// <returns>七片的絕對擺放，索引就是槽索引（同 <see cref="Fixtures.ClassicSquare"/>）。</returns>
        public static Placement[] BuildSolution()
        {
            Placement[] solution = Fixtures.ClassicSquare();
            if (solution[SlotOf(solution, PieceKind.Parallelogram)].IsFlipped) return solution;

            var backIntoPlace = new ExactVector2(Scalar.Zero, Scalar.FromRoot2Multiple(4));
            for (int slotIndex = 0; slotIndex < solution.Length; slotIndex++)
                solution[slotIndex] = solution[slotIndex].ApplyGlobal(0, true).Translated(backIntoPlace);
            return solution;
        }

        /// <summary>
        /// 要留空讓受試者自己放的兩槽：平行四邊形，加上一片<b>姿勢跟抽屜預設不一樣</b>的三角形。
        /// 三角形優先挑小的（放得快），都不行才挑中、大三角。
        /// </summary>
        /// <param name="solution">練習題的解。</param>
        /// <returns>兩個槽索引：平行四邊形、三角形。</returns>
        /// <exception cref="InvalidOperationException">平行四邊形沒翻面，或找不到姿勢跟預設不同的三角形——那樣練不到操作。</exception>
        public static int[] SlotsToLeaveEmpty(IReadOnlyList<Placement> solution)
        {
            int parallelogramSlot = SlotOf(solution, PieceKind.Parallelogram);
            if (!solution[parallelogramSlot].IsFlipped)
                throw new InvalidOperationException("練習題的平行四邊形沒有翻面，受試者練不到翻面鈕");

            foreach (PieceKind kind in new[] { PieceKind.SmallTriangle, PieceKind.MediumTriangle, PieceKind.LargeTriangle })
                for (int slotIndex = 0; slotIndex < solution.Count; slotIndex++)
                    if (solution[slotIndex].Kind == kind && !LooksLikeTrayDefault(solution[slotIndex]))
                        return new[] { parallelogramSlot, slotIndex };

            throw new InvalidOperationException("練習題裡找不到姿勢跟抽屜預設不同的三角形，受試者練不到旋轉鈕");
        }

        /// <summary>
        /// 這一片放上去的樣子，跟「從抽屜拿出來、完全不轉不翻」的樣子<b>是不是同一個形狀方向</b>（位置不算）。
        ///
        /// ⚠️ <b>不能比 <c>Rotation</c> 與 <c>IsFlipped</c></b>——三角形有鏡射對稱，「翻面 ＋ 轉某個角度」
        /// 跟「不翻面」可能佔位一模一樣（CLAUDE.md〈Placement.Equals 是姿態相等，不是形狀相等〉）。
        /// 所以比的是<b>頂點集合平移之後能不能重合</b>。
        /// </summary>
        /// <param name="placement">答案裡的那一片。</param>
        /// <returns>一樣的話 true——那一片放上去不必轉也不必翻。</returns>
        public static bool LooksLikeTrayDefault(Placement placement) =>
            SameShapeIgnoringPosition(new Placement(placement.Kind, 0, false, ExactVector2.Zero), placement);

        /// <summary>兩片（同片種）的頂點集合，平移之後能不能完全重合。</summary>
        /// <param name="first">第一片。</param>
        /// <param name="second">第二片。</param>
        /// <returns>能的話 true。</returns>
        public static bool SameShapeIgnoringPosition(Placement first, Placement second)
        {
            if (first.Kind != second.Kind) return false;

            ExactVector2[] firstVertices = first.Vertices();
            var secondVertices = new HashSet<ExactVector2>(second.Vertices());

            // 如果能重合，第一片的第 0 個頂點一定落在第二片的某個頂點上——逐一試那幾個平移量
            foreach (ExactVector2 candidate in secondVertices)
            {
                ExactVector2 shift = candidate - firstVertices[0];
                bool allMatch = true;
                foreach (ExactVector2 vertex in firstVertices)
                    if (!secondVertices.Contains(vertex + shift)) { allMatch = false; break; }
                if (allMatch) return true;
            }
            return false;
        }

        /// <param name="solution">七片。</param>
        /// <param name="kind">片種。</param>
        /// <returns>第一個是這個片種的槽。</returns>
        private static int SlotOf(IReadOnlyList<Placement> solution, PieceKind kind)
        {
            for (int slotIndex = 0; slotIndex < solution.Count; slotIndex++)
                if (solution[slotIndex].Kind == kind) return slotIndex;
            throw new InvalidOperationException($"解裡沒有 {kind}");
        }
    }
}
