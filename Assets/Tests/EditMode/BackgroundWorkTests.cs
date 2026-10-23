using System;
using System.Diagnostics;
using System.Threading;
using NUnit.Framework;

namespace Tangram.Core.Tests
{
    /// <summary>
    /// 背景工作與預生（P5-2，issue #37，2026-10-23）：<see cref="BackgroundJob{T}"/> 與 <see cref="TargetedPrefetch{T}"/>。
    ///
    /// ⭐ <b>重點是「同一時間最多一件」</b>：出題器整場只有一個，兩條執行緒同時碰它的症狀是偶爾生出重複的題、或去重表壞掉，
    /// 不報錯、而且實機上幾乎重現不出來。所以那一條用閘門卡住工作、數同時在跑的有幾件，不靠時間碰運氣。
    ///
    /// 工作都用 <see cref="ManualResetEventSlim"/> 當閘門，測試自己決定什麼時候放行；等「做完」一律有逾時，壞了不會卡住整批。
    /// </summary>
    public class BackgroundWorkTests
    {
        /// <summary>等條件成立，最多等 5 秒；逾時就讓測試失敗。</summary>
        private static void WaitUntil(Func<bool> condition, string what)
        {
            var clock = Stopwatch.StartNew();
            while (!condition())
            {
                if (clock.Elapsed.TotalSeconds > 5.0) Assert.Fail($"等了 5 秒還沒有：{what}");
                Thread.Sleep(1);
            }
        }

        [Test]
        public void 背景工作_做完之前拿不到結果做完之後拿得到()
        {
            using (var gate = new ManualResetEventSlim(false))
            {
                var job = new BackgroundJob<int>(() => { gate.Wait(); return 42; });

                Assert.IsFalse(job.IsDone);
                Assert.IsFalse(job.TryGetOutcome(out _, out _, out _));

                gate.Set();
                WaitUntil(() => job.IsDone, "工作做完");

                Assert.IsTrue(job.TryGetOutcome(out int result, out Exception error, out double seconds));
                Assert.AreEqual(42, result);
                Assert.IsNull(error);
                Assert.GreaterOrEqual(seconds, 0.0);
            }
        }

        [Test]
        public void 背景工作_真的不在呼叫的執行緒上跑()
        {
            int callerThread = Thread.CurrentThread.ManagedThreadId;
            var job = new BackgroundJob<int>(() => Thread.CurrentThread.ManagedThreadId);
            WaitUntil(() => job.IsDone, "工作做完");

            job.TryGetOutcome(out int workerThread, out _, out _);
            Assert.AreNotEqual(callerThread, workerThread);
        }

        [Test]
        public void 背景工作_例外原樣交回來而且不會丟出來()
        {
            var job = new BackgroundJob<int>(() => throw new InvalidOperationException("出題器壞了"));
            WaitUntil(() => job.IsDone, "工作做完");

            // 拿結果本身不能丟：呼叫端是主執行緒的 Update
            Assert.IsTrue(job.TryGetOutcome(out int result, out Exception error, out _));
            Assert.AreEqual(0, result);

            // 原本那個，不是 Task 包的 AggregateException——log 才看得到真正的型別
            Assert.IsInstanceOf<InvalidOperationException>(error);
            Assert.AreEqual("出題器壞了", error.Message);
        }

        [Test]
        public void 預生_同一個目標只做一次()
        {
            int calls = 0;
            var prefetch = new TargetedPrefetch<int>(target => { Interlocked.Increment(ref calls); return target * 10; });

            prefetch.Want(900);
            prefetch.Want(900);
            WaitUntil(() => !prefetch.IsWorking, "工作做完");
            prefetch.Want(900);
            prefetch.Update();

            Assert.IsTrue(prefetch.TryTake(900, out int result, out Exception error, out _));
            Assert.AreEqual(9000, result);
            Assert.IsNull(error);
            Assert.AreEqual(1, calls);
        }

        [Test]
        public void 預生_目標換了要等舊的做完才重做而且同時最多一件()
        {
            int running = 0, mostRunning = 0, calls = 0;
            using (var gate = new ManualResetEventSlim(false))
            {
                var prefetch = new TargetedPrefetch<int>(target =>
                {
                    Interlocked.Increment(ref calls);
                    int now = Interlocked.Increment(ref running);
                    // 只有一條會寫，但照樣用原子操作記最大值
                    int seen;
                    while (now > (seen = Volatile.Read(ref mostRunning)) &&
                           Interlocked.CompareExchange(ref mostRunning, now, seen) != seen) { }
                    gate.Wait();
                    Interlocked.Decrement(ref running);
                    return target;
                });

                prefetch.Want(900);
                WaitUntil(() => Volatile.Read(ref calls) == 1, "第一件開始");

                // 第一件還卡在閘門上就換目標：不能另開一件
                prefetch.Want(1200);
                prefetch.Update();
                Assert.IsFalse(prefetch.TryTake(1200, out _, out _, out _));
                Assert.AreEqual(1, Volatile.Read(ref calls));

                // 放行：舊的做完、被丟掉，Update 才照新目標重做
                gate.Set();
                WaitUntil(() =>
                {
                    prefetch.Update();
                    return prefetch.TryTake(1200, out _, out _, out _);
                }, "新目標的結果");

                Assert.AreEqual(2, calls);
                Assert.AreEqual(1, mostRunning, "同時在跑的工作不能超過一件");
                Assert.AreEqual(1, prefetch.DiscardedCount);
            }
        }

        [Test]
        public void 預生_目標不對的結果拿不走()
        {
            var prefetch = new TargetedPrefetch<int>(target => target);
            prefetch.Want(900);
            WaitUntil(() => !prefetch.IsWorking, "工作做完");

            // 做好的是 900，要的是 1000：拿不到，而且 900 那份被丟掉、改做 1000
            Assert.IsFalse(prefetch.TryTake(1000, out _, out _, out _));
            Assert.AreEqual(1, prefetch.DiscardedCount);

            WaitUntil(() => prefetch.TryTake(1000, out _, out _, out _), "1000 的結果");
        }

        [Test]
        public void 預生_拿走之後就沒了下一次要重做()
        {
            int calls = 0;
            var prefetch = new TargetedPrefetch<int>(target => { Interlocked.Increment(ref calls); return target; });

            WaitUntil(() => prefetch.TryTake(900, out _, out _, out _), "第一份");
            Assert.AreEqual(1, calls);

            // 同一個目標再拿：上一份已經拿走了，要重做一份（挑戰模式連兩題同難度就是這樣）
            WaitUntil(() => prefetch.TryTake(900, out _, out _, out _), "第二份");
            Assert.AreEqual(2, calls);
            Assert.AreEqual(0, prefetch.DiscardedCount);
        }

        [Test]
        public void 預生_工作丟例外也算拿走了()
        {
            var prefetch = new TargetedPrefetch<int>(target => throw new InvalidOperationException("壞了"));

            Exception error = null;
            WaitUntil(() => prefetch.TryTake(900, out _, out error, out _), "例外交回來");
            Assert.IsInstanceOf<InvalidOperationException>(error);
            Assert.IsFalse(prefetch.IsWorking);
        }

        [Test]
        public void 預生_被丟掉的工作丟的例外留給呼叫端印()
        {
            var prefetch = new TargetedPrefetch<int>(target =>
            {
                if (target == 900) throw new InvalidOperationException("舊目標壞了");
                return target;
            });

            prefetch.Want(900);
            WaitUntil(() => !prefetch.IsWorking, "舊目標做完");
            Assert.IsNull(prefetch.TakeDiscardedError(), "還沒丟掉之前不該有");

            prefetch.Want(1000);
            Exception discarded = prefetch.TakeDiscardedError();
            Assert.IsInstanceOf<InvalidOperationException>(discarded);
            Assert.IsNull(prefetch.TakeDiscardedError(), "拿一次就清掉");

            WaitUntil(() => prefetch.TryTake(1000, out _, out _, out _), "新目標的結果");
        }

        [Test]
        public void 參數檢查()
        {
            Assert.Throws<ArgumentNullException>(() => new BackgroundJob<int>(null));
            Assert.Throws<ArgumentNullException>(() => new TargetedPrefetch<int>(null));
        }
    }
}
