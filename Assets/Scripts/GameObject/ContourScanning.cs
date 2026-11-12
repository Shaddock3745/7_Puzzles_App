using System;
using System.Collections;
using Tangram.Data;
using UnityEngine;

// ⭐ 掃描模式的前兩段（2026-10-14）：取得照片 → 辨識模組 → 輪廓 JSON。
//
//   主畫面「掃描模式」→ 選擇視窗 → IContourScanner ┬ IPhotoSource（拍照或選檔，PhotoSources.cs）
//                                                  └ IContourRecognizer（照片 → JSON，辨識模組）
//                    → RecognizedContour（讀 JSON）→ IContourPuzzleBuilder（出題）→ 進關
//
// 後兩段在 Data 層（RecognizedContour.cs、ContourPuzzleBuilder.cs），測得到；這兩段碰到相機與照片，只能在 Unity 裡跑。
//
// ⭐ 分工（2026-10-17 定）：辨識模組只實作 IContourRecognizer 這一個介面（給照片、回 JSON），
// 相機與選檔在 App 這一側（IPhotoSource）。辨識模組不自帶相機元件，也不實作整個 IContourScanner。

/// <summary>掃描的下場。</summary>
public enum ContourScanStatus
{
    /// <summary>拿到輪廓 JSON 了。</summary>
    Succeeded,

    /// <summary>玩家自己取消（關掉相機、按返回）。<b>不是錯誤</b>，不用報。辨識模組本身不會回這個。</summary>
    Cancelled,

    /// <summary>出事了：相機打不開、沒拍到東西、模型跑不出來……原因在 <see cref="ContourScanResult.FailureReason"/>。</summary>
    Failed
}

/// <summary>
/// ⭐ 失敗發生在哪一段（2026-10-23）。<c>LevelController</c> 照它決定給玩家看哪一句話——技術原因照舊在 <see cref="ContourScanResult.FailureReason"/>，只進 log。
/// </summary>
public enum ContourScanFailureStage
{
    /// <summary>沒有失敗。</summary>
    None,

    /// <summary>拿照片：相機打不開、沒權限、照片讀不出來。</summary>
    Photo,

    /// <summary>辨識：模型看不出東西、跑不出來、丟例外。<b>辨識模組回的失敗一律是這一段</b>（<see cref="ContourScanResult.Failed"/> 的預設值）。</summary>
    Recognition,
}

/// <summary>
/// 一次掃描（或一次辨識）的結果。三種下場用同一個型別，呼叫端只要寫一個回呼。
/// </summary>
public readonly struct ContourScanResult
{
    /// <summary>下場。</summary>
    public readonly ContourScanStatus Status;

    /// <summary>辨識模組交回來的 JSON 原文。<b>只有 <see cref="ContourScanStatus.Succeeded"/> 時有值</b>。</summary>
    public readonly string ContourJson;

    /// <summary>失敗的原因，給 log 與之後的畫面訊息用。<b>只有 <see cref="ContourScanStatus.Failed"/> 時有值</b>。</summary>
    public readonly string FailureReason;

    /// <summary>⭐ 失敗發生在哪一段（2026-10-23）。<b>只有 <see cref="ContourScanStatus.Failed"/> 時不是 <see cref="ContourScanFailureStage.None"/></b>。</summary>
    public readonly ContourScanFailureStage FailureStage;

    /// <summary>
    /// 拍到的照片（2026-10-14，照片對照用）。<b>可以是 null</b>（辨識模組那一層不帶照片；掃描那一層才會補上）。
    ///
    /// ⚠️ <b>必須是辨識模組看到的那一張、而且已經轉正</b>——照片跟 JSON 的像素座標要是同一套，
    /// 照片對照才疊得準（相機畫面的旋轉、鏡射要在這之前處理掉）。
    /// ⚠️ 這是執行時建的貼圖，<b>收到的人負責保管與釋放</b>（遊戲裡交給 <c>ScanPhotoView</c>，用不到就 <c>Destroy</c>）。
    /// </summary>
    public readonly Texture2D Photo;

    private ContourScanResult(ContourScanStatus status, string contourJson, string failureReason,
                              ContourScanFailureStage failureStage, Texture2D photo)
    {
        Status = status;
        ContourJson = contourJson;
        FailureReason = failureReason;
        FailureStage = failureStage;
        Photo = photo;
    }

    /// <param name="contourJson">辨識模組交回來的 JSON 原文。</param>
    /// <param name="photo">拍到的照片，可以是 null。</param>
    /// <returns>成功的結果。</returns>
    public static ContourScanResult Succeeded(string contourJson, Texture2D photo = null) =>
        new ContourScanResult(ContourScanStatus.Succeeded, contourJson, null, ContourScanFailureStage.None, photo);

    /// <returns>玩家取消的結果。</returns>
    public static ContourScanResult Cancelled() =>
        new ContourScanResult(ContourScanStatus.Cancelled, null, null, ContourScanFailureStage.None, null);

    /// <param name="reason">失敗的原因（技術的，只進 log）。</param>
    /// <param name="stage">
    /// 發生在哪一段。<b>預設是辨識</b>：辨識模組只會碰到這一段，不用知道有這個參數；
    /// 拿照片那段由 <see cref="PhotoContourScanner"/> 明確傳 <see cref="ContourScanFailureStage.Photo"/>。
    /// </param>
    /// <returns>失敗的結果。</returns>
    public static ContourScanResult Failed(string reason, ContourScanFailureStage stage = ContourScanFailureStage.Recognition) =>
        new ContourScanResult(ContourScanStatus.Failed, null, reason,
                              stage == ContourScanFailureStage.None ? ContourScanFailureStage.Recognition : stage, null);
}

/// <summary>
/// ⭐ <b>辨識模組的接口</b>：給一張照片，回一份輪廓 JSON。<b>辨識模組只需要實作這一個介面</b>（2026-10-17 定）。
///
/// <b>為什麼是非同步（回呼）</b>：手機上推論可能要好幾秒，做成同步的話換成真的那天整個畫面會凍住。
///
/// <b>收到的照片保證</b>（<see cref="IPhotoSource"/> 的約定）：已經轉正、讀得到像素（RGBA32）、
/// 長邊不超過 <see cref="PhotoTextures.MaximumLongSide"/>。
///
/// <b>實作要守的約定</b>：
/// <list type="bullet">
/// <item>⭐ <b>JSON 的 <c>width</c>／<c>height</c> 必須等於這張照片的寬高</b>，點落在這張照片的像素座標上、<b>y 朝上</b>
///   （原點在左下，跟 Unity 貼圖一樣）。模型內部縮成 320 × 320 是模組自己的事，回來之前要換算回這張照片的尺寸——
///   對不上的話照片對照會改畫剪影並寫警告（<c>LevelController.HandPhotoToView</c>）</item>
/// <item>⚠️ <b>不要釋放、也不要改這張照片</b>：它之後還要拿去畫照片對照，由掃描那一層保管</item>
/// <item>⚠️ <b>不能在 <see cref="Recognize"/> 裡面當場叫回呼</b>，至少等到下一幀——
///   叫的人還停在「開始辨識」那一行裡就收到「完成、進關」，執行順序跟真的完全不一樣</item>
/// <item><b>回呼只能叫一次，而且要在主執行緒上叫</b>（之後要碰 Unity 的物件）</item>
/// <item>只會回 <see cref="ContourScanStatus.Succeeded"/> 或 <see cref="ContourScanStatus.Failed"/>，
///   取消是拍照那一層的事；成功時不用附照片（附了也會被忽略）</item>
/// </list>
/// </summary>
public interface IContourRecognizer
{
    /// <summary>辨識一張照片。</summary>
    /// <param name="photo">照片，約定見介面說明。假實作不看。</param>
    /// <param name="onFinished">辨識完叫一次。</param>
    void Recognize(Texture2D photo, Action<ContourScanResult> onFinished);
}

/// <summary>
/// ⭐ <b>呼叫辨識模組的接口</b>：取得照片 → 把照片交給辨識模組 → 收結果。
/// 掃描模式只跟這一層講話，不直接碰相機或辨識模組。
///
/// 約定跟 <see cref="IContourRecognizer"/> 一樣：不能當場回、回呼只叫一次、在主執行緒上叫。
/// </summary>
public interface IContourScanner
{
    /// <summary>是不是有一次掃描還沒回來。</summary>
    bool IsScanning { get; }

    /// <summary>開始一次掃描。</summary>
    /// <param name="onFinished">拿到 JSON、玩家取消、或失敗時叫一次。</param>
    /// <exception cref="InvalidOperationException">上一次還沒回來（<see cref="IsScanning"/> 是 true）。</exception>
    void BeginScan(Action<ContourScanResult> onFinished);
}

/// <summary>
/// ⚠️ <b>假的辨識模組</b>：不看照片，永遠回同一份 JSON（<c>Resources/ScanSamples/</c> 裡辨識模組產出的樣本）。
///
/// <b>下一幀才回</b>，照 <see cref="IContourRecognizer"/> 的約定——所以它需要一個 <c>MonoBehaviour</c> 幫它跑協程。
///
/// ⚠️ 2026-10-17 起前面接的是真的照片，而這份樣本是 320 × 320——<b>尺寸一定對不上，照片對照會畫剪影</b>
/// （見 <see cref="ContourStandInPhoto"/>），Console 每次都有一行警告，那是預期的。
/// </summary>
public sealed class SampleFileContourRecognizer : IContourRecognizer
{
    private readonly string contourJson;
    private readonly MonoBehaviour coroutineHost;

    /// <param name="contourJson">永遠回這一份。null 的話每次都回失敗（樣本檔沒找到）。</param>
    /// <param name="coroutineHost">幫忙跑協程的物件。⚠️ 它被停用的話回呼就不會來。</param>
    /// <exception cref="ArgumentNullException"><paramref name="coroutineHost"/> 是 null。</exception>
    public SampleFileContourRecognizer(string contourJson, MonoBehaviour coroutineHost)
    {
        this.contourJson = contourJson;
        this.coroutineHost = coroutineHost ? coroutineHost : throw new ArgumentNullException(nameof(coroutineHost));
    }

    /// <inheritdoc/>
    public void Recognize(Texture2D photo, Action<ContourScanResult> onFinished)
    {
        if (onFinished == null) throw new ArgumentNullException(nameof(onFinished));
        coroutineHost.StartCoroutine(ReplyNextFrame(onFinished));
    }

    private IEnumerator ReplyNextFrame(Action<ContourScanResult> onFinished)
    {
        yield return null;

        onFinished(contourJson == null
            ? ContourScanResult.Failed("假的辨識模組沒有樣本 JSON 可以回。")
            : ContourScanResult.Succeeded(contourJson));
    }
}

/// <summary>
/// ⭐ <b>照片 → 輪廓的掃描</b>（2026-10-17，取代原本不開相機的假掃描）：先跟照片來源拿一張照片，再交給辨識模組。
///
/// 遊戲裡有兩個：一個接拍照畫面、一個接相簿，<b>共用同一個辨識模組</b>（見 <c>LevelController.EnsureScanPipeline</c>）。
///
/// <b>照片誰保管</b>：拿到之後由這一層保管，辨識成功就跟 JSON 一起交出去（之後歸收到的人），
/// 辨識失敗或出例外就在這裡釋放。辨識模組只借看，不釋放。
/// </summary>
public sealed class PhotoContourScanner : IContourScanner
{
    private readonly IPhotoSource photoSource;
    private readonly IContourRecognizer recognizer;

    /// <param name="photoSource">照片從哪來。</param>
    /// <param name="recognizer">要叫的辨識模組。</param>
    /// <exception cref="ArgumentNullException">任何一個是 null。</exception>
    public PhotoContourScanner(IPhotoSource photoSource, IContourRecognizer recognizer)
    {
        this.photoSource = photoSource ?? throw new ArgumentNullException(nameof(photoSource));
        this.recognizer = recognizer ?? throw new ArgumentNullException(nameof(recognizer));
    }

    /// <inheritdoc/>
    public bool IsScanning { get; private set; }

    /// <inheritdoc/>
    public void BeginScan(Action<ContourScanResult> onFinished)
    {
        if (onFinished == null) throw new ArgumentNullException(nameof(onFinished));
        if (IsScanning) throw new InvalidOperationException("上一次掃描還沒回來。");

        IsScanning = true;
        photoSource.Acquire(captured => OnPhotoCaptured(captured, onFinished));
    }

    private void OnPhotoCaptured(PhotoCaptureResult captured, Action<ContourScanResult> onFinished)
    {
        if (captured.Status != ContourScanStatus.Succeeded)
        {
            Finish(onFinished, captured.Status == ContourScanStatus.Cancelled
                ? ContourScanResult.Cancelled()
                : ContourScanResult.Failed(captured.FailureReason, ContourScanFailureStage.Photo));
            return;
        }

        Texture2D photo = captured.Photo;
        try
        {
            recognizer.Recognize(photo, recognized =>
            {
                if (recognized.Status == ContourScanStatus.Succeeded)
                {
                    // 照片用 App 這一側拿到的這張，不用辨識模組附的（約定裡它不用附）
                    Finish(onFinished, ContourScanResult.Succeeded(recognized.ContourJson, photo));
                    return;
                }

                UnityEngine.Object.Destroy(photo);
                Finish(onFinished, recognized.Status == ContourScanStatus.Failed
                    ? recognized
                    : ContourScanResult.Failed("辨識模組回了「取消」，那不在它的約定裡。"));
            });
        }
        catch (Exception exception)
        {
            // 辨識模組第一次接上來時最可能出事的地方。接住，才不會讓掃描永遠停在「還在跑」
            UnityEngine.Object.Destroy(photo);
            Finish(onFinished, ContourScanResult.Failed($"辨識模組丟了例外：{exception}"));
        }
    }

    private void Finish(Action<ContourScanResult> onFinished, ContourScanResult result)
    {
        // ⚠️ 先放掉旗標再轉交：收到結果的人可能馬上又開始下一次掃描
        IsScanning = false;
        onFinished(result);
    }
}

/// <summary>
/// ⭐ <b>照片的替身</b>：用輪廓畫一張跟原圖同尺寸的剪影（背景深灰、輪廓裡白色、不畫描邊）。
///
/// <b>什麼時候用</b>（<c>LevelController.HandPhotoToView</c>）：照片的尺寸跟 JSON 講的不一樣時——
/// 那代表點不在這張照片的座標上，硬疊會整張歪掉，改畫剪影至少跟題目對得上。
/// ⚠️ <b>假的辨識模組期間每一次都會走到這裡</b>（它不管照片、永遠回 320 × 320 的貓），所以那時候看到的永遠是剪影。
///
/// ⭐ 剪影跟 JSON 同一個來源，所以<b>一定要剛好疊在輪廓上——對不準就是換算鏈的 bug</b>（2026-10-14 驗換算鏈就是靠它）。
/// </summary>
public static class ContourStandInPhoto
{
    /// <summary>背景（輪廓外）的顏色。</summary>
    private static readonly Color32 Background = new Color32(64, 64, 64, 255);

    /// <summary>剪影（輪廓裡）的顏色。</summary>
    private static readonly Color32 Silhouette = new Color32(255, 255, 255, 255);

    /// <summary>畫剪影。</summary>
    /// <param name="contour">讀好的輪廓。</param>
    /// <returns>剪影貼圖，呼叫端負責釋放。</returns>
    public static Texture2D Draw(RecognizedContour contour)
    {
        if (contour == null) throw new ArgumentNullException(nameof(contour));

        int width = contour.ImageWidth, height = contour.ImageHeight;
        bool[] mask = contour.SilhouetteMask();
        var pixels = new Color32[width * height];

        // ⚠️ 剪影的第 0 列是照片最上面，Unity 貼圖的第 0 列是最下面——上下對調，照片才是正的
        for (int row = 0; row < height; row++)
            for (int column = 0; column < width; column++)
                pixels[(height - 1 - row) * width + column] = mask[row * width + column] ? Silhouette : Background;

        var texture = new Texture2D(width, height, TextureFormat.RGBA32, false)
        {
            name = "掃描照片替身（JSON 剪影）",
            wrapMode = TextureWrapMode.Clamp,
            filterMode = FilterMode.Bilinear,
        };
        texture.SetPixels32(pixels);
        texture.Apply(false, false);
        return texture;
    }
}
