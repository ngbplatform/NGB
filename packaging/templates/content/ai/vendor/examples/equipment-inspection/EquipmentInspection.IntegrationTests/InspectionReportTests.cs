using System.Diagnostics;
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
using NGB.Persistence.Documents;
using NGB.Persistence.Reporting;
using NGB.Runtime.Documents;
using Xunit;
using Xunit.Abstractions;
using static EquipmentInspection.IntegrationTests.InspectionRequests;

namespace EquipmentInspection.IntegrationTests;

public sealed class InspectionReportTests(ITestOutputHelper output)
{
    private const string ExecutePath = "/api/reports/" + InspectionReports.Summary + "/execute";

    [Fact]
    public async Task Dashboard_uses_the_authorized_report_and_its_UTC_cutoff()
    {
        await using var host = new InspectionHost();
        await host.StartAsync();
        using var admin = await host.CreateClientAsync("administrator");
        using var reader = await host.CreateClientAsync("reader");
        const string path = "/api/inspection-dashboard";
        using var denied = await reader.GetAsync(path);
        Assert.Equal(HttpStatusCode.Forbidden, denied.StatusCode);

        await SeedAsync(admin);
        var snapshot = (await admin.GetFromJsonAsync<EquipmentInspection.Api.InspectionDashboardController.Snapshot>(path, Json))!;
        Assert.Equal(5, snapshot.Total);
        Assert.Equal(1, snapshot.FollowUp);
        Assert.Equal(1, snapshot.Rejected);
        Assert.Equal(3, snapshot.Passed);
        var report = await PostAsync<ReportExecutionResponseDto>(admin, ExecutePath,
            new ReportExecutionRequestDto(Parameters: snapshot.Parameters));
        Assert.Equal(snapshot.Total, Total(report.Sheet.Rows));
        var previous = snapshot.AsOf.AddDays(-1).ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
        var empty = (await admin.GetFromJsonAsync<EquipmentInspection.Api.InspectionDashboardController.Snapshot>(
            path + "?asOf=" + previous, Json))!;
        Assert.Equal(previous, empty.Parameters["to_utc"]);
        Assert.Equal(0, empty.Total);
        Assert.Equal(0, empty.FollowUp);
    }

    [Fact]
    public async Task Posting_a_later_inspection_preserves_the_count_at_the_earlier_cutoff()
    {
        await using var host = new InspectionHost();
        await host.StartAsync();
        using var admin = await host.CreateClientAsync("administrator");
        var equipment = await PostAsync<CatalogItemDto>(admin, "/api/catalogs/example.equipment",
            Payload(("display", "Historical count bench")));
        var draft = await PostAsync<DocumentDto>(admin, "/api/documents/example.inspection",
            CompleteInspectionPayload(equipment.Id));
        await using var scope = host.Services.CreateAsyncScope();
        var boundary = new DateTime(2026, 5, 2, 0, 0, 0, DateTimeKind.Utc);
        var drafts = scope.ServiceProvider.GetRequiredService<IDocumentDraftService>();
        await drafts.UpdateDraftAsync(draft.Id, number: null, dateUtc: boundary);
        var document = await scope.ServiceProvider.GetRequiredService<IDocumentRepository>().GetAsync(draft.Id);
        Assert.NotNull(document);
        var day = DateOnly.FromDateTime(document.DateUtc);
        var cutoff = day.AddDays(-1).ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
        var laterCutoff = day.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
        var historical = new ReportExecutionRequestDto(
            Parameters: new Dictionary<string, string> { ["from_utc"] = cutoff, ["to_utc"] = cutoff },
            Filters: Filter("equipment_id", new[] { equipment.Id }));
        var documents = "/api/documents/example.inspection";
        var midday = await PostAsync<DocumentDto>(admin, documents, CompleteInspectionPayload(equipment.Id));
        await drafts.UpdateDraftAsync(midday.Id, number: null, dateUtc: boundary.AddHours(-12));
        var middayPath = documents + "/" + midday.Id;
        var middayState = await admin.GetFromJsonAsync<DocumentEditorStateDto>(middayPath + "/editor-state", Json);
        using var middayPost = await ExecutePostAsync(admin, middayPath, middayState!.DocumentVersion, midday.Id.ToString());
        await ReadActionAsync(middayPost);

        var before = Total((await PostAsync<ReportExecutionResponseDto>(admin, ExecutePath, historical)).Sheet.Rows);
        Assert.Equal(1m, before);

        var path = "/api/documents/example.inspection/" + draft.Id;
        var state = await admin.GetFromJsonAsync<DocumentEditorStateDto>(path + "/editor-state", Json);
        using var post = await ExecutePostAsync(admin, path, state!.DocumentVersion, draft.Id.ToString());
        var posted = await ReadActionAsync(post);
        var after = Total((await PostAsync<ReportExecutionResponseDto>(admin, ExecutePath, historical)).Sheet.Rows);
        var later = historical with { Parameters = new Dictionary<string, string> { ["from_utc"] = cutoff, ["to_utc"] = laterCutoff } };
        var laterActual = Total((await PostAsync<ReportExecutionResponseDto>(admin, ExecutePath, later)).Sheet.Rows);
        Assert.Equal(1m, after);
        Assert.Equal(2m, laterActual);

        output.WriteLine("NGB-CHECK " + JsonSerializer.Serialize(new
        {
            kind = "historical-cutoff",
            target = "report:" + InspectionReports.Summary,
            key = "inspection_count",
            cutoff = historical.Parameters!["to_utc"],
            repeatedCutoff = historical.Parameters!["to_utc"],
            laterCutoff = later.Parameters!["to_utc"],
            timeZone = "UTC",
            cutoffExclusive = boundary,
            laterEvent = posted.Document.Id,
            laterEffectiveAt = document.DateUtc,
            laterStatus = (int)post.StatusCode,
            expected = 1m,
            before,
            after,
            laterExpected = 2m,
            laterActual
        }));
    }

    [Fact]
    public async Task Grand_total_includes_more_groups_than_the_maximum_page_and_is_emitted_only_on_the_final_page()
    {
        await using var host = new InspectionHost();
        await host.StartAsync();
        using var admin = await host.CreateClientAsync("administrator");
        var equipment = await PostAsync<CatalogItemDto>(admin, "/api/catalogs/example.equipment",
            Payload(("display", "Complete aggregate bench")));
        const int count = 501;
        for (var index = 0; index < count; index++)
        {
            var document = await PostAsync<DocumentDto>(admin, "/api/documents/example.inspection",
                CompleteInspectionPayload(equipment.Id, $"Result {index:D4}"));
            var path = "/api/documents/example.inspection/" + document.Id;
            var state = (await admin.GetFromJsonAsync<DocumentEditorStateDto>(path + "/editor-state", Json))!;
            using var posted = await ExecutePostAsync(admin, path, state.DocumentVersion, document.Id.ToString());
            await EnsureSuccessAsync(posted);
        }

        var request = Request() with { Limit = 500 };
        var first = await PostAsync<ReportExecutionResponseDto>(admin, ExecutePath, request);
        Assert.True(first.HasMore);
        Assert.Equal(500, Groups(first.Sheet.Rows).Length);
        Assert.DoesNotContain(first.Sheet.Rows, row => row.RowKind == ReportRowKind.Total);
        var last = await PostAsync<ReportExecutionResponseDto>(admin, ExecutePath,
            request with { Cursor = first.NextCursor });
        Assert.False(last.HasMore);
        Assert.Single(Groups(last.Sheet.Rows));
        Assert.Equal(count, Total(last.Sheet.Rows));
        Assert.Equal(count, Groups(first.Sheet.Rows).Concat(Groups(last.Sheet.Rows)).Sum(row => row.Count));

        var samples = new List<object>();
        for (var index = 0; index < 6; index++)
        {
            using var trace = new Activity("paged-summary").SetIdFormat(ActivityIdFormat.W3C).Start();
            var queries = 0;
            using var listener = new ActivityListener
            {
                ShouldListenTo = source => source.Name.StartsWith("Npgsql", StringComparison.Ordinal)
                    || source.Name is "System.Net.Http" or "Microsoft.AspNetCore",
                Sample = static (ref _) => ActivitySamplingResult.AllData,
                SampleUsingParentId = static (ref _) => ActivitySamplingResult.AllData,
                ActivityStopped = activity =>
                {
                    if (activity.TraceId == trace.TraceId
                        && (activity.GetTagItem("db.query.text") is not null || activity.GetTagItem("db.statement") is not null))
                        Interlocked.Increment(ref queries);
                }
            };
            ActivitySource.AddActivityListener(listener);
            var timer = Stopwatch.StartNew();
            using var response = await admin.PostAsJsonAsync(ExecutePath, request);
            timer.Stop();
            await EnsureSuccessAsync(response);
            var page = await response.Content.ReadFromJsonAsync<ReportExecutionResponseDto>(Json);
            Assert.Equal(500, Groups(page!.Sheet.Rows).Length);
            Assert.InRange(queries, 1, 24);
            var workingSetBytes = Environment.WorkingSet;
            Assert.True(workingSetBytes > 0);
            samples.Add(new
            {
                status = (int)response.StatusCode,
                milliseconds = timer.Elapsed.TotalMilliseconds,
                queries,
                workingSetBytes
            });
        }

        output.WriteLine("NGB-CHECK " + JsonSerializer.Serialize(new
        {
            kind = "capacity",
            target = "report:" + InspectionReports.Summary,
            key = "summary-page",
            populations = new { inspections = Total(last.Sheet.Rows) },
            samples
        }));
    }

    [Fact]
    public async Task Time_groups_and_source_document_actions_execute_through_the_report_endpoint()
    {
        await using var host = new InspectionHost();
        await host.StartAsync();
        using var admin = await host.CreateClientAsync("administrator");
        await SeedAsync(admin);
        var grouped = await PostAsync<ReportExecutionResponseDto>(admin, ExecutePath, Request() with
        {
            Layout = new ReportLayoutDto(
                RowGroups: [new ReportGroupingDto("inspection_date", ReportTimeGrain.Month)],
                Measures: [new ReportMeasureSelectionDto("inspection_count", ReportAggregationKind.Count)],
                ShowSubtotals: false)
        });
        Assert.Equal(5m, Total(grouped.Sheet.Rows));

        var details = await PostAsync<ReportExecutionResponseDto>(admin, ExecutePath, Request() with
        {
            Layout = new ReportLayoutDto(
                DetailFields: ["inspection_display", "inspection_date"],
                ShowDetails: true,
                ShowSubtotals: false,
                ShowGrandTotals: false)
        });
        Assert.DoesNotContain(details.Sheet.Rows.SelectMany(row => row.Cells),
            cell => Guid.TryParse(cell.Display, out _));
        Assert.DoesNotContain(details.Sheet.Columns, column => column.Code == "inspection_id");
        var rawId = Request() with
        {
            Layout = new ReportLayoutDto(DetailFields: ["inspection_id"], ShowDetails: true)
        };
        using var rejected = await admin.PostAsJsonAsync(ExecutePath, rawId);
        Assert.InRange((int)rejected.StatusCode, 400, 499);
        var actions = details.Sheet.Rows.SelectMany(row => row.Cells)
            .Where(cell => cell.Action?.DocumentId is not null)
            .Select(cell => cell.Action!)
            .ToArray();
        Assert.Equal(5, actions.Length);
        Assert.Equal(5, actions.Select(action => action.DocumentId).Distinct().Count());
        using var export = await admin.PostAsJsonAsync("/api/reports/" + InspectionReports.Summary + "/export/xlsx",
            new ReportExportRequestDto(Layout: new ReportLayoutDto(DetailFields: ["inspection_display", "inspection_date"],
                ShowDetails: true, ShowSubtotals: false, ShowGrandTotals: false), Parameters: Request().Parameters));
        await EnsureSuccessAsync(export);
        var exported = (await ReadWorksheetAsync(export)).SelectMany(row => row).ToArray();
        Assert.DoesNotContain(exported, text => Guid.TryParse(text, out _));
        foreach (var cell in details.Sheet.Rows.SelectMany(row => row.Cells).Where(cell => cell.Action?.DocumentId is not null))
        {
            var action = cell.Action!;
            Assert.Equal(EquipmentDefinitions.InspectionCode, action.DocumentType);
            var source = await admin.GetFromJsonAsync<DocumentDto>(
                $"/api/documents/{action.DocumentType}/{action.DocumentId}", Json);
            Assert.Equal(action.DocumentId, source!.Id);
            var display = cell.Display ?? cell.Value?.GetString();
            Assert.False(string.IsNullOrWhiteSpace(display));
            Assert.StartsWith("Inspection " + source.Number + " · ", display);
            Assert.Contains(display, exported);
        }
    }

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
        var rows = await ReadWorksheetAsync(response);
        Assert.Contains(rows, row => row.SequenceEqual(["Follow-up", "1"]));
        Assert.Contains(rows, row => row.SequenceEqual(["Passed", "2"]));
        Assert.Contains(rows, row => row.Length == 2
            && row[0].Contains("Total", StringComparison.OrdinalIgnoreCase) && row[1] == "3");
        Assert.DoesNotContain(rows, row => row.Contains("Rejected"));
        var screen = await PostAsync<ReportExecutionResponseDto>(admin, ExecutePath, request);
        var totalRow = Assert.Single(rows, row => row.Length == 2 && row[0].Contains("Total", StringComparison.OrdinalIgnoreCase));
        output.WriteLine("NGB-CHECK " + JsonSerializer.Serialize(new
        {
            kind = "report-export",
            target = "report:" + InspectionReports.Summary,
            key = "filtered-count",
            downloadStatus = (int)response.StatusCode,
            contentType = response.Content.Headers.ContentType.MediaType,
            expectedCells = new[] { 3m },
            screenCells = new[] { Total(screen.Sheet.Rows) },
            worksheetCells = new[] { decimal.Parse(totalRow[1], CultureInfo.InvariantCulture) }
        }));

        var unused = await PostAsync<CatalogItemDto>(admin, "/api/catalogs/example.equipment", Payload(("display", "No inspections")));
        var empty = request with { Filters = Filter("equipment_id", new[] { unused.Id }) };
        var emptyScreen = await PostAsync<ReportExecutionResponseDto>(admin, ExecutePath, empty);
        Assert.Empty(Groups(emptyScreen.Sheet.Rows));
        using var emptyDownload = await admin.PostAsJsonAsync(path, export with { Filters = empty.Filters });
        await EnsureSuccessAsync(emptyDownload);
        var emptyRows = await ReadWorksheetAsync(emptyDownload);
        Assert.DoesNotContain(emptyRows, row => row.Any(cell => cell is "Follow-up" or "Passed" or "Rejected"));
        foreach (var row in emptyRows.Where(row => row.Length == 2 && row[0].Contains("Total", StringComparison.OrdinalIgnoreCase)))
            Assert.Equal(0m, decimal.Parse(row[1], CultureInfo.InvariantCulture));
    }

    private static async Task<string[][]> ReadWorksheetAsync(HttpResponseMessage response)
    {
        using var bytes = new MemoryStream(await response.Content.ReadAsByteArrayAsync());
        using var archive = new ZipArchive(bytes);
        using var sheet = archive.GetEntry("xl/worksheets/sheet1.xml")!.Open();
        var xml = XDocument.Load(sheet);
        XNamespace ns = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";
        return xml.Descendants(ns + "row").Select(row => row.Elements(ns + "c")
            .Select(cell => cell.Attribute("t")?.Value == "inlineStr"
                ? string.Concat(cell.Descendants(ns + "t").Select(text => text.Value))
                : cell.Element(ns + "v")?.Value ?? string.Empty)
            .ToArray()).ToArray();
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
