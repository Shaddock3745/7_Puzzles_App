using System;
using System.IO;
using NUnit.Framework;

namespace Tangram.Data.Tests
{
    /// <summary>
    /// 測試與工具找 repo 裡的檔案（2026-09-13 從 <c>DifficultyEvaluatorTests</c> 抽出來）。
    ///
    /// <b>為什麼要往上找</b>：兩種跑法的起點不一樣——Unity 的 Test Runner 目前目錄是專案根目錄，
    /// <c>dotnet test</c> 的執行檔在 <c>Tools/DotnetTests/bin/...</c>。往上找兩邊都找得到。
    /// </summary>
    public static class RepositoryFiles
    {
        /// <summary>
        /// 權重檔在 repo 裡的相對路徑。⭐ <b>權重唯一的源頭</b>，遊戲從 <c>Resources</c> 讀的就是它。
        /// </summary>
        public const string DifficultyWeightsRelativePath = "Assets/Resources/DifficultyWeights.json";

        /// <summary>ELO 設定檔在 repo 裡的相對路徑。跟權重檔同一套規則：唯一的源頭。</summary>
        public const string SkillRatingSettingsRelativePath = "Assets/Resources/SkillRatingSettings.json";

        /// <summary>從目前目錄與執行檔目錄往上找 repo 裡的檔案。找不到就讓測試失敗。</summary>
        /// <param name="relativePath">相對於專案根目錄的路徑。</param>
        /// <returns>完整路徑。</returns>
        public static string Find(string relativePath)
        {
            foreach (string start in new[] { Directory.GetCurrentDirectory(), AppContext.BaseDirectory })
                for (var directory = new DirectoryInfo(start); directory != null; directory = directory.Parent)
                {
                    string candidate = Path.Combine(directory.FullName, relativePath);
                    if (File.Exists(candidate)) return candidate;
                }

            Assert.Fail($"從 {Directory.GetCurrentDirectory()} 往上找不到 {relativePath}");
            return null;
        }

        /// <summary>
        /// 讀權重檔。<b>每次都讀一份新的</b>：有些測試會在記憶體裡改權重（例如故意打錯鍵），
        /// 共用同一個物件的話會污染下一個測試，而且那種失敗跟執行順序有關、時好時壞。
        /// </summary>
        /// <returns>檢查過的權重。</returns>
        public static DifficultyWeights LoadDifficultyWeights() =>
            DifficultyWeights.FromJson(File.ReadAllText(Find(DifficultyWeightsRelativePath)));

        /// <summary>讀 ELO 設定檔，每次讀一份新的（理由同 <see cref="LoadDifficultyWeights"/>）。</summary>
        /// <returns>檢查過的設定。</returns>
        public static SkillRatingSettings LoadSkillRatingSettings() =>
            SkillRatingSettings.FromJson(File.ReadAllText(Find(SkillRatingSettingsRelativePath)));
    }
}
