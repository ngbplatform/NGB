using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using NGB.Application.Abstractions.Services;
using NGB.Contracts.IntegrationEvents;
using NGB.Contracts.Metadata;
using NGB.Persistence.Documents;
using NGB.PropertyManagement.Documents;
using NGB.PropertyManagement.Runtime.DocumentActions;
using NGB.PropertyManagement.Runtime.Receivables;
using NGB.PropertyManagement.Runtime.WorkCenter;
using NGB.PropertyManagement.WorkCenter;
using Xunit;

namespace NGB.PropertyManagement.Api.IntegrationTests.WorkCenter;

public sealed class PropertyManagementWorkCenterPolicyTests
{
    private static readonly DateTimeOffset Now =
        new(2026, 7, 26, 19, 0, 0, TimeSpan.Zero);

    [Fact]
    public void Implements_the_typed_document_action_projection_contract()
    {
        CreatePolicy().Policy.Should().BeAssignableTo<IDocumentActionCompletedWorkCenterPolicy>();
    }

    [Theory]
    [InlineData("pm.unrelated", "post")]
    [InlineData(PropertyManagementCodes.ReceivablePayment, "approve")]
    [InlineData(PropertyManagementCodes.ReceivableApply, "approve")]
    public async Task Ignores_unrelated_document_action_events(string documentType, string actionCode)
    {
        var sut = CreatePolicy();

        await sut.Policy.HandleAsync(Context(Guid.NewGuid(), documentType, actionCode), CancellationToken.None);

        sut.Documents.VerifyNoOtherCalls();
        sut.TypedDocuments.VerifyNoOtherCalls();
        sut.Availability.VerifyNoOtherCalls();
        sut.Tasks.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task Unposting_payment_cancels_open_apply_task_case_insensitively()
    {
        var paymentId = Guid.NewGuid();
        var sut = CreatePolicy();
        sut.Tasks
            .Setup(service => service.CancelByDeduplicationKeyAsync(
                PropertyManagementWorkCenterCodes.ApplyReceivablePaymentTask,
                $"pm:receivable-payment:{paymentId:D}:apply",
                CancellationToken.None))
            .ReturnsAsync([]);

        await sut.Policy.HandleAsync(
            Context(paymentId, PropertyManagementCodes.ReceivablePayment.ToUpperInvariant(), "UNPOST"),
            CancellationToken.None);

        sut.Tasks.VerifyAll();
        sut.Documents.VerifyNoOtherCalls();
        sut.Availability.VerifyNoOtherCalls();
    }

    [Theory]
    [InlineData("post", "RP-100", "RP-100")]
    [InlineData("repost", "RP-101", "RP-101")]
    [InlineData("post", null, "Receivable payment")]
    public async Task Posting_payment_with_remaining_credit_creates_apply_task(
        string actionCode,
        string? number,
        string expectedSourceTitle)
    {
        var paymentId = Guid.NewGuid();
        var changedUserId = Guid.NewGuid();
        var sut = CreatePolicy();
        var document = Document(paymentId, number);
        CreateWorkCenterTaskRequest? captured = null;
        sut.Documents
            .Setup(repository => repository.GetAsync(paymentId, CancellationToken.None))
            .ReturnsAsync(document);
        sut.Availability
            .Setup(source => source.EvaluateAsync(
                PropertyManagementCodes.ReceivablePayment,
                paymentId,
                DocumentStatus.Posted,
                CancellationToken.None))
            .ReturnsAsync(DocumentActionAvailabilityResult.Allowed);
        sut.Tasks
            .Setup(service => service.CreateAsync(It.IsAny<CreateWorkCenterTaskRequest>(), CancellationToken.None))
            .Callback<CreateWorkCenterTaskRequest, CancellationToken>((request, _) => captured = request)
            .ReturnsAsync(new WorkCenterMutationResult(Guid.NewGuid(), [changedUserId]));

        var changedUsers = await sut.Policy.HandleAsync(
            Context(paymentId, PropertyManagementCodes.ReceivablePayment, actionCode),
            CancellationToken.None);

        captured.Should().NotBeNull();
        captured!.TaskCode.Should().Be("pm.apply_receivable_payment");
        captured.Source.ResourceCode.Should().Be(PropertyManagementCodes.ReceivablePayment);
        captured.Source.EntityId.Should().Be(paymentId);
        captured.Source.TitleSnapshot.Should().Be(expectedSourceTitle);
        captured.Source.SubtitleSnapshot.Should().Be(number);
        captured.AssignedRoleCode.Should().Be("pm-ar-clerk");
        captured.DueAtUtc.Should().Be(Now.AddDays(3).UtcDateTime);
        captured.PrimaryActionCode!.Value.Value.Should().Be(
            PropertyManagementDocumentActionCodes.OpenReceivablesReconciliation.Value);
        captured.Target!.Code.Should().Be("pm.receivables.reconciliation");
        captured.Target.Parameters["paymentId"].Should().Be(paymentId.ToString());
        captured.DeduplicationKey.Should().Be($"pm:receivable-payment:{paymentId:D}:apply");
        captured.CorrelationId.Should().NotBeNull();
        captured.CausationId.Should().NotBeNull();
        changedUsers.Should().Equal(changedUserId);
    }

    [Fact]
    public async Task Posting_fully_applied_payment_completes_any_stale_task_instead_of_creating_one()
    {
        var paymentId = Guid.NewGuid();
        var sut = CreatePolicy();
        sut.Documents
            .Setup(repository => repository.GetAsync(paymentId, CancellationToken.None))
            .ReturnsAsync(Document(paymentId, "RP-200"));
        sut.Availability
            .Setup(source => source.EvaluateAsync(
                PropertyManagementCodes.ReceivablePayment,
                paymentId,
                DocumentStatus.Posted,
                CancellationToken.None))
            .ReturnsAsync(Disabled());
        sut.Tasks
            .Setup(service => service.CompleteByDeduplicationKeyAsync(
                PropertyManagementWorkCenterCodes.ApplyReceivablePaymentTask,
                $"pm:receivable-payment:{paymentId:D}:apply",
                CancellationToken.None))
            .ReturnsAsync([]);

        await sut.Policy.HandleAsync(
            Context(paymentId, PropertyManagementCodes.ReceivablePayment, "post"),
            CancellationToken.None);

        sut.Tasks.VerifyAll();
    }

    [Theory]
    [InlineData("post")]
    [InlineData("repost")]
    [InlineData("unpost")]
    public async Task Apply_event_recreates_payment_task_when_credit_still_needs_application(string actionCode)
    {
        var applyId = Guid.NewGuid();
        var paymentId = Guid.NewGuid();
        var sut = CreatePolicy();
        sut.TypedDocuments
            .Setup(readers => readers.ReadReceivableApplyHeadAsync(applyId, CancellationToken.None))
            .ReturnsAsync(Apply(applyId, paymentId));
        sut.Documents
            .Setup(repository => repository.GetAsync(paymentId, CancellationToken.None))
            .ReturnsAsync(Document(paymentId, "RP-300"));
        sut.Availability
            .Setup(source => source.EvaluateAsync(
                PropertyManagementCodes.ReceivablePayment,
                paymentId,
                DocumentStatus.Posted,
                CancellationToken.None))
            .ReturnsAsync(DocumentActionAvailabilityResult.Allowed);
        sut.Tasks
            .Setup(service => service.CreateAsync(
                It.Is<CreateWorkCenterTaskRequest>(request =>
                    request.DeduplicationKey == $"pm:receivable-payment:{paymentId:D}:apply"),
                CancellationToken.None))
            .ReturnsAsync(new WorkCenterMutationResult(Guid.NewGuid(), []));

        await sut.Policy.HandleAsync(
            Context(applyId, PropertyManagementCodes.ReceivableApply, actionCode),
            CancellationToken.None);

        sut.Tasks.VerifyAll();
    }

    [Fact]
    public async Task Apply_event_completes_payment_task_when_no_credit_remains()
    {
        var applyId = Guid.NewGuid();
        var paymentId = Guid.NewGuid();
        var sut = CreatePolicy();
        sut.TypedDocuments
            .Setup(readers => readers.ReadReceivableApplyHeadAsync(applyId, CancellationToken.None))
            .ReturnsAsync(Apply(applyId, paymentId));
        sut.Documents
            .Setup(repository => repository.GetAsync(paymentId, CancellationToken.None))
            .ReturnsAsync(Document(paymentId, "RP-400"));
        sut.Availability
            .Setup(source => source.EvaluateAsync(
                PropertyManagementCodes.ReceivablePayment,
                paymentId,
                DocumentStatus.Posted,
                CancellationToken.None))
            .ReturnsAsync(Disabled());
        sut.Tasks
            .Setup(service => service.CompleteByDeduplicationKeyAsync(
                PropertyManagementWorkCenterCodes.ApplyReceivablePaymentTask,
                $"pm:receivable-payment:{paymentId:D}:apply",
                CancellationToken.None))
            .ReturnsAsync([]);

        await sut.Policy.HandleAsync(
            Context(applyId, PropertyManagementCodes.ReceivableApply, "post"),
            CancellationToken.None);

        sut.Tasks.VerifyAll();
    }

    private static (
        PropertyManagementWorkCenterPolicy Policy,
        Mock<IDocumentRepository> Documents,
        Mock<IPropertyManagementDocumentReaders> TypedDocuments,
        Mock<IReceivablesApplyAvailabilitySource> Availability,
        Mock<IWorkCenterTaskService> Tasks) CreatePolicy()
    {
        var documents = new Mock<IDocumentRepository>(MockBehavior.Strict);
        var typedDocuments = new Mock<IPropertyManagementDocumentReaders>(MockBehavior.Strict);
        var availability = new Mock<IReceivablesApplyAvailabilitySource>(MockBehavior.Strict);
        var tasks = new Mock<IWorkCenterTaskService>(MockBehavior.Strict);
        var realtime = new Mock<IWorkCenterRealtimeNotifier>(MockBehavior.Strict);
        var synchronizer = new ReceivablePaymentWorkCenterSynchronizer(
            documents.Object,
            availability.Object,
            tasks.Object,
            realtime.Object,
            new FixedTimeProvider(Now),
            NullLogger<ReceivablePaymentWorkCenterSynchronizer>.Instance);
        return (
            new PropertyManagementWorkCenterPolicy(
                typedDocuments.Object,
                synchronizer),
            documents,
            typedDocuments,
            availability,
            tasks);
    }

    private static DocumentActionCompletedV1 Context(Guid documentId, string documentType, string actionCode)
    {
        var eventId = Guid.NewGuid();
        return new DocumentActionCompletedV1(
            eventId,
            Now.UtcDateTime,
            "tests",
            $"document:{documentId:D}",
            Guid.NewGuid(),
            Guid.NewGuid(),
            eventId,
            new DocumentActionCompletedDataV1(
                documentId,
                documentType,
                actionCode,
                DocumentStatus.Draft,
                DocumentStatus.Posted,
                2));
    }

    private static NGB.Core.Documents.DocumentRecord Document(Guid id, string? number)
        => new()
        {
            Id = id,
            TypeCode = PropertyManagementCodes.ReceivablePayment,
            Number = number,
            DateUtc = Now.UtcDateTime,
            Status = NGB.Core.Documents.DocumentStatus.Posted,
            Version = 1,
            CreatedAtUtc = Now.UtcDateTime,
            UpdatedAtUtc = Now.UtcDateTime,
            PostedAtUtc = Now.UtcDateTime
        };

    private static PmReceivableApplyHead Apply(Guid applyId, Guid paymentId)
        => new(
            applyId,
            paymentId,
            Guid.NewGuid(),
            new DateOnly(2026, 7, 26),
            10m,
            null);

    private static DocumentActionAvailabilityResult Disabled()
        => new([new("pm.receivables.apply.no_credit", "Nothing to apply.")]);

    private sealed class FixedTimeProvider(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }
}
