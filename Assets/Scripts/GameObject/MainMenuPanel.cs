using Tangram.Core;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// ⭐ <b>簡易主畫面</b>（2026-09-23）：兩顆按鈕，選這一場要玩哪一種題目。
///
/// <list type="bullet">
/// <item><b>挑戰模式</b>——照實力當場生題，就是原本的遊玩迴圈</item>
/// <item><b>圖形模式</b>——從題庫隨機抽具象題（貓、船之類），一輪之內不重複</item>
/// <item><b>掃描模式</b>（2026-10-14，名稱暫定）——先跳選擇視窗（新拍一張／從檔案選擇，<see cref="ScanSourcePanel"/>，2026-10-17），
/// 再取輪廓、照輪廓出題。
/// 選擇視窗取消、掃描取消或失敗時這裡會自己顯示回來（<see cref="LevelController.ScanAborted"/>）；
/// ⭐ 失敗的話多一行字告訴玩家為什麼（2026-10-23，<see cref="noticeText"/>），取消的不寫</item>
/// </list>
///
/// ⭐ <b>中途離開</b>（2026-10-23）：<see cref="Show"/> 一叫，進行中的那一關就暫停（<see cref="LevelController.PauseCurrentLevel"/>）；
/// 再按同一個模式是接著玩，按別的模式是新的一關（暫停那局作廢、不結算）。
/// 盤面上的「回主畫面」鈕（<see cref="BoardActionButtons"/>）與結果彈窗的出口都走 <see cref="Show"/>，兩個出口不會分家。
///
/// <b>跟 <see cref="LevelResultPanels"/> 同一個形狀</b>：不做事，只負責「什麼時候顯示」與
/// 「把按下轉給別人」。模式的真相在 <see cref="LevelController.CurrentMode"/>，這裡不留第二份——
/// 留了的話「下一題要抽還是要生」就有兩個答案。
///
/// <b>為什麼是同場景的 Panel，不是換場景</b>（2026-09-23 定案）：換場景要動 build 設定、
/// 要處理 <c>LevelSession.Current</c> 這個 static 的生死，而 9/27 畫面就要凍結了。
/// 同場景的 Panel 只多一個物件，風險最小。⏭️ 真的要卸載盤面時再換場景，
/// <see cref="LevelResultPanels"/> 的說明裡已經留了那條路。
///
/// <b>場景要做的事</b>：
/// <list type="number">
/// <item>在 Canvas 底下建一個<b>整個螢幕大小</b>的 Panel（四邊 anchor 拉滿），底下放兩顆 Button</item>
/// <item>⚠️ <b>Panel 要不透明</b>——它蓋著的時候後面的盤面還在，半透明的話會看到上一關</item>
/// <item>⚠️ <b>Panel 在 Canvas 裡要排在兩個結果彈窗<u>後面</u></b>（sibling index 比較大），
/// 不然從彈窗按「回主畫面」之後，彈窗會蓋在主畫面上</item>
/// <item>兩顆的 <c>onClick</c> 分別接到 <see cref="OnChallengeModePressed"/> /
/// <see cref="OnFigurativeModePressed"/></item>
/// <item>（2026-10-14）第三顆「掃描模式」的 <c>onClick</c> 接到 <see cref="OnScanModePressed"/></item>
/// <item>（2026-10-30）「清除紀錄」鈕（只在展示版出現）拖進 <see cref="clearProgressButton"/>，<c>onClick</c> 接到 <see cref="OnClearProgressPressed"/></item>
/// <item>⚠️ <b>這支腳本掛在另一個物件上，不能掛在 Panel 身上</b>——
/// 停用的物件不跑 <c>Update</c> 也收不到別人叫它的機會，藏起來就再也開不回來
/// （實際踩過一次）</item>
/// <item>把 Panel 的 <c>GameObject</c> 填進 <see cref="panel"/></item>
/// </list>
/// </summary>
[DisallowMultipleComponent]
public class MainMenuPanel : MonoBehaviour
{
    [Tooltip("要開關的主畫面 Panel。⚠️ 不能是這支腳本自己所在的物件。")]
    [SerializeField] private GameObject panel;

    [Tooltip("按下按鈕之後要叫它進關。留空的話 Awake 會自己找。")]
    [SerializeField] private LevelController levelController;

    [Tooltip("擋片區的登記對象。留空的話 Awake 會自己找。")]
    [SerializeField] private TouchInputController inputController;

    [Header("掃描失敗時的訊息（2026-10-23，選填）")]
    [Tooltip("主畫面上一行字：掃描失敗回來時寫原因，下次按任何模式或主畫面再出現時就清掉。留空的話不顯示（跟之前一樣）。")]
    [SerializeField] private TMP_Text noticeText;

    [Tooltip("拿照片時失敗：相機打不開、沒權限、照片讀不出來。")]
    [SerializeField] private string photoFailedText = "打不開相機（或讀不到這張照片），請確認有允許使用相機。";

    [Tooltip("辨識失敗：看不出照片裡的東西。")]
    [SerializeField] private string recognitionFailedText = "看不出照片裡的東西，換一張背景乾淨、東西完整的照片試試。";

    [Tooltip("出題失敗：這個形狀拼不出來。")]
    [SerializeField] private string puzzleFailedText = "這個形狀拼不出來，換一樣東西試試。";

    [Tooltip("設定或程式的問題。技術原因在 log。")]
    [SerializeField] private string unexpectedText = "出了點問題，請再試一次。";

    [Header("清除紀錄（2026-10-30，只在展示版出現）")]
    [Tooltip("「清除紀錄」鈕。onClick 接 OnClearProgressPressed。不是展示版（DemonstrationBuild）時開場就藏起來。選填。")]
    [SerializeField] private GameObject clearProgressButton;

    [Tooltip("清除之後寫在訊息那一行的字。{0} 是清除之後的實力。")]
    [SerializeField] private string progressClearedText = "紀錄已清除，實力回到 {0}。";

    [Header("快速提示（2026-10-30，只在展示版出現）")]
    [Tooltip("「快速提示」勾選。On Value Changed 接 OnQuickHintToggled（選 Dynamic bool 那一區的）。不是展示版時開場就藏起來。選填。")]
    [SerializeField] private Toggle quickHintToggle;

    [Tooltip("勾起來時的提示間隔（秒）。放棄鈕＝這個 × 3。不勾時回到場景裡 BoardActionButtons 原本設的值。")]
    [Min(1f)]
    [SerializeField] private float quickHintIntervalSeconds = 5f;

    [Tooltip("提示間隔的真相在它身上。留空的話 Awake 會自己找。")]
    [SerializeField] private BoardActionButtons boardActionButtons;

    /// <summary>場景裡原本設的提示間隔，開場記一次；取消勾選時改回它（45 秒不寫死在這裡）。</summary>
    private float originalHintIntervalSeconds;

    /// <summary>登記進擋片區的那塊矩形，離場時要取消登記。</summary>
    private RectTransform blockingArea;

    private void Awake()
    {
        if (levelController == null) levelController = FindFirstObjectByType<LevelController>();
        if (inputController == null) inputController = FindFirstObjectByType<TouchInputController>();

        if (panel == null)
            Debug.LogError("MainMenuPanel 的 panel 欄位沒填：主畫面不會被顯示或隱藏。", this);
        else if (panel == gameObject)
            Debug.LogError(
                "MainMenuPanel 掛在它自己要開關的 Panel 上了。藏起來之後這支就跟著被停用，" +
                "再也沒辦法把自己開回來。請把這支腳本移到另一個物件上（照 LevelResultPanels 的做法）。", this);

        // 展示版才有：換一位玩家時從頭開始用的。只在開場決定一次（編譯時就定了，執行中不會變）
        if (clearProgressButton != null) clearProgressButton.SetActive(DemonstrationBuild.IsActive);

        // 快速提示：同一個開關。每次開 App 都從「不勾」開始（不記住）——
        // 不通知地設成 false，免得場景裡存成勾起來的話開場就被改成 5 秒、卻沒人知道
        if (boardActionButtons == null) boardActionButtons = FindFirstObjectByType<BoardActionButtons>();
        if (boardActionButtons != null) originalHintIntervalSeconds = boardActionButtons.HintIntervalSeconds;
        if (quickHintToggle != null)
        {
            quickHintToggle.SetIsOnWithoutNotify(false);
            quickHintToggle.gameObject.SetActive(DemonstrationBuild.IsActive);
            if (boardActionButtons == null && DemonstrationBuild.IsActive)
                Debug.LogError("MainMenuPanel 找不到 BoardActionButtons：「快速提示」勾了不會有作用。", this);
        }

        if (levelController == null)
        {
            Debug.LogError("MainMenuPanel 找不到 LevelController：兩顆按鈕按了不會有反應。", this);
            return;
        }

        // ⚠️ 一定要在 Awake 裡講。自動進關是在 LevelController.Start 裡，
        // 而 Unity 保證所有 Awake 跑完才開始跑 Start——在 Start 裡講就賭上了兩支誰先跑
        levelController.SuppressAutoStart();

        // 掃描沒進關就結束（取消、失敗）時把主畫面顯示回來。不訂閱的話玩家會停在一個被藏起來的主畫面後面
        levelController.ScanAborted += OnScanAborted;
    }

    private void OnDestroy()
    {
        if (levelController != null) levelController.ScanAborted -= OnScanAborted;
    }

    /// <summary>
    /// 掃描沒有進關：主畫面顯示回來，失敗的話寫一行原因（2026-10-23）。<b>取消的不寫</b>——那是玩家自己選的。
    /// 先 <see cref="Show"/> 再寫字：<see cref="Show"/> 會把上一次的訊息清掉。
    /// </summary>
    /// <param name="reason">為什麼沒進關。</param>
    private void OnScanAborted(ScanAbortReason reason)
    {
        Show();
        SetNotice(NoticeFor(reason));
    }

    /// <param name="reason">為什麼沒進關。</param>
    /// <returns>給玩家看的那一句；取消時是空字串。</returns>
    private string NoticeFor(ScanAbortReason reason)
    {
        switch (reason)
        {
            case ScanAbortReason.PhotoFailed: return photoFailedText;
            case ScanAbortReason.RecognitionFailed: return recognitionFailedText;
            case ScanAbortReason.PuzzleFailed: return puzzleFailedText;
            case ScanAbortReason.Unexpected: return unexpectedText;
            default: return string.Empty;
        }
    }

    /// <summary>寫主畫面上那行訊息；空字串就藏起來。沒接 <see cref="noticeText"/> 的話什麼都不做。</summary>
    /// <param name="text">要寫的字。</param>
    private void SetNotice(string text)
    {
        if (noticeText == null) return;
        noticeText.text = text;
        noticeText.gameObject.SetActive(!string.IsNullOrEmpty(text));
    }

    /// <summary>
    /// 把整個 Panel 登記成「按在這裡不要抓片」。
    ///
    /// ⚠️ <b>只登記一次，不用跟著顯示開關</b>：擋片區本來就會跳過停用的物件，
    /// 所以主畫面藏起來時那塊自動不擋。跟結果彈窗完全一樣的做法。
    /// </summary>
    private void OnEnable()
    {
        if (panel == null || inputController == null) return;

        blockingArea = panel.GetComponent<RectTransform>();
        if (blockingArea == null)
        {
            Debug.LogError($"{panel.name} 上沒有 RectTransform，不是 UI 物件？" +
                           "主畫面蓋著的時候盤面還是抓得到片。", this);
            return;
        }

        inputController.RegisterPressBlockingArea(blockingArea);
    }

    private void OnDisable()
    {
        if (inputController == null || blockingArea == null) return;
        inputController.UnregisterPressBlockingArea(blockingArea);
    }

    /// <summary>開場先顯示主畫面，不進任何一關。</summary>
    private void Start() => Show();

    /// <summary>
    /// 顯示主畫面。從結果彈窗按「回主畫面」、盤面上按「回主畫面」（中途離開）都走這支。
    /// ⭐ 進行中的那一關暫停（2026-10-23，<see cref="LevelController.PauseCurrentLevel"/>；打完了的、沒有關的什麼都不做）。
    /// ⭐ 順便請 <see cref="LevelController"/> 在背景先把挑戰模式的下一題生好（2026-10-23）——
    /// 玩家看主畫面的時候就生完了，按挑戰模式不用等。目標沒變的話那邊什麼都不多做，所以每次都叫是安全的。
    /// 上一次掃描失敗的訊息在這裡清掉（要寫的話 <see cref="OnScanAborted"/> 會在之後補上）。
    /// </summary>
    public void Show()
    {
        if (panel != null) panel.SetActive(true);
        SetNotice(string.Empty);
        if (levelController == null) return;

        levelController.PauseCurrentLevel();
        levelController.PrepareNextChallengeLevel();
    }

    /// <summary>藏起主畫面。</summary>
    public void Hide()
    {
        if (panel != null) panel.SetActive(false);
    }

    /// <summary>「挑戰模式」被按下。接在 Unity <c>Button.onClick</c> 上。</summary>
    /// <remarks>
    /// 題目多半已經在主畫面出現時預生好了（2026-10-23）；還沒好就蓋等待遮罩，不會凍住。
    /// 暫停中的是挑戰模式的關，就是接著玩（2026-10-23）。
    /// </remarks>
    public void OnChallengeModePressed() => StartMode(GameMode.GeneratedByRating);

    /// <summary>「圖形模式」被按下。接在 Unity <c>Button.onClick</c> 上。</summary>
    /// <remarks>第一次按會多花零點幾秒讀題庫，之後就不會了。</remarks>
    public void OnFigurativeModePressed() => StartMode(GameMode.Figurative);

    /// <summary>「掃描模式」被按下。接在 Unity <c>Button.onClick</c> 上（2026-10-14）。</summary>
    /// <remarks>
    /// 按下去主畫面先藏起來，換選擇視窗出來（<see cref="ScanSourcePanel"/>）；進關要等拍照或選檔、辨識都回來。
    /// 選擇視窗取消、掃描取消或失敗會經由 <see cref="LevelController.ScanAborted"/> 叫回 <see cref="Show"/>。
    /// </remarks>
    public void OnScanModePressed() => StartMode(GameMode.Scan);

    /// <summary>
    /// ⭐「快速提示」勾選變了（2026-10-30，只在展示版出現）。接在 Toggle 的 <c>On Value Changed</c>（Dynamic bool）上。
    /// 勾了：提示間隔改成 <see cref="quickHintIntervalSeconds"/>（放棄＝× 3）；不勾：改回場景原本的值。
    ///
    /// ⚠️ <b>ELO 的平衡跟著變</b>：提示代價 P（30 秒）是配 45 秒間隔定的（<c>SkillRatingSettings.hintPenaltySeconds</c> 的說明），
    /// 間隔 5 秒時「卡住就拿提示」很划算——跟 K 的 10 倍同一類展示用的調整，所以只在展示版。
    /// </summary>
    /// <param name="isOn">勾了沒。</param>
    public void OnQuickHintToggled(bool isOn)
    {
        if (boardActionButtons == null || !DemonstrationBuild.IsActive) return;

        float seconds = isOn ? quickHintIntervalSeconds : originalHintIntervalSeconds;
        boardActionButtons.SetHintIntervalSeconds(seconds);
        Debug.Log($"快速提示{(isOn ? "開" : "關")}：提示間隔 {seconds:0.#} 秒，放棄鈕 {seconds * LevelData.MaximumHints:0.#} 秒。", this);
    }

    /// <summary>
    /// ⭐「清除紀錄」被按下（2026-10-30，只在展示版出現）。接在 Unity <c>Button.onClick</c> 上。
    /// 做事的是 <see cref="LevelController.ResetProgress"/>（刪檔、實力回起始值、暫停的那關作廢、重生下一題），這裡只轉達、寫一行結果。
    /// 主畫面不藏：清完還在主畫面上，接著按模式就是從頭開始。
    /// </summary>
    public void OnClearProgressPressed()
    {
        if (levelController == null || !DemonstrationBuild.IsActive) return;

        int? rating = levelController.ResetProgress();
        if (!rating.HasValue) return;      // ELO 設定讀不到，LevelController 開場已經報過錯

        string text;
        try
        {
            text = string.Format(progressClearedText, rating.Value);
        }
        catch (System.FormatException)
        {
            // 格式字串寫壞了也不能讓按鈕壞掉（同選題畫面「像不像」那行）
            Debug.LogWarning($"MainMenuPanel 的清除訊息格式寫壞了：「{progressClearedText}」，改用預設的字。", this);
            text = $"紀錄已清除，實力回到 {rating.Value}。";
        }
        SetNotice(text);
    }

    /// <summary>
    /// 先藏起自己再進關。
    /// <b>順序不能反</b>：<c>StartMode</c> 會換關，換關的五步裡有相機 fit 與擺七片，
    /// 那時候主畫面還蓋著的話，玩家會看到畫面在主畫面後面動。
    /// </summary>
    /// <param name="mode">要玩哪一種。</param>
    private void StartMode(GameMode mode)
    {
        if (levelController == null) return;

        SetNotice(string.Empty);
        Hide();
        levelController.StartMode(mode);
    }
}
