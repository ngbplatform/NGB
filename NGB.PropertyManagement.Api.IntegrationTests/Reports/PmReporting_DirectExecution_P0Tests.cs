using System.IO.Compression;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Xml.Linq;
using Dapper;
using Microsoft.Extensions.DependencyInjection;
using NGB.Persistence.UnitOfWork;
using FluentAssertions;
using Microsoft.AspNetCore.Mvc.Testing;
using NGB.Contracts.Reporting;
using NGB.PropertyManagement.Api.IntegrationTests.Infrastructure;
using Xunit;

namespace NGB.PropertyManagement.Api.IntegrationTests.Reports;

[Collection(PmIntegrationCollection.Name)]
public sealed class PmReporting_DirectExecution_P0Tests(PmIntegrationFixture fixture) : IAsyncLifetime
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web) { Converters = { new JsonStringEnumConverter() } };
    public Task InitializeAsync() => fixture.ResetDatabaseAsync();
    public Task DisposeAsync() => Task.CompletedTask;

    [Fact]
    public async Task Pm_ledger_account_summary_uses_one_narrow_aggregate_and_keeps_vertical_filter_semantics()
    {
        await using var factory = new PmApiFactory(fixture);
        await using var scope = factory.Services.CreateAsyncScope();
        var uow = scope.ServiceProvider.GetRequiredService<IUnitOfWork>();
        await uow.EnsureConnectionOpenAsync();
        await uow.Connection.ExecuteAsync("""
            INSERT INTO accounting_accounts(account_id,code,name,account_type,statement_section,negative_balance_policy)
              VALUES (md5('summary-cash')::uuid,'IT-CASH','Cash',0,1,0), (md5('summary-revenue')::uuid,'IT-REVENUE','Revenue',3,4,0);
            INSERT INTO accounting_register_main(document_id,period,debit_account_id,credit_account_id,amount)
              SELECT md5('summary-doc-'||g)::uuid,'2026-01-05'::timestamptz,md5('summary-cash')::uuid,md5('summary-revenue')::uuid,1
              FROM generate_series(1,10001) g;
            """);
        var reports = scope.ServiceProvider.GetRequiredService<NGB.Application.Abstractions.Services.IReportEngine>();
        var request = new ReportExecutionRequestDto(Parameters: new Dictionary<string, string>
            { ["from_utc"] = "2026-01-01", ["to_utc"] = "2026-01-31" }, Limit: 200);
        using (var probe = new NGB.Testing.Reporting.ReportPerformanceProbe("accounting.ledger.analysis", "pm-account-summary"))
        {
            var page = await reports.ExecuteAsync("accounting.ledger.analysis", request, default);
            page.HasMore.Should().BeFalse();
            page.Sheet.Rows.Single(r => r.RowKind == ReportRowKind.Total).Cells[^1].Value!.Value.GetDecimal().Should().Be(0);
            probe.SqlCommands.Count(sql => sql.Contains("FROM accounting_register_main", StringComparison.OrdinalIgnoreCase)).Should().Be(1);
            probe.SqlCommands.Should().NotContain(sql => sql.Contains("JOIN platform_dimension_set_items", StringComparison.OrdinalIgnoreCase));
        }
        var filtered = await reports.ExecuteAsync("accounting.ledger.analysis", request with
        {
            Filters = new Dictionary<string, ReportFilterValueDto> { ["lease_id"] = new(JsonSerializer.SerializeToElement(Guid.NewGuid())) }
        }, default);
        filtered.Sheet.Rows.Should().NotContain(r => r.RowKind == ReportRowKind.Group);
    }

    [Fact]
    public async Task Native_form_export_uses_the_same_permissions_validation_and_streaming_result()
    {
        await using var factory = new PmApiFactory(fixture);
        using var client = factory.CreateClient();
        const string route = "/api/reports/accounting.consistency/export/xlsx/form";
        var request = JsonSerializer.Serialize(new ReportExportRequestDto(Parameters: new Dictionary<string, string> { ["period_utc"] = "2026-09-01" }), Json);
        using var content = new FormUrlEncodedContent(new Dictionary<string, string> { ["request"] = request });
        using var response = await client.PostAsync(route, content);
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        response.Headers.GetValues("X-Accel-Buffering").Should().ContainSingle().Which.Should().Be("no");
        using var zip = new ZipArchive(new MemoryStream(await response.Content.ReadAsByteArrayAsync()));
        zip.GetEntry("xl/worksheets/sheet1.xml").Should().NotBeNull();
        using var invalid = await client.PostAsync(route, new FormUrlEncodedContent(new Dictionary<string, string> { ["request"] = "invalid-json" }));
        invalid.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        using var anonymous = factory.CreateAnonymousClient();
        using var formResponse = await anonymous.PostAsync(route, new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["request"] = request,
            ["access_token"] = client.DefaultRequestHeaders.Authorization!.Parameter!
        }));
        formResponse.StatusCode.Should().Be(HttpStatusCode.OK, "native downloads authenticate the actual signed Keycloak token from the form");
        using var formZip = new ZipArchive(new MemoryStream(await formResponse.Content.ReadAsByteArrayAsync()));
        formZip.GetEntry("xl/worksheets/sheet1.xml").Should().NotBeNull();
        using var denied = await anonymous.PostAsync(route, new FormUrlEncodedContent(new Dictionary<string, string> { ["request"] = request }));
        denied.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        using var invalidToken = await anonymous.PostAsync(route, new FormUrlEncodedContent(new Dictionary<string, string>
            { ["request"] = request, ["access_token"] = "invalid-signature" }));
        invalidToken.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task Integrity_report_pages_and_exports_without_stored_results()
    {
        await using var factory = new PmApiFactory(fixture);
        using var client = factory.CreateClient();
        var parameters = new Dictionary<string, string> { ["period_utc"] = "2026-09-01" };
        foreach (var limit in new[] { 1, 100 })
        {
            using var response = await client.PostAsJsonAsync("/api/reports/accounting.consistency/execute",
                new ReportExecutionRequestDto(Parameters: parameters, Limit: limit));
            response.StatusCode.Should().Be(HttpStatusCode.OK);
            var page = (await response.Content.ReadFromJsonAsync<ReportExecutionResponseDto>(Json))!;
            page.HasMore.Should().BeFalse();
            page.Sheet.Rows.Should().OnlyContain(row => row.RowKind == ReportRowKind.Total);
        }
        using var exported = await client.PostAsJsonAsync("/api/reports/accounting.consistency/export/xlsx", new ReportExportRequestDto(Parameters: parameters));
        exported.StatusCode.Should().Be(HttpStatusCode.OK);
        using var zip = new ZipArchive(new MemoryStream(await exported.Content.ReadAsByteArrayAsync()), ZipArchiveMode.Read);
        zip.GetEntry("xl/worksheets/sheet1.xml").Should().NotBeNull();
    }

    [Fact]
    public async Task Large_occupancy_report_returns_all_buildings_one_global_total_and_the_same_complete_export()
    {
        await using var factory = new PmApiFactory(fixture);
        using var client = factory.CreateClient();
        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var uow = scope.ServiceProvider.GetRequiredService<IUnitOfWork>();
            await uow.BeginTransactionAsync();
            await uow.Connection.ExecuteAsync("""
                INSERT INTO catalogs(id,catalog_code) SELECT md5('building-'||g)::uuid,'pm.property' FROM generate_series(1,10001) g;
                INSERT INTO cat_pm_property(catalog_id,kind,display,address_line1,city,state,zip)
                  SELECT md5('building-'||g)::uuid,'Building','B'||lpad(g::text,5,'0'),'1 Main','City','NJ','12345' FROM generate_series(1,10001) g;
                """, transaction: uow.Transaction);
            await uow.CommitAsync();
        }
        const string root = "/api/reports/pm.occupancy.summary";
        var details = new HashSet<string>();
        var totals = 0;
        string? cursor = null;
        ReportExecutionResponseDto page;
        do
        {
            using var responsePage = await client.PostAsJsonAsync(root + "/execute", new ReportExecutionRequestDto(Limit: 233, Cursor: cursor));
            responsePage.StatusCode.Should().Be(HttpStatusCode.OK);
            page = (await responsePage.Content.ReadFromJsonAsync<ReportExecutionResponseDto>(Json))!;
            foreach (var row in page.Sheet.Rows)
                if (row.RowKind == ReportRowKind.Total) totals++;
                else details.Add(row.Cells[0].Display!).Should().BeTrue();
            cursor = page.NextCursor;
            if (page.HasMore) cursor.Should().NotBeNullOrEmpty();
        } while (page.HasMore);
        details.Should().HaveCount(10001);
        totals.Should().Be(1);
        using var response = await client.PostAsJsonAsync(root + "/export/xlsx", new ReportExportRequestDto());
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        await using var zip = new ZipArchive(new MemoryStream(await response.Content.ReadAsByteArrayAsync()), ZipArchiveMode.Read);
        await using var xml = zip.GetEntry("xl/worksheets/sheet1.xml")!.Open();
        XDocument.Load(xml).Descendants(XName.Get("row", "http://schemas.openxmlformats.org/spreadsheetml/2006/main")).Count().Should().Be(10003);
    }

    [Fact]
    public async Task Direct_pages_and_export_require_authentication_and_validate_input()
    {
        await using var factory = new PmApiFactory(fixture);
        using var client = factory.CreateClient(new WebApplicationFactoryClientOptions { BaseAddress = new Uri("https://localhost") });
        const string root = "/api/reports/accounting.trial_balance";
        var parameters = new Dictionary<string, string> { ["from_utc"] = "2026-09-01", ["to_utc"] = "2026-09-12" };
        using var anonymous = factory.CreateAnonymousClient();
        foreach (var endpoint in new[] { "/execute", "/export/xlsx" })
        {
            using var denied = await anonymous.PostAsJsonAsync(root + endpoint, new { parameters });
            denied.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        }
        using var executed = await client.PostAsJsonAsync(root + "/execute", new ReportExecutionRequestDto(Parameters: parameters, Limit: 100));
        executed.StatusCode.Should().Be(HttpStatusCode.OK);
        var page = (await executed.Content.ReadFromJsonAsync<ReportExecutionResponseDto>(Json))!;
        page.Offset.Should().Be(0);
        page.HasMore.Should().BeFalse();
        page.Sheet.Rows.Should().BeEmpty();
        using var exported = await client.PostAsJsonAsync(root + "/export/xlsx", new ReportExportRequestDto(Parameters: parameters));
        exported.StatusCode.Should().Be(HttpStatusCode.OK);
        using var zip = new ZipArchive(new MemoryStream(await exported.Content.ReadAsByteArrayAsync()), ZipArchiveMode.Read);
        zip.GetEntry("xl/worksheets/sheet1.xml").Should().NotBeNull();
        using var invalid = await client.PostAsJsonAsync(root + "/execute", new ReportExecutionRequestDto());
        invalid.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        using var invalidCursor = await client.PostAsJsonAsync(root + "/execute", new ReportExecutionRequestDto(Parameters: parameters, Cursor: "invalid"));
        invalidCursor.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        using var unbounded = await client.PostAsJsonAsync(root + "/execute", new ReportExecutionRequestDto(Parameters: parameters, DisablePaging: true));
        unbounded.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }
}
