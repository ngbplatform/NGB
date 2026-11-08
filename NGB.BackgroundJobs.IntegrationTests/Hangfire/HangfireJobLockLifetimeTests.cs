using System.Collections.Concurrent;
using FluentAssertions;
using Hangfire;
using Hangfire.PostgreSql;
using Hangfire.PostgreSql.Factories;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using NGB.BackgroundJobs.Contracts;
using NGB.BackgroundJobs.DependencyInjection;
using NGB.BackgroundJobs.Infrastructure;
using NGB.BackgroundJobs.IntegrationTests.Infrastructure;
using NGB.BackgroundJobs.PostgreSql;
using NGB.BackgroundJobs.PostgreSql.DependencyInjection;
using NGB.Persistence.BackgroundJobs;
using NGB.Tools.Exceptions;
using Npgsql;
using Xunit;

namespace NGB.BackgroundJobs.IntegrationTests.Hangfire;

[Collection(HangfirePostgresCollection.Name)]
public sealed class HangfireJobLockLifetimeTests(HangfirePostgresFixture fixture)
{
    [Fact]
    public async Task RunningJob_MustRemainProtectedAfterStorageLockLifetime()
    {
        // Shorten only the provider's lock lifetime to exercise expiry without a ten-minute test.
        // Lock acquisition, the runner, and PostgreSQL persistence remain real.
        var storageOptions = new PostgreSqlStorageOptions
        {
            SchemaName = "it_lock_lifetime",
            PrepareSchemaIfNecessary = true,
            DistributedLockTimeout = TimeSpan.FromSeconds(4)
        };
        var storage = new PostgreSqlStorage(
            new NpgsqlConnectionFactory(fixture.ConnectionString, storageOptions, connectionSetup: null!),
            storageOptions);

        var job = new BlockingJob();
        var notifier = new CapturingNotifier();
        await using var provider = BuildServiceProvider(job);
        await using var competingProvider = BuildServiceProvider(job);
        var runner = CreateRunner(provider, storage, notifier, storageOptions.SchemaName);
        var competingRunner = CreateRunner(competingProvider, storage, notifier, storageOptions.SchemaName);
        var cancellation = new TestJobCancellationToken(CancellationToken.None);

        var firstRun = Task.Run(() => runner.RunAsync(job.JobId, cancellation));
        try
        {
            await job.Started.Task.WaitAsync(TimeSpan.FromSeconds(10));

            // Positive control: another runner invocation cannot overlap before expiry.
            await Task.Run(() => competingRunner.RunAsync(job.JobId, cancellation))
                .WaitAsync(TimeSpan.FromSeconds(10));
            job.InvocationCount.Should().Be(1);
            notifier.Results.Should().Contain(x => x.Outcome == PlatformJobRunOutcome.SkippedOverlap);

            await Task.Delay(storageOptions.DistributedLockTimeout + TimeSpan.FromSeconds(1));
            firstRun.IsCompleted.Should().BeFalse("the original job is deliberately still running");

            await Task.Run(() => competingRunner.RunAsync(job.JobId, cancellation))
                .WaitAsync(TimeSpan.FromSeconds(10));
            job.InvocationCount.Should().Be(1, "the runner promises to skip overlaps for the entire running job");
        }
        finally
        {
            job.Release.TrySetResult();
            await firstRun.WaitAsync(TimeSpan.FromSeconds(10));
        }

        await runner.RunAsync(job.JobId, cancellation);
        job.InvocationCount.Should().Be(2, "completion must release the lock for the next run");
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task FailedOrCancelledJob_ReleasesExecutionLock(bool cancel)
    {
        var job = new BlockingJob("test.lock-release", failOnRelease: !cancel);
        var notifier = new CapturingNotifier();
        await using var provider = BuildServiceProvider(job);
        var runner = CreateRunner(provider, fixture.JobStorage, notifier);
        using var cancellation = new CancellationTokenSource();

        var run = runner.RunAsync(job.JobId, new TestJobCancellationToken(cancellation.Token));
        await job.Started.Task.WaitAsync(TimeSpan.FromSeconds(10));
        if (cancel)
            await cancellation.CancelAsync();
        else
            job.Release.TrySetResult();

        Func<Task> finish = () => run.WaitAsync(TimeSpan.FromSeconds(10));
        if (cancel)
            await finish.Should().ThrowAsync<OperationCanceledException>();
        else
            await finish.Should().ThrowAsync<NgbUnexpectedException>();

        await runner.RunAsync(job.JobId, new TestJobCancellationToken(CancellationToken.None));
        job.InvocationCount.Should().Be(2);
        notifier.Results.Should().ContainSingle(x => x.Outcome ==
            (cancel ? PlatformJobRunOutcome.Cancelled : PlatformJobRunOutcome.Failed));
        notifier.Results.Should().NotContain(x => x.Outcome == PlatformJobRunOutcome.SkippedOverlap);
    }

    [Fact]
    public async Task DifferentJobs_CanRunConcurrently()
    {
        var first = new BlockingJob("test.parallel-guard.first");
        var second = new BlockingJob("test.parallel-guard.second");
        await using var provider = BuildServiceProvider(first, second);
        var runner = CreateRunner(provider, fixture.JobStorage, new CapturingNotifier());

        var firstRun = runner.RunAsync(first.JobId, new TestJobCancellationToken(CancellationToken.None));
        var secondRun = runner.RunAsync(second.JobId, new TestJobCancellationToken(CancellationToken.None));
        try
        {
            await Task.WhenAll(first.Started.Task, second.Started.Task).WaitAsync(TimeSpan.FromSeconds(10));
            firstRun.IsCompleted.Should().BeFalse();
            secondRun.IsCompleted.Should().BeFalse();
        }
        finally
        {
            first.Release.TrySetResult();
            second.Release.TrySetResult();
            await Task.WhenAll(firstRun, secondRun).WaitAsync(TimeSpan.FromSeconds(10));
        }
    }

    [Fact]
    public async Task LostExecutionLock_CancelsRunningJob_AndAllowsNextRun()
    {
        var applicationName = $"it-job-lock-{Guid.NewGuid():N}";
        var connectionString = new NpgsqlConnectionStringBuilder(fixture.ConnectionString)
        {
            ApplicationName = applicationName
        }.ConnectionString;
        var job = new BlockingJob("test.lost-lock");
        var notifier = new CapturingNotifier();
        await using var provider = BuildServiceProvider(job);
        var runner = CreateRunner(provider, fixture.JobStorage, notifier, connectionString: connectionString);

        var run = runner.RunAsync(job.JobId, new TestJobCancellationToken(CancellationToken.None));
        try
        {
            await job.Started.Task.WaitAsync(TimeSpan.FromSeconds(10));

            // Terminate only this test's execution-lock session in the disposable test database.
            await using var connection = new NpgsqlConnection(fixture.ConnectionString);
            await connection.OpenAsync();
            await using var command = connection.CreateCommand();
            command.CommandText = """
                                  SELECT pg_terminate_backend(pid)
                                  FROM pg_stat_activity
                                  WHERE application_name = @ApplicationName;
                                  """;
            command.Parameters.AddWithValue("ApplicationName", applicationName);
            (await command.ExecuteScalarAsync()).Should().Be(true);

            Func<Task> finish = () => run.WaitAsync(TimeSpan.FromSeconds(20));
            await finish.Should().ThrowAsync<OperationCanceledException>();
            notifier.Results.Should().ContainSingle(x =>
                x.Outcome == PlatformJobRunOutcome.Cancelled && x.Counters["execution_lock_lost"] == 1);
        }
        finally
        {
            job.Release.TrySetResult();
            try
            {
                await run.WaitAsync(TimeSpan.FromSeconds(10));
            }
            catch (OperationCanceledException)
            {
                // Expected after the execution-lock session is terminated.
            }
        }

        await runner.RunAsync(job.JobId, new TestJobCancellationToken(CancellationToken.None));
        job.InvocationCount.Should().Be(2);
    }

    [Fact]
    public async Task FactoryStorage_UsesItsActualConnectionAndNamespace_WithoutDuplicateOptions()
    {
        const string storageNamespace = "it_factory_guard";
        var storage = PostgresHangfireJobStorageFactory.Create(fixture.ConnectionString, storageNamespace, true);
        var job = new BlockingJob("test.factory-guard");
        job.Release.TrySetResult();
        var notifier = new CapturingNotifier();
        var services = new ServiceCollection();
        services.AddNgbPostgresBackgroundJobsAdapter();
        services.AddSingleton(storage);
        services.AddSingleton<IPlatformBackgroundJob>(job);
        services.AddScoped<IJobRunMetrics, TestMetrics>();
        await using var provider = services.BuildServiceProvider();
        var runner = new PlatformHangfireJobRunner(
            provider.GetRequiredService<IServiceScopeFactory>(),
            NullLogger<PlatformHangfireJobRunner>.Instance,
            notifier,
            storage,
            Options.Create(new PlatformHangfireOptions()));

        var control = new PostgresBackgroundJobExecutionLockProvider();
        await using (var held = await control.TryAcquireAsync(new BackgroundJobExecutionLockRequest(
                         fixture.ConnectionString, storageNamespace, $"ngb:bgjob:{job.JobId}", TimeSpan.Zero)))
        {
            held.Should().NotBeNull();
            await runner.RunAsync(job.JobId, new TestJobCancellationToken(CancellationToken.None));
            job.InvocationCount.Should().Be(0);
            notifier.Results.Should().ContainSingle(x => x.Outcome == PlatformJobRunOutcome.SkippedOverlap);
        }

        await runner.RunAsync(job.JobId, new TestJobCancellationToken(CancellationToken.None));
        job.InvocationCount.Should().Be(1);
    }

    [Fact]
    public async Task ExecutionLocks_InDifferentStorageNamespaces_DoNotConflict()
    {
        var locks = new PostgresBackgroundJobExecutionLockProvider();
        var request = new BackgroundJobExecutionLockRequest(
            fixture.ConnectionString, "first_jobs", "ngb:bgjob:same-job", TimeSpan.Zero);
        await using var first = await locks.TryAcquireAsync(request);
        await using var second = await locks.TryAcquireAsync(request with { StorageNamespace = "second_jobs" });
        await using var overlap = await locks.TryAcquireAsync(request);

        first.Should().NotBeNull();
        second.Should().NotBeNull();
        overlap.Should().BeNull();
    }

    private static ServiceProvider BuildServiceProvider(params IPlatformBackgroundJob[] jobs)
    {
        var services = new ServiceCollection();
        services.AddNgbPostgresBackgroundJobsAdapter();
        services.AddScoped<IJobRunMetrics, TestMetrics>();
        foreach (var job in jobs)
            services.AddSingleton(job);
        return services.BuildServiceProvider();
    }

    private PlatformHangfireJobRunner CreateRunner(
        IServiceProvider provider,
        JobStorage storage,
        IPlatformJobNotifier notifier,
        string storageNamespace = "hangfire",
        string? connectionString = null)
    {
        return new PlatformHangfireJobRunner(
            provider.GetRequiredService<IServiceScopeFactory>(),
            NullLogger<PlatformHangfireJobRunner>.Instance,
            notifier,
            storage,
            Options.Create(new PlatformHangfireOptions
            {
                ConnectionString = connectionString ?? fixture.ConnectionString,
                StorageNamespace = storageNamespace,
                DistributedLockTimeoutSeconds = 1
            }));
    }

    private sealed class TestJobCancellationToken(CancellationToken shutdownToken) : IJobCancellationToken
    {
        public CancellationToken ShutdownToken => shutdownToken;

        public void ThrowIfCancellationRequested() => shutdownToken.ThrowIfCancellationRequested();
    }

    private sealed class BlockingJob(string jobId = "test.lock-lifetime", bool failOnRelease = false) : IPlatformBackgroundJob
    {
        private int _invocationCount;

        public string JobId => jobId;
        public int InvocationCount => Volatile.Read(ref _invocationCount);
        public TaskCompletionSource Started { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource Release { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public async Task RunAsync(CancellationToken cancellationToken)
        {
            if (Interlocked.Increment(ref _invocationCount) == 1)
            {
                Started.TrySetResult();
                await Release.Task.WaitAsync(cancellationToken);
                if (failOnRelease)
                    throw new InvalidOperationException("Test job failure.");
            }
        }
    }

    private sealed class CapturingNotifier : IPlatformJobNotifier
    {
        public ConcurrentQueue<PlatformJobRunResult> Results { get; } = new();

        public Task NotifyAsync(PlatformJobRunResult result, CancellationToken cancellationToken)
        {
            Results.Enqueue(result);
            return Task.CompletedTask;
        }
    }

    private sealed class TestMetrics : IJobRunMetrics
    {
        private readonly Dictionary<string, long> _counters = new(StringComparer.Ordinal);

        public void Increment(string name, long delta = 1)
        {
            _counters.TryGetValue(name, out var current);
            _counters[name] = current + delta;
        }

        public void Set(string name, long value)
        {
            _counters[name] = value;
        }

        public IReadOnlyDictionary<string, long> Snapshot() => new Dictionary<string, long>(_counters);
    }
}
