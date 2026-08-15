using FluentAssertions;
using Moq;
using NGB.Contracts.IntegrationEvents;
using NGB.Application.Abstractions.Services;
using NGB.Core.Documents;
using NGB.Core.WorkCenter;
using NGB.CRM.Documents;
using NGB.CRM.Runtime.WorkCenter;
using NGB.CRM.WorkCenter;
using NGB.Persistence.Documents;
using ContractDocumentStatus = NGB.Contracts.Metadata.DocumentStatus;

namespace NGB.CRM.Runtime.Tests.WorkCenter;

public sealed class CrmWorkCenterPolicyTests
{
    private static readonly DateTimeOffset Now =
        new(2026, 7, 26, 18, 30, 0, TimeSpan.Zero);

    [Fact]
    public void Implements_the_typed_document_action_projection_contract()
    {
        CreatePolicy().Policy.Should().BeAssignableTo<IDocumentActionCompletedWorkCenterPolicy>();
    }

    [Theory]
    [InlineData("crm.unrelated", "post")]
    [InlineData(CrmCodes.LeadIntake, "approve")]
    [InlineData(CrmCodes.LeadQualification, "unpost")]
    [InlineData(CrmCodes.LeadConversion, "unpost")]
    [InlineData(CrmCodes.ActivityLog, "mark_for_deletion")]
    [InlineData(CrmCodes.OpportunityUpdate, "approve")]
    public async Task Ignores_unrelated_document_action_events(string documentType, string actionCode)
    {
        var sut = CreatePolicy();

        await sut.Policy.HandleAsync(Context(Guid.NewGuid(), documentType, actionCode), CancellationToken.None);

        sut.Documents.VerifyNoOtherCalls();
        sut.TypedDocuments.VerifyNoOtherCalls();
        sut.Tasks.VerifyNoOtherCalls();
        sut.Notifications.VerifyNoOtherCalls();
    }

    [Theory]
    [InlineData("post", "Lead intake", "Acme", "Lead intake", "Acme")]
    [InlineData("repost", "Lead without company", null, "Lead without company", null)]
    public async Task Posting_lead_intake_creates_qualification_task(
        string actionCode,
        string leadName,
        string? companyName,
        string expectedSourceTitle,
        string? expectedSubtitle)
    {
        var leadId = Guid.NewGuid();
        var sut = CreatePolicy();
        CreateWorkCenterTaskRequest? captured = null;
        sut.TypedDocuments
            .Setup(readers => readers.ReadLeadIntakeHeadAsync(leadId, CancellationToken.None))
            .ReturnsAsync(Lead(leadId, leadName, companyName));
        sut.Tasks
            .Setup(service => service.CreateAsync(It.IsAny<CreateWorkCenterTaskRequest>(), CancellationToken.None))
            .Callback<CreateWorkCenterTaskRequest, CancellationToken>((request, _) => captured = request)
            .ReturnsAsync(new WorkCenterMutationResult(Guid.NewGuid(), []));
        await sut.Policy.HandleAsync(Context(leadId, CrmCodes.LeadIntake.ToUpperInvariant(), actionCode), CancellationToken.None);

        captured.Should().NotBeNull();
        captured!.TaskCode.Should().Be("crm.qualify_lead");
        captured.Source.ResourceCode.Should().Be(CrmCodes.LeadIntake);
        captured.Source.EntityId.Should().Be(leadId);
        captured.Source.TitleSnapshot.Should().Be(expectedSourceTitle);
        captured.Source.SubtitleSnapshot.Should().Be(expectedSubtitle);
        captured.AssignedRoleCode.Should().Be("crm.sales_rep");
        captured.DueAtUtc.Should().Be(Now.AddDays(2).UtcDateTime);
        captured.PrimaryActionCode!.Value.Value.Should().Be("crm.create_qualification");
        captured.Target!.Code.Should().Be("document.editor");
        captured.Target.Parameters.Should().Contain(new KeyValuePair<string, string?>("documentType", CrmCodes.LeadIntake));
        captured.Target.Parameters.Should().Contain(new KeyValuePair<string, string?>("documentId", leadId.ToString()));
        captured.DeduplicationKey.Should().Be($"crm:lead:{leadId:D}:qualify");
        captured.CorrelationId.Should().NotBeNull();
        captured.CausationId.Should().NotBeNull();
    }

    [Fact]
    public async Task Unposting_lead_intake_cancels_qualification_task_case_insensitively()
    {
        var leadId = Guid.NewGuid();
        var sut = CreatePolicy();
        sut.Tasks
            .Setup(service => service.CancelByDeduplicationKeyAsync(
                CrmWorkCenterCodes.QualifyLeadTask,
                $"crm:lead:{leadId:D}:qualify",
                CancellationToken.None))
            .ReturnsAsync([]);

        await sut.Policy.HandleAsync(Context(leadId, CrmCodes.LeadIntake, "UNPOST"), CancellationToken.None);

        sut.Documents.VerifyNoOtherCalls();
        sut.TypedDocuments.VerifyNoOtherCalls();
        sut.Tasks.VerifyAll();
    }

    [Theory]
    [InlineData("post", "Q-100", "Q-100")]
    [InlineData("repost", "Q-101", "Q-101")]
    [InlineData("post", null, "Lead qualification")]
    public async Task Qualified_lead_completes_qualification_and_creates_conversion_task(
        string actionCode,
        string? number,
        string expectedSourceTitle)
    {
        var qualificationId = Guid.NewGuid();
        var leadId = Guid.NewGuid();
        var sut = CreatePolicy();
        var head = Qualification(qualificationId, leadId, "Qualified");
        var qualificationTaskUserId = Guid.NewGuid();
        var conversionTaskUserId = Guid.NewGuid();
        var notificationUserId = Guid.NewGuid();
        var sharedUserId = Guid.NewGuid();
        CreateWorkCenterTaskRequest? captured = null;
        CreateNotificationRequest? capturedNotification = null;
        sut.TypedDocuments
            .Setup(readers => readers.ReadLeadQualificationHeadAsync(qualificationId, CancellationToken.None))
            .ReturnsAsync(head);
        sut.Tasks
            .Setup(service => service.CompleteByDeduplicationKeyAsync(
                CrmWorkCenterCodes.QualifyLeadTask,
                $"crm:lead:{leadId:D}:qualify",
                CancellationToken.None))
            .ReturnsAsync([qualificationTaskUserId, sharedUserId]);
        sut.Documents
            .Setup(repository => repository.GetAsync(qualificationId, CancellationToken.None))
            .ReturnsAsync(Document(qualificationId, CrmCodes.LeadQualification, number));
        sut.Tasks
            .Setup(service => service.CreateAsync(It.IsAny<CreateWorkCenterTaskRequest>(), CancellationToken.None))
            .Callback<CreateWorkCenterTaskRequest, CancellationToken>((request, _) => captured = request)
            .ReturnsAsync(new WorkCenterMutationResult(Guid.NewGuid(), [conversionTaskUserId, sharedUserId]));
        sut.Notifications
            .Setup(service => service.CreateAsync(
                It.IsAny<CreateNotificationRequest>(),
                CancellationToken.None))
            .Callback<CreateNotificationRequest, CancellationToken>(
                (request, _) => capturedNotification = request)
            .ReturnsAsync(new WorkCenterMutationResult(Guid.NewGuid(), [notificationUserId, sharedUserId]));

        var changedUsers = await sut.Policy.HandleAsync(
            Context(qualificationId, CrmCodes.LeadQualification, actionCode),
            CancellationToken.None);

        captured.Should().NotBeNull();
        captured!.TaskCode.Should().Be("crm.convert_qualified_lead");
        captured.Source.ResourceCode.Should().Be(CrmCodes.LeadQualification);
        captured.Source.EntityId.Should().Be(qualificationId);
        captured.Source.TitleSnapshot.Should().Be(expectedSourceTitle);
        captured.Source.SubtitleSnapshot.Should().Be(number);
        captured.DueAtUtc.Should().Be(Now.AddDays(3).UtcDateTime);
        captured.PrimaryActionCode!.Value.Value.Should().Be("crm.create_conversion");
        captured.Target!.Parameters["documentType"].Should().Be(CrmCodes.LeadQualification);
        captured.Target.Parameters["documentId"].Should().Be(qualificationId.ToString());
        captured.DeduplicationKey.Should().Be($"crm:lead:{leadId:D}:convert");
        capturedNotification.Should().NotBeNull();
        capturedNotification!.DefinitionCode.Should().Be(CrmWorkCenterCodes.LeadQualified);
        capturedNotification.RecipientUserIds.Should().BeEmpty();
        capturedNotification.RecipientRoleCode.Should().Be(CrmWorkCenterCodes.SalesRepresentativeRole);
        capturedNotification.DeduplicationKey.Should()
            .Be($"crm:lead:{leadId:D}:qualified:{qualificationId:D}");
        changedUsers.Should().BeEquivalentTo(
            [qualificationTaskUserId, conversionTaskUserId, notificationUserId, sharedUserId]);
    }

    [Fact]
    public async Task Nonqualified_lead_cancels_conversion_task_after_completing_qualification()
    {
        var qualificationId = Guid.NewGuid();
        var leadId = Guid.NewGuid();
        var sut = CreatePolicy();
        sut.TypedDocuments
            .Setup(readers => readers.ReadLeadQualificationHeadAsync(qualificationId, CancellationToken.None))
            .ReturnsAsync(Qualification(qualificationId, leadId, "Disqualified"));
        sut.Tasks
            .Setup(service => service.CompleteByDeduplicationKeyAsync(
                CrmWorkCenterCodes.QualifyLeadTask,
                $"crm:lead:{leadId:D}:qualify",
                CancellationToken.None))
            .ReturnsAsync([]);
        sut.Tasks
            .Setup(service => service.CancelByDeduplicationKeyAsync(
                CrmWorkCenterCodes.ConvertQualifiedLeadTask,
                $"crm:lead:{leadId:D}:convert",
                CancellationToken.None))
            .ReturnsAsync([]);

        await sut.Policy.HandleAsync(
            Context(qualificationId, CrmCodes.LeadQualification, "post"),
            CancellationToken.None);

        sut.Documents.VerifyNoOtherCalls();
        sut.Tasks.VerifyAll();
    }

    [Theory]
    [InlineData("post")]
    [InlineData("repost")]
    public async Task Posting_conversion_completes_conversion_task(string actionCode)
    {
        var conversionId = Guid.NewGuid();
        var leadId = Guid.NewGuid();
        var sut = CreatePolicy();
        sut.TypedDocuments
            .Setup(readers => readers.ReadLeadConversionHeadAsync(conversionId, CancellationToken.None))
            .ReturnsAsync(new CrmLeadConversionHead(
                conversionId,
                new DateOnly(2026, 7, 26),
                leadId,
                Guid.NewGuid(),
                Guid.NewGuid(),
                false,
                null,
                null,
                null,
                null,
                null,
                null,
                null));
        sut.Tasks
            .Setup(service => service.CompleteByDeduplicationKeyAsync(
                CrmWorkCenterCodes.ConvertQualifiedLeadTask,
                $"crm:lead:{leadId:D}:convert",
                CancellationToken.None))
            .ReturnsAsync([]);

        await sut.Policy.HandleAsync(Context(conversionId, CrmCodes.LeadConversion, actionCode), CancellationToken.None);

        sut.Tasks.VerifyAll();
        sut.Documents.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task Posting_incomplete_due_activity_creates_completion_task()
    {
        var activityId = Guid.NewGuid();
        var dueAt = Now.AddHours(4).UtcDateTime;
        var sut = CreatePolicy();
        CreateWorkCenterTaskRequest? captured = null;
        sut.TypedDocuments
            .Setup(readers => readers.ReadActivityLogHeadAsync(activityId, CancellationToken.None))
            .ReturnsAsync(Activity(activityId, dueAt, completedAtUtc: null));
        sut.Tasks
            .Setup(service => service.CreateAsync(It.IsAny<CreateWorkCenterTaskRequest>(), CancellationToken.None))
            .Callback<CreateWorkCenterTaskRequest, CancellationToken>((request, _) => captured = request)
            .ReturnsAsync(new WorkCenterMutationResult(Guid.NewGuid(), []));

        await sut.Policy.HandleAsync(
            Context(activityId, CrmCodes.ActivityLog, "post"),
            CancellationToken.None);

        captured.Should().NotBeNull();
        captured!.TaskCode.Should().Be(CrmWorkCenterCodes.CompleteActivityTask);
        captured.Title.Should().Be("Complete CRM activity");
        captured.Description.Should().Be("Follow up with the customer");
        captured.Priority.Should().Be(WorkCenterPriority.Normal);
        captured.DueAtUtc.Should().Be(dueAt);
        captured.AssignedRoleCode.Should().Be(CrmWorkCenterCodes.SalesRepresentativeRole);
        captured.Target!.Parameters["documentType"].Should().Be(CrmCodes.ActivityLog);
        captured.DeduplicationKey.Should().Be($"crm:activity:{activityId:D}:complete");
    }

    [Fact]
    public async Task Posting_completed_activity_completes_existing_activity_task()
    {
        var activityId = Guid.NewGuid();
        var sut = CreatePolicy();
        sut.TypedDocuments
            .Setup(readers => readers.ReadActivityLogHeadAsync(activityId, CancellationToken.None))
            .ReturnsAsync(Activity(activityId, Now.AddHours(-1).UtcDateTime, Now.UtcDateTime));
        sut.Tasks
            .Setup(service => service.CompleteByDeduplicationKeyAsync(
                CrmWorkCenterCodes.CompleteActivityTask,
                $"crm:activity:{activityId:D}:complete",
                CancellationToken.None))
            .ReturnsAsync([]);

        await sut.Policy.HandleAsync(
            Context(activityId, CrmCodes.ActivityLog, "repost"),
            CancellationToken.None);

        sut.Tasks.VerifyAll();
        sut.Documents.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task Posting_activity_without_a_due_date_completes_any_existing_activity_task()
    {
        var activityId = Guid.NewGuid();
        var sut = CreatePolicy();
        sut.TypedDocuments
            .Setup(readers => readers.ReadActivityLogHeadAsync(activityId, CancellationToken.None))
            .ReturnsAsync(Activity(activityId, dueAtUtc: null, completedAtUtc: null));
        sut.Tasks
            .Setup(service => service.CompleteByDeduplicationKeyAsync(
                CrmWorkCenterCodes.CompleteActivityTask,
                $"crm:activity:{activityId:D}:complete",
                CancellationToken.None))
            .ReturnsAsync([]);

        await sut.Policy.HandleAsync(
            Context(activityId, CrmCodes.ActivityLog, "post"),
            CancellationToken.None);

        sut.Tasks.VerifyAll();
    }

    [Fact]
    public async Task Posting_overdue_activity_creates_a_high_priority_completion_task()
    {
        var activityId = Guid.NewGuid();
        var sut = CreatePolicy();
        CreateWorkCenterTaskRequest? captured = null;
        sut.TypedDocuments
            .Setup(readers => readers.ReadActivityLogHeadAsync(activityId, CancellationToken.None))
            .ReturnsAsync(Activity(activityId, Now.AddMinutes(-1).UtcDateTime, completedAtUtc: null));
        sut.Tasks
            .Setup(service => service.CreateAsync(It.IsAny<CreateWorkCenterTaskRequest>(), CancellationToken.None))
            .Callback<CreateWorkCenterTaskRequest, CancellationToken>((request, _) => captured = request)
            .ReturnsAsync(new WorkCenterMutationResult(Guid.NewGuid(), []));

        await sut.Policy.HandleAsync(
            Context(activityId, CrmCodes.ActivityLog, "post"),
            CancellationToken.None);

        captured.Should().NotBeNull();
        captured!.Priority.Should().Be(WorkCenterPriority.High);
    }

    [Fact]
    public async Task Unposting_activity_cancels_activity_task_without_loading_document()
    {
        var activityId = Guid.NewGuid();
        var sut = CreatePolicy();
        sut.Tasks
            .Setup(service => service.CancelByDeduplicationKeyAsync(
                CrmWorkCenterCodes.CompleteActivityTask,
                $"crm:activity:{activityId:D}:complete",
                CancellationToken.None))
            .ReturnsAsync([]);

        await sut.Policy.HandleAsync(
            Context(activityId, CrmCodes.ActivityLog, "unpost"),
            CancellationToken.None);

        sut.Tasks.VerifyAll();
        sut.Documents.VerifyNoOtherCalls();
        sut.TypedDocuments.VerifyNoOtherCalls();
    }

    [Theory]
    [InlineData("post")]
    [InlineData("repost")]
    public async Task Posting_won_opportunity_notifies_sales_representatives(string actionCode)
    {
        var updateId = Guid.NewGuid();
        var opportunityId = Guid.NewGuid();
        var sut = CreatePolicy();
        CreateNotificationRequest? captured = null;
        sut.TypedDocuments
            .Setup(readers => readers.ReadOpportunityUpdateHeadAsync(updateId, CancellationToken.None))
            .ReturnsAsync(OpportunityUpdate(updateId, opportunityId, "Won"));
        sut.Documents
            .Setup(repository => repository.GetAsync(updateId, CancellationToken.None))
            .ReturnsAsync(Document(updateId, CrmCodes.OpportunityUpdate, "OU-100"));
        sut.Notifications
            .Setup(service => service.CreateAsync(It.IsAny<CreateNotificationRequest>(), CancellationToken.None))
            .Callback<CreateNotificationRequest, CancellationToken>((request, _) => captured = request)
            .ReturnsAsync(new WorkCenterMutationResult(Guid.NewGuid(), []));

        await sut.Policy.HandleAsync(
            Context(updateId, CrmCodes.OpportunityUpdate, actionCode),
            CancellationToken.None);

        captured.Should().NotBeNull();
        captured!.DefinitionCode.Should().Be(CrmWorkCenterCodes.OpportunityWon);
        captured.RecipientUserIds.Should().BeEmpty();
        captured.RecipientRoleCode.Should().Be(CrmWorkCenterCodes.SalesRepresentativeRole);
        captured.Source.ResourceCode.Should().Be(CrmCodes.OpportunityUpdate);
        captured.DeduplicationKey.Should()
            .Be($"crm:opportunity:{opportunityId:D}:won:{updateId:D}");
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task Won_opportunity_requires_an_existing_document_of_the_expected_type(bool missing)
    {
        var updateId = Guid.NewGuid();
        var sut = CreatePolicy();
        sut.TypedDocuments
            .Setup(readers => readers.ReadOpportunityUpdateHeadAsync(updateId, CancellationToken.None))
            .ReturnsAsync(OpportunityUpdate(updateId, Guid.NewGuid(), "Won"));
        sut.Documents
            .Setup(repository => repository.GetAsync(updateId, CancellationToken.None))
            .ReturnsAsync(missing
                ? null
                : Document(updateId, CrmCodes.LeadIntake, "wrong-type"));

        var action = () => sut.Policy.HandleAsync(
            Context(updateId, CrmCodes.OpportunityUpdate, "post"),
            CancellationToken.None);

        if (missing)
            await action.Should().ThrowAsync<NGB.Core.Documents.Exceptions.DocumentNotFoundException>();
        else
            await action.Should().ThrowAsync<NGB.Core.Documents.Exceptions.DocumentTypeMismatchException>();
        sut.Notifications.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task Posting_open_opportunity_does_not_create_notification()
    {
        var updateId = Guid.NewGuid();
        var sut = CreatePolicy();
        sut.TypedDocuments
            .Setup(readers => readers.ReadOpportunityUpdateHeadAsync(updateId, CancellationToken.None))
            .ReturnsAsync(OpportunityUpdate(updateId, Guid.NewGuid(), "Open"));

        await sut.Policy.HandleAsync(
            Context(updateId, CrmCodes.OpportunityUpdate, "post"),
            CancellationToken.None);

        sut.Notifications.VerifyNoOtherCalls();
    }

    private static (
        CrmWorkCenterPolicy Policy,
        Mock<IDocumentRepository> Documents,
        Mock<ICrmDocumentReaders> TypedDocuments,
        Mock<IWorkCenterTaskService> Tasks,
        Mock<INotificationService> Notifications) CreatePolicy()
    {
        var documents = new Mock<IDocumentRepository>(MockBehavior.Strict);
        var typedDocuments = new Mock<ICrmDocumentReaders>(MockBehavior.Strict);
        var tasks = new Mock<IWorkCenterTaskService>(MockBehavior.Strict);
        var notifications = new Mock<INotificationService>(MockBehavior.Strict);
        return (
            new CrmWorkCenterPolicy(
                documents.Object,
                typedDocuments.Object,
                tasks.Object,
                notifications.Object,
                new FixedTimeProvider(Now)),
            documents,
            typedDocuments,
            tasks,
            notifications);
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
                ContractDocumentStatus.Draft,
                ContractDocumentStatus.Posted,
                2));
    }

    private static DocumentRecord Document(Guid id, string typeCode, string? number)
        => new()
        {
            Id = id,
            TypeCode = typeCode,
            Number = number,
            DateUtc = Now.UtcDateTime,
            Status = DocumentStatus.Posted,
            Version = 2,
            CreatedAtUtc = Now.UtcDateTime,
            UpdatedAtUtc = Now.UtcDateTime
        };

    private static CrmLeadIntakeHead Lead(Guid id, string leadName, string? companyName)
        => new(
            id,
            new DateOnly(2026, 7, 26),
            leadName,
            companyName,
            "Test contact",
            null,
            null,
            null,
            null,
            null,
            null,
            null);

    private static CrmLeadQualificationHead Qualification(Guid id, Guid leadId, string state)
        => new(id, new DateOnly(2026, 7, 26), leadId, state, 90, null, null);

    private static CrmActivityLogHead Activity(
        Guid id,
        DateTime? dueAtUtc,
        DateTime? completedAtUtc)
        => new(
            id,
            new DateOnly(2026, 7, 26),
            "Call",
            "Follow up with the customer",
            Guid.NewGuid(),
            null,
            null,
            null,
            dueAtUtc,
            completedAtUtc,
            null,
            null);

    private static CrmOpportunityUpdateHead OpportunityUpdate(
        Guid id,
        Guid opportunityId,
        string status)
        => new(
            id,
            new DateOnly(2026, 7, 26),
            opportunityId,
            Guid.NewGuid(),
            100_000m,
            100m,
            new DateOnly(2026, 8, 1),
            status,
            null,
            null);

    private sealed class FixedTimeProvider(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }
}
