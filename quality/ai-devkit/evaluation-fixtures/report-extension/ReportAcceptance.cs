using System.Globalization;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using NGB.Application.Abstractions.Services;
using NGB.Contracts.Documents;
using NGB.Contracts.Metadata;
using NGB.Contracts.Reporting;
using NGB.Contracts.Services;
using NGB.Persistence.Reporting;
using Xunit;
using static EquipmentInspection.IntegrationTests.InspectionRequests;

namespace EquipmentInspection.IntegrationTests;

public sealed class ReportAcceptance
{
    private const string ReportCode = "example.inspection_by_category";
    private const string ExecutePath = "/api/reports/" + ReportCode + "/execute";

    [Fact]
    public async Task Category_report_pages_known_totals_and_filters_without_counting_drafts_or_uncategorized_equipment()
    {
        await using var host = new InspectionHost();
        await host.StartAsync();
        using var admin = await host.CreateClientAsync("administrator");
        var beta = await SeedAsync(admin);
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

        Assert.Equal([("Alpha", 2m), ("Beta", 3m), ("Gamma", 1m)], Groups(rows));
        Assert.Equal(6m, Total(rows));
        Assert.Null(page.NextCursor);
        var filtered = await PostAsync<ReportExecutionResponseDto>(admin, ExecutePath,
            Request() with { Filters = Filter("category_id", new[] { beta }) });
        Assert.Equal([("Beta", 3m)], Groups(filtered.Sheet.Rows));
        Assert.Equal(3m, Total(filtered.Sheet.Rows));

        foreach (var name in new[] { "Missing", "Alpha' OR TRUE --" })
        {
            var empty = await PostAsync<ReportExecutionResponseDto>(admin, ExecutePath,
                Request() with { Filters = Filter("category_name", name) });
            Assert.Empty(Groups(empty.Sheet.Rows));
            Assert.Equal(0m, Total(empty.Sheet.Rows));
            Assert.False(empty.HasMore);
        }
        var oldPeriod = await PostAsync<ReportExecutionResponseDto>(admin, ExecutePath,
            Request() with { Parameters = new Dictionary<string, string>
                { ["from_utc"] = "2000-01-01", ["to_utc"] = "2000-01-31" } });
        Assert.Empty(Groups(oldPeriod.Sheet.Rows));
        Assert.Equal(0m, Total(oldPeriod.Sheet.Rows));
    }

    [Fact]
    public async Task Category_report_rejects_unbounded_requests_and_changed_or_tampered_continuations()
    {
        await using var host = new InspectionHost();
        await host.StartAsync();
        using var admin = await host.CreateClientAsync("administrator");
        var beta = await SeedAsync(admin);
        var request = Request() with { Limit = 1 };
        var first = await PostAsync<ReportExecutionResponseDto>(admin, ExecutePath, request);
        Assert.True(first.HasMore);
        foreach (var invalid in new[]
        {
            request with { DisablePaging = true },
            request with { Offset = 1 },
            request with { Cursor = "invalid-cursor" },
            request with { Cursor = first.NextCursor, Filters = Filter("category_id", new[] { beta }) }
        })
        {
            using var response = await admin.PostAsJsonAsync(ExecutePath, invalid);
            Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        }
    }

    [Fact]
    public async Task Public_provider_returns_bounded_aggregated_category_rows_before_sheet_rendering()
    {
        await using var host = new InspectionHost();
        await host.StartAsync();
        using var admin = await host.CreateClientAsync("administrator");
        await SeedAsync(admin);
        await using var scope = host.Services.CreateAsyncScope();
        var source = scope.ServiceProvider.GetRequiredService<IReportPageDataSource>();
        var session = scope.ServiceProvider.GetRequiredService<IReportReadSession>();
        var query = new ReportDataQuery(
            ReportCode: ReportCode,
            DatasetCode: ReportCode,
            RowGroups: [new ReportPlanGrouping("category_name", "category_name", "Category", "string")],
            ColumnGroups: [],
            DetailFields: [],
            Measures: [new ReportPlanMeasure("inspection_count", "count", "Inspections", "int64", ReportAggregationKind.Count)],
            Sorts: [new ReportPlanSort("category_name", null, ReportSortDirection.Asc)],
            Predicates: [],
            Parameters: Request().Parameters!.Select(pair => new ReportPlanParameter(pair.Key, pair.Value)).ToArray());

        await session.BeginAsync(CancellationToken.None);
        try
        {
            string? cursor = null;
            foreach (var (name, count) in new[] { ("Alpha", 2L), ("Beta", 3L), ("Gamma", 1L) })
            {
                var page = await source.ReadPageAsync(query, new ReportPlanPaging(0, 1, cursor), null, CancellationToken.None);
                Assert.Equal("postgres-foundation", page.Diagnostics!["executor"]);
                var row = Assert.Single(page.Rows);
                Assert.Equal(name, row.Values["category_name"]);
                Assert.Equal(count, row.Values["count"]);
                Assert.Equal(name != "Gamma", page.HasMore);
                cursor = page.NextCursor;
                if (page.HasMore)
                    Assert.NotNull(cursor);
            }
            Assert.Null(cursor);
        }
        finally
        {
            await session.EndAsync(CancellationToken.None);
        }
    }

    [Fact]
    public async Task Report_definition_and_execution_obey_both_administrator_contracts_and_deny_other_actors()
    {
        await using var host = new InspectionHost();
        await host.StartAsync();
        await host.LinkUserAsync("application-administrator", true, InspectionHost.AdministratorRoleCode, "Renamed supervisors");
        await host.LinkUserAsync("inactive-administrator", false, "equipment.inactive-supervisors", "Inactive supervisors");
        await host.LinkUserAsync("imitator", true, "equipment.observer", "Administrator");
        var definitionPath = "/api/report-definitions/" + ReportCode;
        foreach (var username in new[] { "administrator", "application-administrator" })
        {
            using var admin = await host.CreateClientAsync(username);
            var definition = await admin.GetFromJsonAsync<ReportDefinitionDto>(definitionPath, Json);
            Assert.Equal(ReportExecutionMode.Composable, definition!.Mode);
            Assert.Equal(ReportCode, definition.Dataset!.DatasetCode);
            var empty = await PostAsync<ReportExecutionResponseDto>(admin, ExecutePath, Request());
            Assert.Empty(Groups(empty.Sheet.Rows));
            Assert.Equal(0m, Total(empty.Sheet.Rows));
        }
        foreach (var username in new[] { "reader", "inactive-administrator", "imitator" })
        {
            using var limited = await host.CreateClientAsync(username);
            using var definition = await limited.GetAsync(definitionPath);
            using var execution = await limited.PostAsJsonAsync(ExecutePath, Request());
            Assert.Equal(HttpStatusCode.Forbidden, definition.StatusCode);
            Assert.Equal(HttpStatusCode.Forbidden, execution.StatusCode);
        }
        using var anonymous = host.CreateAnonymousClient();
        using var unauthenticated = await anonymous.PostAsJsonAsync(ExecutePath, Request());
        Assert.Equal(HttpStatusCode.Unauthorized, unauthenticated.StatusCode);
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

    private static (string Category, decimal Count)[] Groups(IEnumerable<ReportSheetRowDto> rows)
        => rows.Where(row => row.RowKind == ReportRowKind.Group)
            .Select(row => (row.Cells[0].Display!, row.Cells[^1].Value!.Value.GetDecimal())).ToArray();

    private static decimal Total(IEnumerable<ReportSheetRowDto> rows)
        => rows.Single(row => row.RowKind == ReportRowKind.Total).Cells[^1].Value!.Value.GetDecimal();

    private static async Task<Guid> SeedAsync(HttpClient admin)
    {
        var categories = new Dictionary<string, Guid>();
        foreach (var name in new[] { "Alpha", "Beta", "Gamma", "Unused" })
        {
            var category = await PostAsync<CatalogItemDto>(admin, "/api/catalogs/example.equipment_category",
                Payload(("code", name), ("name", name)));
            categories.Add(name, category.Id);
        }
        foreach (var (name, count) in new[] { ("Alpha", 1), ("Alpha", 1), ("Beta", 3), ("Gamma", 1) })
        {
            var equipment = await PostAsync<CatalogItemDto>(admin, "/api/catalogs/example.equipment",
                Payload(("display", name + " instrument " + Guid.NewGuid().ToString("N")), ("category_id", categories[name])));
            for (var index = 0; index < count; index++)
                await CreateInspectionAsync(admin, equipment.Id, post: true);
            await CreateInspectionAsync(admin, equipment.Id, post: false);
        }
        var uncategorized = await PostAsync<CatalogItemDto>(admin, "/api/catalogs/example.equipment", Payload(("display", "Uncategorized")));
        await CreateInspectionAsync(admin, uncategorized.Id, post: true);
        return categories["Beta"];
    }

    private static async Task CreateInspectionAsync(HttpClient admin, Guid equipment, bool post)
    {
        var document = await PostAsync<DocumentDto>(admin, "/api/documents/example.inspection",
            Payload(("equipment_id", equipment), ("result", "Passed")));
        if (post)
        {
            var path = "/api/documents/example.inspection/" + document.Id;
            var state = await admin.GetFromJsonAsync<DocumentEditorStateDto>(path + "/editor-state", Json);
            using var posted = await ExecutePostAsync(admin, path, state!.DocumentVersion, document.Id.ToString());
            await EnsureSuccessAsync(posted);
        }
    }
}
