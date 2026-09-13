using System.Collections.Generic;
using Tangram.Core;
using Tangram.Data;
using UnityEngine;

/// <summary>
/// 進來一關、離開一關（互動設計 §14.3）。<b>它只驅動，不持有真相。</b>
///
/// 真相在 <see cref="LevelSession"/>，而那個物件<b>不是這支的從屬</b>——
/// 它的消費者有一大票（<c>BoxView</c>、拖曳指揮者、中途回饋、除錯 overlay、
/// 過關判定、之後的提示），這支只是其中一個。所以別人一律走
/// <c>LevelSession.Current</c>，不要穿過這支拿資料。
///
/// <b>換關照順序直接叫，不發事件。</b>第 4 步（擺七片）要用到第 2 步
/// （相機 fit）算出來的版面，而事件沒有順序保證——誰先訂閱誰先收到，
/// 跟想要的先後無關。這正是 §2.5 順序契約要防的那類 bug。
/// </summary>
[DisallowMultipleComponent]
public class LevelController : MonoBehaviour
{
    [Tooltip("負責相機與座標換算。")]
    [SerializeField] private BoardView boardView;

    [Tooltip("負責畫盒子。")]
    [SerializeField] private BoxView boxView;

    [Tooltip("拖曳控制。換關時要叫它取消進行中的拖曳。沒有也能跑（例如只想看盒子的場景）。")]
    [SerializeField] private TouchInputController dragController;

    [Tooltip("待用區抽屜。換關時要叫它把七片的姿態重設。沒有也能跑。")]
    [SerializeField] private TrayView trayView;

    [Tooltip("勾起來的話 Start 會自動載入第一關。⚠️ 2026-09-12 從 loadClassicSquareOnStart 改名——場景裡的舊值會失效並退回預設的 true，而那就是原本的值。")]
    [SerializeField] private bool loadFirstLevelOnStart = true;

    [Tooltip("固定種子，同一個值每次都生出同一串題目——看到怪東西才重現得出來。")]
    [SerializeField] private int generatorSeed = 20260910;

    /// <summary>這一場。<b>別人請走 <c>LevelSession.Current</c>，不要跟這支要。</b></summary>
    private LevelSession session;

    /// <summary>
    /// 整場共用一個，這樣<b>去重才跨關成立</b>——每次換關 new 一個的話，
    /// <c>seenKeys</c> 歸零，同一批題會一直回來。
    /// </summary>
    private TargetedPuzzleGenerator generator;

    /// <summary>這一場玩到第幾關（從 0 開始）。只拿來寫 log。</summary>
    private int levelIndex;

    /// <summary>權重檔在 <c>Resources</c> 裡的名字（不含副檔名）。</summary>
    private const string DifficultyWeightsResourceName = "DifficultyWeights";

    /// <summary>
    /// 難度權重，Awake 讀一次、整場共用。<b>讀不到是 null，那時候不進任何一關</b>。
    ///
    /// ⚠️ <b>這是權重唯一的來源</b>（2026-09-13 起程式裡沒有預設值，見 <see cref="DifficultyWeights"/>）。
    /// 讀不到時刻意不找退路：<c>Resources</c> 是打包進 build 的，讀不到一定是開發時弄錯
    /// （檔案被搬走、改名、內容寫壞），那應該在第一次按 Play 就吵起來，而不是用一組舊數字安靜地跑。
    /// </summary>
    private DifficultyWeights difficultyWeights;

    /// <summary>ELO 設定檔在 <c>Resources</c> 裡的名字（不含副檔名）。</summary>
    private const string SkillRatingSettingsResourceName = "SkillRatingSettings";

    // ⚠️ 2026-09-13（P2-14）拿掉了 startingTargetDifficulty 與 lastTargetDifficulty。
    // 「下一題要多難」的真相換成玩家的實力（LevelSession.Rating），起始值在 SkillRatingSettings.json。
    // 場景裡 startingTargetDifficulty 的舊值會被 Unity 忽略，不用清。

    private void Awake()
    {
        session = new LevelSession();

        // 靜態存取點只在這裡設定一次（互動設計 §14.1 甲案）。
        // 測試不會走這條路——它們自己 new 一個 LevelSession
        LevelSession.Current = session;

        // 跨元件的 UI 決定（P1-13）。⚠️ 一定要在這裡指定，不能用惰性單例——
        // 關掉 Domain Reload 的話 static 欄位會跨播放session 存活，
        // 惰性單例會把上一次播放的狀態帶進來，而且完全不報錯
        SharedUiState.Current = new SharedUiState();

        if (boardView == null) boardView = FindFirstObjectByType<BoardView>();
        if (boxView == null) boxView = FindFirstObjectByType<BoxView>();

        // 這兩個是選用的，所以找不到不報錯——場景可能刻意不要拖曳（例如只看盒子）
        if (dragController == null) dragController = FindFirstObjectByType<TouchInputController>();
        if (trayView == null) trayView = FindFirstObjectByType<TrayView>();

        // ⚠️ 找不到就要大聲講。原本這裡只是讓 EnterLevel 用 null 檢查跳過，
        // 結果症狀是「相機不會自己 fit、盒子不會出現」而且**沒有任何訊息**——
        // 那正是最難查的一種。缺哪個就報哪個
        if (boardView == null)
            Debug.LogError("LevelController 找不到 BoardView：相機不會自己 fit。" +
                           "請在場景裡建一個掛 BoardView 的物件，或直接拖到這支的欄位上。", this);
        if (boxView == null)
            Debug.LogError("LevelController 找不到 BoxView：盒子不會被畫出來。" +
                           "請在場景裡建一個掛 BoxView 的物件，或直接拖到這支的欄位上。", this);

        difficultyWeights = LoadJsonResource(DifficultyWeightsResourceName, DifficultyWeights.FromJson,
                                             "沒有權重就算不出難度");

        // 實力（P2-14）。設定讀不到就不建 tracker，EnterNextLevel() 看到 null 就不進關——
        // 跟權重同一套規則：沒有預設值，讀不到就吵
        SkillRatingSettings ratingSettings = LoadJsonResource(
            SkillRatingSettingsResourceName, SkillRatingSettings.FromJson, "沒有 ELO 設定就不知道下一題要多難");
        if (ratingSettings != null)
        {
            int? savedRating = LoadSavedRating();
            session.Rating = new SkillRatingTracker(ratingSettings, savedRating ?? ratingSettings.initialRating);
        }
    }

    /// <summary>
    /// 從 <c>Assets/Resources/</c> 讀一個 JSON 設定檔。
    /// 檔案不在或內容不合法時<b>大聲報錯並回 null</b>——兩個設定檔都沒有預設值，理由見 <see cref="difficultyWeights"/>。
    /// </summary>
    /// <typeparam name="T">設定的型別。</typeparam>
    /// <param name="resourceName">在 <c>Resources</c> 裡的名字（不含副檔名）。</param>
    /// <param name="parse">解析並檢查的函數；內容不合法時丟 <see cref="System.ArgumentException"/>。</param>
    /// <param name="consequence">讀不到的後果，寫進錯誤訊息。</param>
    /// <returns>檢查過的設定；讀不到時是 null。</returns>
    private T LoadJsonResource<T>(string resourceName, System.Func<string, T> parse, string consequence)
        where T : class
    {
        TextAsset asset = Resources.Load<TextAsset>(resourceName);
        if (asset == null)
        {
            Debug.LogError($"LevelController 讀不到 Resources/{resourceName}.json：{consequence}，" +
                           "所以不會進任何一關。檔案是不是被搬走或改名了？", this);
            return null;
        }

        try
        {
            return parse(asset.text);
        }
        catch (System.ArgumentException exception)
        {
            // FromJson 的訊息會說是哪一項不合法，原樣帶出來
            Debug.LogError($"{resourceName}.json 內容不合法，不會進任何一關。{exception.Message}", this);
            return null;
        }
    }

    private void Start()
    {
        if (!loadFirstLevelOnStart) return;
        EnterNextLevel();
    }

    /// <summary>
    /// 每幀問一次「這一局要不要結算」（P2-14）。
    ///
    /// <b>為什麼每幀問，不讓 <c>TouchInputController</c> 通知</b>：過關與放棄的標記點都在它那裡，
    /// 改成通知就多一條輸入層指向這支的線，而且之後每多一個結束的出口都要記得通知。
    /// 每幀問是 <see cref="LevelResultPanels"/> 已經在用的模式；結算過之後只剩幾個欄位的判斷。
    ///
    /// ⚠️ <b>會晚一幀</b>：<c>Update</c> 之間的先後順序不固定，彈窗可能比結算早一幀出現，
    /// 那一幀方向是空白——彈窗那邊處理「還沒有明細」。
    /// </summary>
    private void Update() => SettleIfNeeded();

    /// <summary>
    /// 需要的話就結算這一局，結算了就寫進度、印一行 log。
    /// 每幀與換關前都叫這一支——守衛全在 <see cref="SkillRatingTracker.TrySettle"/> 裡，重複叫是安全的。
    /// </summary>
    private void SettleIfNeeded()
    {
        SkillRatingTracker rating = session?.Rating;
        if (rating == null || !rating.TrySettle(session.Data, out RatingUpdate update)) return;

        SaveRating(SkillRating.RoundRating(update.RatingAfter));

        // 監控面板（P2-15）做好之前，實測時看這一行
        string outcome = update.Outcome == LevelOutcome.Solved ? "過關" : "放棄";
        Debug.Log($"第 {levelIndex} 關結算：{outcome}，難度 {update.Difficulty}，" +
                  $"預期 {update.ExpectedPerformance:F2}、實際 {update.ActualPerformance:F2}" +
                  $"（時間代價 {update.TimeCostSeconds:F1} 秒），K {update.EffectiveKFactor:0.##}，" +
                  $"實力 {update.RatingBefore:F1} → {update.RatingAfter:F1}（{update.RatingChange:+0.0;-0.0;0.0}），" +
                  $"下一題：{update.Trend}", this);
    }

    // ───────── 進度的讀寫（P2-14 只寫邏輯，P3 才真的落檔）─────────
    //
    // ⚠️ 刻意不為這兩支開介面：實作只有一個，接縫是方法名（同 EnterNextLevel 那段的標準）。
    // P3 做 PlayerProgress.json 時只換這兩支的內容，外面一行都不用動。

    /// <summary>
    /// 讀存檔裡的實力。<b>現在永遠回 null</b>（P3 才做 <c>PlayerProgress.json</c>），
    /// 所以每次開 App 都從 <see cref="SkillRatingSettings.initialRating"/> 開始。
    /// </summary>
    /// <returns>存檔裡的實力；沒有存檔時是 null。</returns>
    private int? LoadSavedRating() => null;

    /// <summary>
    /// 把實力寫進存檔當備份。<b>現在什麼都不做</b>（P3 才落檔）。
    ///
    /// 傳進來的是<b>四捨五入後的整數</b>，記憶體裡的實力照樣留小數繼續算——
    /// 所以重開 App 最多少掉 0.5 分，但玩同一場的時候不會累積誤差（2026-09-13 定）。
    /// </summary>
    /// <param name="rating">四捨五入後的實力。</param>
    private void SaveRating(int rating) { }

    /// <summary>
    /// 進下一關。<b>開局與完成彈窗的「下一關」共用這一支。</b>
    ///
    /// ⚠️ <b>這個名字是接縫，刻意不叫「載入大正方形」</b>（2026-09-09）。
    /// 呼叫端（<see cref="LevelResultPanels"/>、<c>Start</c>）該表達的是
    /// 「玩家要進下一關」這個**意圖**，不是「載入哪一題」這個**當下的實作**。
    ///
    /// 名字裡寫死題目的話，P2 接上題庫時<b>呼叫端也得跟著改</b>，
    /// 而且方法名本身會過期（叫 <c>EnterClassicSquare</c> 卻載入別的題目更糟）。
    /// 現在挑題的決定只住在這支裡面，換掉時**外面一行都不用動**。
    ///
    /// 💡 <b>不需要為此開一個介面。</b>實作只有一個、呼叫端也只有兩個，
    /// 介面只會多一層轉接——接縫是「語意穩定的方法名」，不一定要是型別。
    /// （同一條標準讓 P1-6 決定不實作 <c>IInputSource</c>。）
    ///
    /// <b>目前的實作</b>：照玩家的實力現生一題（P2-7 出題器 ＋ P2-14 實力）。
    ///
    /// ⭐ <b>第一步先補結算</b>：正常情況下每幀的 <c>Update</c> 已經結算過了，這一行是保險——
    /// 萬一哪天結算搬到別的時機，玩家在彈窗上直接按下一關也不會丟掉剛打完的那一局。
    /// 重複叫是安全的（一局只結算一次由 <c>LevelData</c> 保證）。
    ///
    /// ⚠️ <b>刻意不讀題庫檔</b>：讀檔得先做序列化、先決定存檔字串，那是 P2-8b 的工作。
    /// </summary>
    public void EnterNextLevel()
    {
        SettleIfNeeded();

        // 設定讀不到時 Awake 已經報過錯了，這裡不再報一次
        SkillRatingTracker rating = session?.Rating;
        if (rating == null) return;

        EnterNextLevel(rating.NextTargetDifficulty);
    }

    /// <summary>
    /// 進下一關，<b>指定目標難度</b>（P2-7）。
    ///
    /// ⚠️ <b>遊玩迴圈不走這一支</b>（P2-14 起走無參數那支，目標是玩家的實力）。
    /// 留著給工具與除錯用：指定一個難度看出題器生什麼。
    ///
    /// <b>三種下場都要處理</b>：
    /// <list type="bullet">
    /// <item><b>正中</b>：直接進關</item>
    /// <item><b>將就</b>：照樣進關，但<b>要把「你要的」與「實際拿到的」都印出來</b>——
    ///   實測目標 1600 有 11% 會走到這裡、1700 是 40%，而畫面上看不出差別</item>
    /// <item><b>失敗</b>：連題目都沒有，<b>退回經典大正方形</b>。
    ///   九百次測試裡沒發生過，但它是唯一「手上沒有題目」的情況，不擋的話會進到一個空的關</item>
    /// </list>
    /// </summary>
    /// <param name="targetDifficulty">目標難度；超出範圍會被 <c>TargetedPuzzleGenerator</c> 夾住。</param>
    public void EnterNextLevel(int targetDifficulty)
    {
        // 讀檔失敗時 Awake 已經報過錯了。這裡不再報一次，免得每按一次「下一關」就洗一行
        if (difficultyWeights == null) return;

        levelIndex++;

        if (generator == null)
            generator = new TargetedPuzzleGenerator(
                new PuzzleGenerator(generatorSeed, AestheticLimits.Default), difficultyWeights);

        TargetedGenerationResult result = generator.Generate(targetDifficulty);

        if (result.Outcome == TargetedGenerationOutcome.Failed)
        {
            Debug.LogWarning(
                $"第 {levelIndex} 關：目標 {result.TargetDifficulty} 叫了 {result.AttemptCount} 次" +
                "都沒生出題目，退回經典大正方形。審美門檻可能收太緊了。", this);

            // 退路那一題沒有經過出題器，難度要自己算。
            // 💡 大正方形是 2261 分——比生成池最高分還高，那不是算錯，見 DifficultyEvaluator 的說明
            Placement[] fallback = Fixtures.ClassicSquare();
            Puzzle fallbackPuzzle = Puzzle.FromPlacements(fallback);
            EnterLevel(fallbackPuzzle, fallback,
                       DifficultyEvaluator.Score(fallbackPuzzle, fallback, difficultyWeights),
                       allowRoll: true);
            return;
        }

        // ⚠️ 「將就」在畫面上看不出來——玩家拿到一題難度不對的題目，而一切看起來都正常。
        // 所以這裡一定要講，而且要把兩個數字都講出來
        string hit = result.Outcome == TargetedGenerationOutcome.OnTarget
            ? "正中"
            : $"⚠️ 將就（差 {Mathf.Abs(result.Difficulty - result.TargetDifficulty)}）";
        Debug.Log($"第 {levelIndex} 關：目標 {result.TargetDifficulty} → 實際 {result.Difficulty}　" +
                  $"{hit}，叫了 {result.AttemptCount} 次", this);

        // ⚠️ allowRoll 傳 false：生成題要看的是「存進去的那個角度好不好看」，
        // 讓相機轉會看不到真實情況。P2-8 接上題庫時這條要重新決定。
        // ⭐ 難度直接用出題器算好的那個，不重算——同一題只算一次，見 LevelData.Difficulty
        EnterLevel(result.Puzzle, result.ReferenceSolution, result.Difficulty, allowRoll: false);
    }

    /// <summary>
    /// 進來一關。<b>這五步的順序不能動</b>（互動設計 §14.3）。
    /// </summary>
    /// <param name="puzzle">題目。</param>
    /// <param name="referenceSolution">這個題目的一組解，在題目自己的座標系。</param>
    /// <param name="difficulty">這一題的難度值，由出題的人算好（見 <c>LevelData.Difficulty</c>）。</param>
    /// <param name="allowRoll">
    /// 允不允許 roll 相機。精選題傳 false（互動設計 §2.6）。
    /// 目前 <c>Puzzle</c> 還沒有「是不是精選題」的欄位，所以由呼叫端決定，等 P3 題庫做好再改。
    /// </param>
    public void EnterLevel(Puzzle puzzle, IReadOnlyList<Placement> referenceSolution, int difficulty, bool allowRoll)
    {
        // 1. 載入：算原點、建 Region、換掉整包資料
        session.Load(puzzle, referenceSolution, difficulty, allowRoll);
        LevelData level = session.Data;

        // 2. 設盒子 → 相機這時候才算得出來。**一定要排在第 3、4 步之前**
        if (boardView != null)
        {
            boardView.SetBox(level.BoxRegion.Bounds, level.AllowRoll);

            // 推送失敗也是安靜的（相機還沒準備好、螢幕尺寸 0、解算不出來都會讓
            // EnsureCurrent 中途 return）。這裡確認一下有沒有真的算出解
            if (!boardView.Solution.IsValid)
                Debug.LogWarning("盒子設好了但版面解無效，相機不會 fit。" +
                                 "多半是這一幀還拿不到相機或螢幕尺寸，下一幀會自己補上。", this);
        }

        // 3. 畫盒子
        if (boxView != null) boxView.Redraw(level.BoxRegion, level.ReferenceSolution);

        // 4. 七片重置姿態（P1-5）。**必須排在第 2 步之後**——抽屜的版面要用
        //    相機算出來的「一個模型單位佔幾像素」。
        //
        //    位置不用管：七片這時候全在抽屜裡（看不到），而「七片回到待用區」
        //    是 LevelData 建構時就成立的，不必在這裡再做一次——換關是整包換掉，
        //    所以那件事不可能被忘記（互動設計 §14.4）
        if (trayView != null) trayView.ResetPiecePoses();

        // 5. 取消進行中的互動。跟 BoardView.LayoutRecomputed 是同一個動作的兩個入口，
        //    所以都接到 TouchInputController.CancelInteraction，不各寫一次。
        //    它同時會把輸入層歸零（清選取、放掉鎖定的那根手指）
        if (dragController != null) dragController.CancelInteraction();

        // 6. 開始計前景時間（P2-13）。⚠️ 一定要排在第 5 步**之後**——
        //    CancelInteraction 會叫 StopDragTiming，而那一支對「新的這一關」來說
        //    本來就沒在計時，所以無害；但順序反過來讀起來像是「開始了又停掉」
        level.StartForegroundTiming(GameClock.NowSeconds);
    }

    // ───────── App 進出前景（P2-13）─────────
    //
    // ⚠️ 兩個回呼都接，因為兩邊在不同平台上的行為不一樣：
    // Android 進背景給的是 OnApplicationPause(true)，桌機切視窗給的是 OnApplicationFocus(false)。
    // 💡 冪等規則讓「兩個都接、有時候重複觸發」變成安全的——
    // 已經停了再叫停止什麼都不做，已經在計了再叫開始也什麼都不做。

    /// <param name="paused">是不是被暫停（進背景）。</param>
    private void OnApplicationPause(bool paused) => SetForegroundTiming(!paused);

    /// <param name="focused">是不是拿到焦點。</param>
    private void OnApplicationFocus(bool focused) => SetForegroundTiming(focused);

    /// <param name="inForeground">現在在不在前景。</param>
    private void SetForegroundTiming(bool inForeground)
    {
        LevelData level = session?.Data;
        if (level == null) return;

        if (inForeground) level.StartForegroundTiming(GameClock.NowSeconds);
        else level.StopAllTiming(GameClock.NowSeconds);
    }

    /// <summary>
    /// 離開這一關。
    ///
    /// 這裡不用逐一清狀態——下一次 <see cref="EnterLevel"/> 會把整包資料換掉
    /// （互動設計 §14.4）。這支只負責那些「資料換掉也不會自己停下來」的東西。
    /// </summary>
    public void ExitLevel()
    {
        if (dragController != null) dragController.CancelInteraction();
        session.Unload();
    }
}
