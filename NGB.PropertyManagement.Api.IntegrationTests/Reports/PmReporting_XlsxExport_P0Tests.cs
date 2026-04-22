using System.IO.Compression;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Xml.Linq;
using FluentAssertions;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using NGB.Application.Abstractions.Services;
using NGB.Contracts.Common;
using NGB.Contracts.Reporting;
using NGB.PropertyManagement.Api.IntegrationTests.Infrastructure;
using NGB.PropertyManagement.Runtime;
using Xunit;

namespace NGB.PropertyManagement.Api.IntegrationTests.Reports;

[Collection(PmIntegrationCollection.Name)]
public sealed class PmReporting_XlsxExport_P0Tests : IAsyncLifetime
{
    private readonly PmIntegrationFixture _fixture;
    private static readonly JsonSerializerOptions Json = CreateJson();

    public PmReporting_XlsxExport_P0Tests(PmIntegrationFixture fixture) => _fixture = fixture;

    public async Task InitializeAsync() => await _fixture.ResetDatabaseAsync();
    public Task DisposeAsync() => Task.CompletedTask;

    [Fact]
    public async Task Export_Xlsx_Returns_OpenXml_File()
    {
        using var factory = new PmApiFactory(_fixture);
        using var client = factory.CreateClient(new WebApplicationFactoryClientOptions { BaseAddress = new Uri("https://localhost") });

        using var resp = await client.PostAsJsonAsync(
            "/api/reports/accounting.ledger.analysis/export/xlsx",
            new ReportExportRequestDto(
                Parameters: new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
                {
                    ["from_utc"] = "2026-03-01",
                    ["to_utc"] = "2026-03-31"
                },
                Layout: new ReportLayoutDto(
                    RowGroups: [new ReportGroupingDto("account_display")],
                    Measures: [new ReportMeasureSelectionDto("debit_amount", ReportAggregationKind.Sum)],
                    Sorts: [new ReportSortDto("account_display")],
                    ShowDetails: false,
                    ShowSubtotals: true,
                    ShowGrandTotals: true)));

        resp.StatusCode.Should().Be(HttpStatusCode.OK);
        resp.Content.Headers.ContentType!.MediaType.Should().Be("application/vnd.openxmlformats-officedocument.spreadsheetml.sheet");

        var bytes = await resp.Content.ReadAsByteArrayAsync();
        bytes.Should().NotBeEmpty();

        await using var archive = new ZipArchive(new MemoryStream(bytes), ZipArchiveMode.Read);
        archive.GetEntry("xl/workbook.xml").Should().NotBeNull();
        archive.GetEntry("xl/worksheets/sheet1.xml").Should().NotBeNull();

        await using var workbookStream = archive.GetEntry("xl/workbook.xml")!.Open();
        using var reader = new StreamReader(workbookStream);
        var workbookXml = await reader.ReadToEndAsync();
        workbookXml.Should().Contain("Ledger Analysis");
    }

    [Fact]
    public async Task Export_Xlsx_Flat_Composable_LedgerAnalysis_Includes_Hierarchy_Header_And_UserFriendly_Display_Values()
    {
        using var factory = new PmApiFactory(_fixture);
        await SeedLedgerAnalysisScenarioAsync(factory);
        using var client = factory.CreateClient(new WebApplicationFactoryClientOptions { BaseAddress = new Uri("https://localhost") });

        using var resp = await client.PostAsJsonAsync(
            "/api/reports/accounting.ledger.analysis/export/xlsx",
            new ReportExportRequestDto(
                Parameters: new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
                {
                    ["from_utc"] = "2026-02-01",
                    ["to_utc"] = "2026-04-30"
                },
                Layout: new ReportLayoutDto(
                    RowGroups:
                    [
                        new ReportGroupingDto("account_display"),
                        new ReportGroupingDto("period_utc", ReportTimeGrain.Month),
                        new ReportGroupingDto("document_display")
                    ],
                    Measures: [new ReportMeasureSelectionDto("debit_amount", ReportAggregationKind.Sum)],
                    Sorts:
                    [
                        new ReportSortDto("account_display"),
                        new ReportSortDto("period_utc", ReportSortDirection.Asc, ReportTimeGrain.Month),
                        new ReportSortDto("document_display")
                    ],
                    ShowDetails: false,
                    ShowSubtotals: true,
                    ShowSubtotalsOnSeparateRows: false,
                    ShowGrandTotals: true)));

        resp.StatusCode.Should().Be(HttpStatusCode.OK);
        resp.Content.Headers.ContentType!.MediaType.Should().Be("application/vnd.openxmlformats-officedocument.spreadsheetml.sheet");

        var bytes = await resp.Content.ReadAsByteArrayAsync();
        bytes.Should().NotBeEmpty();

        using var archive = new ZipArchive(new MemoryStream(bytes), ZipArchiveMode.Read);
        var worksheetStrings = ReadWorksheetStrings(archive);

        worksheetStrings.Should().Contain("Account\nPeriod\nDocument");
        worksheetStrings.Should().Contain("1100 — Accounts Receivable - Tenants");
        worksheetStrings.Should().Contain("February 2026");
        worksheetStrings.Should().Contain(x => x.StartsWith("Receivable ", StringComparison.OrdinalIgnoreCase));
        worksheetStrings.Should().Contain("Total");
    }

    [Fact]
    public async Task Export_Xlsx_Pivot_Composable_LedgerAnalysis_Includes_Display_Headers_And_Merged_Ranges()
    {
        using var factory = new PmApiFactory(_fixture);
        await SeedLedgerAnalysisScenarioAsync(factory);
        using var client = factory.CreateClient(new WebApplicationFactoryClientOptions { BaseAddress = new Uri("https://localhost") });

        using var resp = await client.PostAsJsonAsync(
            "/api/reports/accounting.ledger.analysis/export/xlsx",
            new ReportExportRequestDto(
                Parameters: new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
                {
                    ["from_utc"] = "2026-02-01",
                    ["to_utc"] = "2026-04-30"
                },
                Layout: new ReportLayoutDto(
                    RowGroups: [new ReportGroupingDto("period_utc", ReportTimeGrain.Day)],
                    ColumnGroups:
                    [
                        new ReportGroupingDto("account_display"),
                        new ReportGroupingDto("document_display")
                    ],
                    Measures: [new ReportMeasureSelectionDto("debit_amount", ReportAggregationKind.Sum)],
                    ShowDetails: false,
                    ShowSubtotals: true,
                    ShowSubtotalsOnSeparateRows: false,
                    ShowGrandTotals: true)));

        resp.StatusCode.Should().Be(HttpStatusCode.OK);
        resp.Content.Headers.ContentType!.MediaType.Should().Be("application/vnd.openxmlformats-officedocument.spreadsheetml.sheet");

        var bytes = await resp.Content.ReadAsByteArrayAsync();
        bytes.Should().NotBeEmpty();

        using var archive = new ZipArchive(new MemoryStream(bytes), ZipArchiveMode.Read);
        var worksheetStrings = ReadWorksheetStrings(archive);
        var mergeRefs = ReadWorksheetMergeRefs(archive);

        worksheetStrings.Should().Contain("Period");
        worksheetStrings.Should().Contain("1100 — Accounts Receivable - Tenants");
        worksheetStrings.Should().Contain(x => x.StartsWith("Receivable ", StringComparison.OrdinalIgnoreCase));
        worksheetStrings.Should().Contain("Debit");
        mergeRefs.Should().NotBeEmpty();
    }

    private static IReadOnlyList<string> ReadWorksheetStrings(ZipArchive archive)
    {
        using var stream = archive.GetEntry("xl/worksheets/sheet1.xml")!.Open();
        var doc = XDocument.Load(stream);
        return doc.Descendants().Where(x => x.Name.LocalName == "t").Select(x => x.Value).ToList();
    }

    private static IReadOnlyList<string> ReadWorksheetMergeRefs(ZipArchive archive)
    {
        using var stream = archive.GetEntry("xl/worksheets/sheet1.xml")!.Open();
        var doc = XDocument.Load(stream);
        return doc.Descendants().Where(x => x.Name.LocalName == "mergeCell")
            .Select(x => x.Attribute("ref")?.Value)
            .Where(x => !string.IsNullOrWhiteSpace(x))
            .Cast<string>()
            .ToList();
    }

    private static async Task SeedLedgerAnalysisScenarioAsync(PmApiFactory factory)
    {
        await using var scope = factory.Services.CreateAsyncScope();

        var setup = scope.ServiceProvider.GetRequiredService<IPropertyManagementSetupService>();
        var catalogs = scope.ServiceProvider.GetRequiredService<ICatalogService>();
        var documents = scope.ServiceProvider.GetRequiredService<IDocumentService>();

        await setup.EnsureDefaultsAsync(CancellationToken.None);

        var party = await catalogs.CreateAsync(PropertyManagementCodes.Party, Payload(new { display = "Tenant" }), CancellationToken.None);
        var building = await catalogs.CreateAsync(PropertyManagementCodes.Property, Payload(new
        {
            kind = "Building",
            address_line1 = "1 Hudson St",
            city = "Hoboken",
            state = "NJ",
            zip = "07030"
        }), CancellationToken.None);

        var unit = await catalogs.CreateAsync(PropertyManagementCodes.Property, Payload(new
        {
            kind = "Unit",
            parent_property_id = building.Id,
            unit_no = "101"
        }), CancellationToken.None);

        var lease = await documents.CreateDraftAsync(PropertyManagementCodes.Lease, Payload(new
        {
            property_id = unit.Id,
            start_on_utc = "2026-02-01",
            rent_amount = "1000.00"
        }, LeaseParts.PrimaryTenant(party.Id)), CancellationToken.None);
        await documents.PostAsync(PropertyManagementCodes.Lease, lease.Id, CancellationToken.None);

        var chargeTypes = await catalogs.GetPageAsync(PropertyManagementCodes.ReceivableChargeType, new PageRequestDto(0, 50, null), CancellationToken.None);
        var utilityType = chargeTypes.Items.Single(x => string.Equals(x.Display, "Utility", StringComparison.OrdinalIgnoreCase));

        var charge = await documents.CreateDraftAsync(PropertyManagementCodes.ReceivableCharge, Payload(new
        {
            lease_id = lease.Id,
            charge_type_id = utilityType.Id,
            due_on_utc = "2026-04-05",
            amount = "100.00"
        }), CancellationToken.None);
        await documents.PostAsync(PropertyManagementCodes.ReceivableCharge, charge.Id, CancellationToken.None);

        var payment = await documents.CreateDraftAsync(PropertyManagementCodes.ReceivablePayment, Payload(new
        {
            lease_id = lease.Id,
            received_on_utc = "2026-02-07",
            amount = "120.00"
        }), CancellationToken.None);
        await documents.PostAsync(PropertyManagementCodes.ReceivablePayment, payment.Id, CancellationToken.None);

        var apply = await documents.CreateDraftAsync(PropertyManagementCodes.ReceivableApply, Payload(new
        {
            credit_document_id = payment.Id,
            charge_document_id = charge.Id,
            applied_on_utc = "2026-02-07",
            amount = "70.00"
        }), CancellationToken.None);
        await documents.PostAsync(PropertyManagementCodes.ReceivableApply, apply.Id, CancellationToken.None);
    }

    private static RecordPayload Payload(object fields, IReadOnlyDictionary<string, RecordPartPayload>? parts = null)
    {
        var element = JsonSerializer.SerializeToElement(fields);
        var dict = new Dictionary<string, JsonElement>(StringComparer.OrdinalIgnoreCase);
        foreach (var property in element.EnumerateObject())
            dict[property.Name] = property.Value;

        return new RecordPayload(dict, parts);
    }

    private static JsonSerializerOptions CreateJson()
    {
        var options = new JsonSerializerOptions(JsonSerializerDefaults.Web);
        options.Converters.Add(new JsonStringEnumConverter());
        return options;
    }
}
