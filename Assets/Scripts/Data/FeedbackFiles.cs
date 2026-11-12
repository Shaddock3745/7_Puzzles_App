using System.Globalization;
using System.IO;

namespace Tangram.Data
{
    /// <summary>
    /// 回饋收集的<b>暫存檔</b>命名（2026-09-14）。
    ///
    /// <b>為什麼要暫存檔</b>：每一位受試者先寫進自己的暫存檔，
    /// <b>做完或「結束並保存」才整份併進正式結果檔</b>；中止就刪掉暫存檔。
    /// 這樣正式結果檔裡<b>永遠沒有被中止的人</b>，而且當掉時暫存檔還在，資料救得回來。
    /// ❌ 否決「中止時回頭刪結果檔裡那幾行」：要整份重寫，寫到一半失敗整個檔案都可能毀掉。
    ///
    /// 放 Data 是為了測得到：編號要從檔名讀回來，而那一步錯了的症狀是「下一位拿到跟遺留暫存檔一樣的編號」，
    /// 兩份資料會被當成同一個人，不報錯。
    /// </summary>
    public static class FeedbackFiles
    {
        /// <summary>暫存檔所在的子資料夾（在結果檔旁邊）。</summary>
        public const string UnfinishedDirectoryName = "unfinished";

        private const string Prefix = "participant_";
        private const string Suffix = ".partial.jsonl";

        /// <summary>第 N 位受試者的暫存檔名，例如 <c>participant_0007.partial.jsonl</c>（補零是為了檔案總管排得對）。</summary>
        /// <param name="participantNumber">受試者編號。</param>
        /// <returns>檔名（不含資料夾）。</returns>
        public static string PartialFileName(int participantNumber) =>
            Prefix + participantNumber.ToString("D4", CultureInfo.InvariantCulture) + Suffix;

        /// <summary>
        /// 從暫存檔名讀回受試者編號。不是暫存檔名、或編號不是正整數時回 false。
        /// </summary>
        /// <param name="fileNameOrPath">檔名或完整路徑。</param>
        /// <param name="participantNumber">讀到的編號；讀不到是 0。</param>
        /// <returns>讀不讀得到。</returns>
        public static bool TryParsePartialFileName(string fileNameOrPath, out int participantNumber)
        {
            participantNumber = 0;
            if (string.IsNullOrEmpty(fileNameOrPath)) return false;

            string fileName = Path.GetFileName(fileNameOrPath);
            if (!fileName.StartsWith(Prefix) || !fileName.EndsWith(Suffix)) return false;

            string digits = fileName.Substring(Prefix.Length, fileName.Length - Prefix.Length - Suffix.Length);
            return int.TryParse(digits, NumberStyles.None, CultureInfo.InvariantCulture, out participantNumber)
                   && participantNumber > 0;
        }
    }
}
