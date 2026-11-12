using System;
using System.IO;
using System.Text;
using Tangram.Core;
using Tangram.Data;
using TMPro;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UI;

/// <summary>
/// 回饋收集場景的流程（2026-09-13）。
///
/// <b>為什麼要有這個場景</b>：現在的難度權重是開發者一個人在對照牆上看出來的，而開發者看過幾千個形狀，
/// 很可能比目標使用者（6 歲起的小孩）強太多。這裡收<b>同一個人、同一題的三個數字</b>：
/// 主觀難度、客觀時間、系統算的難度——分開收就配不起來了。
///
/// <b>流程</b>：開場生題單 → 開始面板（選年齡層與經驗）→ ⭐ <b>練習題</b>（殘局，只剩平行四邊形與一片三角形；
/// 可以教、不問難不難）→「準備好了嗎」面板（<b>開始正式題目</b>，或<b>再練習一次</b>）→
/// 正式五題依打散後的順序玩（<b>不教</b>）→ 每題結束問一句：<b>過關問「難不難」、放棄問「為什麼不玩了」</b>
/// （2026-09-14 改，理由見 <see cref="AbandonReason"/>）→ 五題做完 → 完成面板「下一位」。
///
/// ⭐ <b>資料先寫進這一位自己的暫存檔，做完才整份併進結果檔</b>（2026-09-14，見 <see cref="FeedbackFiles"/>）。
/// ⭐ <b>每個人一批不同的題</b>（<see cref="FeedbackPlan.PlanSeedFor"/>：會一次帶一組小朋友來，在旁邊看的人不能看過同一題），
/// 每一行記<b>權重指紋</b>——分輪收、中間換權重時才分得出來。
/// 下一位的題單在<b>上一位結束、回到開始面板的那一刻</b>就生好，凍住的那幾秒落在換人之間，不讓小朋友按了開始還要等。
/// ⭐ <b>顧場的人按 Esc</b> 叫出中止面板：「中止，這一位不存」／「結束，保存已完成的題目」／「繼續」。
/// 用鍵盤而不是畫面上的鈕：受試者（尤其小孩）看不到、點不到；確認面板防顧場的人自己手滑。
///
/// <b>跟遊戲場景的差別</b>（場景要這樣設）：
/// <list type="bullet">
/// <item><c>LevelController</c> 的「開場自動進第一關」<b>關掉</b>——什麼時候進關由這支決定</item>
/// <item><c>BoardActionButtons</c> 的提示間隔 <b>45 秒</b>，放棄跟第三次提示同時出現（135 秒），跟正式版一模一樣（2026-09-14 定）。
/// （原本放棄門檻是 60 秒；那時還沒正式收資料，所以沒有前後兩批要分）</item>
/// <item><c>LevelResultPanels</c> <b>拿掉</b>，由這裡的面板取代</item>
/// <item>⚠️ <b><c>DebugOverlay</c> 與 <c>DebugTools</c> 拿掉</b>——overlay 會顯示這一題的難度值，受試者看到數字，主觀判斷就被帶走了</item>
/// </list>
/// 💡 <b>進度存檔不用在場景上設</b>：這支在 <c>Awake</c> 直接叫 <c>LevelController.SuppressProgressPersistence</c>（2026-10-17）。
///
/// ⚠️ <b>這一批資料是在固定一台筆電（1920×1080）上用滑鼠收的</b>（打包成 exe）：主觀難度能用，
/// 但時間跟觸控不一樣，<b>不能拿來校正 T</b>。dp 換算在桌機版固定用基準密度（見 <c>ScreenMetrics</c>），手感跟 Editor 一樣。
///
/// <b>它只驅動，不持有真相</b>：一位受試者的進度在 <see cref="FeedbackSession"/>（Core，離線測得到），
/// 這一關在 <c>LevelSession.Current.Data</c>。
///
/// ⚠️ <b>這支腳本不能掛在它要開關的任何一個面板上</b>（停用的物件不跑 <c>Update</c>）。
/// </summary>
[DisallowMultipleComponent]
public class FeedbackSessionController : MonoBehaviour
{
    [Tooltip("進關用它（沿用那條五步順序），權重也跟它拿。留空的話 Awake 會自己找。")]
    [SerializeField] private LevelController levelController;

    [Tooltip("擋片區要登記給它。留空的話 Awake 會自己找。")]
    [SerializeField] private TouchInputController inputController;

    [Tooltip("基準題單種子。第 N 位受試者用「基準 + N − 1」，每個人一批不同的題。會寫進每一行。⚠️ 收資料期間不要改。")]
    [SerializeField] private int basePlanSeed = 20260914;

    [Tooltip("結果檔名，放在專案（或 exe）旁邊的 Tools/Feedback/。")]
    [SerializeField] private string outputFileName = "feedback_results.jsonl";

    [Header("開始面板（滿版）")]
    [Tooltip("整個開始面板的根物件。RectTransform 要滿版。")]
    [SerializeField] private GameObject startPanel;

    [Tooltip("小朋友的英文名字（大小寫照打的樣子存，比對不分大小寫）。同一個小朋友不同天再來時靠它連起來。")]
    [SerializeField] private TMP_InputField codeInputField;

    [Tooltip("「開始」鈕。代號合法、年齡層、經驗、有沒有看別人玩都選了才能按。")]
    [SerializeField] private Button startButton;

    [Tooltip("顯示「目前選了什麼」與「這個代號來過幾次」，讓顧場的人確認沒打錯。沒填也能跑。")]
    [SerializeField] private TMP_Text selectionText;

    [Header("準備好了嗎面板（滿版）")]
    [Tooltip("練習題結束之後出現。「開始正式題目」接 OnReadyPressed、「再練習一次」接 OnPracticeAgainPressed。")]
    [SerializeField] private GameObject readyPanel;

    [Header("問答面板（滿版，過關之後）")]
    [Tooltip("整個問答面板的根物件。五顆鈕的 onClick 各接 OnAnswerPressed(1～5)。只在過關之後出現。")]
    [SerializeField] private GameObject answerPanel;

    [Header("放棄原因面板（滿版，放棄之後）")]
    [Tooltip("放棄之後出現，問「為什麼不玩了」。三顆鈕的 onClick 各接 OnAbandonReasonPressed：0 = 太難了、1 = 不知道怎麼玩、2 = 不想玩了。")]
    [SerializeField] private GameObject abandonReasonPanel;

    [Header("題號")]
    [Tooltip("顯示「練習題」或「第 3／5 題」。一直在畫面上，不屬於任何一個面板，所以過關與放棄共用這一個。沒填也能跑。")]
    [SerializeField] private TMP_Text progressText;

    [Header("完成面板（滿版）")]
    [Tooltip("五題做完之後的面板。「下一位」接 OnNextParticipantPressed、「結束」接 OnExitPressed。")]
    [SerializeField] private GameObject completePanel;

    [Header("中止面板（滿版，顧場的人按 Esc 叫出來）")]
    [Tooltip("三顆鈕：「中止，這一位不存」接 OnStopDiscardPressed、「結束，保存已完成的題目」接 OnStopSavePressed、「繼續」接 OnStopContinuePressed。")]
    [SerializeField] private GameObject stopPanel;

    [Tooltip("中止面板上的說明，例如「第 7 位，已完成 3／5 題」。沒填也能跑。")]
    [SerializeField] private TMP_Text stopSummaryText;

    [Header("共用")]
    [Tooltip("錯誤與狀態訊息（寫檔失敗、題單生不出來、有遺留的暫存檔）。exe 看不到 Console，所以要顯示在畫面上。沒填也能跑。")]
    [SerializeField] private TMP_Text statusText;

    /// <summary>題單上的一題：生好就不再變。</summary>
    private readonly struct PlannedPuzzle
    {
        public readonly Puzzle Puzzle;
        public readonly Placement[] ReferenceSolution;
        public readonly int Difficulty;

        public PlannedPuzzle(Puzzle puzzle, Placement[] referenceSolution, int difficulty)
        {
            Puzzle = puzzle;
            ReferenceSolution = referenceSolution;
            Difficulty = difficulty;
        }
    }

    /// <summary>
    /// 目前這一批的五題，順序跟 <see cref="FeedbackPlan.TargetDifficulties"/> 一樣。生不出來時是 null。
    /// ⚠️ 只對 <see cref="plannedSeed"/> 那個種子有效——換批時要重生（<see cref="EnsurePlanFor"/>）。
    /// </summary>
    private PlannedPuzzle[] plannedPuzzles;

    /// <summary><see cref="plannedPuzzles"/> 是用哪個種子生的。</summary>
    private int plannedSeed;

    /// <summary>難度權重（跟 <c>LevelController</c> 拿的那一份）。拿不到時是 null，整個場景不能開始。</summary>
    private DifficultyWeights weights;

    /// <summary>那份權重的指紋，寫進每一行。</summary>
    private string weightsFingerprint;

    /// <summary>練習題（見 <see cref="FeedbackPractice"/>）。所有人同一題。</summary>
    private PlannedPuzzle practicePuzzle;

    /// <summary>練習題開局留在抽屜裡的兩槽：平行四邊形與一片三角形。</summary>
    private int[] practiceSlotsToLeaveEmpty;

    /// <summary>正式結果檔的完整路徑。<b>只有做完或「結束並保存」的受試者會進來</b>。</summary>
    private string outputFilePath;

    /// <summary>暫存檔的資料夾（結果檔旁邊的 <c>unfinished/</c>）。</summary>
    private string unfinishedDirectory;

    /// <summary>這一位受試者的暫存檔完整路徑；沒有受試者時是 null。</summary>
    private string partialFilePath;

    /// <summary>
    /// 目前為止用過的最大受試者編號。開場從<b>結果檔與遺留的暫存檔</b>一起讀，每開始一位就 ＋ 1。
    /// ⚠️ <b>按「開始」就佔掉編號</b>，不是寫第一行時才佔；中止的人的編號也不重用。
    /// ⚠️ <b>一定要連暫存檔一起算</b>：只看結果檔的話，上次當掉留下的暫存檔會跟新的受試者撞編號，兩份資料被當成同一個人。
    /// </summary>
    private int highestParticipantNumber;

    /// <summary>這一位受試者；還沒按「開始」或回到開始面板之後是 null。</summary>
    private FeedbackSession session;

    /// <summary>開始面板上選了的年齡層，還沒選是 null。</summary>
    private AgeGroup? selectedAgeGroup;

    /// <summary>開始面板上選了的經驗，還沒選是 null。</summary>
    private TangramExperience? selectedExperience;

    /// <summary>開始面板上選了的「有沒有看別人玩」，還沒選是 null。</summary>
    private WatchedOthers? selectedWatchedOthers;

    /// <summary>
    /// 代號 → 在結果檔裡來過幾次。開場讀一次，每位受試者的資料併進結果檔時 ＋ 1（不必每幀讀檔）。
    /// ⚠️ 被中止的那次<b>不算</b>（資料沒存）；「結束並保存」算。
    /// </summary>
    private readonly System.Collections.Generic.Dictionary<string, int> visitsByCode =
        new System.Collections.Generic.Dictionary<string, int>(FeedbackParticipantCode.Comparer);   // ⚠️ 不分大小寫

    /// <summary>中止面板開著嗎。開著時其他面板一律收起來，不靠面板的前後順序蓋住。</summary>
    private bool isStopPanelOpen;

    private RectTransform startArea;
    private RectTransform readyArea;
    private RectTransform answerArea;
    private RectTransform abandonReasonArea;
    private RectTransform completeArea;
    private RectTransform stopArea;

    private void Awake()
    {
        if (levelController == null) levelController = FindFirstObjectByType<LevelController>();
        if (inputController == null) inputController = FindFirstObjectByType<TouchInputController>();

        if (levelController == null)
            Debug.LogError("FeedbackSessionController 找不到 LevelController：沒辦法進關，也拿不到權重。", this);
        else
            // ⚠️ 受試者玩的題照樣會結算，不關掉的話會寫進這台電腦的玩家進度存檔。
            // 一定要在 Awake：LevelController 在 Start 讀存檔
            levelController.SuppressProgressPersistence();
        if (inputController == null)
            Debug.LogError("FeedbackSessionController 找不到 TouchInputController：面板蓋著的時候盤面還抓得到片。", this);

        // ⚠️ 掛在自己要開關的面板上，第一幀就把自己關掉、從此不再跑 Update，而且不報錯
        foreach (GameObject panel in new[] { startPanel, readyPanel, answerPanel, abandonReasonPanel, completePanel, stopPanel })
            if (panel == gameObject)
                Debug.LogError($"FeedbackSessionController 掛在它要開關的 {panel.name} 上了——那個面板一被關掉，" +
                               "這支就不再跑 Update，之後什麼都不會出現。請移到另一個物件上。", this);
    }

    /// <summary>六個面板都登記成擋片區（隱藏時自動不擋，理由見 <see cref="LevelResultPanels"/>）。</summary>
    private void OnEnable()
    {
        startArea = RegisterArea(startPanel);
        readyArea = RegisterArea(readyPanel);
        answerArea = RegisterArea(answerPanel);
        abandonReasonArea = RegisterArea(abandonReasonPanel);
        completeArea = RegisterArea(completePanel);
        stopArea = RegisterArea(stopPanel);
    }

    private void OnDisable()
    {
        if (inputController == null) return;
        inputController.UnregisterPressBlockingArea(startArea);
        inputController.UnregisterPressBlockingArea(readyArea);
        inputController.UnregisterPressBlockingArea(answerArea);
        inputController.UnregisterPressBlockingArea(abandonReasonArea);
        inputController.UnregisterPressBlockingArea(completeArea);
        inputController.UnregisterPressBlockingArea(stopArea);
    }

    /// <param name="panel">面板根物件，可以是 null。</param>
    /// <returns>登記進去的矩形；沒登記成功是 null。</returns>
    private RectTransform RegisterArea(GameObject panel)
    {
        if (panel == null || inputController == null) return null;

        RectTransform area = panel.GetComponent<RectTransform>();
        if (area == null)
        {
            Debug.LogError($"{panel.name} 上沒有 RectTransform，不是 UI 物件？面板蓋著的時候盤面還是抓得到片。", this);
            return null;
        }
        inputController.RegisterPressBlockingArea(area);
        return area;
    }

    /// <summary>
    /// 生題單、找出結果檔與暫存檔資料夾、讀出最大的受試者編號、檢查有沒有遺留的暫存檔。
    /// ⚠️ <b>會凍住幾秒</b>（五題的生成時間加起來）。刻意不做「生成中…」。
    /// </summary>
    private void Start()
    {
        string directory = ResolveOutputDirectory();
        outputFilePath = Path.Combine(directory, outputFileName);
        unfinishedDirectory = Path.Combine(directory, FeedbackFiles.UnfinishedDirectoryName);
        Directory.CreateDirectory(unfinishedDirectory);

        int leftoverCount = ScanLeftoverPartials(out int highestInPartials);
        highestParticipantNumber = Math.Max(ReadHighestParticipantNumber(), highestInPartials);
        LoadVisitsByCode();

        weights = levelController != null ? levelController.CurrentDifficultyWeights : null;
        if (weights == null)
        {
            // LevelController 讀檔失敗時自己已經報過詳細原因，這裡只講後果
            ReportStatus("拿不到難度權重，生不出題單。先看 Console 裡 LevelController 的錯誤。", isError: true);
            return;
        }
        weightsFingerprint = weights.Fingerprint();

        // 練習題：固定的殘局。難度照樣算一次寫進那一行（大正方形約 2261，那不是算錯，見 DifficultyEvaluator）
        Placement[] practiceSolution = FeedbackPractice.BuildSolution();
        Puzzle practiceShape = Puzzle.FromPlacements(practiceSolution);
        practicePuzzle = new PlannedPuzzle(practiceShape, practiceSolution,
                                           DifficultyEvaluator.Score(practiceShape, practiceSolution, weights));
        practiceSlotsToLeaveEmpty = FeedbackPractice.SlotsToLeaveEmpty(practiceSolution);

        // 先把「下一位」那一批生好，凍住的那幾秒發生在開場而不是受試者按下開始時
        int nextSeed = FeedbackPlan.PlanSeedFor(basePlanSeed, highestParticipantNumber + 1);
        if (!EnsurePlanFor(nextSeed)) return;

        string message = $"題單好了（種子 {nextSeed}，權重 {weightsFingerprint}），結果寫到 {outputFilePath}。" +
                         $"目前最大編號 {highestParticipantNumber}，每個人一批不同的題。";
        if (leftoverCount > 0)
            message += $"\n⚠️ unfinished 資料夾裡有 {leftoverCount} 份沒併進結果檔的暫存檔（上次當掉或直接關程式）。" +
                       "不會自動併入，要不要用由你決定。";
        ReportStatus(message, isError: leftoverCount > 0);
    }

    /// <summary>
    /// 確保手上的題單是用 <paramref name="seed"/> 生的；不是的話重生一批（⚠️ 會凍住幾秒）。
    /// </summary>
    /// <param name="seed">要的題單種子。</param>
    /// <returns>有沒有可用的題單。生失敗時手上的題單清成 null，並報錯。</returns>
    private bool EnsurePlanFor(int seed)
    {
        if (plannedPuzzles != null && plannedSeed == seed) return true;

        plannedPuzzles = GeneratePlan(seed);
        plannedSeed = seed;
        return plannedPuzzles != null;
    }

    /// <summary>
    /// 照 800 → 1600 的順序各生一題。<b>同一個出題器連生</b>，所以五題保證互不相同（出題器會去重）。
    /// </summary>
    /// <param name="seed">題單種子。</param>
    /// <returns>五題；有任何一題「失敗」（連題目都沒有）就回 null，並報錯。</returns>
    private PlannedPuzzle[] GeneratePlan(int seed)
    {
        var generator = new TargetedPuzzleGenerator(new PuzzleGenerator(seed, AestheticLimits.Default), weights);
        var puzzles = new PlannedPuzzle[FeedbackPlan.PuzzleCount];
        var summary = new StringBuilder($"回饋題單（種子 {seed}）：");

        for (int planIndex = 0; planIndex < puzzles.Length; planIndex++)
        {
            int target = FeedbackPlan.TargetDifficulties[planIndex];
            TargetedGenerationResult result = generator.Generate(target);

            // 「失敗」是唯一不能用的：沒有題目。「將就」可以——每一行存的是實際難度，不是目標
            if (result.Outcome == TargetedGenerationOutcome.Failed)
            {
                ReportStatus($"種子 {seed} 的目標 {target} 叫了 {result.AttemptCount} 次都沒生出題目，題單不完整，不能開始。" +
                             "改基準種子再試。", isError: true);
                return null;
            }

            puzzles[planIndex] = new PlannedPuzzle(result.Puzzle, result.ReferenceSolution, result.Difficulty);
            summary.Append($" 目標 {target} → {result.Difficulty}" +
                           (result.Outcome == TargetedGenerationOutcome.OnTarget ? "" : "（將就）"));
        }

        Debug.Log(summary.ToString(), this);
        return puzzles;
    }

    /// <summary>每幀：聽 Esc、決定面板開不開、開始鈕能不能按、文字寫什麼。只是狀態的函數，不訂閱任何東西。</summary>
    private void Update()
    {
        // Esc 只在「有受試者、還沒做完」時有作用。沒有人時沒東西可中止；做完時資料已經併進結果檔了
        if (Keyboard.current != null && Keyboard.current.escapeKey.wasPressedThisFrame
            && session != null && !session.IsComplete)
        {
            if (isStopPanelOpen) OnStopContinuePressed();
            else OpenStopPanel();
        }

        if (isStopPanelOpen)
        {
            SetShown(startPanel, false);
            SetShown(readyPanel, false);
            SetShown(answerPanel, false);
            SetShown(abandonReasonPanel, false);
            SetShown(completePanel, false);
            SetShown(stopPanel, true);
            return;
        }
        SetShown(stopPanel, false);

        LevelData level = LevelSession.Current?.Data;
        // ⚠️ 不看「題單生好了沒」：換批時生失敗的話題單是 null，但開始面板要留著——
        // 再按一次「開始」就是重試，而錯誤訊息在狀態列。只有權重或練習題都沒有時才真的不能開始
        bool canStartParticipants = weights != null && practiceSlotsToLeaveEmpty != null;

        bool waitingForParticipant = canStartParticipants && session == null;
        bool levelOver = level != null && level.IsFinished && !level.IsTiming;
        bool practiceOver = session != null && !session.IsPracticeDone && levelOver;
        bool answering = session != null && session.IsPracticeDone && !session.IsComplete && levelOver;
        bool complete = session != null && session.IsComplete;

        SetShown(startPanel, waitingForParticipant);
        SetShown(readyPanel, practiceOver);
        // 依結局分兩個面板。⚠️ 兩個條件都從 answering 分出來、互斥由 LevelData 保證（過關與放棄不會同時成立）；
        // 就算場景接錯線讓兩個都出現，Core 那兩支也會擋掉跟結局對不上的答案
        SetShown(answerPanel, answering && level.IsSolved);
        SetShown(abandonReasonPanel, answering && level.IsAbandoned);
        SetShown(completePanel, complete);

        string code = CurrentCode();
        if (startButton != null)
            startButton.interactable = waitingForParticipant && code != null && selectedAgeGroup.HasValue
                                       && selectedExperience.HasValue && selectedWatchedOthers.HasValue;

        SetText(selectionText, $"{CodeStatus(code)}\n年齡：{LabelOf(selectedAgeGroup)}　玩過七巧板：{LabelOf(selectedExperience)}" +
                               $"　剛才看別人玩：{LabelOf(selectedWatchedOthers)}");
        if (session != null && !session.IsComplete)
            SetText(progressText, session.IsPracticeDone
                ? $"第 {session.SequenceNumber}／{FeedbackPlan.PuzzleCount} 題"
                : "練習題");
    }

    // ───────── 按鈕（接在 Unity Button.onClick 上）─────────

    /// <summary>選年齡層。onClick 帶整數參數：0 = 8 歲以下、1 = 9～12、2 = 13～17、3 = 18 以上。</summary>
    /// <param name="ageGroupIndex">0 ～ 3。</param>
    public void SelectAgeGroup(int ageGroupIndex)
    {
        if (!Enum.IsDefined(typeof(AgeGroup), ageGroupIndex))
        {
            Debug.LogError($"SelectAgeGroup 收到 {ageGroupIndex}，只能是 0 ～ 3。檢查按鈕的 onClick 參數。", this);
            return;
        }
        selectedAgeGroup = (AgeGroup)ageGroupIndex;
    }

    /// <summary>選經驗。onClick 帶整數參數：0 = 0 次、1 = 1～10 次、2 = 10 次以上。</summary>
    /// <param name="experienceIndex">0 ～ 2。</param>
    public void SelectExperience(int experienceIndex)
    {
        if (!Enum.IsDefined(typeof(TangramExperience), experienceIndex))
        {
            Debug.LogError($"SelectExperience 收到 {experienceIndex}，只能是 0 ～ 2。檢查按鈕的 onClick 參數。", this);
            return;
        }
        selectedExperience = (TangramExperience)experienceIndex;
    }

    /// <summary>選「剛才有沒有在旁邊看別人玩」。onClick 帶整數參數：0 = 沒看、1 = 看了一下、2 = 看了很久。</summary>
    /// <param name="watchedIndex">0 ～ 2。</param>
    public void SelectWatchedOthers(int watchedIndex)
    {
        if (!Enum.IsDefined(typeof(WatchedOthers), watchedIndex))
        {
            Debug.LogError($"SelectWatchedOthers 收到 {watchedIndex}，只能是 0 ～ 2。檢查按鈕的 onClick 參數。", this);
            return;
        }
        selectedWatchedOthers = (WatchedOthers)watchedIndex;
    }

    /// <summary>
    /// 「開始」：佔一個新編號、確認這一位那一批題生好了、建進度與暫存檔路徑，進練習題。
    /// 正常情況下題單在回到開始面板時就生好了，這裡不會凍住；只有上次生失敗時才會在這裡重試。
    /// </summary>
    public void OnStartPressed()
    {
        if (weights == null || practiceSlotsToLeaveEmpty == null || session != null) return;

        // 按鈕本來就按不了，這是保險
        string code = CurrentCode();
        if (code == null || !selectedAgeGroup.HasValue || !selectedExperience.HasValue || !selectedWatchedOthers.HasValue)
            return;

        int participantNumber = highestParticipantNumber + 1;
        int seed = FeedbackPlan.PlanSeedFor(basePlanSeed, participantNumber);

        // ⚠️ 生失敗就不佔編號，停在開始面板——手上沒有題目不能讓人開始
        if (!EnsurePlanFor(seed)) return;

        var profile = new FeedbackParticipantProfile(code, VisitsOf(code) + 1, selectedAgeGroup.Value,
                                                     selectedExperience.Value, selectedWatchedOthers.Value);
        highestParticipantNumber = participantNumber;
        session = new FeedbackSession(participantNumber, profile, seed, weightsFingerprint);
        partialFilePath = Path.Combine(unfinishedDirectory, FeedbackFiles.PartialFileName(participantNumber));
        EnterPractice();
    }

    /// <summary>
    /// 「準備好了嗎」面板的「再練習一次」：<b>重新進同一題練習</b>，盤面重置（一樣先擺好五片）。
    ///
    /// <b>這一次練習不記</b>（只記最後一次，也就是按「開始正式題目」那一次）。
    /// 重新進關會換一份新的 <c>LevelData</c>，時間與拖曳次數從零開始，不會跟上一次混在一起。次數不設上限。
    /// </summary>
    public void OnPracticeAgainPressed()
    {
        if (session == null || session.IsPracticeDone) return;

        // 只有練習那一關結束了才有這顆鈕；這是保險，免得連點時在練習途中重開
        LevelData level = LevelSession.Current?.Data;
        if (level == null || !level.IsFinished) return;

        EnterPractice();
    }

    /// <summary>
    /// 「準備好了嗎」面板的「開始正式題目」：練習題記一行 → 寫成功才標記練習做完 → 進正式第一題。
    /// 寫檔失敗時停在這個面板，再按一次就是重試（同 <see cref="OnAnswerPressed"/> 的順序）。
    /// </summary>
    public void OnReadyPressed()
    {
        if (session == null) return;
        LevelData level = LevelSession.Current?.Data;

        if (!session.TryCreatePracticeRecord(level, DateTime.UtcNow, Application.version, out FeedbackRecord record))
            return;   // 還沒結束或已經按過——正常的連點，安靜地忽略
        if (!AppendToPartial(record)) return;

        session.MarkPracticeDone(level);
        EnterCurrentPuzzle();
    }

    /// <summary>
    /// 問答面板的五顆鈕（<b>過關之後</b>）。onClick 帶整數參數 1（很簡單）～ 5（很難）。
    ///
    /// ⚠️ <b>順序</b>：先組出那一行 → 寫進暫存檔 → <b>寫成功才</b>推進度 → 進下一題；
    /// 第五題答完就把暫存檔整份併進結果檔。寫檔失敗時停在這一題，再點一次就是重試。
    /// </summary>
    /// <param name="subjectiveDifficulty">1 ～ 5。</param>
    public void OnAnswerPressed(int subjectiveDifficulty)
    {
        if (session == null) return;
        LevelData level = LevelSession.Current?.Data;

        if (!session.TryCreateSolvedRecord(level, subjectiveDifficulty, DateTime.UtcNow, Application.version,
                                           out FeedbackRecord record))
        {
            if (!FeedbackPlan.IsValidSubjectiveDifficulty(subjectiveDifficulty))
                Debug.LogError($"OnAnswerPressed 收到 {subjectiveDifficulty}，只能是 1 ～ 5。檢查按鈕的 onClick 參數。", this);
            return;   // 其他情況（還沒結束、已經答過）是正常的連點，安靜地忽略
        }

        CommitAnswer(level, record);
    }

    /// <summary>
    /// 放棄原因面板的三顆鈕（<b>放棄之後</b>，2026-09-14）。onClick 帶整數參數：0 = 太難了、1 = 不知道怎麼玩、2 = 不想玩了。
    /// 順序跟 <see cref="OnAnswerPressed"/> 完全一樣。
    /// </summary>
    /// <param name="reasonIndex">0 ～ 2（<see cref="AbandonReason"/> 的順序）。</param>
    public void OnAbandonReasonPressed(int reasonIndex)
    {
        if (session == null) return;
        LevelData level = LevelSession.Current?.Data;
        var reason = (AbandonReason)reasonIndex;

        if (!session.TryCreateAbandonedRecord(level, reason, DateTime.UtcNow, Application.version, out FeedbackRecord record))
        {
            if (!FeedbackPlan.IsValidAbandonReason(reason))
                Debug.LogError($"OnAbandonReasonPressed 收到 {reasonIndex}，只能是 0 ～ 2。檢查按鈕的 onClick 參數。", this);
            return;   // 其他情況（還沒結束、已經答過）是正常的連點，安靜地忽略
        }

        CommitAnswer(level, record);
    }

    /// <summary>完成面板的「下一位」：回到開始面板。資料在答完第五題時已經併進結果檔了。</summary>
    public void OnNextParticipantPressed() => ResetToStart();

    /// <summary>
    /// 完成面板的「結束」。⚠️ <c>Application.Quit()</c> 在 Editor 裡什麼都不做，所以 Editor 走停止播放
    /// （同 <see cref="LevelResultPanels.OnExitPressed"/>）。
    /// </summary>
    public void OnExitPressed()
    {
#if UNITY_EDITOR
        UnityEditor.EditorApplication.isPlaying = false;
#else
        Application.Quit();
#endif
    }

    /// <summary>中止面板的「中止，這一位不存」：刪掉暫存檔，回到開始面板。<b>編號不重用</b>。</summary>
    public void OnStopDiscardPressed()
    {
        if (!isStopPanelOpen) return;

        try
        {
            if (partialFilePath != null && File.Exists(partialFilePath)) File.Delete(partialFilePath);
            ReportStatus($"第 {session?.ParticipantNumber} 位已中止，這一位的資料沒有存。");
        }
        catch (Exception error)
        {
            // 刪不掉不影響結果檔（它本來就沒進去），但要講，不然 unfinished 資料夾會多一份以為被刪掉的檔
            ReportStatus($"第 {session?.ParticipantNumber} 位已中止，但暫存檔刪不掉：{error.Message}。" +
                         "它不在結果檔裡，之後手動刪掉就好。", isError: true);
        }
        ResetToStart();
    }

    /// <summary>
    /// 中止面板的「結束，保存已完成的題目」：把暫存檔裡已經寫的那幾行（含練習）併進結果檔，回到開始面板。
    /// 給「資料是好的，只是做不完」的情況用（例如小孩累了）。
    /// </summary>
    public void OnStopSavePressed()
    {
        if (!isStopPanelOpen) return;

        int savedNumber = session?.ParticipantNumber ?? 0;
        if (MergePartialIntoResults())
            ReportStatus($"第 {savedNumber} 位提早結束，已完成的題目存好了。");
        ResetToStart();
    }

    /// <summary>中止面板的「繼續」（或再按一次 Esc）：收起面板，這一關的前景計時接著跑。</summary>
    public void OnStopContinuePressed()
    {
        if (!isStopPanelOpen) return;
        isStopPanelOpen = false;

        // 開面板時停掉的前景計時接回來。已經結束的一關會被第三條冪等規則忽略，不用另外判斷
        LevelSession.Current?.Data?.StartForegroundTiming(GameClock.NowSeconds);
    }

    // ───────── 內部 ─────────

    /// <summary>
    /// 正式題的一行已經組好：寫進暫存檔 → <b>寫成功才</b>推進度 → 下一題，或第五題答完就併進結果檔。
    /// 過關與放棄兩條路共用，免得兩邊的順序分岔。寫檔失敗時停在這一題，再點一次就是重試。
    /// </summary>
    /// <param name="level">剛答完的那一關。</param>
    /// <param name="record">那一行。</param>
    private void CommitAnswer(LevelData level, FeedbackRecord record)
    {
        if (!AppendToPartial(record)) return;

        session.MarkAnswered(level);
        if (session.IsComplete) MergePartialIntoResults();
        else EnterCurrentPuzzle();
    }

    /// <summary>
    /// 叫出中止面板，並<b>暫停這一關的時間</b>——顧場的人在面板上想的那幾秒不能算進受試者的前景時間。
    /// 進行中的拖曳一起取消（它也會停拖曳計時）。
    /// ⚠️ 面板開著時如果切出視窗再切回來，<c>LevelController</c> 會把前景計時接回去——那幾秒會算進去。很少發生，先接受。
    /// </summary>
    private void OpenStopPanel()
    {
        isStopPanelOpen = true;
        if (inputController != null) inputController.CancelInteraction();
        LevelSession.Current?.Data?.StopForegroundTiming(GameClock.NowSeconds);

        SetText(stopSummaryText, session == null ? "" :
            $"第 {session.ParticipantNumber} 位　" +
            (session.IsPracticeDone ? $"已完成 {session.AnsweredCount}／{FeedbackPlan.PuzzleCount} 題" : "還在練習題"));
    }

    /// <summary>回到開始面板：清掉這一位、選項、中止面板，卸掉盤面上的關卡。</summary>
    private void ResetToStart()
    {
        session = null;
        partialFilePath = null;
        selectedAgeGroup = null;
        selectedExperience = null;
        selectedWatchedOthers = null;
        isStopPanelOpen = false;

        // 名字一定要清掉：留著上一位的名字，下一位忘了改就會被當成同一個小朋友第二次來，而且不報錯
        if (codeInputField != null) codeInputField.text = string.Empty;

        // 盤面上還是上一位的最後一關。卸掉它，免得面板的條件被舊的那一關滿足
        if (levelController != null) levelController.ExitLevel();

        // 趁換人的空檔把下一位的題單生好（⚠️ 會凍住幾秒）——不讓下一個小朋友按了開始還要等。
        // 生失敗的話狀態列會講，按「開始」時會再試一次
        if (weights != null) EnsurePlanFor(FeedbackPlan.PlanSeedFor(basePlanSeed, highestParticipantNumber + 1));
    }

    /// <summary>
    /// 進練習題：照常進關，然後<b>先擺好五片</b>，只留平行四邊形與一片三角形在抽屜裡。
    ///
    /// ⚠️ <b>一定要排在 <c>EnterLevel</c> 之後</b>：進關的第 4 步會把七片姿態重設、第 5 步會取消互動，
    /// 先擺的話會被那兩步蓋掉。擺完要叫 <c>RefreshAllPiecesFromBoard</c> 照盤面重畫——
    /// 模型改了、畫面不跟上的話，受試者看到的是空盤面，而盤面卻以為五片都在（不報錯）。
    /// </summary>
    private void EnterPractice()
    {
        levelController.EnterLevel(practicePuzzle.Puzzle, practicePuzzle.ReferenceSolution,
                                   practicePuzzle.Difficulty, allowRoll: false);

        LevelData level = LevelSession.Current?.Data;
        if (level == null || !level.TryPrefillAtStart(practiceSlotsToLeaveEmpty))
        {
            ReportStatus("練習題擺不上去（盤面不是空的？）。受試者得從七片開始拼。", isError: true);
            return;
        }
        if (inputController != null) inputController.RefreshAllPiecesFromBoard();
    }

    /// <summary>進這一位受試者現在該做的那一題。沿用 <see cref="LevelController.EnterLevel"/> 的五步順序。</summary>
    private void EnterCurrentPuzzle()
    {
        PlannedPuzzle planned = plannedPuzzles[session.CurrentPlanIndex];

        // allowRoll 傳 false：每個受試者看到的角度要一樣，不然「同一題」其實不是同一個畫面
        levelController.EnterLevel(planned.Puzzle, planned.ReferenceSolution, planned.Difficulty, allowRoll: false);
    }

    /// <summary>
    /// 追加一行到<b>這一位的暫存檔</b>。每題寫一次，當掉最多掉一題，而且暫存檔還在、救得回來。
    /// </summary>
    /// <param name="record">那一行。</param>
    /// <returns>寫成功了沒。</returns>
    private bool AppendToPartial(FeedbackRecord record)
    {
        try
        {
            File.AppendAllText(partialFilePath, JsonUtility.ToJson(record) + "\n", new UTF8Encoding(false));
            return true;
        }
        catch (Exception error)
        {
            // ⚠️ 一定要講出來：安靜地失敗會讓人以為收到了，受試者走了才發現什麼都沒有
            ReportStatus($"寫檔失敗，這一題的答案還沒存：{error.Message}。解決之後再點一次就會重試。", isError: true);
            return false;
        }
    }

    /// <summary>
    /// 把這一位的暫存檔整份追加進結果檔，<b>成功之後才刪暫存檔</b>。暫存檔不存在或是空的時什麼都不做。
    /// ⚠️ 追加成功但刪暫存檔失敗的話，下次開程式會被當成「遺留的暫存檔」回報——
    /// 那時它的內容其實已經在結果檔裡了，<b>不要再手動併一次</b>（狀態列會提醒去比對編號）。
    /// </summary>
    /// <returns>併成功了沒（沒東西可併也算成功）。</returns>
    private bool MergePartialIntoResults()
    {
        if (partialFilePath == null || !File.Exists(partialFilePath)) return true;

        try
        {
            string content = File.ReadAllText(partialFilePath, new UTF8Encoding(false));
            if (content.Length > 0)
            {
                File.AppendAllText(outputFilePath, content, new UTF8Encoding(false));

                // 這一位進了結果檔，他的代號就多來了一次。只在真的併了東西時才加——
                // 沒有任何一行的暫存檔（例如練習題途中就結束）不算來過
                if (session != null) visitsByCode[session.Profile.Code] = VisitsOf(session.Profile.Code) + 1;
            }
        }
        catch (Exception error)
        {
            ReportStatus($"併進結果檔失敗：{error.Message}。資料還在 {partialFilePath}，沒有遺失。", isError: true);
            return false;
        }

        try
        {
            File.Delete(partialFilePath);
        }
        catch (Exception error)
        {
            ReportStatus($"已經併進結果檔，但暫存檔刪不掉：{error.Message}。" +
                         $"⚠️ 請手動刪掉 {partialFilePath}，不要再併一次，否則資料會重複。", isError: true);
        }
        return true;
    }

    /// <summary>讀結果檔找出最大的受試者編號；檔案不存在是 0。讀不懂的行跳過並講出來。</summary>
    /// <returns>最大編號。</returns>
    private int ReadHighestParticipantNumber()
    {
        if (!File.Exists(outputFilePath)) return 0;

        int highest = FeedbackRecord.HighestParticipantNumber(
            File.ReadAllLines(outputFilePath, new UTF8Encoding(false)),
            line => JsonUtility.FromJson<FeedbackRecord>(line),
            out int brokenLineCount);

        if (brokenLineCount > 0)
            Debug.LogWarning($"結果檔有 {brokenLineCount} 行讀不懂，已跳過（多半是當掉時最後一行只寫了一半）。", this);
        return highest;
    }

    /// <summary>開場時從結果檔讀出每個代號來過幾次。</summary>
    private void LoadVisitsByCode()
    {
        visitsByCode.Clear();
        if (!File.Exists(outputFilePath)) return;

        foreach (System.Collections.Generic.KeyValuePair<string, int> entry in FeedbackRecord.CountVisitsByCode(
                     File.ReadAllLines(outputFilePath, new UTF8Encoding(false)),
                     line => JsonUtility.FromJson<FeedbackRecord>(line)))
            visitsByCode[entry.Key] = entry.Value;
    }

    /// <param name="code">標準形式的代號。</param>
    /// <returns>在結果檔裡來過幾次；沒來過是 0。</returns>
    private int VisitsOf(string code) => visitsByCode.TryGetValue(code, out int visits) ? visits : 0;

    /// <returns>輸入框裡的代號（標準形式）；沒有輸入框、沒輸入、或不合法時是 null。</returns>
    private string CurrentCode() =>
        codeInputField == null ? null : FeedbackParticipantCode.Normalize(codeInputField.text);

    /// <summary>
    /// 名字那一行要顯示什麼。⭐ <b>「來過幾次」是給顧場的人確認的</b>，兩個方向都要看：
    /// 應該來過的小朋友顯示「第一次來」→ 名字打得跟上次不一樣（例如 Amy 打成 Ami），兩次連不起來；
    /// 不該來過的顯示「第 2 次來」→ <b>跟別人同名</b>，請他在名字後面加字區分（例如 Amy2）。兩種都不報錯。
    /// </summary>
    /// <param name="code">標準形式的名字，或 null。</param>
    /// <returns>畫面上的字。</returns>
    private string CodeStatus(string code)
    {
        if (codeInputField == null) return "⚠️ 場景沒有接名字輸入框";
        if (string.IsNullOrWhiteSpace(codeInputField.text)) return "英文名字：（還沒輸入）";
        if (code == null)
            return $"英文名字格式不對：只能用半形英文字母、數字、空白、-、'，最多 {FeedbackParticipantCode.MaximumLength} 字（輸入法要切成英文）";

        int visits = VisitsOf(code);
        return visits == 0
            ? $"{code}：第一次來"
            : $"{code}：第 {visits + 1} 次來（之前來過 {visits} 次。如果是第一次來，代表跟別人同名，請在名字後面加字區分）";
    }

    /// <summary>數 <c>unfinished/</c> 裡遺留的暫存檔，順便找出它們最大的編號（新的受試者不能跟它們撞號）。</summary>
    /// <param name="highestNumber">遺留暫存檔裡最大的編號；沒有時是 0。</param>
    /// <returns>遺留暫存檔的數量。</returns>
    private int ScanLeftoverPartials(out int highestNumber)
    {
        highestNumber = 0;
        int count = 0;
        foreach (string path in Directory.GetFiles(unfinishedDirectory))
        {
            if (!FeedbackFiles.TryParsePartialFileName(path, out int number)) continue;
            count++;
            if (number > highestNumber) highestNumber = number;
        }
        return count;
    }

    /// <summary>
    /// 結果檔的資料夾：專案（或 exe）旁邊的 <c>Tools/Feedback/</c>。
    /// 規則跟出題工具一樣（見 <c>PuzzleEntryToolController.ResolveOutputDirectory</c>）：
    /// Editor 裡是專案旁邊，exe 是 exe 所在的資料夾旁邊。⚠️ exe 不要放在沒有寫入權限的地方（例如 Program Files）。
    /// </summary>
    /// <returns>資料夾路徑；不存在就建出來。</returns>
    private static string ResolveOutputDirectory()
    {
        DirectoryInfo parent = Directory.GetParent(Application.dataPath);
        string root = parent != null ? parent.FullName : Application.persistentDataPath;
        string directory = Path.Combine(root, "Tools", "Feedback");

        // 第一次跑的時候資料夾還不存在；不先建的話寫檔會丟例外
        Directory.CreateDirectory(directory);
        return directory;
    }

    /// <param name="panel">面板，可以是 null。</param>
    /// <param name="shown">要不要顯示。</param>
    private static void SetShown(GameObject panel, bool shown)
    {
        if (panel == null || panel.activeSelf == shown) return;
        panel.SetActive(shown);
    }

    /// <param name="label">文字元件，可以是 null。</param>
    /// <param name="text">要顯示的文字。</param>
    private static void SetText(TMP_Text label, string text)
    {
        if (label == null || label.text == text) return;
        label.text = text;
    }

    /// <param name="ageGroup">選了的年齡層。</param>
    /// <returns>畫面上的字。</returns>
    private static string LabelOf(AgeGroup? ageGroup)
    {
        if (!ageGroup.HasValue) return "（還沒選）";
        switch (ageGroup.Value)
        {
            case AgeGroup.EightOrUnder: return "8 歲以下";
            case AgeGroup.NineToTwelve: return "9～12 歲";
            case AgeGroup.ThirteenToSeventeen: return "13～17 歲";
            default: return "18 歲以上";
        }
    }

    /// <param name="watched">選了的「有沒有看別人玩」。</param>
    /// <returns>畫面上的字。</returns>
    private static string LabelOf(WatchedOthers? watched)
    {
        if (!watched.HasValue) return "（還沒選）";
        switch (watched.Value)
        {
            case WatchedOthers.DidNotWatch: return "沒看";
            case WatchedOthers.WatchedBriefly: return "看了一下";
            default: return "看了很久";
        }
    }

    /// <param name="experience">選了的經驗。</param>
    /// <returns>畫面上的字。</returns>
    private static string LabelOf(TangramExperience? experience)
    {
        if (!experience.HasValue) return "（還沒選）";
        switch (experience.Value)
        {
            case TangramExperience.Never: return "0 次";
            case TangramExperience.OneToTenTimes: return "1～10 次";
            default: return "10 次以上";
        }
    }

    /// <summary>把訊息顯示在畫面上，同時寫進 Console。</summary>
    /// <param name="message">訊息。</param>
    /// <param name="isError">是不是錯誤。</param>
    private void ReportStatus(string message, bool isError = false)
    {
        SetText(statusText, message);
        if (isError) Debug.LogError(message, this);
        else Debug.Log(message, this);
    }
}
