using System;

namespace Tangram.Data
{
    /// <summary>
    /// ⭐ <b>輪廓 → 題目</b>（2026-10-14，掃描模式的第三段）。
    ///
    /// 給一條辨識出來的輪廓，回一道<b>拼得出來、而且長得像它</b>的題目。
    /// 實作是 <see cref="BeamSearchContourPuzzleBuilder"/>（逼近搜尋）。
    ///
    /// <b>為什麼要開介面</b>：掃描流程是先用假實作接通的，逼近後來才寫；開了介面，換掉時 <c>LevelController</c>
    /// 只改建出題器那一行。⚠️ 2026-10-14 換成 beam search 之後，原本固定回「貓1」的假實作 <c>FixedContourPuzzleBuilder</c>
    /// 已經刪掉（git 歷史裡找得回來）。之後要換別的逼近方法（例如甲：拿題庫比 IoU）照樣實作這個介面。
    ///
    /// ⚠️ <b>現在是同步的</b>。PC 的 Release 約 0.7 秒，手機會更久——凍住畫面的問題排在 P5-2（#37）
    /// 跟挑戰模式的換關一起用背景執行緒處理，到時候這個介面可能要改成非同步，呼叫端也要跟著改。
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
}
