using FluentAssertions;
using Hangfire;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using NGB.BackgroundJobs.Catalog;
using NGB.BackgroundJobs.Contracts;
using NGB.BackgroundJobs.DependencyInjection;
using NGB.BackgroundJobs.Infrastructure;
using NGB.BackgroundJobs.IntegrationTests.Infrastructure;
using Xunit;

namespace NGB.BackgroundJobs.IntegrationTests.Hangfire;

[Collection(HangfirePostgresCollection.Name)]
public sealed class NgbHangfireJobRunner_DistributedLocking_P0Tests(HangfirePostgresFixture fixture)
{
    [Fact]
    public async Task RunAsync_WhenDistributedLockIsHeld_SkipsOverlap_AndDoesNotRequireScopeServices()
    {
        // Arrange: hold the distributed lock so the runner cannot acquire it.
        var jobId = PlatformJobCatalog.PlatformSchemaValidate;
        var lockResource = $"ngb:bgjob:{jobId}";

        using var holdConn = fixture.JobStorage.GetConnection();
        using var held = holdConn.AcquireDistributedLock(lockResource, TimeSpan.FromSeconds(30));

        // Intentionally do NOT register IJobRunMetrics or INgbBackgroundJob; if the runner incorrectly
        // tries to build a scope when the lock is held, it would throw.
        var services = new ServiceCollection();
        await using var sp = services.BuildServiceProvider();

        var notifier = new CapturingNotifier();
        var runner = new PlatformHangfireJobRunner(
            sp.GetRequiredService<IServiceScopeFactory>(),
            NullLogger<PlatformHangfireJobRunner>.Instance,
            notifier,
            fixture.JobStorage,
            Options.Create(new PlatformHangfireOptions { DistributedLockTimeoutSeconds = 1 }));

        // Act
        await runner.RunAsync(jobId, JobCancellationToken.Null);

        // Assert
        notifier.Results.Should().ContainSingle();
        notifier.Results[0].JobId.Should().Be(jobId);
        notifier.Results[0].Outcome.Should().Be(PlatformJobRunOutcome.SkippedOverlap);
        notifier.Results[0].Counters.Should().ContainKey("skipped_overlap");
        notifier.Results[0].Counters["skipped_overlap"].Should().Be(1);
    }

    [Fact]
    public async Task RunAsync_WhenNoJobImplementationRegistered_SkipsNoImplementation_AndNotifies()
    {
        // Arrange: no INgbBackgroundJob registrations.
        var jobId = "it.unknown.job";

        var services = new ServiceCollection();
        services.AddScoped<IJobRunMetrics, TestJobRunMetrics>();
        await using var sp = services.BuildServiceProvider();

        var notifier = new CapturingNotifier();
        var runner = new PlatformHangfireJobRunner(
            sp.GetRequiredService<IServiceScopeFactory>(),
            NullLogger<PlatformHangfireJobRunner>.Instance,
            notifier,
            fixture.JobStorage,
            Options.Create(new PlatformHangfireOptions { DistributedLockTimeoutSeconds = 5 }));

        // Act
        await runner.RunAsync(jobId, JobCancellationToken.Null);

        // Assert
        notifier.Results.Should().ContainSingle();
        notifier.Results[0].JobId.Should().Be(jobId);
        notifier.Results[0].Outcome.Should().Be(PlatformJobRunOutcome.SkippedNoImplementation);
        notifier.Results[0].Counters.Should().ContainKey("skipped_no_implementation");
        notifier.Results[0].Counters["skipped_no_implementation"].Should().Be(1);
    }

    private sealed class CapturingNotifier : IPlatformJobNotifier
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

    private sealed class TestJobRunMetrics : IJobRunMetrics
    {
        private readonly Dictionary<string, long> _counters = new(StringComparer.Ordinal);

        public void Increment(string name, long delta = 1)
        {
            if (string.IsNullOrWhiteSpace(name))
                return;
            if (delta == 0)
                return;

            name = name.Trim();

            _counters.TryGetValue(name, out var existing);
            _counters[name] = existing + delta;
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
                : new Dictionary<string, long>(_counters);
        }
    }
}
