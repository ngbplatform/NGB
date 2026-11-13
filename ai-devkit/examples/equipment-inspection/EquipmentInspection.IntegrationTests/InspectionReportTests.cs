using System.Globalization;
using System.IO.Compression;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Xml.Linq;
using EquipmentInspection.Definitions;
using Microsoft.Extensions.DependencyInjection;
using NGB.Application.Abstractions.Services;
using NGB.Contracts.Documents;
using NGB.Contracts.Reporting;
using NGB.Contracts.Services;
using NGB.Persistence.Reporting;
using Xunit;
using static EquipmentInspection.IntegrationTests.InspectionRequests;

namespace EquipmentInspection.IntegrationTests;

public sealed class InspectionReportTests
{
    private const string ExecutePath = "/api/reports/" + InspectionReports.Summary + "/execute";

    [Fact]
    public async Task Posted_results_are_aggregated_in_postgres_and_paged_without_duplicates()
    {
        await using var host = new InspectionHost();
        await host.StartAsync();
        using var admin = await host.CreateClientAsync("administrator");
        var firstEquipment = await SeedAsync(admin);
        var request = Request() with { Limit = 1 };
        var rows = new List<ReportSheetRowDto>();
        var cursors = new HashSet<string>();
        ReportExecutionResponseDto page;

        do
        {
            page = await PostAsync<ReportExecutionResponseDto>(admin, ExecutePath, request);
            Assert.Equal("query", page.Diagnostics!["paging"]);
            Assert.Single(page.Sheet.Rows, row => row.RowKind == ReportRowKind.Group);
            rows.AddRange(page.Sheet.Rows);
            if (page.HasMore)
            {
                Assert.DoesNotContain(page.Sheet.Rows, row => row.RowKind == ReportRowKind.Total);
                Assert.True(cursors.Add(page.NextCursor!));
                Assert.InRange(cursors.Count, 1, 2);
            }
            request = request with { Cursor = page.NextCursor };
        } while (page.HasMore);

        Assert.Equal([("Follow-up", 1m), ("Passed", 3m), ("Rejected", 1m)], Groups(rows));
        Assert.Equal(5m, Total(rows));
        Assert.Null(page.NextCursor);

        var filtered = await PostAsync<ReportExecutionResponseDto>(admin, ExecutePath,
            Request() with { Filters = Filter("equipment_id", new[] { firstEquipment }) });
        Assert.Equal([("Follow-up", 1m), ("Passed", 2m)], Groups(filtered.Sheet.Rows));
        Assert.Equal(3m, Total(filtered.Sheet.Rows));

        foreach (var result in new[] { "Missing result", "Passed' OR TRUE --" })
        {
            var empty = await PostAsync<ReportExecutionResponseDto>(admin, ExecutePath,
                Request() with { Filters = Filter("result", result) });
            Assert.Empty(Groups(empty.Sheet.Rows));
            Assert.Equal(0m, Total(empty.Sheet.Rows));
            Assert.False(empty.HasMore);
        }

        var oldPeriod = await PostAsync<ReportExecutionResponseDto>(admin, ExecutePath,
            Request() with
            {
                Parameters = new Dictionary<string, string>
                {
                    ["from_utc"] = "2000-01-01",
                    ["to_utc"] = "2000-01-31"
                }
            });
        Assert.Empty(Groups(oldPeriod.Sheet.Rows));
        Assert.Equal(0m, Total(oldPeriod.Sheet.Rows));
    }

    [Fact]
    public async Task Report_rejects_unbounded_requests_and_changed_or_tampered_continuations()
    {
        await using var host = new InspectionHost();
        await host.StartAsync();
        using var admin = await host.CreateClientAsync("administrator");
        await SeedAsync(admin);
        var request = Request() with { Limit = 1 };
        var first = await PostAsync<ReportExecutionResponseDto>(admin, ExecutePath, request);
        Assert.True(first.HasMore);

        foreach (var invalid in new[]
        {
            request with { DisablePaging = true },
            request with { Offset = 1 },
            request with { Cursor = "invalid-cursor" },
            request with { Cursor = first.NextCursor, Filters = Filter("result", "Passed") }
        })
        {
            using var response = await admin.PostAsJsonAsync(ExecutePath, invalid);
            Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        }
    }

    [Theory]
    [InlineData("from_utc", "invalid")]
    [InlineData("to_utc", "2026-02-30")]
    [InlineData("to_utc", "9999-12-31")]
    public async Task Malformed_composable_dates_return_field_validation_errors(string parameter, string value)
    {
        await using var host = new InspectionHost();
        await host.StartAsync();
        using var admin = await host.CreateClientAsync("administrator");
        var request = Request();
        var parameters = new Dictionary<string, string>(request.Parameters!) { [parameter] = value };

        using var response = await admin.PostAsJsonAsync(ExecutePath, request with { Parameters = parameters });
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        using var problem = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var error = problem.RootElement.GetProperty("error");
        Assert.Equal("report.layout.invalid", error.GetProperty("code").GetString());
        Assert.Equal("Validation", error.GetProperty("kind").GetString());
        Assert.Equal("parameters." + parameter, error.GetProperty("context").GetProperty("fieldPath").GetString());
    }

    [Fact]
    public async Task Provider_applies_selected_keys_as_a_bounded_set_with_consistent_counts()
    {
        await using var host = new InspectionHost();
        await host.StartAsync();
        using var admin = await host.CreateClientAsync("administrator");
        await SeedAsync(admin);
        await using var scope = host.Services.CreateAsyncScope();
        var source = scope.ServiceProvider.GetRequiredService<IReportPageDataSource>();
        var session = scope.ServiceProvider.GetRequiredService<IReportReadSession>();
        var query = new ReportDataQuery(
            ReportCode: InspectionReports.Summary,
            DatasetCode: InspectionReports.Summary,
            RowGroups: [new ReportPlanGrouping("result", "result", "Result", "string")],
            ColumnGroups: [],
            DetailFields: [],
            Measures: [new ReportPlanMeasure("inspection_count", "count", "Inspections", "int64", ReportAggregationKind.Count)],
            Sorts: [new ReportPlanSort("result", null, ReportSortDirection.Asc)],
            Predicates: [],
            Parameters: Request().Parameters!.Select(pair => new ReportPlanParameter(pair.Key, pair.Value)).ToArray());
        var selection = new ReportRowSelection([new ReportSelectionField("result")],
            [[JsonSerializer.SerializeToElement("Passed")], [JsonSerializer.SerializeToElement("Rejected")]]);

        await session.BeginAsync(CancellationToken.None);
        try
        {
            var first = await source.ReadPageAsync(query, new ReportPlanPaging(0, 1), selection, CancellationToken.None);
            Assert.Equal("postgres-foundation", first.Diagnostics!["executor"]);
            var row = Assert.Single(first.Rows);
            Assert.Equal("Passed", row.Values["result"]);
            Assert.Equal(3L, row.Values["count"]);
            Assert.True(first.HasMore);

            var last = await source.ReadPageAsync(query, new ReportPlanPaging(0, 1, first.NextCursor), selection, CancellationToken.None);
            row = Assert.Single(last.Rows);
            Assert.Equal("Rejected", row.Values["result"]);
            Assert.Equal(1L, row.Values["count"]);
            Assert.False(last.HasMore);
            Assert.Null(last.NextCursor);

            var empty = await source.ReadPageAsync(query, new ReportPlanPaging(0, 1),
                selection with { Keys = [] }, CancellationToken.None);
            Assert.Empty(empty.Rows);
            Assert.False(empty.HasMore);
        }
        finally
        {
            await session.EndAsync(CancellationToken.None);
        }
    }

    [Fact]
    public async Task Report_uses_registered_permissions_for_both_admin_kinds_and_denies_other_actors()
    {
        await using var host = new InspectionHost();
        await host.StartAsync();
        await host.LinkUserAsync("application-administrator", true, InspectionHost.AdministratorRoleCode, "Renamed report administrators");
        await host.LinkUserAsync("imitator", true, "example.report-observer", "Administrator");
        using var admin = await host.CreateClientAsync("administrator");
        using var applicationAdmin = await host.CreateClientAsync("application-administrator");
        using var imitator = await host.CreateClientAsync("imitator");
        using var reader = await host.CreateClientAsync("reader");
        using var anonymous = host.CreateAnonymousClient();
        var definitionPath = "/api/report-definitions/" + InspectionReports.Summary;
        var definition = await admin.GetFromJsonAsync<ReportDefinitionDto>(definitionPath, Json);
        Assert.Equal(ReportExecutionMode.Composable, definition!.Mode);
        Assert.Equal(50, definition.Presentation!.InitialPageSize);
        var catalog = host.Services.GetRequiredService<IReportDatasetCatalog>();
        var dataset = await catalog.GetDatasetAsync(InspectionReports.Summary, CancellationToken.None);
        Assert.Equal(JsonSerializer.Serialize(definition.Dataset), JsonSerializer.Serialize(dataset));
        var definitions = await admin.GetFromJsonAsync<ReportDefinitionDto[]>("/api/report-definitions", Json);
        Assert.Contains(definitions!, item => item.ReportCode == InspectionReports.Summary);

        using var forbiddenDefinition = await reader.GetAsync(definitionPath);
        using var forbiddenExecution = await reader.PostAsJsonAsync(ExecutePath, Request());
        using var displayNameOnly = await imitator.PostAsJsonAsync(ExecutePath, Request());
        using var unauthorized = await anonymous.PostAsJsonAsync(ExecutePath, Request());
        Assert.Equal(HttpStatusCode.Forbidden, forbiddenDefinition.StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, forbiddenExecution.StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, displayNameOnly.StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, unauthorized.StatusCode);

        var linkedAdmin = await PostAsync<ReportExecutionResponseDto>(applicationAdmin, ExecutePath, Request());
        Assert.Empty(Groups(linkedAdmin.Sheet.Rows));
        await host.DeactivateUserAsync("application-administrator");
        using var inactive = await applicationAdmin.PostAsJsonAsync(ExecutePath, Request());
        Assert.Equal(HttpStatusCode.Forbidden, inactive.StatusCode);
    }

    [Fact]
    public async Task Xlsx_download_contains_the_same_filtered_aggregate_and_requires_export_permission()
    {
        await using var host = new InspectionHost();
        await host.StartAsync();
        using var admin = await host.CreateClientAsync("administrator");
        using var reader = await host.CreateClientAsync("reader");
        var equipment = await SeedAsync(admin);
        var request = Request() with { Filters = Filter("equipment_id", new[] { equipment }) };
        var export = new ReportExportRequestDto(Parameters: request.Parameters, Filters: request.Filters);
        var path = "/api/reports/" + InspectionReports.Summary + "/export/xlsx";
        using var forbidden = await reader.PostAsJsonAsync(path, export);
        Assert.Equal(HttpStatusCode.Forbidden, forbidden.StatusCode);

        using var response = await admin.PostAsJsonAsync(path, export);
        await EnsureSuccessAsync(response);
        Assert.Equal("application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
            response.Content.Headers.ContentType!.MediaType);
        using var bytes = new MemoryStream(await response.Content.ReadAsByteArrayAsync());
        using var archive = new ZipArchive(bytes);
        using var sheet = archive.GetEntry("xl/worksheets/sheet1.xml")!.Open();
        var xml = XDocument.Load(sheet);
        XNamespace ns = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";
        var rows = xml.Descendants(ns + "row").Select(row => row.Elements(ns + "c")
            .Select(cell => cell.Attribute("t")?.Value == "inlineStr"
                ? string.Concat(cell.Descendants(ns + "t").Select(text => text.Value))
                : cell.Element(ns + "v")?.Value ?? string.Empty)
            .ToArray()).ToArray();
        Assert.Contains(rows, row => row.SequenceEqual(["Follow-up", "1"]));
        Assert.Contains(rows, row => row.SequenceEqual(["Passed", "2"]));
        Assert.Contains(rows, row => row.Length == 2
            && row[0].Contains("Total", StringComparison.OrdinalIgnoreCase) && row[1] == "3");
        Assert.DoesNotContain(rows, row => row.Contains("Rejected"));
    }

    private static ReportExecutionRequestDto Request()
    {
        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        return new ReportExecutionRequestDto(Parameters: new Dictionary<string, string>
        {
            ["from_utc"] = today.AddDays(-1).ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
            ["to_utc"] = today.AddDays(1).ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)
        });
    }

    private static Dictionary<string, ReportFilterValueDto> Filter(string field, object value)
        => new() { [field] = new ReportFilterValueDto(JsonSerializer.SerializeToElement(value)) };

    private static (string Result, decimal Count)[] Groups(IEnumerable<ReportSheetRowDto> rows)
        => rows.Where(row => row.RowKind == ReportRowKind.Group)
            .Select(row => (row.Cells[0].Display!, row.Cells[^1].Value!.Value.GetDecimal())).ToArray();

    private static decimal Total(IEnumerable<ReportSheetRowDto> rows)
        => rows.Single(row => row.RowKind == ReportRowKind.Total).Cells[^1].Value!.Value.GetDecimal();

    private static async Task<Guid> SeedAsync(HttpClient admin)
    {
        var first = await PostAsync<CatalogItemDto>(admin, "/api/catalogs/example.equipment", Payload(("display", "Bench A")));
        var second = await PostAsync<CatalogItemDto>(admin, "/api/catalogs/example.equipment", Payload(("display", "Bench B")));
        foreach (var (equipment, result) in new[]
        {
            (first.Id, "Passed"), (first.Id, "Passed"), (first.Id, "Follow-up"),
            (second.Id, "Passed"), (second.Id, "Rejected")
        })
        {
            var document = await PostAsync<DocumentDto>(admin, "/api/documents/example.inspection",
                CompleteInspectionPayload(equipment, result));
            var path = "/api/documents/example.inspection/" + document.Id;
            var state = await admin.GetFromJsonAsync<DocumentEditorStateDto>(path + "/editor-state", Json);
            using var posted = await ExecutePostAsync(admin, path, state!.DocumentVersion, document.Id.ToString());
            await EnsureSuccessAsync(posted);
        }

        await PostAsync<DocumentDto>(admin, "/api/documents/example.inspection",
            Payload(("equipment_id", first.Id), ("result", "Draft is not a completed inspection")));
        return first.Id;
    }
}
