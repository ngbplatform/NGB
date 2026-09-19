using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using NGB.AgencyBilling.Api.IntegrationTests.Infrastructure;
using NGB.AgencyBilling.Api.IntegrationTests.Support;
using NGB.AgencyBilling.Enums;
using NGB.AgencyBilling.Runtime;
using NGB.Application.Abstractions.Services;
using NGB.Contracts.Common;
using NGB.Contracts.Reporting;
using NGB.Testing.Reporting;
using Xunit;

namespace NGB.AgencyBilling.Api.IntegrationTests.Reports;

[CollectionDefinition("Agency report scale", DisableParallelization = true)]
public sealed class AgencyReportScaleCollection : ICollectionFixture<AgencyReportScaleFixture>;

public sealed class AgencyReportScaleFixture : IAsyncLifetime
{
    private readonly AgencyBillingPostgresFixture _database = new();
    public IHost Host { get; private set; } = null!;
    public async Task InitializeAsync()
    {
        await _database.InitializeAsync();
        Host = AgencyBillingHostFactory.Create(_database.ConnectionString);
        await using var scope = Host.Services.CreateAsyncScope();
        var catalogs = scope.ServiceProvider.GetRequiredService<ICatalogService>();
        var documents = scope.ServiceProvider.GetRequiredService<IDocumentService>();
        await scope.ServiceProvider.GetRequiredService<IAgencyBillingSetupService>().EnsureDefaultsAsync(default);
        async Task<Guid> Catalog(string type, object head) => (await catalogs.CreateAsync(type, Payload(head), default)).Id;
        async Task<Guid> Post(string type, object head, string part, object rows)
        {
            var draft = await documents.CreateDraftAsync(type, Payload(head, part, rows), default);
            return (await documents.PostAsync(type, draft.Id, default)).Id;
        }
        var terms = (await catalogs.GetPageAsync(AgencyBillingCodes.PaymentTerms, new PageRequestDto(0, 25, "Net 30"), default)).Items.Single(x => x.Display == "Net 30").Id;
        var service = await Catalog(AgencyBillingCodes.ServiceItem, new { display = "Scale", code = "SCALE", name = "Scale",
            unit_of_measure = (int)AgencyBillingServiceItemUnitOfMeasure.Hour, is_active = true });
        // 10,240 posted time lines, 256 clients/projects/team members and 256 partially billed invoices.
        for (var i = 0; i < 256; i++)
        {
            var client = await Catalog(AgencyBillingCodes.Client, new { display = $"Client {i:D4}", client_code = $"C{i:D4}", name = $"Client {i:D4}",
                status = (int)AgencyBillingClientStatus.Active, payment_terms_id = terms, is_active = true, default_currency = AgencyBillingCodes.DefaultCurrency });
            var member = await Catalog(AgencyBillingCodes.TeamMember, new { display = $"Member {i:D4}", member_code = $"M{i:D4}", full_name = $"Member {i:D4}",
                member_type = (int)AgencyBillingTeamMemberType.Employee, is_active = true, billable_by_default = true, default_billing_rate = 100m, default_cost_rate = 40m });
            var project = await Catalog(AgencyBillingCodes.Project, new { display = $"Project {i:D4}", project_code = $"P{i:D4}", name = $"Project {i:D4}",
                client_id = client, project_manager_id = member, status = (int)AgencyBillingProjectStatus.Active,
                billing_model = (int)AgencyBillingProjectBillingModel.TimeAndMaterials, budget_hours = 100m, budget_amount = 10000m });
            var timesheet = await Post(AgencyBillingCodes.Timesheet,
                new { document_date_utc = "2026-04-10", team_member_id = member, project_id = project, client_id = client,
                    work_date = "2026-04-09", total_hours = 40m, amount = 4000m, cost_amount = 1600m }, "lines",
                Enumerable.Range(1, 40).Select(j => new { ordinal = j, service_item_id = service, description = $"Hour {j}", hours = 1m,
                    billable = true, billing_rate = 100m, cost_rate = 40m, line_amount = 100m, line_cost_amount = 40m }).ToArray());
            await Post(AgencyBillingCodes.SalesInvoice,
                new { document_date_utc = "2026-04-15", due_date = "2026-04-20", client_id = client, project_id = project,
                    currency_code = AgencyBillingCodes.DefaultCurrency, amount = 2000m }, "lines",
                new[] { new { ordinal = 1, service_item_id = service, source_timesheet_id = timesheet,
                    description = "Partial billing", quantity_hours = 20m, rate = 100m, line_amount = 2000m } });
        }
    }
    public async Task DisposeAsync() { Host?.Dispose(); await _database.DisposeAsync(); }
    private static Dictionary<string, JsonElement> Fields(object value) => JsonSerializer.SerializeToElement(value)
        .EnumerateObject().ToDictionary(p => p.Name, p => p.Value.Clone(), StringComparer.OrdinalIgnoreCase);
    private static RecordPayload Payload(object head, string? part = null, object? rows = null) => new(Fields(head),
        part is null ? null : new Dictionary<string, RecordPartPayload> { [part] = new(JsonSerializer.SerializeToElement(rows).EnumerateArray()
            .Select(row => (IReadOnlyDictionary<string, JsonElement>)row.EnumerateObject().ToDictionary(p => p.Name, p => p.Value.Clone())).ToArray()) });
}

[Collection("Agency report scale")]
public sealed class AgencyBillingReporting_ScaleTests(AgencyReportScaleFixture fixture)
{
    [Theory]
    [InlineData(AgencyBillingCodes.UnbilledTimeReport)]
    [InlineData(AgencyBillingCodes.ProjectProfitabilityReport)]
    [InlineData(AgencyBillingCodes.InvoiceRegisterReport)]
    [InlineData(AgencyBillingCodes.ArAgingReport)]
    [InlineData(AgencyBillingCodes.TeamUtilizationReport)]
    public async Task Reports_bound_page_queries_and_complete_exports_on_a_large_posted_dataset(string code)
    {
        await using var scope = fixture.Host.Services.CreateAsyncScope();
        var definition = await scope.ServiceProvider.GetRequiredService<IReportDefinitionProvider>().GetDefinitionAsync(code, default);
        var parameters = (definition.Parameters ?? []).ToDictionary(p => p.Code, p => p.Code == "from_utc" ? "2026-04-01" : "2026-04-30");
        await ReportScaleAssertions.VerifyAsync(scope.ServiceProvider.GetRequiredService<IReportEngine>(),
            scope.ServiceProvider.GetRequiredService<IReportDownloadService>(), code, new ReportExecutionRequestDto(Parameters: parameters), minimumExportRows: 512);
    }
}
