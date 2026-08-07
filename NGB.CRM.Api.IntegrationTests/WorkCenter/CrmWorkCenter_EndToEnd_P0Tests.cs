using System.Text.Json;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using NGB.Application.Abstractions.Services;
using NGB.Core.Events;
using NGB.Core.WorkCenter;
using NGB.CRM.Api.IntegrationTests.Infrastructure;
using NGB.CRM.Api.IntegrationTests.Support;
using NGB.CRM.Runtime;
using NGB.CRM.Runtime.WorkCenter;
using NGB.Persistence.AuditLog;
using NGB.Persistence.Security;
using NGB.Persistence.UnitOfWork;
using NGB.Persistence.WorkCenter;
using NGB.Runtime.UnitOfWork;
using Xunit;

namespace NGB.CRM.Api.IntegrationTests.WorkCenter;

[Collection(CrmPostgresCollection.Name)]
public sealed class CrmWorkCenter_EndToEnd_P0Tests(CrmPostgresFixture fixture) : IAsyncLifetime
{
    public Task InitializeAsync() => fixture.ResetDatabaseAsync();
    public Task DisposeAsync() => Task.CompletedTask;

    [Fact]
    public async Task CRM_flows_keep_task_preferences_separate_from_informational_notifications()
    {
        using var host = CrmHostFactory.Create(fixture.ConnectionString);
        await using var scope = host.Services.CreateAsyncScope();

        var setup = scope.ServiceProvider.GetRequiredService<ICrmSetupService>();
        var catalogs = scope.ServiceProvider.GetRequiredService<ICatalogService>();
        var documents = scope.ServiceProvider.GetRequiredService<IDocumentService>();
        var users = scope.ServiceProvider.GetRequiredService<IPlatformUserRepository>();
        var roles = scope.ServiceProvider.GetRequiredService<IPlatformRoleRepository>();
        var userRoles = scope.ServiceProvider.GetRequiredService<IPlatformUserRoleRepository>();
        var preferences = scope.ServiceProvider.GetRequiredService<INotificationPreferenceRepository>();
        var uow = scope.ServiceProvider.GetRequiredService<IUnitOfWork>();
        var policy = scope.ServiceProvider.GetServices<IWorkCenterEventPolicy>()
            .Single(static candidate => candidate is CrmWorkCenterPolicy);

        await setup.EnsureDefaultsAsync(CancellationToken.None);

        var salesRole = await roles.GetByCodeAsync(
            CrmWorkCenterCodes.SalesRepresentativeRole,
            CancellationToken.None);
        salesRole.Should().NotBeNull();
        var adminUser = (await users.GetAllAsync(CancellationToken.None))
            .Single(static user => user.Email == "alex.carter@demo.ngbplatform.com");
        var salesUserId = await uow.ExecuteInUowTransactionAsync(async ct =>
        {
            var userId = await users.UpsertAsync(
                "crm-work-center-sales",
                "sales@example.test",
                "CRM Sales",
                isActive: true,
                ct);
            await userRoles.ReplaceUserRolesAsync(
                userId,
                [salesRole!.RoleId],
                assignedByUserId: adminUser.UserId,
                ct);
            return userId;
        }, CancellationToken.None);

        var qualificationStageId = await CrmIntegrationTestHelpers.GetCatalogIdByDisplayAsync(
            catalogs,
            CrmCodes.OpportunityStage,
            "Qualification");
        var closedWonStageId = await CrmIntegrationTestHelpers.GetCatalogIdByDisplayAsync(
            catalogs,
            CrmCodes.OpportunityStage,
            "Closed Won");
        var account = await CrmIntegrationTestHelpers.CreateCatalogAsync(catalogs, CrmCodes.Account, new
        {
            display = "Work Center Account",
            account_number = "CRM-WC-100",
            name = "Work Center Account",
            account_type = "Prospect",
            is_active = true
        });
        var contact = await CrmIntegrationTestHelpers.CreateCatalogAsync(catalogs, CrmCodes.Contact, new
        {
            display = "Work Center Contact",
            account_id = account.Id,
            first_name = "Sam",
            last_name = "Seller",
            email = "sam.seller@example.test",
            is_primary = true,
            is_active = true
        });

        var lead = await documents.CreateDraftAsync(CrmCodes.LeadIntake, CrmIntegrationTestHelpers.Payload(new
        {
            document_date_utc = "2026-07-29",
            lead_name = "Work Center lead",
            company_name = "Work Center Account",
            contact_name = "Sam Seller",
            email = "sam.seller@example.test",
            lead_source = "Inbound",
            industry = "Technology",
            estimated_value = 12500m,
            currency = CrmCodes.DefaultCurrency
        }), CancellationToken.None);
        lead = await documents.PostAsync(CrmCodes.LeadIntake, lead.Id, CancellationToken.None);
        await HandleAsync(policy, uow, lead.Id, CrmCodes.LeadIntake, "post");

        await uow.ExecuteInUowTransactionAsync(
            ct => preferences.UpsertAsync(
                new NotificationPreferenceRecord(
                    salesUserId,
                    CrmWorkCenterCodes.ConvertQualifiedLeadTask,
                    NotificationChannel.InApp,
                    IsEnabled: false,
                    DateTime.UtcNow,
                    Version: 1),
                ct),
            CancellationToken.None);

        var qualification = await documents.CreateDraftAsync(
            CrmCodes.LeadQualification,
            CrmIntegrationTestHelpers.Payload(new
            {
                document_date_utc = "2026-07-29",
                lead_intake_id = lead.Id,
                qualification_state = "Qualified",
                score = 90,
                notes = "Ready for conversion"
            }),
            CancellationToken.None);
        qualification = await documents.PostAsync(
            CrmCodes.LeadQualification,
            qualification.Id,
            CancellationToken.None);
        await HandleAsync(policy, uow, qualification.Id, CrmCodes.LeadQualification, "post");
        await HandleAsync(policy, uow, qualification.Id, CrmCodes.LeadQualification, "post");

        var conversion = await documents.CreateDraftAsync(
            CrmCodes.LeadConversion,
            CrmIntegrationTestHelpers.Payload(new
            {
                document_date_utc = "2026-07-29",
                lead_intake_id = lead.Id,
                account_id = account.Id,
                contact_id = contact.Id,
                create_opportunity = true,
                opportunity_name = "Work Center opportunity",
                stage_id = qualificationStageId,
                amount = 12500m,
                probability = 40m,
                expected_close_date = "2026-08-31",
                currency = CrmCodes.DefaultCurrency
            }),
            CancellationToken.None);
        conversion = await documents.PostAsync(
            CrmCodes.LeadConversion,
            conversion.Id,
            CancellationToken.None);
        await HandleAsync(policy, uow, conversion.Id, CrmCodes.LeadConversion, "post");

        var activity = await documents.CreateDraftAsync(
            CrmCodes.ActivityLog,
            CrmIntegrationTestHelpers.Payload(new
            {
                document_date_utc = "2026-07-29",
                activity_type = "Call",
                subject = "Follow up on qualified lead",
                lead_intake_id = lead.Id,
                account_id = account.Id,
                contact_id = contact.Id,
                opportunity_id = conversion.Id,
                due_at_utc = DateTime.UtcNow.AddDays(1),
                outcome = (string?)null
            }),
            CancellationToken.None);
        activity = await documents.PostAsync(
            CrmCodes.ActivityLog,
            activity.Id,
            CancellationToken.None);
        await HandleAsync(policy, uow, activity.Id, CrmCodes.ActivityLog, "post");

        var wonUpdate = await documents.CreateDraftAsync(
            CrmCodes.OpportunityUpdate,
            CrmIntegrationTestHelpers.Payload(new
            {
                document_date_utc = "2026-07-29",
                opportunity_id = conversion.Id,
                stage_id = closedWonStageId,
                amount = 12500m,
                probability = 100m,
                expected_close_date = "2026-07-29",
                status = "Won"
            }),
            CancellationToken.None);
        wonUpdate = await documents.PostAsync(
            CrmCodes.OpportunityUpdate,
            wonUpdate.Id,
            CancellationToken.None);
        await HandleAsync(policy, uow, wonUpdate.Id, CrmCodes.OpportunityUpdate, "post");
        await HandleAsync(policy, uow, wonUpdate.Id, CrmCodes.OpportunityUpdate, "post");

        (await CrmIntegrationTestHelpers.CountRowsAsync(
            fixture.ConnectionString,
            $"SELECT COUNT(*)::int FROM platform_tasks WHERE task_code = '{CrmWorkCenterCodes.QualifyLeadTask}' AND status = 3;"))
            .Should().Be(1);
        (await CrmIntegrationTestHelpers.CountRowsAsync(
            fixture.ConnectionString,
            $"SELECT COUNT(*)::int FROM platform_tasks WHERE task_code = '{CrmWorkCenterCodes.ConvertQualifiedLeadTask}' AND status = 3;"))
            .Should().Be(0, "the disabled Convert qualified lead task preference must prevent task creation");
        (await CrmIntegrationTestHelpers.CountRowsAsync(
            fixture.ConnectionString,
            $"SELECT COUNT(*)::int FROM platform_tasks WHERE task_code = '{CrmWorkCenterCodes.CompleteActivityTask}' AND status = 1;"))
            .Should().Be(1);

        await AssertDeliveryCountAsync(CrmWorkCenterCodes.LeadQualified, salesUserId, 1);
        await AssertDeliveryCountAsync(CrmWorkCenterCodes.OpportunityWon, salesUserId, 1);
        (await CrmIntegrationTestHelpers.CountRowsAsync(
            fixture.ConnectionString,
            $"""
             SELECT COUNT(*)::int
             FROM platform_notification_deliveries
             WHERE user_id = '{salesUserId}';
             """)).Should().Be(
                2,
                "CRM tasks must not create duplicate assignment notifications");

        (await CrmIntegrationTestHelpers.CountRowsAsync(
            fixture.ConnectionString,
            $"""
             SELECT COUNT(*)::int
             FROM platform_notification_deliveries
             WHERE user_id = '{adminUser.UserId}';
             """)).Should().Be(0, "CRM Work Center notifications are visible only to CRM Sales Representatives");
    }

    private static Task HandleAsync(
        IWorkCenterEventPolicy policy,
        IUnitOfWork uow,
        Guid documentId,
        string documentType,
        string actionCode)
        => uow.ExecuteInUowTransactionAsync(
            ct => policy.HandleAsync(Context(documentId, documentType, actionCode), ct),
            CancellationToken.None);

    private static WorkCenterEventContext Context(Guid documentId, string documentType, string actionCode)
    {
        var eventId = Guid.CreateVersion7();
        var now = DateTime.UtcNow;
        return new WorkCenterEventContext(
            new PlatformOutboxEvent(
                eventId,
                "ngb.document.action.completed",
                SchemaVersion: 1,
                now,
                "ngb",
                $"document/{documentType}/{documentId:D}",
                ActorUserId: null,
                CorrelationId: eventId,
                CausationId: null,
                JsonSerializer.Serialize(new
                {
                    data = new
                    {
                        documentId,
                        documentType,
                        actionCode
                    }
                }),
                now));
    }

    private async Task AssertDeliveryCountAsync(string definitionCode, Guid userId, int expected)
    {
        var count = await CrmIntegrationTestHelpers.CountRowsAsync(
            fixture.ConnectionString,
            $"""
             SELECT COUNT(*)::int
             FROM platform_notification_deliveries delivery
             JOIN platform_notifications notification ON notification.id = delivery.notification_id
             WHERE delivery.user_id = '{userId}'
               AND notification.definition_code = '{definitionCode}';
             """);
        count.Should().Be(expected);
    }
}
