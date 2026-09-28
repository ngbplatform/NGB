using System.IO.Compression;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Xml.Linq;
using Dapper;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using NGB.Application.Abstractions.Services;
using NGB.Contracts.Common;
using NGB.Contracts.Reporting;
using NGB.Core.Reporting.Exceptions;
using NGB.Persistence.UnitOfWork;
using NGB.PropertyManagement.Api.IntegrationTests.Infrastructure;
using NGB.PropertyManagement.Api.IntegrationTests.Support;
using NGB.PropertyManagement.Runtime;
using Xunit;

namespace NGB.PropertyManagement.Api.IntegrationTests.Reports;

[Collection(PmIntegrationCollection.Name)]
public sealed class PmReporting_PageBoundaries_P0Tests(PmIntegrationFixture fixture) : IAsyncLifetime
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web)
        { Converters = { new JsonStringEnumConverter() } };

    public Task InitializeAsync() => fixture.ResetDatabaseAsync();
    public Task DisposeAsync() => Task.CompletedTask;

    [Theory]
    [InlineData("pm.receivables.open_items")]
    [InlineData("pm.receivables.open_items.details")]
    [InlineData("pm.receivables.aging")]
    [InlineData("pm.tenant.statement")]
    public async Task Canonical_pages_fit_the_rendered_budget_without_losing_documents_or_totals(string code)
    {
        await using var factory = new PmApiFactory(fixture);
        using var client = factory.CreateClient();
        await using var scope = factory.Services.CreateAsyncScope();
        var services = scope.ServiceProvider;
        await services.GetRequiredService<IPropertyManagementSetupService>().EnsureDefaultsAsync(default);
        var catalogs = services.GetRequiredService<ICatalogService>();
        var documents = services.GetRequiredService<IDocumentService>();
        var party = await catalogs.CreateAsync(PropertyManagementCodes.Party, Payload(new { display = "Page boundary tenant" }), default);
        var building = await catalogs.CreateAsync(PropertyManagementCodes.Property, Payload(new
            { kind = "Building", address_line1 = "1 Boundary St", city = "Hoboken", state = "NJ", zip = "07030" }), default);
        var unit = await catalogs.CreateAsync(PropertyManagementCodes.Property, Payload(new
            { kind = "Unit", parent_property_id = building.Id, unit_no = "1" }), default);
        var lease = await documents.CreateDraftAsync(PropertyManagementCodes.Lease, Payload(new
            { property_id = unit.Id, start_on_utc = "2026-01-01", rent_amount = "1000.00" }, LeaseParts.PrimaryTenant(party.Id)), default);
        await documents.PostAsync(PropertyManagementCodes.Lease, lease.Id, default);
        var chargeType = (await catalogs.GetPageAsync(PropertyManagementCodes.ReceivableChargeType, new PageRequestDto(0, 50, null), default))
            .Items.Single(x => x.Display == "Utility");
        var isStatement = code == "pm.tenant.statement";
        var expected = new HashSet<Guid>();
        Guid? openingDocument = null;
        if (isStatement)
            openingDocument = await PostCharge("Opening charge", "2026-08-31", "7.00");
        var request = new ReportExecutionRequestDto(
            Filters: new Dictionary<string, ReportFilterValueDto> { ["lease_id"] = new(JsonSerializer.SerializeToElement(lease.Id)) },
            Parameters: isStatement
                ? new Dictionary<string, string> { ["from_utc"] = "2026-09-01", ["to_utc"] = "2026-09-27" }
                : code == "pm.receivables.aging" ? new Dictionary<string, string> { ["as_of_utc"] = "2026-09-27" } : null,
            Limit: 500);

        // Use real posting, readers, HTTP validation, protected cursors and the renderer.
        // Equal dates force the continuation to use the document-id tie breaker.
        foreach (var count in new[] { 0, 498, 499, 500, 501, 1000 })
        {
            while (expected.Count < count)
                expected.Add(await PostCharge("Boundary " + expected.Count, "2026-09-05", "2.50"));
            await VerifyPages(request, expected, showTotals: true, openingBalance: isStatement ? 7m : null);
            await VerifyPages(request with { Layout = new ReportLayoutDto(ShowGrandTotals: false) }, expected,
                showTotals: false, openingBalance: isStatement ? 7m : null);
        }

        // A cursor advances by data rows, independently of the opening/footer rows and page-size changes.
        await VerifyPages(request with { Limit = 1 }, expected, showTotals: true,
            openingBalance: isStatement ? 7m : null, changePageSize: true);
        await VerifyPages(request with { Limit = 1000 }, expected, showTotals: true, openingBalance: isStatement ? 7m : null);
        if (isStatement)
        {
            var all = expected.Append(openingDocument!.Value).ToHashSet();
            await VerifyPages(request with { Parameters = new Dictionary<string, string> { ["to_utc"] = "2026-09-27" } },
                all, showTotals: true, openingBalance: null, earlierCharge: openingDocument);
        }

        // HTTP requires paging; the internal bounded materialized path must reject overflow too.
        using var materialized = await client.PostAsJsonAsync($"/api/reports/{code}/execute", request with { DisablePaging = true });
        materialized.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        var problem = await materialized.Content.ReadFromJsonAsync<JsonElement>();
        problem.GetProperty("error").GetProperty("code").GetString().Should().Be("ngb.validation.invalid_argument");
        problem.GetProperty("error").GetProperty("context").GetProperty("paramName").GetString().Should().Be("disablePaging");
        var engine = services.GetRequiredService<IReportEngine>();
        var unpaged = () => engine.ExecuteAsync(code, request with { DisablePaging = true }, default);
        await unpaged.Should().ThrowAsync<ReportLayoutValidationException>().WithMessage("*source rows*");

        // The interactive page budget must not truncate the streaming download.
        using var exported = await client.PostAsJsonAsync($"/api/reports/{code}/export/xlsx",
            new ReportExportRequestDto(Filters: request.Filters, Parameters: request.Parameters));
        exported.StatusCode.Should().Be(HttpStatusCode.OK);
        using var zip = new ZipArchive(new MemoryStream(await exported.Content.ReadAsByteArrayAsync()));
        using var sheet = zip.GetEntry("xl/worksheets/sheet1.xml")!.Open();
        var xml = XDocument.Load(sheet);
        var ns = XNamespace.Get("http://schemas.openxmlformats.org/spreadsheetml/2006/main");
        xml.Descendants(ns + "row").Should().HaveCount(expected.Count + (isStatement ? 3 : 2));

        async Task<Guid> PostCharge(string display, string dueOn, string amount)
        {
            var charge = await documents.CreateDraftAsync(PropertyManagementCodes.ReceivableCharge, Payload(new
                { display, lease_id = lease.Id, charge_type_id = chargeType.Id, due_on_utc = dueOn, amount }), default);
            await documents.PostAsync(PropertyManagementCodes.ReceivableCharge, charge.Id, default);
            return charge.Id;
        }

        async Task VerifyPages(ReportExecutionRequestDto execution, HashSet<Guid> expectedIds,
            bool showTotals, decimal? openingBalance, bool changePageSize = false, Guid? earlierCharge = null)
        {
            var seen = new HashSet<Guid>();
            var openingRows = 0;
            var totalRows = 0;
            var pageNumber = 0;
            var balance = openingBalance ?? 0m;
            string? cursor = null;
            ReportExecutionResponseDto page;
            do
            {
                using var response = await client.PostAsJsonAsync($"/api/reports/{code}/execute", execution with
                    { Cursor = cursor, Limit = changePageSize && pageNumber > 0 ? 500 : execution.Limit });
                response.StatusCode.Should().Be(HttpStatusCode.OK,
                    "{0}, {1} documents, page {2}: {3}", code, expectedIds.Count, pageNumber, await response.Content.ReadAsStringAsync());
                page = (await response.Content.ReadFromJsonAsync<ReportExecutionResponseDto>(Json))!;
                page.Offset.Should().Be(seen.Count, "offsets count source documents only");
                page.Sheet.Rows.Count.Should().BeLessThanOrEqualTo(500);
                page.Limit.Should().BeInRange(1, 500);
                page.Sheet.Rows.Count(row => row.RowKind == ReportRowKind.Detail && row.SemanticRole != "opening_balance")
                    .Should().BeLessThanOrEqualTo(page.Limit);
                foreach (var row in page.Sheet.Rows)
                {
                    if (row.SemanticRole == "opening_balance")
                    {
                        pageNumber.Should().Be(0);
                        openingRows++;
                        row.Cells[^1].Value!.Value.GetDecimal().Should().Be(openingBalance);
                    }
                    else if (row.RowKind == ReportRowKind.Total)
                    {
                        page.HasMore.Should().BeFalse();
                        totalRows++;
                        var amount = expectedIds.Count * 2.5m + (earlierCharge.HasValue ? 4.5m : 0m);
                        switch (code)
                        {
                            case "pm.receivables.open_items":
                                row.Cells[2].Value!.Value.GetDecimal().Should().Be(amount);
                                row.Cells[3].Value!.Value.GetDecimal().Should().Be(0);
                                break;
                            case "pm.receivables.open_items.details":
                                row.Cells[6].Value!.Value.GetDecimal().Should().Be(amount);
                                row.Cells[7].Value!.Value.GetDecimal().Should().Be(0);
                                break;
                            case "pm.receivables.aging":
                                row.Cells[5].Value!.Value.GetDecimal().Should().Be(amount);
                                row.Cells[6].Value!.Value.GetDecimal().Should().Be(amount);
                                break;
                            default:
                                row.Cells[4].Value!.Value.GetDecimal().Should().Be(amount);
                                row.Cells[5].Value!.Value.GetDecimal().Should().Be(0);
                                row.Cells[6].Value!.Value.GetDecimal().Should().Be(amount + (openingBalance ?? 0m));
                                break;
                        }
                    }
                    else
                    {
                        var id = row.Cells[1].Action!.DocumentId!.Value;
                        seen.Add(id).Should().BeTrue("each document must appear exactly once");
                        if (isStatement)
                        {
                            balance += id == earlierCharge ? 7m : 2.5m;
                            row.Cells[6].Value!.Value.GetDecimal().Should().Be(balance);
                        }
                    }
                }
                cursor = page.NextCursor;
                if (page.HasMore) cursor.Should().NotBeNullOrWhiteSpace();
                ++pageNumber;
                pageNumber.Should().BeLessThan(10, "continuation must make progress");
            } while (page.HasMore);
            cursor.Should().BeNull();
            seen.Should().BeEquivalentTo(expectedIds);
            openingRows.Should().Be(openingBalance.HasValue ? 1 : 0);
            totalRows.Should().Be(showTotals && (isStatement || expectedIds.Count > 0) ? 1 : 0);
            page.Total.Should().Be(expectedIds.Count);
        }
    }

    [Theory]
    [InlineData(0)]
    [InlineData(499)]
    [InlineData(500)]
    [InlineData(501)]
    [InlineData(998)]
    [InlineData(1000)]
    public async Task Occupancy_last_page_reserves_space_for_the_global_total(int count)
    {
        await using var factory = new PmApiFactory(fixture);
        using var client = factory.CreateClient();
        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var uow = scope.ServiceProvider.GetRequiredService<IUnitOfWork>();
            await uow.BeginTransactionAsync();
            await uow.Connection.ExecuteAsync("""
                INSERT INTO catalogs(id,catalog_code)
                    SELECT md5('boundary-building-'||g)::uuid,'pm.property' FROM generate_series(1,@count) g;
                INSERT INTO cat_pm_property(catalog_id,kind,display,address_line1,city,state,zip)
                    SELECT md5('boundary-building-'||g)::uuid,'Building','B'||lpad(g::text,5,'0'),
                        '1 Main','City','NJ','12345' FROM generate_series(1,@count) g;
                """, new { count }, uow.Transaction);
            await uow.CommitAsync();
        }
        foreach (var showTotals in new[] { true, false })
        {
            var seen = new HashSet<string>();
            var totals = 0;
            string? cursor = null;
            ReportExecutionResponseDto page;
            do
            {
                using var response = await client.PostAsJsonAsync("/api/reports/pm.occupancy.summary/execute",
                    new ReportExecutionRequestDto(Limit: 500, Cursor: cursor, Layout: new ReportLayoutDto(ShowGrandTotals: showTotals)));
                response.StatusCode.Should().Be(HttpStatusCode.OK, await response.Content.ReadAsStringAsync());
                page = (await response.Content.ReadFromJsonAsync<ReportExecutionResponseDto>(Json))!;
                page.Offset.Should().Be(seen.Count);
                page.Sheet.Rows.Count.Should().BeLessThanOrEqualTo(500);
                foreach (var row in page.Sheet.Rows)
                    if (row.RowKind == ReportRowKind.Total)
                    {
                        page.HasMore.Should().BeFalse();
                        totals++;
                    }
                    else seen.Add(row.Cells[0].Display!).Should().BeTrue();
                cursor = page.NextCursor;
                if (page.HasMore) cursor.Should().NotBeNullOrWhiteSpace();
                seen.Count.Should().BeLessThanOrEqualTo(count);
            } while (page.HasMore);
            seen.Should().HaveCount(count);
            totals.Should().Be(showTotals && count > 0 ? 1 : 0);
            cursor.Should().BeNull();
        }
    }

    private static RecordPayload Payload(object fields, IReadOnlyDictionary<string, RecordPartPayload>? parts = null)
        => new(JsonSerializer.SerializeToElement(fields).EnumerateObject().ToDictionary(p => p.Name, p => p.Value), parts);
}
