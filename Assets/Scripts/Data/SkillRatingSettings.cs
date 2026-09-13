using System;
using UnityEngine;

namespace Tangram.Data
{
    /// <summary>
    /// ELO 換算的七個參數（P2-14，issue #31）。<b>換參數是改資料，不是改程式</b>。
    ///
    /// ⭐ <b>唯一的源頭是 <c>Assets/Resources/SkillRatingSettings.json</c></b>，
    /// 跟 <see cref="DifficultyWeights"/> 同一套規則：程式裡<b>刻意沒有預設值</b>，
    /// 遊戲由 <c>LevelController</c> 從 <c>Resources</c> 讀、讀不到就不進關，測試從 repo 路徑讀同一個檔。
    ///
    /// 公式在 <see cref="SkillRating"/>，每個參數的意思寫在欄位上。
    /// 定案的過程在 `待辦清單.md`〈ELO 公式定案〉。
    ///
    /// ⚠️ <b>欄位名就是 JSON 的鍵</b>。<c>JsonUtility</c> 碰到拼錯的鍵不會吭聲、直接讀成 0：
    /// 「0 一定是錯」的欄位在 <see cref="ThrowIfInvalid"/> 攔下來，
    /// 但 <see cref="dragWeightRatio"/> 與 <see cref="hintPenaltySeconds"/> 的 0 是合法的——
    /// 那兩個拼錯只有 <c>SkillRatingTests</c> 比對鍵名的測試攔得住。
    /// </summary>
    [Serializable]
    public class SkillRatingSettings
    {
        /// <summary>
        /// 新玩家的起始實力。<b>900</b>（2026-09-13 定）：不假定玩家一上來就能解中等題，
        /// 而 900 是量過、出題器保證正中的最低目標（P2-7 生成時間表，100 題全正中）。
        /// ⚠️ 更低的值要先量——800 以下沒量過，第一題就可能是「將就」。
        /// </summary>
        public double initialRating;

        /// <summary>
        /// ELO 的尺度，<b>400</b>（標準值）：實力比題目低 400 分，預期表現約 0.09（贏面 1 比 10）。
        /// 放到難度的尺上，難 1 個標準差（+200）時預期表現是 0.24。
        /// </summary>
        public double ratingScale;

        /// <summary>
        /// r = w₁ ÷ w₂，<b>拖曳一秒算幾秒的代價</b>，0 ～ 1，定 <b>0.5</b>。
        ///
        /// 時間代價 = 前景 − (1 − r) × 拖曳。白話：<b>每拖一秒，退你 (1 − r) 秒</b>。
        /// 只有 w₁ 與 w₂ 的比例有意義——兩個一起放大等於改 <see cref="referenceSeconds"/>——所以固定 w₂ = 1。
        ///
        /// ⚠️ <b>保守退路是 r = 1（只看前景），不是 0</b>。r = 0 是「拖曳完全不算錢」，
        /// 那時「拿著片想、每 3 秒放一次」最划算。原本文件寫反過（2026-09-13 更正）。
        /// </summary>
        public double dragWeightRatio;

        /// <summary>
        /// T，<b>秒數換成 0 ～ 1 的那把尺</b>，定 <b>90</b>（⚠️ 猜的，要真人資料校正）。
        ///
        /// 實際表現 = T ÷ (T + 時間代價)，所以代價剛好等於 T 時表現是 0.5。
        /// ⭐ <b>它也定義了「實力」的意思</b>：實力收斂到「你大約 T 秒解完的那個難度」。
        /// ⚠️ 開發者看過幾千個形狀，用自己的解題時間定它會偏短。
        /// </summary>
        public double referenceSeconds;

        /// <summary>
        /// P，<b>一次提示等於多想幾秒</b>，定 <b>0</b>（提示還不存在，求解器延到決賽前）。
        ///
        /// ⚠️ 提示讓人解得快，而公式獎勵快——不算錢的話「每題都等提示」就是最佳策略。
        /// 它加在時間代價上（不是乘在表現上），所以<b>用光提示解完仍然比放棄好</b>，是結構上保證的。
        /// ⚠️ 它有下限要算：提示在 45 秒才出現，P 太小的話「等提示」仍然划算。要等提示做出來玩過才定得出來。
        /// </summary>
        public double hintPenaltySeconds;

        /// <summary>基準 K，定 <b>20</b>。實際用的 K = 基準 K × <see cref="demonstrationMultiplier"/>。</summary>
        public double baseKFactor;

        /// <summary>
        /// ⚠️ <b>展示倍率</b>，現在是 <b>10</b>，正式版是 <b>1</b>。
        ///
        /// 評審現場只玩 2 ～ 3 局，K 太小看不出變化。刻意調大可以，
        /// <b>但必須在系統概述文件寫明「展示版 K 是正式設定的 10 倍」</b>——
        /// 否則踩到競賽注意事項第 ① 條「資料虛偽不實」。做成獨立欄位就是為了讓監控面板顯示得出來、聲明有東西可以指。
        /// </summary>
        public double demonstrationMultiplier;

        /// <summary>實際用的 K = 基準 K × 展示倍率。</summary>
        public double EffectiveKFactor => baseKFactor * demonstrationMultiplier;

        /// <summary>從設定檔的內容讀回來，讀的當下就檢查完。</summary>
        /// <param name="json">設定檔的內容。</param>
        /// <returns>檢查過的設定。</returns>
        /// <exception cref="ArgumentException">字串是空的，或任何一項不合法（訊息會說是哪一項）。</exception>
        public static SkillRatingSettings FromJson(string json)
        {
            if (string.IsNullOrEmpty(json))
                throw new ArgumentException("SkillRatingSettings.FromJson：字串是空的", nameof(json));

            SkillRatingSettings settings = JsonUtility.FromJson<SkillRatingSettings>(json);
            if (settings == null)
                throw new ArgumentException("SkillRatingSettings.FromJson：讀不出任何東西", nameof(json));

            settings.ThrowIfInvalid();
            return settings;
        }

        /// <summary>
        /// 檢查每一項。<b>讀檔時會自動叫</b>；在記憶體裡自己組一份（例如測試）時由
        /// <c>SkillRatingTracker</c> 的建構子再叫一次，所以不合法的設定進不了結算。
        /// </summary>
        /// <exception cref="ArgumentException">任何一項不合法，訊息會說是哪一項。</exception>
        public void ThrowIfInvalid()
        {
            // 「必須大於 0」那幾項，0 多半代表 JSON 的鍵名拼錯、被讀成預設值
            RequirePositive(initialRating, nameof(initialRating));
            RequirePositive(ratingScale, nameof(ratingScale));
            RequirePositive(referenceSeconds, nameof(referenceSeconds));
            RequirePositive(baseKFactor, nameof(baseKFactor));
            RequirePositive(demonstrationMultiplier, nameof(demonstrationMultiplier));

            if (!(dragWeightRatio >= 0.0 && dragWeightRatio <= 1.0))
                throw new ArgumentException(
                    $"SkillRatingSettings：{nameof(dragWeightRatio)} 是 {dragWeightRatio}，必須在 0 ～ 1 之間");
            if (!(hintPenaltySeconds >= 0.0))
                throw new ArgumentException(
                    $"SkillRatingSettings：{nameof(hintPenaltySeconds)} 是 {hintPenaltySeconds}，不能是負的");
        }

        /// <param name="value">值。</param>
        /// <param name="name">欄位名。</param>
        private static void RequirePositive(double value, string name)
        {
            // 寫成 !(value > 0) 而不是 value <= 0：NaN 跟任何數比較都是 false，後者會讓 NaN 溜過去
            if (!(value > 0.0))
                throw new ArgumentException(
                    $"SkillRatingSettings：{name} 是 {value}，必須大於 0（多半是鍵名拼錯、被讀成 0）");
        }
    }
}
