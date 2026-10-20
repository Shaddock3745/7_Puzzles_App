using System.Collections.Generic;
using Tangram.Core;
using Tangram.Data;
using UnityEngine;

/// <summary>
/// 這一場在玩哪一種題目（2026-09-23）。
///
/// <b>為什麼模式記在 <see cref="LevelController"/> 身上</b>：因為「下一題要什麼」只有它在回答。
/// 記在別處的話，結束彈窗那顆「下一題」就得自己判斷要生還是要抽——
/// 那等於把同一個決定放到兩個地方，而<b>兩個地方各自認定真相就是安靜的 bug</b>（見 <c>CLAUDE.md</c> 的坑）。
/// 現在彈窗照樣只叫 <c>EnterNextLevel()</c>，一行都不用改。
/// </summary>
public enum GameMode
{
    /// <summary>照玩家實力當場生題（P2-7 ＋ P2-14），這是原本的遊玩迴圈。</summary>
    GeneratedByRating,

    /// <summary>從題庫隨機抽具象題（貓、船之類），一輪之內不重複。</summary>
    Figurative,

    /// <summary>
    /// 拍照或選檔 → 辨識模組取輪廓 → 照輪廓出題（2026-10-14，名稱暫定）。
    /// ⚠️ <b>不結算實力</b>，「下一題」是再跳一次選擇視窗（2026-10-17）。
    /// ⚠️ <b>辨識還是假的</b>（見 <c>ContourScanning.cs</c>）；取得照片（2026-10-17）與 beam search 逼近（2026-10-14）是真的。
    /// </summary>
    Scan
}

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

    [Tooltip("掃描題的照片對照（2026-10-14）。沒有也能跑，只是看不到照片。")]
    [SerializeField] private ScanPhotoView scanPhotoView;

    [Tooltip("掃描的拍照畫面（2026-10-17）。沒有的話「新拍一張」會失敗回主畫面，「從檔案選擇」照常能用。")]
    [SerializeField] private CameraCaptureView cameraCaptureView;

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

    /// <summary>
    /// 目前的難度權重，讀不到時是 null。<b>唯讀</b>——給回饋收集場景自己生題單用（2026-09-13）。
    ///
    /// ⚠️ <b>要權重的人一律跟這裡拿，不要自己再讀一次檔</b>：讀兩次就是兩份，哪天其中一邊換了來源就會分家。
    /// 在 <c>Start</c> 或之後讀——它在這支的 <c>Awake</c> 裡填，而 Unity 保證所有 <c>Awake</c> 都跑完才開始跑 <c>Start</c>。
    /// </summary>
    public DifficultyWeights CurrentDifficultyWeights => difficultyWeights;

    /// <summary>ELO 設定檔在 <c>Resources</c> 裡的名字（不含副檔名）。</summary>
    private const string SkillRatingSettingsResourceName = "SkillRatingSettings";

    /// <summary>ELO 設定，Awake 讀一次。讀不到是 null，那時不建實力追蹤、不進任何一關。</summary>
    private SkillRatingSettings ratingSettings;

    // ───────── 進度存檔（P2-8b，2026-10-17）─────────

    /// <summary>
    /// 這一場要不要讀寫進度存檔。<b>預設要</b>；回饋收集場景在 <c>Awake</c> 叫 <see cref="SuppressProgressPersistence"/> 關掉。
    /// 不是 Inspector 欄位，理由見那支。
    /// </summary>
    private bool persistProgress = true;

    /// <summary>
    /// 進度存檔。<b>第一次讀檔時才建</b>（<see cref="LoadSavedRating"/>），不存進度的場景永遠是 null。
    /// 路徑是 <c>Application.persistentDataPath</c>——⚠️ <b>在 Windows 上 Editor 跟 exe 是同一個檔</b>，
    /// 在 Editor 裡玩會改到這台電腦上 exe 的進度。
    /// </summary>
    private PlayerProgressFile progressFile;

    /// <summary>
    /// 這次開啟之前已經結算過幾局（存檔裡的 <c>settledCount</c>）。寫檔時加上這一場的局數，
    /// 就是累計的局數。沒有存檔時是 0。
    /// </summary>
    private int settledCountBeforeThisRun;

    /// <summary>現在這份權重的指紋，第一次存檔時算一次（SHA-256，每局算也不貴，只是沒必要）。</summary>
    private string weightsFingerprint;

    /// <summary>
    /// 題庫在 <c>Resources</c> 裡的名字（不含副檔名）。
    ///
    /// ⚠️ <b>檔案的副檔名必須是 <c>.txt</c></b>（或 <c>.json</c>／<c>.bytes</c>）。出題工具寫出來的是
    /// <c>.jsonl</c>，而 <b>Unity 不把 <c>.jsonl</c> 當成文字資產</b>——那樣 <c>Resources.Load</c> 會回 null，
    /// 而畫面上只會看到「讀不到題庫」，查不到真正的原因是副檔名。
    /// </summary>
    private const string PuzzleLibraryResourceName = "puzzle_library";

    /// <summary>
    /// 題庫，<b>第一次要抽具象題時才讀</b>（見 <see cref="EnsurePuzzleLibraryLoaded"/>）。讀不到是 null。
    /// </summary>
    private PuzzleLibrary puzzleLibrary;

    /// <summary>具象題的洗牌袋：一輪之內不重複，抽完才重洗。跟題庫一起建。</summary>
    private PuzzleShuffleBag figurativeBag;

    // ───────── 掃描模式（2026-10-14）─────────

    /// <summary>
    /// 新拍一張（拍照畫面＋辨識模組）。<b>第一次要掃描時才建</b>，見 <see cref="EnsureScanPipeline"/>。
    /// 沒接拍照畫面時是 null。
    /// </summary>
    private IContourScanner cameraScanner;

    /// <summary>從檔案選擇（相簿＋辨識模組）。跟 <see cref="cameraScanner"/> 一起建、共用同一個辨識模組。</summary>
    private IContourScanner galleryScanner;

    /// <summary>輪廓 → 題目。跟兩個掃描一起建。</summary>
    private IContourPuzzleBuilder contourPuzzleBuilder;

    /// <summary>
    /// ⭐ <b>掃描模式要選照片來源了</b>（2026-10-17）。選擇視窗訂閱它，把自己顯示出來；
    /// 玩家選完叫回 <see cref="BeginScan"/> 或 <see cref="CancelScanSourceChoice"/>。
    ///
    /// 用事件的理由跟 <see cref="ScanAborted"/> 一樣：選擇視窗指向這支，這支再指回去就是環。
    /// ⚠️ <b>沒有人訂閱</b>（場景沒接選擇視窗）時會報錯並回主畫面，不會停在一個什麼都沒有的畫面上。
    /// </summary>
    public event System.Action ScanSourceRequested;

    /// <summary>
    /// ⭐ <b>掃描沒有進關就結束了</b>（取消或失敗）。主畫面訂閱它，把自己顯示回來。
    ///
    /// <b>為什麼用事件，不直接叫主畫面</b>：主畫面已經指向這支（按鈕要叫 <see cref="StartMode"/>），
    /// 這支再指回去就是環。這裡也沒有順序問題要防（只有一個訂閱者、做的事只有顯示自己），
    /// 所以不違反類別說明裡「換關不發事件」那一條。
    /// </summary>
    public event System.Action ScanAborted;

    /// <summary>
    /// 這一場在玩哪一種題目。<b>預設是原本的遊玩迴圈</b>，所以沒有主畫面的場景（回饋收集、工具）行為不變。
    /// </summary>
    public GameMode CurrentMode { get; private set; } = GameMode.GeneratedByRating;

    /// <summary>
    /// ⭐ <b>掃描等待中</b>（2026-10-17）：從跳選擇視窗那一刻起，到進關或中止為止。等待遮罩（<see cref="ScanWaitingCover"/>）看它決定要不要蓋住盤面——
    /// 不蓋的話，選擇視窗與拍照畫面之間、拍完到進關之間都會露出空的（或上一關的）盤面。
    ///
    /// <b>只有三個地方會改它</b>：<see cref="RequestScanSource"/> 設成 true；<see cref="EnterLevel"/>（任何模式）與
    /// <see cref="AbortScan"/> 設成 false。進關與中止是掃描<b>所有出口的匯集點</b>，清在這兩處就不會漏掉哪一條路。
    /// </summary>
    public bool IsScanPending { get; private set; }

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
        if (scanPhotoView == null) scanPhotoView = FindFirstObjectByType<ScanPhotoView>();

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
        // 跟權重同一套規則：沒有預設值，讀不到就吵。
        // ⚠️ tracker 本身在 Start 才建（P2-8b），理由見 CreateRatingTracker
        ratingSettings = LoadJsonResource(
            SkillRatingSettingsResourceName, SkillRatingSettings.FromJson, "沒有 ELO 設定就不知道下一題要多難");
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
        // ⚠️ 一定要在自動進關之前：進關要問實力出題
        CreateRatingTracker();

        if (!loadFirstLevelOnStart) return;
        EnterNextLevel();
    }

    /// <summary>
    /// 讀存檔、建實力追蹤（P2-8b）。設定讀不到時不建，<see cref="EnterNextLevel()"/> 看到 null 就不進關。
    ///
    /// ⚠️ <b>為什麼在 <c>Start</c> 不在 <c>Awake</c></b>：回饋收集場景要在它自己的 <c>Awake</c> 裡叫
    /// <see cref="SuppressProgressPersistence"/>，而兩支 <c>Awake</c> 誰先跑是未定義的。
    /// 留在 <c>Awake</c> 的話，運氣不好時存檔已經讀進來了才被告知「這一場不存」——
    /// 讀進來的實力雖然沒人用，但這是賭順序，跟 <see cref="SuppressAutoStart"/> 同一個理由。
    /// Unity 保證所有 <c>Awake</c> 都跑完才開始跑 <c>Start</c>。
    ///
    /// 💡 在那之前 <c>LevelSession.Rating</c> 是 null，而這支的 <c>Update</c> 不會比自己的 <c>Start</c> 早跑，
    /// 所以結算那條路不受影響。別人要在 <c>Start</c> 裡讀實力的話會讀到 null——目前沒有這樣的讀者。
    /// </summary>
    private void CreateRatingTracker()
    {
        if (ratingSettings == null) return;

        int? savedRating = LoadSavedRating();
        session.Rating = new SkillRatingTracker(ratingSettings, savedRating ?? ratingSettings.initialRating);
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

        // 權重讀不到時 Awake 已經報過錯，而且那樣根本進不了任何一關，沒有東西要結算。
        // 這一行不能省：TrySettle 沒拿到範圍會丟例外，那在這裡就是每幀一次
        if (rating == null || difficultyWeights == null) return;
        if (!rating.TrySettle(session.Data, difficultyWeights.TargetRange, out RatingUpdate update)) return;

        SaveRating(SkillRating.RoundRating(update.RatingAfter));

        // 完整的算式在監控面板（P2-15，Window → Tangram → 監控面板）。這一行留著給沒開面板、或看 exe 的 log 時用
        string outcome = update.Outcome == LevelOutcome.Solved ? "過關" : "放棄";
        Debug.Log($"第 {levelIndex} 關結算：{outcome}，難度 {update.Difficulty}，" +
                  $"預期 {update.ExpectedPerformance:F2}、實際 {update.ActualPerformance:F2}" +
                  $"（時間代價 {update.TimeCostSeconds:F1} 秒），K {update.EffectiveKFactor:0.##}，" +
                  $"實力 {update.RatingBefore:F1} → {update.RatingAfter:F1}（{update.RatingChange:+0.0;-0.0;0.0}），" +
                  $"下一題：{update.Trend}", this);
    }

    // ───────── 進度的讀寫（P2-14 留的接縫，P2-8b 2026-10-17 落檔）─────────
    //
    // ⚠️ 刻意不為這兩支開介面：實作只有一個，接縫是方法名（同 EnterNextLevel 那段的標準）。
    // 檔案怎麼讀寫、壞檔怎麼辦在 Core 的 PlayerProgressFile（有離線測試），這裡只決定「讀到什麼就怎麼做」。

    /// <summary>
    /// 這一場不讀也不寫進度存檔（P2-8b）。<b>回饋收集場景在 <c>Awake</c> 叫這支。</b>
    ///
    /// ⚠️ <b>為什麼需要</b>：回饋收集場景也有這支，而且每一題照樣結算。不擋的話受試者玩的題會寫進玩家存檔——
    /// 而回饋收集的 exe 跟遊戲是同一個專案、<c>persistentDataPath</c> 是同一個，
    /// <b>在筆電上收一輪回饋就把那台的遊戲進度洗掉了</b>，不報錯。
    ///
    /// <b>為什麼不做成 Inspector 勾選</b>：忘了勾的症狀看起來一切正常（同 <see cref="SuppressAutoStart"/>）。
    /// 一定要在 <c>Awake</c> 裡叫——讀檔在 <see cref="Start"/>，理由見 <see cref="CreateRatingTracker"/>。
    /// </summary>
    public void SuppressProgressPersistence() => persistProgress = false;

    /// <summary>
    /// 讀存檔裡的實力（P2-8b）。
    ///
    /// <list type="bullet">
    /// <item><b>沒有存檔</b>：第一次玩，回 null（從起始實力開始），印一行 log</item>
    /// <item><b>合法</b>：回存檔裡的實力，印一行 log 說從哪裡來</item>
    /// <item><b>壞檔</b>：<b>報錯</b>，回 null。壞檔已經被改名留著，錯誤訊息寫出備份在哪</item>
    /// <item><b>權重指紋不一樣</b>：<b>照用、只寫警告</b>（2026-10-17 定，理由見 <see cref="PlayerProgress.weightsFingerprint"/>）</item>
    /// </list>
    ///
    /// ⭐ <b>開場那行 log 一定要說實力從哪裡來</b>：錄影是改 <see cref="SkillRatingSettings.initialRating"/> 分段錄的，
    /// 有存檔的話改了不會生效——不看 log 的話完全看不出來。清除請用選單〈Window → Tangram → 清除進度存檔〉。
    /// </summary>
    /// <returns>存檔裡的實力；沒有存檔、壞檔、或這一場不存進度時是 null。</returns>
    private int? LoadSavedRating()
    {
        if (!persistProgress) return null;

        progressFile = new PlayerProgressFile(Application.persistentDataPath);
        PlayerProgressLoadResult result = progressFile.Load(System.DateTime.UtcNow);

        switch (result.Status)
        {
            case PlayerProgressLoadStatus.Loaded:
                PlayerProgress progress = result.Progress;
                settledCountBeforeThisRun = progress.settledCount;
                Debug.Log($"實力從存檔讀到 {progress.skillRating}（累計 {progress.settledCount} 局，" +
                          $"上次存於 {progress.savedAtUtc} UTC）：{progressFile.FilePath}", this);
                WarnIfWeightsChanged(progress.weightsFingerprint);
                return progress.skillRating;

            case PlayerProgressLoadStatus.Corrupt:
                string backup = result.CorruptBackupPath != null
                    ? $"壞檔已經改名留在 {result.CorruptBackupPath}。"
                    : $"⚠️ 壞檔改名失敗，還在 {progressFile.FilePath}，下一次存檔會蓋掉它——要留的話現在先手動複製一份。";
                Debug.LogError($"進度存檔讀不懂，實力從起始的 {ratingSettings.initialRating} 開始。{result.Problem}。{backup}", this);
                return null;

            default:
                Debug.Log($"沒有進度存檔，實力從起始的 {ratingSettings.initialRating} 開始。存檔會寫在 {progressFile.FilePath}", this);
                return null;
        }
    }

    /// <summary>存檔裡的權重指紋跟現在的不一樣時寫一條警告。照用的理由見 <see cref="PlayerProgress.weightsFingerprint"/>。</summary>
    /// <param name="savedFingerprint">存檔裡的指紋，可能是空的。</param>
    private void WarnIfWeightsChanged(string savedFingerprint)
    {
        // 權重讀不到時 Awake 已經報過錯，也不會進任何一關，不用再講
        if (difficultyWeights == null) return;

        string currentFingerprint = CurrentWeightsFingerprint();
        if (savedFingerprint == currentFingerprint) return;

        string saved = string.IsNullOrEmpty(savedFingerprint) ? "（沒有記錄）" : savedFingerprint;
        Debug.LogWarning($"進度存檔是用另一份權重存的（存檔 {saved}，現在 {currentFingerprint}）。" +
                         "實力照用，但同一個數字的意思可能跟著權重移動了一點。", this);
    }

    /// <returns>現在這份權重的指紋；權重讀不到時是 null。</returns>
    private string CurrentWeightsFingerprint()
    {
        if (difficultyWeights == null) return null;
        if (weightsFingerprint == null) weightsFingerprint = difficultyWeights.Fingerprint();
        return weightsFingerprint;
    }

    /// <summary>
    /// 把實力寫進存檔（P2-8b）。<b>每結算一局叫一次</b>——實力只在結算時變，所以切背景、關 App 時不另外寫。
    ///
    /// 傳進來的是<b>四捨五入後的整數</b>，記憶體裡的實力照樣留小數繼續算——
    /// 所以重開 App 最多少掉 0.5 分，但玩同一場的時候不會累積誤差（2026-09-13 定）。
    ///
    /// ⚠️ <b>寫不進去時報錯、遊戲照常玩</b>，下一局結算時自然會再試一次。不擋玩家：
    /// 存檔失敗的代價是「重開 App 實力回到上一次存成功的值」，不值得讓這一局玩不下去。
    /// </summary>
    /// <param name="rating">四捨五入後的實力。</param>
    private void SaveRating(int rating)
    {
        // progressFile 是 null 有兩種：這一場不存進度，或設定讀不到（那根本不會結算）
        if (!persistProgress || progressFile == null) return;

        // 這一場的局數＝走勢的筆數（結算一局加一筆，見 SkillRatingTracker.History），跟這一行在同一格更新
        int settledCount = settledCountBeforeThisRun + session.Rating.History.Count;

        try
        {
            progressFile.Save(PlayerProgress.Create(rating, settledCount, CurrentWeightsFingerprint(), System.DateTime.UtcNow));
        }
        catch (System.Exception error) when (error is System.IO.IOException || error is System.UnauthorizedAccessException)
        {
            Debug.LogError($"進度存檔寫不進去（實力 {rating}），這一局的結果重開 App 之後會不見。下一局結算時會再試。" +
                           $"{error.Message}：{progressFile.FilePath}", this);
        }
    }

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

        // ⭐ 模式的分派只有這一處（2026-09-23）。結束彈窗那兩顆「下一題」照樣叫這支，
        // 它們不知道也不需要知道現在在玩哪一種
        if (CurrentMode == GameMode.Figurative)
        {
            EnterRandomFigurativeLevel();
            return;
        }

        // 掃描模式的「下一題」＝再掃一次（2026-10-14 定），先跳選擇視窗問照片從哪來（2026-10-17 定）。
        // 進關要等掃描回來，所以這裡只是開始
        if (CurrentMode == GameMode.Scan)
        {
            RequestScanSource();
            return;
        }

        // 設定讀不到時 Awake 已經報過錯了，這裡不再報一次
        SkillRatingTracker rating = session?.Rating;
        if (rating == null) return;

        EnterNextLevel(rating.NextTargetDifficulty);
    }

    /// <summary>
    /// 換一個模式，並且馬上進第一關（2026-09-23）。<b>主畫面那幾顆按鈕叫的就是這支。</b>
    /// ⚠️ 掃描模式不是「馬上」：要等掃描回來才進關，取消或失敗會發 <see cref="ScanAborted"/>。
    /// </summary>
    /// <param name="mode">要玩哪一種。</param>
    public void StartMode(GameMode mode)
    {
        CurrentMode = mode;
        EnterNextLevel();
    }

    /// <summary>
    /// 開場不要自己進關（2026-09-23）。<b>場景裡有主畫面時由主畫面在 <c>Awake</c> 叫這支。</b>
    ///
    /// <b>為什麼不叫人去取消勾選 <c>loadFirstLevelOnStart</c></b>：忘了取消的症狀是
    /// 「主畫面後面已經偷偷開了一關」——看起來一切正常，直到發現第一次按按鈕時題目換了一題。
    /// 用程式講一次，就不會有人忘記。
    ///
    /// ⚠️ 這支一定要在 <c>Awake</c> 裡叫：Unity 保證所有 <c>Awake</c> 都跑完才開始跑 <c>Start</c>，
    /// 而自動進關是在 <see cref="Start"/> 裡。在 <c>Start</c> 裡叫就賭上了兩支誰先跑，那是未定義的。
    /// </summary>
    public void SuppressAutoStart() => loadFirstLevelOnStart = false;

    /// <summary>
    /// 從題庫隨機抽一題具象題進關（2026-09-23）。
    ///
    /// <b>難度當場算</b>，不是存在檔案裡的——理由見 <see cref="PuzzleLibrary"/>：
    /// 存起來就有兩個來源，調權重時一定有一邊沒跟上。
    ///
    /// ⚠️ <b><c>allowRoll</c> 傳 <c>false</c></b>：跟生成題同一個理由，而且
    /// <c>LevelSession.Load</c> 的說明本來就寫著「精選題傳 false」——具象題是人拼的，
    /// 存進去的那個角度就是它該有的樣子（船是正的，轉了就不像船了）。
    ///
    /// ⚠️ <b>不帶出題紀錄</b>（<c>generation</c> 是 null）：那是給監控面板看「出題器叫了幾次、
    /// 正中還是將就」的，抽題根本沒有那回事。面板那邊本來就處理「沒有紀錄」。
    /// </summary>
    private void EnterRandomFigurativeLevel()
    {
        if (difficultyWeights == null) return;      // Awake 已經報過錯
        if (!EnsurePuzzleLibraryLoaded()) return;

        levelIndex++;

        PuzzleLibrary.Entry entry = puzzleLibrary.FigurativeShapes[figurativeBag.Next()];
        int difficulty = entry.Score(difficultyWeights);

        Debug.Log($"第 {levelIndex} 關：具象題「{entry.Name}」，難度 {difficulty}", this);

        EnterLevel(entry.Puzzle, entry.Pieces, difficulty, allowRoll: false);
    }

    /// <summary>
    /// 需要的話把題庫讀進來。<b>第一次要抽具象題時才讀</b>——38 題要跑幾百次輪廓追蹤，
    /// 約零點幾秒；只玩生成題的人不該付這個成本。
    ///
    /// 讀不到就大聲報錯並回 <c>false</c>，跟權重、ELO 設定同一套規則（沒有退路，見 <see cref="difficultyWeights"/>）。
    /// </summary>
    /// <returns>題庫可以用了沒。</returns>
    private bool EnsurePuzzleLibraryLoaded()
    {
        if (puzzleLibrary != null) return true;

        TextAsset asset = Resources.Load<TextAsset>(PuzzleLibraryResourceName);
        if (asset == null)
        {
            Debug.LogError(
                $"Resources 裡沒有 {PuzzleLibraryResourceName}：具象題模式進不了關。" +
                "⚠️ 先確認副檔名是 .txt——Unity 不把 .jsonl 當成文字資產，那樣這裡永遠是 null。", this);
            return false;
        }

        try
        {
            puzzleLibrary = PuzzleLibrary.FromText(asset.text);
        }
        catch (System.ArgumentException exception)
        {
            // 訊息裡會講是第幾行讀不進來，原樣帶出來
            Debug.LogError($"題庫讀不進來，具象題模式進不了關。{exception.Message}", this);
            return false;
        }

        if (puzzleLibrary.FigurativeShapes.Count == 0)
        {
            Debug.LogError("題庫裡一題具象題都沒有（全部都是凸多邊形？），具象題模式進不了關。", this);
            puzzleLibrary = null;
            return false;
        }

        // 固定種子，跟出題器同一套理由：看到怪東西才重現得出來
        figurativeBag = new PuzzleShuffleBag(puzzleLibrary.FigurativeShapes.Count, new System.Random(generatorSeed));
        return true;
    }

    // ───────── 掃描模式（2026-10-14）─────────

    /// <summary>
    /// 請選擇視窗出來問「照片從哪來」（2026-10-17）。<b>沒有人接</b>就報錯回主畫面。
    /// </summary>
    private void RequestScanSource()
    {
        if (ScanSourceRequested == null)
        {
            Debug.LogError("掃描模式要跳選擇視窗，但場景裡沒有 ScanSourcePanel 在聽（ScanSourceRequested 沒人訂閱），回主畫面。", this);
            AbortScan();
            return;
        }

        // 有人接才算開始等：沒人接的話上面已經中止了，遮罩不該亮
        IsScanPending = true;
        ScanSourceRequested();
    }

    /// <summary>
    /// 用指定的照片來源開始一次掃描（2026-10-17 起由選擇視窗叫）。<b>進關要等 <see cref="OnScanFinished"/></b>，這裡只負責開頭。
    ///
    /// 任何一個掃描還沒回來就什麼都不做——玩家連按兩下不該開兩次相機。
    /// </summary>
    /// <param name="source">選擇視窗上按的是哪一顆。</param>
    public void BeginScan(ScanPhotoSource source)
    {
        // 選擇視窗開著的時候不可能換模式，這條是保險：真的不是掃描模式，進關會蓋掉玩家正在玩的那一題
        if (CurrentMode != GameMode.Scan)
        {
            Debug.LogWarning($"不在掃描模式（現在是 {CurrentMode}），不開始掃描。", this);
            return;
        }

        // ⚠️ 失敗一律回主畫面：叫這支的時候主畫面與選擇視窗都已經藏起來了，默默 return 的話玩家會停在空畫面上
        if (difficultyWeights == null || !EnsureScanPipeline())
        {
            AbortScan();        // 權重讀不到的話 Awake 已經報過錯
            return;
        }

        if (cameraScanner?.IsScanning == true || galleryScanner.IsScanning) return;

        IContourScanner scanner = source == ScanPhotoSource.Camera ? cameraScanner : galleryScanner;
        if (scanner == null)
        {
            Debug.LogError("LevelController 的「拍照畫面」欄位沒填：「新拍一張」用不了，回主畫面。", this);
            AbortScan();
            return;
        }

        scanner.BeginScan(OnScanFinished);
    }

    /// <summary>選擇視窗按了「取消」（2026-10-17）：回主畫面。</summary>
    public void CancelScanSourceChoice()
    {
        Debug.Log("選擇視窗取消，回主畫面。", this);
        AbortScan();
    }

    /// <summary>
    /// 掃描回來了：讀輪廓 → 出題 → 進關。<b>任何一步失敗都回主畫面</b>，不留在一個沒有題目的畫面上。
    /// </summary>
    /// <param name="result">掃描的結果。</param>
    private void OnScanFinished(ContourScanResult result)
    {
        // ⚠️ 等待的期間模式可能換掉了（真的相機開著的時候玩家可能回主畫面選別的）。
        // 那時候這個結果已經沒人要了，進關的話會蓋掉玩家剛選的那一題
        if (CurrentMode != GameMode.Scan)
        {
            DiscardPhoto(result.Photo);
            return;
        }

        if (result.Status == ContourScanStatus.Cancelled)
        {
            Debug.Log("掃描取消，回主畫面。", this);
            AbortScan();
            return;
        }

        if (result.Status == ContourScanStatus.Failed)
        {
            Debug.LogWarning($"掃描失敗，回主畫面：{result.FailureReason}", this);
            AbortScan();
            return;
        }

        RecognizedContour contour;
        try
        {
            contour = RecognizedContour.FromJson(result.ContourJson);
        }
        catch (System.ArgumentException exception)
        {
            // 訊息會講是哪裡不合法（截斷、點太少、面積是零……），原樣帶出來
            Debug.LogWarning($"辨識模組交回來的輪廓讀不進來，回主畫面。{exception.Message}", this);
            DiscardPhoto(result.Photo);
            AbortScan();
            return;
        }

        // ⚠️ 逼近是同步的，畫面會凍住（PC Release 約 0.7 秒，Editor 的 Debug 模式可能好幾秒，手機還沒量）。
        // 刻意先接受：挑戰模式換關也會凍住，兩件一起排在 P5-2（#37）用背景執行緒處理，這裡另開一條會變成兩套。
        // 所以這一版只做一件事：把花了幾秒印出來，讓 P5-2 有實際數字
        var clock = System.Diagnostics.Stopwatch.StartNew();
        ContourPuzzle built;
        int difficulty;
        try
        {
            built = contourPuzzleBuilder.Build(contour);
            if (built == null)
            {
                Debug.LogWarning($"這條輪廓（{contour.PixelPoints.Count} 個點）逼近不出題目，回主畫面。", this);
                DiscardPhoto(result.Photo);
                AbortScan();
                return;
            }

            // 難度當場算，跟具象題同一套（見 ContourPuzzle 為什麼不帶難度）。
            // 掃描題不結算，算它只是為了監控面板與 log 看得到
            difficulty = DifficultyEvaluator.Score(built.Puzzle, built.ReferenceSolution, difficultyWeights);
        }
        catch (System.Exception exception)
        {
            // ⚠️ 一定要接住：這裡是掃描的回呼，主畫面已經藏起來了——例外沒接住的話，玩家會停在一個
            // 沒有主畫面也沒有題目的畫面上。已知會丟的是難度特徵（面積不是 32、外框有零寬度的刺時），
            // 理論上碰不到（候選跟出題器同一套），但輸入是使用者的照片，所以不賭
            Debug.LogError("逼近或算難度時出事了，回主畫面。例外的完整內容在下一行。", this);
            Debug.LogException(exception, this);
            DiscardPhoto(result.Photo);
            AbortScan();
            return;
        }

        levelIndex++;

        Debug.Log($"第 {levelIndex} 關：{built.Name}，逼近花了 {clock.Elapsed.TotalSeconds:0.00} 秒，" +
                  $"輪廓 {contour.PixelPoints.Count} 個點（{contour.ImageWidth}×{contour.ImageHeight}），" +
                  $"難度 {difficulty}，不結算實力", this);

        // allowRoll 傳 false：跟具象題同一個理由——照片裡是正的貓，題目也要是正的
        EnterLevel(built.Puzzle, built.ReferenceSolution, difficulty, allowRoll: false,
                   countsTowardRating: false);

        HandPhotoToView(result.Photo, contour, built);
    }

    /// <summary>
    /// 把照片與它該畫在哪交給照片對照（2026-10-14）。<b>一定要在 <see cref="EnterLevel"/> 之後</b>——
    /// 照片要綁的是「進關之後的那一關」，而且要用那一關的盒子原點。
    ///
    /// 換算鏈：圖的四個角（像素）→ 輪廓座標（<c>RecognizedContour.ImageBoundsInModel</c>）
    /// → 題目座標（加 <c>ContourPuzzle.OutlineOffset</c>）→ 盤面座標（加盒子原點）。
    /// ⚠️ 盒子原點現在恆為 0，但照參考解的做法照樣加——不加的話哪天改了原點規則，照片會整張偏掉而且不報錯。
    ///
    /// ⭐ <b>先對尺寸</b>（2026-10-17）：照片的寬高必須等於 JSON 的 <c>width</c>／<c>height</c>（辨識模組的約定）。
    /// 對不上代表點不在這張照片的座標上，硬疊會整張歪掉而且不報錯——所以改畫 JSON 的剪影並寫警告。
    /// ⚠️ 假的辨識模組期間<b>每一次都對不上</b>（樣本是 320 × 320），看到的永遠是剪影，那是預期的。
    /// </summary>
    private void HandPhotoToView(Texture2D photo, RecognizedContour contour, ContourPuzzle built)
    {
        if (scanPhotoView == null)
        {
            DiscardPhoto(photo);        // 沒有人畫，也沒有人會釋放它
            return;
        }

        if (photo == null || photo.width != contour.ImageWidth || photo.height != contour.ImageHeight)
        {
            if (photo != null)
                Debug.LogWarning($"照片是 {photo.width}×{photo.height}，辨識模組的 JSON 講的是 " +
                                 $"{contour.ImageWidth}×{contour.ImageHeight}，對不上：照片對照改畫剪影。" +
                                 "（假的辨識模組期間每次都會這樣。）", this);
            DiscardPhoto(photo);
            photo = ContourStandInPhoto.Draw(contour);
        }

        LevelData level = session.Data;
        Vector2 shift = built.OutlineOffset + level.BoxOrigin.ToVector2();
        (Vector2 minimum, Vector2 maximum) = contour.ImageBoundsInModel();
        scanPhotoView.SetPhoto(level, photo, minimum + shift, maximum + shift);
    }

    /// <summary>
    /// 掃描結果帶來的照片用不到了（沒進關、或沒有人畫）：釋放掉。執行時建的貼圖不釋放會一直佔記憶體。
    /// </summary>
    private static void DiscardPhoto(Texture2D photo)
    {
        if (photo != null) Destroy(photo);
    }

    /// <summary>掃描沒有進關就結束了：讓主畫面顯示回來。</summary>
    private void AbortScan()
    {
        // 先收遮罩再叫主畫面回來：兩件事同一幀發生，順序其實看不出來，但「等待結束」在邏輯上先於「回主畫面」
        IsScanPending = false;
        ScanAborted?.Invoke();
    }

    /// <summary>
    /// 需要的話把掃描的各段建起來。<b>第一次要掃描時才建</b>，只玩別的模式的人不付這個成本。
    ///
    /// <b>辨識使用 U2Net</b>：拍照與選檔共用照片 → JSON 的模組，失敗走既有掃描失敗流程。
    /// <b>取得照片是真的</b>（2026-10-17 起）：拍照畫面與相簿各一個掃描，共用同一個辨識模組。
    /// <b>出題是真的</b>（2026-10-14 起）：beam search 逼近，見 <see cref="BeamSearchContourPuzzleBuilder"/>。
    ///
    /// 💡 回傳 bool 是留給「辨識模組載不起來」（例如模型檔不見）用的；現在永遠是 true。
    /// 相機打不開、權限被拒這類事是拍照當下才知道的，走掃描的「失敗」回來，不在這裡擋。
    /// </summary>
    /// <returns>掃描可以用了沒。</returns>
    private bool EnsureScanPipeline()
    {
        if (galleryScanner != null) return true;

        IContourRecognizer recognizer = new U2NetContourRecognizer(this);

        galleryScanner = new PhotoContourScanner(new GalleryPhotoSource(this), recognizer);

        // 拍照畫面沒接不在這裡報：只玩「從檔案選擇」的人不該看到錯誤，按了「新拍一張」才報（BeginScan）
        if (cameraCaptureView != null) cameraScanner = new PhotoContourScanner(cameraCaptureView, recognizer);

        // 預設參數（N = 100、從第 5 片開始微調……）是驗收工具量過的，見 ContourApproximationSettings
        contourPuzzleBuilder = new BeamSearchContourPuzzleBuilder();
        return true;
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

            // ⚠️ 出題紀錄照樣帶進去（P2-15），下場是失敗——不標出來的話，
            // 走勢上那一跳會看起來像 ELO 壞了
            EnterLevel(fallbackPuzzle, fallback,
                       DifficultyEvaluator.Score(fallbackPuzzle, fallback, difficultyWeights),
                       allowRoll: true, generation: GenerationRecord.From(result));
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
        EnterLevel(result.Puzzle, result.ReferenceSolution, result.Difficulty, allowRoll: false,
                   generation: GenerationRecord.From(result));
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
    /// <param name="generation">
    /// 出題紀錄（P2-15），給監控面板看。<b>選填</b>：只有 <see cref="EnterNextLevel(int)"/> 會給，
    /// 回饋收集場景的題單與練習題不給。
    /// </param>
    /// <param name="countsTowardRating">
    /// 要不要結算實力（2026-10-14）。<b>選填、預設 true</b>，只有掃描題傳 false，見 <c>LevelData.CountsTowardRating</c>。
    /// </param>
    public void EnterLevel(Puzzle puzzle, IReadOnlyList<Placement> referenceSolution, int difficulty, bool allowRoll,
                           GenerationRecord? generation = null, bool countsTowardRating = true)
    {
        // 0. 掃描的等待到此為止（2026-10-17）。不判模式：掃描等到一半換成別的模式進關，遮罩也該收掉
        IsScanPending = false;

        // 1. 載入：算原點、建 Region、換掉整包資料
        session.Load(puzzle, referenceSolution, difficulty, allowRoll, generation, countsTowardRating);
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
