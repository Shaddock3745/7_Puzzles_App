using System;
using System.IO;
using NUnit.Framework;
using Tangram.Data;
using Tangram.Data.Tests;

namespace Tangram.Core.Tests
{
    /// <summary>
    /// 進度存檔（issue #39，2026-10-17）：內容合不合法（<see cref="PlayerProgress"/>）與
    /// 檔案的讀、寫、壞檔處理（<see cref="PlayerProgressFile"/>）。
    ///
    /// ⭐ <b>壞檔那幾條是這一批的重點</b>：實機上很難製造「寫到一半被殺掉」或「鍵名拼錯」的檔案，
    /// 而它們的症狀全是安靜的——實力被讀成 0、或從起始實力重來卻沒人知道為什麼。
    ///
    /// 每一條用自己的暫存資料夾，跑完就刪，不碰真正的 <c>persistentDataPath</c>。
    /// </summary>
    public class PlayerProgressTests
    {
        /// <summary>這一條測試的資料夾。</summary>
        private string directory;

        /// <summary>固定的「現在」，壞檔的備份名才算得出來。</summary>
        private static readonly DateTime Now = new DateTime(2026, 10, 17, 8, 30, 15, DateTimeKind.Utc);

        [SetUp]
        public void CreateDirectory()
        {
            directory = Path.Combine(Path.GetTempPath(), "tangram_progress_" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(directory);
        }

        [TearDown]
        public void DeleteDirectory()
        {
            if (Directory.Exists(directory)) Directory.Delete(directory, true);
        }

        /// <returns>一份合法的進度。</returns>
        private static PlayerProgress SampleProgress(int rating = 1234, int settledCount = 7) =>
            PlayerProgress.Create(rating, settledCount, "3f9a0c1b7e2d", Now);

        /// <param name="name">檔名。</param>
        /// <returns>測試資料夾裡那個檔的完整路徑。</returns>
        private string PathOf(string name) => Path.Combine(directory, name);

        // ───────── 一、內容 ─────────

        [Test]
        public void 內容_轉成JSON再讀回來每一項都一樣()
        {
            PlayerProgress original = SampleProgress();
            PlayerProgress restored = PlayerProgress.FromJson(original.ToJson());

            Assert.AreEqual(PlayerProgress.CurrentFormatVersion, restored.formatVersion);
            Assert.AreEqual(1234, restored.skillRating);
            Assert.AreEqual(7, restored.settledCount);
            Assert.AreEqual("3f9a0c1b7e2d", restored.weightsFingerprint);
            Assert.AreEqual(original.savedAtUtc, restored.savedAtUtc);
        }

        [Test]
        public void 內容_寫出來的鍵跟欄位一一對上()
        {
            // 鍵名是儲存契約：之後有人改了欄位名，這條會紅，提醒他已存的檔案會讀成預設值
            string text = SampleProgress().ToJson();
            JsonKeyAssertions.EveryKeyIsKnown(text, "PlayerProgress.json", typeof(PlayerProgress));
            JsonKeyAssertions.EveryFieldAppears(text, "PlayerProgress.json", typeof(PlayerProgress), 1);
        }

        [Test]
        public void 內容_存檔時間一律寫成UTC()
        {
            var local = new DateTime(2026, 10, 17, 16, 30, 15, DateTimeKind.Local);
            PlayerProgress progress = PlayerProgress.Create(1000, 0, "x", local);
            Assert.AreEqual(local.ToUniversalTime().ToString("o"), progress.savedAtUtc);
        }

        [Test]
        public void 內容_空的或不是JSON都丟ArgumentException()
        {
            // 統一成同一種例外，PlayerProgressFile 才只要接一種就知道是壞檔
            Assert.Throws<ArgumentException>(() => PlayerProgress.FromJson(""));
            Assert.Throws<ArgumentException>(() => PlayerProgress.FromJson("   "));
            Assert.Throws<ArgumentException>(() => PlayerProgress.FromJson("{\"formatVersion\": 1, \"skillRating\": 12"));
            Assert.Throws<ArgumentException>(() => PlayerProgress.FromJson("這不是 JSON"));
        }

        [Test]
        public void 內容_缺了實力的鍵就是壞檔不是零分()
        {
            // ⚠️ 最安靜的一種：JsonUtility 把缺的鍵讀成 0，拿 0 去出題會被夾到範圍最低而且不報錯
            string json = "{\"formatVersion\": 1, \"settledCount\": 3}";
            var error = Assert.Throws<ArgumentException>(() => PlayerProgress.FromJson(json));
            StringAssert.Contains(nameof(PlayerProgress.skillRating), error.Message);
        }

        [Test]
        public void 內容_版本不認得就是壞檔()
        {
            string withoutVersion = "{\"skillRating\": 1200, \"settledCount\": 3}";
            string newerVersion = "{\"formatVersion\": 2, \"skillRating\": 1200, \"settledCount\": 3}";
            Assert.Throws<ArgumentException>(() => PlayerProgress.FromJson(withoutVersion));
            Assert.Throws<ArgumentException>(() => PlayerProgress.FromJson(newerVersion));
        }

        [Test]
        public void 內容_實力不大於零或局數是負的都不合法()
        {
            Assert.Throws<ArgumentException>(() => PlayerProgress.Create(0, 0, "x", Now));
            Assert.Throws<ArgumentException>(() => PlayerProgress.Create(-5, 0, "x", Now));
            Assert.Throws<ArgumentException>(() => PlayerProgress.Create(1000, -1, "x", Now));
        }

        [Test]
        public void 內容_權重指紋是空的照樣讀得進來()
        {
            // 指紋只拿來比對、寫警告，缺了不值得把玩家的實力丟掉
            string json = "{\"formatVersion\": 1, \"skillRating\": 1200, \"settledCount\": 3}";
            Assert.AreEqual(1200, PlayerProgress.FromJson(json).skillRating);
        }

        // ───────── 二、檔案 ─────────

        [Test]
        public void 檔案_沒有存檔時回沒有存檔()
        {
            PlayerProgressLoadResult result = new PlayerProgressFile(directory).Load(Now);
            Assert.AreEqual(PlayerProgressLoadStatus.NoFile, result.Status);
            Assert.IsNull(result.Progress);
        }

        [Test]
        public void 檔案_資料夾還不存在也存得進去()
        {
            string nested = Path.Combine(directory, "還沒建", "第二層");
            var file = new PlayerProgressFile(nested);
            file.Save(SampleProgress());
            Assert.AreEqual(PlayerProgressLoadStatus.Loaded, file.Load(Now).Status);
        }

        [Test]
        public void 檔案_存了再讀回來一樣()
        {
            var file = new PlayerProgressFile(directory);
            file.Save(SampleProgress());

            PlayerProgressLoadResult result = file.Load(Now);
            Assert.AreEqual(PlayerProgressLoadStatus.Loaded, result.Status);
            Assert.AreEqual(1234, result.Progress.skillRating);
            Assert.AreEqual(7, result.Progress.settledCount);
        }

        [Test]
        public void 檔案_存第二次蓋掉第一次而且不留暫存檔()
        {
            var file = new PlayerProgressFile(directory);
            file.Save(SampleProgress(rating: 1000, settledCount: 1));
            file.Save(SampleProgress(rating: 1100, settledCount: 2));

            Assert.AreEqual(1100, file.Load(Now).Progress.skillRating);
            Assert.IsFalse(File.Exists(PathOf(PlayerProgressFile.FileName + ".tmp")), "換完正本之後暫存檔應該不在了");
        }

        [Test]
        public void 檔案_壞檔改名留著然後回壞檔()
        {
            string broken = "{\"formatVersion\": 1, \"skillRa";   // 寫到一半被截斷的樣子
            File.WriteAllText(PathOf(PlayerProgressFile.FileName), broken);

            PlayerProgressLoadResult result = new PlayerProgressFile(directory).Load(Now);

            Assert.AreEqual(PlayerProgressLoadStatus.Corrupt, result.Status);
            Assert.IsNull(result.Progress);
            Assert.IsFalse(string.IsNullOrEmpty(result.Problem));
            Assert.AreEqual(PathOf("PlayerProgress.corrupt-20261017-083015.json"), result.CorruptBackupPath);
            Assert.AreEqual(broken, File.ReadAllText(result.CorruptBackupPath), "備份要是原封不動的內容，才查得到為什麼壞");
            Assert.IsFalse(File.Exists(PathOf(PlayerProgressFile.FileName)), "正本改名走了，下一次讀才不會再撞到它");
        }

        [Test]
        public void 檔案_壞檔之後再讀一次就是沒有存檔()
        {
            File.WriteAllText(PathOf(PlayerProgressFile.FileName), "{}");
            var file = new PlayerProgressFile(directory);

            Assert.AreEqual(PlayerProgressLoadStatus.Corrupt, file.Load(Now).Status);
            Assert.AreEqual(PlayerProgressLoadStatus.NoFile, file.Load(Now).Status);
        }

        [Test]
        public void 檔案_同一秒壞兩次兩份備份都留著()
        {
            var file = new PlayerProgressFile(directory);
            File.WriteAllText(PathOf(PlayerProgressFile.FileName), "第一份");
            string first = file.Load(Now).CorruptBackupPath;
            File.WriteAllText(PathOf(PlayerProgressFile.FileName), "第二份");
            string second = file.Load(Now).CorruptBackupPath;

            Assert.AreNotEqual(first, second);
            Assert.AreEqual("第一份", File.ReadAllText(first));
            Assert.AreEqual("第二份", File.ReadAllText(second));
        }

        [Test]
        public void 檔案_留下來的暫存檔不影響讀檔()
        {
            // 上一次寫到一半被殺掉：暫存檔是殘缺的，正本是上一版完整的
            var file = new PlayerProgressFile(directory);
            file.Save(SampleProgress(rating: 1050));
            File.WriteAllText(PathOf(PlayerProgressFile.FileName + ".tmp"), "{\"formatVersion\": 1, \"ski");

            PlayerProgressLoadResult result = file.Load(Now);
            Assert.AreEqual(PlayerProgressLoadStatus.Loaded, result.Status);
            Assert.AreEqual(1050, result.Progress.skillRating);

            // 而下一次存檔照樣成功，蓋掉那份殘缺的暫存檔
            file.Save(SampleProgress(rating: 1060));
            Assert.AreEqual(1060, file.Load(Now).Progress.skillRating);
        }

        [Test]
        public void 檔案_第一次存檔就被殺掉時算沒有存檔()
        {
            // 只有暫存檔、沒有正本：當作第一次玩，不要去讀那份殘缺的
            File.WriteAllText(PathOf(PlayerProgressFile.FileName + ".tmp"), "{\"formatVersion\": 1, \"ski");
            Assert.AreEqual(PlayerProgressLoadStatus.NoFile, new PlayerProgressFile(directory).Load(Now).Status);
        }

        [Test]
        public void 檔案_清除會刪正本與暫存檔但留著壞檔備份()
        {
            var file = new PlayerProgressFile(directory);
            File.WriteAllText(PathOf(PlayerProgressFile.FileName), "壞的");
            string backup = file.Load(Now).CorruptBackupPath;
            file.Save(SampleProgress());
            File.WriteAllText(PathOf(PlayerProgressFile.FileName + ".tmp"), "殘缺");

            Assert.IsTrue(file.Delete());
            Assert.IsFalse(File.Exists(PathOf(PlayerProgressFile.FileName)));
            Assert.IsFalse(File.Exists(PathOf(PlayerProgressFile.FileName + ".tmp")));
            Assert.IsTrue(File.Exists(backup), "壞檔備份是查問題用的，清除進度不該連它一起刪");

            Assert.IsFalse(file.Delete(), "本來就沒有存檔時回 false");
        }

        [Test]
        public void 檔案_不合法的進度不會被寫出去()
        {
            // 繞過 Create 自己組一份壞的：寫出去的話下次讀會被當壞檔，玩家的實力就沒了
            var invalid = new PlayerProgress { formatVersion = PlayerProgress.CurrentFormatVersion, skillRating = 0 };
            var file = new PlayerProgressFile(directory);

            Assert.Throws<ArgumentException>(() => file.Save(invalid));
            Assert.AreEqual(PlayerProgressLoadStatus.NoFile, file.Load(Now).Status);
        }
    }
}
