using System.Text.Json;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using NGB.AgencyBilling.Api.IntegrationTests.Infrastructure;
using NGB.AgencyBilling.Enums;
using NGB.AgencyBilling.Runtime;
using NGB.Application.Abstractions.Services;
using NGB.Contracts.Common;
using NGB.Contracts.Metadata;
using NGB.Contracts.Reporting;
using Xunit;

namespace NGB.AgencyBilling.Api.IntegrationTests.Reports;

[Collection(AgencyBillingPostgresCollection.Name)]
public sealed class AgencyBillingReporting_EndToEnd_P0Tests(AgencyBillingPostgresFixture fixture) : IAsyncLifetime
{
    public Task InitializeAsync() => fixture.ResetDatabaseAsync();
    public Task DisposeAsync() => Task.CompletedTask;

    [Fact]
    public async Task AgencyBilling_Reports_Are_Discoverable_And_Execute_Against_Posted_Documents()
    {
        using var host = AgencyBillingHostFactory.Create(fixture.ConnectionString);
        await using var scope = host.Services.CreateAsyncScope();

        var setup = scope.ServiceProvider.GetRequiredService<IAgencyBillingSetupService>();
        var catalogs = scope.ServiceProvider.GetRequiredService<ICatalogService>();
        var documents = scope.ServiceProvider.GetRequiredService<IDocumentService>();
        var definitions = scope.ServiceProvider.GetRequiredService<IReportDefinitionProvider>();
        var reports = scope.ServiceProvider.GetRequiredService<IReportEngine>();

        await setup.EnsureDefaultsAsync(CancellationToken.None);

        var paymentTermsId = await GetCatalogIdByDisplayAsync(catalogs, AgencyBillingCodes.PaymentTerms, "Net 30");
        var clientId = await CreateCatalogAsync(catalogs, AgencyBillingCodes.Client, new
        {
            display = "Fabrikam Growth",
            client_code = "CLI-300",
            name = "Fabrikam Growth",
            status = (int)AgencyBillingClientStatus.Active,
            payment_terms_id = paymentTermsId,
            is_active = true,
            default_currency = AgencyBillingCodes.DefaultCurrency
        });
        var teamMemberId = await CreateCatalogAsync(catalogs, AgencyBillingCodes.TeamMember, new
        {
            display = "Emma Brooks",
            member_code = "TM-300",
            full_name = "Emma Brooks",
            member_type = (int)AgencyBillingTeamMemberType.Employee,
            is_active = true,
            billable_by_default = true,
            default_billing_rate = 160m,
            default_cost_rate = 65m
        });
        var serviceItemId = await CreateCatalogAsync(catalogs, AgencyBillingCodes.ServiceItem, new
        {
            display = "Strategy",
            code = "STRATEGY",
            name = "Strategy",
            unit_of_measure = (int)AgencyBillingServiceItemUnitOfMeasure.Hour,
            is_active = true
        });
        var projectId = await CreateCatalogAsync(catalogs, AgencyBillingCodes.Project, new
        {
            display = "Fabrikam Acceleration",
            project_code = "PRJ-300",
            name = "Fabrikam Acceleration",
            client_id = clientId,
            project_manager_id = teamMemberId,
            status = (int)AgencyBillingProjectStatus.Active,
            billing_model = (int)AgencyBillingProjectBillingModel.TimeAndMaterials,
            budget_hours = 80m,
            budget_amount = 12800m
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
                        service_title = "Strategy",
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
                        description = "Strategy workshop",
                        hours = 8m,
                        billable = true,
                        billing_rate = 160m,
                        cost_rate = 65m,
                        line_amount = 1280m,
                        line_cost_amount = 520m
                    }
                }),
            CancellationToken.None);

        contract = await documents.PostAsync(AgencyBillingCodes.ClientContract, contract.Id, CancellationToken.None);
        timesheet = await documents.PostAsync(AgencyBillingCodes.Timesheet, timesheet.Id, CancellationToken.None);

        contract.Status.Should().Be(DocumentStatus.Posted);
        timesheet.Status.Should().Be(DocumentStatus.Posted);

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
                    amount = 960m
                },
                "lines",
                new[]
                {
                    new
                    {
                        ordinal = 1,
                        service_item_id = serviceItemId,
                        source_timesheet_id = timesheet.Id,
                        description = "Strategy workshop",
                        quantity_hours = 6m,
                        rate = 160m,
                        line_amount = 960m
                    }
                }),
            CancellationToken.None);

        invoice = await documents.PostAsync(AgencyBillingCodes.SalesInvoice, invoice.Id, CancellationToken.None);
        invoice.Status.Should().Be(DocumentStatus.Posted);

        var payment = await documents.CreateDraftAsync(
            AgencyBillingCodes.CustomerPayment,
            Payload(
                new
                {
                    document_date_utc = "2026-05-18",
                    client_id = clientId,
                    amount = 500m,
                    reference_number = "WIRE-300"
                },
                "applies",
                new[]
                {
                    new
                    {
                        ordinal = 1,
                        sales_invoice_id = invoice.Id,
                        applied_amount = 500m
                    }
                }),
            CancellationToken.None);

        payment = await documents.PostAsync(AgencyBillingCodes.CustomerPayment, payment.Id, CancellationToken.None);
        payment.Status.Should().Be(DocumentStatus.Posted);

        var availableDefinitions = await definitions.GetAllDefinitionsAsync(CancellationToken.None);
        availableDefinitions.Select(x => x.ReportCode).Should().Contain(
        [
            AgencyBillingCodes.UnbilledTimeReport,
            AgencyBillingCodes.ProjectProfitabilityReport,
            AgencyBillingCodes.InvoiceRegisterReport,
            AgencyBillingCodes.ArAgingReport,
            AgencyBillingCodes.TeamUtilizationReport
        ]);

        var unbilledDefinition = await definitions.GetDefinitionAsync(AgencyBillingCodes.UnbilledTimeReport, CancellationToken.None);
        unbilledDefinition.Mode.Should().Be(ReportExecutionMode.Composable);

        var unbilledResponse = await reports.ExecuteAsync(
            AgencyBillingCodes.UnbilledTimeReport,
            new ReportExecutionRequestDto(
                Parameters: new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
                {
                    ["as_of_utc"] = "2026-04-30"
                },
                DisablePaging: true),
            CancellationToken.None);

        unbilledResponse.Sheet.Meta!.Title.Should().Be("Unbilled Time");
        ReadDecimal(unbilledResponse, ReportRowKind.Total, "hours_open__sum").Should().Be(2m);
        ReadDecimal(unbilledResponse, ReportRowKind.Total, "amount_open__sum").Should().Be(320m);

        var unbilledDetailsResponse = await reports.ExecuteAsync(
            AgencyBillingCodes.UnbilledTimeReport,
            new ReportExecutionRequestDto(
                Parameters: new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
                {
                    ["as_of_utc"] = "2026-04-30"
                },
                Layout: new ReportLayoutDto(
                    DetailFields: ["timesheet_display"],
                    ShowDetails: true),
                DisablePaging: true),
            CancellationToken.None);

        ReadCellAction(unbilledDetailsResponse, "timesheet_display").Should().BeEquivalentTo(new ReportCellActionDto(
            "open_document",
            DocumentType: AgencyBillingCodes.Timesheet,
            DocumentId: timesheet.Id));

        var invoiceRegisterResponse = await reports.ExecuteAsync(
            AgencyBillingCodes.InvoiceRegisterReport,
            new ReportExecutionRequestDto(
                Parameters: new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
                {
                    ["from_utc"] = "2026-04-01",
                    ["to_utc"] = "2026-05-31"
                },
                DisablePaging: true),
            CancellationToken.None);

        invoiceRegisterResponse.Sheet.Meta!.Title.Should().Be("Invoice Register");
        ReadDecimal(invoiceRegisterResponse, ReportRowKind.Total, "invoice_amount__sum").Should().Be(960m);
        ReadDecimal(invoiceRegisterResponse, ReportRowKind.Total, "applied_amount__sum").Should().Be(500m);
        ReadDecimal(invoiceRegisterResponse, ReportRowKind.Total, "balance_amount__sum").Should().Be(460m);
        ReadCellAction(invoiceRegisterResponse, "invoice_display").Should().BeEquivalentTo(new ReportCellActionDto(
            "open_document",
            DocumentType: AgencyBillingCodes.SalesInvoice,
            DocumentId: invoice.Id));

        var invoiceRegisterWithContractResponse = await reports.ExecuteAsync(
            AgencyBillingCodes.InvoiceRegisterReport,
            new ReportExecutionRequestDto(
                Parameters: new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
                {
                    ["from_utc"] = "2026-04-01",
                    ["to_utc"] = "2026-05-31"
                },
                Layout: new ReportLayoutDto(
                    RowGroups:
                    [
                        new ReportGroupingDto("client_display"),
                        new ReportGroupingDto("project_display")
                    ],
                    Measures:
                    [
                        new ReportMeasureSelectionDto("invoice_amount"),
                        new ReportMeasureSelectionDto("applied_amount"),
                        new ReportMeasureSelectionDto("balance_amount")
                    ],
                    DetailFields:
                    [
                        "contract_display",
                        "invoice_display",
                        "invoice_date",
                        "due_date",
                        "payment_status"
                    ],
                    Sorts:
                    [
                        new ReportSortDto("client_display"),
                        new ReportSortDto("project_display"),
                        new ReportSortDto("invoice_date")
                    ],
                    ShowDetails: true,
                    ShowSubtotals: true,
                    ShowSubtotalsOnSeparateRows: false,
                    ShowGrandTotals: true),
                DisablePaging: true),
            CancellationToken.None);

        ReadCellAction(invoiceRegisterWithContractResponse, "contract_display").Should().BeEquivalentTo(new ReportCellActionDto(
            "open_document",
            DocumentType: AgencyBillingCodes.ClientContract,
            DocumentId: contract.Id));

        var agingResponse = await reports.ExecuteAsync(
            AgencyBillingCodes.ArAgingReport,
            new ReportExecutionRequestDto(
                Parameters: new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
                {
                    ["as_of_utc"] = "2026-05-20"
                },
                DisablePaging: true),
            CancellationToken.None);

        agingResponse.Sheet.Meta!.Title.Should().Be("AR Aging");
        ReadDecimal(agingResponse, ReportRowKind.Total, "open_amount__sum").Should().Be(460m);
        ReadDecimal(agingResponse, ReportRowKind.Total, "bucket_1_30_amount__sum").Should().Be(460m);
        ReadCellAction(agingResponse, "invoice_display").Should().BeEquivalentTo(new ReportCellActionDto(
            "open_document",
            DocumentType: AgencyBillingCodes.SalesInvoice,
            DocumentId: invoice.Id));

        var teamUtilizationResponse = await reports.ExecuteAsync(
            AgencyBillingCodes.TeamUtilizationReport,
            new ReportExecutionRequestDto(
                Parameters: new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
                {
                    ["from_utc"] = "2026-04-01",
                    ["to_utc"] = "2026-04-30"
                },
                Layout: new ReportLayoutDto(
                    DetailFields:
                    [
                        "timesheet_display",
                        "work_date",
                        "service_item_display"
                    ],
                    ShowDetails: true),
                DisablePaging: true),
            CancellationToken.None);

        teamUtilizationResponse.Sheet.Meta!.Title.Should().Be("Team Utilization");
        ReadCellAction(teamUtilizationResponse, "timesheet_display").Should().BeEquivalentTo(new ReportCellActionDto(
            "open_document",
            DocumentType: AgencyBillingCodes.Timesheet,
            DocumentId: timesheet.Id));
    }

    private static decimal ReadDecimal(ReportExecutionResponseDto response, ReportRowKind rowKind, string columnCode)
    {
        var columnIndex = response.Sheet.Columns
            .Select((column, index) => new { column.Code, Index = index })
            .Single(x => x.Code == columnCode)
            .Index;

        var row = response.Sheet.Rows.Single(x => x.RowKind == rowKind);
        return row.Cells[columnIndex].Value!.Value.GetDecimal();
    }

    private static ReportCellActionDto? ReadCellAction(ReportExecutionResponseDto response, string columnCode)
    {
        var columnIndex = response.Sheet.Columns
            .Select((column, index) => new { column.Code, Index = index })
            .Single(x => x.Code == columnCode)
            .Index;

        var row = response.Sheet.Rows.First(x => x.RowKind == ReportRowKind.Detail);
        return row.Cells[columnIndex].Action;
    }

    private static async Task<Guid> CreateCatalogAsync(ICatalogService catalogs, string catalogType, object value)
        => (await catalogs.CreateAsync(catalogType, FlatPayload(value), CancellationToken.None)).Id;

    private static async Task<Guid> GetCatalogIdByDisplayAsync(ICatalogService catalogs, string catalogType, string display)
    {
        var page = await catalogs.GetPageAsync(catalogType, new PageRequestDto(0, 25, display), CancellationToken.None);
        return page.Items.Single(x => x.Display == display).Id;
    }

    private static RecordPayload FlatPayload(object value)
        => new(
            JsonSerializer.SerializeToElement(value).EnumerateObject().ToDictionary(
                static x => x.Name,
                static x => x.Value.Clone(),
                StringComparer.OrdinalIgnoreCase),
            null);

    private static RecordPayload Payload(object head, string partKey, object rows)
        => new(
            JsonSerializer.SerializeToElement(head).EnumerateObject().ToDictionary(
                static x => x.Name,
                static x => x.Value.Clone(),
                StringComparer.OrdinalIgnoreCase),
            new Dictionary<string, RecordPartPayload>(StringComparer.OrdinalIgnoreCase)
            {
                [partKey] = new RecordPartPayload(
                    JsonSerializer.SerializeToElement(rows).EnumerateArray()
                        .Select(static row => row.EnumerateObject().ToDictionary(
                            static x => x.Name,
                            static x => x.Value.Clone(),
                            StringComparer.OrdinalIgnoreCase))
                        .ToArray())
            });
}
