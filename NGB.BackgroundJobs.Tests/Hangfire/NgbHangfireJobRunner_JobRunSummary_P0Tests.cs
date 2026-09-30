using FluentAssertions;
using Hangfire;
using Hangfire.Storage;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Moq;
using NGB.BackgroundJobs.Contracts;
using NGB.BackgroundJobs.DependencyInjection;
using NGB.BackgroundJobs.Infrastructure;
using NGB.BackgroundJobs.Tests.TestDoubles;
using NGB.Tools.Exceptions;

namespace NGB.BackgroundJobs.Tests.Hangfire;

public sealed class NgbHangfireJobRunner_JobRunSummary_P0Tests
{
    [Fact]
    public async Task WhenLockTimeout_SkipsWithSkippedOverlapOutcome()
    {
        var jobId = "platform.schema.validate";

        var (runner, logger, _, connection) = CreateRunner(jobId);

        connection
            .Setup(x => x.AcquireDistributedLock(It.IsAny<string>(), It.IsAny<TimeSpan>()))
            .Throws(new DistributedLockTimeoutException("timeout"));

        var token = new Mock<IJobCancellationToken>();
        token.SetupGet(x => x.ShutdownToken).Returns(CancellationToken.None);

        await runner.RunAsync(jobId, token.Object);

        var summary = SingleSummary(logger);
        summary.Level.Should().Be(Microsoft.Extensions.Logging.LogLevel.Information);
        summary.State["Outcome"].Should().Be("SkippedOverlap");

        var counters = summary.State["Counters"].Should().BeAssignableTo<IReadOnlyDictionary<string, long>>().Subject;
        counters.Should().ContainKey("skipped_overlap");
        counters["skipped_overlap"].Should().Be(1);
    }

    [Fact]
    public async Task WhenConfiguredLockTimeoutIsNotPositive_UsesOneSecondBoundary()
    {
        var jobId = "platform.schema.validate";
        var options = new PlatformHangfireOptions { DistributedLockTimeoutSeconds = 0 };
        var (runner, _, _, connection) = CreateRunner(jobId, runnerOptions: options);
        TimeSpan receivedTimeout = default;
        connection
            .Setup(x => x.AcquireDistributedLock(It.IsAny<string>(), It.IsAny<TimeSpan>()))
            .Callback<string, TimeSpan>((_, timeout) => receivedTimeout = timeout)
            .Throws(new DistributedLockTimeoutException("timeout"));
        var token = Mock.Of<IJobCancellationToken>(x => x.ShutdownToken == CancellationToken.None);

        await runner.RunAsync(jobId, token);

        receivedTimeout.Should().Be(TimeSpan.FromSeconds(1));
    }

    [Fact]
    public async Task WhenNoImplementation_SkipsWithSkippedNoImplementationOutcome()
    {
        var jobId = "platform.schema.validate";

        // No job implementation is registered for this JobId.
        var (runner, logger, _, connection) = CreateRunner(jobId);

        connection
            .Setup(x => x.AcquireDistributedLock(It.IsAny<string>(), It.IsAny<TimeSpan>()))
            .Returns(new DummyDisposable());

        var token = new Mock<IJobCancellationToken>();
        token.SetupGet(x => x.ShutdownToken).Returns(CancellationToken.None);

        await runner.RunAsync(jobId, token.Object);

        var summary = SingleSummary(logger);
        summary.Level.Should().Be(Microsoft.Extensions.Logging.LogLevel.Warning);
        summary.State["Outcome"].Should().Be("SkippedNoImplementation");

        var counters = summary.State["Counters"].Should().BeAssignableTo<IReadOnlyDictionary<string, long>>().Subject;
        counters.Should().ContainKey("skipped_no_implementation");
        counters["skipped_no_implementation"].Should().Be(1);
    }

    [Fact]
    public async Task WhenJobSucceeds_LogsSucceededSummaryWithCounters()
    {
        var jobId = "platform.schema.validate";

        var (runner, logger, _, connection) = CreateRunner(jobId, jobsFactory: m =>
            new IPlatformBackgroundJob[] { new SucceedingJob(jobId, m) });

        connection
            .Setup(x => x.AcquireDistributedLock(It.IsAny<string>(), It.IsAny<TimeSpan>()))
            .Returns(new DummyDisposable());

        var token = new Mock<IJobCancellationToken>();
        token.SetupGet(x => x.ShutdownToken).Returns(CancellationToken.None);

        await runner.RunAsync(jobId, token.Object);

        var summary = SingleSummary(logger);
        summary.Level.Should().Be(Microsoft.Extensions.Logging.LogLevel.Information);
        summary.State["Outcome"].Should().Be("Succeeded");

        var counters = summary.State["Counters"].Should().BeAssignableTo<IReadOnlyDictionary<string, long>>().Subject;
        counters["items_total"].Should().Be(2);
        counters["items_processed"].Should().Be(2);
    }

    [Fact]
    public async Task WhenClockMovesBackwards_ClampsSuccessfulDurationToZero()
    {
        var jobId = "platform.schema.validate";
        var clock = new SequenceTimeProvider(
            DateTimeOffset.UnixEpoch.AddSeconds(10),
            DateTimeOffset.UnixEpoch);
        var (runner, logger, _, connection) = CreateRunner(
            jobId,
            jobsFactory: metrics => [new SucceedingJob(jobId, metrics)],
            timeProvider: clock);
        connection
            .Setup(x => x.AcquireDistributedLock(It.IsAny<string>(), It.IsAny<TimeSpan>()))
            .Returns(new DummyDisposable());

        await runner.RunAsync(jobId, Mock.Of<IJobCancellationToken>(x => x.ShutdownToken == CancellationToken.None));

        SingleSummary(logger).State["DurationMs"].Should().Be(0d);
    }

    [Fact]
    public async Task WhenJobFails_LogsFailedSummaryWithExceptionAndCounters()
    {
        var jobId = "platform.schema.validate";

        var (runner, logger, _, connection) = CreateRunner(jobId, jobsFactory: m =>
            new IPlatformBackgroundJob[] { new FailingJob(jobId, m) });

        connection
            .Setup(x => x.AcquireDistributedLock(It.IsAny<string>(), It.IsAny<TimeSpan>()))
            .Returns(new DummyDisposable());

        var token = new Mock<IJobCancellationToken>();
        token.SetupGet(x => x.ShutdownToken).Returns(CancellationToken.None);

        var act = async () => await runner.RunAsync(jobId, token.Object);
        var ex = await act.Should().ThrowAsync<NgbUnexpectedException>();
        ex.Which.InnerException.Should().BeOfType<NullReferenceException>();
        ex.Which.Operation.Should().Be(jobId);
        ex.Which.Context.Should().ContainKey("runId");

        var summary = SingleSummary(logger);
        summary.Level.Should().Be(Microsoft.Extensions.Logging.LogLevel.Error);
        summary.State["Outcome"].Should().Be("Failed");
        summary.Exception.Should().BeOfType<NgbUnexpectedException>();

        var counters = summary.State["Counters"].Should().BeAssignableTo<IReadOnlyDictionary<string, long>>().Subject;
        counters["items_total"].Should().Be(5);
        counters["items_failed"].Should().Be(1);
    }

    [Fact]
    public async Task WhenJobCancelled_LogsCancelledSummaryAndRethrows()
    {
        var jobId = "platform.schema.validate";

        var (runner, logger, _, connection) = CreateRunner(jobId, jobsFactory: m =>
            new IPlatformBackgroundJob[] { new CancellingJob(jobId, m) });

        connection
            .Setup(x => x.AcquireDistributedLock(It.IsAny<string>(), It.IsAny<TimeSpan>()))
            .Returns(new DummyDisposable());

        var token = new Mock<IJobCancellationToken>();
        token.SetupGet(x => x.ShutdownToken).Returns(CancellationToken.None);

        var act = async () => await runner.RunAsync(jobId, token.Object);
        await act.Should().ThrowAsync<OperationCanceledException>();

        var summary = SingleSummary(logger);
        summary.Level.Should().Be(Microsoft.Extensions.Logging.LogLevel.Warning);
        summary.State["Outcome"].Should().Be("Cancelled");

        var counters = summary.State["Counters"].Should().BeAssignableTo<IReadOnlyDictionary<string, long>>().Subject;
        counters["items_total"].Should().Be(1);
        counters["items_cancelled"].Should().Be(1);
    }

    private static LogRecord SingleSummary(RecordingLogger<PlatformHangfireJobRunner> logger)
    {
        return logger.Records.Single(x => x.Template is not null && x.Template.Contains("JobRunSummary", StringComparison.Ordinal));
    }

    private static (PlatformHangfireJobRunner Runner, RecordingLogger<PlatformHangfireJobRunner> Logger, IJobRunMetrics Metrics, Mock<IStorageConnection> ConnectionMock)
        CreateRunner(
            string jobId,
            Func<IJobRunMetrics, IEnumerable<IPlatformBackgroundJob>>? jobsFactory = null,
            PlatformHangfireOptions? runnerOptions = null,
            TimeProvider? timeProvider = null)
    {
        var logger = new RecordingLogger<PlatformHangfireJobRunner>();
        IJobRunMetrics metrics = new TestJobRunMetrics();

        var services = new ServiceCollection();
        services.AddSingleton<IJobRunMetrics>(metrics);

        if (jobsFactory is not null)
            foreach (var job in jobsFactory(metrics))
                services.AddSingleton<IPlatformBackgroundJob>(job);

        // Required by runner's CreateScope()
        var sp = services.BuildServiceProvider();
        var scopeFactory = sp.GetRequiredService<IServiceScopeFactory>();

        var connection = new Mock<IStorageConnection>(MockBehavior.Strict);
        connection.Setup(x => x.Dispose());

        // Runner only uses AcquireDistributedLock.
        var storage = new TestJobStorage(connection.Object);

        var runner = new PlatformHangfireJobRunner(
            scopeFactory,
            logger,
            new NullPlatformJobNotifier(),
            storage,
            Options.Create(runnerOptions ?? new PlatformHangfireOptions
            {
                DistributedLockTimeoutSeconds = 1
            }),
            timeProvider);

        return (runner, logger, metrics, connection);
    }

    private sealed class DummyDisposable : IDisposable
    {
        public void Dispose() { }
    }

    private sealed class SequenceTimeProvider(params DateTimeOffset[] values) : TimeProvider
    {
        private readonly Queue<DateTimeOffset> _values = new(values);
        public override DateTimeOffset GetUtcNow() => _values.Dequeue();
    }

    private sealed class TestJobStorage(IStorageConnection connection) : JobStorage
    {
        public override IStorageConnection GetConnection() => connection;

        public override IMonitoringApi GetMonitoringApi() => new Mock<IMonitoringApi>().Object;
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
            if (_counters.TryGetValue(name, out var existing))
                _counters[name] = existing + delta;
            else
                _counters[name] = delta;
        }

        public void Set(string name, long value)
        {
            if (string.IsNullOrWhiteSpace(name))
                return;
            _counters[name] = value;
        }

        public IReadOnlyDictionary<string, long> Snapshot() => new Dictionary<string, long>(_counters);
    }

    private sealed class SucceedingJob(string jobId, IJobRunMetrics metrics) : IPlatformBackgroundJob
    {
        public string JobId => jobId;

        public Task RunAsync(CancellationToken cancellationToken)
        {
            metrics.Set("items_total", 2);
            metrics.Set("items_processed", 2);
            return Task.CompletedTask;
        }
    }

    private sealed class FailingJob(string jobId, IJobRunMetrics metrics) : IPlatformBackgroundJob
    {
        public string JobId => jobId;

        public Task RunAsync(CancellationToken cancellationToken)
        {
            metrics.Set("items_total", 5);
            metrics.Increment("items_failed", 1);
            throw new NullReferenceException("boom");
        }
    }

    private sealed class CancellingJob(string jobId, IJobRunMetrics metrics) : IPlatformBackgroundJob
    {
        public string JobId => jobId;

        public Task RunAsync(CancellationToken cancellationToken)
        {
            metrics.Set("items_total", 1);
            metrics.Increment("items_cancelled", 1);
            throw new OperationCanceledException();
        }
    }
}
