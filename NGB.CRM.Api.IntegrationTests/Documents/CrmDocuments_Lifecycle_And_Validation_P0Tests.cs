using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using NGB.Application.Abstractions.Services;
using NGB.CRM.Api.IntegrationTests.Infrastructure;
using NGB.CRM.Api.IntegrationTests.Support;
using NGB.CRM.Runtime;
using NGB.Contracts.Metadata;
using NGB.Tools.Exceptions;
using Npgsql;
using Xunit;

namespace NGB.CRM.Api.IntegrationTests.Documents;

[Collection(CrmPostgresCollection.Name)]
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
    public async Task Validators_Block_Invalid_Qualification_And_Quote()
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

        await FluentActions.Awaiting(() => documents.CreateDraftAsync(CrmCodes.LeadQualification, CrmIntegrationTestHelpers.Payload(new
            {
                document_date_utc = "2026-07-02",
                lead_intake_id = lead.Id,
                qualification_state = "Disqualified",
                score = 20
            }), CancellationToken.None))
            .Should()
            .ThrowAsync<PostgresException>()
            .WithMessage("*ck_doc_crm_lead_qualification__disqualification_reason*");

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

        var invalidQuote = await documents.CreateDraftAsync(CrmCodes.Quote, CrmIntegrationTestHelpers.Payload(new
        {
            document_date_utc = "2026-07-04",
            opportunity_id = conversion.Id,
            account_id = account.Id,
            contact_id = contact.Id,
            valid_until = "2026-07-31",
            currency = CrmCodes.DefaultCurrency,
            quote_status = "Draft",
            amount = 0m
        }), CancellationToken.None);

        await FluentActions.Awaiting(() => documents.PostAsync(CrmCodes.Quote, invalidQuote.Id, CancellationToken.None))
            .Should()
            .ThrowAsync<NgbArgumentInvalidException>()
            .WithMessage("*Quote must contain at least one line*");
    }
}
