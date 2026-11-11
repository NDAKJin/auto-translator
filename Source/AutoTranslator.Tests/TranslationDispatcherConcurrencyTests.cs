using System;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using nomadicooer.AutoTranslator;
using Xunit;

namespace AutoTranslator.Tests;

public class TranslationDispatcherConcurrencyTests
{
    [Fact]
    public void ConsecutiveFailuresDoNotDisablePlatform()
    {
        var platform = new FailingPlatform();
        var dispatcher = new TranslationDispatcher(new[] { platform }) { MinIntervalMs = 0, MaxRetryOnRateLimit = 0 };
        for (int i = 0; i < 6; i++) Assert.False(dispatcher.Translate("English", "ChineseSimplified", "text").Success);
        Assert.Equal(6, platform.Calls);
    }

    private sealed class FailingPlatform : ITranslationPlatform
    {
        public int Calls { get; private set; }
        public string PlatformName => "test";
        public string GetLangCode(string name) => name;
        public TranslationResult Translate(string source, string target, string text)
        {
            Calls++;
            return TranslationResult.Fail(text, TranslationErrorCode.ServiceUnavailable, "failed");
        }
    }

    [Fact]
    public async Task CallsOverlapAndRespectPlatformStartInterval()
    {
        using var platform = new BlockingPlatform(3);
        var dispatcher = new TranslationDispatcher(new[] { platform })
        {
            UseRandom = false,
            MinIntervalMs = 80,
            MaxRetryOnRateLimit = 0
        };

        Task<TranslationResult>[] calls = Enumerable.Range(0, 3)
            .Select(_ => Task.Run(() => dispatcher.Translate("English", "ChineseSimplified", "text")))
            .ToArray();
        bool allStarted = platform.AllStarted.WaitOne(TimeSpan.FromSeconds(5));
        platform.Release.Set();
        await Task.WhenAll(calls);

        Assert.True(allStarted);
        Assert.Equal(3, platform.PeakConcurrent);
        long[] starts = platform.StartTicks.OrderBy(value => value).ToArray();
        for (int i = 1; i < starts.Length; i++)
        {
            double milliseconds = (starts[i] - starts[i - 1]) * 1000d / Stopwatch.Frequency;
            Assert.True(milliseconds >= 55d, $"Platform requests started only {milliseconds:F1} ms apart");
        }
    }

    private sealed class BlockingPlatform : ITranslationPlatform, IDisposable
    {
        private readonly CountdownEvent allStarted;
        private int active;
        private int peak;

        public BlockingPlatform(int count) => allStarted = new CountdownEvent(count);

        public ManualResetEventSlim Release { get; } = new(false);
        public WaitHandle AllStarted => allStarted.WaitHandle;
        public ConcurrentQueue<long> StartTicks { get; } = new();
        public int PeakConcurrent => peak;
        public string PlatformName => "test";
        public string GetLangCode(string name) => name;

        public TranslationResult Translate(string source, string target, string text)
        {
            int current = Interlocked.Increment(ref active);
            int observed;
            do
            {
                observed = peak;
                if (observed >= current) break;
            } while (Interlocked.CompareExchange(ref peak, current, observed) != observed);

            StartTicks.Enqueue(Stopwatch.GetTimestamp());
            allStarted.Signal();
            Release.Wait(TimeSpan.FromSeconds(5));
            Interlocked.Decrement(ref active);
            return TranslationResult.Ok("translated");
        }

        public void Dispose()
        {
            Release.Dispose();
            allStarted.Dispose();
        }
    }
}
