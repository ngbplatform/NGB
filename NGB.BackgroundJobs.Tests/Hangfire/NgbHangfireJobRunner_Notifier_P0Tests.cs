using FluentAssertions;
using Hangfire;
using Hangfire.Storage;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Moq;
using NGB.BackgroundJobs.Contracts;
using NGB.BackgroundJobs.DependencyInjection;
using NGB.BackgroundJobs.Infrastructure;
using NGB.BackgroundJobs.Observability;
using NGB.BackgroundJobs.Tests.TestDoubles;
using NGB.Tools.Exceptions;

namespace NGB.BackgroundJobs.Tests.Hangfire;

public sealed class NgbHangfireJobRunner_Notifier_P0Tests
{
    [Fact]
    public async Task WhenJobSucceedsAndNoProblem_DoesNotNotify()
    {
        var jobId = "platform.schema.validate";

        var (runner, notifier, connection) = CreateRunner(
            jobsFactory: metrics => new IPlatformBackgroundJob[] { new SucceedingJob(jobId, metrics) });

        connection
            .Setup(x => x.AcquireDistributedLock(It.IsAny<string>(), It.IsAny<TimeSpan>()))
            .Returns(new DummyDisposable());

        var token = new Mock<IJobCancellationToken>();
        token.SetupGet(x => x.ShutdownToken).Returns(CancellationToken.None);

        await runner.RunAsync(jobId, token.Object);

        notifier.Results.Should().BeEmpty();
    }

    [Fact]
    public async Task WhenJobSucceedsButMarksProblem_NotifiesOnce()
    {
        var jobId = "platform.schema.validate";

        var (runner, notifier, connection) = CreateRunner(
            jobsFactory: metrics => new IPlatformBackgroundJob[] { new SucceedingButProblemJob(jobId, metrics) });

        connection
            .Setup(x => x.AcquireDistributedLock(It.IsAny<string>(), It.IsAny<TimeSpan>()))
            .Returns(new DummyDisposable());

        var token = new Mock<IJobCancellationToken>();
        token.SetupGet(x => x.ShutdownToken).Returns(CancellationToken.None);

        await runner.RunAsync(jobId, token.Object);

        notifier.Results.Should().HaveCount(1);
        var result = notifier.Results.Single();
        result.JobId.Should().Be(jobId);
        result.Outcome.Should().Be(PlatformJobRunOutcome.Succeeded);
        result.HasProblems.Should().BeTrue();
        result.Counters.Should().ContainKey("problem");
        result.Counters["problem"].Should().Be(1);
    }

    [Fact]
    public async Task WhenLockTimeout_SkippedOverlap_Notifies()
    {
        var jobId = "platform.schema.validate";

        var (runner, notifier, connection) = CreateRunner();

        connection
            .Setup(x => x.AcquireDistributedLock(It.IsAny<string>(), It.IsAny<TimeSpan>()))
            .Throws(new DistributedLockTimeoutException("timeout"));

        var token = new Mock<IJobCancellationToken>();
        token.SetupGet(x => x.ShutdownToken).Returns(CancellationToken.None);

        await runner.RunAsync(jobId, token.Object);

        notifier.Results.Should().HaveCount(1);
        var result = notifier.Results.Single();
        result.Outcome.Should().Be(PlatformJobRunOutcome.SkippedOverlap);
        result.Error.Should().BeOfType<NgbTimeoutException>();
        result.Error!.InnerException.Should().BeOfType<DistributedLockTimeoutException>();
    }

    [Fact]
    public async Task WhenNoImplementation_SkippedNoImplementation_Notifies()
    {
        var jobId = "platform.schema.validate";

        var (runner, notifier, connection) = CreateRunner();

        connection
            .Setup(x => x.AcquireDistributedLock(It.IsAny<string>(), It.IsAny<TimeSpan>()))
            .Returns(new DummyDisposable());

        var token = new Mock<IJobCancellationToken>();
        token.SetupGet(x => x.ShutdownToken).Returns(CancellationToken.None);

        await runner.RunAsync(jobId, token.Object);

        notifier.Results.Should().HaveCount(1);
        notifier.Results.Single().Outcome.Should().Be(PlatformJobRunOutcome.SkippedNoImplementation);
    }

        [Fact]
    public async Task WhenJobCancelled_NotifiesAndRethrows()
    {
        var jobId = "platform.schema.validate";

        var (runner, notifier, connection) = CreateRunner(
            jobsFactory: metrics => new IPlatformBackgroundJob[] { new CancellingJob(jobId, metrics) });

        connection
            .Setup(x => x.AcquireDistributedLock(It.IsAny<string>(), It.IsAny<TimeSpan>()))
            .Returns(new DummyDisposable());

        var token = new Mock<IJobCancellationToken>();
        token.SetupGet(x => x.ShutdownToken).Returns(CancellationToken.None);

        var act = async () => await runner.RunAsync(jobId, token.Object);
        await act.Should().ThrowAsync<OperationCanceledException>();

        notifier.Results.Should().HaveCount(1);
        notifier.Results.Single().Outcome.Should().Be(PlatformJobRunOutcome.Cancelled);
    }

[Fact]
    public async Task WhenNotifierThrows_DoesNotFailSuccessfulRun()
    {
        var jobId = "platform.schema.validate";

        var logger = new RecordingLogger<PlatformHangfireJobRunner>();
        var metrics = new JobRunMetrics();
        var notifier = new RecordingPlatformJobNotifier(throwOnNotify: true);

        var services = new ServiceCollection();
        services.AddSingleton<IJobRunMetrics>(metrics);
        services.AddSingleton<IPlatformBackgroundJob>(new SucceedingButProblemJob(jobId, metrics));

        var sp = services.BuildServiceProvider();
        var scopeFactory = sp.GetRequiredService<IServiceScopeFactory>();

        var connection = new Mock<IStorageConnection>(MockBehavior.Strict);
        connection.Setup(x => x.Dispose());
        connection
            .Setup(x => x.AcquireDistributedLock(It.IsAny<string>(), It.IsAny<TimeSpan>()))
            .Returns(new DummyDisposable());

        var storage = new TestJobStorage(connection.Object);

        var runner = new PlatformHangfireJobRunner(
            scopeFactory,
            logger,
            notifier,
            storage,
            Options.Create(new PlatformHangfireOptions { DistributedLockTimeoutSeconds = 1 }));

        var token = new Mock<IJobCancellationToken>();
        token.SetupGet(x => x.ShutdownToken).Returns(CancellationToken.None);

        await runner.RunAsync(jobId, token.Object);

        // The notifier throws, but it is best-effort: job run must still succeed.
        logger.Records.Should().Contain(x => x.Template  != null && x.Template.Contains("notification failed", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task WhenNotifierThrows_DoesNotMaskJobFailure()
    {
        var jobId = "platform.schema.validate";

        var logger = new RecordingLogger<PlatformHangfireJobRunner>();
        var metrics = new JobRunMetrics();
        var notifier = new RecordingPlatformJobNotifier(throwOnNotify: true);

        var services = new ServiceCollection();
        services.AddSingleton<IJobRunMetrics>(metrics);
        services.AddSingleton<IPlatformBackgroundJob>(new FailingJob(jobId, metrics));

        var sp = services.BuildServiceProvider();
        var scopeFactory = sp.GetRequiredService<IServiceScopeFactory>();

        var connection = new Mock<IStorageConnection>(MockBehavior.Strict);
        connection.Setup(x => x.Dispose());
        connection
            .Setup(x => x.AcquireDistributedLock(It.IsAny<string>(), It.IsAny<TimeSpan>()))
            .Returns(new DummyDisposable());

        var storage = new TestJobStorage(connection.Object);

        var runner = new PlatformHangfireJobRunner(
            scopeFactory,
            logger,
            notifier,
            storage,
            Options.Create(new PlatformHangfireOptions { DistributedLockTimeoutSeconds = 1 }));

        var token = new Mock<IJobCancellationToken>();
        token.SetupGet(x => x.ShutdownToken).Returns(CancellationToken.None);

        var act = async () => await runner.RunAsync(jobId, token.Object);
        var ex = await act.Should().ThrowAsync<NgbUnexpectedException>();
        ex.Which.InnerException.Should().BeOfType<NullReferenceException>();

        // Notifier failure should be logged, but must not replace the original job exception.
        logger.Records.Should().Contain(x => x.Template != null && x.Template.Contains("notification failed", StringComparison.OrdinalIgnoreCase));
    }

    private static (PlatformHangfireJobRunner Runner, RecordingPlatformJobNotifier Notifier, Mock<IStorageConnection> ConnectionMock)
        CreateRunner(Func<IJobRunMetrics, IEnumerable<IPlatformBackgroundJob>>? jobsFactory = null)
    {
        var logger = new RecordingLogger<PlatformHangfireJobRunner>();
        var metrics = new JobRunMetrics();
        var notifier = new RecordingPlatformJobNotifier();

        var services = new ServiceCollection();
        services.AddSingleton<IJobRunMetrics>(metrics);

        if (jobsFactory is not null)
            foreach (var job in jobsFactory(metrics))
                services.AddSingleton<IPlatformBackgroundJob>(job);

        var sp = services.BuildServiceProvider();
        var scopeFactory = sp.GetRequiredService<IServiceScopeFactory>();

        var connection = new Mock<IStorageConnection>(MockBehavior.Strict);
        connection.Setup(x => x.Dispose());

        var storage = new TestJobStorage(connection.Object);

        var runner = new PlatformHangfireJobRunner(
            scopeFactory,
            logger,
            notifier,
            storage,
            Options.Create(new PlatformHangfireOptions { DistributedLockTimeoutSeconds = 1 }));

        return (runner, notifier, connection);
    }

    private sealed class DummyDisposable : IDisposable
    {
        public void Dispose() { }
    }

    private sealed class TestJobStorage(IStorageConnection connection) : JobStorage
    {
        public override IStorageConnection GetConnection() => connection;
        public override IMonitoringApi GetMonitoringApi() => new Mock<IMonitoringApi>().Object;
    }

    private sealed class SucceedingJob(string jobId, IJobRunMetrics metrics) : IPlatformBackgroundJob
    {
        public string JobId => jobId;

        public Task RunAsync(CancellationToken cancellationToken)
        {
            metrics.Set("items_total", 1);
            metrics.Set("items_processed", 1);
            return Task.CompletedTask;
        }
    }

    private sealed class SucceedingButProblemJob(string jobId, IJobRunMetrics metrics) : IPlatformBackgroundJob
    {
        public string JobId => jobId;

        public Task RunAsync(CancellationToken cancellationToken)
        {
            metrics.Set("items_total", 1);
            metrics.Set("items_processed", 1);

            // Success, but it still indicates a problem worth notifying on.
            metrics.Set("problem", 1);

            return Task.CompletedTask;
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

    private sealed class FailingJob(string jobId, IJobRunMetrics metrics) : IPlatformBackgroundJob
    {
        public string JobId => jobId;

        public Task RunAsync(CancellationToken cancellationToken)
        {
            metrics.Set("items_total", 1);
            metrics.Increment("items_failed", 1);
            throw new NullReferenceException("boom");
        }
    }
}
