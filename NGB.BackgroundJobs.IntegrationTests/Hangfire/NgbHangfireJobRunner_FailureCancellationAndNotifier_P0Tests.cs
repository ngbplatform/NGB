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
public sealed class NgbHangfireJobRunner_FailureCancellationAndNotifier_P0Tests(HangfirePostgresFixture fixture)
{
    [Fact]
    public async Task RunAsync_WhenJobFails_NotifiesFailed_AndRethrows()
    {
        var jobId = "test.fail";

        var notifier = new SpyNotifier();
        var job = new ThrowingJob(jobId, new NullReferenceException("boom"));

        using var sp = BuildServiceProvider(job, notifier);

        var runner = CreateRunner(sp, notifier);

        Func<Task> act = () => runner.RunAsync(jobId, new TestJobCancellationToken(CancellationToken.None));

        var ex = await act.Should().ThrowAsync<NgbUnexpectedException>();
        ex.Which.InnerException.Should().BeOfType<NullReferenceException>();
        ex.Which.Operation.Should().Be(jobId);

        notifier.Results.Should().ContainSingle(x => x.JobId == jobId && x.Outcome == PlatformJobRunOutcome.Failed);
    }

    [Fact]
    public async Task RunAsync_WhenJobFails_NotifierThrows_DoesNotMaskJobException()
    {
        var jobId = "test.fail.notifier";

        var notifier = new ThrowingNotifier();
        var job = new ThrowingJob(jobId, new NullReferenceException("boom"));

        using var sp = BuildServiceProvider(job, notifier);

        var runner = CreateRunner(sp, notifier);

        Func<Task> act = () => runner.RunAsync(jobId, new TestJobCancellationToken(CancellationToken.None));

        var ex = await act.Should().ThrowAsync<NgbUnexpectedException>();
        ex.Which.InnerException.Should().BeOfType<NullReferenceException>();
        ex.Which.Operation.Should().Be(jobId);

        notifier.CallCount.Should().Be(1);
        notifier.LastOutcome.Should().Be(PlatformJobRunOutcome.Failed);
    }

    [Fact]
    public async Task RunAsync_WhenCancelled_NotifiesCancelled_AndRethrows()
    {
        var jobId = "test.cancelled";

        using var cts = new CancellationTokenSource();
        cts.Cancel();

        var notifier = new SpyNotifier();
        var job = new DelayJob(jobId);

        using var sp = BuildServiceProvider(job, notifier);

        var runner = CreateRunner(sp, notifier);

        Func<Task> act = () => runner.RunAsync(jobId, new TestJobCancellationToken(cts.Token));

        await act.Should().ThrowAsync<OperationCanceledException>();

        notifier.Results.Should().ContainSingle(x => x.JobId == jobId && x.Outcome == PlatformJobRunOutcome.Cancelled);
    }

    [Fact]
    public async Task RunAsync_WhenNoImplementation_NotifierThrows_IsSwallowed_AndRunReturns()
    {
        var jobId = "test.missing";

        var notifier = new ThrowingNotifier();

        using var sp = BuildServiceProvider(job: null, notifier);

        var runner = CreateRunner(sp, notifier);

        await runner.RunAsync(jobId, new TestJobCancellationToken(CancellationToken.None));

        notifier.CallCount.Should().Be(1);
        notifier.LastOutcome.Should().Be(PlatformJobRunOutcome.SkippedNoImplementation);
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

    private static ServiceProvider BuildServiceProvider(IPlatformBackgroundJob? job, IPlatformJobNotifier notifier)
    {
        var services = new ServiceCollection();
        services.AddScoped<IJobRunMetrics, TestJobRunMetrics>();
        if (job is not null)
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

    private sealed class ThrowingNotifier : IPlatformJobNotifier
    {
        private int _callCount;

        public int CallCount => Volatile.Read(ref _callCount);

        public PlatformJobRunOutcome? LastOutcome { get; private set; }

        public Task NotifyAsync(PlatformJobRunResult result, CancellationToken cancellationToken)
        {
            Interlocked.Increment(ref _callCount);
            LastOutcome = result.Outcome;
            throw new NotSupportedException("notifier down");
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

    private sealed class ThrowingJob : IPlatformBackgroundJob
    {
        private readonly Exception _exception;

        public ThrowingJob(string jobId, Exception exception)
        {
            JobId = jobId;
            _exception = exception;
        }

        public string JobId { get; }

        public Task RunAsync(CancellationToken cancellationToken)
        {
            throw _exception;
        }
    }

    private sealed class DelayJob : IPlatformBackgroundJob
    {
        public DelayJob(string jobId) => JobId = jobId;

        public string JobId { get; }

        public Task RunAsync(CancellationToken cancellationToken)
        {
            return Task.Delay(TimeSpan.FromSeconds(30), cancellationToken);
        }
    }
}
