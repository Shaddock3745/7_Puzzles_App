using System;
using System.Collections.Generic;
using System.Text;
using Tangram.Data;

namespace Tangram.Core
{
    /// <summary>監控面板上的一列「名稱：值」。</summary>
    public readonly struct MonitorLine
    {
        /// <summary>左邊的名字。</summary>
        public readonly string Label;

        /// <summary>右邊的值。</summary>
        public readonly string Value;

        /// <param name="label">左邊的名字。</param>
        /// <param name="value">右邊的值。</param>
        public MonitorLine(string label, string value)
        {
            Label = label;
            Value = value;
        }
    }

    /// <summary>
    /// 一條公式的三段：符號 → 代入數字 → 結果。評審照著按計算機就驗得出來。
    /// </summary>
    public readonly struct MonitorFormula
    {
        /// <summary>左邊的名字（例如「預期表現 E」）。</summary>
        public readonly string Label;

        /// <summary>符號版。</summary>
        public readonly string Symbolic;

        /// <summary>代入數字版。</summary>
        public readonly string Substituted;

        /// <summary>結果。</summary>
        public readonly string Result;

        /// <param name="label">左邊的名字。</param>
        /// <param name="symbolic">符號版。</param>
        /// <param name="substituted">代入數字版。</param>
        /// <param name="result">結果。</param>
        public MonitorFormula(string label, string symbolic, string substituted, string result)
        {
            Label = label;
            Symbolic = symbolic;
            Substituted = substituted;
            Result = result;
        }

        /// <summary>三段接成一行：「符號　＝　代入　＝　結果」。</summary>
        public string Equation => $"{Symbolic}　＝　{Substituted}　＝　{Result}";
    }

    /// <summary>App 裡的監控面板有哪幾頁（2026-10-23）。</summary>
    public enum MonitorPage
    {
        /// <summary>這一題：難度、出題紀錄、拆解。</summary>
        Puzzle,

        /// <summary>這一局：狀態、統計、預期表現、結算公式。</summary>
        Round,

        /// <summary>這一場的走勢。</summary>
        History,

        /// <summary>ELO 參數。</summary>
        Settings,
    }

    /// <summary>
    /// ⭐ <b>監控面板上的每一個字</b>（P5-3，issue #38，2026-10-23）：<see cref="LevelMonitorReport"/> → 要顯示的字串。
    ///
    /// <b>為什麼搬進 Core</b>：監控面板現在有兩個殼——Editor 的視窗（<c>MonitorWindow</c>，表格排版）與 App 裡的 overlay
    /// （<c>DebugOverlay</c>，純文字分頁）。公式代進哪幾個數、特徵叫什麼名字、出題紀錄怎麼講，<b>兩邊各寫一份就會分家</b>，
    /// 而分家的症狀是「影片裡的面板跟現場的面板講的不一樣」，不報錯。放這裡兩邊共用，而且 <c>dotnet test</c> 驗得到。
    ///
    /// <b>這一層只組字，不決定排版</b>：表格的欄寬、字級、顏色是殼的事。
    /// 會分欄的東西（拆解表、走勢表）給的是「一格一格的字」，殼自己決定怎麼排；只有 overlay 用的 <see cref="PageText"/> 把它們接成整段。
    /// </summary>
    public static class LevelMonitorText
    {
        // ───────── 共用的小字串 ─────────

        /// <param name="points">分數。</param>
        /// <returns>帶正負號、一位小數。</returns>
        public static string SignedPoints(double points) => points.ToString("+0.0;-0.0;0.0");

        /// <param name="key">特徵的鍵。</param>
        /// <returns>給人看的名字。不認得的鍵原樣顯示——這裡只是顯示，打錯字讀檔時就會被攔。</returns>
        public static string FeatureNameOf(string key)
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

        /// <param name="generation">出題紀錄。</param>
        /// <param name="levelDifficulty">這一關的難度（失敗退路時要用它）。</param>
        /// <returns>一行文字。</returns>
        public static string GenerationText(GenerationRecord? generation, int levelDifficulty)
        {
            if (!generation.HasValue) return "非生成題（回饋題單、練習題、具象題、掃描題或手動載入）";

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

        /// <param name="generation">出題紀錄。</param>
        /// <returns>走勢表裡那一格。</returns>
        public static string ShortGenerationText(GenerationRecord? generation)
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

        /// <param name="trend">下一題的方向。</param>
        /// <returns>給人看的字。</returns>
        public static string TrendTextOf(NextPuzzleTrend trend)
        {
            switch (trend)
            {
                case NextPuzzleTrend.Harder: return "會難一點";
                case NextPuzzleTrend.Easier: return "會簡單一點";
                default: return "差不多";
            }
        }

        // ───────── 狀態列 ─────────

        /// <summary>
        /// 面板最上面那一行：沒有東西可看的原因，或「沒有實力紀錄」的警告。<b>一切正常時是 null。</b>
        /// </summary>
        /// <param name="report">報告。</param>
        /// <param name="sessionProblem">殼自己知道的「拿不到這一場」的原因（例如 Editor 沒在 Play）；沒有就傳 null。</param>
        /// <returns>一行文字或 null。</returns>
        public static string StatusText(LevelMonitorReport report, string sessionProblem = null)
        {
            if (report == null) throw new ArgumentNullException(nameof(report));

            switch (report.State)
            {
                case MonitorState.NoSession: return sessionProblem ?? "沒有這一場。";
                case MonitorState.NoLevel: return "還沒進關。";
                case MonitorState.NoPuzzle: return "出題工具場景：沒有題目，不算難度也不結算。";
            }

            if (report.Rating == null) return "沒有實力紀錄（ELO 設定檔讀不到？看 Console）。結算與走勢都不會有。";
            return null;
        }

        // ───────── 這一題 ─────────

        /// <summary>拆解表的標題列。</summary>
        public static readonly IReadOnlyList<string> BreakdownHeader = new[] { "特徵", "原始值", "平均 ± 標準差", "離平均", "權重 × 方向", "分數" };

        /// <param name="term">一個特徵的貢獻。</param>
        /// <returns>拆解表的一列，格數跟 <see cref="BreakdownHeader"/> 一樣。</returns>
        public static string[] BreakdownCells(DifficultyTermContribution term) => new[]
        {
            FeatureNameOf(term.Key),
            term.RawValue.ToString("0.###"),
            $"{term.Mean:0.##} ± {term.Deviation:0.##}",
            double.IsNaN(term.StandardScore) ? "—" : $"{term.StandardScore:+0.00;-0.00;0.00} σ",
            $"{term.Weight:0.##} × {(term.Direction > 0 ? "+1" : "−1")}",
            term.Weight == 0.0 ? "不算分" : SignedPoints(term.Points),
        };

        /// <summary>「1200 ＋ 66 − 40 …… ＝ 1347」那一行。不算分的特徵不列。</summary>
        /// <param name="breakdown">拆解。</param>
        /// <returns>合計的算式。</returns>
        public static string BreakdownSumText(DifficultyBreakdown breakdown)
        {
            if (breakdown == null) throw new ArgumentNullException(nameof(breakdown));

            var sum = new StringBuilder();
            sum.Append(breakdown.Center.ToString("0"));
            if (Math.Abs(breakdown.CenterOffsetPoints) >= 0.05) sum.Append($" {SignedPoints(breakdown.CenterOffsetPoints)}（校正）");
            foreach (DifficultyTermContribution term in breakdown.Terms)
                if (term.Weight != 0.0) sum.Append($" {SignedPoints(term.Points)}");
            sum.Append($" ＝ {breakdown.Score}");
            return sum.ToString();
        }

        /// <summary>合計底下那行小字：分數怎麼算的。</summary>
        public const string BreakdownFootnote = "（分數 ＝ 權重 × 方向 × 離平均 × 間距 ÷ 加權和的標準差；權重與基準批次見 DifficultyWeights.json）";

        /// <param name="report">報告。</param>
        /// <returns>拆解跟存著的難度對不上時的紅字；對得上或沒有拆解時是 null。</returns>
        public static string BreakdownMismatchText(LevelMonitorReport report)
        {
            if (report?.Breakdown == null || report.BreakdownMatchesDifficulty != false) return null;
            return $"⚠️ 對帳對不上：拆解算出 {report.Breakdown.Score}，這一關存著 {report.Level.Difficulty}。\n" +
                   "可能是出題器與拆解量特徵的方式不一樣了、原點規則改過，或 Play 中改了權重檔。ELO 用的是存著的那個。";
        }

        // ───────── 這一局 ─────────

        /// <param name="report">報告。</param>
        /// <returns>「這一局」的狀態字：進行中／暫停中／已過關／已放棄。</returns>
        public static string RoundStateText(LevelMonitorReport report)
        {
            LevelData level = report.Level;
            if (level == null) return string.Empty;
            if (!level.IsFinished) return report.IsPaused ? "暫停中（主畫面蓋著，不計時）" : "進行中";
            return level.IsSolved ? "已過關" : "已放棄";
        }

        /// <param name="report">報告。</param>
        /// <returns>局內統計那一行。</returns>
        public static string RoundStatisticsText(LevelMonitorReport report)
        {
            LevelData level = report.Level;
            return $"前景 {report.ForegroundSecondsSoFar:F1} 秒　拖曳 {level.DragSeconds:F1} 秒（{level.DragCount} 次）　" +
                   $"提示 {level.HintsUsed} 次　清空 {level.ClearCount} 次";
        }

        /// <summary>
        /// 還在玩的時候，照<b>現在的實力</b>預覽這一題的預期表現 E。沒有預覽（不是在玩、沒有實力紀錄）時是 null。
        /// </summary>
        /// <param name="report">報告。</param>
        /// <returns>公式或 null。</returns>
        public static MonitorFormula? PreviewFormula(LevelMonitorReport report)
        {
            if (report.Rating == null || !report.PreviewExpectedPerformance.HasValue) return null;
            return new MonitorFormula("預期表現 E", "1 ÷ (1 + 10^((D − R) ÷ 尺度))",
                $"1 ÷ (1 + 10^(({report.Level.Difficulty} − {report.Rating.CurrentRating:F1}) ÷ {report.Settings.ratingScale:0}))",
                report.PreviewExpectedPerformance.Value.ToString("F3"));
        }

        /// <summary>結算之後的四條公式：E、C、S、ΔR。</summary>
        /// <param name="update">結算明細。</param>
        /// <param name="settings">ELO 參數。</param>
        /// <returns>四條，順序固定。</returns>
        public static IReadOnlyList<MonitorFormula> SettlementFormulas(RatingUpdate update, SkillRatingSettings settings)
        {
            if (settings == null) throw new ArgumentNullException(nameof(settings));

            bool solved = update.Outcome == LevelOutcome.Solved;
            return new[]
            {
                new MonitorFormula("預期表現 E", "1 ÷ (1 + 10^((D − R) ÷ 尺度))",
                    $"1 ÷ (1 + 10^(({update.Difficulty} − {update.RatingBefore:F1}) ÷ {settings.ratingScale:0}))",
                    update.ExpectedPerformance.ToString("F3")),
                new MonitorFormula("時間代價 C", "前景 − (1 − r) × 拖曳 ＋ 提示 × P",
                    $"{update.ForegroundSeconds:F1} − (1 − {settings.dragWeightRatio:0.##}) × {update.DragSeconds:F1} ＋ " +
                    $"{update.HintsUsed} × {settings.hintPenaltySeconds:0.##}",
                    $"{update.TimeCostSeconds:F1} 秒"),
                solved
                    ? new MonitorFormula("實際表現 S", "T ÷ (T ＋ C)",
                        $"{settings.referenceSeconds:0.##} ÷ ({settings.referenceSeconds:0.##} ＋ {update.TimeCostSeconds:F1})",
                        update.ActualPerformance.ToString("F3"))
                    : new MonitorFormula("實際表現 S", "放棄 ＝ 0", "—", "0"),
                new MonitorFormula("實力變化 ΔR", "K × (S − E)",
                    $"{update.EffectiveKFactor:0.##} × ({update.ActualPerformance:F3} − {update.ExpectedPerformance:F3})",
                    SignedPoints(update.RatingChange)),
            };
        }

        /// <param name="settings">ELO 參數。</param>
        /// <returns>ΔR 底下那行小字：K 是怎麼來的。</returns>
        public static string KFactorNote(SkillRatingSettings settings) =>
            $"K ＝ 基準 {settings.baseKFactor:0.##} × 展示倍率 {settings.demonstrationMultiplier:0.##}";

        /// <param name="update">結算明細。</param>
        /// <param name="targetRange">這一場的目標難度範圍，只拿來顯示「夾進 X～Y」；沒有權重時是 null。</param>
        /// <returns>結算之後的兩行：實力的變化、下一題的目標與方向。</returns>
        public static IReadOnlyList<MonitorLine> SettlementResultLines(RatingUpdate update, TargetDifficultyRange targetRange)
        {
            // 目標讀明細裡記下的那個——方向比的就是它，這裡不另外算
            string rangeText = targetRange != null ? $"夾進 {targetRange.Minimum}～{targetRange.Maximum}" : "夾進出題範圍";
            int gap = update.NextTargetDifficulty - update.Difficulty;
            return new[]
            {
                new MonitorLine("實力", $"{update.RatingBefore:F1} → {update.RatingAfter:F1}"),
                new MonitorLine("下一題",
                    $"目標 {update.NextTargetDifficulty}（實力四捨五入、{rangeText}）− 這一題 {update.Difficulty} ＝ {gap:+0;-0;0}，" +
                    $"門檻 ±{TargetedPuzzleGenerator.DefaultTolerance} → {TrendTextOf(update.Trend)}"),
            };
        }

        /// <summary>結算中（結束了但還沒結算）那段說明。</summary>
        /// <param name="level">這一關。</param>
        /// <returns>兩行說明。</returns>
        public static string AwaitingSettlementText(LevelData level) =>
            $"{(level.IsSolved ? "已過關" : "已放棄")}，結算中……\n" +
            "正常情況下一格就補上。一直停在這裡代表被結算的守衛擋下了（例如計時沒停），看 SkillRatingTracker.TrySettle。";

        // ───────── 走勢 ─────────

        /// <summary>走勢表的標題列。</summary>
        public static readonly IReadOnlyList<string> HistoryHeader = new[] { "#", "出題", "難度", "結局", "C（秒）", "E → S", "ΔR", "實力" };

        /// <param name="report">報告。</param>
        /// <returns>走勢那一段的標題（局數、起始 → 目前）；沒有實力紀錄時是 null。</returns>
        public static string HistoryTitle(LevelMonitorReport report)
        {
            if (report.Rating == null) return null;
            return $"這一場的走勢（{report.History.Count} 局，起始 {report.Rating.StartingRating:F1} → 目前 {report.Rating.CurrentRating:F1}）";
        }

        /// <param name="record">一局的結算。</param>
        /// <returns>走勢表的一列，格數跟 <see cref="HistoryHeader"/> 一樣。</returns>
        public static string[] HistoryCells(SettlementRecord record)
        {
            RatingUpdate update = record.Update;
            return new[]
            {
                record.SequenceNumber.ToString(),
                ShortGenerationText(record.Generation),
                update.Difficulty.ToString(),
                update.Outcome == LevelOutcome.Solved ? "過關" : "放棄",
                update.TimeCostSeconds.ToString("F1"),
                $"{update.ExpectedPerformance:F2} → {update.ActualPerformance:F2}",
                SignedPoints(update.RatingChange),
                update.RatingAfter.ToString("F1"),
            };
        }

        // ───────── 參數 ─────────

        /// <param name="settings">ELO 參數。</param>
        /// <returns>參數那一段的每一列。</returns>
        public static IReadOnlyList<MonitorLine> SettingsLines(SkillRatingSettings settings)
        {
            if (settings == null) throw new ArgumentNullException(nameof(settings));
            return new[]
            {
                new MonitorLine("T（秒數的尺）", $"{settings.referenceSeconds:0.##} 秒"),
                new MonitorLine("r（拖曳一秒算幾秒）", settings.dragWeightRatio.ToString("0.##")),
                new MonitorLine("P（一次提示算幾秒）", $"{settings.hintPenaltySeconds:0.##} 秒"),
                new MonitorLine("K", $"基準 {settings.baseKFactor:0.##} × 展示倍率 {settings.demonstrationMultiplier:0.##} ＝ {settings.EffectiveKFactor:0.##}"),
                new MonitorLine("尺度", settings.ratingScale.ToString("0.##")),
                new MonitorLine("起始實力", settings.initialRating.ToString("0.##")),
            };
        }

        /// <param name="report">報告。</param>
        /// <returns>展示倍率不是 1 時的警語；是 1 時是 null。</returns>
        public static string DemonstrationWarning(LevelMonitorReport report) =>
            report.IsDemonstrationMultiplierActive
                ? $"展示版：K 是正式設定的 {report.Settings.demonstrationMultiplier:0.##} 倍。系統概述文件與影片必須寫明。"
                : null;

        // ───────── 整頁（App 裡的 overlay 用）─────────

        /// <summary>
        /// 一整頁的純文字，<b>App 裡的 overlay 用</b>（Editor 的視窗自己用上面的零件排表格）。
        /// 分欄的東西用兩個全形空白隔開，對不齊是接受的（字級與排版是美術，往後放）。
        /// </summary>
        /// <param name="report">報告。</param>
        /// <param name="page">哪一頁。</param>
        /// <param name="maximumHistoryRows">走勢最多列幾局（最新的那幾局）；手機畫面放不下太多。</param>
        /// <returns>整頁的字，最後沒有換行。</returns>
        /// <exception cref="ArgumentNullException"><paramref name="report"/> 是 null。</exception>
        /// <exception cref="ArgumentOutOfRangeException"><paramref name="maximumHistoryRows"/> 小於 1。</exception>
        public static string PageText(LevelMonitorReport report, MonitorPage page, int maximumHistoryRows = 8)
        {
            if (report == null) throw new ArgumentNullException(nameof(report));
            if (maximumHistoryRows < 1) throw new ArgumentOutOfRangeException(nameof(maximumHistoryRows), maximumHistoryRows, "至少要列一局");

            var text = new StringBuilder();
            string status = StatusText(report);
            if (status != null) text.AppendLine(status);

            switch (page)
            {
                case MonitorPage.Puzzle: AppendPuzzlePage(text, report); break;
                case MonitorPage.Round: AppendRoundPage(text, report); break;
                case MonitorPage.History: AppendHistoryPage(text, report, maximumHistoryRows); break;
                case MonitorPage.Settings: AppendSettingsPage(text, report); break;
            }
            return text.ToString().TrimEnd('\r', '\n');
        }

        /// <summary>分欄的格子之間的空白。</summary>
        private const string CellGap = "　　";

        private static void AppendLine(StringBuilder text, MonitorLine line) => text.AppendLine($"{line.Label}　{line.Value}");

        private static void AppendFormula(StringBuilder text, MonitorFormula formula) => text.AppendLine($"{formula.Label}　{formula.Equation}");

        private static bool HasPuzzle(LevelMonitorReport report) => report.Level != null && report.State != MonitorState.NoPuzzle;

        private static void AppendPuzzlePage(StringBuilder text, LevelMonitorReport report)
        {
            if (!HasPuzzle(report)) return;
            LevelData level = report.Level;

            AppendLine(text, new MonitorLine("難度", level.Difficulty.ToString()));
            AppendLine(text, new MonitorLine("出題", GenerationText(level.Generation, level.Difficulty)));

            DifficultyBreakdown breakdown = report.Breakdown;
            if (breakdown == null)
            {
                text.AppendLine("沒有權重，無法拆解難度。");
                return;
            }

            AppendLine(text, new MonitorLine("權重指紋", breakdown.WeightsFingerprint));
            string mismatch = BreakdownMismatchText(report);
            if (mismatch != null) text.AppendLine(mismatch);

            text.AppendLine(string.Join(CellGap, BreakdownHeader));
            foreach (DifficultyTermContribution term in breakdown.Terms) text.AppendLine(string.Join(CellGap, BreakdownCells(term)));
            AppendLine(text, new MonitorLine("合計", BreakdownSumText(breakdown)));
            text.AppendLine(BreakdownFootnote);
        }

        private static void AppendRoundPage(StringBuilder text, LevelMonitorReport report)
        {
            if (!HasPuzzle(report)) return;
            LevelData level = report.Level;

            switch (report.State)
            {
                case MonitorState.NotRated:
                    AppendLine(text, new MonitorLine("狀態", RoundStateText(report)));
                    AppendLine(text, new MonitorLine("時間", RoundStatisticsText(report)));
                    text.AppendLine("這一關不結算實力（掃描題）。");
                    break;

                case MonitorState.Playing:
                    AppendLine(text, new MonitorLine("狀態", RoundStateText(report)));
                    AppendLine(text, new MonitorLine("時間", RoundStatisticsText(report)));
                    MonitorFormula? preview = PreviewFormula(report);
                    if (preview.HasValue)
                    {
                        AppendLine(text, new MonitorLine("目前實力 R", report.Rating.CurrentRating.ToString("F1")));
                        AppendFormula(text, preview.Value);
                        text.AppendLine("照現在的實力，系統預期這一題的表現。解完之後拿實際表現跟它比。");
                    }
                    break;

                case MonitorState.AwaitingSettlement:
                    text.AppendLine(AwaitingSettlementText(level));
                    break;

                case MonitorState.Settled:
                    RatingUpdate update = level.Settlement.Value;
                    AppendLine(text, new MonitorLine("結局", update.Outcome == LevelOutcome.Solved ? "過關" : "放棄"));
                    foreach (MonitorFormula formula in SettlementFormulas(update, report.Settings)) AppendFormula(text, formula);
                    text.AppendLine(KFactorNote(report.Settings));
                    foreach (MonitorLine line in SettlementResultLines(update, report.TargetRange)) AppendLine(text, line);
                    break;
            }
        }

        private static void AppendHistoryPage(StringBuilder text, LevelMonitorReport report, int maximumHistoryRows)
        {
            string title = HistoryTitle(report);
            if (title == null) return;
            text.AppendLine(title);

            IReadOnlyList<SettlementRecord> history = report.History;
            if (history.Count == 0)
            {
                text.AppendLine("還沒有結算過任何一局。");
                return;
            }

            text.AppendLine(string.Join(CellGap, HistoryHeader));
            int first = Math.Max(0, history.Count - maximumHistoryRows);
            if (first > 0) text.AppendLine($"（前 {first} 局省略）");
            for (int i = first; i < history.Count; i++) text.AppendLine(string.Join(CellGap, HistoryCells(history[i])));
        }

        private static void AppendSettingsPage(StringBuilder text, LevelMonitorReport report)
        {
            if (report.Settings == null) return;
            string warning = DemonstrationWarning(report);
            if (warning != null) text.AppendLine(warning);
            foreach (MonitorLine line in SettingsLines(report.Settings)) AppendLine(text, line);
        }
    }
}
