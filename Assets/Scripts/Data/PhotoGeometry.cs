using System;

namespace Tangram.Data
{
    /// <summary>
    /// ⭐ <b>照片的幾何</b>（2026-10-17，掃描模式取得照片）：縮圖要縮成多大、相機畫面怎麼轉正。
    ///
    /// <b>為什麼放在 Data 層</b>：碰相機與貼圖的那半只能在 Unity 裡跑，而「轉錯方向」是不報錯的——
    /// 照片照樣出得來，只是躺著或倒著，辨識模組照樣回一份 JSON，題目跟著歪。
    /// 把像素怎麼搬的那半抽出來，就能用 int 陣列在 <c>dotnet test</c> 裡逐格驗。
    ///
    /// <b>像素的排法跟 Unity 貼圖一樣</b>：一整排連續、<b>第 0 列是最下面</b>（y 朝上），
    /// 第 (x, y) 個像素在 <c>y × 寬 + x</c>。協作者的 JSON 也是 y 朝上，所以這三者是同一套座標。
    /// </summary>
    public static class PhotoGeometry
    {
        /// <summary>
        /// 長邊縮到 <paramref name="maximumLongSide"/> 以內，比例不變。<b>本來就夠小的不放大</b>。
        /// </summary>
        /// <param name="width">原本的寬。</param>
        /// <param name="height">原本的高。</param>
        /// <param name="maximumLongSide">長邊最多幾個像素。</param>
        /// <returns>縮完的寬高。短邊四捨五入，至少 1。</returns>
        /// <exception cref="ArgumentOutOfRangeException">任何一個參數不是正數。</exception>
        public static (int width, int height) FitWithin(int width, int height, int maximumLongSide)
        {
            if (width <= 0) throw new ArgumentOutOfRangeException(nameof(width), width, "寬要是正數。");
            if (height <= 0) throw new ArgumentOutOfRangeException(nameof(height), height, "高要是正數。");
            if (maximumLongSide <= 0)
                throw new ArgumentOutOfRangeException(nameof(maximumLongSide), maximumLongSide, "長邊上限要是正數。");

            int longSide = Math.Max(width, height);
            if (longSide <= maximumLongSide) return (width, height);

            double scale = (double)maximumLongSide / longSide;

            // 長邊直接給上限，不經過乘法——免得浮點誤差讓它變成 1023
            if (width >= height)
                return (maximumLongSide, Math.Max(1, (int)Math.Round(height * scale)));
            return (Math.Max(1, (int)Math.Round(width * scale)), maximumLongSide);
        }

        /// <summary>
        /// 把相機畫面轉正：<b>先上下鏡射、再順時針轉</b>。
        ///
        /// <b>順序為什麼是這樣</b>：跟預覽畫面用的 <c>RectTransform</c> 一致——Unity 的本地變換是先縮放
        /// （<c>localScale.y = -1</c> 就是上下鏡射）再旋轉。預覽與拍下來的照片走同一套規則，
        /// 玩家看到的就是辨識模組拿到的。
        ///
        /// ⚠️ 參數直接對應 <c>WebCamTexture.videoRotationAngle</c>（官方說明：「順時針的角度，拿來轉多邊形讓畫面正過來」）
        /// 與 <c>videoVerticallyMirrored</c>。<b>正負號要在 T20 上實拍確認</b>——Unity 的旋轉方向慣例很容易搞反，
        /// 這支只保證「照參數的字面意思轉」。
        /// </summary>
        /// <typeparam name="T">像素的型別（遊戲裡是 <c>Color32</c>，測試用 <c>int</c>）。</typeparam>
        /// <param name="pixels">原本的像素，排法見類別說明。不會被改動。</param>
        /// <param name="width">原本的寬。</param>
        /// <param name="height">原本的高。</param>
        /// <param name="clockwiseDegrees">順時針轉幾度。只能是 90 的倍數（負的、超過 360 都可以）。</param>
        /// <param name="mirrorVertically">要不要先上下鏡射。</param>
        /// <returns>轉好的像素與新的寬高（轉 90 或 270 度時寬高對調）。</returns>
        /// <exception cref="ArgumentNullException"><paramref name="pixels"/> 是 null。</exception>
        /// <exception cref="ArgumentException">像素數量跟寬 × 高對不上，或角度不是 90 的倍數。</exception>
        public static (T[] pixels, int width, int height) Reorient<T>(
            T[] pixels, int width, int height, int clockwiseDegrees, bool mirrorVertically)
        {
            if (pixels == null) throw new ArgumentNullException(nameof(pixels));
            if (width <= 0 || height <= 0 || (long)width * height != pixels.Length)
                throw new ArgumentException($"像素有 {pixels.Length} 個，跟 {width} × {height} 對不上。", nameof(pixels));
            if (clockwiseDegrees % 90 != 0)
                throw new ArgumentException($"只能轉 90 的倍數，拿到 {clockwiseDegrees} 度。", nameof(clockwiseDegrees));

            int quarterTurns = ((clockwiseDegrees / 90) % 4 + 4) % 4;
            bool swapped = quarterTurns % 2 == 1;
            int newWidth = swapped ? height : width;
            int newHeight = swapped ? width : height;

            var result = new T[pixels.Length];
            for (int y = 0; y < height; y++)
            {
                for (int x = 0; x < width; x++)
                {
                    // 一個像素一個像素算它搬到哪：先鏡射，再一次轉 90 度、轉 quarterTurns 次
                    int targetX = x;
                    int targetY = mirrorVertically ? height - 1 - y : y;
                    int currentWidth = width, currentHeight = height;

                    for (int turn = 0; turn < quarterTurns; turn++)
                    {
                        // y 朝上的座標裡順時針轉 90 度：(x, y) → (y, −x)，再平移回到 0 以上。
                        // 驗算：左下角 (0, 0) 跑到左上角 (0, 寬 − 1)；左上角 (0, 高 − 1) 跑到右上角
                        (targetX, targetY) = (targetY, currentWidth - 1 - targetX);
                        (currentWidth, currentHeight) = (currentHeight, currentWidth);
                    }

                    result[targetY * newWidth + targetX] = pixels[y * width + x];
                }
            }

            return (result, newWidth, newHeight);
        }
    }
}
