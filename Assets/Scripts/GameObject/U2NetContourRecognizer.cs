using System;
using System.Collections;
using System.Diagnostics;
using Unity.InferenceEngine;
using UnityEngine;
using Debug = UnityEngine.Debug;

/// <summary>
/// 照片 → U2Net 前景 → 最大外框 JSON。只實作辨識，不處理選圖、出題或照片顯示。
/// 模型放 Resources/u2netp.onnx；推論套件沿用已實測分支的 2.6.1。
/// </summary>
public sealed class U2NetContourRecognizer : IContourRecognizer
{
    private readonly MonoBehaviour coroutineHost;

    /// <summary>由 LevelController 提供協程宿主，讓所有回呼都在主執行緒執行。</summary>
    public U2NetContourRecognizer(MonoBehaviour coroutineHost)
    {
        if (coroutineHost == null) throw new ArgumentNullException(nameof(coroutineHost));
        this.coroutineHost = coroutineHost;
    }

    /// <summary>即使失敗也至少等下一幀才通知；不修改或釋放呼叫者的照片。</summary>
    public void Recognize(Texture2D photo, Action<ContourScanResult> onFinished)
    {
        if (onFinished == null) throw new ArgumentNullException(nameof(onFinished));
        coroutineHost.StartCoroutine(Run(photo, onFinished));
    }

    private IEnumerator Run(Texture2D photo, Action<ContourScanResult> onFinished)
    {
        yield return null;
        ContourScanResult result;
        var timer = Stopwatch.StartNew();
        try
        {
            if (photo == null || !photo.isReadable)
                throw new InvalidOperationException("照片不存在或無法讀取像素。");
            ModelAsset asset = Resources.Load<ModelAsset>("u2netp");
            if (asset == null)
                throw new InvalidOperationException("找不到 Resources/u2netp.onnx 模型，請確認完整模型已成功匯入。");
            var geometry = new U2NetInputGeometry(photo.width, photo.height);
            // 無 Compute Shader 的裝置使用 CPU；這一版先接通流程，速度需於實機量測。
            BackendType backend = SystemInfo.supportsComputeShaders ? BackendType.GPUCompute : BackendType.CPU;
            string json;
            using (var worker = new Worker(ModelLoader.Load(asset), backend))
            using (var input = new Tensor<float>(new TensorShape(1, 3, 320, 320), geometry.CreateInput(photo)))
            {
                worker.Schedule(input);
                // PeekOutput 是 Worker 擁有的張量，不能自行 Dispose；離開 using 時由 Worker 釋放。
                var output = worker.PeekOutput() as Tensor<float>;
                if (output == null) throw new InvalidOperationException("無法取得 U2Net 的前景遮罩。");
                float[] probabilities = output.DownloadToArray();
                Texture2D mask = null;
                try
                {
                    mask = geometry.CreateMask(probabilities);
                    json = U2NetMaskContour.CreateJson(mask, photo.width, photo.height);
                }
                finally
                {
                    if (mask != null) UnityEngine.Object.Destroy(mask);
                }
            }
            Debug.Log($"U2Net 照片轉 JSON 完成：{photo.width}×{photo.height}，{timer.Elapsed.TotalSeconds:F2} 秒。");
            result = ContourScanResult.Succeeded(json);
        }
        catch (Exception exception)
        {
            Debug.LogWarning($"U2Net 辨識失敗：{exception.Message}");
            result = ContourScanResult.Failed(exception.Message);
        }
        // 回呼放在 try/catch 外面：下游例外不能造成第二次回呼。
        onFinished(result);
    }
}
