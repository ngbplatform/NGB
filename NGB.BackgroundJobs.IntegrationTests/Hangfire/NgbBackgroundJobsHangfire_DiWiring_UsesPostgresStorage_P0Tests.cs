using FluentAssertions;
using Hangfire;
using Hangfire.PostgreSql;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using NGB.BackgroundJobs.Contracts;
using NGB.BackgroundJobs.DependencyInjection;
using NGB.BackgroundJobs.Infrastructure;
using NGB.BackgroundJobs.IntegrationTests.Infrastructure;
using NGB.PostgreSql.DependencyInjection;
using NGB.Runtime.DependencyInjection;
using Xunit;

namespace NGB.BackgroundJobs.IntegrationTests.Hangfire;

[Collection(HangfirePostgresCollection.Name)]
public sealed class NgbBackgroundJobsHangfire_DiWiring_UsesPostgresStorage_P0Tests(HangfirePostgresFixture fixture)
{
    [Fact]
    public async Task RunAsync_UsesJobStorageFromDI_AndRespectsDistributedLock()
    {
        var jobId = "test.di.lock";

        var notifier = new SpyNotifier();
        var counter = new Counter();

        using var sp = BuildServiceProvider(fixture.ConnectionString, notifier, counter);

        var runner = sp.GetRequiredService<PlatformHangfireJobRunner>();
        var storage = sp.GetRequiredService<JobStorage>();

        storage.Should().BeOfType<PostgreSqlStorage>("AddNgbBackgroundJobsHangfire must configure Hangfire.PostgreSql storage");

        // Hold the same distributed lock resource used by the runner.
        using (var conn = storage.GetConnection())
        using (conn.AcquireDistributedLock($"ngb:bgjob:{jobId}", TimeSpan.FromSeconds(30)))
        {
            await runner.RunAsync(jobId, new TestJobCancellationToken());
        }

        // Job should NOT have executed (skipped by lock timeout).
        counter.Value.Should().Be(0);

        notifier.Results.Should().ContainSingle(r =>
            r.JobId == jobId && r.Outcome == PlatformJobRunOutcome.SkippedOverlap);
    }

    [Fact]
    public async Task RunAsync_RunsJob_WhenLockIsFree_AndDoesNotNotify()
    {
        var jobId = "test.di.free";

        var notifier = new SpyNotifier();
        var counter = new Counter();

        using var sp = BuildServiceProvider(fixture.ConnectionString, notifier, counter);

        var runner = sp.GetRequiredService<PlatformHangfireJobRunner>();

        await runner.RunAsync(jobId, new TestJobCancellationToken());

        counter.Value.Should().Be(1);
        notifier.Results.Should().BeEmpty("succeeded runs are not notified");
    }

    private static ServiceProvider BuildServiceProvider(
        string connectionString,
        IPlatformJobNotifier notifier,
        Counter counter)
    {
        var services = new ServiceCollection();

        services.AddLogging();

        // Platform services needed to resolve the default job implementations registered by AddNgbBackgroundJobsHangfire.
        services.AddNgbPostgres(connectionString);
        services.AddNgbRuntime();

        // Override notifier before AddNgbBackgroundJobsHangfire registers defaults (TryAdd).
        services.AddSingleton<IPlatformJobNotifier>(notifier);

        // A simple test job (registered in addition to the platform catalog).
        services.AddSingleton(counter);
        services.AddTransient<IPlatformBackgroundJob>(_ => new CountingJob("test.di.lock", counter));
        services.AddTransient<IPlatformBackgroundJob>(_ => new CountingJob("test.di.free", counter));

        services.AddPlatformBackgroundJobsHangfire(o =>
        {
            o.ConnectionString = connectionString;
            o.PrepareSchemaIfNecessary = true;
            o.WorkerCount = 1;
            o.DistributedLockTimeoutSeconds = 1;
        });

        // Avoid noisy console logging from Hangfire internals during tests.
        services.AddSingleton<ILoggerFactory>(_ => NullLoggerFactory.Instance);

        return services.BuildServiceProvider();
    }

    private sealed class CountingJob(string jobId, Counter counter) : IPlatformBackgroundJob
    {
        public string JobId { get; } = jobId;

        public Task RunAsync(CancellationToken cancellationToken)
        {
            Interlocked.Increment(ref counter.Value);
            return Task.CompletedTask;
        }
    }

    private sealed class Counter
    {
        public int Value;
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
}
