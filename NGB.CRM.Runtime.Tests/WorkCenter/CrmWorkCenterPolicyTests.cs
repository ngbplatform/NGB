using System.Text.Json;
using FluentAssertions;
using Moq;
using NGB.Application.Abstractions.Services;
using NGB.Contracts.Common;
using NGB.Contracts.Metadata;
using NGB.Contracts.Services;
using NGB.Core.Events;
using NGB.Core.Security;
using NGB.Core.WorkCenter;
using NGB.CRM.Documents;
using NGB.CRM.Runtime.WorkCenter;
using NGB.Persistence.Security;

namespace NGB.CRM.Runtime.Tests.WorkCenter;

public sealed class CrmWorkCenterPolicyTests
{
    private static readonly DateTimeOffset Now =
        new(2026, 7, 26, 18, 30, 0, TimeSpan.Zero);

    [Fact]
    public void Exposes_document_action_completed_event_type()
    {
        CreatePolicy().Policy.EventType.Should().Be("ngb.document.action.completed");
    }

    [Theory]
    [InlineData("crm.unrelated", "post")]
    [InlineData(CrmCodes.LeadIntake, "approve")]
    [InlineData(CrmCodes.LeadQualification, "unpost")]
    [InlineData(CrmCodes.LeadConversion, "unpost")]
    public async Task Ignores_unrelated_document_action_events(string documentType, string actionCode)
    {
        var sut = CreatePolicy();

        await sut.Policy.HandleAsync(Context(Guid.NewGuid(), documentType, actionCode), CancellationToken.None);

        sut.Documents.VerifyNoOtherCalls();
        sut.TypedDocuments.VerifyNoOtherCalls();
        sut.Tasks.VerifyNoOtherCalls();
        sut.Notifications.VerifyNoOtherCalls();
        sut.Roles.VerifyNoOtherCalls();
        sut.UserRoles.VerifyNoOtherCalls();
    }

    [Theory]
    [InlineData("post", "Lead intake", "L-100", "Lead intake")]
    [InlineData("repost", null, "L-101", "L-101")]
    [InlineData("post", null, null, "Lead intake")]
    public async Task Posting_lead_intake_creates_qualification_task(
        string actionCode,
        string? display,
        string? number,
        string expectedSourceTitle)
    {
        var leadId = Guid.NewGuid();
        var sut = CreatePolicy();
        CreateWorkCenterTaskRequest? captured = null;
        sut.Documents
            .Setup(service => service.GetByIdAsync(CrmCodes.LeadIntake, leadId, CancellationToken.None))
            .ReturnsAsync(Document(leadId, display, number));
        sut.Tasks
            .Setup(service => service.CreateAsync(It.IsAny<CreateWorkCenterTaskRequest>(), CancellationToken.None))
            .Callback<CreateWorkCenterTaskRequest, CancellationToken>((request, _) => captured = request)
            .ReturnsAsync(Guid.NewGuid());
        await sut.Policy.HandleAsync(Context(leadId, CrmCodes.LeadIntake.ToUpperInvariant(), actionCode), CancellationToken.None);

        captured.Should().NotBeNull();
        captured!.TaskCode.Should().Be("crm.qualify_lead");
        captured.Source.ResourceCode.Should().Be(CrmCodes.LeadIntake);
        captured.Source.EntityId.Should().Be(leadId);
        captured.Source.TitleSnapshot.Should().Be(expectedSourceTitle);
        captured.Source.SubtitleSnapshot.Should().Be(number);
        captured.AssignedRoleCode.Should().Be("crm.sales_rep");
        captured.DueAtUtc.Should().Be(Now.AddDays(2).UtcDateTime);
        captured.PrimaryActionCode.Should().Be("crm.create_qualification");
        captured.NavigationTargetCode.Should().Be("document.editor");
        captured.NavigationParameters.Should().Contain(new KeyValuePair<string, string?>("documentType", CrmCodes.LeadIntake));
        captured.NavigationParameters.Should().Contain(new KeyValuePair<string, string?>("documentId", leadId.ToString()));
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
                $"crm:lead:{leadId:D}:qualify",
                CancellationToken.None))
            .Returns(Task.CompletedTask);

        await sut.Policy.HandleAsync(Context(leadId, CrmCodes.LeadIntake, "UNPOST"), CancellationToken.None);

        sut.Documents.VerifyNoOtherCalls();
        sut.TypedDocuments.VerifyNoOtherCalls();
        sut.Tasks.VerifyAll();
    }

    [Theory]
    [InlineData("post", "Qualification", "Q-100", "Qualification")]
    [InlineData("repost", null, "Q-101", "Q-101")]
    [InlineData("post", null, null, "Lead qualification")]
    public async Task Qualified_lead_completes_qualification_and_creates_conversion_task(
        string actionCode,
        string? display,
        string? number,
        string expectedSourceTitle)
    {
        var qualificationId = Guid.NewGuid();
        var leadId = Guid.NewGuid();
        var sut = CreatePolicy();
        var head = Qualification(qualificationId, leadId, "Qualified");
        var salesRole = Role(CrmWorkCenterCodes.SalesRepresentativeRole);
        var recipient = Guid.NewGuid();
        CreateWorkCenterTaskRequest? captured = null;
        CreateNotificationRequest? capturedNotification = null;
        sut.TypedDocuments
            .Setup(readers => readers.ReadLeadQualificationHeadAsync(qualificationId, CancellationToken.None))
            .ReturnsAsync(head);
        sut.Tasks
            .Setup(service => service.CompleteByDeduplicationKeyAsync(
                $"crm:lead:{leadId:D}:qualify",
                CancellationToken.None))
            .Returns(Task.CompletedTask);
        sut.Documents
            .Setup(service => service.GetByIdAsync(CrmCodes.LeadQualification, qualificationId, CancellationToken.None))
            .ReturnsAsync(Document(qualificationId, display, number));
        sut.Tasks
            .Setup(service => service.CreateAsync(It.IsAny<CreateWorkCenterTaskRequest>(), CancellationToken.None))
            .Callback<CreateWorkCenterTaskRequest, CancellationToken>((request, _) => captured = request)
            .ReturnsAsync(Guid.NewGuid());
        sut.Roles
            .Setup(repository => repository.GetByCodeAsync(
                CrmWorkCenterCodes.SalesRepresentativeRole,
                CancellationToken.None))
            .ReturnsAsync(salesRole);
        sut.UserRoles
            .Setup(repository => repository.GetUserIdsForRoleAsync(
                salesRole.RoleId,
                CancellationToken.None))
            .ReturnsAsync([recipient]);
        sut.Notifications
            .Setup(service => service.CreateAsync(
                It.IsAny<CreateNotificationRequest>(),
                CancellationToken.None))
            .Callback<CreateNotificationRequest, CancellationToken>(
                (request, _) => capturedNotification = request)
            .ReturnsAsync(Guid.NewGuid());

        await sut.Policy.HandleAsync(Context(qualificationId, CrmCodes.LeadQualification, actionCode), CancellationToken.None);

        captured.Should().NotBeNull();
        captured!.TaskCode.Should().Be("crm.convert_qualified_lead");
        captured.Source.ResourceCode.Should().Be(CrmCodes.LeadQualification);
        captured.Source.EntityId.Should().Be(qualificationId);
        captured.Source.TitleSnapshot.Should().Be(expectedSourceTitle);
        captured.Source.SubtitleSnapshot.Should().Be(number);
        captured.DueAtUtc.Should().Be(Now.AddDays(3).UtcDateTime);
        captured.PrimaryActionCode.Should().Be("crm.create_conversion");
        captured.NavigationParameters["documentType"].Should().Be(CrmCodes.LeadQualification);
        captured.NavigationParameters["documentId"].Should().Be(qualificationId.ToString());
        captured.DeduplicationKey.Should().Be($"crm:lead:{leadId:D}:convert");
        capturedNotification.Should().NotBeNull();
        capturedNotification!.DefinitionCode.Should().Be(CrmWorkCenterCodes.LeadQualified);
        capturedNotification.RecipientUserIds.Should().Equal(recipient);
        capturedNotification.DeduplicationKey.Should()
            .Be($"crm:lead:{leadId:D}:qualified:{qualificationId:D}");
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
                $"crm:lead:{leadId:D}:qualify",
                CancellationToken.None))
            .Returns(Task.CompletedTask);
        sut.Tasks
            .Setup(service => service.CancelByDeduplicationKeyAsync(
                $"crm:lead:{leadId:D}:convert",
                CancellationToken.None))
            .Returns(Task.CompletedTask);

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
                $"crm:lead:{leadId:D}:convert",
                CancellationToken.None))
            .Returns(Task.CompletedTask);

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
        sut.Documents
            .Setup(service => service.GetByIdAsync(CrmCodes.ActivityLog, activityId, CancellationToken.None))
            .ReturnsAsync(Document(activityId, "Call customer", "A-100"));
        sut.Tasks
            .Setup(service => service.CreateAsync(It.IsAny<CreateWorkCenterTaskRequest>(), CancellationToken.None))
            .Callback<CreateWorkCenterTaskRequest, CancellationToken>((request, _) => captured = request)
            .ReturnsAsync(Guid.NewGuid());

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
        captured.NavigationParameters["documentType"].Should().Be(CrmCodes.ActivityLog);
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
                $"crm:activity:{activityId:D}:complete",
                CancellationToken.None))
            .Returns(Task.CompletedTask);

        await sut.Policy.HandleAsync(
            Context(activityId, CrmCodes.ActivityLog, "repost"),
            CancellationToken.None);

        sut.Tasks.VerifyAll();
        sut.Documents.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task Unposting_activity_cancels_activity_task_without_loading_document()
    {
        var activityId = Guid.NewGuid();
        var sut = CreatePolicy();
        sut.Tasks
            .Setup(service => service.CancelByDeduplicationKeyAsync(
                $"crm:activity:{activityId:D}:complete",
                CancellationToken.None))
            .Returns(Task.CompletedTask);

        await sut.Policy.HandleAsync(
            Context(activityId, CrmCodes.ActivityLog, "unpost"),
            CancellationToken.None);

        sut.Tasks.VerifyAll();
        sut.Documents.VerifyNoOtherCalls();
        sut.TypedDocuments.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task Posting_won_opportunity_notifies_sales_representatives()
    {
        var updateId = Guid.NewGuid();
        var opportunityId = Guid.NewGuid();
        var role = Role(CrmWorkCenterCodes.SalesRepresentativeRole);
        var recipient = Guid.NewGuid();
        var sut = CreatePolicy();
        CreateNotificationRequest? captured = null;
        sut.TypedDocuments
            .Setup(readers => readers.ReadOpportunityUpdateHeadAsync(updateId, CancellationToken.None))
            .ReturnsAsync(OpportunityUpdate(updateId, opportunityId, "Won"));
        sut.Documents
            .Setup(service => service.GetByIdAsync(CrmCodes.OpportunityUpdate, updateId, CancellationToken.None))
            .ReturnsAsync(Document(updateId, "Opportunity won", "OU-100"));
        sut.Roles
            .Setup(repository => repository.GetByCodeAsync(
                CrmWorkCenterCodes.SalesRepresentativeRole,
                CancellationToken.None))
            .ReturnsAsync(role);
        sut.UserRoles
            .Setup(repository => repository.GetUserIdsForRoleAsync(role.RoleId, CancellationToken.None))
            .ReturnsAsync([recipient]);
        sut.Notifications
            .Setup(service => service.CreateAsync(It.IsAny<CreateNotificationRequest>(), CancellationToken.None))
            .Callback<CreateNotificationRequest, CancellationToken>((request, _) => captured = request)
            .ReturnsAsync(Guid.NewGuid());

        await sut.Policy.HandleAsync(
            Context(updateId, CrmCodes.OpportunityUpdate, "post"),
            CancellationToken.None);

        captured.Should().NotBeNull();
        captured!.DefinitionCode.Should().Be(CrmWorkCenterCodes.OpportunityWon);
        captured.RecipientUserIds.Should().Equal(recipient);
        captured.Source.ResourceCode.Should().Be(CrmCodes.OpportunityUpdate);
        captured.DeduplicationKey.Should()
            .Be($"crm:opportunity:{opportunityId:D}:won:{updateId:D}");
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
        sut.Roles.VerifyNoOtherCalls();
        sut.UserRoles.VerifyNoOtherCalls();
    }

    private static (
        CrmWorkCenterPolicy Policy,
        Mock<IDocumentService> Documents,
        Mock<ICrmDocumentReaders> TypedDocuments,
        Mock<IWorkCenterTaskService> Tasks,
        Mock<INotificationService> Notifications,
        Mock<IPlatformRoleRepository> Roles,
        Mock<IPlatformUserRoleRepository> UserRoles) CreatePolicy()
    {
        var documents = new Mock<IDocumentService>(MockBehavior.Strict);
        var typedDocuments = new Mock<ICrmDocumentReaders>(MockBehavior.Strict);
        var tasks = new Mock<IWorkCenterTaskService>(MockBehavior.Strict);
        var notifications = new Mock<INotificationService>(MockBehavior.Strict);
        var roles = new Mock<IPlatformRoleRepository>(MockBehavior.Strict);
        var userRoles = new Mock<IPlatformUserRoleRepository>(MockBehavior.Strict);
        return (
            new CrmWorkCenterPolicy(
                documents.Object,
                typedDocuments.Object,
                tasks.Object,
                notifications.Object,
                roles.Object,
                userRoles.Object,
                new FixedTimeProvider(Now)),
            documents,
            typedDocuments,
            tasks,
            notifications,
            roles,
            userRoles);
    }

    private static WorkCenterEventContext Context(Guid documentId, string documentType, string actionCode)
    {
        var eventId = Guid.NewGuid();
        return new WorkCenterEventContext(new PlatformOutboxEvent(
            eventId,
            "ngb.document.action.completed",
            1,
            Now.UtcDateTime,
            "tests",
            $"document:{documentId:D}",
            Guid.NewGuid(),
            Guid.NewGuid(),
            eventId,
            JsonSerializer.Serialize(new
            {
                data = new
                {
                    documentId,
                    documentType,
                    actionCode
                }
            }),
            Now.UtcDateTime));
    }

    private static DocumentDto Document(Guid id, string? display, string? number)
        => new(id, display, new RecordPayload(), DocumentStatus.Posted, false, number);

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

    private static PlatformRole Role(string code)
        => new(
            Guid.NewGuid(),
            code,
            code,
            null,
            IsSystem: true,
            IsActive: true,
            Now.UtcDateTime,
            Now.UtcDateTime);

    private sealed class FixedTimeProvider(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }
}
