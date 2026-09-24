using System.Collections.Generic;
using System.IO;

namespace Tangram.Data.Tests
{
    /// <summary>
    /// ⭐ <b>校準用的那一批題目</b>：13 個凸多邊形 ＋ 25 個具象題，2026-09-20 由使用者用出題工具手拼。
    ///
    /// <b>它跟 <see cref="BaselineBatch"/> 的分工</b>：基準批次是<b>生成器生的</b>，難度那把尺就是在它上面定的；
    /// 這一批是<b>人拼的</b>，尺定好之後拿來從外面量那把尺。兩者都不能互相取代——
    /// 用生成的題目去驗生成的尺是循環論證。
    ///
    /// ⚠️ <b>2026-09-23 起這支只剩「檔案在 repo 的哪裡」這件事。</b>
    /// 解析、解碼、追邊、分凸／具象全部搬到 <see cref="PuzzleLibrary"/>（<c>Data</c> 層）——
    /// 因為遊戲的具象題模式要走同一條路，而<b>同一條路有兩份實作，遲早會有一邊沒跟上，
    /// 症狀是「讀進來了但形狀不對」而且不報錯</b>。
    ///
    /// ⚠️ <b>檔案本身不要手改。</b> 它是出題工具的輸出原封不動複製過來的（一行一筆 JSON Lines）。
    /// 要加題目就用工具拼、再複製一次，不要在編輯器裡改欄位——
    /// <c>puzzleKey</c> 與 <c>links</c> 對不起來的話，<see cref="ConvexPolygonCalibrationTests"/>
    /// 第一條會抓到，但省下那次來回比較划算。
    ///
    /// 💡 <b>遊戲讀的是另一份複本</b>（<c>Assets/Resources/puzzle_library.txt</c>）。
    /// 兩份的內容一樣，但用途不同：這一份是「一組已知答案」，動了會讓校準測試失去意義；
    /// 那一份是題庫，之後會加題目。<c>Resources</c> 進得了 build，測試素材不該進。
    /// </summary>
    public sealed class CalibrationEntries
    {
        /// <summary>素材檔在 repo 裡的相對路徑，找法跟權重檔同一套（<see cref="RepositoryFiles.Find"/>）。</summary>
        public const string RelativePath = "Assets/Tests/EditMode/calibration_entries.jsonl";

        /// <summary>檔案裡應該有幾筆。<b>對不上就是有人動了素材</b>，那時候下面的每一條都在量別的東西。</summary>
        public const int ExpectedEntryCount = 38;

        /// <summary>其中凸多邊形應該有幾個。Wang &amp; Hsiung（1942）證明的就是這個 13。</summary>
        public const int ExpectedConvexCount = 13;

        /// <summary>
        /// 讀進來的題庫，<b>順序照檔案</b>。
        ///
        /// ⚠️ 幾何算一次就存著（38 題要跑 38 × 17 次輪廓追蹤），實測整批約 0.4 秒。
        /// 裡面的東西都是唯讀的，所以共用一份不會有測試互相污染的問題——
        /// 權重那邊之所以「每次讀一份新的」，是因為有測試會在記憶體裡改它。
        /// </summary>
        private static PuzzleLibrary cached;

        /// <summary>讀素材檔。第一次會真的算，之後直接給同一份。</summary>
        /// <returns>讀好的題庫。</returns>
        public static PuzzleLibrary Library() =>
            cached ??= PuzzleLibrary.FromLines(File.ReadAllLines(RepositoryFiles.Find(RelativePath)));

        /// <returns>38 題，順序照檔案。</returns>
        public static IReadOnlyList<PuzzleLibrary.Entry> All() => Library().All;

        /// <returns>13 個凸多邊形，順序照檔案。</returns>
        public static IReadOnlyList<PuzzleLibrary.Entry> ConvexPolygons() => Library().ConvexPolygons;

        /// <returns>25 個具象題（貓、狗、房子、魚、數字、茶壺、衣服、船），順序照檔案。</returns>
        public static IReadOnlyList<PuzzleLibrary.Entry> FigurativeShapes() => Library().FigurativeShapes;
    }
}
