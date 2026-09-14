using System;
using System.Collections.Generic;
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
/// <b>這一支只負責排版</b>。狀態怎麼判、公式代進去哪幾個數、對帳過了沒，全在 <see cref="LevelMonitorReport"/>（有測試）。
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
        return monitor.Build(LevelSession.Current, levelController.CurrentDifficultyWeights, GameClock.NowSeconds);
    }

    // ───────── 狀態列 ─────────

    private static void DrawStatus(LevelMonitorReport report, string sessionProblem)
    {
        switch (report.State)
        {
            case MonitorState.NoSession:
                EditorGUILayout.HelpBox(sessionProblem ?? "沒有這一場。", MessageType.Info);
                break;
            case MonitorState.NoLevel:
                EditorGUILayout.HelpBox("還沒進關。", MessageType.Info);
                break;
            case MonitorState.NoPuzzle:
                EditorGUILayout.HelpBox("出題工具場景：沒有題目，不算難度也不結算。", MessageType.Info);
                break;
        }

        if (report.State != MonitorState.NoSession && report.Rating == null)
            EditorGUILayout.HelpBox("沒有實力紀錄（ELO 設定檔讀不到？看 Console）。結算與走勢都不會有。", MessageType.Warning);
    }

    // ───────── 這一題 ─────────

    private void DrawPuzzleSection(LevelMonitorReport report)
    {
        LevelData level = report.Level;

        EditorGUILayout.Space();
        EditorGUILayout.LabelField("這一題", EditorStyles.boldLabel);
        EditorGUILayout.LabelField("難度", level.Difficulty.ToString());
        EditorGUILayout.LabelField("出題", GenerationText(level.Generation, level.Difficulty));

        DifficultyBreakdown breakdown = report.Breakdown;
        if (breakdown == null)
        {
            EditorGUILayout.HelpBox("沒有權重，無法拆解難度。", MessageType.Warning);
            return;
        }

        EditorGUILayout.LabelField("權重指紋", breakdown.WeightsFingerprint);

        if (report.BreakdownMatchesDifficulty == false)
            EditorGUILayout.HelpBox(
                $"⚠️ 對帳對不上：拆解算出 {breakdown.Score}，這一關存著 {level.Difficulty}。\n" +
                "可能是出題器與拆解量特徵的方式不一樣了、原點規則改過，或 Play 中改了權重檔。ELO 用的是存著的那個。",
                MessageType.Error);

        showBreakdown = EditorGUILayout.Foldout(showBreakdown, "為什麼是這個分數", true);
        if (!showBreakdown) return;

        using (new EditorGUI.IndentLevelScope())
        {
            DrawRow(true, BreakdownColumns, "特徵", "原始值", "平均 ± 標準差", "離平均", "權重 × 方向", "分數");
            foreach (DifficultyTermContribution term in breakdown.Terms)
            {
                DrawRow(false, BreakdownColumns,
                        FeatureNameOf(term.Key),
                        term.RawValue.ToString("0.###"),
                        $"{term.Mean:0.##} ± {term.Deviation:0.##}",
                        double.IsNaN(term.StandardScore) ? "—" : $"{term.StandardScore:+0.00;-0.00;0.00} σ",
                        $"{term.Weight:0.##} × {(term.Direction > 0 ? "+1" : "−1")}",
                        term.Weight == 0.0 ? "不算分" : SignedPoints(term.Points));
            }

            // 1200 ＋ 66 − 40 …… ＝ 1347
            var sum = new System.Text.StringBuilder();
            sum.Append(breakdown.Center.ToString("0"));
            if (Math.Abs(breakdown.CenterOffsetPoints) >= 0.05) sum.Append($" {SignedPoints(breakdown.CenterOffsetPoints)}（校正）");
            foreach (DifficultyTermContribution term in breakdown.Terms)
                if (term.Weight != 0.0) sum.Append($" {SignedPoints(term.Points)}");
            sum.Append($" ＝ {breakdown.Score}");

            // 合計跟表格之間畫一條細線，跟算式分開
            Rect separator = EditorGUI.IndentedRect(EditorGUILayout.GetControlRect(false, 1f));
            EditorGUI.DrawRect(separator, new Color(0.5f, 0.5f, 0.5f, 0.5f));

            DrawSpanRow(BreakdownColumns, "合計", sum.ToString());
            DrawSpanRow(BreakdownColumns, string.Empty,
                        "（分數 ＝ 權重 × 方向 × 離平均 × 間距 ÷ 加權和的標準差；權重與基準批次見 DifficultyWeights.json）",
                        EditorStyles.miniLabel);
        }
    }

    /// <param name="generation">出題紀錄。</param>
    /// <param name="levelDifficulty">這一關的難度（失敗退路時要用它）。</param>
    /// <returns>一行文字。</returns>
    private static string GenerationText(GenerationRecord? generation, int levelDifficulty)
    {
        if (!generation.HasValue) return "非生成題（回饋題單、練習題或手動載入）";

        GenerationRecord record = generation.Value;
        switch (record.Outcome)
        {
            case TargetedGenerationOutcome.OnTarget:
                return $"目標 {record.TargetDifficulty} → 實際 {record.GeneratedDifficulty}，正中，試了 {record.AttemptCount} 次";
            case TargetedGenerationOutcome.SettledForNearest:
                return $"目標 {record.TargetDifficulty} → 實際 {record.GeneratedDifficulty}，⚠️ 將就（差 {record.DistanceFromTarget}），" +
                       $"預算 {record.AttemptCount} 次用完";
            default:
                return $"⚠️ 目標 {record.TargetDifficulty} 出題失敗（試了 {record.AttemptCount} 次），退回經典大正方形 {levelDifficulty}";
        }
    }

    /// <param name="key">特徵的鍵。</param>
    /// <returns>給人看的名字。不認得的鍵原樣顯示——這裡只是顯示，打錯字讀檔時就會被攔。</returns>
    private static string FeatureNameOf(string key)
    {
        switch (key)
        {
            case DifficultyTermKeys.Perimeter: return "外框周長";
            case DifficultyTermKeys.CornerCount: return "轉角數";
            case DifficultyTermKeys.ForcedPieceCount: return "被外框逼出的片數";
            case DifficultyTermKeys.KeyPiecesExposingOneVertex: return "只露一個頂點的關鍵片";
            case DifficultyTermKeys.StaggeredEdgeContactCount: return "錯開的邊接觸";
            case DifficultyTermKeys.LargeTrianglesJoined: return "兩片大三角拼成一塊";
            default: return key;
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
            case MonitorState.Playing:
                EditorGUILayout.LabelField("狀態", "進行中");
                EditorGUILayout.LabelField("時間",
                    $"前景 {report.ForegroundSecondsSoFar:F1} 秒　拖曳 {level.DragSeconds:F1} 秒（{level.DragCount} 次）　" +
                    $"提示 {level.HintsUsed} 次　清空 {level.ClearCount} 次");

                if (report.Rating != null && report.PreviewExpectedPerformance.HasValue)
                {
                    EditorGUILayout.LabelField("目前實力 R", report.Rating.CurrentRating.ToString("F1"));
                    EditorGUILayout.LabelField("預期表現 E",
                        $"1 ÷ (1 + 10^(({level.Difficulty} − {report.Rating.CurrentRating:F1}) ÷ {settings.ratingScale:0})) ＝ " +
                        $"{report.PreviewExpectedPerformance.Value:F3}");
                    EditorGUILayout.LabelField(" ", "照現在的實力，系統預期這一題的表現。解完之後拿實際表現跟它比。", EditorStyles.miniLabel);
                }
                break;

            case MonitorState.AwaitingSettlement:
                EditorGUILayout.HelpBox(
                    $"{(level.IsSolved ? "已過關" : "已放棄")}，結算中……\n" +
                    "正常情況下一格就補上。一直停在這裡代表被結算的守衛擋下了（例如計時沒停），看 SkillRatingTracker.TrySettle。",
                    MessageType.Info);
                break;

            case MonitorState.Settled:
                DrawSettlement(level.Settlement.Value, settings, report.TargetRange);
                break;
        }
    }

    /// <summary>
    /// 每一條公式都是「符號 → 代入數字 → 結果」三段，評審照著按計算機就驗得出來。
    /// </summary>
    /// <param name="update">結算明細。</param>
    /// <param name="settings">ELO 參數。</param>
    /// <param name="targetRange">這一場的目標難度範圍，只拿來顯示「夾進 X～Y」；沒有權重時是 null。</param>
    private static void DrawSettlement(RatingUpdate update, SkillRatingSettings settings, TargetDifficultyRange targetRange)
    {
        bool solved = update.Outcome == LevelOutcome.Solved;
        EditorGUILayout.LabelField("結局", solved ? "過關" : "放棄");

        DrawFormula("預期表現 E", "1 ÷ (1 + 10^((D − R) ÷ 尺度))",
                    $"1 ÷ (1 + 10^(({update.Difficulty} − {update.RatingBefore:F1}) ÷ {settings.ratingScale:0}))",
                    update.ExpectedPerformance.ToString("F3"));

        DrawFormula("時間代價 C", "前景 − (1 − r) × 拖曳 ＋ 提示 × P",
                    $"{update.ForegroundSeconds:F1} − (1 − {settings.dragWeightRatio:0.##}) × {update.DragSeconds:F1} ＋ " +
                    $"{update.HintsUsed} × {settings.hintPenaltySeconds:0.##}",
                    $"{update.TimeCostSeconds:F1} 秒");

        if (solved)
            DrawFormula("實際表現 S", "T ÷ (T ＋ C)",
                        $"{settings.referenceSeconds:0.##} ÷ ({settings.referenceSeconds:0.##} ＋ {update.TimeCostSeconds:F1})",
                        update.ActualPerformance.ToString("F3"));
        else
            DrawFormula("實際表現 S", "放棄 ＝ 0", "—", "0");

        DrawFormula("實力變化 ΔR", "K × (S − E)",
                    $"{update.EffectiveKFactor:0.##} × ({update.ActualPerformance:F3} − {update.ExpectedPerformance:F3})",
                    SignedPoints(update.RatingChange));
        EditorGUILayout.LabelField(" ",
            $"K ＝ 基準 {settings.baseKFactor:0.##} × 展示倍率 {settings.demonstrationMultiplier:0.##}", EditorStyles.miniLabel);

        EditorGUILayout.LabelField("實力", $"{update.RatingBefore:F1} → {update.RatingAfter:F1}");

        // 目標讀明細裡記下的那個——方向比的就是它，這裡不另外算
        string rangeText = targetRange != null ? $"夾進 {targetRange.Minimum}～{targetRange.Maximum}" : "夾進出題範圍";
        int gap = update.NextTargetDifficulty - update.Difficulty;
        EditorGUILayout.LabelField("下一題",
            $"目標 {update.NextTargetDifficulty}（實力四捨五入、{rangeText}）− 這一題 {update.Difficulty} ＝ {gap:+0;-0;0}，" +
            $"門檻 ±{TargetedPuzzleGenerator.DefaultTolerance} → {TrendTextOf(update.Trend)}");
    }

    /// <param name="label">左邊的名字。</param>
    /// <param name="symbolic">符號版。</param>
    /// <param name="substituted">代入數字版。</param>
    /// <param name="result">結果。</param>
    private static void DrawFormula(string label, string symbolic, string substituted, string result)
    {
        EditorGUILayout.LabelField(label, $"{symbolic}　＝　{substituted}　＝　{result}");
    }

    private static string TrendTextOf(NextPuzzleTrend trend)
    {
        switch (trend)
        {
            case NextPuzzleTrend.Harder: return "會難一點";
            case NextPuzzleTrend.Easier: return "會簡單一點";
            default: return "差不多";
        }
    }

    // ───────── 走勢 ─────────

    private void DrawHistorySection(LevelMonitorReport report)
    {
        if (report.Rating == null) return;

        IReadOnlyList<SettlementRecord> history = report.History;

        EditorGUILayout.Space();
        showHistory = EditorGUILayout.Foldout(showHistory,
            $"這一場的走勢（{history.Count} 局，起始 {report.Rating.StartingRating:F1} → 目前 {report.Rating.CurrentRating:F1}）", true);
        if (!showHistory) return;

        if (history.Count == 0)
        {
            EditorGUILayout.LabelField(" ", "還沒有結算過任何一局。", EditorStyles.miniLabel);
            return;
        }

        using (new EditorGUI.IndentLevelScope())
        {
            DrawRow(true, HistoryColumns, "#", "出題", "難度", "結局", "C（秒）", "E → S", "ΔR", "實力");
            foreach (SettlementRecord record in history)
            {
                RatingUpdate update = record.Update;
                DrawRow(false, HistoryColumns,
                        record.SequenceNumber.ToString(),
                        ShortGenerationText(record.Generation),
                        update.Difficulty.ToString(),
                        update.Outcome == LevelOutcome.Solved ? "過關" : "放棄",
                        update.TimeCostSeconds.ToString("F1"),
                        $"{update.ExpectedPerformance:F2} → {update.ActualPerformance:F2}",
                        SignedPoints(update.RatingChange),
                        update.RatingAfter.ToString("F1"));
            }
        }
    }

    /// <param name="generation">出題紀錄。</param>
    /// <returns>走勢表裡那一格。</returns>
    private static string ShortGenerationText(GenerationRecord? generation)
    {
        if (!generation.HasValue) return "非生成題";
        GenerationRecord record = generation.Value;
        switch (record.Outcome)
        {
            case TargetedGenerationOutcome.OnTarget: return $"{record.TargetDifficulty} 正中";
            case TargetedGenerationOutcome.SettledForNearest: return $"{record.TargetDifficulty} ⚠️將就";
            default: return $"{record.TargetDifficulty} ⚠️失敗→退路";
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

        if (report.IsDemonstrationMultiplierActive)
            EditorGUILayout.HelpBox(
                $"展示版：K 是正式設定的 {settings.demonstrationMultiplier:0.##} 倍。系統概述文件與影片必須寫明。",
                MessageType.Warning);

        using (new EditorGUI.IndentLevelScope())
        {
            EditorGUILayout.LabelField("T（秒數的尺）", $"{settings.referenceSeconds:0.##} 秒");
            EditorGUILayout.LabelField("r（拖曳一秒算幾秒）", settings.dragWeightRatio.ToString("0.##"));
            EditorGUILayout.LabelField("P（一次提示算幾秒）", $"{settings.hintPenaltySeconds:0.##} 秒");
            EditorGUILayout.LabelField("K", $"基準 {settings.baseKFactor:0.##} × 展示倍率 {settings.demonstrationMultiplier:0.##} ＝ {settings.EffectiveKFactor:0.##}");
            EditorGUILayout.LabelField("尺度", settings.ratingScale.ToString("0.##"));
            EditorGUILayout.LabelField("起始實力", settings.initialRating.ToString("0.##"));
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

    /// <param name="points">分數。</param>
    /// <returns>帶正負號、一位小數。</returns>
    private static string SignedPoints(double points) => points.ToString("+0.0;-0.0;0.0");
}
