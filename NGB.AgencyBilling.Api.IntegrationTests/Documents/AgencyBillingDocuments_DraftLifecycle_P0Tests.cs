using System.Text.Json;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using NGB.AgencyBilling.Api.IntegrationTests.Infrastructure;
using NGB.AgencyBilling.Enums;
using NGB.AgencyBilling.Runtime;
using NGB.Application.Abstractions.Services;
using NGB.Contracts.Common;
using NGB.Contracts.Metadata;
using Xunit;

namespace NGB.AgencyBilling.Api.IntegrationTests.Documents;

[Collection(AgencyBillingPostgresCollection.Name)]
public sealed class AgencyBillingDocuments_DraftLifecycle_P0Tests(AgencyBillingPostgresFixture fixture) : IAsyncLifetime
{
    public Task InitializeAsync() => fixture.ResetDatabaseAsync();
    public Task DisposeAsync() => Task.CompletedTask;

    [Fact]
    public async Task Core_AgencyBilling_Documents_Can_Be_Created_And_Posted_With_Computed_Displays()
    {
        using var host = AgencyBillingHostFactory.Create(fixture.ConnectionString);
        await using var scope = host.Services.CreateAsyncScope();

        var setup = scope.ServiceProvider.GetRequiredService<IAgencyBillingSetupService>();
        var catalogs = scope.ServiceProvider.GetRequiredService<ICatalogService>();
        var documents = scope.ServiceProvider.GetRequiredService<IDocumentService>();

        await setup.EnsureDefaultsAsync(CancellationToken.None);

        var paymentTermsId = await GetCatalogIdByDisplayAsync(catalogs, AgencyBillingCodes.PaymentTerms, "Net 30");
        var clientId = await CreateCatalogAsync(catalogs, AgencyBillingCodes.Client, new
        {
            display = "Contoso Advisory",
            client_code = "CLI-200",
            name = "Contoso Advisory",
            status = (int)AgencyBillingClientStatus.Active,
            payment_terms_id = paymentTermsId,
            is_active = true,
            default_currency = AgencyBillingCodes.DefaultCurrency
        });
        var teamMemberId = await CreateCatalogAsync(catalogs, AgencyBillingCodes.TeamMember, new
        {
            display = "Liam Carter",
            member_code = "TM-200",
            full_name = "Liam Carter",
            member_type = (int)AgencyBillingTeamMemberType.Contractor,
            is_active = true,
            billable_by_default = true,
            default_billing_rate = 160m,
            default_cost_rate = 65m
        });
        var serviceItemId = await CreateCatalogAsync(catalogs, AgencyBillingCodes.ServiceItem, new
        {
            display = "Implementation",
            code = "IMPLEMENTATION",
            name = "Implementation",
            unit_of_measure = (int)AgencyBillingServiceItemUnitOfMeasure.Hour,
            is_active = true
        });
        var projectId = await CreateCatalogAsync(catalogs, AgencyBillingCodes.Project, new
        {
            display = "Contoso Rollout",
            project_code = "PRJ-200",
            name = "Contoso Rollout",
            client_id = clientId,
            project_manager_id = teamMemberId,
            status = (int)AgencyBillingProjectStatus.Active,
            billing_model = (int)AgencyBillingProjectBillingModel.TimeAndMaterials,
            budget_hours = 120m,
            budget_amount = 19200m
        });

        var contract = await documents.CreateDraftAsync(
            AgencyBillingCodes.ClientContract,
            Payload(
                new
                {
                    effective_from = "2026-04-01",
                    client_id = clientId,
                    project_id = projectId,
                    currency_code = AgencyBillingCodes.DefaultCurrency,
                    billing_frequency = (int)AgencyBillingContractBillingFrequency.Monthly,
                    payment_terms_id = paymentTermsId,
                    is_active = true
                },
                "lines",
                new[]
                {
                    new
                    {
                        ordinal = 1,
                        service_item_id = serviceItemId,
                        team_member_id = teamMemberId,
                        service_title = "Implementation",
                        billing_rate = 160m,
                        cost_rate = 65m
                    }
                }),
            CancellationToken.None);

        var timesheet = await documents.CreateDraftAsync(
            AgencyBillingCodes.Timesheet,
            Payload(
                new
                {
                    document_date_utc = "2026-04-10",
                    team_member_id = teamMemberId,
                    project_id = projectId,
                    client_id = clientId,
                    work_date = "2026-04-09",
                    total_hours = 8m,
                    amount = 1280m,
                    cost_amount = 520m
                },
                "lines",
                new[]
                {
                    new
                    {
                        ordinal = 1,
                        service_item_id = serviceItemId,
                        description = "Implementation workshop",
                        hours = 8m,
                        billable = true,
                        billing_rate = 160m,
                        cost_rate = 65m,
                        line_amount = 1280m,
                        line_cost_amount = 520m
                    }
                }),
            CancellationToken.None);

        var invoice = await documents.CreateDraftAsync(
            AgencyBillingCodes.SalesInvoice,
            Payload(
                new
                {
                    document_date_utc = "2026-04-15",
                    due_date = "2026-05-15",
                    client_id = clientId,
                    project_id = projectId,
                    contract_id = contract.Id,
                    currency_code = AgencyBillingCodes.DefaultCurrency,
                    amount = 1280m,
                    memo = "April services"
                },
                "lines",
                new[]
                {
                    new
                    {
                        ordinal = 1,
                        service_item_id = serviceItemId,
                        source_timesheet_id = timesheet.Id,
                        description = "Implementation workshop",
                        quantity_hours = 8m,
                        rate = 160m,
                        line_amount = 1280m
                    }
                }),
            CancellationToken.None);

        var payment = await documents.CreateDraftAsync(
            AgencyBillingCodes.CustomerPayment,
            Payload(
                new
                {
                    document_date_utc = "2026-04-20",
                    client_id = clientId,
                    amount = 1280m,
                    reference_number = "WIRE-200"
                },
                "applies",
                new[]
                {
                    new
                    {
                        ordinal = 1,
                        sales_invoice_id = invoice.Id,
                        applied_amount = 1280m
                    }
                }),
            CancellationToken.None);

        contract.Number.Should().NotBeNullOrWhiteSpace();
        timesheet.Number.Should().NotBeNullOrWhiteSpace();
        invoice.Number.Should().NotBeNullOrWhiteSpace();
        payment.Number.Should().NotBeNullOrWhiteSpace();

        contract.Status.Should().Be(DocumentStatus.Draft);
        timesheet.Status.Should().Be(DocumentStatus.Draft);
        invoice.Status.Should().Be(DocumentStatus.Draft);
        payment.Status.Should().Be(DocumentStatus.Draft);

        (await GetDocumentCountAsync(documents, AgencyBillingCodes.ClientContract)).Should().Be(1);
        (await GetDocumentCountAsync(documents, AgencyBillingCodes.Timesheet)).Should().Be(1);
        (await GetDocumentCountAsync(documents, AgencyBillingCodes.SalesInvoice)).Should().Be(1);
        (await GetDocumentCountAsync(documents, AgencyBillingCodes.CustomerPayment)).Should().Be(1);

        contract = await documents.PostAsync(AgencyBillingCodes.ClientContract, contract.Id, CancellationToken.None);
        timesheet = await documents.PostAsync(AgencyBillingCodes.Timesheet, timesheet.Id, CancellationToken.None);
        invoice = await documents.PostAsync(AgencyBillingCodes.SalesInvoice, invoice.Id, CancellationToken.None);
        payment = await documents.PostAsync(AgencyBillingCodes.CustomerPayment, payment.Id, CancellationToken.None);

        contract.Status.Should().Be(DocumentStatus.Posted);
        timesheet.Status.Should().Be(DocumentStatus.Posted);
        invoice.Status.Should().Be(DocumentStatus.Posted);
        payment.Status.Should().Be(DocumentStatus.Posted);

        contract.Display.Should().StartWith("Client Contract ");
        timesheet.Display.Should().StartWith("Timesheet ");
        invoice.Display.Should().StartWith("Sales Invoice ");
        payment.Display.Should().StartWith("Customer Payment ");

        var flow = await documents.GetRelationshipGraphAsync(
            AgencyBillingCodes.SalesInvoice,
            invoice.Id,
            depth: 5,
            maxNodes: 50,
            CancellationToken.None);

        flow.Nodes.Should().ContainSingle(x => x.TypeCode == AgencyBillingCodes.SalesInvoice && x.Title.StartsWith("Sales Invoice "));
        flow.Nodes.Should().ContainSingle(x => x.TypeCode == AgencyBillingCodes.ClientContract && x.Title.StartsWith("Client Contract "));
        flow.Nodes.Should().ContainSingle(x => x.TypeCode == AgencyBillingCodes.Timesheet && x.Title.StartsWith("Timesheet "));
        flow.Edges.Should().Contain(x =>
            x.RelationshipType.Equals("based_on", StringComparison.OrdinalIgnoreCase)
            && x.FromNodeId.Contains(invoice.Id.ToString(), StringComparison.OrdinalIgnoreCase));
    }

    private static async Task<Guid> CreateCatalogAsync(ICatalogService catalogs, string catalogType, object payload)
        => (await catalogs.CreateAsync(catalogType, Payload(payload), CancellationToken.None)).Id;

    private static async Task<Guid> GetCatalogIdByDisplayAsync(ICatalogService catalogs, string catalogType, string display)
    {
        var page = await catalogs.GetPageAsync(catalogType, new PageRequestDto(0, 25, display), CancellationToken.None);
        return page.Items.Single(x => string.Equals(x.Display, display, StringComparison.OrdinalIgnoreCase)).Id;
    }

    private static async Task<int> GetDocumentCountAsync(IDocumentService documents, string documentType)
    {
        var page = await documents.GetPageAsync(documentType, new PageRequestDto(0, 1, null), CancellationToken.None);
        return page.Total.GetValueOrDefault(page.Items.Count);
    }

    private static RecordPayload Payload(object head, string? partName = null, IEnumerable<object>? partRows = null)
    {
        var fields = JsonSerializer.SerializeToElement(head).EnumerateObject().ToDictionary(
            static x => x.Name,
            static x => x.Value.Clone(),
            StringComparer.OrdinalIgnoreCase);

        Dictionary<string, RecordPartPayload>? parts = null;
        if (!string.IsNullOrWhiteSpace(partName) && partRows is not null)
        {
            var rows = partRows.Select(row =>
                    JsonSerializer.SerializeToElement(row).EnumerateObject().ToDictionary(
                        static x => x.Name,
                        static x => x.Value.Clone(),
                        StringComparer.OrdinalIgnoreCase))
                .Cast<IReadOnlyDictionary<string, JsonElement>>()
                .ToArray();

            parts = new Dictionary<string, RecordPartPayload>(StringComparer.OrdinalIgnoreCase)
            {
                [partName] = new RecordPartPayload(rows)
            };
        }

        return new RecordPayload(fields, parts);
    }
}
