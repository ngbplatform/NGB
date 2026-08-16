using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using NGB.Application.Abstractions.Services;
using NGB.Contracts.Documents;
using NGB.Contracts.IntegrationEvents;
using NGB.Core.Documents;
using NGB.Core.Documents.Exceptions;
using NGB.Core.WorkCenter;
using NGB.Persistence.Documents;
using NGB.PropertyManagement.Documents;
using NGB.PropertyManagement.Runtime.DocumentActions;
using NGB.PropertyManagement.Runtime.Receivables;
using NGB.PropertyManagement.Runtime.WorkCenter;
using NGB.PropertyManagement.WorkCenter;
using Xunit;
using ContractDocumentStatus = NGB.Contracts.Metadata.DocumentStatus;
using StoredDocumentStatus = NGB.Core.Documents.DocumentStatus;

namespace NGB.PropertyManagement.Runtime.Tests.WorkCenter;

public sealed class PropertyManagementWorkCenterFullCoverageTests
{
    private static readonly DateTimeOffset Now = new(2026, 8, 16, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task Policy_routes_payment_actions_and_ignores_unrelated_actions_and_types()
    {
        var readers = new Mock<IPropertyManagementDocumentReaders>(MockBehavior.Strict);
        var synchronizer = new Mock<IReceivablePaymentWorkCenterSynchronizer>(MockBehavior.Strict);
        var paymentId = Guid.CreateVersion7();
        var users = new[] { Guid.CreateVersion7() };
        synchronizer.Setup(x => x.CancelAsync(paymentId, It.IsAny<CancellationToken>())).ReturnsAsync(users);
        synchronizer.Setup(x => x.SynchronizeAsync(paymentId, It.IsAny<Guid?>(), It.IsAny<Guid?>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(users);
        var sut = new PropertyManagementWorkCenterPolicy(readers.Object, synchronizer.Object);

        (await sut.HandleAsync(Event(paymentId, PropertyManagementCodes.ReceivablePayment, " UNPOST "), default))
            .Should().Equal(users);
        (await sut.HandleAsync(Event(paymentId, PropertyManagementCodes.ReceivablePayment.ToUpperInvariant(), "POST"), default))
            .Should().Equal(users);
        (await sut.HandleAsync(Event(paymentId, PropertyManagementCodes.ReceivablePayment, "repost"), default))
            .Should().Equal(users);
        (await sut.HandleAsync(Event(paymentId, PropertyManagementCodes.ReceivablePayment, "delete"), default))
            .Should().BeEmpty();
        (await sut.HandleAsync(Event(paymentId, "other", "post"), default)).Should().BeEmpty();
        readers.VerifyNoOtherCalls();
    }

    [Theory]
    [InlineData("post")]
    [InlineData("repost")]
    [InlineData("unpost")]
    public async Task Policy_resynchronizes_the_apply_credit_source_for_every_balance_changing_action(string action)
    {
        var readers = new Mock<IPropertyManagementDocumentReaders>(MockBehavior.Strict);
        var synchronizer = new Mock<IReceivablePaymentWorkCenterSynchronizer>(MockBehavior.Strict);
        var applyId = Guid.CreateVersion7();
        var paymentId = Guid.CreateVersion7();
        readers.Setup(x => x.ReadReceivableApplyHeadAsync(applyId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new PmReceivableApplyHead(applyId, paymentId, Guid.CreateVersion7(),
                new DateOnly(2026, 8, 16), 1m, null));
        synchronizer.Setup(x => x.SynchronizeAsync(paymentId, It.IsAny<Guid?>(), It.IsAny<Guid?>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync([Guid.CreateVersion7()]);
        var sut = new PropertyManagementWorkCenterPolicy(readers.Object, synchronizer.Object);

        var result = await sut.HandleAsync(Event(
            applyId, PropertyManagementCodes.ReceivableApply.ToUpperInvariant(), action), default);

        result.Should().ContainSingle();
    }

    [Fact]
    public async Task Policy_ignores_apply_actions_that_do_not_change_balances()
    {
        var readers = new Mock<IPropertyManagementDocumentReaders>(MockBehavior.Strict);
        var synchronizer = new Mock<IReceivablePaymentWorkCenterSynchronizer>(MockBehavior.Strict);
        var sut = new PropertyManagementWorkCenterPolicy(readers.Object, synchronizer.Object);

        (await sut.HandleAsync(Event(Guid.CreateVersion7(), PropertyManagementCodes.ReceivableApply, "delete"), default))
            .Should().BeEmpty();

        readers.VerifyNoOtherCalls();
        synchronizer.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task Synchronizer_rejects_missing_and_wrong_type_documents()
    {
        var fixture = new SynchronizerFixture();
        var missingId = Guid.CreateVersion7();
        var wrongId = Guid.CreateVersion7();
        fixture.DocumentsById[wrongId] = Document(wrongId, "other", "X");

        await ((Func<Task>)(() => fixture.Sut.SynchronizeAsync(missingId, null, null, default)))
            .Should().ThrowAsync<DocumentNotFoundException>();
        await ((Func<Task>)(() => fixture.Sut.SynchronizeAsync(wrongId, null, null, default)))
            .Should().ThrowAsync<DocumentTypeMismatchException>();
        fixture.Availability.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task Synchronize_completes_exhausted_payment_task()
    {
        var fixture = new SynchronizerFixture();
        var paymentId = fixture.AddPayment("RP-1");
        fixture.AvailabilityResult = new DocumentActionAvailabilityResult(
            [new DocumentActionDisabledReasonDto("none", "None")]);

        var result = await fixture.Sut.SynchronizeAsync(paymentId, Guid.CreateVersion7(), Guid.CreateVersion7(), default);

        result.Should().Equal(fixture.ChangedUsers);
        fixture.Tasks.Verify(x => x.CompleteByDeduplicationKeyAsync(
            PropertyManagementWorkCenterCodes.ApplyReceivablePaymentTask,
            $"pm:receivable-payment:{paymentId:D}:apply",
            It.IsAny<CancellationToken>()), Times.Once);
        fixture.Tasks.Verify(x => x.CreateAsync(It.IsAny<CreateWorkCenterTaskRequest>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Synchronize_creates_or_updates_task_with_number_and_fallback_display()
    {
        var fixture = new SynchronizerFixture();
        var numberedId = fixture.AddPayment("RP-1");
        var unnumberedId = fixture.AddPayment(null);

        var numbered = await fixture.Sut.SynchronizeAsync(numberedId, Guid.CreateVersion7(), Guid.CreateVersion7(), default);
        var unnumbered = await fixture.Sut.SynchronizeAsync(unnumberedId, null, null, default);

        numbered.Should().Equal(fixture.ChangedUsers);
        unnumbered.Should().Equal(fixture.ChangedUsers);
        fixture.CapturedRequests.Should().HaveCount(2);
        fixture.CapturedRequests[0].Source.TitleSnapshot.Should().Be("RP-1");
        fixture.CapturedRequests[1].Source.TitleSnapshot.Should().Be("Receivable payment");
        fixture.CapturedRequests.Should().OnlyContain(x =>
            x.Priority == WorkCenterPriority.High
            && x.DueAtUtc == Now.AddDays(3).UtcDateTime
            && x.AssignedRoleCode == PropertyManagementWorkCenterCodes.AccountsReceivableClerkRole
            && x.PrimaryActionCode == PropertyManagementDocumentActionCodes.OpenReceivablesReconciliation);
        fixture.CapturedRequests[0].Target!.Parameters!["paymentId"].Should().Be(numberedId.ToString());
    }

    [Fact]
    public async Task Completion_only_path_completes_exhausted_and_preserves_available_payment_task()
    {
        var fixture = new SynchronizerFixture();
        var paymentId = fixture.AddPayment("RP-1");
        fixture.AvailabilityResult = new DocumentActionAvailabilityResult(
            [new DocumentActionDisabledReasonDto("none", "None")]);
        (await fixture.Sut.CompleteIfExhaustedAsync(paymentId, default)).Should().Equal(fixture.ChangedUsers);

        fixture.AvailabilityResult = DocumentActionAvailabilityResult.Allowed;
        (await fixture.Sut.CompleteIfExhaustedAsync(paymentId, default)).Should().BeEmpty();
    }

    [Fact]
    public async Task Cancel_uses_stable_deduplication_key()
    {
        var fixture = new SynchronizerFixture();
        var paymentId = Guid.CreateVersion7();

        var result = await fixture.Sut.CancelAsync(paymentId, default);

        result.Should().Equal(fixture.ChangedUsers);
        fixture.Tasks.Verify(x => x.CancelByDeduplicationKeyAsync(
            PropertyManagementWorkCenterCodes.ApplyReceivablePaymentTask,
            $"pm:receivable-payment:{paymentId:D}:apply",
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Realtime_notification_filters_ids_and_swallows_argument_and_transport_failures()
    {
        var fixture = new SynchronizerFixture();
        var userA = Guid.CreateVersion7();
        var userB = Guid.CreateVersion7();

        await fixture.Sut.NotifyChangedAsync(null!, default);
        await fixture.Sut.NotifyChangedAsync([], default);
        await fixture.Sut.NotifyChangedAsync([Guid.Empty], default);
        await fixture.Sut.NotifyChangedAsync([Guid.Empty, userA, userA, userB], default);
        fixture.Realtime.Verify(x => x.NotifyUsersChangedAsync(
            Now.UtcTicks,
            It.Is<IReadOnlyCollection<Guid>>(ids => ids.Count == 2 && ids.Contains(userA) && ids.Contains(userB)),
            It.IsAny<CancellationToken>()), Times.Once);

        fixture.Realtime.Setup(x => x.NotifyUsersChangedAsync(
                It.IsAny<long>(), It.IsAny<IReadOnlyCollection<Guid>>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("transport"));
        await fixture.Sut.NotifyChangedAsync([userA], default);
    }

    private static DocumentActionCompletedV1 Event(Guid id, string type, string action)
    {
        var eventId = Guid.CreateVersion7();
        return new DocumentActionCompletedV1(
            eventId,
            Now.UtcDateTime,
            "tests",
            $"document:{id:D}",
            null,
            Guid.CreateVersion7(),
            eventId,
            new DocumentActionCompletedDataV1(
                id, type, action, ContractDocumentStatus.Draft, ContractDocumentStatus.Posted, 2));
    }

    private static DocumentRecord Document(Guid id, string type, string? number)
        => new()
        {
            Id = id,
            TypeCode = type,
            Number = number,
            DateUtc = Now.UtcDateTime,
            Status = StoredDocumentStatus.Posted,
            Version = 2,
            CreatedAtUtc = Now.UtcDateTime,
            UpdatedAtUtc = Now.UtcDateTime
        };

    private sealed class SynchronizerFixture
    {
        public SynchronizerFixture()
        {
            Documents.Setup(x => x.GetAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync((Guid id, CancellationToken _) => DocumentsById.GetValueOrDefault(id));
            Availability.Setup(x => x.EvaluateAsync(
                    PropertyManagementCodes.ReceivablePayment, It.IsAny<Guid>(), ContractDocumentStatus.Posted,
                    It.IsAny<CancellationToken>()))
                .ReturnsAsync(() => AvailabilityResult);
            Tasks.Setup(x => x.CreateAsync(It.IsAny<CreateWorkCenterTaskRequest>(), It.IsAny<CancellationToken>()))
                .Callback<CreateWorkCenterTaskRequest, CancellationToken>((request, _) => CapturedRequests.Add(request))
                .ReturnsAsync(() => new WorkCenterMutationResult(Guid.CreateVersion7(), ChangedUsers));
            Tasks.Setup(x => x.CompleteByDeduplicationKeyAsync(
                    It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(ChangedUsers);
            Tasks.Setup(x => x.CancelByDeduplicationKeyAsync(
                    It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(ChangedUsers);
            Realtime.Setup(x => x.NotifyUsersChangedAsync(
                    It.IsAny<long>(), It.IsAny<IReadOnlyCollection<Guid>>(), It.IsAny<CancellationToken>()))
                .Returns(Task.CompletedTask);
            Sut = new ReceivablePaymentWorkCenterSynchronizer(
                Documents.Object,
                Availability.Object,
                Tasks.Object,
                Realtime.Object,
                new FixedTimeProvider(Now),
                NullLogger<ReceivablePaymentWorkCenterSynchronizer>.Instance);
        }

        public Dictionary<Guid, DocumentRecord> DocumentsById { get; } = [];
        public DocumentActionAvailabilityResult AvailabilityResult { get; set; } = DocumentActionAvailabilityResult.Allowed;
        public IReadOnlyList<Guid> ChangedUsers { get; } = [Guid.CreateVersion7(), Guid.CreateVersion7()];
        public List<CreateWorkCenterTaskRequest> CapturedRequests { get; } = [];
        public Mock<IDocumentRepository> Documents { get; } = new(MockBehavior.Strict);
        public Mock<IReceivablesApplyAvailabilitySource> Availability { get; } = new(MockBehavior.Strict);
        public Mock<IWorkCenterTaskService> Tasks { get; } = new(MockBehavior.Strict);
        public Mock<IWorkCenterRealtimeNotifier> Realtime { get; } = new(MockBehavior.Strict);
        public ReceivablePaymentWorkCenterSynchronizer Sut { get; }

        public Guid AddPayment(string? number)
        {
            var id = Guid.CreateVersion7();
            DocumentsById[id] = Document(id, PropertyManagementCodes.ReceivablePayment, number);
            return id;
        }
    }

    private sealed class FixedTimeProvider(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }
}
