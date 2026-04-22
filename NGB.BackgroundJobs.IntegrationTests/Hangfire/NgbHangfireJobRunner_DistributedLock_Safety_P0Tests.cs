using FluentAssertions;
using Hangfire;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using NGB.BackgroundJobs.Contracts;
using NGB.BackgroundJobs.DependencyInjection;
using NGB.BackgroundJobs.Infrastructure;
using NGB.BackgroundJobs.IntegrationTests.Infrastructure;
using NGB.Tools.Exceptions;
using Xunit;

namespace NGB.BackgroundJobs.IntegrationTests.Hangfire;

[Collection(HangfirePostgresCollection.Name)]
public sealed class NgbHangfireJobRunner_DistributedLock_Safety_P0Tests(HangfirePostgresFixture fixture)
{
    [Fact]
    public async Task RunAsync_WhenJobFails_LockIsReleased_AndNextRunCanProceed()
    {
        var jobId = "test.lock.release";

        var notifier = new SpyNotifier();
        var job = new FailOnceJob(jobId);

        using var sp = BuildServiceProvider([job], notifier);
        var runner = CreateRunner(sp, notifier);

        Func<Task> act = () => runner.RunAsync(jobId, new TestJobCancellationToken(CancellationToken.None));

        var ex = await act.Should().ThrowAsync<NgbUnexpectedException>();
        ex.Which.InnerException.Should().BeOfType<NullReferenceException>();
        ex.Which.Operation.Should().Be(jobId);

        await runner.RunAsync(jobId, new TestJobCancellationToken(CancellationToken.None));

        job.RunCount.Should().Be(2);

        notifier.Results.Should().ContainSingle(x => x.JobId == jobId && x.Outcome == PlatformJobRunOutcome.Failed);
        notifier.Results.Should().NotContain(x => x.JobId == jobId && x.Outcome == PlatformJobRunOutcome.SkippedOverlap);
    }

    [Fact]
    public async Task RunAsync_AllowsParallelRuns_ForDifferentJobIds()
    {
        var jobId1 = "test.parallel.1";
        var jobId2 = "test.parallel.2";

        var notifier = new SpyNotifier();
        var job1 = new BlockingJob(jobId1);
        var job2 = new BlockingJob(jobId2);

        using var sp = BuildServiceProvider([job1, job2], notifier);
        var runner = CreateRunner(sp, notifier);

        var token = new TestJobCancellationToken(CancellationToken.None);

        var t1 = runner.RunAsync(jobId1, token);
        var t2 = runner.RunAsync(jobId2, token);

        using var startedCts = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        await Task.WhenAll(job1.WaitUntilStartedAsync(), job2.WaitUntilStartedAsync())
            .WaitAsync(startedCts.Token);

        notifier.Results.Should().BeEmpty("successful runs must not notify");

        job1.Release();
        job2.Release();

        await Task.WhenAll(t1, t2);

        job1.RunCount.Should().Be(1);
        job2.RunCount.Should().Be(1);
    }

    private PlatformHangfireJobRunner CreateRunner(IServiceProvider sp, IPlatformJobNotifier notifier)
    {
        return new PlatformHangfireJobRunner(
            sp.GetRequiredService<IServiceScopeFactory>(),
            NullLogger<PlatformHangfireJobRunner>.Instance,
            notifier,
            fixture.JobStorage,
            Options.Create(new PlatformHangfireOptions
            {
                DistributedLockTimeoutSeconds = 1
            }));
    }

    private static ServiceProvider BuildServiceProvider(
        IReadOnlyCollection<IPlatformBackgroundJob> jobs,
        IPlatformJobNotifier notifier)
    {
        var services = new ServiceCollection();
        services.AddScoped<IJobRunMetrics, TestJobRunMetrics>();
        foreach (var job in jobs)
            services.AddSingleton(job);
        services.AddSingleton(notifier);
        return services.BuildServiceProvider();
    }

    private sealed class TestJobRunMetrics : IJobRunMetrics
    {
        private readonly Dictionary<string, long> _counters = new(StringComparer.Ordinal);

        public void Increment(string name, long delta = 1)
        {
            if (string.IsNullOrWhiteSpace(name) || delta == 0)
                return;

            name = name.Trim();

            _counters.TryGetValue(name, out var current);
            _counters[name] = current + delta;
        }

        public void Set(string name, long value)
        {
            if (string.IsNullOrWhiteSpace(name))
                return;

            _counters[name.Trim()] = value;
        }

        public IReadOnlyDictionary<string, long> Snapshot()
        {
            return _counters.Count == 0
                ? new Dictionary<string, long>(0)
                : new Dictionary<string, long>(_counters, StringComparer.Ordinal);
        }
    }

    private sealed class SpyNotifier : IPlatformJobNotifier
    {
        private readonly List<PlatformJobRunResult> _results = new();
        private readonly object _gate = new();

        public IReadOnlyList<PlatformJobRunResult> Results
        {
            get
            {
                lock (_gate)
                    return _results.ToList();
            }
        }

        public Task NotifyAsync(PlatformJobRunResult result, CancellationToken cancellationToken)
        {
            lock (_gate)
                _results.Add(result);
            return Task.CompletedTask;
        }
    }

    private sealed class TestJobCancellationToken : IJobCancellationToken
    {
        public TestJobCancellationToken(CancellationToken shutdownToken)
        {
            ShutdownToken = shutdownToken;
        }

        public CancellationToken ShutdownToken { get; }

        public void ThrowIfCancellationRequested()
        {
            ShutdownToken.ThrowIfCancellationRequested();
        }
    }

    private sealed class FailOnceJob : IPlatformBackgroundJob
    {
        private int _runCount;

        public FailOnceJob(string jobId)
        {
            JobId = jobId;
        }

        public string JobId { get; }

        public int RunCount => Volatile.Read(ref _runCount);

        public Task RunAsync(CancellationToken cancellationToken)
        {
            var n = Interlocked.Increment(ref _runCount);
            if (n == 1)
                throw new NullReferenceException("boom");
            return Task.CompletedTask;
        }
    }

    private sealed class BlockingJob : IPlatformBackgroundJob
    {
        private readonly TaskCompletionSource _started = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly TaskCompletionSource _release = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public BlockingJob(string jobId)
        {
            JobId = jobId;
        }

        public string JobId { get; }

        public int RunCount { get; private set; }

        public Task WaitUntilStartedAsync() => _started.Task;

        public void Release() => _release.TrySetResult();

        public async Task RunAsync(CancellationToken cancellationToken)
        {
            RunCount++;
            _started.TrySetResult();
            await _release.Task.WaitAsync(cancellationToken);
        }
    }
}
