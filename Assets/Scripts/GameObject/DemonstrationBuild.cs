/// <summary>
/// ⭐ <b>「這是不是展示版」唯一的定義</b>（2026-10-30）：Editor、Development Build，
/// 或 Player Settings 的 Scripting Define Symbols 有 <c>TANGRADE_MONITOR</c>（展示用的 build 只加在 Android）。
///
/// 三個讀者都問這裡，不各寫一份條件——哪天改了一邊、另一邊沒跟上，症狀是「看得到面板、K 卻是 1 倍」之類，不報錯：
/// <list type="bullet">
/// <item>監控面板（<see cref="DebugOverlay"/>）：不是展示版就關掉自己</item>
/// <item>K 的展示倍率（<c>LevelController</c>）：不是展示版就改成 1，設定檔裡的 10 是「展示版用的倍率」</item>
/// <item>主畫面的「清除紀錄」鈕（<see cref="MainMenuPanel"/>）：不是展示版就藏起來</item>
/// </list>
///
/// ⚠️ 編譯符號一個平台一份：加在 Windows 的話遊戲版 exe 也會變展示版（玩家看得到分數、K 變 10 倍）。
/// </summary>
public static class DemonstrationBuild
{
#if UNITY_EDITOR || DEVELOPMENT_BUILD || TANGRADE_MONITOR
    /// <summary>這一份 build 是展示版。</summary>
    public static readonly bool IsActive = true;
#else
    /// <summary>這一份 build 是展示版。</summary>
    public static readonly bool IsActive = false;
#endif
}
