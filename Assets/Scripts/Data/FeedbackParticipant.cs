using System;
using System.Text;

namespace Tangram.Data
{
    /// <summary>受試者的年齡層（回饋收集，2026-09-13）。目標使用者是 6 歲起的小孩，所以要分得出大人小孩。</summary>
    public enum AgeGroup
    {
        /// <summary>8 歲以下。</summary>
        EightOrUnder,

        /// <summary>9 ～ 12 歲。</summary>
        NineToTwelve,

        /// <summary>13 ～ 17 歲。</summary>
        ThirteenToSeventeen,

        /// <summary>18 歲以上。</summary>
        EighteenOrOver,
    }

    /// <summary>
    /// 受試者以前玩過幾次七巧板（回饋收集，2026-09-13）。
    /// 用次數分三級而不是「是／否」：權重最大的偏差來源是「很會玩的人」，是否分不出來。
    /// </summary>
    public enum TangramExperience
    {
        /// <summary>0 次。</summary>
        Never,

        /// <summary>1 ～ 10 次。</summary>
        OneToTenTimes,

        /// <summary>超過 10 次。</summary>
        MoreThanTenTimes,
    }

    /// <summary>
    /// 輪到他之前，有沒有在旁邊看別人玩（2026-09-14，由顧場的人選）。
    ///
    /// <b>為什麼要記</b>：會一次帶一組小朋友來測。每個人一批不同的題，擋得掉「看過同一題的解法」，
    /// 但<b>擋不掉「看別人玩學到的策略」</b>（例如「大三角先放」）。擋不掉的就記下來，分析時才能把有看、沒看的分開比。
    /// </summary>
    public enum WatchedOthers
    {
        /// <summary>沒看。</summary>
        DidNotWatch,

        /// <summary>看了一下。</summary>
        WatchedBriefly,

        /// <summary>看了很久。</summary>
        WatchedLong,
    }

    /// <summary>
    /// 受試者代號（2026-09-14）：<b>小朋友自己打的英文名字</b>，例如「Amy」。
    ///
    /// <b>為什麼要有</b>：同一群小朋友可能不同天再來。沒有代號的話，第二次會被當成另一個人——
    /// 人數灌水、很會玩的人被算兩遍、學習效應混進題目難度。有代號就連得起來，
    /// 而<b>同一個人評比較多題</b>正是最可信的資料（主觀評分最準的是「同一個人心裡的排序」）。
    ///
    /// ⚠️ <b>存的時候照打的樣子，比對的時候不分大小寫</b>（<see cref="Comparer"/>）：設計上不全轉大寫，
    /// 但小朋友第一次打「Amy」、第二次打「amy」是同一個人——大小寫分開算的話就連不起來，而且不報錯。
    ///
    /// ⚠️ <b>英文名字不是唯一的</b>：不同組各有一個 Amy 的話會被當成同一個人。開始面板會顯示「第幾次來」，
    /// 顧場的人看到不該來過的小朋友顯示「第 2 次」時，要請他在名字後面加字區分（例如 Amy2）。
    ///
    /// ⚠️ <b>結果檔從此含有小朋友的英文名字</b>：不進版控（<c>.gitignore</c> 已擋 <c>Tools/Feedback/</c>），
    /// 對外文件只能放統計結果，不能放原始資料。
    /// </summary>
    public static class FeedbackParticipantCode
    {
        /// <summary>代號最長幾個字元。</summary>
        public const int MaximumLength = 20;

        /// <summary>
        /// 比對代號用的比較器：<b>不分大小寫</b>。字典的 key、「來過幾次」的計數都要用它。
        /// </summary>
        public static readonly StringComparer Comparer = StringComparer.OrdinalIgnoreCase;

        /// <summary>
        /// 把輸入的英文名字整理成標準形式：去掉前後的半形空白、中間連續的半形空白縮成一個，<b>大小寫照原樣</b>。<b>不合法時回 null</b>。
        ///
        /// <b>規則只有這幾條</b>（2026-09-14 對過一次，說明與程式必須一致）：
        /// <list type="number">
        /// <item>合法字元：<b>半形</b>英文字母、數字、<b>半形空白</b>、減號 <c>-</c>、撇號 <c>'</c>（O'Neil、Mary-Jane、Mary Ann 都行）</item>
        /// <item>其他字元<b>不管出現在哪裡</b>都擋，包括 Tab、換行、<b>全形空白</b>、全形字母（中文輸入法開著時打出來的「Ａｍｙ」）、中文</item>
        /// <item>前後的半形空白去掉，中間連續的半形空白縮成一個</item>
        /// <item>整理完最多 <see cref="MaximumLength"/> 個字元</item>
        /// </list>
        /// ⚠️ <b>不能用 <c>string.Trim()</c></b>：它會把頭尾的全形空白、換行也刪掉——
        /// 結果「Amy　」（尾巴一個全形空白）會通過，同一個全形空白放中間卻被擋，規則前後不一致（2026-09-14 發現）。
        /// </summary>
        /// <param name="rawCode">輸入框裡的文字。</param>
        /// <returns>標準形式；空的、太長、或有不合法的字元時是 null。</returns>
        public static string Normalize(string rawCode)
        {
            if (rawCode == null) return null;

            var builder = new StringBuilder(rawCode.Length);
            bool pendingSpace = false;
            foreach (char character in rawCode)
            {
                if (character == ' ')
                {
                    // 開頭的空白不留（builder 還是空的）；中間的先記著，遇到下一個字元才補一個；結尾的永遠補不上
                    pendingSpace = builder.Length > 0;
                    continue;
                }

                bool allowed = (character >= 'a' && character <= 'z') || (character >= 'A' && character <= 'Z')
                               || (character >= '0' && character <= '9') || character == '-' || character == '\'';
                if (!allowed) return null;

                if (pendingSpace) builder.Append(' ');
                pendingSpace = false;
                builder.Append(character);
            }

            return builder.Length == 0 || builder.Length > MaximumLength ? null : builder.ToString();
        }
    }

    /// <summary>
    /// 一位受試者開始之前填好的資料（2026-09-14）。建構時就檢查完，進得了 <c>FeedbackSession</c> 的一定是合法的。
    /// </summary>
    public sealed class FeedbackParticipantProfile
    {
        /// <summary>標準形式的英文名字（大小寫照打的樣子，見 <see cref="FeedbackParticipantCode.Normalize"/>）。比對時要用 <see cref="FeedbackParticipantCode.Comparer"/>。</summary>
        public readonly string Code;

        /// <summary>
        /// 這是這個代號第幾次來（從 1 起）。<b>由程式從結果檔算，不靠人記</b>。
        /// 分析時用它分開學習效應：第二次一定比較熟練。
        /// </summary>
        public readonly int VisitNumber;

        /// <summary>年齡層。</summary>
        public readonly AgeGroup AgeGroup;

        /// <summary>玩過幾次七巧板。</summary>
        public readonly TangramExperience Experience;

        /// <summary>輪到他之前有沒有在旁邊看別人玩。</summary>
        public readonly WatchedOthers WatchedOthers;

        /// <param name="code">代號，會先整理成標準形式。</param>
        /// <param name="visitNumber">第幾次來，必須 ≥ 1。</param>
        /// <param name="ageGroup">年齡層。</param>
        /// <param name="experience">玩過幾次七巧板。</param>
        /// <param name="watchedOthers">有沒有看別人玩。</param>
        /// <exception cref="ArgumentException">代號不合法。</exception>
        /// <exception cref="ArgumentOutOfRangeException">第幾次小於 1。</exception>
        public FeedbackParticipantProfile(string code, int visitNumber, AgeGroup ageGroup,
                                          TangramExperience experience, WatchedOthers watchedOthers)
        {
            Code = FeedbackParticipantCode.Normalize(code)
                   ?? throw new ArgumentException($"名字「{code}」不合法：只能用半形英文字母、數字、空白、-、'，最多 {FeedbackParticipantCode.MaximumLength} 字", nameof(code));
            if (visitNumber < 1)
                throw new ArgumentOutOfRangeException(nameof(visitNumber), visitNumber, "第幾次從 1 起");

            VisitNumber = visitNumber;
            AgeGroup = ageGroup;
            Experience = experience;
            WatchedOthers = watchedOthers;
        }
    }
}
