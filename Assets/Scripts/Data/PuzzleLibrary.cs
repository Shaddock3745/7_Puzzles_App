using System;
using System.Collections.Generic;
using UnityEngine;

namespace Tangram.Data
{
    /// <summary>
    /// ⭐ <b>題庫</b>：出題工具存出來的那些題目，讀回來變成可以直接進關的東西（2026-09-23）。
    ///
    /// <b>為什麼是一支獨立的程式</b>：這條路（一行 JSON → 解碼成七片 → 重新追邊得到外框 → 算特徵）
    /// 原本只寫在測試的 <c>CalibrationEntries</c> 裡。遊戲要載入具象題時需要一模一樣的一條路——
    /// 抄第二遍的話，哪天追邊或存檔格式改了，會有一邊沒跟上，而症狀是「題目讀進來了但形狀不對」，
    /// <b>不會報錯</b>。所以邏輯搬來這裡，測試那支改成薄薄一層（它只多管「檔案在 repo 的哪裡」）。
    ///
    /// <b>為什麼放 <c>Data</c> 不放 <c>GameObject</c></b>：這裡一行 Unity 的畫面程式都沒有，
    /// 放 <c>Data</c> 才進得了 <c>dotnet test</c>（<c>GameObject/</c> 那層只有 Unity 驗得到）。
    /// 讀檔那一步刻意留在外面——遊戲走 <c>Resources</c>、測試走 repo 的相對路徑，
    /// 來源不同但解析是同一份。
    ///
    /// ⚠️ <b>難度刻意不存在題庫裡，要當場算</b>：難度是特徵配上權重才有的，而權重是可變的。
    /// 存起來就有兩個來源，調權重時一定有一邊沒跟上。特徵只跟幾何有關，算一次存著是安全的。
    /// </summary>
    public sealed class PuzzleLibrary
    {
        /// <summary>
        /// 檔案裡一行的格式，<b>必須跟出題工具寫出來的一致</b>（<c>PuzzleEntryToolController.SavedEntry</c>）。
        ///
        /// 💡 <b>為什麼是 public</b>：它就是檔案格式，要另外寫東西讀這個檔的人需要它；
        /// 而且欄位由 <c>JsonUtility</c> 用反射填，私有的話編譯器會警告「從未指派」。
        /// </summary>
        [Serializable]
        public class EntryDto
        {
            /// <summary>人取的名字，例如「船」。</summary>
            public string name;

            /// <summary>相對編碼，裡面已經有 <c>key</c> 與 <c>puzzleKey</c>。</summary>
            public ArrangementDto arrangement;
        }

        /// <summary>
        /// 一題：名字，加上所有<b>跟權重無關</b>的東西。
        /// </summary>
        public sealed class Entry
        {
            /// <summary>人取的名字，例如「船」。⚠️ 只拿來讓訊息看得懂，<b>不要拿它判斷是不是凸多邊形</b>。</summary>
            public readonly string Name;

            /// <summary>從檔案讀進來的相對編碼，<c>Key</c> 與 <c>PuzzleKey</c> 是<b>存檔裡的值</b>，不是重算的。</summary>
            public readonly Arrangement Arrangement;

            /// <summary>解碼出來的七片。<b>它同時就是這一題的參考解</b>。</summary>
            public readonly Placement[] Pieces;

            /// <summary>從七片重新追邊得到的題目，<c>Key</c> 是<b>重算的</b>。</summary>
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
            /// 外框是不是凸的。<b>凸的＝那 13 個凸多邊形，其餘就是具象題</b>。
            ///
            /// ⚠️ <b>刻意不看名字</b>（「五邊形1」這種）：名字是人打的，改個名分組就跟著錯，而且不會報錯。
            /// 判準用專案自己的定義——<b>沒有凹角、沒有洞</b>。
            /// </summary>
            public bool IsConvex => ShapeFeatures.ConcaveAngleCount == 0 && ShapeFeatures.HoleCount == 0;

            /// <summary>用指定的權重算這一題的難度。</summary>
            /// <param name="weights">權重，從權重檔讀。</param>
            /// <returns>難度值。</returns>
            public int Score(DifficultyWeights weights) =>
                DifficultyEvaluator.Score(ShapeFeatures, ForcedPieceCount, SolutionFeatures, weights);

            /// <returns>訊息裡用得到的一行。</returns>
            public override string ToString() => Name;
        }

        private readonly List<Entry> all = new List<Entry>();
        private readonly List<Entry> convexPolygons = new List<Entry>();
        private readonly List<Entry> figurativeShapes = new List<Entry>();

        private PuzzleLibrary() { }

        /// <summary>全部的題目，<b>順序照檔案</b>。</summary>
        public IReadOnlyList<Entry> All => all;

        /// <summary>外框是凸多邊形的那些（那 13 個），順序照檔案。</summary>
        public IReadOnlyList<Entry> ConvexPolygons => convexPolygons;

        /// <summary>
        /// 具象題（貓、狗、房子、魚、數字、茶壺、衣服、船）——<b>就是「不是凸多邊形」的那些</b>，順序照檔案。
        /// 遊戲的具象題模式抽的就是這一批。
        /// </summary>
        public IReadOnlyList<Entry> FigurativeShapes => figurativeShapes;

        /// <summary>
        /// 從整份檔案的文字讀出題庫（一行一筆，JSON Lines）。
        ///
        /// <b>空行會跳過</b>；換行是 <c>\n</c> 還是 <c>\r\n</c> 都可以——
        /// Windows 存的檔進 <c>Resources</c> 之後帶著 <c>\r</c>，不處理的話 <c>JsonUtility</c> 會解出怪東西。
        /// </summary>
        /// <param name="text">整份檔案的內容。</param>
        /// <returns>讀好的題庫。</returns>
        /// <exception cref="ArgumentNullException"><paramref name="text"/> 是 null。</exception>
        /// <exception cref="ArgumentException">某一行不合法，訊息裡會講是第幾行。</exception>
        public static PuzzleLibrary FromText(string text)
        {
            if (text == null) throw new ArgumentNullException(nameof(text));
            return FromLines(text.Split('\n'));
        }

        /// <summary>
        /// 從一行一筆的內容讀出題庫。
        /// </summary>
        /// <param name="lines">每一行的文字；空行會跳過，前後空白會去掉。</param>
        /// <returns>讀好的題庫。</returns>
        /// <exception cref="ArgumentNullException"><paramref name="lines"/> 是 null。</exception>
        /// <exception cref="ArgumentException">某一行不合法，訊息裡會講是第幾行。</exception>
        public static PuzzleLibrary FromLines(IEnumerable<string> lines)
        {
            if (lines == null) throw new ArgumentNullException(nameof(lines));

            var library = new PuzzleLibrary();
            int lineNumber = 0;

            foreach (string rawLine in lines)
            {
                lineNumber++;
                string line = rawLine == null ? string.Empty : rawLine.Trim();
                if (line.Length == 0) continue;

                Entry entry;
                try
                {
                    EntryDto dto = JsonUtility.FromJson<EntryDto>(line);

                    // ⚠️ JsonUtility 對不認得的欄位不吭聲，整行是別的東西時 dto 會是「每個欄位都是預設值」。
                    // 不擋的話症狀是「讀進來了，但 arrangement 是 null」，然後在別的地方炸
                    if (dto == null || dto.arrangement == null)
                        throw new ArgumentException("這一行沒有 arrangement 欄位，可能不是題庫的格式。");

                    entry = new Entry(dto.name, Serialization.FromDto(dto.arrangement));
                }
                catch (Exception error)
                {
                    // 帶上行號：一份三十幾行的檔案，沒有行號等於要整份重看
                    throw new ArgumentException($"題庫第 {lineNumber} 行讀不進來：{error.Message}", error);
                }

                library.all.Add(entry);
                (entry.IsConvex ? library.convexPolygons : library.figurativeShapes).Add(entry);
            }

            return library;
        }
    }

    /// <summary>
    /// ⭐ <b>洗牌袋</b>：隨機抽，但<b>一輪之內不重複</b>，抽完一輪才重新洗（2026-09-23）。
    ///
    /// <b>為什麼不直接每次隨機取一個</b>：25 題裡連續兩次抽到同一題的機率是 4%，
    /// 而「隨機」這件事在影片裡只演三、四次——撞到一次就會看起來像壞掉。
    ///
    /// <b>為什麼回傳索引而不是題目</b>：這樣它跟題庫無關，測得起來也用得到別的地方。
    ///
    /// ⚠️ <b>跨輪的交界也擋了</b>：新的一輪如果第一個正好是上一輪最後抽到的那個，
    /// 就把它跟別的換掉——不然「一輪內不重複」在交界處等於沒有。
    /// 只有一個項目時不擋，那時候本來就只能一直是同一個。
    /// </summary>
    public sealed class PuzzleShuffleBag
    {
        private readonly int itemCount;
        private readonly System.Random random;
        private readonly List<int> remaining = new List<int>();

        /// <summary>上一次抽到的索引，還沒抽過是 −1。跨輪的交界要用它。</summary>
        private int lastDrawn = -1;

        /// <summary>
        /// 開一個洗牌袋。
        /// </summary>
        /// <param name="itemCount">有幾個可以抽的東西，索引是 0 到 <paramref name="itemCount"/> − 1。</param>
        /// <param name="random">亂數來源。固定種子就能重現同一串。</param>
        /// <exception cref="ArgumentOutOfRangeException"><paramref name="itemCount"/> 小於 1。</exception>
        /// <exception cref="ArgumentNullException"><paramref name="random"/> 是 null。</exception>
        public PuzzleShuffleBag(int itemCount, System.Random random)
        {
            if (itemCount < 1)
                throw new ArgumentOutOfRangeException(nameof(itemCount), itemCount, "至少要有一個可以抽的東西。");

            this.itemCount = itemCount;
            this.random = random ?? throw new ArgumentNullException(nameof(random));
        }

        /// <summary>這一輪還剩幾個沒抽。</summary>
        public int RemainingCount => remaining.Count;

        /// <summary>抽一個。</summary>
        /// <returns>索引，0 到 <c>itemCount</c> − 1。</returns>
        public int Next()
        {
            if (remaining.Count == 0) Refill();

            int last = remaining.Count - 1;
            int drawn = remaining[last];
            remaining.RemoveAt(last);
            lastDrawn = drawn;
            return drawn;
        }

        /// <summary>重新裝滿並洗牌。洗完之後<b>下一個要抽的</b>不會跟上一輪最後抽到的重複。</summary>
        private void Refill()
        {
            for (int i = 0; i < itemCount; i++) remaining.Add(i);

            // Fisher-Yates
            for (int i = remaining.Count - 1; i > 0; i--)
            {
                int j = random.Next(i + 1);
                (remaining[i], remaining[j]) = (remaining[j], remaining[i]);
            }

            // Next() 是從尾端取的，所以「下一個」是最後一個。跟上一輪撞到就跟第一個換
            if (itemCount > 1 && remaining[remaining.Count - 1] == lastDrawn)
                (remaining[remaining.Count - 1], remaining[0]) = (remaining[0], remaining[remaining.Count - 1]);
        }
    }
}
