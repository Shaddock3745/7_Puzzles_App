using Tangram.Data;
using UnityEngine;

namespace Tangram.Core
{
    /// <summary>
    /// 角色二：這一題的座標狀態（互動設計 §2.4）。
    ///
    /// <b>它持有的是「輸入」，不是算好的係數。</b>這是「係數重算不快取」的必然結果——
    /// 只要有一個算好的值被存起來，就會有「它什麼時候過期」的問題，
    /// 而過期的座標係數不會報錯，只會讓吸附半徑跟畫面對不上。
    /// 所以 <see cref="Solve"/> 每次都重算，成本是幾十個浮點運算。
    ///
    /// <b>roll 角度是輸出不是輸入。</b>這裡存的是 <see cref="AllowRoll"/>（一個 bool），
    /// roll 幾度由 <see cref="ViewportMath.Fit"/> 從「盒子比例 vs 盒子區比例」算出來。
    /// 分開才不會有人以為可以手動指定 roll——精選題的鎖定是把這個 bool 設成 false，
    /// 不是直接寫死 roll = 0。
    ///
    /// 無 Unity 相依（只用到 <c>Vector2</c>），不是 <c>MonoBehaviour</c>，
    /// 不持有 <c>BoardState</c>，不知道拖曳狀態。
    /// </summary>
    public sealed class BoardLayout
    {
        // 這裡原本有一個 BoxOrigin 欄位，2026-09-04 拿掉了：它從頭到尾只被 SetBox 寫入、
        // 沒有任何地方讀，Solve() 也用不到（版面計算只需要包圍盒）。
        // 盒子原點的家改成 LevelSession——它是無狀態算出來的，不存欄位就不會有第二份。

        /// <summary>盒子的絕對包圍盒，<b>精確座標</b>。通常來自 <c>Region.Bounds</c>。</summary>
        public BoundingBox BoxBounds { get; private set; }

        /// <summary>版面規則：盒子要放進螢幕的哪一塊。</summary>
        public LayoutRule Rule { get; private set; }

        /// <summary>
        /// 允不允許 roll 相機。精選題要傳 false（互動設計 §2.6）。
        /// </summary>
        public bool AllowRoll { get; private set; }

        /// <summary>有沒有設過題目。沒有的話 <see cref="Solve"/> 一律回無效解。</summary>
        public bool HasBox { get; private set; }

        /// <summary>
        /// <b>已經成功推送過</b>的螢幕寬——不是「上一次看到的」。
        /// <b>只用來偵測變化，不參與任何計算。</b>
        /// 存在的原因是 Unity 改視窗大小沒有事件可以訂閱，只能每幀比對。
        ///
        /// 語意差在中途失敗的那一幀：角色三如果算到一半放棄（相機還沒準備好、
        /// 螢幕尺寸是 0、解無效），這組尺寸就<b>不該</b>被記起來。記了的話
        /// <see cref="HasScreenSizeChanged"/> 從此回 false，永遠不再重算，
        /// 畫面卡在錯的縮放而且不會有任何錯誤訊息。
        /// 這也是 <see cref="RecordScreenSize"/> 要跟 <see cref="Solve"/> 分開的原因。
        /// </summary>
        public int LastScreenWidth { get; private set; }

        /// <summary>已經成功推送過的螢幕高。同上。</summary>
        public int LastScreenHeight { get; private set; }

        /// <param name="rule">版面規則。目前傳 <see cref="LayoutRule.Portrait"/>。</param>
        public BoardLayout(LayoutRule rule)
        {
            Rule = rule;
            LastScreenWidth = -1;
            LastScreenHeight = -1;
        }

        /// <summary>
        /// 換題目。呼叫端通常這樣拿參數：
        /// <code>
        /// Region boxRegion = Region.FromPuzzle(puzzle, boxOrigin);
        /// layout.SetBox(session.Data.BoxRegion.Bounds, session.Data.AllowRoll);
        /// </code>
        /// 刻意收 <see cref="BoundingBox"/> 而不是 <c>Region</c>：版面計算只需要包圍盒，
        /// 收 <c>Region</c> 會讓這一層平白多一個相依，測試也得先組一顆 <c>Region</c> 才能跑。
        ///
        /// <b>不收盒子原點</b>：這一層用不到它，原點的家在 <see cref="LevelSession"/>。
        /// </summary>
        /// <param name="absoluteBoxBounds">盒子的絕對包圍盒（已經含盒子原點的平移）。</param>
        /// <param name="allowRoll">允不允許 roll 相機。精選題傳 false。</param>
        public void SetBox(BoundingBox absoluteBoxBounds, bool allowRoll)
        {
            BoxBounds = absoluteBoxBounds;
            AllowRoll = allowRoll;
            HasBox = true;

            // 強迫下一次 HasScreenSizeChanged 回 true，讓角色三一定會重算並推送
            LastScreenWidth = -1;
            LastScreenHeight = -1;
        }

        /// <summary>換版面規則（調參、之後的自適應版面會用到）。</summary>
        /// <param name="rule">新的版面規則。</param>
        public void SetRule(LayoutRule rule)
        {
            Rule = rule;
            LastScreenWidth = -1;
            LastScreenHeight = -1;
        }

        /// <summary>
        /// 螢幕尺寸跟上次看到的不一樣嗎。Unity 改視窗大小沒有事件，只能每幀問這一句。
        /// </summary>
        /// <param name="screenWidth">現在的螢幕寬。</param>
        /// <param name="screenHeight">現在的螢幕高。</param>
        /// <returns>需要重算就回 true。</returns>
        public bool HasScreenSizeChanged(int screenWidth, int screenHeight)
            => screenWidth != LastScreenWidth || screenHeight != LastScreenHeight;

        /// <summary>
        /// 記下這組尺寸「已經成功推送過」。<b>由角色三在推送完之後才呼叫</b>，
        /// 而且中途任何一個放棄的分支都不准走到這裡（見 <see cref="LastScreenWidth"/>）。
        ///
        /// 刻意不在 <see cref="Solve"/> 裡順手做：那會讓「算一次」變成有副作用的操作，
        /// 而且會把「算得出來」跟「推送成功」混為一談——這兩件事在相機還沒準備好的那一幀
        /// 是不同的。
        /// </summary>
        /// <param name="screenWidth">這次的螢幕寬。</param>
        /// <param name="screenHeight">這次的螢幕高。</param>
        public void RecordScreenSize(int screenWidth, int screenHeight)
        {
            LastScreenWidth = screenWidth;
            LastScreenHeight = screenHeight;
        }

        /// <summary>
        /// 算出這組螢幕尺寸下的版面解。<b>純函式：不改任何狀態，不快取。</b>
        /// </summary>
        /// <param name="screenWidth">螢幕寬，像素。</param>
        /// <param name="screenHeight">螢幕高，像素。</param>
        /// <returns>
        /// 版面解。還沒設題目、或螢幕尺寸不是正數時回
        /// <see cref="ViewportSolution.Invalid"/>。
        /// </returns>
        public ViewportSolution Solve(int screenWidth, int screenHeight)
        {
            if (!HasBox) return ViewportSolution.Invalid;
            return ViewportMath.Fit(ToFloat(BoxBounds), Rule, AllowRoll,
                                    screenWidth, screenHeight);
        }

        /// <summary>
        /// 精確包圍盒 → 浮點包圍盒。<b>這是這一題唯一的「精確 → 浮點」轉換點。</b>
        ///
        /// 往這個方向走是安全的（只是失去精度）；<b>反方向永遠不准</b>——
        /// 浮點世界座標要變回精確座標只有一條路，就是 <see cref="SnapSolver"/>（鐵則一）。
        /// </summary>
        /// <param name="exactBounds">精確包圍盒。</param>
        /// <returns>對應的浮點包圍盒。</returns>
        public static FloatBoundingBox ToFloat(BoundingBox exactBounds)
            => new FloatBoundingBox(
                new Vector2((float)exactBounds.Minimum.X.ToDouble(),
                            (float)exactBounds.Minimum.Y.ToDouble()),
                new Vector2((float)exactBounds.Maximum.X.ToDouble(),
                            (float)exactBounds.Maximum.Y.ToDouble()));
    }
}
