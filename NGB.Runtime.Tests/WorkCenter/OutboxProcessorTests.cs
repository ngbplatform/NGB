using System.Data.Common;
using System.Diagnostics;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using NGB.Application.Abstractions.Services;
using NGB.Core.Events;
using NGB.Persistence.Outbox;
using NGB.Persistence.UnitOfWork;
using NGB.Runtime.WorkCenter;
using Xunit;

namespace NGB.Runtime.Tests.WorkCenter;

[Collection(Observability.TelemetrySerialCollection.Name)]
public sealed class OutboxProcessorTests
{
    private static readonly DateTime Now = new(2026, 7, 26, 19, 0, 0, DateTimeKind.Utc);

    [Theory]
    [InlineData(0, 1)]
    [InlineData(750, 500)]
    public async Task Empty_batches_are_clamped_and_return_zero(int requested, int expected)
    {
        var uow = new RecordingUnitOfWork();
        var outbox = new Mock<IOutboxEventRepository>(MockBehavior.Strict);
        outbox.Setup(repository => repository.ClaimBatchAsync(
                "work-center", expected, Now, It.IsAny<CancellationToken>()))
            .ReturnsAsync([]);
        var processor = Processor(uow, outbox, [], new Mock<IWorkCenterRealtimeNotifier>().Object);

        (await processor.ProcessBatchAsync(requested, CancellationToken.None)).Should().Be(0);

        uow.CommitCount.Should().Be(1);
        outbox.VerifyAll();
    }

    [Fact]
    public async Task Matching_policies_complete_once_and_realtime_failures_do_not_replay_business_work()
    {
        var uow = new RecordingUnitOfWork();
        var outbox = new Mock<IOutboxEventRepository>(MockBehavior.Strict);
        var matching = new Mock<IWorkCenterEventPolicy>(MockBehavior.Strict);
        var other = new Mock<IWorkCenterEventPolicy>(MockBehavior.Strict);
        var realtime = new Mock<IWorkCenterRealtimeNotifier>(MockBehavior.Strict);
        var item = WorkItem("TEST.EVENT", attempt: 2);
        outbox.Setup(repository => repository.ClaimBatchAsync(
                "work-center", 25, Now, It.IsAny<CancellationToken>()))
            .ReturnsAsync([item]);
        matching.SetupGet(policy => policy.EventType).Returns("test.event");
        matching.Setup(policy => policy.HandleAsync(
                It.Is<WorkCenterEventContext>(context => context.Event == item.Event),
                It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);
        other.SetupGet(policy => policy.EventType).Returns("different.event");
        outbox.Setup(repository => repository.MarkCompletedAsync(
                item.Event.EventId,
                "work-center",
                2,
                Now,
                It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);
        realtime.Setup(notifier => notifier.NotifyChangedAsync(
                Now.Ticks, It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("socket unavailable"));
        var processor = Processor(uow, outbox, [matching.Object, other.Object], realtime.Object);

        (await processor.ProcessBatchAsync(25, CancellationToken.None)).Should().Be(1);

        matching.VerifyAll();
        other.Verify(policy => policy.HandleAsync(
            It.IsAny<WorkCenterEventContext>(), It.IsAny<CancellationToken>()), Times.Never);
        outbox.Verify(repository => repository.MarkFailedAsync(
            It.IsAny<Guid>(), It.IsAny<string>(), It.IsAny<int>(), It.IsAny<DateTime>(),
            It.IsAny<DateTime?>(), It.IsAny<string>(), It.IsAny<bool>(),
            It.IsAny<CancellationToken>()), Times.Never);
        uow.CommitCount.Should().Be(2);
    }

    [Fact]
    public async Task Successful_projection_marks_the_activity_and_publishes_realtime_invalidation()
    {
        using var listener = new ActivityListener
        {
            ShouldListenTo = source => source.Name == "NGB.Platform.DocumentActionsWorkCenter",
            Sample = static (ref ActivityCreationOptions<ActivityContext> _) =>
                ActivitySamplingResult.AllDataAndRecorded
        };
        ActivitySource.AddActivityListener(listener);
        var uow = new RecordingUnitOfWork();
        var outbox = new Mock<IOutboxEventRepository>(MockBehavior.Strict);
        var realtime = new Mock<IWorkCenterRealtimeNotifier>(MockBehavior.Strict);
        var item = WorkItem("test.no_policy", 1);
        outbox.Setup(repository => repository.ClaimBatchAsync(
                "work-center", 1, Now, It.IsAny<CancellationToken>()))
            .ReturnsAsync([item]);
        outbox.Setup(repository => repository.MarkCompletedAsync(
                item.Event.EventId, "work-center", 1, Now, It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);
        realtime.Setup(notifier => notifier.NotifyChangedAsync(
                Now.Ticks, It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);
        var processor = Processor(uow, outbox, [], realtime.Object);

        (await processor.ProcessBatchAsync(1, CancellationToken.None)).Should().Be(1);

        outbox.VerifyAll();
        realtime.VerifyAll();
    }

    [Theory]
    [InlineData(1, false)]
    [InlineData(8, true)]
    public async Task Policy_failures_are_retried_with_bounded_backoff_then_dead_lettered(
        int attempt,
        bool expectedDeadLetter)
    {
        using var listener = expectedDeadLetter ? null : new ActivityListener
        {
            ShouldListenTo = source => source.Name == "NGB.Platform.DocumentActionsWorkCenter",
            Sample = static (ref ActivityCreationOptions<ActivityContext> _) =>
                ActivitySamplingResult.AllDataAndRecorded
        };
        if (listener is not null)
            ActivitySource.AddActivityListener(listener);
        var uow = new RecordingUnitOfWork();
        var outbox = new Mock<IOutboxEventRepository>(MockBehavior.Strict);
        var policy = new Mock<IWorkCenterEventPolicy>(MockBehavior.Strict);
        var realtime = new Mock<IWorkCenterRealtimeNotifier>(MockBehavior.Strict);
        var item = WorkItem("test.failed", attempt);
        outbox.Setup(repository => repository.ClaimBatchAsync(
                "work-center", 100, Now, It.IsAny<CancellationToken>()))
            .ReturnsAsync([item]);
        policy.SetupGet(candidate => candidate.EventType).Returns("test.failed");
        policy.Setup(candidate => candidate.HandleAsync(
                It.IsAny<WorkCenterEventContext>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("policy failed"));
        DateTime? capturedNextAttempt = default;
        outbox.Setup(repository => repository.MarkFailedAsync(
                item.Event.EventId,
                "work-center",
                attempt,
                Now,
                It.IsAny<DateTime?>(),
                "InvalidOperationException: policy failed",
                expectedDeadLetter,
                It.IsAny<CancellationToken>()))
            .Callback<Guid, string, int, DateTime, DateTime?, string, bool, CancellationToken>(
                (_, _, _, _, nextAttempt, _, _, _) => capturedNextAttempt = nextAttempt)
            .Returns(Task.CompletedTask);
        var processor = Processor(uow, outbox, [policy.Object], realtime.Object);

        (await processor.ProcessBatchAsync(100, CancellationToken.None)).Should().Be(1);

        if (expectedDeadLetter)
            capturedNextAttempt.Should().BeNull();
        else
        {
            capturedNextAttempt.Should().BeAfter(Now);
            capturedNextAttempt.Should().BeBefore(Now.AddSeconds(3));
        }
        realtime.Verify(
            notifier => notifier.NotifyChangedAsync(It.IsAny<long>(), It.IsAny<CancellationToken>()),
            Times.Never);
        outbox.VerifyAll();
    }

    [Fact]
    public async Task Caller_cancellation_is_propagated_without_recording_a_processing_failure()
    {
        var uow = new RecordingUnitOfWork();
        var outbox = new Mock<IOutboxEventRepository>(MockBehavior.Strict);
        var policy = new Mock<IWorkCenterEventPolicy>(MockBehavior.Strict);
        var item = WorkItem("test.cancel", 1);
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        outbox.Setup(repository => repository.ClaimBatchAsync(
                "work-center", 1, Now, cancellation.Token))
            .ReturnsAsync([item]);
        policy.SetupGet(candidate => candidate.EventType).Returns("test.cancel");
        policy.Setup(candidate => candidate.HandleAsync(
                It.IsAny<WorkCenterEventContext>(), cancellation.Token))
            .ThrowsAsync(new OperationCanceledException(cancellation.Token));
        var processor = Processor(
            uow,
            outbox,
            [policy.Object],
            new Mock<IWorkCenterRealtimeNotifier>().Object);

        await FluentActions.Awaiting(() => processor.ProcessBatchAsync(1, cancellation.Token))
            .Should().ThrowAsync<OperationCanceledException>();

        outbox.Verify(repository => repository.MarkFailedAsync(
            It.IsAny<Guid>(), It.IsAny<string>(), It.IsAny<int>(), It.IsAny<DateTime>(),
            It.IsAny<DateTime?>(), It.IsAny<string>(), It.IsAny<bool>(),
            It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Null_realtime_notifier_is_a_safe_noop()
    {
        var notifier = new NullWorkCenterRealtimeNotifier();

        await notifier.NotifyChangedAsync(42, CancellationToken.None);
    }

    private static OutboxProcessor Processor(
        RecordingUnitOfWork uow,
        Mock<IOutboxEventRepository> outbox,
        IEnumerable<IWorkCenterEventPolicy> policies,
        IWorkCenterRealtimeNotifier realtime)
        => new(
            uow,
            outbox.Object,
            policies,
            realtime,
            new FixedTimeProvider(Now),
            NullLogger<OutboxProcessor>.Instance);

    private static OutboxConsumerWorkItem WorkItem(string eventType, int attempt)
    {
        var id = Guid.Parse("01980000-7000-8000-8000-000000000001");
        return new OutboxConsumerWorkItem(
            new PlatformOutboxEvent(
                id,
                eventType,
                1,
                Now.AddMinutes(-1),
                "tests",
                "subject",
                null,
                Guid.NewGuid(),
                null,
                "{}",
                Now.AddMinutes(-1)),
            "work-center",
            attempt);
    }

    private sealed class FixedTimeProvider(DateTime now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => new(now);
    }

    private sealed class RecordingUnitOfWork : IUnitOfWork
    {
        public DbConnection Connection { get; } = new Mock<DbConnection>().Object;
        public DbTransaction? Transaction => null;
        public bool HasActiveTransaction { get; private set; }
        public int CommitCount { get; private set; }

        public Task EnsureConnectionOpenAsync(CancellationToken ct = default) => Task.CompletedTask;

        public Task BeginTransactionAsync(CancellationToken ct = default)
        {
            HasActiveTransaction = true;
            return Task.CompletedTask;
        }

        public Task CommitAsync(CancellationToken ct = default)
        {
            CommitCount++;
            HasActiveTransaction = false;
            return Task.CompletedTask;
        }

        public Task RollbackAsync(CancellationToken ct = default)
        {
            HasActiveTransaction = false;
            return Task.CompletedTask;
        }

        public void EnsureActiveTransaction()
        {
            if (!HasActiveTransaction)
                throw new InvalidOperationException("No active transaction.");
        }

        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }
}
