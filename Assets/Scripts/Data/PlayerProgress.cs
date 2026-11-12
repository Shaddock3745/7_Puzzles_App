using System;
using UnityEngine;

namespace Tangram.Data
{
    /// <summary>
    /// 玩家進度存檔 <c>PlayerProgress.json</c> 的內容（issue #39，2026-10-17）。
    ///
    /// <b>只存「關掉 App 再開回來要接得上」的東西</b>：實力與累計局數，加兩個除錯用的欄位。
    /// 刻意不存的（2026-10-17 定）：
    /// <list type="bullet">
    /// <item><b>走勢</b>（<c>SkillRatingTracker.History</c>）：每一筆是整份結算明細加出題紀錄，
    ///   存下去那十幾個欄位全部變成儲存契約，之後改 <see cref="RatingUpdate"/> 就得轉檔；
    ///   而唯一讀走勢的監控面板只在 Editor 裡。等「監控面板進 App」定了再說，真要存就另開一個逐局追加的檔</item>
    /// <item><b>具象題的洗牌袋</b>：重開就重洗，最壞是剛玩過的那一題又出現一次</item>
    /// <item><b>小數的實力</b>：存四捨五入後的整數，記憶體裡照樣留小數（2026-09-13 定）</item>
    /// </list>
    ///
    /// ⚠️ <b>欄位名就是 JSON 的鍵，也是儲存契約</b>（<c>文件/開發須知.md</c>〈儲存契約〉第二條）。
    /// 玩家手上已經有檔案之後改名，<c>JsonUtility</c> 會不吭聲地把舊鍵讀成預設值——
    /// <see cref="formatVersion"/> 與 <see cref="skillRating"/> 讀成 0 會被 <see cref="ThrowIfInvalid"/> 攔下，
    /// 其他三個不會。
    ///
    /// <b>讀寫檔案不在這裡</b>（先寫暫存檔再換掉正本、壞檔改名留著），那是 <c>Core</c> 的 <c>PlayerProgressFile</c>。
    /// 這一支只管「內容合不合法」，所以字串進、字串出。
    /// </summary>
    [Serializable]
    public class PlayerProgress
    {
        /// <summary>
        /// 現在的格式版本。<b>之後加「中途離開的那一局」時要升版</b>。
        /// </summary>
        public const int CurrentFormatVersion = 1;

        /// <summary>
        /// 格式版本。⚠️ <b>讀到不認得的版本一律當壞檔</b>，不去猜它的意思：
        /// 新版 App 寫的檔被舊版讀到時，猜錯的後果是安靜地讀錯實力。
        /// 0 代表這個鍵不在檔案裡（<c>JsonUtility</c> 的預設值）。
        /// </summary>
        public int formatVersion;

        /// <summary>
        /// 實力，<b>四捨五入後的整數</b>（<see cref="SkillRating.RoundRating"/>）。
        /// 重開 App 最多少 0.5 分，但同一場裡不會累積誤差（2026-09-13 定）。
        /// ⚠️ 必須大於 0：0 多半是鍵名拼錯或檔案被截斷，拿 0 去出題會被夾到範圍最低、而且不報錯。
        /// </summary>
        public int skillRating;

        /// <summary>
        /// 累計結算過幾局（所有次開啟加起來）。不結算的關不算——掃描題、關掉 App 走掉的那一局都不算。
        /// </summary>
        public int settledCount;

        /// <summary>
        /// 存檔那時用的難度權重的指紋（<see cref="DifficultyWeights.Fingerprint"/>）。
        ///
        /// <b>為什麼要存</b>：權重一改，同一個實力數字的意思就跟著移動。
        /// ⭐ <b>讀到不一樣時照用、只寫警告</b>（2026-10-17 定）：重算工具會把尺拉回 1200 ± 200，
        /// 實力的意思大致還在；直接重設的話玩家會莫名其妙掉回起始實力。
        /// 可以是空的（沒有權重的場景不會結算，所以正常不會寫出空的），空的也只是警告。
        /// </summary>
        public string weightsFingerprint;

        /// <summary>存檔時間，ISO-8601 UTC（同 <c>ArrangementDto.createdAt</c> 的寫法）。只拿來除錯，不參與任何判斷。</summary>
        public string savedAtUtc;

        /// <summary>組一份要寫進檔案的進度，版本自動填 <see cref="CurrentFormatVersion"/>。</summary>
        /// <param name="skillRating">四捨五入後的實力。</param>
        /// <param name="settledCount">累計結算局數。</param>
        /// <param name="weightsFingerprint">現在用的權重指紋。</param>
        /// <param name="savedAtUtc">存檔時間（UTC）。</param>
        /// <returns>一份合法的進度。</returns>
        /// <exception cref="ArgumentException">實力或局數不合法——那是呼叫端的錯，寫出去的話下次讀會被當壞檔。</exception>
        public static PlayerProgress Create(int skillRating, int settledCount, string weightsFingerprint,
                                            DateTime savedAtUtc)
        {
            var progress = new PlayerProgress
            {
                formatVersion = CurrentFormatVersion,
                skillRating = skillRating,
                settledCount = settledCount,
                weightsFingerprint = weightsFingerprint,
                savedAtUtc = savedAtUtc.ToUniversalTime().ToString("o"),
            };
            progress.ThrowIfInvalid();
            return progress;
        }

        /// <summary>從檔案內容讀回來，讀的當下就檢查完。</summary>
        /// <param name="json">檔案內容。</param>
        /// <returns>檢查過的進度。</returns>
        /// <exception cref="ArgumentException">
        /// 字串是空的、不是 JSON，或任何一項不合法（訊息會說是哪一項）。
        /// <b>所有讀不懂的情況都統一成這一種</b>，呼叫端只要接一種例外就知道「這是壞檔」。
        /// </exception>
        public static PlayerProgress FromJson(string json)
        {
            if (string.IsNullOrWhiteSpace(json))
                throw new ArgumentException("PlayerProgress.FromJson：檔案是空的", nameof(json));

            PlayerProgress progress;
            try
            {
                progress = JsonUtility.FromJson<PlayerProgress>(json);
            }
            catch (Exception error) when (!(error is ArgumentException))
            {
                // Unity 的 JsonUtility 對壞 JSON 丟 ArgumentException，Unity 外（dotnet 的替身）丟的是別的型別。
                // 兩邊統一成 ArgumentException，PlayerProgressFile 才不用知道是在哪裡跑
                throw new ArgumentException($"PlayerProgress.FromJson：不是合法的 JSON（{error.Message}）", nameof(json), error);
            }

            if (progress == null)
                throw new ArgumentException("PlayerProgress.FromJson：讀不出任何東西", nameof(json));

            progress.ThrowIfInvalid();
            return progress;
        }

        /// <summary>寫成檔案內容（有縮排，打開來看得懂）。</summary>
        /// <returns>JSON 字串。</returns>
        public string ToJson() => JsonUtility.ToJson(this, true);

        /// <summary>檢查每一項。<see cref="FromJson"/> 與 <see cref="Create"/> 都會自動叫。</summary>
        /// <exception cref="ArgumentException">任何一項不合法，訊息會說是哪一項。</exception>
        public void ThrowIfInvalid()
        {
            if (formatVersion != CurrentFormatVersion)
                throw new ArgumentException(
                    $"PlayerProgress：{nameof(formatVersion)} 是 {formatVersion}，這個版本只認得 {CurrentFormatVersion}" +
                    "（0 代表檔案裡沒有這個鍵）");
            if (skillRating <= 0)
                throw new ArgumentException(
                    $"PlayerProgress：{nameof(skillRating)} 是 {skillRating}，必須大於 0（多半是鍵名拼錯、被讀成 0）");
            if (settledCount < 0)
                throw new ArgumentException(
                    $"PlayerProgress：{nameof(settledCount)} 是 {settledCount}，不能是負的");
        }
    }
}
