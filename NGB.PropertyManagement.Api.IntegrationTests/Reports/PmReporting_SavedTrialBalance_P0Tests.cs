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
public sealed class PmReporting_SavedTrialBalance_P0Tests(PmIntegrationFixture fixture) : IAsyncLifetime
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web) { Converters = { new JsonStringEnumConverter() } };
    public Task InitializeAsync() => fixture.ResetDatabaseAsync();
    public Task DisposeAsync() => Task.CompletedTask;

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
        using var started = await client.PostAsJsonAsync(root + "/runs", new ReportExecutionRequestDto());
        started.StatusCode.Should().Be(HttpStatusCode.Accepted);
        var run = (await started.Content.ReadFromJsonAsync<ReportRunDto>())!;
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(60));
        while (run.Status is "Queued" or "Running")
        {
            await Task.Delay(100, deadline.Token);
            run = (await client.GetFromJsonAsync<ReportRunDto>($"{root}/runs/{run.Id}/status", deadline.Token))!;
        }
        run.Status.Should().Be("Ready");
        run.RowCount.Should().Be(10002);
        var details = new HashSet<string>();
        var totals = 0;
        for (var offset = 0; offset < run.RowCount; offset += 233)
        {
            var page = (await client.GetFromJsonAsync<ReportExecutionResponseDto>($"{root}/runs/{run.Id}?offset={offset}&limit=233", Json))!;
            foreach (var row in page.Sheet.Rows)
                if (row.RowKind == ReportRowKind.Total) totals++;
                else details.Add(row.Cells[0].Display!).Should().BeTrue();
        }
        details.Should().HaveCount(10001);
        totals.Should().Be(1);
        using var response = await client.PostAsJsonAsync($"{root}/runs/{run.Id}/export/xlsx", new { });
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        await using var zip = new ZipArchive(new MemoryStream(await response.Content.ReadAsByteArrayAsync()), ZipArchiveMode.Read);
        await using var xml = zip.GetEntry("xl/worksheets/sheet1.xml")!.Open();
        XDocument.Load(xml).Descendants(XName.Get("row", "http://schemas.openxmlformats.org/spreadsheetml/2006/main")).Count().Should().Be(10003);
    }

    [Fact]
    public async Task Run_status_pages_and_export_work_through_authenticated_API_and_hosted_worker()
    {
        await using var factory = new PmApiFactory(fixture);
        using var client = factory.CreateClient(new WebApplicationFactoryClientOptions { BaseAddress = new Uri("https://localhost") });
        const string root = "/api/reports/accounting.trial_balance";
        var definition = await client.GetFromJsonAsync<ReportDefinitionDto>("/api/report-definitions/accounting.trial_balance", Json);
        definition!.Capabilities!.SupportsSavedExecution.Should().BeTrue();
        using var anonymous = factory.CreateAnonymousClient();
        using var denied = await anonymous.GetAsync($"{root}/runs/{Guid.NewGuid()}/status");
        denied.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        var request = new ReportExecutionRequestDto(Parameters: new Dictionary<string, string>
            { ["from_utc"] = "2026-09-01", ["to_utc"] = "2026-09-12" });
        using var started = await client.PostAsJsonAsync(root + "/runs", request);
        started.StatusCode.Should().Be(HttpStatusCode.Accepted);
        started.Headers.Location.Should().NotBeNull();
        var run = (await started.Content.ReadFromJsonAsync<ReportRunDto>())!;
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        while (run.Status is "Queued" or "Running")
        {
            await Task.Delay(100, deadline.Token);
            run = (await client.GetFromJsonAsync<ReportRunDto>($"{root}/runs/{run.Id}/status", deadline.Token))!;
        }
        run.Status.Should().Be("Ready");
        var page = await client.GetFromJsonAsync<ReportExecutionResponseDto>($"{root}/runs/{run.Id}?offset=0&limit=100", Json);
        page!.Offset.Should().Be(0);
        page.Limit.Should().Be(100);
        page.HasMore.Should().BeFalse();
        page.Total.Should().Be(0);
        page.Sheet.Rows.Should().BeEmpty();
        var defaultPage = await client.GetFromJsonAsync<ReportExecutionResponseDto>($"{root}/runs/{run.Id}", Json);
        defaultPage!.Offset.Should().Be(0);
        defaultPage.Limit.Should().Be(200);
        using var exported = await client.PostAsJsonAsync($"{root}/runs/{run.Id}/export/xlsx", new { });
        exported.StatusCode.Should().Be(HttpStatusCode.OK);
        using var zip = new ZipArchive(new MemoryStream(await exported.Content.ReadAsByteArrayAsync()), ZipArchiveMode.Read);
        zip.GetEntry("xl/worksheets/sheet1.xml").Should().NotBeNull();
        using var unknown = await client.GetAsync($"{root}/runs/{Guid.NewGuid()}/status");
        unknown.StatusCode.Should().Be(HttpStatusCode.NotFound);
        using var invalid = await client.PostAsJsonAsync(root + "/runs", new ReportExecutionRequestDto());
        invalid.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }
}
