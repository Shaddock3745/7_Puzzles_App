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
    /// 四段都是真的：取得照片（2026-10-17）、協作者的 U2Net 辨識（2026-10-20）、beam search 逼近（2026-10-14）、選題畫面（2026-10-21）。
    /// </summary>
    Scan
}

/// <summary>
/// ⭐ 掃描為什麼沒有進關（2026-10-23）。<see cref="LevelController.ScanAborted"/> 帶著它，主畫面照它決定給玩家看哪一句話。
/// </summary>
public enum ScanAbortReason
{
    /// <summary>玩家自己取消（選擇視窗、相機、相簿、選題畫面）。<b>不顯示任何訊息</b>——那是他選的。</summary>
    Cancelled,

    /// <summary>拿照片時失敗：相機打不開、沒權限、照片讀不出來。</summary>
    PhotoFailed,

    /// <summary>辨識失敗：模型看不出東西，或交回來的輪廓讀不進來。</summary>
    RecognitionFailed,

    /// <summary>出題失敗：一個候選都逼不出來，或算難度時出事。</summary>
    PuzzleFailed,

    /// <summary>設定或程式的問題（場景沒接好、設定檔讀不到）。玩家能做的只有再試一次，技術原因在 log。</summary>
    Unexpected,
}

/// <summary>
/// ⭐ 等待遮罩上要寫哪一個階段（P5-2，2026-10-23）。<see cref="LevelController.WaitingStage"/> 回答，<see cref="ScanWaitingCover"/> 照著換字。
/// </summary>
public enum LevelWaitingStage
{
    /// <summary>沒有在等，遮罩不出現。</summary>
    None,

    /// <summary>
    /// 在等，但不是在算東西：掃描的選擇視窗或選題畫面開著（遮罩墊在它們底下）。寫「處理中」。
    /// </summary>
    Waiting,

    /// <summary>照片交給辨識模組了（拍照畫面與相簿的系統畫面蓋在遮罩上面，所以實際看到的是選完之後那段）。</summary>
    Recognizing,

    /// <summary>出題中：挑戰模式在等背景生下一題，或掃描的候選在背景逼近。</summary>
    Generating,
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

    /// <summary>
    /// ⭐ <b>挑戰模式的下一題在背景預生</b>（P5-2，issue #37，2026-10-23）。<b>出題器只有它碰</b>——主執行緒不再直接叫
    /// <c>generator.Generate</c>，所以「同一時間只有一條執行緒碰出題器」由 <see cref="TargetedPrefetch{T}"/> 一處保證。
    ///
    /// 什麼時候開始生（中文邏輯 2026-10-23 確認過）：① 主畫面每次出現時（<see cref="PrepareNextChallengeLevel"/>，第一次進關不用等）
    /// ② 挑戰模式每結算一局（<see cref="SettleIfNeeded"/>，玩家看結果彈窗的那一兩秒就生好了）。
    /// 按「下一題」時目標對得上就直接進關，還沒好就蓋遮罩等（<see cref="TryEnterWaitingChallengeLevel"/>）。
    /// 💡 取代了 10/4 規劃的「甲：預生 ±200 的小池」——目標在結算那一刻就確定了，不用猜。
    /// </summary>
    private TargetedPrefetch<TargetedGenerationResult> challengePrefetch;

    /// <summary>
    /// 按了「下一題」、題目還沒好：在等哪個目標。<b>沒在等是 null</b>。
    /// 跟 <see cref="IsScanPending"/> 同一套：<see cref="EnterLevel"/> 一律清掉，所以不管從哪條路進關都不會殘留。
    /// </summary>
    private int? waitingChallengeTarget;

    /// <summary>從按「下一題」開始量，進關時印出玩家實際等了幾秒（T20 上要看的數字）。</summary>
    private System.Diagnostics.Stopwatch waitingClock;

    /// <summary>
    /// 主畫面在實力讀進來之前就要求預生了（主畫面的 <c>Start</c> 可能比這支的 <c>Start</c> 早跑，兩支誰先是未定義的）。
    /// 記下來，<see cref="Start"/> 建好實力之後補做。
    /// </summary>
    private bool prepareChallengeAfterStart;

    /// <summary>
    /// ⭐ 第一次進關之後還要量幾幀（P5-2，2026-10-23）：出題搬到背景之後，第一次進關剩下的卡頓就是暖機
    /// （片的圖、shader、字型圖集），先量出來，數字大才處理。0 ＝ 不量了。
    /// </summary>
    private int warmupFramesToMeasure;

    /// <summary>第一次進關之後那幾幀裡最長的一幀（秒）。</summary>
    private float longestWarmupFrame;

    /// <summary>第一次進關時 <see cref="EnterLevel"/> 本身花了幾秒。</summary>
    private double firstEnterSeconds;

    /// <summary>這一場進過幾關（任何模式、任何來源）。只拿來判斷「第一次」。</summary>
    private int enteredLevelCount;

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

    /// <summary>
    /// ⭐ 輪廓 → 候選題目，<b>一個預設一個 builder</b>（2026-10-21）。跟兩個掃描一起建，順序照 <see cref="scanCandidatePresets"/>。
    /// </summary>
    private BeamSearchContourPuzzleBuilder[] candidateBuilders;

    /// <summary>
    /// ⭐ <b>掃描回來、候選正在背景逼近</b>（P5-2，2026-10-23）：一個預設一件工作、同時跑。全部做完時
    /// <see cref="TryFinishScanComputation"/> 照預設的順序合成候選。<b>沒在算時是 null</b>。
    /// </summary>
    private ScanComputation pendingComputation;

    /// <summary>一次掃描的候選計算：照片與輪廓等著跟結果一起交給選題畫面。</summary>
    private sealed class ScanComputation
    {
        /// <summary>掃描帶來的照片。算完沒用到（模式換了、一個候選都沒有）要釋放。</summary>
        public Texture2D Photo;

        /// <summary>辨識出來的輪廓。⚠️ 幾件工作同時讀它——它建好之後就不會再變（<c>RecognizedContour</c> 全是唯讀欄位），所以安全。</summary>
        public RecognizedContour Contour;

        /// <summary>一個預設一件，順序跟 <see cref="candidateBuilders"/> 一樣。</summary>
        public BackgroundJob<IReadOnlyList<ContourApproximation>>[] Jobs;

        /// <summary>從開始逼近量起，印「逼近合計」用（同時跑，所以合計是牆上的時間，不是各件相加）。</summary>
        public System.Diagnostics.Stopwatch Clock;
    }

    [Header("掃描題的候選（2026-10-21）")]
    [Tooltip("每個預設各跑一次逼近，合起來給選題畫面。順序就是候選的順序。⚠️ 每一個都是一次完整搜尋，手機上各約 2 ～ 4 秒。")]
    [SerializeField] private ScanCandidatePreset[] scanCandidatePresets =
    {
        ScanCandidatePreset.FifthVersion, ScanCandidatePreset.NoFeatures, ScanCandidatePreset.EdgeContactBonus32,
    };

    [Tooltip("每個預設取前幾名當候選（同一個形狀只留第一次出現的）。")]
    [Min(1)]
    [SerializeField] private int candidatesPerPreset = 2;

    [Tooltip("候選最多幾個（選題畫面的槽數）。")]
    [Min(1)]
    [SerializeField] private int maximumCandidates = 6;

    /// <summary>
    /// ⭐ <b>待選的掃描結果</b>（2026-10-21）：掃描回來、候選算好、玩家還沒選。
    /// 選題畫面讀它畫照片與縮圖；玩家選了（<see cref="PickScanCandidate"/>）、重拍（<see cref="RetryScan"/>）或回主畫面
    /// （<see cref="CancelScanPick"/>）就清掉。<b>沒有待選時是 null</b>。
    /// </summary>
    public ScanCandidateSet PendingScan { get; private set; }

    /// <summary>
    /// ⭐ <b>掃描模式要選照片來源了</b>（2026-10-17）。選擇視窗訂閱它，把自己顯示出來；
    /// 玩家選完叫回 <see cref="BeginScan"/> 或 <see cref="CancelScanSourceChoice"/>。
    ///
    /// 用事件的理由跟 <see cref="ScanAborted"/> 一樣：選擇視窗指向這支，這支再指回去就是環。
    /// ⚠️ <b>沒有人訂閱</b>（場景沒接選擇視窗）時會報錯並回主畫面，不會停在一個什麼都沒有的畫面上。
    /// </summary>
    public event System.Action ScanSourceRequested;

    /// <summary>
    /// ⭐ <b>掃描題的候選算好了、要玩家選</b>（2026-10-21）。選題畫面（<see cref="ScanPickPanel"/>）訂閱它，
    /// 從 <see cref="PendingScan"/> 拿照片與候選畫出來；玩家選完叫回 <see cref="PickScanCandidate"/>、
    /// <see cref="RetryScan"/> 或 <see cref="CancelScanPick"/>。
    ///
    /// 用事件的理由跟 <see cref="ScanSourceRequested"/> 一樣。⚠️ <b>沒有人訂閱</b>（場景沒接選題畫面）時<b>直接拿第 1 個候選進關</b>，
    /// 行為跟沒有選題畫面之前一樣，不報錯。
    /// </summary>
    public event System.Action ScanPickRequested;

    /// <summary>
    /// ⭐ <b>掃描沒有進關就結束了</b>（取消或失敗）。主畫面訂閱它，把自己顯示回來。
    ///
    /// <b>為什麼用事件，不直接叫主畫面</b>：主畫面已經指向這支（按鈕要叫 <see cref="StartMode"/>），
    /// 這支再指回去就是環。這裡也沒有順序問題要防（只有一個訂閱者、做的事只有顯示自己），
    /// 所以不違反類別說明裡「換關不發事件」那一條。
    ///
    /// ⭐ 2026-10-23 起帶著<b>為什麼</b>（<see cref="ScanAbortReason"/>），主畫面照它顯示一句話給玩家；取消的不顯示。
    /// </summary>
    public event System.Action<ScanAbortReason> ScanAborted;

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

    /// <summary>
    /// ⭐ <b>等待中</b>（P5-2，2026-10-23）：掃描等待中，或挑戰模式按了「下一題」、題目還在背景生。
    /// 等待遮罩看<b>這個</b>決定要不要蓋住盤面（原本只看 <see cref="IsScanPending"/>）。
    /// </summary>
    public bool IsWaitingForLevel => IsScanPending || waitingChallengeTarget.HasValue;

    /// <summary>⭐ 等待遮罩上要寫哪一個階段（P5-2，2026-10-23）。沒在等是 <see cref="LevelWaitingStage.None"/>。</summary>
    public LevelWaitingStage WaitingStage
    {
        get
        {
            if (waitingChallengeTarget.HasValue || pendingComputation != null) return LevelWaitingStage.Generating;
            if (!IsScanPending) return LevelWaitingStage.None;
            if (cameraScanner?.IsScanning == true || galleryScanner?.IsScanning == true) return LevelWaitingStage.Recognizing;
            return LevelWaitingStage.Waiting;
        }
    }

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

        // 主畫面比這支早跑 Start 的話，它要求的預生在這裡補做（見 prepareChallengeAfterStart）
        if (prepareChallengeAfterStart)
        {
            prepareChallengeAfterStart = false;
            PrepareNextChallengeLevel();
        }

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
    ///
    /// ⭐ 2026-10-23（P5-2）起同一格也照顧背景工作：預生換目標、等的那一題好了就進關、掃描的候選算完就跳選題畫面、量暖機。
    /// </summary>
    private void Update()
    {
        SettleIfNeeded();
        UpdateChallengePrefetch();
        TryEnterWaitingChallengeLevel();
        TryFinishScanComputation();
        MeasureWarmup();
    }

    /// <summary>預生換目標（舊的做完才換，見 <see cref="TargetedPrefetch{T}.Update"/>），被丟掉的那件丟過例外就印出來。</summary>
    private void UpdateChallengePrefetch()
    {
        if (challengePrefetch == null) return;
        challengePrefetch.Update();

        System.Exception discarded = challengePrefetch.TakeDiscardedError();
        if (discarded == null) return;
        Debug.LogError("預生的題目（後來目標換了、被丟掉的那一題）出題時出事了，例外的完整內容在下一行。" +
                       "沒有影響這一題，但多半代表出題器有 bug。", this);
        Debug.LogException(discarded, this);
    }

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

        // ⭐ 目標在這一刻確定了：馬上在背景生下一題，玩家看結果彈窗的時候就生好了（P5-2）。
        // 只在挑戰模式——具象題也會結算，但它的下一題是抽的；它改了實力的話，主畫面再出現時會照新目標重生。
        // ⚠️ 用 ?. 不用 EnsureChallengePrefetch：預生只在有人走過挑戰模式的入口（主畫面、自動進關）之後才建。
        // 回饋收集場景也是這個模式、每題也結算，但它用自己的題單進關——在那裡建的話每題都白白在背景生一題
        if (CurrentMode == GameMode.GeneratedByRating) challengePrefetch?.Want(rating.NextTargetDifficulty);
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
        // ⭐ 中途離開（2026-10-23）：按的就是暫停那一關的模式 → 接著玩，不換題。
        // 按了別的模式就照常進新的一關，暫停的那一關在 EnterLevel 被整包換掉——那一局作廢、不結算
        if (pausedMode == mode && CanResume)
        {
            ResumePausedLevel();
            return;
        }

        CurrentMode = mode;
        EnterNextLevel();
    }

    // ───────── 中途離開（2026-10-23，待辦清單〈中途離開〉的丙）─────────
    //
    // 不結算、不換題：離開＝暫停（沒代價，回來還是它），放棄＝認輸（有代價），兩件事分開。
    // ⚠️ 只在同一次執行裡成立：關掉 App 那一局就沒了（使用者 2026-10-23 定這次不存）——沒結算，所以也不扣分。

    /// <summary>
    /// 暫停中的那一關是哪個模式的。<b>沒有暫停中的關是 null。</b>
    /// 記模式而不是只記「有沒有暫停」：暫停之後去試了掃描、取消回來，<see cref="CurrentMode"/> 已經變成掃描了，
    /// 但盤面上還是那一關，按回原本的模式要接得上。
    /// </summary>
    private GameMode? pausedMode;

    /// <summary>
    /// ⭐ 有一關暫停中（主畫面蓋著它）。監控面板顯示「暫停中」，App 回到前景時也看它決定要不要重新計時。
    /// </summary>
    public bool IsLevelPaused => pausedMode.HasValue;

    /// <summary>暫停的那一關還在、還沒結束（理論上一定成立，這是保險：被換掉時 <see cref="EnterLevel"/> 已經清了旗標）。</summary>
    private bool CanResume => session?.Data != null && session.Data.Puzzle != null && !session.Data.IsFinished;

    /// <summary>
    /// ⭐ 主畫面要出現了：進行中的那一關暫停（2026-10-23）。<b>主畫面的 <c>Show</c> 叫這支</b>——
    /// 盤面上的「回主畫面」與結果彈窗的「回主畫面」走同一支，所以兩個出口不會分家。
    ///
    /// 做三件事：記下是哪個模式、取消進行中的互動（拖到一半的片放回去，拖曳計時跟著停）、兩段計時都停。
    /// <b>不卸載、不結算</b>：片原地留著，回來接著拼。
    /// 沒有關、已經結束（打完了，下次按模式本來就是下一題）、已經暫停中：什麼都不做。
    /// </summary>
    public void PauseCurrentLevel()
    {
        if (pausedMode.HasValue) return;
        LevelData level = session?.Data;
        if (level == null || level.Puzzle == null || level.IsFinished) return;

        // 先取消互動再停計時：取消會停拖曳計時，順序反過來也一樣，但這樣讀起來是「先放手、再暫停」
        if (dragController != null) dragController.CancelInteraction();
        level.StopAllTiming(GameClock.NowSeconds);
        pausedMode = CurrentMode;

        Debug.Log($"第 {levelIndex} 關暫停（{CurrentMode}），前景 {level.ForegroundSeconds:F1} 秒。" +
                  "按同一個模式接著玩，按別的模式這一局作廢、不結算。", this);
    }

    /// <summary>接著玩暫停的那一關：模式切回它、前景重新計時。</summary>
    private void ResumePausedLevel()
    {
        CurrentMode = pausedMode.Value;
        pausedMode = null;
        session.Data.StartForegroundTiming(GameClock.NowSeconds);
        Debug.Log($"第 {levelIndex} 關接著玩（{CurrentMode}），前景到目前 {session.Data.ForegroundSeconds:F1} 秒。", this);
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
            AbortScan(ScanAbortReason.Unexpected);
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
            AbortScan(ScanAbortReason.Unexpected);        // 權重讀不到的話 Awake 已經報過錯
            return;
        }

        // 上一次的候選還在背景算（P5-2）也算「還沒回來」：兩批結果會搶同一個待選
        if (cameraScanner?.IsScanning == true || galleryScanner.IsScanning || pendingComputation != null) return;

        IContourScanner scanner = source == ScanPhotoSource.Camera ? cameraScanner : galleryScanner;
        if (scanner == null)
        {
            Debug.LogError("LevelController 的「拍照畫面」欄位沒填：「新拍一張」用不了，回主畫面。", this);
            AbortScan(ScanAbortReason.PhotoFailed);
            return;
        }

        scanner.BeginScan(OnScanFinished);
    }

    /// <summary>選擇視窗按了「取消」（2026-10-17）：回主畫面。</summary>
    public void CancelScanSourceChoice()
    {
        Debug.Log("選擇視窗取消，回主畫面。", this);
        AbortScan(ScanAbortReason.Cancelled);
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
            AbortScan(ScanAbortReason.Cancelled);
            return;
        }

        if (result.Status == ContourScanStatus.Failed)
        {
            Debug.LogWarning($"掃描失敗，回主畫面：{result.FailureReason}", this);
            AbortScan(result.FailureStage == ContourScanFailureStage.Photo ? ScanAbortReason.PhotoFailed : ScanAbortReason.RecognitionFailed);
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
            AbortScan(ScanAbortReason.RecognitionFailed);
            return;
        }

        // ⭐ 2026-10-23（P5-2）起逼近在背景、一個預設一件同時跑，畫面不再凍住（以前三個預設輪流跑，Xperia 上約 10 秒）。
        // 全部做完時 Update 裡的 TryFinishScanComputation 接手，遮罩這段寫「出題中」
        var jobs = new BackgroundJob<IReadOnlyList<ContourApproximation>>[candidateBuilders.Length];
        for (int i = 0; i < candidateBuilders.Length; i++)
        {
            // ⚠️ 這個 lambda 在背景執行緒上跑：builder 只有設定、沒有狀態，而且一件一個；輪廓是唯讀的，幾件同時讀沒問題
            BeamSearchContourPuzzleBuilder builder = candidateBuilders[i];
            jobs[i] = new BackgroundJob<IReadOnlyList<ContourApproximation>>(() => builder.Approximate(contour));
        }
        pendingComputation = new ScanComputation
        {
            Photo = result.Photo, Contour = contour, Jobs = jobs, Clock = System.Diagnostics.Stopwatch.StartNew(),
        };
    }

    /// <summary>
    /// 掃描的候選全部算完了的話：合成候選、跳選題畫面（P5-2，2026-10-23；原本在 <see cref="OnScanFinished"/> 裡同步做）。
    /// <b>每幀問一次</b>，還有一件沒做完就什麼都不做。
    /// </summary>
    private void TryFinishScanComputation()
    {
        ScanComputation computation = pendingComputation;
        if (computation == null) return;
        foreach (BackgroundJob<IReadOnlyList<ContourApproximation>> job in computation.Jobs)
            if (!job.IsDone) return;

        pendingComputation = null;
        double totalSeconds = computation.Clock.Elapsed.TotalSeconds;

        // 算的期間模式換掉了（理論上遮罩擋住了換不了，這條是保險）：結果沒人要了，進關的話會蓋掉玩家剛選的那一題
        if (CurrentMode != GameMode.Scan)
        {
            DiscardPhoto(computation.Photo);
            return;
        }

        RecognizedContour contour = computation.Contour;
        List<ContourPuzzle> candidates = MergeScanCandidates(computation);
        if (candidates.Count == 0)
        {
            Debug.LogWarning($"這條輪廓（{contour.PixelPoints.Count} 個點）一個候選都逼不出來，回主畫面。", this);
            DiscardPhoto(computation.Photo);
            AbortScan(ScanAbortReason.PuzzleFailed);
            return;
        }

        Debug.Log($"掃描題候選 {candidates.Count} 個，逼近合計 {totalSeconds:0.00} 秒（{computation.Jobs.Length} 個預設同時跑），" +
                  $"輪廓 {contour.PixelPoints.Count} 個點（{contour.ImageWidth}×{contour.ImageHeight}）", this);

        PendingScan = new ScanCandidateSet(computation.Photo, contour, candidates);

        // ⭐ 不進關、先選題（2026-10-21 使用者定）。沒有選題畫面的場景直接拿第 1 個，行為跟之前一樣
        if (ScanPickRequested == null)
        {
            Debug.Log("場景裡沒有 ScanPickPanel 在聽（ScanPickRequested 沒人訂閱），直接拿第 1 個候選進關。", this);
            PickScanCandidate(0);
            return;
        }
        ScanPickRequested();
    }

    /// <summary>
    /// ⭐ 把每個預設的逼近結果合成候選（2026-10-21）：照 <see cref="scanCandidatePresets"/> 的順序，
    /// 每個預設取前 <see cref="candidatesPerPreset"/> 名，同一個形狀（<c>Puzzle.Key</c>）只留第一次出現的，最多 <see cref="maximumCandidates"/> 個。
    /// 某個預設丟例外就跳過它（log 完整印出來）——輸入是使用者的照片，不賭。
    ///
    /// ⭐ 2026-10-23（P5-2）起逼近改在背景同時跑，這裡只合成。<b>規則與順序跟同步那一版一模一樣，所以候選也一模一樣</b>——
    /// 唯一的差別是額滿之後的預設以前不跑、現在照樣跑完（同時跑，不多花牆上的時間）。
    /// </summary>
    /// <param name="computation">全部做完的那一次計算。</param>
    /// <returns>候選，可能是空的。</returns>
    private List<ContourPuzzle> MergeScanCandidates(ScanComputation computation)
    {
        var candidates = new List<ContourPuzzle>();
        var seenKeys = new HashSet<string>();
        for (int presetIndex = 0; presetIndex < candidateBuilders.Length && candidates.Count < maximumCandidates; presetIndex++)
        {
            BeamSearchContourPuzzleBuilder builder = candidateBuilders[presetIndex];
            computation.Jobs[presetIndex].TryGetOutcome(out IReadOnlyList<ContourApproximation> results,
                                                        out System.Exception error, out double seconds);
            if (error != null)
            {
                Debug.LogError($"候選預設 {scanCandidatePresets[presetIndex]} 逼近時出事了，跳過它。例外的完整內容在下一行。", this);
                Debug.LogException(error, this);
                continue;
            }

            int taken = 0;
            for (int rank = 0; rank < results.Count && taken < candidatesPerPreset && candidates.Count < maximumCandidates; rank++)
            {
                ContourApproximation approximation = results[rank];
                taken++;
                if (!seenKeys.Add(approximation.Puzzle.Key)) continue;
                ContourPuzzle candidate = builder.ToContourPuzzle(approximation);
                candidates.Add(candidate);
                Debug.Log($"候選 {candidates.Count}：{scanCandidatePresets[presetIndex]} 第 {rank + 1} 名，{candidate.Name}", this);
            }
            Debug.Log($"候選預設 {scanCandidatePresets[presetIndex]}：{seconds:0.00} 秒（背景量的，同時跑時會比單獨跑慢一點）", this);
        }
        return candidates;
    }

    /// <summary>
    /// ⭐ 玩家選了第幾個候選（2026-10-21，選題畫面叫）：算難度、進關、照片交給照片對照、清掉待選。
    /// 索引超出範圍就當第 1 個（寫警告）。沒有待選時什麼都不做（寫警告）。
    /// </summary>
    /// <param name="index">候選的索引，0 起算。</param>
    public void PickScanCandidate(int index)
    {
        ScanCandidateSet pending = PendingScan;
        if (pending == null)
        {
            Debug.LogWarning("選題畫面選了一題，但沒有待選的掃描結果（已經進關或中止了？），不做事。", this);
            return;
        }
        if (index < 0 || index >= pending.Candidates.Count)
        {
            Debug.LogWarning($"選題畫面選了第 {index} 個候選，但只有 {pending.Candidates.Count} 個，改拿第 1 個。", this);
            index = 0;
        }

        ContourPuzzle chosen = pending.Candidates[index];
        int difficulty;
        try
        {
            // 難度當場算，跟具象題同一套（見 ContourPuzzle 為什麼不帶難度）。
            // 掃描題不結算，算它只是為了監控面板與 log 看得到
            difficulty = DifficultyEvaluator.Score(chosen.Puzzle, chosen.ReferenceSolution, difficultyWeights);
        }
        catch (System.Exception exception)
        {
            // ⚠️ 一定要接住：主畫面已經藏起來了——例外沒接住的話，玩家會停在一個沒有主畫面也沒有題目的畫面上。
            // 已知會丟的是難度特徵（面積不是 32、外框有零寬度的刺時），理論上碰不到（候選跟出題器同一套），但輸入是使用者的照片，所以不賭
            Debug.LogError("算難度時出事了，回主畫面。例外的完整內容在下一行。", this);
            Debug.LogException(exception, this);
            DiscardPendingScan();
            AbortScan(ScanAbortReason.PuzzleFailed);
            return;
        }

        levelIndex++;
        Debug.Log($"第 {levelIndex} 關：候選 {index + 1}／{pending.Candidates.Count}，{chosen.Name}，難度 {difficulty}，不結算實力", this);

        // 先把待選拿下來再進關：進關之後這些東西歸這一關
        PendingScan = null;

        // allowRoll 傳 false：跟具象題同一個理由——照片裡是正的貓，題目也要是正的
        EnterLevel(chosen.Puzzle, chosen.ReferenceSolution, difficulty, allowRoll: false,
                   countsTowardRating: false);

        HandPhotoToView(pending.Photo, pending.Contour, chosen);
    }

    /// <summary>⭐ 選題畫面按了「重拍／重選」（2026-10-21）：丟掉這一輪，回到選擇視窗（遮罩一直蓋著）。</summary>
    public void RetryScan()
    {
        if (CurrentMode != GameMode.Scan)
        {
            Debug.LogWarning($"不在掃描模式（現在是 {CurrentMode}），不重拍。", this);
            DiscardPendingScan();
            return;
        }
        Debug.Log("選題畫面要重拍，回到選擇視窗。", this);
        DiscardPendingScan();
        RequestScanSource();
    }

    /// <summary>⭐ 選題畫面按了「回主畫面」（2026-10-21）：丟掉這一輪，回主畫面。</summary>
    public void CancelScanPick()
    {
        Debug.Log("選題畫面取消，回主畫面。", this);
        DiscardPendingScan();
        AbortScan(ScanAbortReason.Cancelled);
    }

    /// <summary>待選的掃描結果用不到了：釋放照片、清掉。</summary>
    private void DiscardPendingScan()
    {
        if (PendingScan == null) return;
        DiscardPhoto(PendingScan.Photo);
        PendingScan = null;
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

    /// <summary>掃描沒有進關就結束了：讓主畫面顯示回來。待選的結果（如果有）一起丟掉。</summary>
    /// <param name="reason">
    /// 為什麼沒進關（2026-10-23）。<b>這支只決定「為什麼」，給玩家看的那句話由主畫面決定</b>——字是 UI，住在場景裡、改字不用改程式。
    /// </param>
    private void AbortScan(ScanAbortReason reason)
    {
        DiscardPendingScan();

        // 還在背景算的候選（P5-2）：工作停不下來，只能不要它的結果——照片現在就釋放，算完時 TryFinishScanComputation 看到 null 就什麼都不做
        if (pendingComputation != null)
        {
            DiscardPhoto(pendingComputation.Photo);
            pendingComputation = null;
        }
        // 先收遮罩再叫主畫面回來：兩件事同一幀發生，順序其實看不出來，但「等待結束」在邏輯上先於「回主畫面」
        IsScanPending = false;
        ScanAborted?.Invoke(reason);
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

        // ⭐ 一個預設一個 builder（2026-10-21）。沒填預設的話至少有第五版，不然一個候選都生不出來
        if (scanCandidatePresets == null || scanCandidatePresets.Length == 0)
        {
            Debug.LogWarning("候選預設清單是空的，改用第五版一個。", this);
            scanCandidatePresets = new[] { ScanCandidatePreset.FifthVersion };
        }
        candidateBuilders = new BeamSearchContourPuzzleBuilder[scanCandidatePresets.Length];
        for (int i = 0; i < scanCandidatePresets.Length; i++)
            candidateBuilders[i] = new BeamSearchContourPuzzleBuilder(ScanCandidatePresets.SettingsOf(scanCandidatePresets[i]));
        return true;
    }

    /// <summary>
    /// 進下一關，<b>指定目標難度</b>（P2-7）。
    ///
    /// 遊玩迴圈從無參數那支進來（目標是玩家的實力）；直接叫這支是給工具與除錯用：指定一個難度看出題器生什麼。
    ///
    /// ⭐ <b>2026-10-23（P5-2）起不是當場進關</b>：題目在背景生（多半已經預生好了，見 <see cref="challengePrefetch"/>）。
    /// 好了的話這一格就進關；還沒好就蓋上等待遮罩（<see cref="IsWaitingForLevel"/>），好了 <see cref="Update"/> 會接著進關。
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
        TargetedPrefetch<TargetedGenerationResult> prefetch = EnsureChallengePrefetch();
        if (prefetch == null) return;

        // ⭐ 不在這裡生（P5-2）：出題器只有背景碰。好了的話這一格就進關，還沒好就蓋遮罩等，Update 每幀再問。
        // 已經在等的時候又按一次（遮罩擋住了，理論上按不到）：換成新的目標，計時不重來
        waitingChallengeTarget = targetDifficulty;
        if (waitingClock == null || !waitingClock.IsRunning) waitingClock = System.Diagnostics.Stopwatch.StartNew();
        prefetch.Want(targetDifficulty);
        TryEnterWaitingChallengeLevel();
    }

    /// <summary>
    /// ⭐ 先在背景把挑戰模式的下一題準備好（P5-2，2026-10-23）。<b>主畫面每次出現時叫</b>——第一次進關（主要卡的那一次）
    /// 就不用等；中間去玩了具象題、實力變了的話，這裡照新目標重生。
    /// 實力還沒讀進來（主畫面的 <c>Start</c> 比這支早）就記下來，<see cref="Start"/> 補做。
    /// </summary>
    public void PrepareNextChallengeLevel()
    {
        SkillRatingTracker rating = session?.Rating;
        if (rating == null)
        {
            // 設定讀不到的話 Start 補做時還是 null，什麼都不會發生——Awake 已經報過錯
            prepareChallengeAfterStart = true;
            return;
        }

        EnsureChallengePrefetch()?.Want(rating.NextTargetDifficulty);
    }

    /// <summary>需要的話把出題器與預生建起來。權重讀不到時回 null（Awake 已經報過錯）。</summary>
    private TargetedPrefetch<TargetedGenerationResult> EnsureChallengePrefetch()
    {
        if (difficultyWeights == null) return null;
        if (challengePrefetch != null) return challengePrefetch;

        generator = new TargetedPuzzleGenerator(new PuzzleGenerator(generatorSeed, AestheticLimits.Default), difficultyWeights);

        // ⚠️ 這個 lambda 在背景執行緒上跑。它只碰出題器，而 TargetedPrefetch 保證同一時間只有一件——
        // 主執行緒不能再在任何地方直接叫 generator.Generate，不然那條保證就破了
        challengePrefetch = new TargetedPrefetch<TargetedGenerationResult>(target => generator.Generate(target));
        return challengePrefetch;
    }

    /// <summary>
    /// 在等的那一題好了就進關（P5-2）。<see cref="EnterNextLevel(int)"/> 當下問一次，之後 <see cref="Update"/> 每幀問。
    /// </summary>
    private void TryEnterWaitingChallengeLevel()
    {
        if (!waitingChallengeTarget.HasValue || challengePrefetch == null) return;
        if (!challengePrefetch.TryTake(waitingChallengeTarget.Value, out TargetedGenerationResult result,
                                       out System.Exception error, out double workSeconds)) return;

        double waitedSeconds = waitingClock.Elapsed.TotalSeconds;
        waitingClock.Stop();
        waitingChallengeTarget = null;
        levelIndex++;

        if (error != null)
        {
            // 以前出題在主執行緒，丟例外就整個往上丟；現在拿得到，就照「失敗」那條退回大正方形——玩家已經按了下一題，不能停在空畫面上
            Debug.LogError($"第 {levelIndex} 關：出題時出事了，退回經典大正方形。例外的完整內容在下一行。", this);
            Debug.LogException(error, this);
            EnterClassicSquare(generation: null);
            return;
        }

        EnterGeneratedLevel(result, workSeconds, waitedSeconds);
    }

    /// <summary>
    /// 用出題器生好的那一題進關。<b>三種下場都要處理</b>（見 <see cref="EnterNextLevel(int)"/>）。
    /// </summary>
    /// <param name="result">出題器的結果。</param>
    /// <param name="workSeconds">出題本身花了幾秒（背景量的）。</param>
    /// <param name="waitedSeconds">玩家按了下一題之後實際等了幾秒；預生好了的話接近 0。</param>
    private void EnterGeneratedLevel(TargetedGenerationResult result, double workSeconds, double waitedSeconds)
    {
        string timing = $"出題 {workSeconds:0.00} 秒、玩家等了 {waitedSeconds:0.00} 秒";

        if (result.Outcome == TargetedGenerationOutcome.Failed)
        {
            Debug.LogWarning(
                $"第 {levelIndex} 關：目標 {result.TargetDifficulty} 叫了 {result.AttemptCount} 次" +
                $"都沒生出題目，退回經典大正方形。審美門檻可能收太緊了。（{timing}）", this);

            // ⚠️ 出題紀錄照樣帶進去（P2-15），下場是失敗——不標出來的話，
            // 走勢上那一跳會看起來像 ELO 壞了
            EnterClassicSquare(GenerationRecord.From(result));
            return;
        }

        // ⚠️ 「將就」在畫面上看不出來——玩家拿到一題難度不對的題目，而一切看起來都正常。
        // 所以這裡一定要講，而且要把兩個數字都講出來
        string hit = result.Outcome == TargetedGenerationOutcome.OnTarget
            ? "正中"
            : $"⚠️ 將就（差 {Mathf.Abs(result.Difficulty - result.TargetDifficulty)}）";
        Debug.Log($"第 {levelIndex} 關：目標 {result.TargetDifficulty} → 實際 {result.Difficulty}　" +
                  $"{hit}，叫了 {result.AttemptCount} 次（{timing}）", this);

        // ⚠️ allowRoll 傳 false：生成題要看的是「存進去的那個角度好不好看」，
        // 讓相機轉會看不到真實情況。P2-8 接上題庫時這條要重新決定。
        // ⭐ 難度直接用出題器算好的那個，不重算——同一題只算一次，見 LevelData.Difficulty
        EnterLevel(result.Puzzle, result.ReferenceSolution, result.Difficulty, allowRoll: false,
                   generation: GenerationRecord.From(result));
    }

    /// <summary>
    /// 退路：經典大正方形。出題器失敗，或出題時丟了例外（P5-2 起拿得到了）才走這裡。
    /// </summary>
    /// <param name="generation">出題紀錄；丟例外的話沒有紀錄，傳 null。</param>
    private void EnterClassicSquare(GenerationRecord? generation)
    {
        // 退路那一題沒有經過出題器，難度要自己算。
        // 💡 大正方形是 2261 分——比生成池最高分還高，那不是算錯，見 DifficultyEvaluator 的說明
        Placement[] fallback = Fixtures.ClassicSquare();
        Puzzle fallbackPuzzle = Puzzle.FromPlacements(fallback);
        EnterLevel(fallbackPuzzle, fallback,
                   DifficultyEvaluator.Score(fallbackPuzzle, fallback, difficultyWeights),
                   allowRoll: true, generation: generation);
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
        // 0. 掃描的等待到此為止（2026-10-17）。不判模式：掃描等到一半換成別的模式進關，遮罩也該收掉。
        //    挑戰模式的等待同一個道理（P5-2）：等到一半從別的路進關的話，那一題好了也不該再進一次
        IsScanPending = false;
        waitingChallengeTarget = null;
        pausedMode = null;      // 中途離開（2026-10-23）：暫停的那一關被換掉了，那一局作廢
        var enterClock = System.Diagnostics.Stopwatch.StartNew();

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

        // 7. 第一次進關：開始量暖機（P5-2）。出題已經不在這段了，剩下的卡頓就是片的圖、shader、字型圖集
        enteredLevelCount++;
        if (enteredLevelCount == 1)
        {
            firstEnterSeconds = enterClock.Elapsed.TotalSeconds;
            warmupFramesToMeasure = WarmupFrameCount;
            longestWarmupFrame = 0f;
        }
    }

    /// <summary>第一次進關之後量幾幀。第一幀畫出來時 shader 與字型圖集才真的準備，所以要往後看幾幀。</summary>
    private const int WarmupFrameCount = 5;

    /// <summary>
    /// ⭐ 量第一次進關的暖機（P5-2，2026-10-23）：進關之後幾幀裡最長的一幀，加上進關本身花的時間，印一行。
    /// 在 T20 上看這一行決定暖機要不要另外處理（10/4 規劃的 B②）。
    /// 💡 用 <c>unscaledDeltaTime</c>：進關那一幀的工作會算進「下一幀拿到的間隔」裡，所以從下一幀開始量剛好涵蓋到。
    /// </summary>
    private void MeasureWarmup()
    {
        if (warmupFramesToMeasure <= 0) return;

        // 進關如果發生在這一格的 Update 裡，這一格的間隔（進關之前的）也會被算進來——無害，取的是最大值，所以多量幾幀就好
        longestWarmupFrame = Mathf.Max(longestWarmupFrame, Time.unscaledDeltaTime);
        warmupFramesToMeasure--;
        if (warmupFramesToMeasure > 0) return;

        Debug.Log($"第一次進關的暖機：進關本身 {firstEnterSeconds:0.000} 秒，之後 {WarmupFrameCount} 幀裡最長的一幀 " +
                  $"{longestWarmupFrame:0.000} 秒（出題不在這裡面，在背景）", this);
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

        // ⚠️ 暫停中（主畫面蓋著）不重新計時（2026-10-23）：不擋的話「回主畫面、切到別的 App、再切回來」，
        // 前景就偷偷在主畫面上走，回來接著玩時被時間代價扣
        if (inForeground && IsLevelPaused) return;

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
