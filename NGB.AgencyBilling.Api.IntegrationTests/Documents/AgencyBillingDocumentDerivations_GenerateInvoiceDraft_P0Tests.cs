using System.Text.Json;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using NGB.AgencyBilling.Api.IntegrationTests.Infrastructure;
using NGB.AgencyBilling.Enums;
using NGB.AgencyBilling.Runtime;
using NGB.AgencyBilling.Runtime.Derivations.Exceptions;
using NGB.Application.Abstractions.Services;
using NGB.Contracts.Common;
using NGB.Contracts.Metadata;
using NGB.Tools.Exceptions;
using Xunit;

namespace NGB.AgencyBilling.Api.IntegrationTests.Documents;

[Collection(AgencyBillingPostgresCollection.Name)]
public sealed class AgencyBillingDocumentDerivations_GenerateInvoiceDraft_P0Tests(AgencyBillingPostgresFixture fixture)
    : IAsyncLifetime
{
    public Task InitializeAsync() => fixture.ResetDatabaseAsync();
    public Task DisposeAsync() => Task.CompletedTask;

    [Fact]
    public async Task GenerateInvoiceDraft_Creates_SalesInvoice_Draft_From_Posted_Timesheet()
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
            display = "Northwind Studio",
            client_code = "CLI-410",
            name = "Northwind Studio",
            status = (int)AgencyBillingClientStatus.Active,
            payment_terms_id = paymentTermsId,
            is_active = true,
            default_currency = AgencyBillingCodes.DefaultCurrency
        });
        var teamMemberId = await CreateCatalogAsync(catalogs, AgencyBillingCodes.TeamMember, new
        {
            display = "Amelia Stone",
            member_code = "TM-410",
            full_name = "Amelia Stone",
            member_type = (int)AgencyBillingTeamMemberType.Employee,
            is_active = true,
            billable_by_default = true,
            default_billing_rate = 175m,
            default_cost_rate = 70m
        });
        var serviceItemId = await CreateCatalogAsync(catalogs, AgencyBillingCodes.ServiceItem, new
        {
            display = "Design",
            code = "DESIGN",
            name = "Design",
            unit_of_measure = (int)AgencyBillingServiceItemUnitOfMeasure.Hour,
            is_active = true
        });
        var projectId = await CreateCatalogAsync(catalogs, AgencyBillingCodes.Project, new
        {
            display = "Northwind Website Refresh",
            project_code = "PRJ-410",
            name = "Northwind Website Refresh",
            client_id = clientId,
            project_manager_id = teamMemberId,
            status = (int)AgencyBillingProjectStatus.Active,
            billing_model = (int)AgencyBillingProjectBillingModel.TimeAndMaterials,
            budget_hours = 60m,
            budget_amount = 10500m
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
                    invoice_memo_template = "April delivery billing",
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
                        service_title = "Design",
                        billing_rate = 175m,
                        cost_rate = 70m
                    }
                }),
            CancellationToken.None);

        var timesheet = await documents.CreateDraftAsync(
            AgencyBillingCodes.Timesheet,
            Payload(
                new
                {
                    document_date_utc = "2026-04-12",
                    team_member_id = teamMemberId,
                    project_id = projectId,
                    client_id = clientId,
                    work_date = "2026-04-11",
                    total_hours = 12m,
                    amount = 1750m,
                    cost_amount = 840m
                },
                "lines",
                new object[]
                {
                    new
                    {
                        ordinal = 1,
                        service_item_id = serviceItemId,
                        description = "Discovery workshop",
                        hours = 6m,
                        billable = true,
                        billing_rate = 175m,
                        cost_rate = 70m,
                        line_amount = 1050m,
                        line_cost_amount = 420m
                    },
                    new
                    {
                        ordinal = 2,
                        service_item_id = serviceItemId,
                        description = "Internal retrospective",
                        hours = 2m,
                        billable = false,
                        billing_rate = 175m,
                        cost_rate = 70m,
                        line_amount = 0m,
                        line_cost_amount = 140m
                    },
                    new
                    {
                        ordinal = 3,
                        service_item_id = serviceItemId,
                        description = "Design iteration",
                        hours = 4m,
                        billable = true,
                        billing_rate = 175m,
                        cost_rate = 70m,
                        line_amount = 700m,
                        line_cost_amount = 280m
                    }
                }),
            CancellationToken.None);

        contract = await documents.PostAsync(AgencyBillingCodes.ClientContract, contract.Id, CancellationToken.None);
        timesheet = await documents.PostAsync(AgencyBillingCodes.Timesheet, timesheet.Id, CancellationToken.None);

        var invoice = await documents.DeriveAsync(
            AgencyBillingCodes.SalesInvoice,
            timesheet.Id,
            relationshipType: "created_from",
            initialPayload: null,
            CancellationToken.None);

        var fields = invoice.Payload.Fields!;
        invoice.Status.Should().Be(DocumentStatus.Draft);
        invoice.Display.Should().StartWith("Sales Invoice ");
        GetReferenceId(fields["client_id"]).Should().Be(clientId);
        GetReferenceId(fields["project_id"]).Should().Be(projectId);
        GetReferenceId(fields["contract_id"]).Should().Be(contract.Id);
        fields["currency_code"].GetString().Should().Be(AgencyBillingCodes.DefaultCurrency);
        fields["document_date_utc"].GetString().Should().Be("2026-04-12");
        fields["due_date"].GetString().Should().Be("2026-05-12");
        fields["memo"].GetString().Should().Be("April delivery billing");
        fields["amount"].GetDecimal().Should().Be(1750m);

        var parts = invoice.Payload.Parts;
        parts.Should().NotBeNull();
        var lines = parts!["lines"].Rows;
        lines.Should().HaveCount(2);
        lines[0]["description"].GetString().Should().Be("Discovery workshop");
        lines[0]["quantity_hours"].GetDecimal().Should().Be(6m);
        lines[0]["line_amount"].GetDecimal().Should().Be(1050m);
        GetReferenceId(lines[0]["source_timesheet_id"]).Should().Be(timesheet.Id);
        lines[1]["description"].GetString().Should().Be("Design iteration");
        lines[1]["quantity_hours"].GetDecimal().Should().Be(4m);
        lines[1]["line_amount"].GetDecimal().Should().Be(700m);
        GetReferenceId(lines[1]["source_timesheet_id"]).Should().Be(timesheet.Id);

        var flow = await documents.GetRelationshipGraphAsync(
            AgencyBillingCodes.SalesInvoice,
            invoice.Id,
            depth: 5,
            maxNodes: 50,
            CancellationToken.None);

        flow.Nodes.Should().ContainSingle(x => x.TypeCode == AgencyBillingCodes.Timesheet && x.EntityId == timesheet.Id);
        flow.Nodes.Should().ContainSingle(x => x.TypeCode == AgencyBillingCodes.ClientContract && x.EntityId == contract.Id);
        flow.Edges.Should().Contain(x => string.Equals(x.RelationshipType, "created_from", StringComparison.OrdinalIgnoreCase));
        flow.Edges.Should().Contain(x => string.Equals(x.RelationshipType, "based_on", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task GenerateInvoiceDraft_Blocks_Duplicate_Derivation_For_Same_Timesheet()
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
            display = "Lucerne Advisory",
            client_code = "CLI-420",
            name = "Lucerne Advisory",
            status = (int)AgencyBillingClientStatus.Active,
            payment_terms_id = paymentTermsId,
            is_active = true,
            default_currency = AgencyBillingCodes.DefaultCurrency
        });
        var teamMemberId = await CreateCatalogAsync(catalogs, AgencyBillingCodes.TeamMember, new
        {
            display = "Aiden Perry",
            member_code = "TM-420",
            full_name = "Aiden Perry",
            member_type = (int)AgencyBillingTeamMemberType.Contractor,
            is_active = true,
            billable_by_default = true,
            default_billing_rate = 150m,
            default_cost_rate = 60m
        });
        var serviceItemId = await CreateCatalogAsync(catalogs, AgencyBillingCodes.ServiceItem, new
        {
            display = "Consulting",
            code = "CONSULTING",
            name = "Consulting",
            unit_of_measure = (int)AgencyBillingServiceItemUnitOfMeasure.Hour,
            is_active = true
        });
        var projectId = await CreateCatalogAsync(catalogs, AgencyBillingCodes.Project, new
        {
            display = "Lucerne Growth Audit",
            project_code = "PRJ-420",
            name = "Lucerne Growth Audit",
            client_id = clientId,
            project_manager_id = teamMemberId,
            status = (int)AgencyBillingProjectStatus.Active,
            billing_model = (int)AgencyBillingProjectBillingModel.TimeAndMaterials
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
                        service_title = "Consulting",
                        billing_rate = 150m,
                        cost_rate = 60m
                    }
                }),
            CancellationToken.None);

        var timesheet = await documents.CreateDraftAsync(
            AgencyBillingCodes.Timesheet,
            Payload(
                new
                {
                    document_date_utc = "2026-04-18",
                    team_member_id = teamMemberId,
                    project_id = projectId,
                    client_id = clientId,
                    work_date = "2026-04-17",
                    total_hours = 5m,
                    amount = 750m,
                    cost_amount = 300m
                },
                "lines",
                new[]
                {
                    new
                    {
                        ordinal = 1,
                        service_item_id = serviceItemId,
                        description = "Growth audit work",
                        hours = 5m,
                        billable = true,
                        billing_rate = 150m,
                        cost_rate = 60m,
                        line_amount = 750m,
                        line_cost_amount = 300m
                    }
                }),
            CancellationToken.None);

        await documents.PostAsync(AgencyBillingCodes.ClientContract, contract.Id, CancellationToken.None);
        await documents.PostAsync(AgencyBillingCodes.Timesheet, timesheet.Id, CancellationToken.None);

        await documents.DeriveAsync(
            AgencyBillingCodes.SalesInvoice,
            timesheet.Id,
            relationshipType: "created_from",
            initialPayload: null,
            CancellationToken.None);

        var act = () => documents.DeriveAsync(
            AgencyBillingCodes.SalesInvoice,
            timesheet.Id,
            relationshipType: "created_from",
            initialPayload: null,
            CancellationToken.None);

        var ex = await act.Should().ThrowAsync<AgencyBillingInvoiceDraftAlreadyExistsException>();
        ex.Which.Kind.Should().Be(NgbErrorKind.Validation);
        ex.Which.Context.Should().ContainKey("sourceTimesheetId");
    }

    [Fact]
    public async Task GenerateInvoiceDraft_Blocks_When_Timesheet_Has_No_Billable_Time()
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
            display = "Harbor Creative",
            client_code = "CLI-430",
            name = "Harbor Creative",
            status = (int)AgencyBillingClientStatus.Active,
            payment_terms_id = paymentTermsId,
            is_active = true,
            default_currency = AgencyBillingCodes.DefaultCurrency
        });
        var teamMemberId = await CreateCatalogAsync(catalogs, AgencyBillingCodes.TeamMember, new
        {
            display = "Mila Ross",
            member_code = "TM-430",
            full_name = "Mila Ross",
            member_type = (int)AgencyBillingTeamMemberType.Employee,
            is_active = true,
            billable_by_default = true,
            default_billing_rate = 140m,
            default_cost_rate = 55m
        });
        var serviceItemId = await CreateCatalogAsync(catalogs, AgencyBillingCodes.ServiceItem, new
        {
            display = "Production",
            code = "PRODUCTION",
            name = "Production",
            unit_of_measure = (int)AgencyBillingServiceItemUnitOfMeasure.Hour,
            is_active = true
        });
        var projectId = await CreateCatalogAsync(catalogs, AgencyBillingCodes.Project, new
        {
            display = "Harbor Campaign",
            project_code = "PRJ-430",
            name = "Harbor Campaign",
            client_id = clientId,
            project_manager_id = teamMemberId,
            status = (int)AgencyBillingProjectStatus.Active,
            billing_model = (int)AgencyBillingProjectBillingModel.TimeAndMaterials
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
                        service_title = "Production",
                        billing_rate = 140m,
                        cost_rate = 55m
                    }
                }),
            CancellationToken.None);

        var timesheet = await documents.CreateDraftAsync(
            AgencyBillingCodes.Timesheet,
            Payload(
                new
                {
                    document_date_utc = "2026-04-21",
                    team_member_id = teamMemberId,
                    project_id = projectId,
                    client_id = clientId,
                    work_date = "2026-04-20",
                    total_hours = 3m,
                    amount = 0m,
                    cost_amount = 165m
                },
                "lines",
                new[]
                {
                    new
                    {
                        ordinal = 1,
                        service_item_id = serviceItemId,
                        description = "Internal production review",
                        hours = 3m,
                        billable = false,
                        billing_rate = 140m,
                        cost_rate = 55m,
                        line_amount = 0m,
                        line_cost_amount = 165m
                    }
                }),
            CancellationToken.None);

        await documents.PostAsync(AgencyBillingCodes.ClientContract, contract.Id, CancellationToken.None);
        await documents.PostAsync(AgencyBillingCodes.Timesheet, timesheet.Id, CancellationToken.None);

        var act = () => documents.DeriveAsync(
            AgencyBillingCodes.SalesInvoice,
            timesheet.Id,
            relationshipType: "created_from",
            initialPayload: null,
            CancellationToken.None);

        var ex = await act.Should().ThrowAsync<AgencyBillingInvoiceDraftNoBillableTimeException>();
        ex.Which.Kind.Should().Be(NgbErrorKind.Conflict);
        ex.Which.Context.Should().ContainKey("sourceTimesheetId");
    }

    private static async Task<Guid> CreateCatalogAsync(ICatalogService catalogs, string catalogType, object payload)
        => (await catalogs.CreateAsync(catalogType, Payload(payload), CancellationToken.None)).Id;

    private static async Task<Guid> GetCatalogIdByDisplayAsync(ICatalogService catalogs, string catalogType, string display)
    {
        var page = await catalogs.GetPageAsync(catalogType, new PageRequestDto(0, 25, display), CancellationToken.None);
        return page.Items.Single(x => string.Equals(x.Display, display, StringComparison.OrdinalIgnoreCase)).Id;
    }

    private static Guid GetReferenceId(JsonElement element)
        => Guid.Parse(element.GetProperty("id").GetString()!);

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
