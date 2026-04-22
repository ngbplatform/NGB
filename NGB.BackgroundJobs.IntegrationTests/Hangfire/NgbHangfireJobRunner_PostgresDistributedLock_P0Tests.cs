using FluentAssertions;
using Hangfire;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using NGB.BackgroundJobs.Contracts;
using NGB.BackgroundJobs.DependencyInjection;
using NGB.BackgroundJobs.Infrastructure;
using NGB.BackgroundJobs.IntegrationTests.Infrastructure;
using Xunit;

namespace NGB.BackgroundJobs.IntegrationTests.Hangfire;

[Collection(HangfirePostgresCollection.Name)]
public sealed class NgbHangfireJobRunner_PostgresDistributedLock_P0Tests(HangfirePostgresFixture fixture)
{
    [Fact]
    public async Task RunAsync_SkipsOverlap_WhenPreviousRunIsStillExecuting()
    {
        var jobId = "test.long";

        var notifier = new SpyNotifier();
        var job = new BlockingJob(jobId);

        using var sp = BuildServiceProvider(job, notifier);

        var runner = new PlatformHangfireJobRunner(
            sp.GetRequiredService<IServiceScopeFactory>(),
            NullLogger<PlatformHangfireJobRunner>.Instance,
            notifier,
            fixture.JobStorage,
            Options.Create(new PlatformHangfireOptions
            {
                DistributedLockTimeoutSeconds = 1
            }));

        var token = new TestJobCancellationToken();

        var first = runner.RunAsync(jobId, token);

        await job.WaitUntilStartedAsync();

        await runner.RunAsync(jobId, token);

        notifier.Results.Should().ContainSingle(x =>
            x.JobId == jobId && x.Outcome == PlatformJobRunOutcome.SkippedOverlap);

        job.Release();

        await first;

        // first run succeeded => no extra notifications
        notifier.Results.Should().HaveCount(1);
    }

    [Fact]
    public async Task RunAsync_RunsJob_WhenLockIsFree_AndDoesNotNotify()
    {
        var jobId = "test.quick";

        var notifier = new SpyNotifier();
        var job = new CountingJob(jobId);

        using var sp = BuildServiceProvider(job, notifier);

        var runner = new PlatformHangfireJobRunner(
            sp.GetRequiredService<IServiceScopeFactory>(),
            NullLogger<PlatformHangfireJobRunner>.Instance,
            notifier,
            fixture.JobStorage,
            Options.Create(new PlatformHangfireOptions
            {
                DistributedLockTimeoutSeconds = 1
            }));

        await runner.RunAsync(jobId, new TestJobCancellationToken());

        job.RunCount.Should().Be(1);
        notifier.Results.Should().BeEmpty();
    }

    private static ServiceProvider BuildServiceProvider(IPlatformBackgroundJob job, IPlatformJobNotifier notifier)
    {
        var services = new ServiceCollection();
        services.AddScoped<IJobRunMetrics, TestJobRunMetrics>();
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
        public CancellationToken ShutdownToken => CancellationToken.None;

        public void ThrowIfCancellationRequested()
        {
            // no-op
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

        public Task WaitUntilStartedAsync() => _started.Task;

        public void Release() => _release.TrySetResult();

        public async Task RunAsync(CancellationToken cancellationToken)
        {
            _started.TrySetResult();
            await _release.Task.WaitAsync(cancellationToken);
        }
    }

    private sealed class CountingJob : IPlatformBackgroundJob
    {
        public CountingJob(string jobId)
        {
            JobId = jobId;
        }

        public string JobId { get; }

        public int RunCount { get; private set; }

        public Task RunAsync(CancellationToken cancellationToken)
        {
            RunCount++;
            return Task.CompletedTask;
        }
    }
}
