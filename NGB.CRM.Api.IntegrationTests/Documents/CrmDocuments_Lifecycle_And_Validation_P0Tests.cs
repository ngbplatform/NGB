using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using NGB.Application.Abstractions.Services;
using NGB.Contracts.Documents;
using NGB.CRM.Api.IntegrationTests.Infrastructure;
using NGB.CRM.Api.IntegrationTests.Support;
using NGB.CRM.Runtime;
using NGB.CRM.Runtime.DocumentActions;
using NGB.Contracts.Metadata;
using NGB.Core.Documents.Actions;
using NGB.Runtime.Security;
using NGB.Tools.Exceptions;
using Xunit;

namespace NGB.CRM.Api.IntegrationTests.Documents;

[Collection(CrmDocumentsPostgresCollection.Name)]
public sealed class CrmDocuments_Lifecycle_And_Validation_P0Tests(CrmPostgresFixture fixture) : IAsyncLifetime
{
    public Task InitializeAsync() => fixture.ResetDatabaseAsync();
    public Task DisposeAsync() => Task.CompletedTask;

    [Fact]
    public async Task Lead_To_Quote_Workflow_Posts_Documents_Mirrors_Relationships_And_Refreshes_Quote_Amount()
    {
        using var host = CrmHostFactory.Create(fixture.ConnectionString);
        await using var scope = host.Services.CreateAsyncScope();

        var setup = scope.ServiceProvider.GetRequiredService<ICrmSetupService>();
        var catalogs = scope.ServiceProvider.GetRequiredService<ICatalogService>();
        var documents = scope.ServiceProvider.GetRequiredService<IDocumentService>();
        await setup.EnsureDefaultsAsync(CancellationToken.None);

        var qualificationStageId = await CrmIntegrationTestHelpers.GetCatalogIdByDisplayAsync(catalogs, CrmCodes.OpportunityStage, "Qualification");
        var proposalStageId = await CrmIntegrationTestHelpers.GetCatalogIdByDisplayAsync(catalogs, CrmCodes.OpportunityStage, "Proposal");
        var productId = await CrmIntegrationTestHelpers.GetCatalogIdByDisplayAsync(catalogs, CrmCodes.Product, "Platform Subscription");

        var account = await CrmIntegrationTestHelpers.CreateCatalogAsync(catalogs, CrmCodes.Account, new
        {
            display = "Lifecycle Account",
            account_number = "CRM-T100",
            name = "Lifecycle Account",
            account_type = "Prospect",
            industry = "Technology",
            is_active = true
        });
        var contact = await CrmIntegrationTestHelpers.CreateCatalogAsync(catalogs, CrmCodes.Contact, new
        {
            display = "Lifecycle Contact",
            account_id = account.Id,
            first_name = "Lina",
            last_name = "Stone",
            email = "lina.stone@lifecycle.example",
            is_primary = true,
            is_active = true
        });

        var lead = await documents.CreateDraftAsync(CrmCodes.LeadIntake, CrmIntegrationTestHelpers.Payload(new
        {
            document_date_utc = "2026-07-01",
            lead_name = "Lifecycle lead",
            company_name = "Lifecycle Account",
            contact_name = "Lina Stone",
            email = "lina.stone@lifecycle.example",
            lead_source = "Outbound",
            industry = "Technology",
            estimated_value = 25000m,
            currency = CrmCodes.DefaultCurrency
        }), CancellationToken.None);
        lead = await documents.PostAsync(CrmCodes.LeadIntake, lead.Id, CancellationToken.None);
        lead.Status.Should().Be(DocumentStatus.Posted);
        lead.Number.Should().NotBeNullOrWhiteSpace();

        var qualification = await documents.CreateDraftAsync(CrmCodes.LeadQualification, CrmIntegrationTestHelpers.Payload(new
        {
            document_date_utc = "2026-07-02",
            lead_intake_id = lead.Id,
            qualification_state = "Qualified",
            score = 91,
            notes = "Strong fit"
        }), CancellationToken.None);
        qualification = await documents.PostAsync(CrmCodes.LeadQualification, qualification.Id, CancellationToken.None);

        var conversion = await documents.CreateDraftAsync(CrmCodes.LeadConversion, CrmIntegrationTestHelpers.Payload(new
        {
            document_date_utc = "2026-07-03",
            lead_intake_id = lead.Id,
            account_id = account.Id,
            contact_id = contact.Id,
            create_opportunity = true,
            opportunity_name = "Lifecycle opportunity",
            stage_id = qualificationStageId,
            amount = 25000m,
            probability = 35m,
            expected_close_date = "2026-08-15",
            currency = CrmCodes.DefaultCurrency
        }), CancellationToken.None);
        conversion = await documents.PostAsync(CrmCodes.LeadConversion, conversion.Id, CancellationToken.None);

        var update = await documents.CreateDraftAsync(CrmCodes.OpportunityUpdate, CrmIntegrationTestHelpers.Payload(new
        {
            document_date_utc = "2026-07-05",
            opportunity_id = conversion.Id,
            stage_id = proposalStageId,
            amount = 30000m,
            probability = 60m,
            expected_close_date = "2026-08-10",
            status = "Open"
        }), CancellationToken.None);
        update = await documents.PostAsync(CrmCodes.OpportunityUpdate, update.Id, CancellationToken.None);

        var quote = await documents.CreateDraftAsync(CrmCodes.Quote, CrmIntegrationTestHelpers.Payload(
            new
            {
                document_date_utc = "2026-07-06",
                opportunity_id = conversion.Id,
                account_id = account.Id,
                contact_id = contact.Id,
                valid_until = "2026-08-06",
                currency = CrmCodes.DefaultCurrency,
                quote_status = "Presented",
                amount = 0m
            },
            CrmIntegrationTestHelpers.QuoteLines(
                new QuoteLineSeed(1, productId, "Lifecycle subscription", 5m, 1000m, 10m),
                new QuoteLineSeed(2, productId, "Lifecycle enablement", 1m, 2000m, 0m))),
            CancellationToken.None);
        quote = await documents.PostAsync(CrmCodes.Quote, quote.Id, CancellationToken.None);
        quote.Status.Should().Be(DocumentStatus.Posted);

        var activity = await documents.CreateDraftAsync(CrmCodes.ActivityLog, CrmIntegrationTestHelpers.Payload(new
        {
            document_date_utc = "2026-07-07",
            activity_type = "Meeting",
            subject = "Lifecycle proposal review",
            lead_intake_id = lead.Id,
            account_id = account.Id,
            contact_id = contact.Id,
            opportunity_id = conversion.Id,
            outcome = "Proposal accepted for review"
        }), CancellationToken.None);
        activity = await documents.PostAsync(CrmCodes.ActivityLog, activity.Id, CancellationToken.None);

        (await CrmIntegrationTestHelpers.ScalarDecimalAsync(
            fixture.ConnectionString,
            $"SELECT amount FROM doc_crm_quote WHERE document_id = '{quote.Id}';")).Should().Be(6500m);

        (await CrmIntegrationTestHelpers.CountRowsAsync(
            fixture.ConnectionString,
            $"""
            SELECT COUNT(*)::int
            FROM document_relationships
            WHERE (from_document_id, relationship_code_norm, to_document_id) IN (
                ('{qualification.Id}', 'qualifies', '{lead.Id}'),
                ('{conversion.Id}', 'converts', '{lead.Id}'),
                ('{update.Id}', 'updates', '{conversion.Id}'),
                ('{quote.Id}', 'quotes', '{conversion.Id}'),
                ('{activity.Id}', 'activity_for_lead', '{lead.Id}'),
                ('{activity.Id}', 'activity_for_opportunity', '{conversion.Id}'),
                ('{qualification.Id}', 'created_from', '{lead.Id}'),
                ('{conversion.Id}', 'created_from', '{lead.Id}'),
                ('{update.Id}', 'created_from', '{conversion.Id}'),
                ('{quote.Id}', 'created_from', '{conversion.Id}'),
                ('{activity.Id}', 'related_to', '{lead.Id}'),
                ('{activity.Id}', 'related_to', '{conversion.Id}')
            );
            """)).Should().Be(12);

        var graph = await documents.GetRelationshipGraphAsync(
            CrmCodes.LeadConversion,
            conversion.Id,
            depth: 2,
            maxNodes: 20,
            CancellationToken.None);
        graph.Nodes.Select(x => x.EntityId).Should().Contain([lead.Id, qualification.Id, update.Id, quote.Id, activity.Id]);
        graph.Edges.Select(x => x.RelationshipType).Should().Contain([
            "converts",
            "qualifies",
            "updates",
            "quotes",
            "activity_for_lead",
            "activity_for_opportunity",
            "created_from",
            "related_to"
        ]);

        graph.Edges
            .Where(edge => edge.RelationshipType is "created_from" or "related_to")
            .Should()
            .NotBeEmpty("CRM document flow must include platform canonical relationships rendered by the standard flow page");

        (await CrmIntegrationTestHelpers.CountRowsAsync(
            fixture.ConnectionString,
            "SELECT COUNT(*)::int FROM crm_opportunities_current WHERE opportunity_name = 'Lifecycle opportunity' AND amount = 30000 AND probability = 60;")).Should().Be(1);
        (await CrmIntegrationTestHelpers.CountRowsAsync(
            fixture.ConnectionString,
            "SELECT COUNT(*)::int FROM crm_activities_current WHERE subject = 'Lifecycle proposal review';")).Should().Be(1);
    }

    [Fact]
    public async Task Create_Conversion_Action_Creates_An_Incomplete_Draft_That_Can_Be_Completed_And_Posted()
    {
        using var host = CrmHostFactory.Create(fixture.ConnectionString, services =>
        {
            services.RemoveAll<IPermissionSnapshotProvider>();
            services.AddScoped<IPermissionSnapshotProvider, BootstrapAdminPermissionSnapshotProvider>();
        });
        await using var scope = host.Services.CreateAsyncScope();

        var setup = scope.ServiceProvider.GetRequiredService<ICrmSetupService>();
        var catalogs = scope.ServiceProvider.GetRequiredService<ICatalogService>();
        var documents = scope.ServiceProvider.GetRequiredService<IDocumentService>();
        var actionQueries = scope.ServiceProvider.GetRequiredService<IDocumentActionQueryService>();
        var actions = scope.ServiceProvider.GetRequiredService<IDocumentActionDispatcher>();
        await setup.EnsureDefaultsAsync(CancellationToken.None);

        var account = await CrmIntegrationTestHelpers.CreateCatalogAsync(catalogs, CrmCodes.Account, new
        {
            display = "Derived Conversion Account",
            account_number = "CRM-DERIVED-100",
            name = "Derived Conversion Account",
            account_type = "Prospect",
            is_active = true
        });
        var contact = await CrmIntegrationTestHelpers.CreateCatalogAsync(catalogs, CrmCodes.Contact, new
        {
            display = "Derived Conversion Contact",
            account_id = account.Id,
            first_name = "Dana",
            last_name = "Reed",
            email = "dana.reed@derived.example",
            is_primary = true,
            is_active = true
        });

        var lead = await documents.CreateDraftAsync(CrmCodes.LeadIntake, CrmIntegrationTestHelpers.Payload(new
        {
            document_date_utc = "2026-07-31",
            lead_name = "Derived conversion lead",
            company_name = "Derived Conversion Account",
            contact_name = "Dana Reed",
            email = "dana.reed@derived.example",
            estimated_value = 42000m,
            currency = CrmCodes.DefaultCurrency
        }), CancellationToken.None);
        lead = await documents.PostAsync(CrmCodes.LeadIntake, lead.Id, CancellationToken.None);

        var qualification = await documents.CreateDraftAsync(CrmCodes.LeadQualification, CrmIntegrationTestHelpers.Payload(new
        {
            document_date_utc = "2026-07-31",
            lead_intake_id = lead.Id,
            qualification_state = "Qualified",
            score = 95
        }), CancellationToken.None);
        qualification = await documents.PostAsync(CrmCodes.LeadQualification, qualification.Id, CancellationToken.None);

        var editorState = await actionQueries.GetEditorStateAsync(
            CrmCodes.LeadQualification,
            qualification.Id,
            CancellationToken.None);
        editorState.Actions.Should().ContainSingle(action =>
            action.Code == CrmDocumentActionCodes.CreateConversion && action.IsAllowed);

        var result = await actions.ExecuteAsync(
            CrmCodes.LeadQualification,
            qualification.Id,
            new DocumentActionCode(CrmDocumentActionCodes.CreateConversion),
            $"crm-create-conversion:{qualification.Id:D}",
            new ExecuteDocumentActionRequestDto(editorState.DocumentVersion),
            CancellationToken.None);

        result.CreatedDocument.Should().NotBeNull();
        var conversion = result.CreatedDocument!;
        conversion.Status.Should().Be(DocumentStatus.Draft);
        conversion.Payload.Fields.Should().NotBeNull();
        conversion.Payload.Fields!["lead_intake_id"].ToString().Should().Contain(lead.Id.ToString("D"));
        conversion.Payload.Fields["account_id"].ValueKind.Should().Be(System.Text.Json.JsonValueKind.Null);
        conversion.Payload.Fields["contact_id"].ValueKind.Should().Be(System.Text.Json.JsonValueKind.Null);
        conversion.Payload.Fields["opportunity_name"].GetString().Should().Be("Derived conversion lead");
        conversion.Payload.Fields["amount"].GetDecimal().Should().Be(42000m);

        await FluentActions.Awaiting(() => documents.PostAsync(
                CrmCodes.LeadConversion,
                conversion.Id,
                CancellationToken.None))
            .Should()
            .ThrowAsync<NgbArgumentInvalidException>()
            .WithMessage("*Account is required*");

        conversion = await documents.UpdateDraftAsync(CrmCodes.LeadConversion, conversion.Id, CrmIntegrationTestHelpers.Payload(new
        {
            account_id = account.Id
        }), CancellationToken.None);

        await FluentActions.Awaiting(() => documents.PostAsync(
                CrmCodes.LeadConversion,
                conversion.Id,
                CancellationToken.None))
            .Should()
            .ThrowAsync<NgbArgumentInvalidException>()
            .WithMessage("*Contact is required*");

        conversion = await documents.UpdateDraftAsync(CrmCodes.LeadConversion, conversion.Id, CrmIntegrationTestHelpers.Payload(new
        {
            contact_id = contact.Id
        }), CancellationToken.None);
        conversion = await documents.PostAsync(CrmCodes.LeadConversion, conversion.Id, CancellationToken.None);

        conversion.Status.Should().Be(DocumentStatus.Posted);
    }

    [Fact]
    public async Task Conditional_Post_Requirements_Do_Not_Block_Draft_Persistence_And_Return_Domain_Validation()
    {
        using var host = CrmHostFactory.Create(fixture.ConnectionString);
        await using var scope = host.Services.CreateAsyncScope();

        var setup = scope.ServiceProvider.GetRequiredService<ICrmSetupService>();
        var catalogs = scope.ServiceProvider.GetRequiredService<ICatalogService>();
        var documents = scope.ServiceProvider.GetRequiredService<IDocumentService>();
        await setup.EnsureDefaultsAsync(CancellationToken.None);

        var stageId = await CrmIntegrationTestHelpers.GetCatalogIdByDisplayAsync(catalogs, CrmCodes.OpportunityStage, "Qualification");
        var account = await CrmIntegrationTestHelpers.CreateCatalogAsync(catalogs, CrmCodes.Account, new
        {
            display = "Validation Account",
            account_number = "CRM-T200",
            name = "Validation Account",
            account_type = "Prospect",
            is_active = true
        });
        var contact = await CrmIntegrationTestHelpers.CreateCatalogAsync(catalogs, CrmCodes.Contact, new
        {
            display = "Validation Contact",
            account_id = account.Id,
            first_name = "Vera",
            last_name = "Moore",
            email = "vera.moore@validation.example",
            is_primary = true,
            is_active = true
        });

        var lead = await documents.CreateDraftAsync(CrmCodes.LeadIntake, CrmIntegrationTestHelpers.Payload(new
        {
            document_date_utc = "2026-07-01",
            lead_name = "Validation lead",
            contact_name = "Vera Moore"
        }), CancellationToken.None);
        lead = await documents.PostAsync(CrmCodes.LeadIntake, lead.Id, CancellationToken.None);

        var incompleteQualification = await documents.CreateDraftAsync(
            CrmCodes.LeadQualification,
            CrmIntegrationTestHelpers.Payload(new
            {
                document_date_utc = "2026-07-02",
                lead_intake_id = lead.Id,
                qualification_state = "Disqualified",
                score = 20
            }),
            CancellationToken.None);

        await FluentActions.Awaiting(() => documents.PostAsync(
                CrmCodes.LeadQualification,
                incompleteQualification.Id,
                CancellationToken.None))
            .Should()
            .ThrowAsync<NgbArgumentInvalidException>()
            .WithMessage("*Disqualification Reason is required*");

        var incompleteConversion = await documents.CreateDraftAsync(CrmCodes.LeadConversion, CrmIntegrationTestHelpers.Payload(new
        {
            document_date_utc = "2026-07-03",
            lead_intake_id = lead.Id,
            account_id = account.Id,
            contact_id = contact.Id,
            create_opportunity = true,
            amount = 10000m,
            probability = 25m,
            currency = CrmCodes.DefaultCurrency
        }), CancellationToken.None);

        await FluentActions.Awaiting(() => documents.PostAsync(
                CrmCodes.LeadConversion,
                incompleteConversion.Id,
                CancellationToken.None))
            .Should()
            .ThrowAsync<NgbArgumentInvalidException>()
            .WithMessage("*Opportunity Name is required*");

        var conversion = await documents.CreateDraftAsync(CrmCodes.LeadConversion, CrmIntegrationTestHelpers.Payload(new
        {
            document_date_utc = "2026-07-03",
            lead_intake_id = lead.Id,
            account_id = account.Id,
            contact_id = contact.Id,
            create_opportunity = true,
            opportunity_name = "Validation opportunity",
            stage_id = stageId,
            amount = 10000m,
            probability = 25m,
            currency = CrmCodes.DefaultCurrency
        }), CancellationToken.None);
        conversion = await documents.PostAsync(CrmCodes.LeadConversion, conversion.Id, CancellationToken.None);

        var incompleteUpdate = await documents.CreateDraftAsync(CrmCodes.OpportunityUpdate, CrmIntegrationTestHelpers.Payload(new
        {
            document_date_utc = "2026-07-04",
            opportunity_id = conversion.Id,
            stage_id = stageId,
            amount = 10000m,
            probability = 0m,
            status = "Lost"
        }), CancellationToken.None);

        await FluentActions.Awaiting(() => documents.PostAsync(
                CrmCodes.OpportunityUpdate,
                incompleteUpdate.Id,
                CancellationToken.None))
            .Should()
            .ThrowAsync<NgbArgumentInvalidException>()
            .WithMessage("*Loss Reason is required*");

        var invalidQuote = await documents.CreateDraftAsync(CrmCodes.Quote, CrmIntegrationTestHelpers.Payload(new
        {
            document_date_utc = "2026-07-05",
            opportunity_id = conversion.Id,
            account_id = account.Id,
            contact_id = contact.Id,
            valid_until = "2026-07-04",
            currency = CrmCodes.DefaultCurrency,
            quote_status = "Draft",
            amount = 0m
        }), CancellationToken.None);

        await FluentActions.Awaiting(() => documents.PostAsync(CrmCodes.Quote, invalidQuote.Id, CancellationToken.None))
            .Should()
            .ThrowAsync<NgbArgumentInvalidException>()
            .WithMessage("*Valid Until must be on or after Document Date*");

        var emptyQuote = await documents.CreateDraftAsync(CrmCodes.Quote, CrmIntegrationTestHelpers.Payload(new
        {
            document_date_utc = "2026-07-05",
            opportunity_id = conversion.Id,
            account_id = account.Id,
            contact_id = contact.Id,
            valid_until = "2026-07-31",
            currency = CrmCodes.DefaultCurrency,
            quote_status = "Draft",
            amount = 0m
        }), CancellationToken.None);

        await FluentActions.Awaiting(() => documents.PostAsync(CrmCodes.Quote, emptyQuote.Id, CancellationToken.None))
            .Should()
            .ThrowAsync<NgbArgumentInvalidException>()
            .WithMessage("*Quote must contain at least one line*");

        var incompleteActivity = await documents.CreateDraftAsync(CrmCodes.ActivityLog, CrmIntegrationTestHelpers.Payload(new
        {
            document_date_utc = "2026-07-06",
            activity_type = "Email",
            subject = "Follow up with validation lead",
            due_at_utc = "2026-08-01T15:58:00Z"
        }), CancellationToken.None);

        await FluentActions.Awaiting(() => documents.PostAsync(
                CrmCodes.ActivityLog,
                incompleteActivity.Id,
                CancellationToken.None))
            .Should()
            .ThrowAsync<NgbArgumentInvalidException>()
            .WithMessage("*Activity must reference at least one lead, account, contact, or opportunity*");

        var completedActivity = await documents.UpdateDraftAsync(
            CrmCodes.ActivityLog,
            incompleteActivity.Id,
            CrmIntegrationTestHelpers.Payload(new { lead_intake_id = lead.Id }),
            CancellationToken.None);
        completedActivity = await documents.PostAsync(CrmCodes.ActivityLog, completedActivity.Id, CancellationToken.None);
        completedActivity.Status.Should().Be(DocumentStatus.Posted);
    }

    private sealed class BootstrapAdminPermissionSnapshotProvider : IPermissionSnapshotProvider
    {
        private static readonly PermissionSnapshot Snapshot = new(
            userId: null,
            authSubject: "crm-integration-bootstrap-admin",
            isAuthenticated: true,
            isActive: true,
            isBootstrapAdmin: true,
            accessVersion: 1,
            permissions: []);

        public Task<PermissionSnapshot> GetCurrentAsync(CancellationToken ct)
            => Task.FromResult(Snapshot);
    }
}
