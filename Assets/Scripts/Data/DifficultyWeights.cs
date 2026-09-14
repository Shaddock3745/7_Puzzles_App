using System;
using System.Collections.Generic;
using UnityEngine;

namespace Tangram.Data
{
    /// <summary>
    /// 難度公式裡一個特徵的設定：權重、方向，以及<b>把原始數字換成「離平均幾個標準差」用的平均與標準差</b>。
    ///
    /// ⚠️ <b>平均與標準差是存下來的常數，不是每次現算</b>。這是「同題同值」的關鍵：
    /// 對照牆是拿當下那一池重新標準化的，所以同一題換一池分數就會變；
    /// 遊戲裡不能這樣，否則 ELO 的前提直接不成立。
    /// </summary>
    [Serializable]
    public class DifficultyTermWeight
    {
        /// <summary>特徵的鍵，見 <see cref="DifficultyTermKeys"/>。</summary>
        public string key;

        /// <summary>權重，非負。0 = 不算分（欄位留著方便之後調回來）。</summary>
        public double weight;

        /// <summary>+1 = 數字越大越難，−1 = 越小越難。</summary>
        public int direction;

        /// <summary>這個特徵在基準批次裡的平均。</summary>
        public double mean;

        /// <summary>這個特徵在基準批次裡的標準差（母體，除以 n）。</summary>
        public double deviation;
    }

    /// <summary><see cref="DifficultyTermWeight.key"/> 的合法值。打錯字會變成「這個特徵不存在」而不報錯，所以集中成常數。</summary>
    public static class DifficultyTermKeys
    {
        /// <summary>
        /// 全部合法的鍵。讀檔時拿它檢查。
        /// ⚠️ <b>新增特徵時這裡要一起加</b>，<see cref="DifficultyEvaluator.RawValueOf"/> 的 switch 也要。
        /// 漏了這裡的症狀是讀檔時被當成打錯字而丟例外——會響，不是安靜的。
        /// </summary>
        public static readonly string[] All =
        {
            Perimeter, CornerCount, ForcedPieceCount,
            KeyPiecesExposingOneVertex, StaggeredEdgeContactCount, LargeTrianglesJoined,
        };

        /// <summary>外框周長。越短越難。</summary>
        public const string Perimeter = "perimeter";

        /// <summary>輪廓的轉角數。越少越難。</summary>
        public const string CornerCount = "cornerCount";

        /// <summary>被外框逼出來的片數。越多越簡單。</summary>
        public const string ForcedPieceCount = "forcedPieceCount";

        /// <summary>三片關鍵片裡只露一個頂點的片數。越多越難。</summary>
        public const string KeyPiecesExposingOneVertex = "keyPiecesExposingOneVertex";

        /// <summary>錯開的邊接觸處數。越少越難。</summary>
        public const string StaggeredEdgeContactCount = "staggeredEdgeContactCount";

        /// <summary>兩片大三角有沒有拼成一塊（0／1）。有 = 簡單。</summary>
        public const string LargeTrianglesJoined = "largeTrianglesJoined";
    }

    /// <summary>
    /// 難度公式的全部數字（P2-6）。<b>換權重是改這份資料，不是改程式</b>。
    ///
    /// <code>
    /// 加權和 = Σ 權重 × 方向 × (特徵 − 該特徵的平均) ÷ 該特徵的標準差
    /// 難度   = 中心 + 間距 × (加權和 − 加權和的平均) ÷ 加權和的標準差
    /// </code>
    ///
    /// ⭐ <b>唯一的源頭是 <c>Assets/Resources/DifficultyWeights.json</c></b>（2026-09-13 起）。
    /// 程式裡<b>刻意沒有預設值</b>：遊戲由 <c>LevelController</c> 從 <c>Resources</c> 讀，
    /// 測試與工具從 repo 路徑讀同一個檔。
    /// 原本程式裡有一份 <c>Default</c>、JSON 是從它匯出的鏡像，而文件寫的卻是「改 JSON 就好」——
    /// 照文件改的話遊戲完全不變。拿掉預設值之後，<b>忘了傳權重是編譯錯誤</b>，不會安靜地用到舊數字。
    ///
    /// ⚠️ <b>改權重之後一定要跑重算工具</b>（<c>DifficultyEvaluatorTests.尺度_權重改過之後重算換算倍率</c>）。
    /// <see cref="weightedDeviation"/> 是權重的函數：只改權重不重算的話，一個標準差不再是 200 分，
    /// 整條尺被拉長或壓扁，<b>而且不報錯</b>。
    ///
    /// <b>權重怎麼來的</b>：2026-09-11～12 使用者在對照牆上逐列掃過試出來的。
    /// 每項亂抖 ±0.05 試 200 組，排序跟原本的相關（Spearman）最差仍有 0.99——
    /// 所以<b>小數點不用再調</b>，要決定的只有用哪幾個特徵、大概什麼比例。
    /// ⚠️ 名目權重不等於實際影響：周長與轉角合計佔了分數變異的約 77%，因為兩者相關 0.54、互相放大。
    /// ⚠️ <b>權重還沒找其他人對過</b>（`待辦清單.md`〈權重要找其他人對〉）。
    ///
    /// <b>基準批次</b>：種子 20260910 生 2000 個拼得起來的形狀，取預設審美門檻收的
    /// （含凸包填滿率 ≥ 0.6）共 845 個。分數範圍約 600～2100，其中 ≤ 900 的 43 題、≥ 1500 的 60 題。
    /// </summary>
    [Serializable]
    public class DifficultyWeights
    {
        /// <summary>難度尺的中心，1200。⚠️ 跟 ELO 的起始實力<b>不是</b>同一個數字（起始實力另外設）。</summary>
        public double center;

        /// <summary>加權和一個標準差等於幾分，200。</summary>
        public double spread;

        /// <summary>加權和在基準批次裡的平均。每個特徵都已經置中，所以理論上是 0。</summary>
        public double weightedMean;

        /// <summary>加權和在基準批次裡的標準差。</summary>
        public double weightedDeviation;

        /// <summary>每個特徵一筆。<b>順序不影響結果</b>——公式是加總。</summary>
        public DifficultyTermWeight[] terms;

        /// <summary>
        /// 出題器目標難度的下限：基準批次篩過的 845 題在這把尺上的第 1 百分位（2026-09-14 從程式常數搬進來）。
        /// ⚠️ <b>不是公式的一部分</b>，但跟換算倍率一樣是「在這把尺上量的」，所以住在這裡、由重算工具一起寫回。
        /// 用的時候拿 <see cref="TargetRange"/>，不要直接讀這兩個欄位。
        /// </summary>
        public int minimumTargetDifficulty;

        /// <summary>出題器目標難度的上限：同一群的第 99 百分位。說明見 <see cref="minimumTargetDifficulty"/>。</summary>
        public int maximumTargetDifficulty;

        /// <summary>
        /// 出題器的目標難度範圍。讀檔時已經檢查過，所以<b>從 <see cref="FromJson"/> 拿到的權重，這裡一定建得出來</b>。
        /// </summary>
        /// <exception cref="ArgumentException">欄位被改成不合法的值（只有測試會這樣做）。</exception>
        public TargetDifficultyRange TargetRange =>
            new TargetDifficultyRange(minimumTargetDifficulty, maximumTargetDifficulty);

        /// <summary>
        /// 從權重檔的內容讀回來，<b>讀的當下就把整份檢查完</b>。
        ///
        /// ⚠️ <b>為什麼要在這裡檢查，不等算難度時才發現</b>：原本特徵名稱打錯字，
        /// 要等到第一次算難度才丟例外，錯誤訊息看不出來源是檔案。
        /// 而且 <c>JsonUtility</c> 碰到拼錯的<b>欄位名</b>不會吭聲、直接讀成 0——
        /// 所以倍率、間距這些「0 一定是錯」的數字，在這裡攔下來。
        /// （權重本身的 0 是合法的，拼錯鍵名那種要靠 <c>DifficultyEvaluatorTests</c> 比對鍵名的測試攔。）
        /// </summary>
        /// <param name="json">權重檔的內容。</param>
        /// <returns>檢查過的權重設定。</returns>
        /// <exception cref="ArgumentException">字串是空的，或內容有任何一項不合法（訊息會說是哪一項）。</exception>
        public static DifficultyWeights FromJson(string json)
        {
            if (string.IsNullOrEmpty(json))
                throw new ArgumentException("DifficultyWeights.FromJson：字串是空的", nameof(json));

            DifficultyWeights weights = JsonUtility.FromJson<DifficultyWeights>(json);
            if (weights == null || weights.terms == null || weights.terms.Length == 0)
                throw new ArgumentException(
                    "DifficultyWeights.FromJson：讀出來沒有任何特徵——欄位名稱可能對不上", nameof(json));

            string problem = weights.FirstProblem();
            if (problem != null)
                throw new ArgumentException($"DifficultyWeights.FromJson：{problem}", nameof(json));
            return weights;
        }

        /// <summary>
        /// 這份權重的<b>指紋</b>：12 個十六進位字元的短雜湊。任何一個數字改了，指紋就變（2026-09-14）。
        ///
        /// <b>為什麼要有</b>：回饋收集會分好幾輪，中間可能換權重。每一行只記「當時的難度值」的話，
        /// 不同輪的難度值是不同的尺量出來的，混在一起對不上，而且看不出來是哪一版。
        /// 題目本身雖然存了、可以重算，但「當時受試者看到的題是照哪把尺挑出來的」會搞不清楚。
        ///
        /// ⚠️ <b>算的是數字不是檔案文字</b>：排版、空白、特徵的先後順序不影響指紋（公式是加總，順序本來就不影響結果），
        /// 只有真的會改變難度值的東西才會。數字用 <c>"R"</c> 格式與 InvariantCulture 寫，換機器、換地區設定都一樣。
        ///
        /// ⚠️ <b>目標難度範圍刻意不算進去</b>（2026-09-14）：它不改變任何一題的難度值。
        /// 而且它是從權重量出來的，權重一改指紋自然會變，不會漏；
        /// 不算進去的好處是加欄位那一天指紋沒變，<b>之前收的回饋資料照樣對得上</b>。
        /// </summary>
        /// <returns>例如 <c>3f9a0c1b7e2d</c>。</returns>
        public string Fingerprint()
        {
            var builder = new System.Text.StringBuilder();
            void Append(double value) =>
                builder.Append(value.ToString("R", System.Globalization.CultureInfo.InvariantCulture)).Append('|');

            Append(center);
            Append(spread);
            Append(weightedMean);
            Append(weightedDeviation);

            var sortedTerms = new List<DifficultyTermWeight>(terms);
            sortedTerms.Sort((left, right) => string.CompareOrdinal(left.key, right.key));
            foreach (DifficultyTermWeight term in sortedTerms)
            {
                builder.Append(term.key).Append('|');
                Append(term.weight);
                builder.Append(term.direction).Append('|');
                Append(term.mean);
                Append(term.deviation);
            }

            using (var hashAlgorithm = System.Security.Cryptography.SHA256.Create())
            {
                byte[] hash = hashAlgorithm.ComputeHash(System.Text.Encoding.UTF8.GetBytes(builder.ToString()));
                var hexadecimal = new System.Text.StringBuilder(12);
                for (int i = 0; i < 6; i++) hexadecimal.Append(hash[i].ToString("x2"));
                return hexadecimal.ToString();
            }
        }

        /// <summary>
        /// 找出這份設定的第一個問題。
        /// </summary>
        /// <returns>問題的說明；沒有問題時回 null。</returns>
        private string FirstProblem()
        {
            if (spread <= 0.0) return $"spread 是 {spread}，必須大於 0（多半是欄位名稱拼錯、被讀成 0）";
            if (weightedDeviation <= 0.0)
                return $"weightedDeviation 是 {weightedDeviation}，必須大於 0（多半是欄位名稱拼錯、被讀成 0）";

            // 範圍的兩條跟 TargetDifficultyRange 的建構子同一套，這裡先擋，錯誤訊息才會指到權重檔
            if (minimumTargetDifficulty <= 0)
                return $"minimumTargetDifficulty 是 {minimumTargetDifficulty}，必須大於 0（多半是欄位名稱拼錯、被讀成 0）";
            if (maximumTargetDifficulty <= minimumTargetDifficulty)
                return $"maximumTargetDifficulty 是 {maximumTargetDifficulty}，必須大於 minimumTargetDifficulty " +
                       $"{minimumTargetDifficulty}（上下限寫反，或上限的欄位名稱拼錯、被讀成 0）";

            var seenKeys = new HashSet<string>();
            foreach (DifficultyTermWeight term in terms)
            {
                if (term == null) return "terms 裡有一筆是空的";
                if (Array.IndexOf(DifficultyTermKeys.All, term.key) < 0)
                    return $"不認得的特徵鍵「{term.key}」——合法的值在 DifficultyTermKeys 裡";

                // 同一個鍵出現兩次的話那個特徵會被算兩遍，分數看起來正常、只是比例偷偷變了
                if (!seenKeys.Add(term.key)) return $"特徵 {term.key} 出現了兩次";

                if (term.direction != 1 && term.direction != -1)
                    return $"特徵 {term.key} 的方向是 {term.direction}，只能是 1 或 -1";
                if (term.weight < 0.0) return $"特徵 {term.key} 的權重是 {term.weight}，不能是負的";

                // 0 分的特徵不會被拿來除，標準差是多少都無所謂（跟 DifficultyEvaluator.WeightedSum 同一條規則）
                if (term.weight != 0.0 && term.deviation <= 0.0)
                    return $"特徵 {term.key} 的標準差是 {term.deviation}，必須大於 0";
            }
            return null;
        }
    }
}
