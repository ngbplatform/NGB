using System.Collections.Concurrent;
using System.Data.Common;
using System.Diagnostics;
using System.Text.Json;
using System.Text.Json.Serialization;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using NGB.Application.Abstractions.Services;
using NGB.Contracts.IntegrationEvents;
using NGB.Core.WorkCenter;
using NGB.Definitions.WorkCenter;
using NGB.Persistence.AuditLog;
using NGB.Persistence.Outbox;
using NGB.Persistence.Security;
using NGB.Persistence.UnitOfWork;
using NGB.Persistence.WorkCenter;
using NGB.Runtime.WorkCenter;
using Xunit;
using ContractDocumentStatus = NGB.Contracts.Metadata.DocumentStatus;

namespace NGB.Runtime.Tests.WorkCenter;

[Collection(Observability.TelemetrySerialCollection.Name)]
public sealed class OutboxProcessorTests
{
    private static readonly DateTime Now = new(2026, 7, 26, 19, 0, 0, DateTimeKind.Utc);

    [Theory]
    [InlineData(0, 1)]
    [InlineData(750, 25)]
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
        var first = new Mock<IDocumentActionCompletedWorkCenterPolicy>(MockBehavior.Strict);
        var second = new Mock<IDocumentActionCompletedWorkCenterPolicy>(MockBehavior.Strict);
        var realtime = new Mock<IWorkCenterRealtimeNotifier>(MockBehavior.Strict);
        var recipientUserId = Guid.NewGuid();
        var item = WorkItem(DocumentActionCompletedV1.EventType, attempt: 2);
        outbox.Setup(repository => repository.ClaimBatchAsync(
                "work-center", 25, Now, It.IsAny<CancellationToken>()))
            .ReturnsAsync([item]);
        first.Setup(policy => policy.HandleAsync(
                It.Is<DocumentActionCompletedV1>(@event => @event.EventId == item.Event.EventId),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync([recipientUserId]);
        second.Setup(policy => policy.HandleAsync(
                It.Is<DocumentActionCompletedV1>(@event => @event.EventId == item.Event.EventId),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync([]);
        outbox.Setup(repository => repository.MarkCompletedAsync(
                item.Event.EventId,
                "work-center",
                2,
                Now,
                It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);
        realtime.Setup(notifier => notifier.NotifyUsersChangedAsync(
                Now.Ticks,
                It.Is<IReadOnlyCollection<Guid>>(users => users.SequenceEqual(new[] { recipientUserId })),
                It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("socket unavailable"));
        var processor = Processor(uow, outbox, [first.Object, second.Object], realtime.Object);

        (await processor.ProcessBatchAsync(25, CancellationToken.None)).Should().Be(1);

        first.VerifyAll();
        second.VerifyAll();
        outbox.Verify(repository => repository.MarkFailedAsync(
            It.IsAny<Guid>(), It.IsAny<string>(), It.IsAny<int>(), It.IsAny<DateTime>(),
            It.IsAny<DateTime?>(), It.IsAny<string>(), It.IsAny<bool>(),
            It.IsAny<CancellationToken>()), Times.Never);
        uow.CommitCount.Should().Be(2);
    }

    [Fact]
    public async Task Batch_aggregates_recipient_changes_and_emits_one_targeted_realtime_invalidation()
    {
        var uow = new RecordingUnitOfWork();
        var outbox = new Mock<IOutboxEventRepository>(MockBehavior.Strict);
        var policy = new Mock<IDocumentActionCompletedWorkCenterPolicy>(MockBehavior.Strict);
        var realtime = new Mock<IWorkCenterRealtimeNotifier>(MockBehavior.Strict);
        var firstUser = Guid.NewGuid();
        var secondUser = Guid.NewGuid();
        var first = WorkItem(DocumentActionCompletedV1.EventType, 1, eventId: Guid.NewGuid());
        var second = WorkItem(DocumentActionCompletedV1.EventType, 1, eventId: Guid.NewGuid());

        outbox.Setup(repository => repository.ClaimBatchAsync(
                "work-center", 2, Now, It.IsAny<CancellationToken>()))
            .ReturnsAsync([first, second]);
        policy.Setup(candidate => candidate.HandleAsync(
                It.IsAny<DocumentActionCompletedV1>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((DocumentActionCompletedV1 completed, CancellationToken _) =>
                (IReadOnlyList<Guid>)(completed.EventId == first.Event.EventId
                    ? [firstUser]
                    : [firstUser, secondUser]));
        foreach (var item in new[] { first, second })
        {
            outbox.Setup(repository => repository.MarkCompletedAsync(
                    item.Event.EventId, "work-center", 1, Now, It.IsAny<CancellationToken>()))
                .Returns(Task.CompletedTask);
        }
        realtime.Setup(notifier => notifier.NotifyUsersChangedAsync(
                Now.Ticks,
                It.Is<IReadOnlyCollection<Guid>>(ids =>
                    ids.Count == 2 && ids.Contains(firstUser) && ids.Contains(secondUser)),
                It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);
        var processor = Processor(uow, outbox, [policy.Object], realtime.Object);

        (await processor.ProcessBatchAsync(2, CancellationToken.None)).Should().Be(2);

        policy.Verify(candidate => candidate.HandleAsync(
            It.IsAny<DocumentActionCompletedV1>(), It.IsAny<CancellationToken>()), Times.Exactly(2));
        realtime.Verify(notifier => notifier.NotifyUsersChangedAsync(
            It.IsAny<long>(), It.IsAny<IReadOnlyCollection<Guid>>(), It.IsAny<CancellationToken>()), Times.Once);
        outbox.VerifyAll();
        uow.CommitCount.Should().Be(3);
    }

    [Fact]
    public async Task Independent_subjects_are_parallelized_while_each_subject_keeps_claim_order()
    {
        var uow = new RecordingUnitOfWork();
        var outbox = new Mock<IOutboxEventRepository>(MockBehavior.Strict);
        var realtime = new Mock<IWorkCenterRealtimeNotifier>(MockBehavior.Strict);
        var factoryCalls = 0;
        var first = WorkItem(DocumentActionCompletedV1.EventType, 1, eventId: Guid.NewGuid(), subject: "document/a/1");
        var second = WorkItem(DocumentActionCompletedV1.EventType, 1, eventId: Guid.NewGuid(), subject: "document/b/2");
        var third = WorkItem(DocumentActionCompletedV1.EventType, 1, eventId: Guid.NewGuid(), subject: "document/a/1");
        var captured = new ConcurrentBag<IReadOnlyList<OutboxConsumerWorkItem>>();
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var bothStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var running = 0;

        outbox.Setup(repository => repository.ClaimBatchAsync(
                "work-center", 3, Now, It.IsAny<CancellationToken>()))
            .ReturnsAsync([first, second, third]);
        var factory = new DelegatePartitionProcessorFactory(async (partition, ct) =>
            {
                Interlocked.Increment(ref factoryCalls);
                captured.Add(partition);
                if (Interlocked.Increment(ref running) == 2)
                    bothStarted.TrySetResult();

                await release.Task.WaitAsync(ct);
                Interlocked.Decrement(ref running);
                return [];
            });
        var processor = Processor(
            uow,
            outbox,
            [],
            realtime.Object,
            partitionProcessorFactory: factory,
            options: new NgbWorkCenterOptions { ProjectionParallelism = 2 });

        var processing = processor.ProcessBatchAsync(3, CancellationToken.None);
        await bothStarted.Task.WaitAsync(TimeSpan.FromSeconds(5));
        release.TrySetResult();

        (await processing).Should().Be(3);
        captured.Should().HaveCount(2);
        captured.Single(partition => partition[0].Event.Subject == "document/a/1")
            .Select(item => item.Event.EventId)
            .Should().Equal(first.Event.EventId, third.Event.EventId);
        factoryCalls.Should().Be(2);
        realtime.VerifyNoOtherCalls();
        uow.CommitCount.Should().Be(1);
    }

    [Fact]
    public async Task Recipient_metadata_is_cached_within_a_batch_and_reset_between_batches()
    {
        var uow = new RecordingUnitOfWork();
        var outbox = new Mock<IOutboxEventRepository>(MockBehavior.Strict);
        var realtime = new Mock<IWorkCenterRealtimeNotifier>(MockBehavior.Strict);
        var preferences = new Mock<INotificationPreferenceRepository>(MockBehavior.Strict);
        var users = new Mock<IPlatformUserRepository>(MockBehavior.Strict);
        var roles = new Mock<IPlatformRoleRepository>(MockBehavior.Strict);
        var userRoles = new Mock<IPlatformUserRoleRepository>(MockBehavior.Strict);
        var recipientUserId = Guid.NewGuid();
        var first = WorkItem(DocumentActionCompletedV1.EventType, 1, eventId: Guid.NewGuid());
        var second = WorkItem(DocumentActionCompletedV1.EventType, 1, eventId: Guid.NewGuid());

        outbox.Setup(repository => repository.ClaimBatchAsync(
                "work-center", 2, Now, It.IsAny<CancellationToken>()))
            .ReturnsAsync([first, second]);
        foreach (var item in new[] { first, second })
        {
            outbox.Setup(repository => repository.MarkCompletedAsync(
                    item.Event.EventId, "work-center", 1, Now, It.IsAny<CancellationToken>()))
                .Returns(Task.CompletedTask);
        }

        users.Setup(repository => repository.GetByIdsAsync(
                It.Is<IReadOnlyList<Guid>>(ids => ids.SequenceEqual(new[] { recipientUserId })),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Dictionary<Guid, NGB.Core.AuditLog.PlatformUser>
            {
                [recipientUserId] = new(
                    recipientUserId,
                    $"subject-{recipientUserId:N}",
                    Email: null,
                    DisplayName: null,
                    IsActive: true,
                    Now,
                    Now)
            });
        preferences.Setup(repository => repository.GetForUsersAsync(
                It.Is<IReadOnlyList<Guid>>(ids => ids.SequenceEqual(new[] { recipientUserId })),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync([]);

        var source = new Mock<IWorkCenterPreferenceDefinitionSource>(MockBehavior.Strict);
        source.Setup(candidate => candidate.GetDefinitions())
            .Returns([
                new WorkCenterPreferenceDefinition(
                    "test.notification",
                    WorkCenterPreferenceKind.Notification,
                    "Test notification",
                    "Tests",
                    DefaultEnabled: true,
                    UserCanDisable: true,
                    NotificationSeverity.Information,
                    new HashSet<NotificationChannel> { NotificationChannel.InApp },
                    Retention: null)
            ]);
        var resolver = new WorkCenterPreferenceRecipientResolver(
            preferences.Object,
            users.Object,
            roles.Object,
            userRoles.Object,
            new WorkCenterPreferenceDefinitionRegistry([source.Object]));
        var processor = Processor(
            uow,
            outbox,
            [new ResolvingPolicy(resolver, recipientUserId)],
            realtime.Object,
            resolver);

        (await processor.ProcessBatchAsync(2, CancellationToken.None)).Should().Be(2);
        (await processor.ProcessBatchAsync(2, CancellationToken.None)).Should().Be(2);

        users.Verify(repository => repository.GetByIdsAsync(
            It.IsAny<IReadOnlyList<Guid>>(), It.IsAny<CancellationToken>()), Times.Exactly(2));
        preferences.Verify(repository => repository.GetForUsersAsync(
            It.IsAny<IReadOnlyList<Guid>>(), It.IsAny<CancellationToken>()), Times.Exactly(2));
        outbox.VerifyAll();
        source.VerifyAll();
        realtime.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task Projection_changes_are_not_published_when_the_projection_transaction_rolls_back()
    {
        var uow = new RecordingUnitOfWork();
        var outbox = new Mock<IOutboxEventRepository>(MockBehavior.Strict);
        var policy = new Mock<IDocumentActionCompletedWorkCenterPolicy>(MockBehavior.Strict);
        var realtime = new Mock<IWorkCenterRealtimeNotifier>(MockBehavior.Strict);
        var recipientUserId = Guid.NewGuid();
        var item = WorkItem(DocumentActionCompletedV1.EventType, attempt: 1);

        outbox.Setup(repository => repository.ClaimBatchAsync(
                "work-center", 1, Now, It.IsAny<CancellationToken>()))
            .ReturnsAsync([item]);
        policy.Setup(candidate => candidate.HandleAsync(
                It.IsAny<DocumentActionCompletedV1>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync([recipientUserId]);
        outbox.Setup(repository => repository.MarkCompletedAsync(
                item.Event.EventId,
                "work-center",
                1,
                Now,
                It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("commit marker failed"));
        outbox.Setup(repository => repository.MarkFailedAsync(
                item.Event.EventId,
                "work-center",
                1,
                Now,
                It.IsAny<DateTime?>(),
                "InvalidOperationException: commit marker failed",
                false,
                It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);
        var processor = Processor(uow, outbox, [policy.Object], realtime.Object);

        (await processor.ProcessBatchAsync(1, CancellationToken.None)).Should().Be(1);

        realtime.Verify(
            notifier => notifier.NotifyUsersChangedAsync(
                It.IsAny<long>(),
                It.IsAny<IReadOnlyCollection<Guid>>(),
                It.IsAny<CancellationToken>()),
            Times.Never);
        policy.VerifyAll();
        outbox.VerifyAll();
        uow.CommitCount.Should().Be(2, "only the claim and failure-record transactions committed");
    }

    [Fact]
    public async Task Successful_projection_without_changes_marks_the_activity_without_realtime_invalidation()
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
        var processor = Processor(uow, outbox, [], realtime.Object);

        (await processor.ProcessBatchAsync(1, CancellationToken.None)).Should().Be(1);

        outbox.VerifyAll();
        realtime.VerifyNoOtherCalls();
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
        var policy = new Mock<IDocumentActionCompletedWorkCenterPolicy>(MockBehavior.Strict);
        var realtime = new Mock<IWorkCenterRealtimeNotifier>(MockBehavior.Strict);
        var item = WorkItem(DocumentActionCompletedV1.EventType, attempt);
        outbox.Setup(repository => repository.ClaimBatchAsync(
                "work-center", 25, Now, It.IsAny<CancellationToken>()))
            .ReturnsAsync([item]);
        policy.Setup(candidate => candidate.HandleAsync(
                It.IsAny<DocumentActionCompletedV1>(), It.IsAny<CancellationToken>()))
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
            notifier => notifier.NotifyUsersChangedAsync(
                It.IsAny<long>(),
                It.IsAny<IReadOnlyCollection<Guid>>(),
                It.IsAny<CancellationToken>()),
            Times.Never);
        outbox.VerifyAll();
    }

    [Fact]
    public async Task Caller_cancellation_is_propagated_without_recording_a_processing_failure()
    {
        var uow = new RecordingUnitOfWork();
        var outbox = new Mock<IOutboxEventRepository>(MockBehavior.Strict);
        var policy = new Mock<IDocumentActionCompletedWorkCenterPolicy>(MockBehavior.Strict);
        var item = WorkItem(DocumentActionCompletedV1.EventType, 1);
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        outbox.Setup(repository => repository.ClaimBatchAsync(
                "work-center", 1, Now, cancellation.Token))
            .ReturnsAsync([item]);
        policy.Setup(candidate => candidate.HandleAsync(
                It.IsAny<DocumentActionCompletedV1>(), cancellation.Token))
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
    public async Task Unsupported_document_action_schema_is_dead_lettered_without_invoking_policies()
    {
        var item = WorkItem(
            DocumentActionCompletedV1.EventType,
            attempt: 8,
            schemaVersion: DocumentActionCompletedV1.SchemaVersion + 1);

        await AssertPoisonEventIsDeadLetteredAsync(
            item,
            $"Unsupported '{DocumentActionCompletedV1.EventType}' schema version");
    }

    [Fact]
    public async Task Payload_that_does_not_match_its_envelope_is_dead_lettered_without_invoking_policies()
    {
        var valid = WorkItem(DocumentActionCompletedV1.EventType, attempt: 8);
        var mismatched = new OutboxConsumerWorkItem(
            new OutboxEventEnvelope(
                Guid.Parse("01980000-7000-8000-8000-000000000099"),
                valid.Event.EventType,
                valid.Event.SchemaVersion,
                valid.Event.OccurredAtUtc,
                valid.Event.Source,
                valid.Event.Subject,
                valid.Event.ActorUserId,
                valid.Event.CorrelationId,
                valid.Event.CausationId,
                valid.Event.PayloadJson,
                valid.Event.CreatedAtUtc),
            valid.ConsumerCode,
            valid.AttemptCount);

        await AssertPoisonEventIsDeadLetteredAsync(
            mismatched,
            "Document action completed payload does not match its outbox envelope");
    }

    [Fact]
    public async Task Empty_document_action_payload_is_dead_lettered_without_invoking_policies()
    {
        var valid = WorkItem(DocumentActionCompletedV1.EventType, attempt: 8);
        var empty = WithEnvelope(valid, payloadJson: "null");

        await AssertPoisonEventIsDeadLetteredAsync(
            empty,
            "Document action completed payload is empty");
    }

    [Fact]
    public async Task Payload_with_a_different_correlation_is_dead_lettered_without_invoking_policies()
    {
        var valid = WorkItem(DocumentActionCompletedV1.EventType, attempt: 8);
        var mismatched = WithEnvelope(
            valid,
            correlationId: Guid.Parse("01980000-7000-8000-8000-000000000098"));

        await AssertPoisonEventIsDeadLetteredAsync(
            mismatched,
            "Document action completed payload does not match its outbox envelope");
    }

    [Fact]
    public async Task Payload_with_a_differently_cased_envelope_type_is_dead_lettered_without_invoking_policies()
    {
        var valid = WorkItem(DocumentActionCompletedV1.EventType, attempt: 8);
        var mismatched = WithEnvelope(valid, eventType: DocumentActionCompletedV1.EventType.ToUpperInvariant());

        await AssertPoisonEventIsDeadLetteredAsync(
            mismatched,
            "Document action completed payload does not match its outbox envelope");
    }

    [Fact]
    public async Task Null_realtime_notifier_is_a_safe_noop()
    {
        var notifier = new NullWorkCenterRealtimeNotifier();

        await notifier.NotifyUsersChangedAsync(42, [Guid.NewGuid()], CancellationToken.None);
    }

    private static OutboxProcessor Processor(
        RecordingUnitOfWork uow,
        Mock<IOutboxEventRepository> outbox,
        IEnumerable<IDocumentActionCompletedWorkCenterPolicy> policies,
        IWorkCenterRealtimeNotifier realtime,
        WorkCenterPreferenceRecipientResolver? recipientResolver = null,
        IWorkCenterOutboxPartitionProcessorFactory? partitionProcessorFactory = null,
        NgbWorkCenterOptions? options = null)
        => new(
            uow,
            outbox.Object,
            policies,
            realtime,
            recipientResolver ?? RecipientResolver(),
            new FixedTimeProvider(Now),
            NullLogger<OutboxProcessor>.Instance,
            partitionProcessorFactory,
            options is null ? null : Microsoft.Extensions.Options.Options.Create(options));

    private static async Task AssertPoisonEventIsDeadLetteredAsync(
        OutboxConsumerWorkItem item,
        string expectedError)
    {
        var uow = new RecordingUnitOfWork();
        var outbox = new Mock<IOutboxEventRepository>(MockBehavior.Strict);
        var policy = new Mock<IDocumentActionCompletedWorkCenterPolicy>(MockBehavior.Strict);
        var realtime = new Mock<IWorkCenterRealtimeNotifier>(MockBehavior.Strict);
        outbox.Setup(repository => repository.ClaimBatchAsync(
                "work-center", 1, Now, It.IsAny<CancellationToken>()))
            .ReturnsAsync([item]);
        outbox.Setup(repository => repository.MarkFailedAsync(
                item.Event.EventId,
                "work-center",
                item.AttemptCount,
                Now,
                null,
                It.Is<string>(error => error.Contains(expectedError, StringComparison.Ordinal)),
                true,
                It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);
        var processor = Processor(uow, outbox, [policy.Object], realtime.Object);

        (await processor.ProcessBatchAsync(1, CancellationToken.None)).Should().Be(1);

        policy.Verify(
            candidate => candidate.HandleAsync(
                It.IsAny<DocumentActionCompletedV1>(), It.IsAny<CancellationToken>()),
            Times.Never);
        realtime.Verify(
            notifier => notifier.NotifyUsersChangedAsync(
                It.IsAny<long>(),
                It.IsAny<IReadOnlyCollection<Guid>>(),
                It.IsAny<CancellationToken>()),
            Times.Never);
        outbox.VerifyAll();
        uow.CommitCount.Should().Be(2);
    }

    private static OutboxConsumerWorkItem WorkItem(
        string eventType,
        int attempt,
        int schemaVersion = DocumentActionCompletedV1.SchemaVersion,
        Guid? eventId = null,
        string subject = "subject")
    {
        var id = eventId ?? Guid.Parse("01980000-7000-8000-8000-000000000001");
        var correlationId = Guid.Parse("01980000-7000-8000-8000-000000000002");
        var payload = eventType == DocumentActionCompletedV1.EventType
            ? JsonSerializer.Serialize(
                new DocumentActionCompletedV1(
                    id,
                    Now.AddMinutes(-1),
                    "tests",
                    subject,
                    null,
                    correlationId,
                    null,
                    new DocumentActionCompletedDataV1(
                        Guid.Parse("01980000-7000-8000-8000-000000000003"),
                        "test.document",
                        "post",
                        ContractDocumentStatus.Draft,
                        ContractDocumentStatus.Posted,
                        2)),
                new JsonSerializerOptions(JsonSerializerDefaults.Web)
                {
                    Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase) }
                })
            : "{}";
        return new OutboxConsumerWorkItem(
            new OutboxEventEnvelope(
                id,
                eventType,
                schemaVersion,
                Now.AddMinutes(-1),
                "tests",
                subject,
                null,
                correlationId,
                null,
                payload,
                Now.AddMinutes(-1)),
            "work-center",
            attempt);
    }

    private static OutboxConsumerWorkItem WithEnvelope(
        OutboxConsumerWorkItem item,
        Guid? correlationId = null,
        string? eventType = null,
        string? payloadJson = null)
        => new(
            new OutboxEventEnvelope(
                item.Event.EventId,
                eventType ?? item.Event.EventType,
                item.Event.SchemaVersion,
                item.Event.OccurredAtUtc,
                item.Event.Source,
                item.Event.Subject,
                item.Event.ActorUserId,
                correlationId ?? item.Event.CorrelationId,
                item.Event.CausationId,
                payloadJson ?? item.Event.PayloadJson,
                item.Event.CreatedAtUtc),
            item.ConsumerCode,
            item.AttemptCount);

    private static WorkCenterPreferenceRecipientResolver RecipientResolver()
        => new(
            new Mock<INotificationPreferenceRepository>().Object,
            new Mock<IPlatformUserRepository>().Object,
            new Mock<IPlatformRoleRepository>().Object,
            new Mock<IPlatformUserRoleRepository>().Object,
            new WorkCenterPreferenceDefinitionRegistry([]));

    private sealed class FixedTimeProvider(DateTime now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => new(now);
    }

    private sealed class DelegatePartitionProcessorFactory(
        Func<IReadOnlyList<OutboxConsumerWorkItem>, CancellationToken, Task<IReadOnlyCollection<Guid>>> process)
        : IWorkCenterOutboxPartitionProcessorFactory
    {
        public Task<IReadOnlyCollection<Guid>> ProcessAsync(
            IReadOnlyList<OutboxConsumerWorkItem> items,
            CancellationToken ct)
            => process(items, ct);
    }

    private sealed class ResolvingPolicy(
        WorkCenterPreferenceRecipientResolver resolver,
        Guid recipientUserId)
        : IDocumentActionCompletedWorkCenterPolicy
    {
        public async Task<IReadOnlyList<Guid>> HandleAsync(
            DocumentActionCompletedV1 completed,
            CancellationToken ct)
        {
            await resolver.ResolveAsync(
                "test.notification",
                WorkCenterPreferenceKind.Notification,
                [recipientUserId],
                ct);
            return [];
        }
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
