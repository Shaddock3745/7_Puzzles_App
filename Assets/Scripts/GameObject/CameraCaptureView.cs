using System;
using System.Collections;
using Tangram.Data;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UI;
#if UNITY_ANDROID && !UNITY_EDITOR
using UnityEngine.Android;
#endif

/// <summary>
/// ⭐ <b>新拍一張</b>：App 裡自己做的拍照畫面（2026-10-17）。整個螢幕的相機預覽，加快門與取消兩顆按鈕。
///
/// <b>為什麼不叫系統相機 App</b>（使用者定，甲案）：那要裝 NativeCamera 外掛，而且會切出 Unity——
/// 決賽用的 T20 只有 4 GB RAM，Unity 的 Activity 在背景被系統收掉的話，拍完回來 App 是重開的。
/// 自己做還多一個好處：之後可以在預覽上畫取景框，提示「物體放框裡、背景要乾淨」，對辨識模組也有利。
///
/// <b>一次拍照的生命週期</b>：
/// <list type="number">
/// <item><see cref="Acquire"/>：下一幀才開始（守住「不能當場回」）→ 要相機權限 → 挑鏡頭（後鏡頭優先）→ 開相機、顯示畫面</item>
/// <item>每幀：照相機回報的角度與鏡射轉預覽、等比例塞進畫面。⚠️ <b>相機剛開的前幾幀回報 16 × 16 的假尺寸</b>，
///   尺寸正常了才算準備好，在那之前快門按了沒反應</item>
/// <item><see cref="OnShutterPressed"/>：抓當下那一幀 → 轉正（<see cref="PhotoGeometry.Reorient{T}"/>，跟預覽同一套規則）
///   → 縮到長邊 1024 → <b>關相機</b> → 回照片</item>
/// <item><see cref="OnCancelPressed"/> 或 Android 返回鍵：關相機 → 回取消</item>
/// </list>
///
/// ⚠️ <b>每一條出口都要關相機</b>（包括這支被停用或刪掉）。不關的話相機燈一直亮、一直耗電，而且下次開會失敗。
///
/// <b>場景要做的事</b>：
/// <list type="number">
/// <item>在 Canvas 底下建一個<b>整個螢幕大小、不透明（黑底）</b>的 Panel，<b>排在 Canvas 最後面</b>（蓋過所有東西）</item>
/// <item>Panel 底下放一個 <c>RawImage</c> 當預覽（位置大小不用調，每幀由這支算），再放「拍照」「取消」兩顆 Button，
///   <b>按鈕要排在 RawImage 後面</b>，不然會被預覽蓋住</item>
/// <item>兩顆的 <c>onClick</c> 分別接到 <see cref="OnShutterPressed"/> / <see cref="OnCancelPressed"/></item>
/// <item>⚠️ <b>這支腳本掛在另一個物件上，不能掛在 Panel 身上</b>（除錯記錄 004：藏起來就再也開不回來）</item>
/// <item>把 Panel、RawImage、拍照鈕拖進對應欄位，再把這支拖進 <c>LevelController</c> 的「拍照畫面」欄位</item>
/// <item>Panel 一開始關著或開著都可以，<see cref="Awake"/> 會把它關掉</item>
/// </list>
/// </summary>
[DisallowMultipleComponent]
public class CameraCaptureView : MonoBehaviour, IPhotoSource
{
    [Tooltip("整個拍照畫面的 Panel。⚠️ 不能是這支腳本自己所在的物件。")]
    [SerializeField] private GameObject panel;

    [Tooltip("相機預覽。位置、大小、旋轉每幀由這支算，Inspector 裡調的會被蓋掉。")]
    [SerializeField] private RawImage preview;

    [Tooltip("快門鈕。相機還沒準備好時不能按。")]
    [SerializeField] private Button shutterButton;

    [Tooltip("擋片區的登記對象：拍照畫面開著時不要抓到底下的片。留空的話 Awake 會自己找。")]
    [SerializeField] private TouchInputController inputController;

    [Tooltip("跟相機要的解析度（寬）。相機會挑最接近的，不一定給得到。")]
    [SerializeField] private int requestedWidth = 1280;

    [Tooltip("跟相機要的解析度（高）。")]
    [SerializeField] private int requestedHeight = 720;

    /// <summary>相機剛打開時回報的假尺寸。寬度大於這個才算真的有畫面了。</summary>
    private const int PlaceholderSize = 16;

    /// <summary>開著的相機。沒在拍照時是 null。</summary>
    private WebCamTexture webCamera;

    /// <summary>這一次拍照的回呼。叫過就清掉，保證只叫一次。</summary>
    private Action<PhotoCaptureResult> pending;

    /// <summary>登記進擋片區的那塊矩形，離場時要取消登記。</summary>
    private RectTransform blockingArea;

    /// <inheritdoc/>
    public bool IsBusy => pending != null;

    private void Awake()
    {
        if (inputController == null) inputController = FindFirstObjectByType<TouchInputController>();

        if (panel == null)
            Debug.LogError("CameraCaptureView 的 panel 欄位沒填：「新拍一張」每次都會失敗。", this);
        else if (panel == gameObject)
            Debug.LogError("CameraCaptureView 掛在它自己要開關的 Panel 上了。藏起來之後這支就跟著被停用，" +
                           "再也開不回來（除錯記錄 004）。請把這支移到另一個物件上。", this);
        if (preview == null)
            Debug.LogError("CameraCaptureView 的 preview 欄位沒填：「新拍一張」每次都會失敗。", this);

        if (preview != null)
        {
            // 預覽的位置與大小由這支每幀算，錨點固定在中間，才能直接用 sizeDelta 當大小
            RectTransform rect = preview.rectTransform;
            rect.anchorMin = rect.anchorMax = rect.pivot = new Vector2(0.5f, 0.5f);
            rect.anchoredPosition = Vector2.zero;
        }

        if (panel != null && panel != gameObject) panel.SetActive(false);
    }

    /// <summary>整塊 Panel 登記成擋片區。停用的物件本來就不擋，所以不用跟著顯示開關（跟主畫面同一個做法）。</summary>
    private void OnEnable()
    {
        if (panel == null || inputController == null) return;

        blockingArea = panel.GetComponent<RectTransform>();
        if (blockingArea != null) inputController.RegisterPressBlockingArea(blockingArea);
    }

    private void OnDisable()
    {
        if (inputController != null && blockingArea != null) inputController.UnregisterPressBlockingArea(blockingArea);

        // 拍到一半被停用（換場景、關遊戲）：相機一定要關，回呼也要給一個交代，不然掃描那層永遠以為還在拍
        if (IsBusy) Finish(PhotoCaptureResult.Cancelled());
    }

    /// <inheritdoc/>
    public void Acquire(Action<PhotoCaptureResult> onFinished)
    {
        if (onFinished == null) throw new ArgumentNullException(nameof(onFinished));
        if (IsBusy) throw new InvalidOperationException("上一次拍照還沒回來。");

        pending = onFinished;
        StartCoroutine(OpenCamera());
    }

    private IEnumerator OpenCamera()
    {
        // 第一行就讓出一幀：StartCoroutine 會同步跑到第一個 yield，不讓的話下面「已經有權限、沒有相機」
        // 這類失敗會在 Acquire 還沒 return 時就回呼
        yield return null;

        if (panel == null || panel == gameObject || preview == null)
        {
            Finish(PhotoCaptureResult.Failed("拍照畫面沒接好（看 Console 最前面 CameraCaptureView 的錯誤）。"));
            yield break;
        }

        bool? granted = null;
        yield return RequestCameraPermission(answer => granted = answer);
        if (granted != true)
        {
            Finish(PhotoCaptureResult.Failed("沒有相機權限。"));
            yield break;
        }

        WebCamDevice[] devices = WebCamTexture.devices;
        if (devices.Length == 0)
        {
            Finish(PhotoCaptureResult.Failed("這台裝置找不到相機。"));
            yield break;
        }

        // 後鏡頭優先：拍的是桌上的東西。只有前鏡頭（筆電的 webcam）就用前鏡頭
        string deviceName = devices[0].name;
        foreach (WebCamDevice device in devices)
        {
            if (device.isFrontFacing) continue;
            deviceName = device.name;
            break;
        }

        webCamera = new WebCamTexture(deviceName, requestedWidth, requestedHeight);
        webCamera.Play();
        if (!webCamera.isPlaying)
        {
            Finish(PhotoCaptureResult.Failed($"相機「{deviceName}」打不開（可能被別的程式佔用）。"));
            yield break;
        }

        preview.texture = webCamera;
        if (shutterButton != null) shutterButton.interactable = false;
        panel.SetActive(true);
    }

    /// <summary>
    /// 要相機權限。⚠️ <b>Android 要用 <c>UnityEngine.Android.Permission</c></b>，
    /// <c>Application.RequestUserAuthorization</c> 是給 iOS 與 WebGL 的。CAMERA 權限 Unity 看到 <c>WebCamTexture</c> 會自己加進 manifest。
    /// </summary>
    /// <param name="answer">有沒有拿到權限。</param>
    private static IEnumerator RequestCameraPermission(Action<bool> answer)
    {
#if UNITY_ANDROID && !UNITY_EDITOR
        if (Permission.HasUserAuthorizedPermission(Permission.Camera))
        {
            answer(true);
            yield break;
        }

        bool? result = null;
        var callbacks = new PermissionCallbacks();
        callbacks.PermissionGranted += _ => result = true;
        callbacks.PermissionDenied += _ => result = false;
        callbacks.PermissionRequestDismissed += _ => result = false;    // 玩家沒選就把對話框關掉
        Permission.RequestUserPermission(Permission.Camera, callbacks);

        while (result == null) yield return null;
        answer(result.Value);
#else
        yield return Application.RequestUserAuthorization(UserAuthorization.WebCam);
        answer(Application.HasUserAuthorization(UserAuthorization.WebCam));
#endif
    }

    private void Update()
    {
        if (webCamera == null) return;

        bool ready = webCamera.width > PlaceholderSize;
        if (shutterButton != null) shutterButton.interactable = ready;
        if (ready) LayoutPreview();

        // Android 的返回鍵在新 Input System 裡是鍵盤的 Esc；電腦上按 Esc 也一樣取消
        if (Keyboard.current != null && Keyboard.current.escapeKey.wasPressedThisFrame) OnCancelPressed();
    }

    /// <summary>
    /// 照相機回報的角度與鏡射擺預覽，再等比例塞進 Panel。
    ///
    /// ⭐ <b>跟拍下來的照片同一套規則</b>：<c>localScale.y = -1</c> 是上下鏡射、<c>localEulerAngles.z = −角度</c> 是順時針轉，
    /// 而 Unity 是先縮放再旋轉——正好是 <see cref="PhotoGeometry.Reorient{T}"/> 的「先鏡射再轉」。
    /// 所以<b>預覽看起來是正的，拍下來的就是正的</b>；預覽歪了就是參數的正負號要改，兩邊一起改。
    /// </summary>
    private void LayoutPreview()
    {
        int angle = webCamera.videoRotationAngle;
        bool mirrored = webCamera.videoVerticallyMirrored;
        bool sideways = (angle / 90) % 2 != 0;

        RectTransform rect = preview.rectTransform;
        rect.localEulerAngles = new Vector3(0f, 0f, -angle);
        rect.localScale = new Vector3(1f, mirrored ? -1f : 1f, 1f);

        // 轉 90 度時「看起來的寬高」是對調的，塞進畫面要拿看起來的那組比
        float width = webCamera.width, height = webCamera.height;
        float shownWidth = sideways ? height : width;
        float shownHeight = sideways ? width : height;

        Rect area = ((RectTransform)rect.parent).rect;
        float scale = Mathf.Min(area.width / shownWidth, area.height / shownHeight);

        // sizeDelta 是轉之前的大小，所以給原本的寬高
        rect.sizeDelta = new Vector2(width * scale, height * scale);
    }

    /// <summary>「拍照」被按下。接在 Unity <c>Button.onClick</c> 上。</summary>
    public void OnShutterPressed()
    {
        if (webCamera == null || webCamera.width <= PlaceholderSize) return;

        int width = webCamera.width, height = webCamera.height;
        (Color32[] pixels, int uprightWidth, int uprightHeight) = PhotoGeometry.Reorient(
            webCamera.GetPixels32(), width, height, webCamera.videoRotationAngle, webCamera.videoVerticallyMirrored);

        var upright = new Texture2D(uprightWidth, uprightHeight, TextureFormat.RGBA32, false);
        upright.SetPixels32(pixels);
        upright.Apply(false, false);

        // 1280 × 720 也要縮到 1024：兩種來源交出去的照片守同一份約定
        Texture2D photo = PhotoTextures.ShrinkToFit(upright);
        Destroy(upright);

        Finish(PhotoCaptureResult.Succeeded(photo));
    }

    /// <summary>「取消」被按下。接在 Unity <c>Button.onClick</c> 上。</summary>
    public void OnCancelPressed()
    {
        if (IsBusy) Finish(PhotoCaptureResult.Cancelled());
    }

    /// <summary>
    /// 收尾的唯一出口：關相機、藏畫面、回呼。<b>回呼先從欄位拿下來再叫</b>——收到的人可能馬上又開始下一次拍照，
    /// 那時 <see cref="IsBusy"/> 必須已經是 false。
    /// </summary>
    private void Finish(PhotoCaptureResult result)
    {
        StopAllCoroutines();
        StopCamera();
        if (panel != null && panel != gameObject) panel.SetActive(false);

        Action<PhotoCaptureResult> callback = pending;
        pending = null;
        callback?.Invoke(result);
    }

    private void StopCamera()
    {
        if (webCamera == null) return;

        if (preview != null) preview.texture = null;
        webCamera.Stop();
        Destroy(webCamera);
        webCamera = null;
    }
}
