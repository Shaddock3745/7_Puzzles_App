using System;
using UnityEngine;

namespace Tangram.Core
{
    /// <summary>
    /// 浮點的軸對齊包圍盒。座標層專用——模型層那顆是 <see cref="Tangram.Data.BoundingBox"/>，
    /// 用精確算術。這一顆是浮點的，因為版面計算本來就在浮點世界（互動設計 §2.1）：
    /// 置中要算 −寬/2，而它在 ℤ[√2] 裡不一定除得盡，所以置中永遠是相機的事，不是模型的事。
    /// </summary>
    public readonly struct FloatBoundingBox
    {
        /// <summary>左下角。</summary>
        public readonly Vector2 Minimum;
        /// <summary>右上角。</summary>
        public readonly Vector2 Maximum;

        /// <param name="minimum">左下角（兩個分量都取最小）。</param>
        /// <param name="maximum">右上角（兩個分量都取最大）。</param>
        public FloatBoundingBox(Vector2 minimum, Vector2 maximum)
        {
            Minimum = minimum;
            Maximum = maximum;
        }

        /// <summary>寬。</summary>
        public float Width => Maximum.x - Minimum.x;
        /// <summary>高。</summary>
        public float Height => Maximum.y - Minimum.y;
        /// <summary>中心點。</summary>
        public Vector2 Center => new Vector2((Minimum.x + Maximum.x) * 0.5f,
                                             (Minimum.y + Maximum.y) * 0.5f);

        /// <summary>
        /// 寬或高不是正數就算退化。
        /// 寫成 <c>!(Width &gt; 0f)</c> 而不是 <c>Width &lt;= 0f</c> 是刻意的：
        /// 這樣連 NaN 也會被判定為退化（NaN 跟任何數比較都是 false）。
        /// </summary>
        public bool IsDegenerate => !(Width > 0f) || !(Height > 0f);

        public override string ToString() => Minimum + " ~ " + Maximum;
    }

    /// <summary>
    /// 版面規則：盒子要放在螢幕的哪一塊。座標全是 0~1 的螢幕比例，
    /// 原點左下（跟 Unity 的螢幕座標一致）。
    ///
    /// P1 寫死一個直式版面就夠（互動設計 §2.7）。之後要做自適應版面時，
    /// 這個矩形改成「由螢幕 aspect 算出來的連續函式」即可，
    /// 因為它跟吸附、盒內判定完全解耦——換掉不影響任何已驗證的東西。
    ///
    /// 留白只用這個矩形表達，不另外設縮放係數。兩套機制表達同一件事，
    /// 只會讓人在調參時猜錯是哪一套在生效。
    /// </summary>
    public readonly struct LayoutRule
    {
        /// <summary>盒子區左邊界，螢幕寬的比例。</summary>
        public readonly float BoxAreaMinimumX;
        /// <summary>盒子區下邊界，螢幕高的比例。</summary>
        public readonly float BoxAreaMinimumY;
        /// <summary>盒子區右邊界，螢幕寬的比例。</summary>
        public readonly float BoxAreaMaximumX;
        /// <summary>盒子區上邊界，螢幕高的比例。</summary>
        public readonly float BoxAreaMaximumY;

        /// <param name="boxAreaMinimumX">盒子區左邊界（0~1）。</param>
        /// <param name="boxAreaMinimumY">盒子區下邊界（0~1）。</param>
        /// <param name="boxAreaMaximumX">盒子區右邊界（0~1）。</param>
        /// <param name="boxAreaMaximumY">盒子區上邊界（0~1）。</param>
        public LayoutRule(float boxAreaMinimumX, float boxAreaMinimumY,
                          float boxAreaMaximumX, float boxAreaMaximumY)
        {
            BoxAreaMinimumX = boxAreaMinimumX;
            BoxAreaMinimumY = boxAreaMinimumY;
            BoxAreaMaximumX = boxAreaMaximumX;
            BoxAreaMaximumY = boxAreaMaximumY;
        }

        /// <summary>
        /// P1 的直式版面：盒子區佔螢幕上方約三分之二，左右各留 5%。
        /// 下方那塊留給待用區（P1-5），這一層不管它擺什麼。
        /// </summary>
        public static LayoutRule Portrait => new LayoutRule(0.05f, 0.34f, 0.95f, 0.97f);

        /// <summary>盒子區是不是一個正常的矩形（有正的寬高，NaN 也會被擋掉）。</summary>
        public bool IsValid => BoxAreaMaximumX > BoxAreaMinimumX
                            && BoxAreaMaximumY > BoxAreaMinimumY;

        /// <summary>盒子區的寬，螢幕比例。</summary>
        public float BoxAreaWidthRatio => BoxAreaMaximumX - BoxAreaMinimumX;
        /// <summary>盒子區的高，螢幕比例。</summary>
        public float BoxAreaHeightRatio => BoxAreaMaximumY - BoxAreaMinimumY;
        /// <summary>盒子區中心的 X，螢幕比例。</summary>
        public float BoxAreaCenterXRatio => (BoxAreaMinimumX + BoxAreaMaximumX) * 0.5f;
        /// <summary>盒子區中心的 Y，螢幕比例。</summary>
        public float BoxAreaCenterYRatio => (BoxAreaMinimumY + BoxAreaMaximumY) * 0.5f;
    }

    /// <summary>
    /// 一次版面計算的完整結果(相機位置、縮放、roll參數)。<b>它自己帶著算它的那組螢幕尺寸</b>，
    /// 所以 <see cref="ViewportMath.ScreenToWorld"/> 只要收這一個參數就夠——
    /// 不可能發生「用 A 螢幕算出來的解去換算 B 螢幕的座標」。
    ///
    /// 全部欄位唯讀。這是「係數重算不快取」的另一面：解是一顆不可變的值，
    /// 過期了就整個重算一顆新的，不會有人偷偷改其中一個欄位讓它半新半舊。
    /// </summary>
    public readonly struct ViewportSolution
    {
        /// <summary>相機中心的世界座標（只有 XY，Z 由角色三保留）。</summary>
        public readonly Vector2 CameraCenter;
        /// <summary><c>Camera.orthographicSize</c>。<b>它是半個高度，不是全高。</b></summary>
        public readonly float OrthographicSize;
        /// <summary>相機 roll，以 90° 為單位，0~3。逆時針為正，跟 Unity 繞 Z 軸旋轉一致。</summary>
        public readonly int QuarterTurns;
        /// <summary>一個模型單位對應幾個螢幕像素。</summary>
        public readonly float PixelsPerUnit;
        /// <summary>算這顆解時的螢幕寬（像素）。</summary>
        public readonly float ScreenWidth;
        /// <summary>算這顆解時的螢幕高（像素）。</summary>
        public readonly float ScreenHeight;

        /// <param name="cameraCenter">相機中心的世界座標。</param>
        /// <param name="orthographicSize">相機的正交半高。</param>
        /// <param name="quarterTurns">相機 roll，0~3 個 90°。</param>
        /// <param name="pixelsPerUnit">一個模型單位幾個像素。</param>
        /// <param name="screenWidth">螢幕寬（像素）。</param>
        /// <param name="screenHeight">螢幕高（像素）。</param>
        public ViewportSolution(Vector2 cameraCenter, float orthographicSize, int quarterTurns,
                                float pixelsPerUnit, float screenWidth, float screenHeight)
        {
            CameraCenter = cameraCenter;
            OrthographicSize = orthographicSize;
            QuarterTurns = quarterTurns;
            PixelsPerUnit = pixelsPerUnit;
            ScreenWidth = screenWidth;
            ScreenHeight = screenHeight;
        }

        /// <summary>算不出解時回傳的東西。具名的「無效」比 default 好讀。</summary>
        public static ViewportSolution Invalid
            => new ViewportSolution(Vector2.zero, 0f, 0, 0f, 0f, 0f);

        /// <summary>
        /// 這顆解能不能用。<c>PixelsPerUnit</c> 是關鍵，因為所有換算都要除以它。
        /// 螢幕尺寸為 0（初始化中、視窗最小化）或盒子退化時就會是 false。
        /// </summary>
        public bool IsValid => PixelsPerUnit > 0f && ScreenWidth > 0f && ScreenHeight > 0f;

        /// <summary>相機 roll 的角度，直接餵給 <c>Quaternion.Euler(0, 0, this)</c>。</summary>
        public float RollDegrees => QuarterTurns * 90f;
    }

    /// <summary>
    /// 角色一：座標與版面的純數學（互動設計 §2.4）。
    ///
    /// <b>無狀態、無 Unity 相依。</b>不碰 <c>Camera</c>、<c>Screen</c>、<c>Puzzle</c>、
    /// <c>Region</c>、<c>BoardState</c>、<c>Placement</c>——螢幕尺寸是參數傳進來的，
    /// 不是自己去讀。所以整層可以在 <c>Tools/DotnetTests</c> 裡秒級驗證。
    ///
    /// 這件事值得：座標轉換是最容易寫出「差半個螢幕」「Y 軸反了」「roll 轉錯邊」的地方，
    /// 而這三種錯都不會當機，只會「位置怪怪的」。
    ///
    /// <b>型別不對稱</b>：往模型走的輸出一定是浮點，因為螢幕座標本身就是浮點，
    /// 沒有任何資訊能讓它變精確。要變精確只有一條路——交給 <see cref="SnapSolver"/>。
    /// <b>這一層不准自己做任何吸附或取整。</b>
    /// </summary>
    public static class ViewportMath
    {
        /// <summary>
        /// 把向量逆時針轉 <paramref name="quarterTurns"/> 個 90°。
        ///
        /// <b>整數運算，沒有三角函數。</b>這正是「roll 限定 0/90/180/270」換來的東西：
        /// 旋轉退化成 XY 交換加換號，浮點誤差在型別層面就不可能存在，不是靠自律。
        /// </summary>
        /// <param name="vector">要旋轉的向量。</param>
        /// <param name="quarterTurns">轉幾個 90°，逆時針為正。負數與超過 4 都會先取模。</param>
        /// <returns>旋轉後的向量。</returns>
        public static Vector2 RotateQuarterTurns(Vector2 vector, int quarterTurns)
        {
            switch (((quarterTurns % 4) + 4) % 4)
            {
                case 1: return new Vector2(-vector.y, vector.x);
                case 2: return new Vector2(-vector.x, -vector.y);
                case 3: return new Vector2(vector.y, -vector.x);
                default: return vector;
            }
        }

        /// <param name="vector">要旋轉的向量。</param>
        /// <param name="quarterTurns">要抵銷掉的旋轉量。</param>
        /// <returns>反方向旋轉後的向量。</returns>
        public static Vector2 RotateQuarterTurnsInverse(Vector2 vector, int quarterTurns)
            => RotateQuarterTurns(vector, -quarterTurns);

        /// <summary>
        /// 一個模型單位佔幾個螢幕像素。
        ///
        /// <b>那個 2 是因為 <c>orthographicSize</c> 是半高不是全高。</b>
        /// 少除這個 2 不會報錯，只會讓所有東西大小差兩倍。
        /// </summary>
        /// <param name="screenHeightPixels">螢幕高，像素。</param>
        /// <param name="orthographicSize">相機的正交半高，世界單位。</param>
        /// <returns>每個模型單位的像素數；參數不合法時回 0。</returns>
        public static float PixelsPerUnitFrom(float screenHeightPixels, float orthographicSize)
        {
            if (!(screenHeightPixels > 0f) || !(orthographicSize > 0f)) return 0f;
            return screenHeightPixels / (2f * orthographicSize);
        }

        /// <summary>
        /// 算出這一題的相機該擺哪、拉多遠、要不要 roll。
        ///
        /// <b>roll 的判斷只是一個比較</b>：算「不轉」與「轉 90°」各自 fit 得到的縮放，
        /// 誰大就用誰。不需要列舉「螢幕長/方 × 盒子長/方」四種 case，
        /// 中間比例的裝置也會自動得到正確答案。
        ///
        /// <b>而且只交換一次。</b>決定 roll 之後就把「盒子的寬高」對調，下游只有一條 fit 路徑。
        /// 寫成 roll／不 roll 兩條分支的話一定有一條會錯，而且只在特定裝置比例上才顯現。
        /// </summary>
        /// <param name="boxBounds">盒子的絕對包圍盒，世界座標（模型:世界 = 1:1）。</param>
        /// <param name="rule">版面規則：盒子要放進螢幕的哪一塊。</param>
        /// <param name="allowRoll">
        /// 允不允許 roll 相機。<b>精選題傳 false</b>——相機 roll 90° 跟把世界轉 90° 在螢幕上
        /// 完全等價，一隻貓不管轉哪邊都會躺著，所以精選題就是接受 fit 得比較差（互動設計 §2.6）。
        /// </param>
        /// <param name="screenWidth">螢幕寬，像素。</param>
        /// <param name="screenHeight">螢幕高，像素。</param>
        /// <returns>
        /// 算好的解。螢幕尺寸不是正數（初始化中、視窗最小化）、盒子退化、
        /// 或版面規則不合法時回 <see cref="ViewportSolution.Invalid"/>——
        /// <b>不丟例外，也不回 NaN</b>。NaN 會一路寫進 <c>Camera</c> 讓畫面全黑，
        /// 而且不留任何錯誤訊息。
        /// </returns>
        public static ViewportSolution Fit(FloatBoundingBox boxBounds, LayoutRule rule,
                                           bool allowRoll,
                                           float screenWidth, float screenHeight)
        {
            if (!(screenWidth > 0f) || !(screenHeight > 0f)) return ViewportSolution.Invalid;
            if (boxBounds.IsDegenerate || !rule.IsValid) return ViewportSolution.Invalid;

            float boxAreaWidthPixels = screenWidth * rule.BoxAreaWidthRatio;
            float boxAreaHeightPixels = screenHeight * rule.BoxAreaHeightRatio;

            // 兩個候選縮放。min(寬限制, 高限制) 這條就自動處理了「盒子長還是方」，不用判斷
            float scaleWithoutRoll = Math.Min(boxAreaWidthPixels / boxBounds.Width,
                                              boxAreaHeightPixels / boxBounds.Height);
            // roll 之後盒子的寬要跟盒子區的高比，所以這裡把盒子的寬高對調
            float scaleWithRoll = Math.Min(boxAreaWidthPixels / boxBounds.Height,
                                           boxAreaHeightPixels / boxBounds.Width);

            bool shouldRoll = allowRoll && scaleWithRoll > scaleWithoutRoll;
            int quarterTurns = shouldRoll ? 1 : 0;
            float pixelsPerUnit = shouldRoll ? scaleWithRoll : scaleWithoutRoll;

            if (!(pixelsPerUnit > 0f)) return ViewportSolution.Invalid;

            // orthographicSize 是半高，而且永遠拿「實際的螢幕高」算——roll 不改變這件事，
            // 相機的垂直視野對應的永遠是螢幕的高。roll 的影響上面換過寬高就處理完了
            float orthographicSize = screenHeight / (2f * pixelsPerUnit);

            // 目標是讓盒子中心落在盒子區的中心。先算這個目標離螢幕中心差幾個像素，
            // 換成相機本地座標，再轉回世界座標反推相機該站哪
            Vector2 offsetInCameraLocal = new Vector2(
                (rule.BoxAreaCenterXRatio - 0.5f) * screenWidth / pixelsPerUnit,
                (rule.BoxAreaCenterYRatio - 0.5f) * screenHeight / pixelsPerUnit);
            Vector2 offsetInWorld = RotateQuarterTurns(offsetInCameraLocal, quarterTurns);

            Vector2 boxCenter = boxBounds.Center;
            Vector2 cameraCenter = new Vector2(boxCenter.x - offsetInWorld.x,
                                               boxCenter.y - offsetInWorld.y);

            return new ViewportSolution(cameraCenter, orthographicSize, quarterTurns,
                                        pixelsPerUnit, screenWidth, screenHeight);
        }

        /// <summary>
        /// 手指在模型的哪裡。<b>輸出必定是浮點</b>，因為螢幕座標本身就是浮點——
        /// 沒有任何資訊能讓它變精確。要變精確請交給 <see cref="SnapSolver"/>。
        ///
        /// 這是自己實作而不是用 <c>Camera.ScreenToWorldPoint</c> 的原因：
        /// 順序契約（互動設計 §2.5 第 6 條）要求「就算相機這幀還沒更新，答案也要對」，
        /// 而 <c>Camera</c> 讀的就是它當下已生效的值，物理上做不到這件事。
        /// </summary>
        /// <param name="screenPoint">
        /// 螢幕座標，像素。<b>原點在左下、Y 向上</b>——這是 Unity 與新 Input System 的
        /// <c>Pointer.current.position</c> 的慣例，跟多數 UI 框架相反。
        /// 所以這裡不該出現任何 <c>height - y</c>。
        /// </param>
        /// <param name="solution">版面解，自己帶著它適用的螢幕尺寸。</param>
        /// <returns>對應的世界座標；解無效時回 <c>Vector2.zero</c>。</returns>
        public static Vector2 ScreenToWorld(Vector2 screenPoint, ViewportSolution solution)
        {
            if (!solution.IsValid) return Vector2.zero;

            Vector2 offsetInCameraLocal = new Vector2(
                (screenPoint.x - solution.ScreenWidth * 0.5f) / solution.PixelsPerUnit,
                (screenPoint.y - solution.ScreenHeight * 0.5f) / solution.PixelsPerUnit);
            Vector2 offsetInWorld = RotateQuarterTurns(offsetInCameraLocal, solution.QuarterTurns);

            return new Vector2(solution.CameraCenter.x + offsetInWorld.x,
                               solution.CameraCenter.y + offsetInWorld.y);
        }

        /// <summary>
        /// <see cref="ScreenToWorld"/> 的反方向。
        ///
        /// <b>⚠️ 只給測試與除錯用，不在繪製路徑上。</b>提示的覆蓋框走 world-space sprite
        /// （互動設計 §2.8），會自動跟著相機的 roll 與縮放跑，所以正常繪製完全用不到這個方向。
        ///
        /// 它存在的理由是 round-trip 測試：螢幕座標 → <see cref="ScreenToWorld"/> →
        /// 這個函式 → 應該回到原點。這種測試<b>不需要知道正確答案是什麼</b>，
        /// 只要求兩個方向互為反函式，所以「Y 軸反了 / roll 轉錯邊 / 差半個螢幕」一抓一個準。
        /// 用手算期望值來測則抓不到——寫程式時搞錯的地方，手算時會用同一個錯誤的腦內模型再錯一次。
        /// </summary>
        /// <param name="worldPoint">世界座標。</param>
        /// <param name="solution">版面解。</param>
        /// <returns>對應的螢幕座標（像素，原點左下）；解無效時回 <c>Vector2.zero</c>。</returns>
        public static Vector2 WorldToScreen(Vector2 worldPoint, ViewportSolution solution)
        {
            if (!solution.IsValid) return Vector2.zero;

            Vector2 offsetInWorld = new Vector2(worldPoint.x - solution.CameraCenter.x,
                                                worldPoint.y - solution.CameraCenter.y);
            Vector2 offsetInCameraLocal =
                RotateQuarterTurnsInverse(offsetInWorld, solution.QuarterTurns);

            return new Vector2(
                solution.ScreenWidth * 0.5f + offsetInCameraLocal.x * solution.PixelsPerUnit,
                solution.ScreenHeight * 0.5f + offsetInCameraLocal.y * solution.PixelsPerUnit);
        }
    }
}
