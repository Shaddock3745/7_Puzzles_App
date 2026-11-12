using System;
using System.Diagnostics;
using System.Threading.Tasks;

namespace Tangram.Core
{
    /// <summary>
    /// <b>一件丟到背景執行緒做的工作</b>（issue #37，2026-10-23）。主執行緒交出去之後<b>每一幀問一次做完沒</b>，做完了才拿結果。
    ///
    /// <b>為什麼用輪詢、不用回呼</b>：回呼會在背景執行緒上跑，而 Unity 的東西（貼圖、物件、大部分 API）只能在主執行緒碰——
    /// 回呼裡只要順手碰一下就出事，而且常常不報錯、只是偶爾壞。輪詢的話結果一定是在主執行緒上被拿走的，
    /// 「背景只算、主執行緒才動畫面」這條線畫在型別上，不靠每個呼叫端記得。
    ///
    /// ⚠️ <b>工作裡只能放純計算</b>：出題器、逼近、算難度（全是 <c>Data</c> 層，不碰 Unity）。<b>也不要印 log</b>——
    /// Unity 雖然允許背景執行緒印，但跟主執行緒的 log 交錯之後順序看不懂；要印的東西放進結果，由主執行緒印。
    ///
    /// ⚠️ <b>沒有取消</b>：開始了就一定做完，不要了的話丟掉結果就好（出題器一次最多幾秒，不值得多一套取消）。
    /// </summary>
    /// <typeparam name="T">工作的結果。</typeparam>
    public sealed class BackgroundJob<T>
    {
        private readonly Task<T> task;

        /// <summary>工作本身花了幾秒（在背景量的，不含排隊）。只在做完之後有意義。</summary>
        private double workSeconds;

        /// <summary>交出去就開始做（丟進執行緒池）。</summary>
        /// <param name="work">要做的事。⚠️ 會在別的執行緒上跑，見類別說明。</param>
        /// <exception cref="ArgumentNullException"><paramref name="work"/> 是 null。</exception>
        public BackgroundJob(Func<T> work)
        {
            if (work == null) throw new ArgumentNullException(nameof(work));

            task = Task.Run(() =>
            {
                var clock = Stopwatch.StartNew();
                try
                {
                    return work();
                }
                finally
                {
                    // 寫在 finally：丟例外的工作也量得到花了多久。
                    // 主執行緒只在 IsDone 之後才讀，而 Task 完成本身保證這個寫入看得到
                    workSeconds = clock.Elapsed.TotalSeconds;
                }
            });
        }

        /// <summary>做完了沒（成功或丟例外都算做完）。</summary>
        public bool IsDone => task.IsCompleted;

        /// <summary>
        /// 做完的話拿結果。<b>工作丟的例外不會在這裡丟出來</b>，而是放在 <paramref name="error"/> 交給呼叫端決定怎麼辦——
        /// 呼叫端是主執行緒的 <c>Update</c>，在那裡丟出來的話這一幀後面的事全部不做了。
        /// </summary>
        /// <param name="result">結果；還沒做完或丟了例外時是預設值。</param>
        /// <param name="error">工作丟的例外（<b>原本那個</b>，不是包了一層的 <see cref="AggregateException"/>）；成功時是 null。</param>
        /// <param name="seconds">工作本身花了幾秒。</param>
        /// <returns>做完了沒；false 時三個輸出都沒有意義。</returns>
        public bool TryGetOutcome(out T result, out Exception error, out double seconds)
        {
            result = default;
            error = null;
            seconds = 0.0;
            if (!task.IsCompleted) return false;

            seconds = workSeconds;
            if (task.IsFaulted)
            {
                // Task 會把例外包在 AggregateException 裡；只有一個的話拆開，log 才看得到真正的型別與堆疊
                AggregateException wrapped = task.Exception;
                error = wrapped != null && wrapped.InnerExceptions.Count == 1 ? wrapped.InnerExceptions[0] : wrapped;
                return true;
            }

            result = task.Result;
            return true;
        }
    }

    /// <summary>
    /// <b>照目標預先在背景做好一份結果</b>（issue #37，2026-10-23）。挑戰模式拿它預生下一題：目標＝下一題的目標難度。
    ///
    /// <b>規則</b>：
    /// <list type="bullet">
    /// <item><b>同一時間最多一件工作。</b>出題器（去重表＋亂數）整場只有一個，不能有兩條執行緒同時碰它——
    ///   所以目標換了的時候，<b>正在做的那件不打斷、也不另開一件</b>，等它做完、丟掉，才照新目標重做</item>
    /// <item><b>同一個目標只做一次</b>：已經在做、或已經做好的，再要一次什麼都不多做</item>
    /// <item><b>結果只能照它的目標拿走</b>：目標不對的拿不走（會丟掉重做）；拿走之後就沒了，下一次要重新要</item>
    /// </list>
    ///
    /// ⚠️ <b>被丟掉的結果不是白做的而已</b>：出題器生出來的題目已經記進去重表，整場就少一題可以出。量很小（只有中途換目標才會發生），
    /// 但「同一個種子生同一串題目」只在沒有丟掉過的時候成立。
    ///
    /// 💡 為什麼放 Core：會算錯的是「什麼時候重做、什麼時候拿得走」，跟 Unity 無關，<c>dotnet test</c> 驗得到。
    /// </summary>
    /// <typeparam name="T">結果。</typeparam>
    public sealed class TargetedPrefetch<T>
    {
        private readonly Func<int, T> produce;

        /// <summary>正在做或已經做好的那一件；沒有時是 null。</summary>
        private BackgroundJob<T> job;

        /// <summary><see cref="job"/> 是照哪個目標做的。</summary>
        private int jobTarget;

        /// <summary>現在要的目標；拿走之後清掉，沒人要時是 null。</summary>
        private int? wantedTarget;

        /// <summary>被丟掉的工作丟過的例外，還沒被呼叫端拿走的。</summary>
        private Exception discardedError;

        /// <param name="produce">照目標做一份結果。⚠️ <b>會在背景執行緒上跑</b>，而且保證同一時間只有一條在跑，見類別說明。</param>
        /// <exception cref="ArgumentNullException"><paramref name="produce"/> 是 null。</exception>
        public TargetedPrefetch(Func<int, T> produce)
        {
            this.produce = produce ?? throw new ArgumentNullException(nameof(produce));
        }

        /// <summary>有沒有一件還沒做完的工作。</summary>
        public bool IsWorking => job != null && !job.IsDone;

        /// <summary>因為目標換了而丟掉的結果有幾個（log 與測試用）。</summary>
        public int DiscardedCount { get; private set; }

        /// <summary>
        /// 要這個目標的結果。還沒有就開始做；正在做別的目標就等它做完再換（<see cref="Update"/> 會接手）。
        /// </summary>
        /// <param name="target">目標。</param>
        public void Want(int target)
        {
            wantedTarget = target;
            StartIfIdle();
        }

        /// <summary>
        /// <b>每一幀叫一次</b>：正在做的那件是舊目標、而且做完了的話，丟掉、照現在要的目標重做。
        /// 不叫的話目標換過一次之後就停在那裡，拿不到結果。
        /// </summary>
        public void Update() => StartIfIdle();

        /// <summary>
        /// 拿這個目標的結果。<b>順便表示「我要這個目標」</b>（等於先叫 <see cref="Want"/>），所以拿不到的時候工作已經在路上了，下一幀再問就好。
        /// </summary>
        /// <param name="target">目標。</param>
        /// <param name="result">結果；工作丟了例外時是預設值。</param>
        /// <param name="error">工作丟的例外；成功時是 null。<b>丟例外也算拿走了</b>，下一次會重做。</param>
        /// <param name="seconds">工作本身花了幾秒。</param>
        /// <returns>拿到了沒。</returns>
        public bool TryTake(int target, out T result, out Exception error, out double seconds)
        {
            Want(target);

            result = default;
            error = null;
            seconds = 0.0;
            if (job == null || jobTarget != target) return false;
            if (!job.TryGetOutcome(out result, out error, out seconds)) return false;

            job = null;
            wantedTarget = null;
            return true;
        }

        /// <summary>
        /// 被丟掉的工作丟過例外的話，拿走它（拿一次就清掉）。<b>主執行緒每幀問一次、有就印</b>——
        /// 不問的話那個例外就安靜地消失了，而它多半代表出題器有 bug。
        /// </summary>
        /// <returns>例外；沒有時是 null。</returns>
        public Exception TakeDiscardedError()
        {
            Exception error = discardedError;
            discardedError = null;
            return error;
        }

        private void StartIfIdle()
        {
            if (wantedTarget == null) return;
            int target = wantedTarget.Value;

            // 正在做或已經做好的就是這個目標：什麼都不多做
            if (job != null && jobTarget == target) return;

            // 正在做別的目標：不打斷、不另開（同一時間最多一件），等它做完
            if (job != null && !job.IsDone) return;

            // 舊目標的已經做完了：丟掉。它丟過例外的話留給呼叫端印
            if (job != null)
            {
                DiscardedCount++;
                if (job.TryGetOutcome(out _, out Exception error, out _) && error != null) discardedError = error;
            }

            jobTarget = target;
            job = new BackgroundJob<T>(() => produce(target));
        }
    }
}
