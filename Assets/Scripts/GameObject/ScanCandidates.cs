using System;
using System.Collections.Generic;
using Tangram.Data;
using UnityEngine;

/// <summary>
/// ⭐ <b>掃描題的候選預設</b>（2026-10-21，設計上「多生幾種讓玩家自己選」）：每一個是一組逼近參數，
/// <see cref="LevelController"/> 照 Inspector 上的順序各跑一次，合起來給選題畫面（<see cref="ScanPickPanel"/>）。
/// 之前實驗的廢案（不找特徵、貼邊 ×32）在這裡變成選項——它們不是「比較差」，是「長得不一樣」：
/// 不找特徵的比較胖、貼邊 ×32 的比較直但會離開輪廓一點。
///
/// ⚠️ 每個預設一次完整搜尋（Xperia 上開特徵的約 4 秒、不找特徵的約 2 秒），畫面凍住的時間跟著加；
/// 數量由 Inspector 決定，背景執行緒（#37）做完才能邊算邊顯示。
/// </summary>
public enum ScanCandidatePreset
{
    /// <summary>第五版：預設參數（找特徵、貼邊 ×16）。</summary>
    FifthVersion,

    /// <summary>不找特徵（第三版那條路，貼邊照獎勵）：大片吃掉細節，整體比較胖。</summary>
    NoFeatures,

    /// <summary>貼邊 ×32：外露周長更短、更不鋸齒，代價是可能離開輪廓一點（湯匙 0.797 → 0.712）。</summary>
    EdgeContactBonus32,
}

/// <summary>預設 → 參數。</summary>
public static class ScanCandidatePresets
{
    /// <param name="preset">哪一個預設。</param>
    /// <returns>那個預設的逼近參數。</returns>
    /// <exception cref="ArgumentOutOfRangeException">不認得的預設。</exception>
    public static ContourApproximationSettings SettingsOf(ScanCandidatePreset preset)
    {
        switch (preset)
        {
            case ScanCandidatePreset.FifthVersion: return ContourApproximationSettings.Default;
            case ScanCandidatePreset.NoFeatures: return ContourApproximationSettings.Default.WithoutFeatures();
            case ScanCandidatePreset.EdgeContactBonus32: return new ContourApproximationSettings(edgeContactBonus: 32.0);
            default: throw new ArgumentOutOfRangeException(nameof(preset), preset, "不認得的候選預設。");
        }
    }
}

/// <summary>
/// ⭐ <b>待選的掃描結果</b>（2026-10-21）：掃描回來、候選算好、玩家還沒選。由 <see cref="LevelController"/> 持有，
/// 選題畫面只借讀。照片歸 <see cref="LevelController"/> 管（進關交給照片對照、不進關就釋放）。
/// </summary>
public sealed class ScanCandidateSet
{
    /// <summary>拍的那張照片（已轉正、長邊 ≤ 1024）；沒有照片時是 null。</summary>
    public readonly Texture2D Photo;

    /// <summary>辨識出來的輪廓。</summary>
    public readonly RecognizedContour Contour;

    /// <summary>候選題目，照預設的順序、同一個形狀只留一個，至少 1 個。</summary>
    public readonly IReadOnlyList<ContourPuzzle> Candidates;

    /// <param name="photo">照片，可以是 null。</param>
    /// <param name="contour">輪廓。</param>
    /// <param name="candidates">候選，至少 1 個。</param>
    /// <exception cref="ArgumentNullException"><paramref name="contour"/> 或 <paramref name="candidates"/> 是 null。</exception>
    /// <exception cref="ArgumentException">一個候選都沒有。</exception>
    public ScanCandidateSet(Texture2D photo, RecognizedContour contour, IReadOnlyList<ContourPuzzle> candidates)
    {
        if (contour == null) throw new ArgumentNullException(nameof(contour));
        if (candidates == null) throw new ArgumentNullException(nameof(candidates));
        if (candidates.Count == 0) throw new ArgumentException("至少要有一個候選。", nameof(candidates));
        Photo = photo;
        Contour = contour;
        Candidates = candidates;
    }

    /// <summary>
    /// 這張照片能不能疊回輪廓上（2026-10-30 從 <c>LevelController.HandPhotoToView</c> 搬出來，選題畫面也要問）：
    /// 照片的寬高必須等於 JSON 的 <c>width</c>／<c>height</c>（辨識模組的約定）。對不上代表點不在這張照片的座標上，
    /// 硬疊會整張歪掉而且不報錯。⭐ 兩個地方問同一支，免得一邊疊、一邊不疊。
    /// </summary>
    /// <param name="photo">照片，可以是 null。</param>
    /// <param name="contour">辨識出來的輪廓。</param>
    /// <returns>有照片、而且寬高跟輪廓講的一樣。</returns>
    public static bool PhotoMatchesContour(Texture2D photo, RecognizedContour contour) =>
        photo != null && contour != null && photo.width == contour.ImageWidth && photo.height == contour.ImageHeight;
}
