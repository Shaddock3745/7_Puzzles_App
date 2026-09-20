using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;

namespace Tangram.Data.Tests
{
    /// <summary>
    /// ⭐ <b>校準用的那一批題目</b>：13 個凸多邊形 ＋ 25 個具象題，2026-09-20 由使用者用出題工具手拼。
    ///
    /// <b>它跟 <see cref="BaselineBatch"/> 的分工</b>：基準批次是<b>生成器生的</b>，難度那把尺就是在它上面定的；
    /// 這一批是<b>人拼的</b>，尺定好之後拿來從外面量那把尺。兩者都不能互相取代——
    /// 用生成的題目去驗生成的尺是循環論證。
    ///
    /// <b>為什麼是測試素材，不是遊戲題庫</b>（2026-09-20 使用者定）：這一批的用途是「一組已知答案」。
    /// 放進 <c>Resources</c> 讓遊戲讀得到的話，等於順便回答了「什麼時候載入題庫、玩家怎麼抽到具象題、
    /// 精選題跟生成題混不混、ELO 算不算具象題」——那幾題排在 P2-8b 與 P3，還沒討論過。
    /// 要接進遊戲是另一張卡，不是這一張。
    ///
    /// ⚠️ <b>檔案本身不要手改。</b> 它是出題工具的輸出原封不動複製過來的（一行一筆 JSON Lines）。
    /// 要加題目就用工具拼、再複製一次，不要在編輯器裡改欄位——
    /// <c>puzzleKey</c> 與 <c>links</c> 對不起來的話，<see cref="ConvexPolygonCalibrationTests"/>
    /// 第一條會抓到，但省下那次來回比較划算。
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
        /// 一筆存檔的形狀，<b>必須跟出題工具寫出來的一致</b>（<c>PuzzleEntryToolController.SavedEntry</c>）。
        ///
        /// 💡 <b>為什麼是 public</b>：它就是素材檔的格式，要另外寫東西讀這個檔的人需要它；
        /// 而且欄位由 <c>JsonUtility</c> 用反射填，私有的話編譯器會警告「從未指派」。
        /// </summary>
        [Serializable]
        public class CalibrationEntryDto
        {
            /// <summary>人取的名字。</summary>
            public string name;

            /// <summary>相對編碼，裡面已經有 <c>key</c> 與 <c>puzzleKey</c>。</summary>
            public ArrangementDto arrangement;
        }

        /// <summary>
        /// 一題：名字，加上所有<b>跟權重無關</b>的東西。
        ///
        /// <b>為什麼難度不存在這裡、要當場算</b>：難度是特徵配上權重才有的，而權重是可變的。
        /// 存起來的話「這一題幾分」就有兩個來源，哪天調權重就會有一個沒跟上——
        /// 那正是 <c>DifficultyBreakdown</c> 的 XML doc 在防的同一件事。
        /// 特徵本身只跟幾何有關，算一次存著是安全的。
        /// </summary>
        public sealed class Entry
        {
            /// <summary>人取的名字，例如「船」。⚠️ 只拿來讓失敗訊息看得懂，<b>不要拿它判斷是不是凸多邊形</b>。</summary>
            public readonly string Name;

            /// <summary>從檔案讀進來的相對編碼，<c>Key</c> 與 <c>PuzzleKey</c> 是<b>存檔裡的值</b>，不是重算的。</summary>
            public readonly Arrangement Arrangement;

            /// <summary>解碼出來的七片。</summary>
            public readonly Placement[] Pieces;

            /// <summary>從七片重新追邊得到的題目，<c>Key</c> 是<b>重算的</b>。跟上面那個比對就是第一條測試。</summary>
            public readonly Puzzle Puzzle;

            /// <summary>只看形狀的特徵。</summary>
            public readonly DifficultyFeatures ShapeFeatures;

            /// <summary>被外框逼出來的片數。</summary>
            public readonly int ForcedPieceCount;

            /// <summary>看參考解的特徵。</summary>
            public readonly SolutionFeatures SolutionFeatures;

            internal Entry(string name, Arrangement arrangement)
            {
                Name = name;
                Arrangement = arrangement;
                Pieces = arrangement.Decode();
                Puzzle = Puzzle.FromPlacements(Pieces, PuzzleSource.UserDrawn);
                ShapeFeatures = DifficultyFeatures.Measure(Puzzle.Walks);
                ForcedPieceCount = DifficultyFeatures.ForcedPieceCountOf(Puzzle.Walks);
                SolutionFeatures = SolutionFeatures.Measure(Pieces);
            }

            /// <summary>
            /// 外框是不是凸的。
            ///
            /// ⚠️ <b>刻意不看名字</b>（「五邊形1」這種）：名字是人打的，改個名分組就跟著錯，而且不會報錯。
            /// 判準用專案自己的定義——<b>沒有凹角、沒有洞</b>。實測這一批剛好切成 13 ／ 25，
            /// 凸的那 13 個凹角全是 0，其餘每一個都 ≥ 1，沒有模稜兩可的。
            /// </summary>
            public bool IsConvex => ShapeFeatures.ConcaveAngleCount == 0 && ShapeFeatures.HoleCount == 0;

            /// <summary>用指定的權重算這一題的難度。</summary>
            /// <param name="weights">權重，從權重檔讀。</param>
            /// <returns>難度值。</returns>
            public int Score(DifficultyWeights weights) =>
                DifficultyEvaluator.Score(ShapeFeatures, ForcedPieceCount, SolutionFeatures, weights);

            /// <returns>失敗訊息用得到的一行。</returns>
            public override string ToString() => Name;
        }

        /// <summary>
        /// 讀進來的全部題目，<b>順序照檔案</b>。
        ///
        /// ⚠️ 幾何算一次就存著（38 題要跑 38 × 17 次輪廓追蹤），實測整批約 0.4 秒。
        /// 裡面的東西都是唯讀的，所以共用一份不會有測試互相污染的問題——
        /// 權重那邊之所以「每次讀一份新的」，是因為有測試會在記憶體裡改它。
        /// </summary>
        private static IReadOnlyList<Entry> cached;

        /// <summary>讀素材檔。第一次會真的算，之後直接給同一份。</summary>
        /// <returns>38 題，順序照檔案。</returns>
        public static IReadOnlyList<Entry> All()
        {
            if (cached != null) return cached;

            var entries = new List<Entry>();
            foreach (string rawLine in File.ReadAllLines(RepositoryFiles.Find(RelativePath)))
            {
                string line = rawLine.Trim();
                if (line.Length == 0) continue;

                CalibrationEntryDto dto = JsonUtility.FromJson<CalibrationEntryDto>(line);
                entries.Add(new Entry(dto.name, Serialization.FromDto(dto.arrangement)));
            }

            cached = entries;
            return cached;
        }

        /// <returns>13 個凸多邊形，順序照檔案。</returns>
        public static List<Entry> ConvexPolygons() => Where(true);

        /// <returns>25 個具象題（貓、狗、房子、魚、數字、茶壺、衣服、船），順序照檔案。</returns>
        public static List<Entry> FigurativeShapes() => Where(false);

        private static List<Entry> Where(bool convex)
        {
            var picked = new List<Entry>();
            foreach (Entry entry in All())
                if (entry.IsConvex == convex) picked.Add(entry);
            return picked;
        }
    }
}
