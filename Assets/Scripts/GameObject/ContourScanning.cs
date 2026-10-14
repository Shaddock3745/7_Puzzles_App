using System;
using System.Collections;
using UnityEngine;

// ⭐ 掃描模式的前兩段（2026-10-14）：相機 → 辨識模組 → 輪廓 JSON。
//
//   主畫面「掃描模式」→ IContourScanner（開相機、拍照）→ IContourRecognizer（照片 → JSON）
//                    → RecognizedContour（讀 JSON）→ IContourPuzzleBuilder（出題）→ 進關
//
// 後兩段在 Data 層（RecognizedContour.cs、ContourPuzzleBuilder.cs），測得到；這兩段碰到相機與照片，只能在 Unity 裡跑。
//
// ⭐ 為什麼拆成兩個介面：協作者的模組最後可能是
//   ① ONNX 模型，由我們在 Unity 裡開相機、跑推論 → 兩個介面都由我們實作
//   ② 自帶相機畫面的 Android 元件 → IContourScanner 直接轉給他，IContourRecognizer 用不到
// 兩種都接得上，現在不用先決定。

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

    private ContourScanResult(ContourScanStatus status, string contourJson, string failureReason)
    {
        Status = status;
        ContourJson = contourJson;
        FailureReason = failureReason;
    }

    /// <param name="contourJson">辨識模組交回來的 JSON 原文。</param>
    /// <returns>成功的結果。</returns>
    public static ContourScanResult Succeeded(string contourJson) =>
        new ContourScanResult(ContourScanStatus.Succeeded, contourJson, null);

    /// <returns>玩家取消的結果。</returns>
    public static ContourScanResult Cancelled() =>
        new ContourScanResult(ContourScanStatus.Cancelled, null, null);

    /// <param name="reason">失敗的原因。</param>
    /// <returns>失敗的結果。</returns>
    public static ContourScanResult Failed(string reason) =>
        new ContourScanResult(ContourScanStatus.Failed, null, reason);
}

/// <summary>
/// ⭐ <b>辨識模組的接口</b>：給一張照片，回一份輪廓 JSON。
///
/// <b>為什麼是非同步（回呼）</b>：手機上推論可能要好幾秒，做成同步的話換成真的那天整個畫面會凍住。
///
/// <b>實作要守的約定</b>：
/// <list type="bullet">
/// <item>⚠️ <b>不能在 <see cref="Recognize"/> 裡面當場叫回呼</b>，至少等到下一幀——
///   叫的人還停在「開始辨識」那一行裡就收到「完成、進關」，執行順序跟真的完全不一樣</item>
/// <item><b>回呼只能叫一次，而且要在主執行緒上叫</b>（之後要碰 Unity 的物件）</item>
/// <item>只會回 <see cref="ContourScanStatus.Succeeded"/> 或 <see cref="ContourScanStatus.Failed"/>，
///   取消是拍照那一層的事</item>
/// </list>
/// </summary>
public interface IContourRecognizer
{
    /// <summary>辨識一張照片。</summary>
    /// <param name="photo">拍到的照片。假實作不看，可以是 null。</param>
    /// <param name="onFinished">辨識完叫一次。</param>
    void Recognize(Texture2D photo, Action<ContourScanResult> onFinished);
}

/// <summary>
/// ⭐ <b>呼叫辨識模組的接口</b>：開相機 → 玩家按快門 → 把照片交給辨識模組 → 收結果。
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
/// ⚠️ <b>假的辨識模組</b>：不看照片，永遠回同一份 JSON（<c>Resources/ScanSamples/</c> 裡協作者交的樣本）。
///
/// <b>下一幀才回</b>，照 <see cref="IContourRecognizer"/> 的約定——所以它需要一個 <c>MonoBehaviour</c> 幫它跑協程。
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
/// ⚠️ <b>假的掃描</b>：不開相機，直接叫辨識模組（照片傳 null），結果原樣轉回去。
///
/// 真的相機做好之後換掉它；辨識模組那一層可以照樣沿用。
/// </summary>
public sealed class NoCameraContourScanner : IContourScanner
{
    private readonly IContourRecognizer recognizer;

    /// <param name="recognizer">要叫的辨識模組。</param>
    /// <exception cref="ArgumentNullException"><paramref name="recognizer"/> 是 null。</exception>
    public NoCameraContourScanner(IContourRecognizer recognizer)
    {
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
        recognizer.Recognize(null, result =>
        {
            // ⚠️ 先放掉旗標再轉交：收到結果的人可能馬上又開始下一次掃描
            IsScanning = false;
            onFinished(result);
        });
    }
}
