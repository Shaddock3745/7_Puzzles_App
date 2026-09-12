using UnityEngine;

/// <summary>
/// 局內統計要用的時刻來源（P2-13）。**只有一行，但那一行的理由要有個家。**
///
/// <b>四個候選裡選 <c>Time.realtimeSinceStartup</c></b>：
///
/// | 來源 | 為什麼不用 |
/// |---|---|
/// | <c>Time.time</c> | ⚠️ 受 <c>timeScale</c> 影響。之後做暫停選單設 0 的話，計時會整個停住 |
/// | <c>Time.unscaledTime</c> | 逐幀推進，語意上可以，但下面那個更直接 |
/// | <c>DateTime.UtcNow</c> | ⚠️ <b>使用者改系統時間會跳</b>，而跳成負的會污染累積 |
/// | ⭐ <c>Time.realtimeSinceStartup</c> | <b>選它</b>：不受 <c>timeScale</c> 影響，也不受改系統時間影響 |
///
/// ⚠️ 它有兩個已知怪癖——「Editor 暫停時仍在走」「App 在背景時仍在走」——
/// <b>但兩個都不重要</b>，因為我們在 <c>LevelController.OnApplicationPause</c> /
/// <c>OnApplicationFocus</c> <b>明確停止計時</b>，不靠時鐘自己停。
/// 💡 所以原本那個「要先實測背景時哪幾個時鐘會走」的實驗，從「決定用哪個」降級成「確認一下」。
///
/// ⚠️ <b>模型層拿不到這支，也不該拿到。</b><see cref="Tangram.Core.LevelData"/> 的計時方法
/// 一律由呼叫端把秒數傳進去——那樣測試傳假時間就驗得完，完全不用開 Unity。
/// 💡 同一個模式：<c>Serialization.ToDto</c> 的 <c>createdAtUtc</c>。
/// </summary>
public static class GameClock
{
    /// <summary>現在的時刻（秒）。⚠️ 只有相減才有意義，絕對值沒有。</summary>
    public static double NowSeconds => Time.realtimeSinceStartup;
}
