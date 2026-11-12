using System;
using System.Globalization;
using System.IO;
using System.Text;
using Tangram.Data;

namespace Tangram.Core
{
    /// <summary>讀進度存檔的結果是哪一種。</summary>
    public enum PlayerProgressLoadStatus
    {
        /// <summary>沒有存檔：第一次玩（或剛清除過）。<b>不是錯誤。</b></summary>
        NoFile,

        /// <summary>讀到了、內容合法。</summary>
        Loaded,

        /// <summary>
        /// 有檔案但讀不懂：不是 JSON、版本不認得、欄位不合法，或根本讀不出來。
        /// 壞檔已經改名留著（改得動的話），呼叫端應該報錯並從起始實力開始。
        /// </summary>
        Corrupt,
    }

    /// <summary>讀進度存檔的結果。</summary>
    public readonly struct PlayerProgressLoadResult
    {
        /// <summary>是哪一種結果。</summary>
        public readonly PlayerProgressLoadStatus Status;

        /// <summary>讀到的進度；只有 <see cref="PlayerProgressLoadStatus.Loaded"/> 時不是 null。</summary>
        public readonly PlayerProgress Progress;

        /// <summary>壞在哪裡；只有 <see cref="PlayerProgressLoadStatus.Corrupt"/> 時不是 null。</summary>
        public readonly string Problem;

        /// <summary>
        /// 壞檔改名之後的完整路徑。<b>改名失敗時是 null</b>——那時壞檔還在原位，下一次存檔會把它蓋掉。
        /// </summary>
        public readonly string CorruptBackupPath;

        /// <param name="status">結果的種類。</param>
        /// <param name="progress">讀到的進度。</param>
        /// <param name="problem">壞在哪裡。</param>
        /// <param name="corruptBackupPath">壞檔改名之後的路徑。</param>
        public PlayerProgressLoadResult(PlayerProgressLoadStatus status, PlayerProgress progress,
                                        string problem, string corruptBackupPath)
        {
            Status = status;
            Progress = progress;
            Problem = problem;
            CorruptBackupPath = corruptBackupPath;
        }
    }

    /// <summary>
    /// <b>進度存檔的讀、寫、清除</b>（issue #39，2026-10-17）。內容合不合法是 <see cref="PlayerProgress"/> 的事，這支只管檔案。
    ///
    /// <b>為什麼放 Core、不直接寫在 <c>LevelController</c> 裡</b>：壞檔怎麼辦是這部分最容易寫錯、也最難在實機上製造的情況
    /// （要真的弄出一個截斷的檔案）。這支只吃一個資料夾路徑、不碰 Unity，所以 <c>dotnet test</c> 用暫存資料夾就驗得到。
    /// 遊戲裡傳的是 <c>Application.persistentDataPath</c>。
    ///
    /// ⭐ <b>三條規則</b>：
    /// <list type="number">
    /// <item><b>寫檔先寫 <c>.tmp</c>，寫完才換掉正本</b>。手機在寫到一半時被系統殺掉，正本還是上一版的完整內容。
    ///   讀的時候<b>不看 <c>.tmp</c></b>——留下來的 <c>.tmp</c> 就是那次沒寫完的，下一次存檔會蓋掉它</item>
    /// <item><b>壞檔改名留著，不刪</b>（<c>PlayerProgress.corrupt-時間.json</c>）。玩家的實力是唯一一份，
    ///   刪了就查不到為什麼壞；改名之後下一次存檔寫的是新的正本，不會再撞到它</item>
    /// <item><b>寫檔失敗丟例外，讀檔不丟</b>。寫檔失敗由呼叫端報錯、遊戲照常玩、下一局再試；
    ///   讀檔的每一種失敗都已經是一個「結果」，呼叫端照結果走就好</item>
    /// </list>
    /// </summary>
    public sealed class PlayerProgressFile
    {
        /// <summary>存檔的檔名。</summary>
        public const string FileName = "PlayerProgress.json";

        /// <summary>寫到一半的暫存檔的副檔名（加在正本檔名後面）。</summary>
        private const string TemporarySuffix = ".tmp";

        /// <summary>存檔所在的資料夾。</summary>
        public readonly string DirectoryPath;

        /// <summary>存檔的完整路徑。</summary>
        public string FilePath => Path.Combine(DirectoryPath, FileName);

        /// <summary>暫存檔的完整路徑。</summary>
        private string TemporaryFilePath => FilePath + TemporarySuffix;

        /// <param name="directory">存檔所在的資料夾。不存在沒關係，第一次存檔時會建出來。</param>
        /// <exception cref="ArgumentException">路徑是空的。</exception>
        public PlayerProgressFile(string directory)
        {
            if (string.IsNullOrEmpty(directory))
                throw new ArgumentException("PlayerProgressFile：資料夾路徑是空的", nameof(directory));
            DirectoryPath = directory;
        }

        /// <summary>
        /// 讀存檔。<b>不丟例外</b>：沒有檔、讀到了、壞掉三種都回結果。壞掉的話會順手把壞檔改名留著。
        /// </summary>
        /// <param name="nowUtc">現在的時間，拿來給壞檔取名。由呼叫端傳，測試才能固定。</param>
        /// <returns>讀的結果。</returns>
        public PlayerProgressLoadResult Load(DateTime nowUtc)
        {
            if (!File.Exists(FilePath))
                return new PlayerProgressLoadResult(PlayerProgressLoadStatus.NoFile, null, null, null);

            string problem;
            try
            {
                string json = File.ReadAllText(FilePath, Encoding.UTF8);
                PlayerProgress progress = PlayerProgress.FromJson(json);
                return new PlayerProgressLoadResult(PlayerProgressLoadStatus.Loaded, progress, null, null);
            }
            catch (ArgumentException error)
            {
                problem = error.Message;
            }
            catch (Exception error) when (error is IOException || error is UnauthorizedAccessException)
            {
                // 讀不出來（被別的程式鎖住、沒有權限）。一樣當壞檔：不處理的話下一次存檔照樣會蓋掉它，
                // 至少先試著改名留一份
                problem = $"檔案讀不出來：{error.Message}";
            }

            string backupPath = TryMoveAside(nowUtc);
            return new PlayerProgressLoadResult(PlayerProgressLoadStatus.Corrupt, null, problem, backupPath);
        }

        /// <summary>
        /// 寫存檔：先寫 <c>.tmp</c>，寫完才換掉正本。資料夾不存在就建出來。
        /// </summary>
        /// <param name="progress">要寫的進度。寫之前再檢查一次——寫出不合法的檔，下一次讀就會被當壞檔。</param>
        /// <exception cref="ArgumentNullException">沒有給進度。</exception>
        /// <exception cref="ArgumentException">進度不合法。</exception>
        /// <exception cref="IOException">寫不進去（磁碟滿了、被鎖住）。</exception>
        /// <exception cref="UnauthorizedAccessException">沒有權限。</exception>
        public void Save(PlayerProgress progress)
        {
            if (progress == null) throw new ArgumentNullException(nameof(progress));
            progress.ThrowIfInvalid();

            System.IO.Directory.CreateDirectory(DirectoryPath);

            // ⚠️ 不帶 BOM：JsonUtility 讀得了，但打開來看或給別的工具讀時，BOM 是常見的絆腳石
            File.WriteAllText(TemporaryFilePath, progress.ToJson(), new UTF8Encoding(false));

            // 正本存在時用 Replace（一步換掉，中間不會有「正本不見了」的時刻）；
            // 第一次存檔時正本還不存在，Replace 會丟例外，所以改用 Move
            if (File.Exists(FilePath))
                File.Replace(TemporaryFilePath, FilePath, null);
            else
                File.Move(TemporaryFilePath, FilePath);
        }

        /// <summary>
        /// 清除存檔（正本與暫存檔）。<b>壞檔的備份不動</b>——那是查問題用的，要清請自己去資料夾刪。
        /// Editor 選單〈Tangram → 清除進度存檔〉叫的就是這支。
        /// </summary>
        /// <returns>有沒有真的刪掉正本（本來就沒有的話是 false）。</returns>
        /// <exception cref="IOException">刪不掉（被鎖住）。</exception>
        /// <exception cref="UnauthorizedAccessException">沒有權限。</exception>
        public bool Delete()
        {
            bool existed = File.Exists(FilePath);
            if (existed) File.Delete(FilePath);
            if (File.Exists(TemporaryFilePath)) File.Delete(TemporaryFilePath);
            return existed;
        }

        /// <summary>
        /// 把壞掉的正本改名成 <c>PlayerProgress.corrupt-yyyyMMdd-HHmmss.json</c>。同一秒內已經有一份的話後面加編號。
        /// </summary>
        /// <param name="nowUtc">現在的時間（UTC）。</param>
        /// <returns>改名之後的路徑；改不動時是 null。</returns>
        private string TryMoveAside(DateTime nowUtc)
        {
            string stem = Path.GetFileNameWithoutExtension(FileName);
            string extension = Path.GetExtension(FileName);
            string stamp = nowUtc.ToUniversalTime().ToString("yyyyMMdd-HHmmss", CultureInfo.InvariantCulture);

            try
            {
                string backupPath = Path.Combine(DirectoryPath, $"{stem}.corrupt-{stamp}{extension}");
                for (int i = 2; File.Exists(backupPath); i++)
                    backupPath = Path.Combine(DirectoryPath, $"{stem}.corrupt-{stamp}-{i}{extension}");

                File.Move(FilePath, backupPath);
                return backupPath;
            }
            catch (Exception error) when (error is IOException || error is UnauthorizedAccessException)
            {
                return null;
            }
        }
    }
}
