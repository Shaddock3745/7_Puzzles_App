using System;

namespace Tangram.Data
{
    /// <summary>
    /// ⭐ <b>輪廓 → 題目</b>（2026-10-14，掃描模式的第三段）。
    ///
    /// 給一條辨識出來的輪廓，回一道<b>拼得出來、而且長得像它</b>的題目。
    /// 真的做法是逼近搜尋（<c>待辦清單.md</c>〈2026-09-17 重啟〉：甲是拿題庫比 IoU，丙是 beam search），
    /// <b>兩個都還沒寫</b>，現在只有 <see cref="FixedContourPuzzleBuilder"/> 這個假的。
    ///
    /// <b>為什麼要開介面</b>：掃描流程現在就要接通，而逼近是另一件 2～4 天的事。
    /// 開了介面，之後換掉假的那一個就好，<c>LevelController</c> 一行都不用動。
    ///
    /// ⚠️ <b>現在是同步的</b>。beam search 在手機上可能要好幾秒——到時候如果凍住畫面太久，
    /// 這個介面要改成非同步（跟 <c>IContourScanner</c> 一樣用回呼），呼叫端也要跟著改。
    /// </summary>
    public interface IContourPuzzleBuilder
    {
        /// <summary>
        /// 照輪廓出一題。
        /// </summary>
        /// <param name="contour">辨識出來的輪廓，已經換成模型座標。</param>
        /// <returns>題目與參考解；<b>逼近不出來時回 null</b>（例如輪廓太細長，沒有一種拼法像它）。</returns>
        ContourPuzzle Build(RecognizedContour contour);
    }

    /// <summary>
    /// 輪廓出出來的一題。
    ///
    /// ⚠️ <b>刻意不帶難度</b>：難度是特徵配上權重才有的，權重歸 <c>LevelController</c> 管，
    /// 由它在進關前當場算（跟具象題同一套，見 <see cref="PuzzleLibrary"/>）。
    /// </summary>
    public sealed class ContourPuzzle
    {
        /// <summary>給訊息看的名字。逼近出來的題目沒有名字時可以是「掃描題」這種通稱。</summary>
        public readonly string Name;

        /// <summary>題目。</summary>
        public readonly Puzzle Puzzle;

        /// <summary>這個題目的一組解，<b>在題目自己的座標系</b>。提示與盒子填色都靠它。</summary>
        public readonly Placement[] ReferenceSolution;

        /// <param name="name">給訊息看的名字。</param>
        /// <param name="puzzle">題目。</param>
        /// <param name="referenceSolution">七片的參考解。</param>
        /// <exception cref="ArgumentNullException">題目或參考解是 null。</exception>
        public ContourPuzzle(string name, Puzzle puzzle, Placement[] referenceSolution)
        {
            Name = name;
            Puzzle = puzzle ?? throw new ArgumentNullException(nameof(puzzle));
            ReferenceSolution = referenceSolution ?? throw new ArgumentNullException(nameof(referenceSolution));
        }
    }

    /// <summary>
    /// ⚠️ <b>假實作</b>：不看輪廓，永遠回題庫裡固定的那一題。
    ///
    /// 用途只有一個：讓「掃描 → 讀輪廓 → 出題 → 進關」整條先跑得通。
    /// 逼近搜尋寫好之後就換掉，<b>不要拿它的結果去講「辨識出來像不像」</b>。
    /// </summary>
    public sealed class FixedContourPuzzleBuilder : IContourPuzzleBuilder
    {
        private readonly PuzzleLibrary.Entry entry;

        /// <param name="entry">永遠回這一題。</param>
        /// <exception cref="ArgumentNullException"><paramref name="entry"/> 是 null。</exception>
        public FixedContourPuzzleBuilder(PuzzleLibrary.Entry entry)
        {
            this.entry = entry ?? throw new ArgumentNullException(nameof(entry));
        }

        /// <inheritdoc/>
        /// <exception cref="ArgumentNullException"><paramref name="contour"/> 是 null。</exception>
        public ContourPuzzle Build(RecognizedContour contour)
        {
            // 雖然不看，還是要求有——假實作比真的寬鬆的話，換掉的那天才會冒出「原來這裡會傳 null」
            if (contour == null) throw new ArgumentNullException(nameof(contour));

            // ⚠️ 複製一份參考解：Entry.Pieces 是題庫共用的那一份陣列，交出去之後被改就污染了題庫
            return new ContourPuzzle(entry.Name, entry.Puzzle, (Placement[])entry.Pieces.Clone());
        }
    }
}
