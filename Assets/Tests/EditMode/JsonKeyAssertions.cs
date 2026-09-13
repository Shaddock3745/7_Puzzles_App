using System;
using System.Collections.Generic;
using System.Reflection;
using System.Text.RegularExpressions;
using NUnit.Framework;

namespace Tangram.Data.Tests
{
    /// <summary>
    /// 比對 JSON 設定檔的<b>鍵名</b>跟類別的<b>欄位名</b>（2026-09-13 從 <c>DifficultyEvaluatorTests</c> 抽出來，
    /// 權重檔與 ELO 設定檔共用）。
    ///
    /// ⚠️ <b>為什麼需要它</b>：<c>JsonUtility</c> 碰到拼錯的鍵<b>不會吭聲</b>——檔案裡的 <c>"weigth"</c> 被忽略，
    /// 類別裡的 <c>weight</c> 讀成 0。讀檔時的檢查攔得住「0 一定是錯」的欄位，
    /// 但<b>合法值包含 0 的欄位</b>（權重、拖曳比例、提示代價）只有比對鍵名攔得住。
    /// </summary>
    public static class JsonKeyAssertions
    {
        /// <summary>
        /// 檔案裡的每一個鍵都要是某個類別的公開欄位。
        /// 鍵 = 雙引號包起來、後面接冒號的字；值（例如 <c>"perimeter"</c>）後面接的是逗號或換行，不會被抓到。
        /// </summary>
        /// <param name="text">JSON 文字。</param>
        /// <param name="fileLabel">檔名，寫進失敗訊息。</param>
        /// <param name="types">檔案裡會出現的類別（巢狀的也要列）。</param>
        public static void EveryKeyIsKnown(string text, string fileLabel, params Type[] types)
        {
            var knownNames = new HashSet<string>();
            foreach (Type type in types)
                foreach (FieldInfo field in PublicFieldsOf(type))
                    knownNames.Add(field.Name);

            foreach (Match match in Regex.Matches(text, "\"(\\w+)\"\\s*:"))
                Assert.IsTrue(knownNames.Contains(match.Groups[1].Value),
                    $"{fileLabel} 裡有程式不認得的鍵「{match.Groups[1].Value}」——" +
                    "拼錯的話，它原本要填的欄位會被讀成 0");
        }

        /// <summary>
        /// 類別的每一個公開欄位都要在檔案裡出現剛好 <paramref name="expectedCount"/> 次。
        /// 巢狀陣列裡的類別傳陣列長度——這樣「某一筆少了一個欄位」也抓得到。
        /// </summary>
        /// <param name="text">JSON 文字。</param>
        /// <param name="fileLabel">檔名，寫進失敗訊息。</param>
        /// <param name="type">類別。</param>
        /// <param name="expectedCount">每個欄位應該出現幾次。</param>
        public static void EveryFieldAppears(string text, string fileLabel, Type type, int expectedCount)
        {
            foreach (FieldInfo field in PublicFieldsOf(type))
                Assert.AreEqual(expectedCount, Regex.Matches(text, $"\"{Regex.Escape(field.Name)}\"\\s*:").Count,
                    $"{fileLabel} 裡「{field.Name}」應該出現 {expectedCount} 次——少掉的會被讀成 0");
        }

        /// <summary>
        /// 把第一個 <c>"name": 數字</c> 的數字換掉。給「壞掉的內容讀檔時就丟例外」那類測試用——
        /// 從真的設定檔出發、一次只弄壞一個地方，證明的才是「就是這一處讓它失敗」。
        /// </summary>
        /// <param name="text">JSON 文字。</param>
        /// <param name="name">鍵名。</param>
        /// <param name="newValue">新的值（原樣寫進去）。</param>
        /// <returns>換過的文字。</returns>
        public static string ReplaceFirstNumber(string text, string name, string newValue)
        {
            var pattern = new Regex($"(\"{Regex.Escape(name)}\"\\s*:\\s*)-?[0-9.eE+-]+");
            Assert.IsTrue(pattern.IsMatch(text), $"設定檔裡找不到「{name}」");
            return pattern.Replace(text, match => match.Groups[1].Value + newValue, 1);
        }

        /// <param name="type">類別。</param>
        /// <returns>公開的實例欄位（<c>JsonUtility</c> 讀寫的就是這些）。</returns>
        private static FieldInfo[] PublicFieldsOf(Type type) =>
            type.GetFields(BindingFlags.Public | BindingFlags.Instance);
    }
}
