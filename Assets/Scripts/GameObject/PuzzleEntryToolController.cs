using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using Tangram.Core;
using Tangram.Data;
using TMPro;
using UnityEngine;

/// <summary>
/// <b>出題工具的控制器。這一支只給工具場景用，正式版的 build 不會包含它所在的場景。</b>
///
/// 用途：把古典七巧板圖形（船、貓、房子那些）用七片拼出來，存成題目。
/// 拼的過程跟玩遊戲完全一樣——拖曳、吸附、旋轉、翻面、undo、清空全部沿用現成的，
/// 所以這支<b>不重寫任何互動</b>，它只做三件遊戲裡沒有的事：
/// <list type="number">
///   <item>把盒子換成一個空的大方框（邊長 16），讓七片有地方自由拼</item>
///   <item>隨時能問「現在盤上這些片合不合法」，四條檢查各顯示一行</item>
///   <item>合法的時候，連同名字存成一筆</item>
///   <item>把之前存過的題<b>重現到盤面上改</b>，存檔時覆蓋原本那一筆</item>
/// </list>
///
/// <b>為什麼是「大方框」而不是「沒有盒子」</b>：吸附的錨點來自「已放置的片 ＋ 盒子輪廓」，
/// 而且合法性靠 <c>isAllowed</c>（必須在盒內）撐著。真的把盒子拿掉的話，第一片沒有任何錨點
/// 可以吸，整套吸附要另外寫一條路。給一個很大的框，這兩件事都自動成立，**一行都不用改**。
/// 代價只有「第一片要先貼著框邊放」，而那無所謂——存進去的是相對編碼，絕對位置不入庫。
///
/// ⚠️ <b>這裡的盤面沒有題目也沒有參考解</b>（見 <see cref="LevelSession.LoadBoxOnly"/>）。
/// 所以任何吃參考解的功能在這個場景都不能用：提示、盒子填色、
/// <c>TouchInputController.FillFromReferenceSolutionForDebug</c>。盒子只畫線框。
///
/// ⚠️ <b>場景上不要放 <c>LevelController</c></b>——兩支都會在 <c>Awake</c> 設定
/// <c>LevelSession.Current</c> 與 <c>SharedUiState.Current</c>，誰後跑誰贏，
/// 而 <c>MonoBehaviour</c> 之間的順序是未定義的。症狀會是「有時候盒子是大方框、
/// 有時候是大正方形」，而且不報錯。同理，<c>LevelResultPanels</c> 與 <c>DebugTools</c>
/// 也請從工具場景移除（前者依賴 <c>LevelController</c>，後者會去叫生成器）。
/// </summary>
[DisallowMultipleComponent]
public class PuzzleEntryToolController : MonoBehaviour
{
    // ───────── 沿用遊戲的元件。找不到的行為跟 LevelController 一致 ─────────

    [Tooltip("負責相機與座標換算。")]
    [SerializeField] private BoardView boardView;

    [Tooltip("負責畫作業區的線框。")]
    [SerializeField] private BoxView boxView;

    [Tooltip("拖曳控制。開場要叫它取消進行中的互動。")]
    [SerializeField] private TouchInputController dragController;

    [Tooltip("待用區抽屜。開場要叫它重設七片的姿態。")]
    [SerializeField] private TrayView trayView;

    // ───────── 工具自己的 UI ─────────

    [Tooltip("題目的名字，例如「船」。")]
    [SerializeField] private TMP_InputField nameInputField;

    [Tooltip("四條檢查的第一行：無重疊。")]
    [SerializeField] private TMP_Text noOverlapText;

    [Tooltip("四條檢查的第二行：連通。")]
    [SerializeField] private TMP_Text connectedText;

    [Tooltip("四條檢查的第三行：面積。")]
    [SerializeField] private TMP_Text areaText;

    [Tooltip("四條檢查的第四行：編碼往返。")]
    [SerializeField] private TMP_Text roundTripText;

    [Tooltip("存檔結果、錯誤訊息顯示在這裡。選用，沒有的話只會寫進 Console。")]
    [SerializeField] private TMP_Text statusText;

    // ───────── 參數 ─────────

    [Tooltip("作業區的邊長（模型單位）。經典大正方形是 4√2 ≈ 5.66，16 約是它的三倍寬。"
             + "改大可以拼更細長的圖形，代價是片在螢幕上變小、吸附相對變鬆。")]
    [SerializeField] private int workAreaSideLength = 16;

    [Tooltip("檢查通過的顏色。")]
    [SerializeField] private Color passColor = new Color(0.2f, 0.8f, 0.3f, 1f);

    [Tooltip("檢查沒過的顏色。")]
    [SerializeField] private Color failColor = new Color(0.9f, 0.2f, 0.2f, 1f);

    [Tooltip("輸出檔名。檔案會放在 exe 旁邊（在 Editor 裡是專案資料夾）。")]
    [SerializeField] private string outputFileName = "puzzle_entries.jsonl";

    /// <summary>四行檢查的文字格式。要改版面在這裡改一次就好。</summary>
    private const string CheckLineFormat = "{0}：{1}";

    /// <summary>通過與沒過的記號。</summary>
    private const string PassMark = "O";
    private const string FailMark = "X";

    /// <summary>這一場。跟遊戲一樣，別人請走 <c>LevelSession.Current</c>。</summary>
    private LevelSession session;

    /// <summary>
    /// 已經存過的題，<b>順序就是檔案裡的順序</b>（「上一題／下一題」照這個走）。
    /// 開場從檔案讀回來，存檔時同步更新——檔案與這份清單永遠一致。
    /// </summary>
    private readonly List<SavedEntry> savedEntries = new List<SavedEntry>();

    /// <summary>
    /// 形狀的 key → 它在 <see cref="savedEntries"/> 的位置。擋重複用。
    ///
    /// <b>用 <c>PuzzleKey</c> 當識別而不是名字</b>：key 是形狀的指紋，
    /// 而且 <see cref="Canonical"/> 已經把旋轉與鏡射歸成同一個值，
    /// 所以「同一隻貓轉 90 度再存一次」會被抓出來。名字重複則不擋（兩隻不同的貓都叫貓沒關係）。
    /// </summary>
    private readonly Dictionary<string, int> entryIndexByPuzzleKey = new Dictionary<string, int>();

    /// <summary>
    /// 現在正在編輯哪一筆存檔，<b>−1 代表「這是新的一題」</b>。
    ///
    /// 載入舊題時設成那一筆的位置，存檔時就<b>覆蓋它</b>而不是追加；
    /// 按「新的一題」或存檔成功之後回到 −1。
    /// 有這個欄位，「載入 → 挪一片 → 存」才會是修改，不會變成多一筆幾乎一樣的題目。
    /// </summary>
    private int editingEntryIndex = -1;

    /// <summary>輸出檔的完整路徑，開場算一次。</summary>
    private string outputFilePath;

    /// <summary>一筆存檔。<b>一行一筆</b>（JSON Lines），這樣「每存一題就追加寫檔」才成立。</summary>
    [Serializable]
    private class SavedEntry
    {
        /// <summary>人取的名字，例如「船」。</summary>
        public string name;

        /// <summary>相對編碼（鐵則三：不存座標）。裡面已經有 key 與 puzzleKey。</summary>
        public ArrangementDto arrangement;
    }

    private void Awake()
    {
        session = new LevelSession();
        LevelSession.Current = session;

        // 跟 LevelController 一樣一定要在這裡指定，不能用惰性單例——
        // 關掉 Domain Reload 的話 static 欄位會跨播放存活，會把上一次播放的狀態帶進來
        SharedUiState.Current = new SharedUiState();

        if (boardView == null) boardView = FindFirstObjectByType<BoardView>();
        if (boxView == null) boxView = FindFirstObjectByType<BoxView>();
        if (dragController == null) dragController = FindFirstObjectByType<TouchInputController>();
        if (trayView == null) trayView = FindFirstObjectByType<TrayView>();

        // 缺哪個就報哪個。少了它們的症狀都是「安靜地不動作」，那是最難查的一種
        if (boardView == null)
            Debug.LogError("出題工具找不到 BoardView：相機不會 fit。", this);
        if (boxView == null)
            Debug.LogError("出題工具找不到 BoxView：作業區的框不會被畫出來。", this);
        if (dragController == null)
            Debug.LogError("出題工具找不到 TouchInputController：片拖不動。", this);
        if (trayView == null)
            Debug.LogError("出題工具找不到 TrayView：七片拿不出來。", this);

        outputFilePath = Path.Combine(ResolveOutputDirectory(), outputFileName);
    }

    private void Start()
    {
        EnterWorkArea();
        LoadExistingEntries();
        RefreshChecks();
    }

    /// <summary>
    /// 進入作業區。<b>這幾步的順序跟 <c>LevelController.EnterLevel</c> 一樣，不能動</b>
    /// （互動設計 §14.3）：抽屜的版面要用相機算出來的「一個模型單位佔幾像素」，
    /// 所以設盒子一定要排在重設七片姿態之前。
    /// </summary>
    private void EnterWorkArea()
    {
        Region workArea = BuildWorkArea(workAreaSideLength);

        // 1. 載入：只有一個空的框，沒有題目、沒有參考解
        session.LoadBoxOnly(workArea);

        // 2. 設盒子 → 相機這時候才算得出來
        if (boardView != null)
        {
            boardView.SetBox(workArea.Bounds, allowRoll: false);
            if (!boardView.Solution.IsValid)
                Debug.LogWarning("作業區設好了但版面解無效，相機不會 fit。"
                                 + "多半是這一幀還拿不到相機或螢幕尺寸，下一幀會自己補上。", this);
        }

        // 3. 畫框。⚠️ 參考解傳 null：工具沒有解可以拿來三角化，所以只會畫出輪廓、內部留空。
        //    副作用是那幾根「越界畫進材料裡」的描邊刺沒有填色蓋住（除錯記錄 010），工具上無所謂
        if (boxView != null) boxView.Redraw(workArea, null);

        // 4. 七片重置姿態，全部在抽屜裡
        if (trayView != null) trayView.ResetPiecePoses();

        // 5. 取消進行中的互動，順便把選取與鎖定的觸控歸零
        if (dragController != null) dragController.CancelInteraction();
    }

    /// <summary>
    /// 作業區：一個以原點為左下角、逆時針的正方形。
    ///
    /// ⚠️ <b>邊長只能是整數</b>（走 <see cref="ExactVector2"/> 的整數建構子）。
    /// 邊必須落在 45° 網格上，不然 <c>Region.BoundaryNodes</c> 會丟例外——軸對齊的正方形一定合格。
    /// </summary>
    /// <param name="sideLength">邊長，模型單位。</param>
    /// <returns>作業區。</returns>
    private static Region BuildWorkArea(int sideLength)
    {
        long side = sideLength;
        var ring = new[]
        {
            new ExactVector2(0, 0),
            new ExactVector2(side, 0),
            new ExactVector2(side, side),
            new ExactVector2(0, side)
        };
        return Region.FromPolygon(ring);
    }

    // ─────────────────────────────────────────────────────────────
    // 四條檢查
    // ─────────────────────────────────────────────────────────────

    /// <summary>一條檢查的簽章，四條長得都一樣，所以可以用同一支跑。</summary>
    /// <param name="pieces">盤上的片。</param>
    /// <param name="report">失敗會被加進這份報告。</param>
    private delegate void SingleCheck(IReadOnlyList<Placement> pieces, Validation.Report report);

    /// <summary>
    /// 重算四條檢查並更新畫面。<b>接在「檢查」按鈕上，存檔前也會自己叫一次。</b>
    ///
    /// ⚠️ <b>刻意不訂閱 <c>BoardState.Changed</c></b>：手指還在動的時候片每吸一次就會發事件，
    /// 而這四條裡有兩條要追一次輪廓（面積、往返），每格都算會鈍。按鈕按下去才算就夠了。
    ///
    /// ⚠️ 四條<b>各自跑一份報告</b>，不是跑 <c>Validation.RunAll</c> 再去比對訊息字串。
    /// 那樣的話訊息一改，這裡就默默對不上了。
    /// </summary>
    public void RefreshChecks()
    {
        Placement[] pieces = OnBoardPieces();

        SetCheckRow(noOverlapText, "無重疊", RunSingleCheck(Validation.CheckNoOverlap, pieces));
        SetCheckRow(connectedText, "連通", RunSingleCheck(Validation.CheckConnected, pieces));
        SetCheckRow(areaText, "面積", RunSingleCheck(Validation.CheckArea, pieces));
        SetCheckRow(roundTripText, "往返", RunSingleCheck(Validation.CheckRoundTrip, pieces));
    }

    /// <returns>盤上的片。七片沒放滿時只會有放上去的那幾片，沒有片時是空陣列。</returns>
    private static Placement[] OnBoardPieces()
    {
        LevelData level = LevelSession.Current?.Data;
        return level == null ? Array.Empty<Placement>() : level.Board.OnBoardPlacements();
    }

    /// <summary>
    /// 跑一條檢查。
    ///
    /// ⚠️ <b>例外一律當成沒過</b>。拼到一半的盤面（沒放滿、重疊、分成兩坨）並不是
    /// <c>BoundaryTracer</c> 與編碼那幾支的前提，它們可以丟例外；而工具的整個用途
    /// 就是停在那些中間狀態上。讓例外冒出去的話，症狀是「拼一拼整個畫面就不動了」。
    /// </summary>
    /// <param name="check">要跑的那一條。</param>
    /// <param name="pieces">盤上的片。空的時候直接算沒過——沒有片就沒有題目。</param>
    /// <returns>過了沒。</returns>
    private static bool RunSingleCheck(SingleCheck check, IReadOnlyList<Placement> pieces)
    {
        if (pieces == null || pieces.Count == 0) return false;

        var report = new Validation.Report();
        try
        {
            check(pieces, report);
        }
        catch (Exception error)
        {
            report.Fail(error.Message);
        }
        return report.Ok;
    }

    /// <summary>更新一行檢查的文字與顏色。</summary>
    /// <param name="text">那一行的 TMP。沒接的話就跳過。</param>
    /// <param name="label">行首的名稱，例如「無重疊」。</param>
    /// <param name="passed">過了沒。</param>
    private void SetCheckRow(TMP_Text text, string label, bool passed)
    {
        if (text == null) return;
        text.text = string.Format(CheckLineFormat, label, passed ? PassMark : FailMark);
        text.color = passed ? passColor : failColor;
    }

    // ─────────────────────────────────────────────────────────────
    // 存成一題
    // ─────────────────────────────────────────────────────────────

    /// <summary>
    /// 把目前盤上的七片存成一題。<b>接在「存成一題」按鈕上。</b>
    ///
    /// 擋下來的情況有四種，每一種都會在 <see cref="statusText"/> 說清楚原因：
    /// 沒放滿七片、四條檢查沒全過、沒打名字、這個形狀存過了。
    ///
    /// 💡 <b>存完不會自動清空盤面</b>——要接下一題請按現有的「清空」鈕。
    /// 這支不自己清是因為清空同時要動模型與畫面兩邊（<c>BoardActionButtons</c> 已經做好那件事），
    /// 在這裡再寫一份就會變成「兩個地方各自認定真相」。
    /// </summary>
    public void SaveCurrentPuzzle()
    {
        Placement[] pieces = OnBoardPieces();
        if (pieces.Length != BoardState.PieceCount)
        {
            ReportStatus($"還沒放滿七片（現在 {pieces.Length} 片）。", isError: true);
            return;
        }

        RefreshChecks();
        if (!RunSingleCheck(Validation.CheckNoOverlap, pieces)
            || !RunSingleCheck(Validation.CheckConnected, pieces)
            || !RunSingleCheck(Validation.CheckArea, pieces)
            || !RunSingleCheck(Validation.CheckRoundTrip, pieces))
        {
            ReportStatus("有檢查沒過，看上面四行。", isError: true);
            return;
        }

        string name = nameInputField == null ? string.Empty : nameInputField.text.Trim();
        if (string.IsNullOrEmpty(name))
        {
            ReportStatus("請先打名字。", isError: true);
            return;
        }

        // WithKeys 會算 Key 與 PuzzleKey。⚠️ PuzzleKey 要跑 16 次輪廓追蹤，不便宜——
        // 所以只在真的要存的時候算，檢查那條路上不碰它
        Arrangement arrangement;
        try
        {
            arrangement = Arrangement.FromPlacements(pieces).WithKeys();
        }
        catch (Exception error)
        {
            ReportStatus("編碼失敗：" + error.Message, isError: true);
            return;
        }

        // ⚠️ 正在編輯的那一筆不算重複——不然「載入舊題 → 只改名字 → 存」會被自己擋下來
        if (entryIndexByPuzzleKey.TryGetValue(arrangement.PuzzleKey, out int existingIndex)
            && existingIndex != editingEntryIndex)
        {
            ReportStatus($"這個形狀存過了，叫「{savedEntries[existingIndex].name}」。"
                         + "（旋轉或鏡射過的同一個形狀也算存過）", isError: true);
            return;
        }

        var entry = new SavedEntry { name = name, arrangement = Serialization.ToDto(arrangement) };
        bool isEditing = editingEntryIndex >= 0;

        if (isEditing)
        {
            // 覆蓋：形狀可能被改過，所以舊的 key 要先從索引拿掉，不然它會一直擋著
            savedEntries[editingEntryIndex] = entry;
            RebuildIndex();
            if (!WriteAllEntries()) return;
        }
        else
        {
            if (!AppendEntry(entry)) return;
            savedEntries.Add(entry);
            entryIndexByPuzzleKey[arrangement.PuzzleKey] = savedEntries.Count - 1;
        }

        string action = isEditing ? $"已改好第 {editingEntryIndex + 1} 題" : "已存";
        ReportStatus($"{action}「{name}」，目前共 {savedEntries.Count} 題。");

        // 存完就脫離編輯狀態：接下來拼的是新的一題
        editingEntryIndex = -1;
        if (nameInputField != null) nameInputField.text = string.Empty;
    }

    // ─────────────────────────────────────────────────────────────
    // 重現與編輯之前的題目
    // ─────────────────────────────────────────────────────────────

    /// <summary>載入上一題。<b>接在「上一題」按鈕上。</b></summary>
    public void LoadPreviousEntry() => LoadEntryAt(editingEntryIndex < 0
        ? savedEntries.Count - 1              // 還沒在編輯任何一筆：從最後一題開始往回翻
        : editingEntryIndex - 1);

    /// <summary>載入下一題。<b>接在「下一題」按鈕上。</b></summary>
    public void LoadNextEntry() => LoadEntryAt(editingEntryIndex + 1);

    /// <summary>
    /// 離開編輯狀態，接下來存的算新的一題。<b>接在「新的一題」按鈕上。</b>
    /// 盤面不會動——要清掉請按現有的「清空」鈕。
    /// </summary>
    public void StopEditing()
    {
        editingEntryIndex = -1;
        if (nameInputField != null) nameInputField.text = string.Empty;
        ReportStatus("接下來存的算新的一題。");
    }

    /// <summary>
    /// 把第幾筆存檔重現到盤面上，並進入編輯那一筆的狀態。
    ///
    /// 流程：解碼成七片 → 平移進作業區 → 依片種對回槽 → 蓋掉盤面 → 重畫畫面。
    ///
    /// ⚠️ <b>解碼出來的座標不能直接用。</b><c>Arrangement.Decode</c> 固定把第 0 片放在原點，
    /// 所以整個圖形有一半以上會落在作業區外面（座標是負的）。**平移是必要的一步**，
    /// 少了它會看到「片放上去了但畫面上找不到」，而且不報錯。
    /// </summary>
    /// <param name="entryIndex">要載入第幾筆（0 起算）。超出範圍就什麼都不做。</param>
    public void LoadEntryAt(int entryIndex)
    {
        if (savedEntries.Count == 0)
        {
            ReportStatus("還沒有存過任何一題。", isError: true);
            return;
        }
        if (entryIndex < 0 || entryIndex >= savedEntries.Count)
        {
            ReportStatus($"沒有第 {entryIndex + 1} 題（目前共 {savedEntries.Count} 題）。", isError: true);
            return;
        }

        LevelData level = LevelSession.Current?.Data;
        if (level == null) return;

        SavedEntry entry = savedEntries[entryIndex];
        Placement[] decoded;
        try
        {
            decoded = Serialization.FromDto(entry.arrangement).Decode();
        }
        catch (Exception error)
        {
            ReportStatus($"第 {entryIndex + 1} 題解碼失敗：{error.Message}", isError: true);
            return;
        }

        if (!TryMoveIntoWorkArea(decoded, out Placement[] moved))
        {
            ReportStatus($"「{entry.name}」放不進作業區，把邊長調大一點再試。", isError: true);
            return;
        }

        if (!TryOrderBySlot(moved, out Placement[] bySlot))
        {
            ReportStatus($"「{entry.name}」的片種湊不齊七片，檔案可能壞了。", isError: true);
            return;
        }

        level.TryFillFromPlacements(bySlot, out _);
        if (dragController != null) dragController.RefreshAllPiecesFromBoard();

        editingEntryIndex = entryIndex;
        if (nameInputField != null) nameInputField.text = entry.name;
        RefreshChecks();
        ReportStatus($"載入第 {entryIndex + 1} / {savedEntries.Count} 題「{entry.name}」，存檔會覆蓋它。");
    }

    /// <summary>
    /// 把整組片平移到作業區裡面。
    ///
    /// <b>平移量一律是整數</b>：整數向量的平移不會離開 ℤ[√2]，所以平移後的座標仍然精確
    /// （鐵則一）。取「置中之後往下取整」就夠了，反正絕對位置不入庫。
    /// </summary>
    /// <param name="pieces">解碼出來的七片。</param>
    /// <param name="moved">平移後的七片。放不下時是 null。</param>
    /// <returns>放得下沒。</returns>
    private bool TryMoveIntoWorkArea(IReadOnlyList<Placement> pieces, out Placement[] moved)
    {
        moved = null;

        var allVertices = new List<ExactVector2>();
        foreach (Placement piece in pieces) allVertices.AddRange(piece.Vertices());
        BoundingBox bounds = BoundingBox.FromPoints(allVertices);

        double width = (bounds.Maximum.X - bounds.Minimum.X).ToDouble();
        double height = (bounds.Maximum.Y - bounds.Minimum.Y).ToDouble();
        if (width > workAreaSideLength || height > workAreaSideLength) return false;

        // 左下角先移到原點，再往中間推。Floor 是為了留在框內——往上取整可能剛好戳出邊界
        long offsetX = (long)Math.Floor((workAreaSideLength - width) / 2.0);
        long offsetY = (long)Math.Floor((workAreaSideLength - height) / 2.0);
        ExactVector2 translation = new ExactVector2(offsetX, offsetY) - bounds.Minimum;

        moved = new Placement[pieces.Count];
        for (int pieceIndex = 0; pieceIndex < pieces.Count; pieceIndex++)
            moved[pieceIndex] = pieces[pieceIndex].Translated(translation);
        return true;
    }

    /// <summary>
    /// 把七片依片種對回槽。
    ///
    /// ⚠️ <b>解碼出來的順序是 BFS 的擺放順序，不是槽順序</b>（兩個大三角、兩個小三角
    /// 更是隨時可能對調）。直接照順序塞會讓「畫面上是正方形、模型以為是小三角」，
    /// 而且之後每一步都跟著錯、完全不報錯。片種相同的兩片互換沒有影響——
    /// 它們的原型一模一樣。
    /// </summary>
    /// <param name="pieces">七片，順序不拘。</param>
    /// <param name="bySlot">照槽索引排好的七片。湊不齊時是 null。</param>
    /// <returns>對得起來沒。</returns>
    private static bool TryOrderBySlot(IReadOnlyList<Placement> pieces, out Placement[] bySlot)
    {
        bySlot = null;
        if (pieces.Count != BoardState.PieceCount) return false;

        var result = new Placement[BoardState.PieceCount];
        var isSlotTaken = new bool[BoardState.PieceCount];
        BoardState board = LevelSession.Current?.Data?.Board;
        if (board == null) return false;

        foreach (Placement piece in pieces)
        {
            int matchedSlot = -1;
            for (int slotIndex = 0; slotIndex < BoardState.PieceCount; slotIndex++)
            {
                if (isSlotTaken[slotIndex] || board.KindOf(slotIndex) != piece.Kind) continue;
                matchedSlot = slotIndex;
                break;
            }

            if (matchedSlot < 0) return false;   // 這個片種的槽用完了＝檔案裡的片種湊不出一套
            isSlotTaken[matchedSlot] = true;
            result[matchedSlot] = piece;
        }

        bySlot = result;
        return true;
    }

    /// <summary>形狀的 key → 位置的索引整份重建。覆蓋存檔之後形狀可能變了，所以不能只補一筆。</summary>
    private void RebuildIndex()
    {
        entryIndexByPuzzleKey.Clear();
        for (int entryIndex = 0; entryIndex < savedEntries.Count; entryIndex++)
        {
            string puzzleKey = savedEntries[entryIndex].arrangement?.puzzleKey;
            if (puzzleKey != null) entryIndexByPuzzleKey[puzzleKey] = entryIndex;
        }
    }

    /// <summary>
    /// 整份重寫輸出檔（只有「覆蓋某一筆」會走這條）。
    ///
    /// ⚠️ <b>先寫到旁邊的檔案，成功了再換上去。</b>直接覆寫原檔的話，
    /// 寫到一半當掉就會<b>連已經存好的幾十題一起失去</b>——那是這個工具最貴的資料。
    /// </summary>
    /// <returns>寫成功了沒。</returns>
    private bool WriteAllEntries()
    {
        string stagingFilePath = outputFilePath + ".staging";
        try
        {
            var builder = new StringBuilder();
            foreach (SavedEntry entry in savedEntries) builder.Append(JsonUtility.ToJson(entry)).Append('\n');

            File.WriteAllText(stagingFilePath, builder.ToString(), new UTF8Encoding(false));
            File.Copy(stagingFilePath, outputFilePath, overwrite: true);
            File.Delete(stagingFilePath);
            return true;
        }
        catch (Exception error)
        {
            ReportStatus($"寫檔失敗：{error.Message}（原本的檔案沒有被動到）", isError: true);
            return false;
        }
    }

    /// <summary>
    /// 追加一行到輸出檔。
    ///
    /// <b>每存一題就寫一次檔，不是關閉時才一起寫</b>——工具當掉一次就少掉一整段工作。
    /// 追加也代表<b>這個檔永遠不會被整份重寫</b>，所以已經存進去的東西不可能被這支弄壞。
    /// </summary>
    /// <param name="entry">要存的那一筆。</param>
    /// <returns>寫成功了沒。</returns>
    private bool AppendEntry(SavedEntry entry)
    {
        try
        {
            // UTF-8 不加 BOM：名字是中文，而檔案之後要被別的工具讀
            File.AppendAllText(outputFilePath, JsonUtility.ToJson(entry) + "\n", new UTF8Encoding(false));
            return true;
        }
        catch (Exception error)
        {
            // ⚠️ 寫檔失敗一定要講出來。這個檔就是工具存在的全部理由，
            // 安靜地失敗會讓人以為存好了，關掉才發現什麼都沒有
            ReportStatus("寫檔失敗：" + error.Message, isError: true);
            return false;
        }
    }

    /// <summary>
    /// 讀回之前存過的題：擋重複用，也是「上一題／下一題」翻得到東西的前提。
    ///
    /// ⚠️ <b>壞掉的一行只跳過那一行，不讓整支開不起來</b>——
    /// 檔案是一行一筆的格式，一行壞掉不影響其他行。
    /// ⚠️ 但<b>跳過的行不會被寫回去</b>：之後只要覆蓋過任何一筆就會整份重寫，
    /// 那幾行就消失了。真的看到「有幾行讀不懂」，先去看檔案再繼續做事。
    /// </summary>
    private void LoadExistingEntries()
    {
        savedEntries.Clear();
        entryIndexByPuzzleKey.Clear();
        editingEntryIndex = -1;

        if (!File.Exists(outputFilePath))
        {
            ReportStatus($"還沒有存檔，之後會寫到 {outputFilePath}");
            return;
        }

        int brokenLineCount = 0;
        foreach (string line in File.ReadAllLines(outputFilePath, Encoding.UTF8))
        {
            if (string.IsNullOrWhiteSpace(line)) continue;
            try
            {
                SavedEntry entry = JsonUtility.FromJson<SavedEntry>(line);
                if (entry?.arrangement?.puzzleKey == null) { brokenLineCount++; continue; }
                savedEntries.Add(entry);
            }
            catch (Exception)
            {
                brokenLineCount++;
            }
        }
        RebuildIndex();

        string message = $"讀到 {savedEntries.Count} 題（{outputFilePath}）";
        if (brokenLineCount > 0) message += $"，另有 {brokenLineCount} 行讀不懂，已跳過";
        ReportStatus(message);
    }

    /// <summary>
    /// 輸出檔要放哪個資料夾：<b>專案（或 exe）旁邊的 <c>Tools/PuzzleEntries/</c></b>（2026-09-12，P2-8a）。
    ///
    /// standalone 的 <c>Application.dataPath</c> 是「exe 資料夾/專案名_Data」，
    /// 所以它的上一層就是 exe 所在的地方。在 Editor 裡 <c>dataPath</c> 是
    /// 「專案/Assets」，上一層剛好是專案資料夾——兩邊都是打開就看得到檔案的位置。
    ///
    /// ⚠️ <b>為什麼是 <c>Tools/</c> 底下，不是直接丟專案根目錄</b>（P2-8a 定）：
    /// <list type="number">
    /// <item><b>這是工作檔，不是出貨檔。</b>工作檔是 JSON Lines、逐題追加（當掉只掉最後一題、
    /// 兩個人的檔案好合併）；遊戲讀的出貨檔是 <c>Assets/Resources/</c> 底下的單一 JSON，
    /// 中間靠一支 <c>[Explicit]</c> 測試轉換——跟權重檔完全一樣的模式</item>
    /// <item>⚠️ <b><c>Tools/</c> 在 <c>Assets/</c> 外面，Unity 完全不會看它</b>
    /// （跟 <c>Tools/DotnetTests/UnityStub.cs</c> 同一個理由）。
    /// 幫忙出題的人不會碰到 <c>Assets/</c>，也就不會有漏 <c>.meta</c> 的問題</item>
    /// <item>一個資料夾放得下多份檔案，之後多人各自一份時不會撞在一起</item>
    /// </list>
    /// </summary>
    /// <returns>資料夾路徑；不存在就建出來。</returns>
    private static string ResolveOutputDirectory()
    {
        DirectoryInfo parent = Directory.GetParent(Application.dataPath);
        string root = parent != null ? parent.FullName : Application.persistentDataPath;
        string directory = Path.Combine(root, "Tools", "PuzzleEntries");

        // 第一次跑的時候資料夾還不存在。⚠️ 不先建的話症狀是「按了存檔但什麼都沒發生」
        // ——寫檔會丟 DirectoryNotFoundException，而那被 WriteAllEntries 的 catch 吃掉，
        // 只在狀態列留一行紅字
        Directory.CreateDirectory(directory);
        return directory;
    }

    /// <summary>把訊息顯示在畫面上，同時寫進 Console（實機看不到 Console，Editor 看不到也不方便）。</summary>
    /// <param name="message">訊息。</param>
    /// <param name="isError">是不是錯誤；錯誤會用沒過的顏色。</param>
    private void ReportStatus(string message, bool isError = false)
    {
        if (statusText != null)
        {
            statusText.text = message;
            statusText.color = isError ? failColor : passColor;
        }

        if (isError) Debug.LogWarning(message, this);
        else Debug.Log(message, this);
    }
}
