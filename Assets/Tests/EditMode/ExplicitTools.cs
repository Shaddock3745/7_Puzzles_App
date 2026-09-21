using NUnit.Framework;

namespace Tangram.Data.Tests
{
    /// <summary>
    /// ⭐ <b>四個 <c>[Explicit]</c> 工具的英文入口</b>（2026-09-14）。每一支只是轉呼叫原本那個中文名稱的方法，
    /// 工具本身的說明、會寫哪些檔、要跑多久，都在原本那支的 XML doc 裡。
    ///
    /// <b>它只是方便，不是必要</b>：用中文名稱一樣跑得動（Git Bash、PowerShell 都驗過），
    /// 英文入口的好處是短、打的時候不用切輸入法。
    /// （⚠️ 2026-09-14 一度以為「中文名稱篩不到」才加了這支——後來查清楚，篩不到的原因是 <c>[Explicit]</c>，跟中文無關，見下。）
    ///
    /// ⚠️ <b>要用 NUnit 自己的篩選，不能用 <c>--filter</c></b>：<c>--filter</c> 是 VSTest 的篩選，
    /// 在這個套件組合下<b>永遠選不到 <c>[Explicit]</c> 的測試</b>（回「沒有符合的測試」），英文中文都一樣。
    /// <c>dotnet test Tools/DotnetTests -- "NUnit.Where=name == RescaleDifficultyWeights"</c>
    ///
    /// <b>Unity 的 Test Runner 裡會看到兩份</b>（這裡一份、原本一份），跑哪一個都一樣。
    /// ⚠️ <b>不把原本那支改成英文</b>：專案的測試一律用中文句子當名稱，工具也不例外，而且文件到處引用那些名字。
    ///
    /// ⚠️ 每一支都要掛 <c>[Explicit]</c>，否則日常的 Run All 會把它們跑進去（最慢的要十幾分鐘，重算工具還會寫權重檔）。
    /// </summary>
    public class ExplicitTools
    {
        /// <summary>改權重之後的重算工具（換算倍率與目標難度範圍）。<b>會寫權重檔</b>。約 40 秒。</summary>
        [Test, Explicit("重算工具：會寫權重檔")]
        public void RescaleDifficultyWeights() => new DifficultyEvaluatorTests().尺度_權重改過之後重算換算倍率();

        /// <summary>P2-7 生成時間表：權重檔的兩端、800、900 ～ 1600 各一百題。Debug 建置十幾分鐘。</summary>
        [Test, Explicit("P2-7 生成時間：要跑十幾分鐘")]
        public void GenerationTimeTable() => new TargetedPuzzleGeneratorTests().生成時間_幾個目標各生一百題();

        /// <summary>P2-5 批次統計：一千題的特徵分布。約 20 秒。</summary>
        [Test, Explicit("P2-5 批次統計：要數十秒")]
        public void BatchStatistics() => new PuzzleBatchStatisticsTests().批次統計_一千題的特徵分布量得出來();

        /// <summary>縮圖牆（對照牆）的資料與網頁匯出，寫到系統暫存資料夾。約 35 秒。</summary>
        [Test, Explicit("縮圖牆匯出：要數十秒")]
        public void ExportThumbnailWall() => new ThumbnailWallExportTests().縮圖牆_匯出兩千個形狀();

        /// <summary>求解器完整驗證：38 個形狀全部檢查，13 個凸多邊形的解數釘住，印出解數表。Debug 約 2 分半（2026-09-21）。</summary>
        [Test, Explicit("求解器完整驗證：Debug 約 2 分半")]
        public void SolverTable() => new TangramSolverTests().求解器_全部形狀驗證並印出解數表();
    }
}
