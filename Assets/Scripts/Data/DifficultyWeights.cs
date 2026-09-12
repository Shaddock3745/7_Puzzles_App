using System;
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
    /// <b>權重怎麼來的</b>：2026-09-11～12 使用者在對照牆上逐列掃過試出來的。
    /// 每項亂抖 ±0.05 試 200 組，排序跟原本的相關（Spearman）最差仍有 0.99——
    /// 所以<b>小數點不用再調</b>，要決定的只有用哪幾個特徵、大概什麼比例。
    /// ⚠️ 名目權重不等於實際影響：周長與轉角合計佔了分數變異的約 77%，因為兩者相關 0.54、互相放大。
    ///
    /// <b>基準批次</b>：種子 20260910 生 2000 個拼得起來的形狀，取預設審美門檻收的
    /// （含凸包填滿率 ≥ 0.6）共 845 個。分數範圍約 600～2100，其中 ≤ 900 的 43 題、≥ 1500 的 60 題。
    ///
    /// ⚠️ <b><see cref="Default"/> 與 <c>Assets/Resources/DifficultyWeights.json</c> 是同一份數字</b>，
    /// 由 <c>DifficultyWeightsTests</c> 釘住：檔案被改、程式沒跟上（或反過來）測試就紅，
    /// 訊息裡會直接給出該貼哪些數字。兩份的理由是 <c>Data</c> 層不認識 Unity 的 <c>Resources</c>，
    /// 而 <c>dotnet</c> 測試也沒有 Unity。
    /// </summary>
    [Serializable]
    public class DifficultyWeights
    {
        /// <summary>難度尺的中心，1200（跟 ELO 的起始實力同一個數字）。</summary>
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
        /// 2026-09-12 定案的那一組。數字的來源與意義見型別本身的說明。
        /// <b>每次都回傳新的物件</b>：呼叫端拿去改不會污染下一個呼叫端。
        /// </summary>
        public static DifficultyWeights Default => new DifficultyWeights
        {
            center = 1200.0,
            spread = 200.0,
            weightedMean = 0.0,
            weightedDeviation = 0.628725,
            terms = new[]
            {
                new DifficultyTermWeight { key = DifficultyTermKeys.Perimeter,
                    weight = 0.35, direction = -1, mean = 38.895101, deviation = 3.134819 },
                new DifficultyTermWeight { key = DifficultyTermKeys.CornerCount,
                    weight = 0.25, direction = -1, mean = 16.311243, deviation = 1.953041 },
                new DifficultyTermWeight { key = DifficultyTermKeys.KeyPiecesExposingOneVertex,
                    weight = 0.15, direction = 1, mean = 1.725444, deviation = 0.736458 },
                new DifficultyTermWeight { key = DifficultyTermKeys.ForcedPieceCount,
                    weight = 0.15, direction = -1, mean = 0.276923, deviation = 0.487962 },
                new DifficultyTermWeight { key = DifficultyTermKeys.LargeTrianglesJoined,
                    weight = 0.15, direction = -1, mean = 0.066272, deviation = 0.248757 },
                new DifficultyTermWeight { key = DifficultyTermKeys.StaggeredEdgeContactCount,
                    weight = 0.10, direction = -1, mean = 1.642604, deviation = 1.017054 },
            },
        };

        /// <summary>存檔字串。</summary>
        /// <param name="pretty">要不要排版。</param>
        /// <returns>JSON。</returns>
        public string ToJson(bool pretty = true) => JsonUtility.ToJson(this, pretty);

        /// <summary>從存檔字串讀回來。</summary>
        /// <param name="json">JSON。</param>
        /// <returns>權重設定。</returns>
        /// <exception cref="ArgumentException">字串是空的，或讀出來沒有任何特徵。</exception>
        public static DifficultyWeights FromJson(string json)
        {
            if (string.IsNullOrEmpty(json))
                throw new ArgumentException("DifficultyWeights.FromJson：字串是空的", nameof(json));

            DifficultyWeights weights = JsonUtility.FromJson<DifficultyWeights>(json);
            if (weights == null || weights.terms == null || weights.terms.Length == 0)
                throw new ArgumentException(
                    "DifficultyWeights.FromJson：讀出來沒有任何特徵——欄位名稱可能對不上", nameof(json));
            return weights;
        }
    }
}
