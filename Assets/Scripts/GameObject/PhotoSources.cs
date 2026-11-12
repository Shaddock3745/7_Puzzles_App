using System;
using System.Collections;
using Tangram.Data;
using UnityEngine;

// ⭐ 掃描模式的第一段：取得照片（2026-10-17）。
//
//   選擇視窗 ─┬ 新拍一張   → CameraCaptureView（自己做的拍照畫面，WebCamTexture）─┐
//            └ 從檔案選擇 → GalleryPhotoSource（NativeGallery 外掛）            ─┴→ 照片 → 辨識模組
//
// 兩種來源交出來的照片守同一份約定（見 IPhotoSource），所以辨識模組不用知道照片是哪來的。

/// <summary>選擇視窗上的兩個來源。</summary>
public enum ScanPhotoSource
{
    /// <summary>新拍一張：開 App 裡的拍照畫面。</summary>
    Camera,

    /// <summary>從檔案選擇：開系統相簿（Editor 裡是檔案視窗）。</summary>
    Gallery
}

/// <summary>
/// 一次取得照片的結果。三種下場跟 <see cref="ContourScanResult"/> 共用同一個列舉。
/// </summary>
public readonly struct PhotoCaptureResult
{
    /// <summary>下場。</summary>
    public readonly ContourScanStatus Status;

    /// <summary>
    /// 照片。<b>只有 <see cref="ContourScanStatus.Succeeded"/> 時有值</b>，約定見 <see cref="IPhotoSource"/>。
    /// ⚠️ 執行時建的貼圖，<b>收到的人負責釋放</b>。
    /// </summary>
    public readonly Texture2D Photo;

    /// <summary>失敗的原因。<b>只有 <see cref="ContourScanStatus.Failed"/> 時有值</b>。</summary>
    public readonly string FailureReason;

    private PhotoCaptureResult(ContourScanStatus status, Texture2D photo, string failureReason)
    {
        Status = status;
        Photo = photo;
        FailureReason = failureReason;
    }

    /// <param name="photo">照片，不能是 null。</param>
    /// <returns>成功的結果。</returns>
    /// <exception cref="ArgumentNullException"><paramref name="photo"/> 是 null。</exception>
    public static PhotoCaptureResult Succeeded(Texture2D photo) =>
        new PhotoCaptureResult(ContourScanStatus.Succeeded, photo ? photo : throw new ArgumentNullException(nameof(photo)), null);

    /// <returns>玩家取消的結果。</returns>
    public static PhotoCaptureResult Cancelled() => new PhotoCaptureResult(ContourScanStatus.Cancelled, null, null);

    /// <param name="reason">失敗的原因。</param>
    /// <returns>失敗的結果。</returns>
    public static PhotoCaptureResult Failed(string reason) => new PhotoCaptureResult(ContourScanStatus.Failed, null, reason);
}

/// <summary>
/// ⭐ <b>照片來源</b>：「給我一張照片」。
///
/// <b>交出來的照片一定</b>（這是辨識模組那邊可以依賴的約定，見 <see cref="IContourRecognizer"/>）：
/// <list type="bullet">
/// <item><b>已經轉正</b>——相機畫面的旋轉與鏡射、相簿照片的 EXIF 方向都處理掉了</item>
/// <item><b>讀得到像素</b>（<c>isReadable</c>），格式 RGBA32，沒有 mipmap</item>
/// <item><b>長邊不超過 <see cref="PhotoTextures.MaximumLongSide"/></b>——手機原圖 1200 萬像素，一張就是 48 MB</item>
/// </list>
///
/// 約定跟 <see cref="IContourScanner"/> 一樣：<b>不能當場回</b>（至少等到下一幀）、回呼只叫一次、在主執行緒上叫。
/// </summary>
public interface IPhotoSource
{
    /// <summary>是不是有一次還沒回來。</summary>
    bool IsBusy { get; }

    /// <summary>開始取得一張照片。</summary>
    /// <param name="onFinished">拿到照片、玩家取消、或失敗時叫一次。</param>
    /// <exception cref="InvalidOperationException">上一次還沒回來（<see cref="IsBusy"/> 是 true）。</exception>
    void Acquire(Action<PhotoCaptureResult> onFinished);
}

/// <summary>照片貼圖的共用工具：縮圖。</summary>
public static class PhotoTextures
{
    /// <summary>
    /// 照片長邊的上限。<b>1024 是先抓的數字</b>：U²-Net 的輸入是 320，照片對照只佔盤面的一塊，
    /// 再大只是多吃記憶體。之後要改就改這裡，兩種來源一起變。
    /// </summary>
    public const int MaximumLongSide = 1024;

    /// <summary>
    /// 縮成長邊不超過 <see cref="MaximumLongSide"/> 的新貼圖（讀得到像素、RGBA32、沒有 mipmap）。
    /// <b>本來就夠小的也會複製一份</b>——這樣不管哪條路，回來的都是同一種貼圖，而且原本那張一律由呼叫端釋放。
    ///
    /// <b>用 GPU 縮</b>（畫到一張暫時的 RenderTexture 再讀回來），原圖不用讀得到像素。
    /// ⚠️ 那張 RenderTexture 刻意指定 sRGB：專案是 Linear 色彩空間，取樣時 sRGB → 線性、寫回時線性 → sRGB，
    /// 讀回來的位元組才跟原圖同一套。用預設值的話照片會整張變暗或變亮，而且不報錯。
    /// </summary>
    /// <param name="source">原圖。不會被釋放。</param>
    /// <returns>新的貼圖，呼叫端負責釋放。</returns>
    public static Texture2D ShrinkToFit(Texture source)
    {
        if (source == null) throw new ArgumentNullException(nameof(source));

        (int width, int height) = PhotoGeometry.FitWithin(source.width, source.height, MaximumLongSide);

        RenderTexture target = RenderTexture.GetTemporary(width, height, 0, RenderTextureFormat.ARGB32, RenderTextureReadWrite.sRGB);
        RenderTexture previous = RenderTexture.active;
        try
        {
            Graphics.Blit(source, target);
            RenderTexture.active = target;

            var result = new Texture2D(width, height, TextureFormat.RGBA32, false)
            {
                name = "掃描照片",
                wrapMode = TextureWrapMode.Clamp,
                filterMode = FilterMode.Bilinear,
            };
            result.ReadPixels(new Rect(0, 0, width, height), 0, 0, false);
            result.Apply(false, false);     // 第二個 false：保留 CPU 那份，辨識模組要讀像素
            return result;
        }
        finally
        {
            RenderTexture.active = previous;
            RenderTexture.ReleaseTemporary(target);
        }
    }
}

/// <summary>
/// ⭐ <b>從檔案選擇</b>：用 NativeGallery 外掛開系統相簿（2026-10-17）。
///
/// <b>為什麼要外掛</b>：Unity 執行中完全沒有選檔案的功能（<c>EditorUtility.OpenFilePanel</c> 只在 Editor 裡有）。
/// NativeGallery 是 yasirkula 寫的、MIT 授權，從 <c>Packages/manifest.json</c> 用 git 網址裝，釘在 v1.9.5。
///
/// <b>平台的下場</b>：
/// <list type="bullet">
/// <item><b>Android</b>：系統相簿。讀圖時外掛會照 EXIF 轉正、而且先縮到長邊 1024</item>
/// <item>⭐ <b>iOS／iPadOS</b>（2026-10-23 打開）：跟 Android 同一條路。外掛在 iOS 14 以上用系統的 <c>PHPickerViewController</c>，
///   <b>不需要相簿權限</b>（外掛把 iOS 固定成 permission-free 模式）；讀圖時一樣照 EXIF 轉正、縮到長邊（原生那半做的）。
///   ⚠️ 專案的最低版本是 15.0，所以碰不到 iOS 13 以下那條要權限的舊選擇器。相簿說明字串之類的設定在
///   <b>Project Settings → yasirkula → Native Gallery</b>，由打包的人調</item>
/// <item><b>Editor</b>：外掛自己會開檔案視窗。⚠️ <b>這條不轉 EXIF、也不縮</b>（外掛只在手機上做這兩件），
///   縮圖由這支補上；EXIF 只有 Editor 會碰到，接受</item>
/// <item><b>桌機版 exe 與其他</b>：回失敗。回饋收集版用不到掃描模式，不值得為它另寫 Windows 的檔案視窗</item>
/// </list>
///
/// ⚠️ <b>相簿權限被拒時外掛回的跟「沒選」一樣</b>（路徑是 null），所以玩家拒絕權限在這裡看起來是取消，回主畫面而不報錯。
/// Android 13 以後選照片走系統的選擇器、不需要權限，T20 應該碰不到。
/// </summary>
public sealed class GalleryPhotoSource : IPhotoSource
{
    private readonly MonoBehaviour coroutineHost;

    /// <param name="coroutineHost">幫忙跑協程的物件。⚠️ 它被停用的話回呼就不會來。</param>
    /// <exception cref="ArgumentNullException"><paramref name="coroutineHost"/> 是 null。</exception>
    public GalleryPhotoSource(MonoBehaviour coroutineHost)
    {
        this.coroutineHost = coroutineHost ? coroutineHost : throw new ArgumentNullException(nameof(coroutineHost));
    }

    /// <inheritdoc/>
    public bool IsBusy { get; private set; }

    /// <inheritdoc/>
    public void Acquire(Action<PhotoCaptureResult> onFinished)
    {
        if (onFinished == null) throw new ArgumentNullException(nameof(onFinished));
        if (IsBusy) throw new InvalidOperationException("上一次選照片還沒回來。");

        IsBusy = true;
        coroutineHost.StartCoroutine(Pick(onFinished));
    }

    private IEnumerator Pick(Action<PhotoCaptureResult> onFinished)
    {
#if UNITY_EDITOR || UNITY_ANDROID || UNITY_IOS
        bool answered = false;
        string path = null;

        // ⚠️ Editor 裡這一行會當場跳出檔案視窗、當場叫回呼；Android 與 iOS 上要等玩家選完、外掛轉回主執行緒才叫。
        // 兩種都只是把答案記下來，下面一律等到下一幀以後才往外回——守住「不能當場回」
        NativeGallery.GetImageFromGallery(pickedPath =>
        {
            path = pickedPath;
            answered = true;
        }, "選一張照片");

        yield return null;
        while (!answered) yield return null;

        if (path == null)
        {
            Finish(onFinished, PhotoCaptureResult.Cancelled());
            yield break;
        }

        Texture2D loaded = null;
        string failure = null;
        try
        {
            // 讀得到像素、不要 mipmap：縮圖要讀它，而且 mipmap 對一張只用一次的照片是浪費
            loaded = NativeGallery.LoadImageAtPath(path, PhotoTextures.MaximumLongSide,
                                                   markTextureNonReadable: false, generateMipmaps: false);
            if (loaded == null) failure = $"這個檔案讀不出圖片：{path}";
        }
        catch (Exception exception)
        {
            failure = $"讀照片時出事了（{path}）：{exception.Message}";
        }

        if (failure != null)
        {
            Finish(onFinished, PhotoCaptureResult.Failed(failure));
            yield break;
        }

        // 手機上外掛已經縮過了，這裡還是走一次：格式統一成 RGBA32（jpg 讀進來是 RGB24），Editor 那條也才有縮
        Texture2D photo = PhotoTextures.ShrinkToFit(loaded);
        UnityEngine.Object.Destroy(loaded);
        Finish(onFinished, PhotoCaptureResult.Succeeded(photo));
#else
        yield return null;
        Finish(onFinished, PhotoCaptureResult.Failed("這個版本不支援從檔案選擇（只有 Android、iOS 與 Editor 可以）。"));
#endif
    }

    private void Finish(Action<PhotoCaptureResult> onFinished, PhotoCaptureResult result)
    {
        // 先放掉旗標再轉交：收到結果的人可能馬上又要一張
        IsBusy = false;
        onFinished(result);
    }
}
