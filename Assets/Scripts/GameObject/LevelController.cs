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

    [Tooltip("勾起來的話 Start 會自動載入第一關。")]
    [SerializeField] private bool loadClassicSquareOnStart = true;

    // ───────── 以下三個是 P2-4 的暫時鷹架，驗完手感就整段拿掉 ─────────

    [Tooltip("⚠️ P2-4 暫時的。勾起來時，第 2 關開始改用出題器現生的題目。")]
    [SerializeField] private bool useGeneratedPuzzles = true;

    [Tooltip("⚠️ P2-4 暫時的。固定種子，同一個值每次都生出同一串題目——看到怪東西才重現得出來。")]
    [SerializeField] private int generatorSeed = 20260910;

    [Tooltip("⚠️ P2-4 暫時的。生成的題目玩幾關，之後回到經典大正方形當對照。")]
    [SerializeField] private int generatedPuzzleCount = 10;

    // ───────── 以下五個是 P2-5 的審視鷹架，P2-6 定案周長門檻之後整段拿掉 ─────────
    //
    // 2026-09-11：凹角（≤10）與洞（≤1）已經定案，鷹架預設關閉。
    // 保留的理由是 P2-6 可能要放寬周長門檻，到時候要實機玩被擋掉的形狀（手感只有實機測得準）。
    // ⚠️ 要看周長的話得另外加一個欄位——現在只放寬洞與凹角，捏點與周長一律用預設值
    //
    // 用途：把「現在會被審美門檻篩掉」的那些形狀放進來，用肉眼看它們能不能當題目。
    // 批次統計量得出範圍與比例，量不出「好不好看」，那條只能看。

    [Tooltip("⚠️ P2-5 暫時的。勾起來時改用放寬的門檻，而且只挑符合下面兩條下限的形狀出題。")]
    [SerializeField] private bool inspectRejectedShapes = false;

    [Tooltip("⚠️ P2-5 暫時的。放寬後的洞上限。1 = 讓有洞的形狀進得來（實測 1000 題裡的上界就是 1）。")]
    [SerializeField] private int relaxedMaximumHoleCount = 1;

    [Tooltip("⚠️ P2-5 暫時的。放寬後的凹角上限。11 涵蓋實測的 99.9%（上界是 13）。")]
    [SerializeField] private int relaxedMaximumConcaveAngleCount = 11;

    [Tooltip("⚠️ P2-5 暫時的。只看洞數至少這麼多的形狀。1 = 只看有洞的，0 = 不篩。")]
    [SerializeField] private int inspectMinimumHoleCount = 1;

    [Tooltip("⚠️ P2-5 暫時的。只看凹角至少這麼多的形狀。9 = 只看現在會被篩掉的，0 = 不篩。")]
    [SerializeField] private int inspectMinimumConcaveAngleCount = 0;

    /// <summary>這一場。<b>別人請走 <c>LevelSession.Current</c>，不要跟這支要。</b></summary>
    private LevelSession session;

    /// <summary>⚠️ P2-4 暫時的：生成器叫幾次還生不出來就放棄這一關。</summary>
    private const int MaximumGenerationAttempts = 50;

    /// <summary>
    /// ⚠️ P2-5 暫時的：審視模式下的呼叫上限，比平常大兩個數量級。
    ///
    /// 因為審視是<b>刻意在撈稀有的東西</b>——有洞的形狀實測只佔 1.1%，
    /// 平均要叫一百次上下才中一個。沿用平常那個 50 會幾乎每一關都退回經典大正方形，
    /// 而症狀看起來像「生成器壞了」。
    /// </summary>
    private const int MaximumInspectionAttempts = 3000;

    /// <summary>⚠️ P2-4 暫時的：整場共用一個生成器，這樣「不重複」才跨關成立。</summary>
    private PuzzleGenerator generator;

    /// <summary>⚠️ P2-4 暫時的：這一場玩到第幾關（從 0 開始）。</summary>
    private int levelIndex;

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
    }

    private void Start()
    {
        if (!loadClassicSquareOnStart) return;
        EnterNextLevel();
    }

    /// <summary>
    /// 進下一關。<b>開局與完成彈窗的「下一關」共用這一支。</b>
    ///
    /// ⚠️ <b>這個名字是接縫，刻意不叫「載入大正方形」</b>（2026-09-09）。
    /// 呼叫端（<see cref="LevelCompletePanel"/>、<c>Start</c>）該表達的是
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
    /// <b>目前的實作</b>：P1 沒有題庫，永遠回經典大正方形。
    /// 它同時是題目也是參考解——盒子的填色就是靠這七片三角化出來的。
    /// 之後會換成「難度回饋 → 挑下一題」。
    ///
    /// 💡 所以現在測「下一關」的時候<b>畫面看起來會跟原本一模一樣</b>，
    /// 只有七片回到抽屜、undo 堆疊清空、過關旗標歸零。那是正常的，不是沒換到。
    /// </summary>
    public void EnterNextLevel()
    {
        int currentIndex = levelIndex;
        levelIndex++;

        // ⚠️ P2-4 的暫時鷹架。第 1 關與第 12 關是經典大正方形，中間十關是現生的
        // ——**前後都要有對照組**，因為「手感有沒有變」是比較級的問題：
        // 不在同一次遊玩、同一台裝置上摸過舊的那題，就只是在跟記憶比
        if (useGeneratedPuzzles && currentIndex >= 1 && currentIndex <= generatedPuzzleCount
            && TryEnterGeneratedLevel(currentIndex)) return;

        Placement[] solution = PickNextPuzzleSolution();
        EnterLevel(Puzzle.FromPlacements(solution), solution, allowRoll: true);
    }

    /// <summary>
    /// 挑下一題。<b>「挑哪一題」的規則只住在這裡</b>——P2 接上題庫、
    /// P3 接上難度回饋時，要改的就是這一支。
    /// </summary>
    /// <returns>那一題的參考解（目前恆為經典大正方形）。</returns>
    private static Placement[] PickNextPuzzleSolution() => Fixtures.ClassicSquare();

    /// <summary>
    /// ⚠️ <b>P2-4 的暫時鷹架，驗完手感就整段拿掉。</b>
    ///
    /// 用出題器現生一題進來。<b>刻意不讀檔</b>——讀檔得先做序列化、先決定存檔字串、
    /// 先開 <c>StreamingAssets</c>，那三件事全是 P2-8 的工作，為了玩十題把它們拖到現在做不划算。
    /// 生成器在 <c>Data</c> 層，這支透過 <c>Core</c> 就叫得到。
    ///
    /// ⚠️ <b>迴圈在這裡而不在生成器裡。</b>生成器刻意「失敗就直接回報，不自己重試」，
    /// 那樣「試幾次中一題」才量得出來（P2-5 要用）。所以重試是呼叫端的事。
    /// 命中率實測約 48%，這個迴圈幾乎不會跑幾次。
    /// </summary>
    /// <param name="levelNumber">這是第幾關，只用來寫 log。</param>
    /// <returns>有沒有成功進到一關生成的題目。false 時呼叫端會退回經典大正方形。</returns>
    private bool TryEnterGeneratedLevel(int levelNumber)
    {
        if (generator == null) generator = new PuzzleGenerator(generatorSeed, LimitsForThisRun());

        int maximumAttempts = inspectRejectedShapes
            ? MaximumInspectionAttempts
            : MaximumGenerationAttempts;

        for (int attempt = 0; attempt < maximumAttempts; attempt++)
        {
            GenerationResult result = generator.TryGenerate();
            if (result.Outcome != GenerationOutcome.Generated) continue;
            if (!MatchesInspectionFilter(result.Features)) continue;

            // 特徵印出來，判斷「十題的難度有沒有落差」時要看。⚠️ 實機看不到 Console，
            // 所以形狀與難度在 Editor 裡看，手感才需要上實機
            Debug.Log($"第 {levelNumber} 關（生成，叫了 {attempt + 1} 次）：{result.Features}", this);

            // ⚠️ allowRoll 傳 false：要評估的是「存進去的那個角度好不好看」，
            // 讓相機轉會看不到真實情況
            EnterLevel(result.Puzzle, result.ReferenceSolution, allowRoll: false);
            return true;
        }

        Debug.LogWarning($"叫了 {maximumAttempts} 次都沒生出題目，這一關退回經典大正方形。" +
                         (inspectRejectedShapes
                             ? "審視的條件可能太罕見了，把下限調低一點。"
                             : "審美門檻可能收太緊了。"), this);
        return false;
    }

    /// <summary>
    /// ⚠️ <b>P2-5 的審視鷹架，P2-6 定案周長門檻之後整段拿掉。</b>
    ///
    /// 這一場要用的審美門檻。
    ///
    /// ⚠️ <b>審視模式一定要用放寬的門檻建生成器</b>，不能只在外面過濾——
    /// <c>TryGenerate</c> 對被審美篩掉的形狀<b>只帶回特徵，不帶回題目與參考解</b>
    /// （那兩樣要先付正規化那 16 次輪廓追蹤才有）。用預設門檻的話，
    /// 要看的形狀全部是 <c>RejectedByAesthetics</c>，永遠載不進來。
    /// </summary>
    /// <returns>這一場要用的審美門檻。沒開審視時就是預設的那組。</returns>
    private AestheticLimits LimitsForThisRun()
    {
        if (!inspectRejectedShapes) return AestheticLimits.Default;

        // 捏點與周長維持預設：這次要看的是洞與凹角，一次只動要看的那兩條，
        // 不然看到怪東西分不出是哪一條放寬造成的
        AestheticLimits standard = AestheticLimits.Default;
        return new AestheticLimits(
            maximumHoleCount: relaxedMaximumHoleCount,
            maximumPinchPointCount: standard.MaximumPinchPointCount,
            maximumConcaveAngleCount: relaxedMaximumConcaveAngleCount,
            maximumPerimeter: standard.MaximumPerimeter);
    }

    /// <summary>
    /// ⚠️ <b>P2-5 的審視鷹架，P2-6 定案周長門檻之後整段拿掉。</b>
    ///
    /// 這個形狀是不是這次要看的那一種。門檻放寬之後，符合條件的仍然只佔少數
    /// （有洞的實測 1.1%），所以還要在這裡再篩一次，
    /// 否則十關裡會有九關是「本來就通得過」的普通形狀，等於什麼都沒看到。
    /// </summary>
    /// <param name="features">這個形狀的特徵。</param>
    /// <returns>要不要拿它當這一關的題目。沒開審視時一律要。</returns>
    private bool MatchesInspectionFilter(DifficultyFeatures features)
    {
        if (!inspectRejectedShapes) return true;
        if (features.HoleCount < inspectMinimumHoleCount) return false;
        if (features.ConcaveAngleCount < inspectMinimumConcaveAngleCount) return false;
        return true;
    }

    /// <summary>
    /// 進來一關。<b>這五步的順序不能動</b>（互動設計 §14.3）。
    /// </summary>
    /// <param name="puzzle">題目。</param>
    /// <param name="referenceSolution">這個題目的一組解，在題目自己的座標系。</param>
    /// <param name="allowRoll">
    /// 允不允許 roll 相機。精選題傳 false（互動設計 §2.6）。
    /// 目前 <c>Puzzle</c> 還沒有「是不是精選題」的欄位，所以由呼叫端決定，等 P3 題庫做好再改。
    /// </param>
    public void EnterLevel(Puzzle puzzle, IReadOnlyList<Placement> referenceSolution, bool allowRoll)
    {
        // 1. 載入：算原點、建 Region、換掉整包資料
        session.Load(puzzle, referenceSolution, allowRoll);
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
