using System.Collections.Generic;
using NUnit.Framework;
using Tangram.Data;

namespace Tangram.Data.Tests
{
    /// <summary>
    /// 把一組擺放正規化（P2-3 的前置）。
    ///
    /// <b>這個改動存在的唯一理由</b>：出題器要把題目存成正規化的樣子，
    /// 而<b>參考解必須套一模一樣的變換</b>。原本 <c>PuzzleForm</c> 算得出那個變換卻不回報，
    /// 出題器只能自己再跑一次 16 種——那會讓正規化的規則有兩份。
    ///
    /// 所以這裡最重要的是〈參考解與外框在同一個座標系〉那一條。
    /// </summary>
    public class CanonicalNormalizeTests
    {
        private static Placement[] Square() => Fixtures.ClassicSquare();

        /// <summary>把一組環轉成「排序過的 key 清單」，用來比較兩組環幾何上是不是同一件事。</summary>
        private static List<string> SortedWalkKeys(IEnumerable<Walk> walks)
        {
            var keys = new List<string>();
            foreach (Walk walk in walks) keys.Add(Canonical.CanonicalStart(walk).ToKey());
            keys.Sort(System.StringComparer.Ordinal);
            return keys;
        }

        // ─────────────────────────────────────────────────────────────
        // 一、門面沒有改變既有行為
        // ─────────────────────────────────────────────────────────────

        /// <summary>
        /// <c>PuzzleForm</c> / <c>PuzzleKey</c> 現在只是 <c>NormalizePuzzle</c> 的門面，
        /// 回傳值必須一模一樣。⚠️ 這條是回歸測試——已經存好的題庫 key 不能因為重構而變。
        /// </summary>
        [Test]
        public void 門面_舊的兩支與正規化回報的一致()
        {
            Placement[] pieces = Square();
            CanonicalPuzzle normalized = Canonical.NormalizePuzzle(pieces);

            Assert.AreEqual(Canonical.PuzzleForm(pieces), normalized.Form);
            Assert.AreEqual(Canonical.PuzzleKey(pieces), normalized.Key);
        }

        // ─────────────────────────────────────────────────────────────
        // 二、⭐ 這個改動的目的
        // ─────────────────────────────────────────────────────────────

        /// <summary>
        /// 把基準擺放套上 16 種變換，逐一回傳。
        ///
        /// ⚠️ <b>這三條測試一定要跑遍變體，不能只跑基準。</b>
        /// <c>Fixtures.ClassicSquare()</c> 自己就是字典序最小的那一版，
        /// 所以對它正規化時贏的變換是<b>單位變換</b>——「先變換再平移」與
        /// 「先平移再變換」在單位變換下結果一樣，只跑基準的話這個順序寫反了也不會紅。
        /// （驗過：把順序對調，只跑基準時 162 條全過。）
        /// </summary>
        private static IEnumerable<TestCaseData> AllVariants()
        {
            foreach ((int rotation, bool flipped) in Symmetry.GlobalTransforms)
            {
                Placement[] pieces = Square();
                var variant = new Placement[pieces.Length];
                for (int pieceIndex = 0; pieceIndex < pieces.Length; pieceIndex++)
                    variant[pieceIndex] = pieces[pieceIndex].ApplyGlobal(rotation, flipped);

                yield return new TestCaseData(variant)
                    .SetName($"{{m}}_rotation{rotation}_flipped{flipped}");
            }
        }

        /// <summary>
        /// ⭐ <b>回傳的擺放與回傳的外框在同一個座標系。</b>
        ///
        /// 拿正規化之後的七片重新描一次外框，必須跟回傳的 <c>Walks</c> 完全對得上
        /// （環的 key 含起點座標，所以 key 相等就代表位置也相等，不只是形狀相等）。
        ///
        /// ⚠️ 這條就是「提示覆蓋框會不會整組偏移」的守衛。對不上的話，
        /// 題目在一個角度、參考解在另一個角度，而且執行時完全不報錯。
        /// </summary>
        [TestCaseSource(nameof(AllVariants))]
        public void 正規化_擺放與外框在同一個座標系(Placement[] pieces)
        {
            CanonicalPuzzle normalized = Canonical.NormalizePuzzle(pieces);
            List<Walk> retraced = BoundaryTracer.Trace(normalized.Pieces);

            CollectionAssert.AreEqual(SortedWalkKeys(normalized.Walks), SortedWalkKeys(retraced));
        }

        /// <summary>
        /// 文件寫的重現方式真的重現得出來：
        /// <c>原本的片.ApplyGlobal(GlobalRotation, IsGloballyFlipped).Translated(Translation)</c>。
        ///
        /// ⚠️ 順序不能反——先變換再平移。反過來的話平移量會跟著被旋轉。
        /// </summary>
        [TestCaseSource(nameof(AllVariants))]
        public void 正規化_回報的變換重現得出回報的擺放(Placement[] pieces)
        {
            CanonicalPuzzle normalized = Canonical.NormalizePuzzle(pieces);

            for (int pieceIndex = 0; pieceIndex < pieces.Length; pieceIndex++)
            {
                Placement reproduced = pieces[pieceIndex]
                    .ApplyGlobal(normalized.GlobalRotation, normalized.IsGloballyFlipped)
                    .Translated(normalized.Translation);

                Assert.AreEqual(normalized.Pieces[pieceIndex], reproduced, $"第 {pieceIndex} 片");
            }
        }

        /// <summary>正規化的定義之一：平移到字典序最小的頂點當原點，所以最小頂點必須是原點。</summary>
        [TestCaseSource(nameof(AllVariants))]
        public void 正規化_最小的頂點被移到原點(Placement[] pieces)
        {
            CanonicalPuzzle normalized = Canonical.NormalizePuzzle(pieces);

            ExactVector2 smallest = default;
            bool hasVertex = false;
            foreach (Walk walk in normalized.Walks)
                foreach (ExactVector2 vertex in walk.Vertices())
                    if (!hasVertex || vertex.CompareTo(smallest) < 0)
                    {
                        smallest = vertex;
                        hasVertex = true;
                    }

            Assert.IsTrue(hasVertex);
            Assert.AreEqual(ExactVector2.Zero, smallest);
        }

        /// <summary>正規化不該把片弄壞：還是七片、不重疊、面積還是 32。</summary>
        [Test]
        public void 正規化_出來的擺放仍然是一組合法的解()
        {
            CanonicalPuzzle normalized = Canonical.NormalizePuzzle(Square());

            Assert.AreEqual(7, normalized.Pieces.Length);
            Validation.Report report = Validation.RunAll(normalized.Pieces);
            Assert.IsTrue(report.Ok, report.ToString());
        }

        // ─────────────────────────────────────────────────────────────
        // 三、⚠️ 那個必須知道的不對稱
        // ─────────────────────────────────────────────────────────────

        /// <summary>
        /// 對同一個形狀的 16 種擺法各正規化一次，<b>key 與外框一定相同</b>。
        ///
        /// 理由：變換群是封閉的，從哪一版出發，取遍 16 種得到的候選集合都一樣，
        /// 所以字典序最小的那一版也一樣。這是「同一題存進去長得一模一樣」的保證。
        /// </summary>
        [Test]
        public void 正規化_key與外框對十六種變換不變()
        {
            Placement[] pieces = Square();
            CanonicalPuzzle original = Canonical.NormalizePuzzle(pieces);
            List<string> originalWalkKeys = SortedWalkKeys(original.Walks);

            foreach ((int rotation, bool flipped) in Symmetry.GlobalTransforms)
            {
                var variant = new Placement[pieces.Length];
                for (int pieceIndex = 0; pieceIndex < pieces.Length; pieceIndex++)
                    variant[pieceIndex] = pieces[pieceIndex].ApplyGlobal(rotation, flipped);

                CanonicalPuzzle normalized = Canonical.NormalizePuzzle(variant);
                string label = $"rotation={rotation} flipped={flipped}";

                Assert.AreEqual(original.Key, normalized.Key, $"{label} key");
                CollectionAssert.AreEqual(originalWalkKeys, SortedWalkKeys(normalized.Walks),
                                          $"{label} 外框");
            }
        }

        /// <summary>
        /// ⚠️ <b>但擺放不保證相同</b>——這條刻意不斷言 <c>Pieces</c> 相等。
        ///
        /// 對稱的外框可以被不同的擺法填出來。平手時贏的變換不一樣，填法就跟著不一樣，
        /// <b>兩組都是那個外框的正確解</b>。所以能保證的是「每一組都填得出同一個外框」，
        /// 不是「每一組長得一樣」。
        ///
        /// 💡 寫這條是為了擋掉一個很容易寫下去的假設。哪天有人拿
        /// <c>Pieces</c> 當去重的依據，對稱的題目就會被當成兩題。
        /// </summary>
        [Test]
        public void 正規化_擺放可能不同但一定填得出同一個外框()
        {
            Placement[] pieces = Square();
            string outlineKey = Canonical.NormalizePuzzle(pieces).Key;

            foreach ((int rotation, bool flipped) in Symmetry.GlobalTransforms)
            {
                var variant = new Placement[pieces.Length];
                for (int pieceIndex = 0; pieceIndex < pieces.Length; pieceIndex++)
                    variant[pieceIndex] = pieces[pieceIndex].ApplyGlobal(rotation, flipped);

                CanonicalPuzzle normalized = Canonical.NormalizePuzzle(variant);

                // 拿回報的擺放重新算一次 key，必須還是同一個外框
                Assert.AreEqual(outlineKey, Canonical.PuzzleKey(normalized.Pieces),
                                $"rotation={rotation} flipped={flipped}");
            }
        }
    }
}
