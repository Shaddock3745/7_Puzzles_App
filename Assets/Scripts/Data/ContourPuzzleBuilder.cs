using System;
using UnityEngine;

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
    /// 已經刪掉（git 歷史裡找得回來）。之後要換別的逼近方法（例如拿題庫比 IoU）照樣實作這個介面。
    ///
    /// ⚠️ <b>現在是同步的</b>。PC 的 Release 約 1.3 秒（2026-10-20 第四版開特徵之後，原本 0.7 秒），手機會更久——凍住畫面的問題排在 #37
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

        /// <summary>
        /// 輪廓要平移多少才對得上這一題：<b>題目座標 ＝ 輪廓座標 ＋ 這個量</b>（模型單位，2026-10-14）。
        /// 照片對照要靠它把照片疊回盒子上——像素 →（<see cref="RecognizedContour.PixelToModel"/>）輪廓座標
        /// →（這個）題目座標 →（加盒子原點）盤面座標。
        /// </summary>
        public readonly Vector2 OutlineOffset;

        /// <summary>
        /// ⭐ <b>像不像</b>（2026-10-23）：照片輪廓跟拼出來的形狀，重疊的面積 ÷ 合起來的面積，0 ～ 1。選題畫面翻頁時顯示。
        /// <b>不是逼近出來的題目沒有這個數字（null）。</b>
        ///
        /// ⚠️ 這是逼近<b>在格子上量的</b>（<see cref="ContourApproximation.IntersectionOverUnion"/>），
        /// 跟驗收工具用多邊形算的 IoU 會差一點（通常在小數第二位）——拿來比哪個候選比較像夠用，
        /// 影片字卡要引用平均值的話用驗收工具的數字。
        /// </summary>
        public readonly double? IntersectionOverUnion;

        /// <param name="name">給訊息看的名字。</param>
        /// <param name="puzzle">題目。</param>
        /// <param name="referenceSolution">七片的參考解。</param>
        /// <param name="outlineOffset">輪廓 → 題目座標的平移量，見 <see cref="OutlineOffset"/>。</param>
        /// <param name="intersectionOverUnion">像不像，見 <see cref="IntersectionOverUnion"/>；沒有就不填。</param>
        /// <exception cref="ArgumentNullException">題目或參考解是 null。</exception>
        /// <exception cref="ArgumentOutOfRangeException">像不像不在 0 ～ 1 之間（或不是數字）。</exception>
        public ContourPuzzle(string name, Puzzle puzzle, Placement[] referenceSolution, Vector2 outlineOffset,
                             double? intersectionOverUnion = null)
        {
            Name = name;
            Puzzle = puzzle ?? throw new ArgumentNullException(nameof(puzzle));
            ReferenceSolution = referenceSolution ?? throw new ArgumentNullException(nameof(referenceSolution));
            OutlineOffset = outlineOffset;

            // 寫成「不在範圍內」的反面，NaN 才擋得到（NaN 跟什麼比都是 false）
            if (intersectionOverUnion.HasValue && !(intersectionOverUnion.Value >= 0.0 && intersectionOverUnion.Value <= 1.0))
                throw new ArgumentOutOfRangeException(nameof(intersectionOverUnion), intersectionOverUnion, "像不像要在 0 到 1 之間");
            IntersectionOverUnion = intersectionOverUnion;
        }
    }
}
