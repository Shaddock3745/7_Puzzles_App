using System.Collections.Generic;
using NUnit.Framework;

namespace Tangram.Data.Tests
{
    /// <summary>
    /// ⭐ <b>基準批次</b>：難度那把尺是在這一批題目上定出來的（2026-09-13 從縮圖牆匯出抽出來）。
    ///
    /// <b>為什麼要抽成一份</b>：縮圖牆匯出與重算換算倍率的工具都要生這一批。兩邊各寫一份的話，
    /// 「基準批次」的定義哪天就會分家——例如一邊改了種子或門檻，另一邊沒跟上——
    /// 重算工具就會拿一批不一樣的題目去校正尺，<b>而且不報錯</b>。
    ///
    /// <b>定義</b>：種子 20260910、<b>全開的門檻</b>拿滿 2000 個拼得起來的形狀，
    /// 再用 <see cref="AestheticLimits.Default"/> 篩（含凸包填滿率 ≥ 0.6），應該剩 845 個。
    ///
    /// <b>為什麼用全開的門檻生</b>：生成器對被審美門檻篩掉的形狀只回傳特徵、不回傳輪廓
    /// （輪廓要付正規化那 16 次追邊才有），所以用預設門檻生的話，被擋掉的那些在牆上根本畫不出來。
    /// 全開之後每個拼得起來的形狀都會走完正規化；「預設門檻收不收」再用
    /// <see cref="AestheticLimits.Violations"/> 問一次——跟生成器用的是同一個判斷，
    /// <c>PuzzleGeneratorTests.門檻_生成器的判定跟AestheticLimits一致</c> 釘住這件事。
    ///
    /// ⚠️ 全開跑出來的序列跟預設門檻下不完全一樣（去重的集合不同：被擋掉的形狀也會進去重表），
    /// 但實測一千題重複數是 0，實務上是同一批。
    ///
    /// ⚠️ 生 2000 個形狀要數十秒，<b>只給 [Explicit] 的工具用</b>，不要拿到日常測試裡。
    /// </summary>
    public sealed class BaselineBatch
    {
        /// <summary>固定種子，跟批次統計同一個。</summary>
        public const int Seed = 20260910;

        /// <summary>要拿滿幾個拼得起來的形狀（未篩選池的大小）。</summary>
        public const int WantedShapeCount = 2000;

        /// <summary>
        /// 用預設門檻篩完應該剩幾個。<b>對不上就代表出題器或門檻被改過</b>，
        /// 那時候這一批已經不是當初定尺的那一批了。
        /// </summary>
        public const int ExpectedAcceptedCount = 845;

        /// <summary>呼叫次數的硬上限。只防病態情況。</summary>
        private const int MaximumAttempts = 20000;

        /// <summary>
        /// 全開的門檻：什麼都收。
        ///
        /// ⚠️ <b>周長上限不能用 long 的最大值</b>。<see cref="Scalar"/> 比大小是相減之後看正負，
        /// 極端大的值相減會溢位，而且不報錯——結果是「全開」變成「全關」之類的怪行為。
        /// 一百萬遠大於任何可能的周長（七片排成一長串也不到 69），又絕對不會溢位。
        /// 三個整數門檻是直接用 &gt; 比，int 的最大值沒有這個問題。
        /// </summary>
        public static readonly AestheticLimits WideOpenLimits = new AestheticLimits(
            int.MaxValue, int.MaxValue, int.MaxValue, Scalar.FromInteger(1000000));

        /// <summary>2000 個拼得起來的形狀，生成的順序。</summary>
        public readonly List<GenerationResult> Shapes;

        /// <summary>總共叫了幾次生成器。</summary>
        public readonly int Attempts;

        /// <summary>各種結果各出現幾次。</summary>
        public readonly Dictionary<GenerationOutcome, int> OutcomeCounts;

        private BaselineBatch(List<GenerationResult> shapes, int attempts,
                              Dictionary<GenerationOutcome, int> outcomeCounts)
        {
            Shapes = shapes;
            Attempts = attempts;
            OutcomeCounts = outcomeCounts;
        }

        /// <summary>
        /// 生出這一批。拿不滿、或全開的門檻居然擋掉了東西，都讓測試失敗。
        /// </summary>
        /// <returns>生好的批次。</returns>
        public static BaselineBatch Generate()
        {
            var generator = new PuzzleGenerator(Seed, WideOpenLimits);
            var shapes = new List<GenerationResult>(WantedShapeCount);
            var outcomeCounts = new Dictionary<GenerationOutcome, int>();
            int attempts = 0;

            while (shapes.Count < WantedShapeCount && attempts < MaximumAttempts)
            {
                GenerationResult result = generator.TryGenerate();
                attempts++;
                outcomeCounts.TryGetValue(result.Outcome, out int count);
                outcomeCounts[result.Outcome] = count + 1;

                if (result.Outcome == GenerationOutcome.Generated) shapes.Add(result);
            }

            Assert.AreEqual(WantedShapeCount, shapes.Count, $"呼叫了 {attempts} 次還沒拿滿");
            outcomeCounts.TryGetValue(GenerationOutcome.RejectedByAesthetics, out int rejectedUnderWideOpen);
            Assert.AreEqual(0, rejectedUnderWideOpen, "全開的門檻不應該擋掉任何形狀");

            return new BaselineBatch(shapes, attempts, outcomeCounts);
        }

        /// <summary>用 <see cref="AestheticLimits.Default"/> 篩過的那些——<b>尺就是在這一群上定的</b>。</summary>
        /// <returns>預設門檻會收的形狀，保持生成的順序。</returns>
        public List<GenerationResult> AcceptedByDefault()
        {
            AestheticLimits defaultLimits = AestheticLimits.Default;
            var accepted = new List<GenerationResult>();
            foreach (GenerationResult shape in Shapes)
                if (defaultLimits.Accepts(shape.Features)) accepted.Add(shape);
            return accepted;
        }
    }
}
