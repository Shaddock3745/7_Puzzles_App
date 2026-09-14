using System;
using System.Collections.Generic;

namespace Tangram.Data
{
    // 受試者本身的資料（年齡層、經驗、代號…）在 FeedbackParticipant.cs（2026-09-14 搬過去）

    /// <summary>
    /// 回饋收集的題單（2026-09-13，`待辦清單.md`〈權重要找其他人對〉）。
    ///
    /// ⚠️ <b>固定題單，不讀 ELO</b>：ELO 會把難度收斂到受試者實力附近，每個人只會看到 ±100 分那個窄帶，
    /// 整條尺根本驗不到。所以<b>每個人都做同樣五題，只有順序不同</b>。
    ///
    /// <b>五題、每人順序打散</b>（使用者定）：10 題 × 新手一題 3 分鐘 = 30 分鐘，對小孩與志願者太長。
    /// 順序打散是為了<b>學習效應</b>：固定順序的話，第 5 題永遠是做過四題之後才遇到，難度會系統性被低估。
    /// </summary>
    public static class FeedbackPlan
    {
        /// <summary>
        /// 五個目標難度，涵蓋整條尺。⚠️ 兩端會拿到「將就」的題：2026-09-14 量過，800 是 4%、1600 是 11%（各一百題，Debug）——
        /// 那不影響資料，因為每一行存的是<b>實際難度</b>，不是這裡的目標值。
        /// </summary>
        public static readonly IReadOnlyList<int> TargetDifficulties = new[] { 800, 1000, 1200, 1400, 1600 };

        /// <summary>一位受試者要做幾題。</summary>
        public static int PuzzleCount => TargetDifficulties.Count;

        /// <summary>主觀難度的下限（很簡單）。</summary>
        public const int MinimumSubjectiveDifficulty = 1;

        /// <summary>主觀難度的上限（很難）。</summary>
        public const int MaximumSubjectiveDifficulty = 5;

        /// <summary>
        /// 第 <paramref name="participantNumber"/> 位受試者的出題順序：題單索引 0 ～ 4 的一個排列。
        ///
        /// ⭐ <b>可重現</b>：同一個種子、同一個編號永遠是同一個順序，事後查得出他當時的題序。
        /// ⚠️ <b>刻意不用 <c>System.Random</c></b>：Unity 的 Mono 與 .NET 的實作不保證序列一樣，
        /// 那樣 dotnet 測試與實機會排出不同的順序。自己寫一個 xorshift，兩邊保證相同。
        /// </summary>
        /// <param name="planSeed">題單種子。</param>
        /// <param name="participantNumber">受試者編號（從 1 起）。</param>
        /// <returns>長度 <see cref="PuzzleCount"/> 的排列。</returns>
        public static int[] OrderFor(int planSeed, int participantNumber)
        {
            var order = new int[PuzzleCount];
            for (int i = 0; i < order.Length; i++) order[i] = i;

            // 兩個大質數把種子與編號攪在一起，避免「編號 1 與 2 的起點太像」
            uint state = unchecked((uint)planSeed * 2654435761u ^ (uint)participantNumber * 2246822519u);
            if (state == 0) state = 0x9E3779B9u;   // xorshift 的狀態不能是 0，否則永遠是 0

            // Fisher–Yates：從最後一格往前，每格跟前面（含自己）隨機一格交換
            for (int i = order.Length - 1; i > 0; i--)
            {
                state ^= state << 13;
                state ^= state >> 17;
                state ^= state << 5;
                int j = (int)(state % (uint)(i + 1));
                (order[i], order[j]) = (order[j], order[i]);
            }
            return order;
        }

        /// <summary>
        /// 第 <paramref name="participantNumber"/> 位受試者用哪一批題：<b>每個人一個新種子</b>，種子 = 基準種子 ＋ 編號 − 1（2026-09-14）。
        ///
        /// <b>為什麼要換</b>：一個種子只有 5 題，而權重有 6 個——用 5 個點決定 6 個權重解不出唯一答案，
        /// 而且 5 題之間的差異可能被一個特徵主導，其他特徵沒機會表現。<b>題目多樣性比人數重要</b>。
        ///
        /// <b>為什麼每人一個</b>（使用者定）：會<b>一次帶一組小朋友來測，其他人在旁邊看</b>。
        /// 同一組拿到同一批題的話，輪到的人等於在做看過解法的題，主觀難度與時間都失真。
        /// 原本是每 4 人一批（為了看「大家對同一題的評分一不一致」），改成每人一批之後
        /// ⚠️ <b>每一題只有一個人評，那個一致性量不到了</b>——要檢查主觀評分可不可信，得改看「系統難度相近的題，不同人評得像不像」。
        /// ⚠️ 換種子擋得掉「看過同一題」，擋不掉「看別人玩學到的策略」。
        /// </summary>
        /// <param name="basePlanSeed">基準種子。</param>
        /// <param name="participantNumber">受試者編號（從 1 起）。</param>
        /// <returns>這一位的題單種子。</returns>
        /// <exception cref="ArgumentOutOfRangeException">編號小於 1。</exception>
        public static int PlanSeedFor(int basePlanSeed, int participantNumber)
        {
            if (participantNumber < 1)
                throw new ArgumentOutOfRangeException(nameof(participantNumber), participantNumber, "受試者編號從 1 起");

            return unchecked(basePlanSeed + participantNumber - 1);
        }

        /// <summary>主觀難度合不合法。</summary>
        /// <param name="subjectiveDifficulty">1 ～ 5。</param>
        /// <returns>在範圍內就是 true。</returns>
        public static bool IsValidSubjectiveDifficulty(int subjectiveDifficulty) =>
            subjectiveDifficulty >= MinimumSubjectiveDifficulty && subjectiveDifficulty <= MaximumSubjectiveDifficulty;
    }
}
