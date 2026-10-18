using Tangram.Core;
using UnityEditor;
using UnityEngine;

/// <summary>
/// 進度存檔的兩個 Editor 選單（P2-8b，issue #39，2026-10-17）：清除、打開所在的資料夾。
///
/// ⭐ <b>為什麼需要清除</b>：錄影是改 <c>SkillRatingSettings.initialRating</c> 分段錄的（2026-09-22 定），
/// 而<b>有存檔之後改它不會生效</b>——錄出來的起點是上次玩到的實力，不報錯。開場的 log 會說實力從哪裡來。
///
/// ⚠️ <b>Windows 上 Editor 跟 exe 是同一個檔</b>（<c>Application.persistentDataPath</c> 照公司名與產品名決定），
/// 在這裡清除，這台電腦上 exe 的進度也一起沒了。
///
/// App 裡的重置鈕（決賽讓評審從頭玩）牽涉 UI，等決賽腳本再定，見 `待辦清單.md`〈進度存檔〉。
/// </summary>
public static class PlayerProgressMenu
{
    [MenuItem("Window/Tangram/清除進度存檔")]
    private static void Clear()
    {
        var file = new PlayerProgressFile(Application.persistentDataPath);

        string playingNote = EditorApplication.isPlaying
            ? "\n\n⚠️ 現在正在 Play：這一場的實力還在記憶體裡，下一局結算時會再寫一份回去。要從頭開始請先停止 Play。"
            : "";
        bool confirmed = EditorUtility.DisplayDialog(
            "清除進度存檔",
            $"要刪掉玩家的進度（實力與累計局數）嗎？刪了就救不回來。\n\n{file.FilePath}\n\n" +
            "這台電腦上 exe 的進度也是同一個檔。壞檔的備份不會刪。" + playingNote,
            "清除", "取消");
        if (!confirmed) return;

        try
        {
            bool existed = file.Delete();
            Debug.Log(existed
                ? $"進度存檔已清除，下次 Play 從起始實力開始：{file.FilePath}"
                : $"本來就沒有進度存檔：{file.FilePath}");
        }
        catch (System.Exception error) when (error is System.IO.IOException || error is System.UnauthorizedAccessException)
        {
            // exe 正開著的話檔案可能被鎖住
            Debug.LogError($"進度存檔刪不掉（exe 是不是還開著？）：{error.Message}：{file.FilePath}");
        }
    }

    [MenuItem("Window/Tangram/打開進度存檔的資料夾")]
    private static void Reveal()
    {
        // 有存檔就選起存檔；沒有的話選起資料夾本身（Windows 上會打開上一層、把它選起來）
        var file = new PlayerProgressFile(Application.persistentDataPath);
        EditorUtility.RevealInFinder(System.IO.File.Exists(file.FilePath) ? file.FilePath : file.DirectoryPath);
    }
}
