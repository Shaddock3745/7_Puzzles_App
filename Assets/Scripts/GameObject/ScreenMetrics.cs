using UnityEngine;

/// <summary>
/// 螢幕像素與 <b>dp</b>（density-independent pixel）之間的換算（P1-13）。
///
/// <b>為什麼要有這支</b>：原本吸附半徑、移動門檻、箭頭大小都直接寫**螢幕像素**，
/// 但那不是一個穩定的單位——18 px 在 1080 寬的手機是 1.7% 螢幕寬，
/// 在 2160 寬的平板只剩 0.83%。**同一個數字，手感差一倍。**
///
/// dp 是 Android 的標準單位：<b>1 dp = 1/160 英寸</b>，所以它對應的是**實體大小**。
/// 手指的大小不會因為換裝置而改變，所以「多近算吸得到」「移動多少算拖曳」
/// 這類跟手指有關的數值，用 dp 才是對的。
///
/// ⚠️ <b>這跟 Canvas 的縮放不是同一套。</b>
/// <c>CanvasScaler</c> 設的是 <b>Scale With Screen Size</b>（參考解析度 1080×2340、Match Width），
/// 也就是**照解析度比例**縮放，不是照實體大小。兩者在手機上很接近
/// （多數手機都是 1080 寬、400 dpi 上下），但在平板上會分家：
/// 解析度變兩倍時 Canvas 的按鈕跟著變兩倍，而 dp 的箭頭維持原本的實體大小。
///
/// **目前只打算支援手機，所以接受這個差異。** 真的要上平板時再回來統一。
/// </summary>
public static class ScreenMetrics
{
    /// <summary>Android 的定義：1 dp = 1/160 英寸。</summary>
    private const float DotsPerInchPerDp = 160f;

    /// <summary>
    /// 基準裝置的像素密度（Xperia 1 VI：1080×2340、6.5 吋，約 398 dpi）。
    ///
    /// 兩個時機會用到它：<c>Screen.dpi</c> 回報不出來的時候，以及在 Editor 裡。
    /// </summary>
    private const float ReferenceDotsPerInch = 400f;

    /// <summary>
    /// 一個 dp 等於幾個螢幕像素。
    ///
    /// ⚠️ <b>Editor 一律用基準密度</b>，不讀 <c>Screen.dpi</c>——
    /// 那支在 Editor 回的是**你螢幕的** dpi（桌機通常 96 左右），
    /// 所以 Game 視窗就算設成 1080×2340，UI 也會縮成手機上的四分之一大。
    /// 那不是 bug，但會讓「在 Editor 調好的手感」到實機上完全不成立。
    ///
    /// ⚠️ 實機上 <c>Screen.dpi</c> 也可能回 0（平台回報不出來）或回明顯離譜的值，
    /// 所以兩邊都要擋。擋不住的話症狀是**整個 UI 大小暴走**。
    /// </summary>
    public static float PixelsPerDp
    {
        get
        {
#if UNITY_EDITOR
            float dotsPerInch = ReferenceDotsPerInch;
#else
            float dotsPerInch = Screen.dpi;
            if (dotsPerInch < 50f || dotsPerInch > 1000f) dotsPerInch = ReferenceDotsPerInch;
#endif
            return dotsPerInch / DotsPerInchPerDp;
        }
    }

    /// <param name="dp">density-independent pixel。</param>
    /// <returns>對應的螢幕像素數。</returns>
    public static float ToPixels(float dp) => dp * PixelsPerDp;
}
