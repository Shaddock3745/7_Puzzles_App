using System;
using System.Collections.Generic;
using System.IO;
using NUnit.Framework;

namespace Tangram.Data.Tests
{
    /// <summary>
    /// <see cref="PuzzleLibrary"/> 與 <see cref="PuzzleShuffleBag"/>（2026-09-23，具象題模式）。
    ///
    /// <b>這一支在看什麼</b>：讀檔那條路的邊界（空行、Windows 換行、壞掉的行），
    /// 凸／具象分得對不對，以及抽題不會連續抽到同一題。
    /// 幾何與難度本身不在這裡驗——那是 <see cref="ConvexPolygonCalibrationTests"/> 的事。
    ///
    /// ⭐ <b>最後一條是「兩份檔案不要分家」</b>：遊戲讀 <c>Resources</c> 的複本、
    /// 測試讀素材檔，內容必須一樣。人複製檔案這件事沒有東西保護，所以讓測試保護。
    /// </summary>
    public class PuzzleLibraryTests
    {
        /// <summary>遊戲那一份題庫在 repo 裡的相對路徑。⚠️ 副檔名是 <c>.txt</c> 不是 <c>.jsonl</c>，理由見那一條測試。</summary>
        private const string GameLibraryRelativePath = "Assets/Resources/puzzle_library.txt";

        /// <summary>素材檔的前兩行，拿來組各種畸形的輸入。</summary>
        private static string[] FirstTwoLines()
        {
            string[] lines = File.ReadAllLines(RepositoryFiles.Find(CalibrationEntries.RelativePath));
            return new[] { lines[0], lines[1] };
        }

        [Test]
        public void 空行與前後空白都跳過()
        {
            string[] two = FirstTwoLines();
            string text = "\n" + two[0] + "\n\n   \n" + two[1] + "\n";

            PuzzleLibrary library = PuzzleLibrary.FromText(text);

            Assert.AreEqual(2, library.All.Count, "空行不應該變成題目。");
        }

        [Test]
        public void Windows的換行讀得過()
        {
            // ⚠️ 這一條擋的是真的會發生的事：檔案在 Windows 存的，進 Resources 之後每一行尾端有 \r。
            // 不處理的話 JsonUtility 會把 \r 吃進最後一個欄位，而且不報錯
            string[] two = FirstTwoLines();
            string text = two[0] + "\r\n" + two[1] + "\r\n";

            PuzzleLibrary library = PuzzleLibrary.FromText(text);

            Assert.AreEqual(2, library.All.Count);
            Assert.IsFalse(library.All[0].Name.EndsWith("\r"), "名字尾端不應該留著 \\r。");
        }

        [Test]
        public void 壞掉的行會講是第幾行()
        {
            string[] two = FirstTwoLines();
            string text = two[0] + "\n" + "{\"這不是題目\":1}" + "\n" + two[1];

            var error = Assert.Throws<ArgumentException>(() => PuzzleLibrary.FromText(text));

            StringAssert.Contains("第 2 行", error.Message,
                "行號要講出來——三十幾行的檔案，沒有行號等於整份重看。");
        }

        [Test]
        public void 凸多邊形加具象題等於全部()
        {
            PuzzleLibrary library = CalibrationEntries.Library();

            Assert.AreEqual(CalibrationEntries.ExpectedEntryCount, library.All.Count);
            Assert.AreEqual(CalibrationEntries.ExpectedConvexCount, library.ConvexPolygons.Count);
            Assert.AreEqual(library.All.Count - library.ConvexPolygons.Count, library.FigurativeShapes.Count,
                            "每一題只能落在其中一邊。");

            foreach (PuzzleLibrary.Entry entry in library.FigurativeShapes)
                Assert.IsFalse(entry.IsConvex, $"「{entry.Name}」被分到具象，但它是凸的。");
        }

        [Test]
        public void 洗牌袋一輪之內不重複()
        {
            var bag = new PuzzleShuffleBag(5, new Random(1));
            var drawn = new HashSet<int>();

            for (int i = 0; i < 5; i++)
                Assert.IsTrue(drawn.Add(bag.Next()), "一輪之內不應該抽到重複的。");

            Assert.AreEqual(5, drawn.Count);
        }

        [Test]
        public void 洗牌袋在兩輪交界也不會連抽同一個()
        {
            // ⚠️ 這裡要跑很多次：交界撞到是隨機事件，跑一次看不出來。
            // 25 題的話每個交界撞到的機率是 1/25，跑 200 輪幾乎必然踩到——如果沒擋的話
            var bag = new PuzzleShuffleBag(25, new Random(20260923));
            int previous = bag.Next();

            for (int i = 0; i < 25 * 200; i++)
            {
                int current = bag.Next();
                Assert.AreNotEqual(previous, current, $"第 {i} 次抽到了跟上一次一樣的。");
                previous = current;
            }
        }

        [Test]
        public void 只有一個項目時照樣抽得出來()
        {
            // 「跨輪不重複」在只有一個項目時不可能成立，那時候不能卡住也不能丟例外
            var bag = new PuzzleShuffleBag(1, new Random(1));

            for (int i = 0; i < 3; i++) Assert.AreEqual(0, bag.Next());
        }

        [Test]
        public void 一個項目都沒有是錯的()
        {
            Assert.Throws<ArgumentOutOfRangeException>(() => new PuzzleShuffleBag(0, new Random(1)));
        }

        [Test]
        public void 遊戲那份題庫跟測試素材一模一樣()
        {
            // ⭐ 兩份檔案是人手動複製的，沒有東西保護。分家的症狀是「錄影時看到的題目
            // 跟校準測試在量的不是同一批」，而且完全不報錯
            string gameCopy = File.ReadAllText(RepositoryFiles.Find(GameLibraryRelativePath));
            string material = File.ReadAllText(RepositoryFiles.Find(CalibrationEntries.RelativePath));

            Assert.AreEqual(Normalize(material), Normalize(gameCopy),
                            "Assets/Resources/puzzle_library.txt 跟測試素材對不起來了——" +
                            "改了其中一份就要複製到另一份。");
        }

        [Test]
        public void 遊戲那份題庫讀得出三十八題()
        {
            // ⚠️ 副檔名一定要是 Unity 認得的文字資產（.txt／.json／.bytes）。
            // 原本工具寫出來的是 .jsonl，那個 Unity 不會當成 TextAsset，
            // 症狀是 Resources.Load 回 null ——而那時遊戲只會說「讀不到題庫」，查不到是副檔名的問題
            PuzzleLibrary library = PuzzleLibrary.FromText(
                File.ReadAllText(RepositoryFiles.Find(GameLibraryRelativePath)));

            Assert.AreEqual(CalibrationEntries.ExpectedEntryCount, library.All.Count);
            Assert.AreEqual(CalibrationEntries.ExpectedEntryCount - CalibrationEntries.ExpectedConvexCount,
                            library.FigurativeShapes.Count, "具象題的數量變了，具象題模式抽的就是這一批。");
        }

        /// <summary>換行與尾端空白正規化，免得兩份檔案只差一個 CRLF 就紅。</summary>
        private static string Normalize(string text) =>
            text.Replace("\r\n", "\n").TrimEnd('\n');
    }
}
