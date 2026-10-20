using System;
using System.Collections;
using System.Diagnostics;
using Unity.InferenceEngine;
using UnityEngine;
using Debug = UnityEngine.Debug;

/// <summary>
/// 照片 → U2Net 前景 → 最大外框 JSON。只實作辨識，不處理選圖、出題或照片顯示。
/// 模型放 Resources/u2netp.onnx；推論套件沿用已實測分支的 2.6.1。
///
/// ⭐ <b>不凍畫面</b>（2026-10-20，P5-6 #46）：排程<b>分幀</b>（每幀送 <see cref="LayersPerFrame"/> 層），
/// 送完再送非同步的讀回請求、<b>每幀問一次好了沒</b>。
///
/// 為什麼兩件都要（Xperia S8 Gen 3 實測）：
/// <list type="bullet">
/// <item>CPU 這邊送指令幾乎不花時間（259 層 0.05 秒），時間全在 GPU 做完——剛裝好的第一次 4.1 秒、之後 0.3 秒</item>
/// <item>只改讀回、一口氣送 259 層的時候：主執行緒是讓出去了，但<b>整批推論把 GPU 佔滿，畫面畫不出來</b>——
///   4.1 秒只過了 4 幀（每幀 1 秒），等於沒改。<b>分幀送，GPU 在兩批之間才有空畫畫面</b></item>
/// </list>
/// ⚠️ Editor 的第一次另外慢在 CPU 這邊（當場編 compute shader，6～14 秒），分幀之後會變成一頓一頓，只影響開發。
/// ⚠️ 決賽用的 T20 比 Xperia 弱很多，<see cref="LayersPerFrame"/> 要在 T20 上看 log 調。
/// </summary>
public sealed class U2NetContourRecognizer : IContourRecognizer
{
    /// <summary>
    /// 排程時每幀送幾層（模型共 259 層 → 8 層約 33 幀，正常情況多等約 0.25 秒）。
    ///
    /// 用層數不用時間當額度：CPU 送指令幾乎不花時間，真正花時間的是 GPU 做那幾層，CPU 這邊量不到。
    /// <b>怎麼調</b>：看 log 的「排程」與「讀回」兩段，秒數 ÷ 幀數就是那段期間每幀多長。
    /// 每幀還是很長 → 調小（GPU 每批的工作少一點）；很順但整體太久 → 調大。T20 上應該要比 Xperia 小。
    /// </summary>
    private const int LayersPerFrame = 8;

    /// <summary>
    /// 等讀回最多等幾秒，超過就當失敗。正常情況用不到（最慢的是剛裝好的第一次），
    /// 防的是 GPU 出事、請求永遠不完成——沒有這條的話等待遮罩會永遠蓋著，玩家只能關 App。
    /// </summary>
    private const double ReadbackTimeoutSeconds = 60;

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

    /// <summary>
    /// 一次辨識，分五段：① 準備 ② 分幀排程 ③ 送讀回請求 ④ 每幀等讀回 ⑤ 拿結果、遮罩轉輪廓。
    ///
    /// 為什麼拆這麼細：C# 不准在有 <c>catch</c> 的 <c>try</c> 裡 <c>yield</c>，
    /// 所以每段各自接例外，讓出幀的那一行一律在 <c>try</c> 外面。Worker 與輸入張量要活過整個過程，
    /// 由外層的 <c>finally</c> 釋放（<c>try</c>／<c>finally</c> 裡可以 <c>yield</c>）。
    ///
    /// ⚠️ 等讀回的期間宿主被銷毀（換場景）的話，協程直接停掉、<c>finally</c> 不會跑，
    /// 這一次的 GPU 緩衝區會漏掉。只會發生在離開遊戲場景時，先接受。
    /// </summary>
    private IEnumerator Run(Texture2D photo, Action<ContourScanResult> onFinished)
    {
        yield return null;
        // 還沒有結果時是 null；哪一段失敗就在那一段填上 Failed，後面的段看到有值就跳過
        ContourScanResult? result = null;
        var timer = Stopwatch.StartNew();
        // ⏱️ 分段計時（2026-10-20）：每段是「從上一段結束到這段結束」，加起來等於總秒數。
        // 留著是為了 T20：那台的數字決定排程要不要也分幀
        var stageTimes = new System.Text.StringBuilder();
        double previousStageEnd = 0;
        void MarkStage(string stageName)
        {
            double now = timer.Elapsed.TotalSeconds;
            stageTimes.Append($"\n  {stageName}：{now - previousStageEnd:F3} 秒");
            previousStageEnd = now;
        }
        ContourScanResult Fail(Exception exception)
        {
            Debug.LogWarning($"U2Net 辨識失敗：{exception.Message}");
            return ContourScanResult.Failed(exception.Message);
        }

        Worker worker = null;
        Tensor<float> input = null;
        try
        {
            Tensor<float> output = null;
            U2NetInputGeometry geometry = default;
            IEnumerator schedule = null;

            // ① 準備：載模型、建 Worker、照片轉成輸入
            try
            {
                if (photo == null || !photo.isReadable)
                    throw new InvalidOperationException("照片不存在或無法讀取像素。");
                ModelAsset asset = Resources.Load<ModelAsset>("u2netp");
                if (asset == null)
                    throw new InvalidOperationException("找不到 Resources/u2netp.onnx 模型，請確認完整模型已成功匯入。");
                Model model = ModelLoader.Load(asset);
                MarkStage("載模型");
                geometry = new U2NetInputGeometry(photo.width, photo.height);
                // 無 Compute Shader 的裝置使用 CPU
                BackendType backend = SystemInfo.supportsComputeShaders ? BackendType.GPUCompute : BackendType.CPU;
                worker = new Worker(model, backend);
                MarkStage($"建 Worker（{backend}）");
                input = new Tensor<float>(new TensorShape(1, 3, 320, 320), geometry.CreateInput(photo));
                MarkStage("前處理（照片縮成 320×320 輸入）");
                schedule = worker.ScheduleIterable(input);
            }
            catch (Exception exception)
            {
                result = Fail(exception);
            }

            // ② 分幀排程：每幀送 LayersPerFrame 層就讓出這一幀，GPU 在兩批之間才有空畫畫面。
            // MoveNext 會丟例外（例如顯示卡記憶體不夠），所以每一批各自接；yield 在 try 外面
            if (result == null)
            {
                int framesScheduling = 0;
                while (true)
                {
                    bool finished = false;
                    try
                    {
                        for (int i = 0; i < LayersPerFrame; i++)
                        {
                            if (!schedule.MoveNext())
                            {
                                finished = true;
                                break;
                            }
                        }
                    }
                    catch (Exception exception)
                    {
                        result = Fail(exception);
                        break;
                    }
                    if (finished) break;
                    framesScheduling++;
                    yield return null;
                }
                MarkStage($"排程（每幀 {LayersPerFrame} 層，分了 {framesScheduling} 幀）");
            }

            // ③ 送出讀回請求。非同步：GPU 那邊排在所有層後面，算完才會開始搬；CPU 後端則是等推論的工作做完
            if (result == null)
            {
                try
                {
                    // PeekOutput 是 Worker 擁有的張量，不能自行 Dispose；Worker 釋放時一起釋放
                    output = worker.PeekOutput() as Tensor<float>;
                    if (output == null) throw new InvalidOperationException("無法取得 U2Net 的前景遮罩。");
                    output.ReadbackRequest();
                }
                catch (Exception exception)
                {
                    result = Fail(exception);
                }
            }

            // ④ 每幀問一次好了沒，等的期間讓出這一幀
            if (result == null)
            {
                int framesWaited = 0;
                double waitStart = timer.Elapsed.TotalSeconds;
                while (!output.IsReadbackRequestDone())
                {
                    if (timer.Elapsed.TotalSeconds - waitStart > ReadbackTimeoutSeconds)
                    {
                        result = Fail(new TimeoutException($"等 GPU 結果超過 {ReadbackTimeoutSeconds} 秒。"));
                        break;
                    }
                    framesWaited++;
                    yield return null;
                }
                MarkStage($"讀回（等 GPU 算完＋搬回 CPU，等了 {framesWaited} 幀）");
            }

            // ⑤ 拿結果、遮罩轉輪廓。請求已經完成，DownloadToArray 只是複製、不會再等
            if (result == null)
            {
                try
                {
                    float[] probabilities = output.DownloadToArray();
                    Texture2D mask = null;
                    string json;
                    try
                    {
                        mask = geometry.CreateMask(probabilities);
                        json = U2NetMaskContour.CreateJson(mask, photo.width, photo.height);
                    }
                    finally
                    {
                        if (mask != null) UnityEngine.Object.Destroy(mask);
                    }
                    MarkStage("後處理（遮罩 → 輪廓 JSON）");
                    Debug.Log($"U2Net 照片轉 JSON 完成：{photo.width}×{photo.height}，{timer.Elapsed.TotalSeconds:F2} 秒。{stageTimes}");
                    result = ContourScanResult.Succeeded(json);
                }
                catch (Exception exception)
                {
                    result = Fail(exception);
                }
            }
        }
        finally
        {
            input?.Dispose();
            worker?.Dispose();
        }

        // 回呼放在 try 外面：下游例外不能造成第二次回呼。
        onFinished(result.Value);
    }
}
