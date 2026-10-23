using System;
using System.Collections.Generic;
using System.Linq;
using Tangram.Core;
using Tangram.Data;
using UnityEditor;
using UnityEngine;

/// <summary>
/// 監控面板（P2-15，issue #32）。<b>Window → Tangram → 監控面板</b>，擺在 Game view 旁邊。
///
/// <b>它回答三件事</b>：這一題為什麼是這個難度、參考解長什麼樣、實力為什麼 +13。
/// 這是「把量化做得看得見」的載體——評審看得到算式裡代進去的每一個數字，可以當場驗。
///
/// ⭐ <b>只在 Editor</b>（2026-09-14 定，待辦清單 D 區的丙案）：這支住在 <c>Editor</c> 資料夾，
/// Unity 會把它編成 Editor 專用的組件，<b>物理上進不了 build</b>——比編譯符號更徹底，
/// 不會有「關得掉腳本、關不掉場景物件」那種事（<c>DebugTools</c> 踩過）。
/// 理由：回饋收集的 exe 會交給小朋友玩，build 裡藏著面板就可能讓他們看到分數（違反 9/13 的 A 案）；
/// 手機畫面也沒有地方放。代價是決賽 Demo 要用筆電開 Unity，要不要進 APP 延到 10/5 之後再決定——
/// 那時只要再做一個殼，資料那半（<see cref="LevelMonitor"/>）不動。
///
/// ⚠️ <b>錄影</b>：Unity Recorder 只錄 Game view，錄不到這個視窗，影片要用 OBS 這類工具錄整個螢幕。
///
/// <b>它只讀，絕對不寫。</b>上面沒有任何會改到遊戲狀態的按鈕。
/// 也因為它不在遊戲畫面上，<b>不需要登記擋片區</b>。
///
/// <b>這一支只負責排版</b>。狀態怎麼判、對帳過了沒在 <see cref="LevelMonitorReport"/>；
/// ⭐ 每一個字（公式代進哪幾個數、特徵名、出題紀錄怎麼講）2026-10-23 起在 <see cref="LevelMonitorText"/>，
/// 跟 App 裡的監控 overlay（<c>DebugOverlay</c>）共用——兩邊各寫一份的話影片裡跟現場的面板會講不一樣的話。
/// 顏色、字級、欄寬都是暫定的，美術往後放。
/// </summary>
public class MonitorWindow : EditorWindow
{
    /// <summary>
    /// 資料那半，持有難度拆解的快取。<b>不序列化</b>：離開 Play 或重新編譯之後就該重來，
    /// 留著的話會指著上一場的 <c>LevelData</c>。
    /// </summary>
    [NonSerialized] private LevelMonitor monitor;

    /// <summary>權重的來源。找一次存起來；物件被銷毀（離開 Play、換場景）時 Unity 的 == null 會成立，就再找一次。</summary>
    [NonSerialized] private LevelController levelController;

    private Vector2 scrollPosition;
    private bool showBreakdown = true;
    private bool showReferenceSolution = true;
    private bool showHistory = true;
    private bool showSettings = true;

    /// <summary>參考解那一塊的高度，單位是 Editor 的 GUI 點。</summary>
    private const float ReferenceSolutionHeight = 220f;

    [MenuItem("Window/Tangram/監控面板")]
    private static void Open()
    {
        var window = GetWindow<MonitorWindow>();
        window.titleContent = new GUIContent("監控面板");
        window.Show();
    }

    private void OnEnable() => monitor = new LevelMonitor();

    /// <summary>
    /// 一秒 10 次。數字是給人看的，10 次夠了。
    /// ⚠️ <b>走勢不會因為刷新慢而漏</b>——漏不漏是 <see cref="SkillRatingTracker"/> 的事，它在結算的同一刻就記下來了。
    /// </summary>
    private void OnInspectorUpdate()
    {
        if (EditorApplication.isPlaying) Repaint();
    }

    private void OnGUI()
    {
        if (monitor == null) monitor = new LevelMonitor();

        LevelMonitorReport report;
        string sessionProblem = null;
        try
        {
            report = BuildReport(out sessionProblem);
        }
        catch (Exception exception)
        {
            // 拆解會丟例外的情況（權重在記憶體裡被改壞之類）。顯示出來就好，不要每次重畫都洗 Console
            EditorGUILayout.HelpBox($"組監控報告時出錯：{exception.Message}", MessageType.Error);
            return;
        }

        scrollPosition = EditorGUILayout.BeginScrollView(scrollPosition);
        DrawStatus(report, sessionProblem);

        if (report.Level != null && report.State != MonitorState.NoPuzzle)
        {
            DrawPuzzleSection(report);
            DrawReferenceSolutionSection(report.Level);
            DrawRoundSection(report);
        }

        if (report.State != MonitorState.NoSession)
        {
            DrawHistorySection(report);
            DrawSettingsSection(report);
        }
        EditorGUILayout.EndScrollView();
    }

    /// <param name="sessionProblem">拿不到這一場的原因；拿得到是 null。</param>
    /// <returns>這一刻的報告。</returns>
    private LevelMonitorReport BuildReport(out string sessionProblem)
    {
        sessionProblem = null;

        // ⚠️ 先問在不在 Play，不在就不去碰 LevelSession.Current——靜態欄位不一定會被清掉
        // （專案要是關了進 Play 時的網域重載就一定不會），碰了會拿到上一場的東西
        if (!EditorApplication.isPlaying)
        {
            sessionProblem = "進 Play 模式後才有資料。";
            return monitor.Build(null, null, 0.0);
        }

        if (levelController == null) levelController = FindFirstObjectByType<LevelController>();

        // 場景裡沒有 LevelController 的話，LevelSession.Current 是別的場景留下來的（或 null），同樣不能信
        if (levelController == null)
        {
            sessionProblem = "場景裡沒有 LevelController，沒有關卡可以看。";
            return monitor.Build(null, null, 0.0);
        }

        // ⭐ 權重跟 LevelController 拿，不自己讀檔——要跟出題用的是同一個實例，理由見 LevelMonitor.Build
        return monitor.Build(LevelSession.Current, levelController.CurrentDifficultyWeights, GameClock.NowSeconds,
                             levelController.IsLevelPaused);
    }

    // ───────── 狀態列 ─────────

    private static void DrawStatus(LevelMonitorReport report, string sessionProblem)
    {
        string status = LevelMonitorText.StatusText(report, sessionProblem);
        if (status == null) return;

        // 沒有東西可看是說明；有關卡卻沒有實力紀錄是警告
        bool nothingToShow = report.State == MonitorState.NoSession || report.State == MonitorState.NoLevel ||
                             report.State == MonitorState.NoPuzzle;
        EditorGUILayout.HelpBox(status, nothingToShow ? MessageType.Info : MessageType.Warning);
    }

    // ───────── 這一題 ─────────

    private void DrawPuzzleSection(LevelMonitorReport report)
    {
        LevelData level = report.Level;

        EditorGUILayout.Space();
        EditorGUILayout.LabelField("這一題", EditorStyles.boldLabel);
        EditorGUILayout.LabelField("難度", level.Difficulty.ToString());
        EditorGUILayout.LabelField("出題", LevelMonitorText.GenerationText(level.Generation, level.Difficulty));

        DifficultyBreakdown breakdown = report.Breakdown;
        if (breakdown == null)
        {
            EditorGUILayout.HelpBox("沒有權重，無法拆解難度。", MessageType.Warning);
            return;
        }

        EditorGUILayout.LabelField("權重指紋", breakdown.WeightsFingerprint);

        string mismatch = LevelMonitorText.BreakdownMismatchText(report);
        if (mismatch != null) EditorGUILayout.HelpBox(mismatch, MessageType.Error);

        showBreakdown = EditorGUILayout.Foldout(showBreakdown, "為什麼是這個分數", true);
        if (!showBreakdown) return;

        using (new EditorGUI.IndentLevelScope())
        {
            DrawRow(true, BreakdownColumns, LevelMonitorText.BreakdownHeader.ToArray());
            foreach (DifficultyTermContribution term in breakdown.Terms)
                DrawRow(false, BreakdownColumns, LevelMonitorText.BreakdownCells(term));

            // 合計跟表格之間畫一條細線，跟算式分開
            Rect separator = EditorGUI.IndentedRect(EditorGUILayout.GetControlRect(false, 1f));
            EditorGUI.DrawRect(separator, new Color(0.5f, 0.5f, 0.5f, 0.5f));

            DrawSpanRow(BreakdownColumns, "合計", LevelMonitorText.BreakdownSumText(breakdown));
            DrawSpanRow(BreakdownColumns, string.Empty, LevelMonitorText.BreakdownFootnote, EditorStyles.miniLabel);
        }
    }

    // ───────── 參考解 ─────────

    private void DrawReferenceSolutionSection(LevelData level)
    {
        EditorGUILayout.Space();
        showReferenceSolution = EditorGUILayout.Foldout(showReferenceSolution, "參考解", true);
        if (!showReferenceSolution) return;

        if (level.ReferenceSolution == null)
        {
            EditorGUILayout.HelpBox("這一關沒有參考解。", MessageType.Info);
            return;
        }

        Rect area = GUILayoutUtility.GetRect(10f, ReferenceSolutionHeight, GUILayout.ExpandWidth(true));
        if (Event.current.type != EventType.Repaint) return;

        EditorGUI.DrawRect(area, new Color(0.16f, 0.16f, 0.16f));
        DrawReferenceSolution(area, level);
    }

    /// <summary>
    /// 把盒子輪廓與七片參考解縮放進 <paramref name="area"/>。
    ///
    /// ⚠️ <b>Y 軸要翻</b>：模型 Y 朝上，GUI 的 Y 朝下。寫錯的症狀是參考解上下顛倒，而且不報錯——
    /// 翻面的片會看起來像沒翻，旋轉會看起來反了。<b>要跟 Game view 並排看過一次才算驗過。</b>
    /// 片的頂點直接用 <see cref="Placement.Vertices"/>（永遠逆時針），不會有背面剔除的問題。
    /// </summary>
    /// <param name="area">要畫的地方（GUI 座標）。</param>
    /// <param name="level">這一關。</param>
    private static void DrawReferenceSolution(Rect area, LevelData level)
    {
        BoundingBox bounds = level.BoxRegion.Bounds;
        double minimumX = bounds.Minimum.X.ToDouble();
        double minimumY = bounds.Minimum.Y.ToDouble();
        double width = bounds.Maximum.X.ToDouble() - minimumX;
        double height = bounds.Maximum.Y.ToDouble() - minimumY;
        if (width <= 0.0 || height <= 0.0) return;

        const float padding = 12f;
        float scale = (float)Math.Min((area.width - padding * 2f) / width, (area.height - padding * 2f) / height);
        if (scale <= 0f) return;

        // 置中：多出來的寬或高平均分到兩邊
        float offsetX = area.x + (area.width - (float)width * scale) * 0.5f;
        float offsetY = area.y + (area.height - (float)height * scale) * 0.5f;

        Vector3 ToGui(ExactVector2 point) => new Vector3(
            offsetX + (float)(point.X.ToDouble() - minimumX) * scale,
            offsetY + (float)(height - (point.Y.ToDouble() - minimumY)) * scale,   // Y 翻過來
            0f);

        Color previous = Handles.color;

        Placement[] solution = level.ReferenceSolution;
        for (int slotIndex = 0; slotIndex < solution.Length; slotIndex++)
        {
            ExactVector2[] vertices = solution[slotIndex].Vertices();
            var points = new Vector3[vertices.Length];
            for (int i = 0; i < vertices.Length; i++) points[i] = ToGui(vertices[i]);

            // 顏色只是讓七片分得開，美術往後放
            Handles.color = Color.HSVToRGB(slotIndex / (float)solution.Length, 0.5f, 0.85f);
            Handles.DrawAAConvexPolygon(points);

            var outline = new Vector3[points.Length + 1];
            Array.Copy(points, outline, points.Length);
            outline[points.Length] = points[0];
            Handles.color = new Color(0f, 0f, 0f, 0.6f);
            Handles.DrawAAPolyLine(1.5f, outline);
        }

        Handles.color = Color.white;
        foreach (ExactVector2[] ring in level.BoxRegion.Rings)
        {
            var outline = new Vector3[ring.Length + 1];
            for (int i = 0; i < ring.Length; i++) outline[i] = ToGui(ring[i]);
            outline[ring.Length] = outline[0];
            Handles.DrawAAPolyLine(2.5f, outline);
        }

        Handles.color = previous;
    }

    // ───────── 這一局 ─────────

    private static void DrawRoundSection(LevelMonitorReport report)
    {
        LevelData level = report.Level;
        SkillRatingSettings settings = report.Settings;

        EditorGUILayout.Space();
        EditorGUILayout.LabelField("這一局", EditorStyles.boldLabel);

        switch (report.State)
        {
            // 掃描題（2026-10-14）：統計照樣顯示，但沒有預期表現也沒有結算——這一局不會拿來比
            case MonitorState.NotRated:
                EditorGUILayout.LabelField("狀態", LevelMonitorText.RoundStateText(report));
                DrawRoundStatistics(report);
                EditorGUILayout.HelpBox("這一關不結算實力（掃描題）。", MessageType.None);
                break;

            case MonitorState.Playing:
                EditorGUILayout.LabelField("狀態", LevelMonitorText.RoundStateText(report));
                DrawRoundStatistics(report);

                MonitorFormula? preview = LevelMonitorText.PreviewFormula(report);
                if (preview.HasValue)
                {
                    EditorGUILayout.LabelField("目前實力 R", report.Rating.CurrentRating.ToString("F1"));
                    DrawFormula(preview.Value);
                    EditorGUILayout.LabelField(" ", "照現在的實力，系統預期這一題的表現。解完之後拿實際表現跟它比。", EditorStyles.miniLabel);
                }
                break;

            case MonitorState.AwaitingSettlement:
                EditorGUILayout.HelpBox(LevelMonitorText.AwaitingSettlementText(level), MessageType.Info);
                break;

            case MonitorState.Settled:
                DrawSettlement(level.Settlement.Value, settings, report.TargetRange);
                break;
        }
    }

    /// <summary>局內統計那一行。進行中與不結算的關卡共用。</summary>
    private static void DrawRoundStatistics(LevelMonitorReport report) =>
        EditorGUILayout.LabelField("時間", LevelMonitorText.RoundStatisticsText(report));

    /// <summary>
    /// 每一條公式都是「符號 → 代入數字 → 結果」三段，評審照著按計算機就驗得出來。
    /// </summary>
    /// <param name="update">結算明細。</param>
    /// <param name="settings">ELO 參數。</param>
    /// <param name="targetRange">這一場的目標難度範圍，只拿來顯示「夾進 X～Y」；沒有權重時是 null。</param>
    private static void DrawSettlement(RatingUpdate update, SkillRatingSettings settings, TargetDifficultyRange targetRange)
    {
        EditorGUILayout.LabelField("結局", update.Outcome == LevelOutcome.Solved ? "過關" : "放棄");

        IReadOnlyList<MonitorFormula> formulas = LevelMonitorText.SettlementFormulas(update, settings);
        foreach (MonitorFormula formula in formulas) DrawFormula(formula);
        EditorGUILayout.LabelField(" ", LevelMonitorText.KFactorNote(settings), EditorStyles.miniLabel);

        foreach (MonitorLine line in LevelMonitorText.SettlementResultLines(update, targetRange))
            EditorGUILayout.LabelField(line.Label, line.Value);
    }

    /// <param name="formula">一條公式。</param>
    private static void DrawFormula(MonitorFormula formula) => EditorGUILayout.LabelField(formula.Label, formula.Equation);

    // ───────── 走勢 ─────────

    private void DrawHistorySection(LevelMonitorReport report)
    {
        if (report.Rating == null) return;

        IReadOnlyList<SettlementRecord> history = report.History;

        EditorGUILayout.Space();
        showHistory = EditorGUILayout.Foldout(showHistory, LevelMonitorText.HistoryTitle(report), true);
        if (!showHistory) return;

        if (history.Count == 0)
        {
            EditorGUILayout.LabelField(" ", "還沒有結算過任何一局。", EditorStyles.miniLabel);
            return;
        }

        using (new EditorGUI.IndentLevelScope())
        {
            DrawRow(true, HistoryColumns, LevelMonitorText.HistoryHeader.ToArray());
            foreach (SettlementRecord record in history) DrawRow(false, HistoryColumns, LevelMonitorText.HistoryCells(record));
        }
    }

    // ───────── 參數 ─────────

    private void DrawSettingsSection(LevelMonitorReport report)
    {
        SkillRatingSettings settings = report.Settings;
        if (settings == null) return;

        EditorGUILayout.Space();
        showSettings = EditorGUILayout.Foldout(showSettings, "ELO 參數（SkillRatingSettings.json）", true);
        if (!showSettings) return;

        string warning = LevelMonitorText.DemonstrationWarning(report);
        if (warning != null) EditorGUILayout.HelpBox(warning, MessageType.Warning);

        using (new EditorGUI.IndentLevelScope())
        {
            foreach (MonitorLine line in LevelMonitorText.SettingsLines(settings))
                EditorGUILayout.LabelField(line.Label, line.Value);
        }
    }

    // ───────── 共用 ─────────

    /// <summary>
    /// 表格的一欄：佔多寬（比例）、靠哪邊。
    /// ⚠️ <b>寬度是比例不是像素</b>：視窗拉寬拉窄，標題列與每一列都照同一組比例切，欄位才對得齊。
    /// 原本用 GUILayout 讓每格自己決定寬度，寬度會跟著內容長短變，每一列切出來的位置都不一樣。
    /// </summary>
    private readonly struct Column
    {
        public readonly float Weight;
        public readonly TextAnchor Alignment;

        public Column(float weight, TextAnchor alignment)
        {
            Weight = weight;
            Alignment = alignment;
        }
    }

    /// <summary>難度拆解表：特徵名靠左，數字全部靠右（小數點才對得齊）。</summary>
    private static readonly Column[] BreakdownColumns =
    {
        new Column(2.4f, TextAnchor.MiddleLeft),    // 特徵
        new Column(1.0f, TextAnchor.MiddleRight),   // 原始值
        new Column(1.5f, TextAnchor.MiddleRight),   // 平均 ± 標準差
        new Column(1.0f, TextAnchor.MiddleRight),   // 離平均
        new Column(1.1f, TextAnchor.MiddleRight),   // 權重 × 方向
        new Column(0.9f, TextAnchor.MiddleRight),   // 分數
    };

    /// <summary>走勢表。</summary>
    private static readonly Column[] HistoryColumns =
    {
        new Column(0.4f, TextAnchor.MiddleRight),   // #
        new Column(1.8f, TextAnchor.MiddleLeft),    // 出題
        new Column(0.8f, TextAnchor.MiddleRight),   // 難度
        new Column(0.7f, TextAnchor.MiddleCenter),  // 結局
        new Column(0.8f, TextAnchor.MiddleRight),   // C
        new Column(1.3f, TextAnchor.MiddleRight),   // E → S
        new Column(0.8f, TextAnchor.MiddleRight),   // ΔR
        new Column(0.9f, TextAnchor.MiddleRight),   // 實力
    };

    /// <summary>欄與欄之間留多少空白，單位是 GUI 點。</summary>
    private const float ColumnGap = 8f;

    /// <summary>
    /// 一列表格。整列先跟版面要一塊矩形，再照 <paramref name="columns"/> 的比例切。
    /// </summary>
    /// <param name="isHeader">是不是標題列。</param>
    /// <param name="columns">欄的設定，格數要跟 <paramref name="cells"/> 一樣。</param>
    /// <param name="cells">每一格的文字。</param>
    private static void DrawRow(bool isHeader, Column[] columns, params string[] cells)
    {
        Rect row = EditorGUI.IndentedRect(EditorGUILayout.GetControlRect());
        Rect[] cellRects = SplitRow(row, columns);
        for (int i = 0; i < cells.Length && i < cellRects.Length; i++)
            GUI.Label(cellRects[i], cells[i], StyleOf(isHeader, columns[i].Alignment));
    }

    /// <summary>
    /// 第一欄放標題，其餘幾欄併成一格放一長串文字（「合計」那一行）。
    /// 標題跟上面的特徵名對齊，內容從第二欄的左緣開始。
    /// </summary>
    /// <param name="columns">欄的設定。</param>
    /// <param name="label">第一欄的字。</param>
    /// <param name="text">後面那一長串。</param>
    /// <param name="style">後面那一串的樣式；null 用一般字。</param>
    private static void DrawSpanRow(Column[] columns, string label, string text, GUIStyle style = null)
    {
        Rect row = EditorGUI.IndentedRect(EditorGUILayout.GetControlRect());
        Rect[] cellRects = SplitRow(row, columns);

        GUI.Label(cellRects[0], label, StyleOf(false, TextAnchor.MiddleLeft));
        var rest = new Rect(cellRects[1].x, row.y, row.xMax - cellRects[1].x, row.height);
        GUI.Label(rest, text, style ?? StyleOf(false, TextAnchor.MiddleLeft));
    }

    /// <param name="row">整列的矩形。</param>
    /// <param name="columns">欄的設定。</param>
    /// <returns>每一欄的矩形，已經扣掉欄間空白。</returns>
    private static Rect[] SplitRow(Rect row, Column[] columns)
    {
        float totalWeight = 0f;
        foreach (Column column in columns) totalWeight += column.Weight;

        float usableWidth = Mathf.Max(0f, row.width - ColumnGap * (columns.Length - 1));
        var rects = new Rect[columns.Length];
        float x = row.x;
        for (int i = 0; i < columns.Length; i++)
        {
            float width = usableWidth * columns[i].Weight / totalWeight;
            rects[i] = new Rect(x, row.y, width, row.height);
            x += width + ColumnGap;
        }
        return rects;
    }

    /// <summary>樣式快取：標題／內容 × 對齊方式。GUIStyle 只能在 OnGUI 裡建，所以用到才建。</summary>
    private static readonly Dictionary<(bool, TextAnchor), GUIStyle> cellStyles = new Dictionary<(bool, TextAnchor), GUIStyle>();

    /// <param name="isHeader">是不是標題列。</param>
    /// <param name="alignment">對齊方式。</param>
    /// <returns>樣式。太長的字會被裁掉，不會壓到隔壁那一格。</returns>
    private static GUIStyle StyleOf(bool isHeader, TextAnchor alignment)
    {
        if (cellStyles.TryGetValue((isHeader, alignment), out GUIStyle style)) return style;

        style = new GUIStyle(isHeader ? EditorStyles.miniBoldLabel : EditorStyles.label)
        {
            alignment = alignment,
            clipping = TextClipping.Clip,
        };
        cellStyles[(isHeader, alignment)] = style;
        return style;
    }
}
