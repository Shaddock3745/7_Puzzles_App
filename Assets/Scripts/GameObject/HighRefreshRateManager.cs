using UnityEngine;

public class HighRefreshRateManager : MonoBehaviour
{
    void Awake()
    {
        // 避免被場景切換銷毀
        DontDestroyOnLoad(gameObject);

        // 關閉 Unity 的 VSync 計數（行動裝置設為 0）
        QualitySettings.vSyncCount = 0;

        // 鎖定目標幀率為 120
        Application.targetFrameRate = 120;

        // 找出裝置支援的最高更新率並切換（防止 Android 預設停在 60Hz）
        Resolution highestRate = Screen.currentResolution;
        foreach (var res in Screen.resolutions)
        {
#if UNITY_2022_2_OR_NEWER
            if (res.refreshRateRatio.value > highestRate.refreshRateRatio.value)
                highestRate = res;
#else
            if (res.refreshRate > highestRate.refreshRate)
                highestRate = res;
#endif
        }

#if UNITY_2022_2_OR_NEWER
        Screen.SetResolution(highestRate.width, highestRate.height, FullScreenMode.FullScreenWindow, highestRate.refreshRateRatio);
#else
        Screen.SetResolution(highestRate.width, highestRate.height, true, highestRate.refreshRate);
#endif
    }
}