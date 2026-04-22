using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
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
public sealed class PmReporting_Endpoints_P0Tests : IAsyncLifetime
{
    private readonly PmIntegrationFixture _fixture;
    private static readonly JsonSerializerOptions Json = CreateJson();

    public PmReporting_Endpoints_P0Tests(PmIntegrationFixture fixture) => _fixture = fixture;

    public async Task InitializeAsync() => await _fixture.ResetDatabaseAsync();
    public Task DisposeAsync() => Task.CompletedTask;

    [Fact]
    public async Task Definition_Discovery_And_Execute_Work_EndToEnd()
    {
        using var factory = new PmApiFactory(_fixture);
        using var client = factory.CreateClient(new WebApplicationFactoryClientOptions { BaseAddress = new Uri("https://localhost") });

        using (var resp = await client.GetAsync("/api/report-definitions"))
        {
            resp.StatusCode.Should().Be(HttpStatusCode.OK);
            var defs = await resp.Content.ReadFromJsonAsync<IReadOnlyList<ReportDefinitionDto>>(Json);
            defs.Should().NotBeNull();
            defs!.Select(x => x.ReportCode).Should().Contain("accounting.ledger.analysis");
        }

        using (var resp = await client.GetAsync("/api/report-definitions/accounting.ledger.analysis"))
        {
            resp.StatusCode.Should().Be(HttpStatusCode.OK);
            var def = await resp.Content.ReadFromJsonAsync<ReportDefinitionDto>(Json);
            def.Should().NotBeNull();
            def!.Mode.Should().Be(ReportExecutionMode.Composable);
            def.Dataset!.DatasetCode.Should().Be("pm.accounting.ledger.analysis");
            def.DefaultLayout!.RowGroups.Should().HaveCount(2);
        }

        using (var resp = await client.GetAsync("/api/report-definitions/accounting.posting_log"))
        {
            resp.StatusCode.Should().Be(HttpStatusCode.OK);
            var def = await resp.Content.ReadFromJsonAsync<ReportDefinitionDto>(Json);
            def.Should().NotBeNull();
            def!.Description.Should().Be("Posting engine activity log for diagnostics and support");
            def.Capabilities!.AllowsGrandTotals.Should().BeFalse();
            def.DefaultLayout!.ShowGrandTotals.Should().BeFalse();
            var filters = def.Filters!;
            filters.Select(x => x.FieldCode).Should().Equal("operation", "status");
            def.Presentation.Should().BeEquivalentTo(new ReportPresentationDto(
                InitialPageSize: 100,
                RowNoun: "posting operation",
                EmptyStateMessage: "Adjust the time window or filters and run again."));
            filters.Single(x => x.FieldCode == "operation").Options!.Select(x => new { x.Value, x.Label }).Should().Equal(
                new { Value = "Post", Label = "Post" },
                new { Value = "Unpost", Label = "Unpost" },
                new { Value = "Repost", Label = "Repost" },
                new { Value = "CloseFiscalYear", Label = "Close fiscal year" });
            filters.Single(x => x.FieldCode == "status").Options!.Select(x => new { x.Value, x.Label }).Should().Equal(
                new { Value = "InProgress", Label = "In progress" },
                new { Value = "Completed", Label = "Completed" },
                new { Value = "StaleInProgress", Label = "Stale in progress" });
        }

        using (var resp = await client.PostAsJsonAsync(
                   "/api/reports/accounting.ledger.analysis/execute",
                   new ReportExecutionRequestDto(
                       Parameters: new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
                       {
                           ["from_utc"] = "2026-02-01",
                           ["to_utc"] = "2026-04-30"
                       },
                       Layout: new ReportLayoutDto(
                           RowGroups: [new ReportGroupingDto("account_display")],
                           Measures: [new ReportMeasureSelectionDto("debit_amount", ReportAggregationKind.Sum)],
                           Sorts: [new ReportSortDto("account_display")],
                           ShowDetails: false,
                           ShowSubtotals: true,
                           ShowGrandTotals: true),
                       Offset: 0,
                       Limit: 50)))
        {
            resp.StatusCode.Should().Be(HttpStatusCode.OK);
            var dto = await resp.Content.ReadFromJsonAsync<ReportExecutionResponseDto>(Json);
            dto.Should().NotBeNull();
            dto!.Diagnostics.Should().NotBeNull();
            dto.Diagnostics!["engine"].Should().Be("runtime");
            dto.Sheet.Columns.Select(x => x.Code).Should().Contain(new[] { "__row_hierarchy", "debit_amount__sum" });
        }
    }

    [Fact]
    public async Task Unknown_Report_Code_Returns_404_For_Definition_And_Execute()
    {
        using var factory = new PmApiFactory(_fixture);
        using var client = factory.CreateClient(new WebApplicationFactoryClientOptions { BaseAddress = new Uri("https://localhost") });

        using (var resp = await client.GetAsync("/api/report-definitions/no.such"))
        {
            resp.StatusCode.Should().Be(HttpStatusCode.NotFound);
            var root = await ReadJsonAsync(resp);
            root.GetProperty("error").GetProperty("code").GetString().Should().Be("report.type.not_found");
        }

        using (var resp = await client.PostAsJsonAsync(
                   "/api/reports/no.such/execute",
                   new ReportExecutionRequestDto(
                       Parameters: new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
                       {
                           ["from_utc"] = "2026-03-01",
                           ["to_utc"] = "2026-03-31"
                       },
                       Offset: 0,
                       Limit: 10)))
        {
            resp.StatusCode.Should().Be(HttpStatusCode.NotFound);
            var root = await ReadJsonAsync(resp);
            root.GetProperty("error").GetProperty("code").GetString().Should().Be("report.type.not_found");
        }
    }

    [Fact]
    public async Task Invalid_Grouping_Field_Returns_400_With_Validation_Details()
    {
        using var factory = new PmApiFactory(_fixture);
        using var client = factory.CreateClient(new WebApplicationFactoryClientOptions { BaseAddress = new Uri("https://localhost") });

        using var resp = await client.PostAsJsonAsync(
            "/api/reports/accounting.ledger.analysis/execute",
            new ReportExecutionRequestDto(
                Parameters: new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
                {
                    ["from_utc"] = "2026-02-01",
                    ["to_utc"] = "2026-04-30"
                },
                Layout: new ReportLayoutDto(
                    RowGroups: [new ReportGroupingDto("unknown_field")],
                    Measures: [new ReportMeasureSelectionDto("debit_amount", ReportAggregationKind.Sum)]),
                Offset: 0,
                Limit: 50));

        resp.StatusCode.Should().Be(HttpStatusCode.BadRequest);

        var root = await ReadJsonAsync(resp);
        root.GetProperty("error").GetProperty("code").GetString().Should().Be("report.layout.invalid");

        var errors = root.GetProperty("error").GetProperty("errors");
        errors.TryGetProperty("layout.rowGroups[0].fieldCode", out var fieldErrors).Should().BeTrue();
        fieldErrors.EnumerateArray().Select(x => x.GetString()).Should().ContainSingle(x => x!.Contains("selected row grouping is no longer available", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task Duplicate_Field_Across_Row_And_Column_Groups_Returns_400_With_Validation_Details()
    {
        using var factory = new PmApiFactory(_fixture);
        using var client = factory.CreateClient(new WebApplicationFactoryClientOptions { BaseAddress = new Uri("https://localhost") });

        using var resp = await client.PostAsJsonAsync(
            "/api/reports/accounting.ledger.analysis/execute",
            new ReportExecutionRequestDto(
                Parameters: new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
                {
                    ["from_utc"] = "2026-02-01",
                    ["to_utc"] = "2026-04-30"
                },
                Layout: new ReportLayoutDto(
                    RowGroups: [new ReportGroupingDto("account_display")],
                    ColumnGroups: [new ReportGroupingDto("account_display")],
                    Measures: [new ReportMeasureSelectionDto("debit_amount", ReportAggregationKind.Sum)]),
                Offset: 0,
                Limit: 50));

        resp.StatusCode.Should().Be(HttpStatusCode.BadRequest);

        var root = await ReadJsonAsync(resp);
        root.GetProperty("error").GetProperty("code").GetString().Should().Be("report.layout.invalid");
        var errors = root.GetProperty("error").GetProperty("errors");
        errors.TryGetProperty("layout.columnGroups[0].fieldCode", out var fieldErrors).Should().BeTrue();
        fieldErrors.EnumerateArray().Select(x => x.GetString()).Should().ContainSingle(x => x!.Contains("already selected as a row grouping", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task Duplicate_Field_Across_Group_And_Detail_Returns_400_With_Validation_Details()
    {
        using var factory = new PmApiFactory(_fixture);
        using var client = factory.CreateClient(new WebApplicationFactoryClientOptions { BaseAddress = new Uri("https://localhost") });

        using var resp = await client.PostAsJsonAsync(
            "/api/reports/accounting.ledger.analysis/execute",
            new ReportExecutionRequestDto(
                Parameters: new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
                {
                    ["from_utc"] = "2026-02-01",
                    ["to_utc"] = "2026-04-30"
                },
                Layout: new ReportLayoutDto(
                    RowGroups: [new ReportGroupingDto("account_display")],
                    DetailFields: ["account_display"],
                    Measures: [new ReportMeasureSelectionDto("debit_amount", ReportAggregationKind.Sum)]),
                Offset: 0,
                Limit: 50));

        resp.StatusCode.Should().Be(HttpStatusCode.BadRequest);

        var root = await ReadJsonAsync(resp);
        root.GetProperty("error").GetProperty("code").GetString().Should().Be("report.layout.invalid");
        var errors = root.GetProperty("error").GetProperty("errors");
        errors.TryGetProperty("layout.detailFields[0]", out var fieldErrors).Should().BeTrue();
        fieldErrors.EnumerateArray().Select(x => x.GetString()).Should().ContainSingle(x => x!.Contains("already selected as a row grouping", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task PostingLog_When_Operation_Filter_Is_Invalid_Returns_User_Friendly_Message()
    {
        using var factory = new PmApiFactory(_fixture);
        using var client = factory.CreateClient(new WebApplicationFactoryClientOptions { BaseAddress = new Uri("https://localhost") });

        using var resp = await client.PostAsJsonAsync(
            "/api/reports/accounting.posting_log/execute",
            new ReportExecutionRequestDto(
                Filters: new Dictionary<string, ReportFilterValueDto>(StringComparer.OrdinalIgnoreCase)
                {
                    ["operation"] = new(JsonSerializer.SerializeToElement("Nope"))
                },
                Offset: 0,
                Limit: 50));

        resp.StatusCode.Should().Be(HttpStatusCode.BadRequest);

        var root = await ReadJsonAsync(resp);
        root.GetProperty("detail").GetString().Should().Be("Select a valid Operation. Allowed values: Post, Unpost, Repost, Close fiscal year.");
        root.GetProperty("error").GetProperty("errors").GetProperty("filters.operation")[0].GetString().Should().Be("Select a valid Operation. Allowed values: Post, Unpost, Repost, Close fiscal year.");
    }

    [Fact]
    public async Task PostingLog_When_To_Is_Before_From_Returns_User_Friendly_Message()
    {
        using var factory = new PmApiFactory(_fixture);
        using var client = factory.CreateClient(new WebApplicationFactoryClientOptions { BaseAddress = new Uri("https://localhost") });

        using var resp = await client.PostAsJsonAsync(
            "/api/reports/accounting.posting_log/execute",
            new ReportExecutionRequestDto(
                Parameters: new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
                {
                    ["from_utc"] = "2026-03-02T00:00:00Z",
                    ["to_utc"] = "2026-03-01T00:00:00Z"
                },
                Offset: 0,
                Limit: 50));

        resp.StatusCode.Should().Be(HttpStatusCode.BadRequest);

        var root = await ReadJsonAsync(resp);
        root.GetProperty("detail").GetString().Should().Be("To must be on or after From.");
        root.GetProperty("error").GetProperty("errors").GetProperty("parameters.to_utc")[0].GetString().Should().Be("To must be on or after From.");
    }

    [Fact]
    public async Task PostingLog_When_ShowGrandTotals_Is_Requested_Returns_User_Friendly_Message()
    {
        using var factory = new PmApiFactory(_fixture);
        using var client = factory.CreateClient(new WebApplicationFactoryClientOptions { BaseAddress = new Uri("https://localhost") });

        using var resp = await client.PostAsJsonAsync(
            "/api/reports/accounting.posting_log/execute",
            new ReportExecutionRequestDto(
                Layout: new ReportLayoutDto(ShowGrandTotals: true),
                Offset: 0,
                Limit: 50));

        resp.StatusCode.Should().Be(HttpStatusCode.BadRequest);

        var root = await ReadJsonAsync(resp);
        root.GetProperty("detail").GetString().Should().Be("This report does not allow grand totals.");
        root.GetProperty("error").GetProperty("errors").GetProperty("layout.showGrandTotals")[0].GetString().Should().Be("This report does not allow grand totals.");
    }

    [Fact]
    public async Task PostingLog_Cursor_Paging_Stays_Stable_And_Duplicate_Free_EndToEnd()
    {
        await using var factory = new PmApiFactory(_fixture);
        await SeedLedgerAnalysisScenarioAsync(factory);
        using var client = factory.CreateClient(new WebApplicationFactoryClientOptions { BaseAddress = new Uri("https://localhost") });

        var request = new ReportExecutionRequestDto(
            Parameters: new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["from_utc"] = "2026-02-01",
                ["to_utc"] = "2026-04-30"
            },
            Filters: new Dictionary<string, ReportFilterValueDto>(StringComparer.OrdinalIgnoreCase)
            {
                ["status"] = new(JsonSerializer.SerializeToElement("Completed"))
            },
            Offset: 0,
            Limit: 1);

        using var firstResponse = await client.PostAsJsonAsync(
            "/api/reports/accounting.posting_log/execute",
            request,
            CancellationToken.None);

        firstResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        var firstPage = await firstResponse.Content.ReadFromJsonAsync<ReportExecutionResponseDto>(Json, CancellationToken.None);
        firstPage.Should().NotBeNull();
        firstPage!.Sheet.Rows.Should().HaveCount(1);
        firstPage.HasMore.Should().BeTrue();
        firstPage.NextCursor.Should().NotBeNullOrWhiteSpace();

        using var secondResponse = await client.PostAsJsonAsync(
            "/api/reports/accounting.posting_log/execute",
            request with { Cursor = firstPage.NextCursor },
            CancellationToken.None);

        secondResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        var secondPage = await secondResponse.Content.ReadFromJsonAsync<ReportExecutionResponseDto>(Json, CancellationToken.None);
        secondPage.Should().NotBeNull();
        secondPage!.Sheet.Rows.Should().NotBeEmpty();

        firstPage.Sheet.Rows
            .Concat(secondPage.Sheet.Rows)
            .Select(ToPostingLogRowKey)
            .Should().OnlyHaveUniqueItems();
    }


    [Fact]
    public async Task LedgerAnalysis_RowGroups_Render_As_Single_Hierarchy_Column_And_Inline_Group_Totals_EndToEnd()
    {
        using var factory = new PmApiFactory(_fixture);
        using var client = factory.CreateClient(new WebApplicationFactoryClientOptions { BaseAddress = new Uri("https://localhost") });

        await SeedLedgerAnalysisScenarioAsync(factory);

        using var resp = await client.PostAsJsonAsync(
            "/api/reports/accounting.ledger.analysis/execute",
            new ReportExecutionRequestDto(
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
                    ShowGrandTotals: true),
                Offset: 0,
                Limit: 200));

        resp.StatusCode.Should().Be(HttpStatusCode.OK);

        var dto = await resp.Content.ReadFromJsonAsync<ReportExecutionResponseDto>(Json);
        dto.Should().NotBeNull();
        dto!.Sheet.Columns.Select(x => x.Code).Should().Equal("__row_hierarchy", "debit_amount__sum");
        dto.Sheet.Columns[0].Title.Should().Be("Account\nPeriod\nDocument");
        dto.Sheet.Rows.Should().Contain(x => x.RowKind == ReportRowKind.Group && x.OutlineLevel == 0 && x.Cells[0].Display == "1100 — Accounts Receivable - Tenants" && !string.IsNullOrWhiteSpace(x.Cells[1].Display));
        dto.Sheet.Rows.Should().Contain(x => x.RowKind == ReportRowKind.Group && x.OutlineLevel == 1 && x.Cells[0].Display == "February 2026" && !string.IsNullOrWhiteSpace(x.Cells[1].Display));
        dto.Sheet.Rows.Should().Contain(x => x.RowKind == ReportRowKind.Group && x.OutlineLevel == 2 && x.Cells[0].Display!.StartsWith("Receivable ", StringComparison.OrdinalIgnoreCase));
        dto.Sheet.Rows.Should().NotContain(x => x.RowKind == ReportRowKind.Subtotal);
        dto.Sheet.Rows.Should().Contain(x => x.RowKind == ReportRowKind.Total && x.Cells[0].Display == "Total");
    }

    [Fact]
    public async Task LedgerAnalysis_When_PmScopeFilters_Are_Applied_Executes_Successfully_EndToEnd()
    {
        await using var factory = new PmApiFactory(_fixture);
        var seeded = await SeedLedgerAnalysisScenarioAsync(factory);
        using var client = factory.CreateClient(new WebApplicationFactoryClientOptions { BaseAddress = new Uri("https://localhost") });

        using var response = await client.PostAsJsonAsync(
            "/api/reports/accounting.ledger.analysis/execute",
            new ReportExecutionRequestDto(
                Parameters: new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
                {
                    ["from_utc"] = "2026-02-01",
                    ["to_utc"] = "2026-04-30"
                },
                Filters: new Dictionary<string, ReportFilterValueDto>(StringComparer.OrdinalIgnoreCase)
                {
                    ["property_id"] = new(JsonSerializer.SerializeToElement(seeded.UnitId)),
                    ["lease_id"] = new(JsonSerializer.SerializeToElement(seeded.LeaseId)),
                    ["party_id"] = new(JsonSerializer.SerializeToElement(seeded.PartyId))
                },
                Layout: new ReportLayoutDto(
                    RowGroups: [new ReportGroupingDto("account_display")],
                    Measures: [new ReportMeasureSelectionDto("debit_amount", ReportAggregationKind.Sum)],
                    Sorts: [new ReportSortDto("account_display")],
                    ShowDetails: false,
                    ShowSubtotals: true,
                    ShowGrandTotals: true),
                Offset: 0,
                Limit: 100),
            CancellationToken.None);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var payload = await response.Content.ReadFromJsonAsync<ReportExecutionResponseDto>(Json, CancellationToken.None);
        payload.Should().NotBeNull();
        payload!.Diagnostics!["engine"].Should().Be("runtime");
        payload.Sheet.Columns.Select(x => x.Code).Should().Equal("__row_hierarchy", "debit_amount__sum");
        payload.Sheet.Rows.Should().Contain(x => x.RowKind == ReportRowKind.Group && x.Cells[0].Display == "1100 — Accounts Receivable - Tenants");
    }

    [Fact]
    public async Task LedgerAnalysis_When_ShowSubtotalsOnSeparateRows_Is_True_Emits_Subtotal_Rows_EndToEnd()
    {
        using var factory = new PmApiFactory(_fixture);
        using var client = factory.CreateClient(new WebApplicationFactoryClientOptions { BaseAddress = new Uri("https://localhost") });

        await SeedLedgerAnalysisScenarioAsync(factory);

        using var resp = await client.PostAsJsonAsync(
            "/api/reports/accounting.ledger.analysis/execute",
            new ReportExecutionRequestDto(
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
                    ShowSubtotalsOnSeparateRows: true,
                    ShowGrandTotals: true),
                Offset: 0,
                Limit: 200));

        resp.StatusCode.Should().Be(HttpStatusCode.OK);

        var dto = await resp.Content.ReadFromJsonAsync<ReportExecutionResponseDto>(Json);
        dto.Should().NotBeNull();
        dto!.Sheet.Columns.Select(x => x.Code).Should().Equal("__row_hierarchy", "debit_amount__sum");
        dto.Sheet.Rows.Should().NotContain(x => x.RowKind == ReportRowKind.Subtotal && x.OutlineLevel == 2);
        dto.Sheet.Rows.Should().Contain(x => x.RowKind == ReportRowKind.Subtotal && x.OutlineLevel == 1 && x.Cells[0].Display == "February 2026 subtotal");
        dto.Sheet.Rows.Should().Contain(x => x.RowKind == ReportRowKind.Subtotal && x.OutlineLevel == 0 && x.Cells[0].Display == "1100 — Accounts Receivable - Tenants subtotal");
        dto.Sheet.Rows.Should().Contain(x => x.RowKind == ReportRowKind.Group && x.OutlineLevel == 2 && x.Cells[0].Display!.StartsWith("Receivable ", StringComparison.OrdinalIgnoreCase) && !string.IsNullOrWhiteSpace(x.Cells[1].Display));
        dto.Sheet.Rows.Should().Contain(x => x.RowKind == ReportRowKind.Group && x.OutlineLevel == 0 && x.Cells[0].Display == "1100 — Accounts Receivable - Tenants" && string.IsNullOrWhiteSpace(x.Cells[1].Display));
    }

    [Fact]
    public async Task LedgerAnalysis_FlatDetail_CursorPaging_Is_Stable_And_Duplicate_Free_EndToEnd()
    {
        await using var factory = new PmApiFactory(_fixture);
        var seeded = await SeedLedgerAnalysisScenarioAsync(factory);
        using var client = factory.CreateClient(new WebApplicationFactoryClientOptions { BaseAddress = new Uri("https://localhost") });

        var request = new ReportExecutionRequestDto(
            Parameters: new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["from_utc"] = "2026-02-01",
                ["to_utc"] = "2026-04-30"
            },
            Filters: new Dictionary<string, ReportFilterValueDto>(StringComparer.OrdinalIgnoreCase)
            {
                ["property_id"] = new(JsonSerializer.SerializeToElement(seeded.UnitId)),
                ["lease_id"] = new(JsonSerializer.SerializeToElement(seeded.LeaseId)),
                ["party_id"] = new(JsonSerializer.SerializeToElement(seeded.PartyId))
            },
            Layout: new ReportLayoutDto(
                DetailFields: ["period_utc", "account_display", "document_display"],
                Measures:
                [
                    new ReportMeasureSelectionDto("debit_amount", ReportAggregationKind.Sum),
                    new ReportMeasureSelectionDto("credit_amount", ReportAggregationKind.Sum),
                    new ReportMeasureSelectionDto("net_amount", ReportAggregationKind.Sum)
                ],
                ShowDetails: false,
                ShowSubtotals: false,
                ShowSubtotalsOnSeparateRows: false,
                ShowGrandTotals: false),
            Offset: 0,
            Limit: 2);

        using var firstResponse = await client.PostAsJsonAsync(
            "/api/reports/accounting.ledger.analysis/execute",
            request,
            CancellationToken.None);

        firstResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        var firstPage = await firstResponse.Content.ReadFromJsonAsync<ReportExecutionResponseDto>(Json, CancellationToken.None);
        firstPage.Should().NotBeNull();
        firstPage!.Sheet.Rows.Should().HaveCount(2);
        firstPage.HasMore.Should().BeTrue();
        firstPage.NextCursor.Should().NotBeNullOrWhiteSpace();
        firstPage.Diagnostics!["executor"].Should().Be("runtime-ledger-analysis-flat-detail");

        using var secondResponse = await client.PostAsJsonAsync(
            "/api/reports/accounting.ledger.analysis/execute",
            request with { Cursor = firstPage.NextCursor },
            CancellationToken.None);

        secondResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        var secondPage = await secondResponse.Content.ReadFromJsonAsync<ReportExecutionResponseDto>(Json, CancellationToken.None);
        secondPage.Should().NotBeNull();
        secondPage!.Sheet.Rows.Should().NotBeEmpty();
        secondPage.Diagnostics!["executor"].Should().Be("runtime-ledger-analysis-flat-detail");

        firstPage.Sheet.Rows
            .Concat(secondPage.Sheet.Rows)
            .Select(ToLedgerAnalysisRowKey)
            .Should().OnlyHaveUniqueItems();
    }

    [Fact]
    public async Task LedgerAnalysis_GroupedLayout_Remains_Bounded_And_DoesNot_Return_Cursor()
    {
        await using var factory = new PmApiFactory(_fixture);
        await SeedLedgerAnalysisScenarioAsync(factory);
        using var client = factory.CreateClient(new WebApplicationFactoryClientOptions { BaseAddress = new Uri("https://localhost") });

        using var response = await client.PostAsJsonAsync(
            "/api/reports/accounting.ledger.analysis/execute",
            new ReportExecutionRequestDto(
                Parameters: new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
                {
                    ["from_utc"] = "2026-02-01",
                    ["to_utc"] = "2026-04-30"
                },
                Layout: new ReportLayoutDto(
                    RowGroups: [new ReportGroupingDto("account_display")],
                    Measures: [new ReportMeasureSelectionDto("debit_amount", ReportAggregationKind.Sum)],
                    Sorts: [new ReportSortDto("account_display")],
                    ShowDetails: false,
                    ShowSubtotals: true,
                    ShowSubtotalsOnSeparateRows: false,
                    ShowGrandTotals: true),
                Offset: 0,
                Limit: 1),
            CancellationToken.None);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var payload = await response.Content.ReadFromJsonAsync<ReportExecutionResponseDto>(Json, CancellationToken.None);
        payload.Should().NotBeNull();
        payload!.HasMore.Should().BeTrue();
        payload.NextCursor.Should().BeNull();
    }

    [Fact]
    public async Task LedgerAnalysis_Allows_Quarter_Column_Group_And_Column_Axis_Sort_EndToEnd()
    {
        await using var factory = new PmApiFactory(_fixture);
        await SeedLedgerAnalysisScenarioAsync(factory);
        using var client = factory.CreateClient(new WebApplicationFactoryClientOptions { BaseAddress = new Uri("https://localhost") });

        using var response = await client.PostAsJsonAsync(
            "/api/reports/accounting.ledger.analysis/execute",
            new ReportExecutionRequestDto(
                Parameters: new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
                {
                    ["from_utc"] = "2026-02-01",
                    ["to_utc"] = "2026-04-30"
                },
                Layout: new ReportLayoutDto(
                    RowGroups: [new ReportGroupingDto("account_display")],
                    ColumnGroups: [new ReportGroupingDto("period_utc", ReportTimeGrain.Quarter)],
                    Measures: [new ReportMeasureSelectionDto("net_amount", ReportAggregationKind.Sum)],
                    Sorts: [new ReportSortDto("period_utc", ReportSortDirection.Asc, ReportTimeGrain.Quarter, true)],
                    ShowDetails: false,
                    ShowSubtotals: true,
                    ShowSubtotalsOnSeparateRows: false,
                    ShowGrandTotals: true),
                Offset: 0,
                Limit: 200),
            CancellationToken.None);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var payload = await response.Content.ReadFromJsonAsync<ReportExecutionResponseDto>(Json, CancellationToken.None);
        payload.Should().NotBeNull();
        payload!.Sheet.HeaderRows.Should().NotBeNull();
        payload.Sheet.HeaderRows!
            .SelectMany(row => row.Cells)
            .Select(cell => cell.Display)
            .Where(display => !string.IsNullOrWhiteSpace(display))
            .Should()
            .Contain(display => string.Equals(display, "Q1 2026", StringComparison.OrdinalIgnoreCase));
    }

    private static string ToPostingLogRowKey(ReportSheetRowDto row)
        => string.Join("|", row.Cells.Select(ToPostingLogCellKey));

    private static string ToLedgerAnalysisRowKey(ReportSheetRowDto row)
        => string.Join("|", row.Cells.Select(ToPostingLogCellKey));

    private static string ToPostingLogCellKey(ReportCellDto cell)
    {
        if (!string.IsNullOrWhiteSpace(cell.Display))
        {
            return cell.Display.Trim();
        }

        if (cell.Value is not JsonElement value)
        {
            return string.Empty;
        }

        return value.ValueKind switch
        {
            JsonValueKind.Null or JsonValueKind.Undefined => string.Empty,
            JsonValueKind.String => value.GetString()?.Trim() ?? string.Empty,
            _ => value.ToString().Trim()
        };
    }

    private static async Task<JsonElement> ReadJsonAsync(HttpResponseMessage resp)
    {
        var text = await resp.Content.ReadAsStringAsync();
        using var doc = JsonDocument.Parse(text);
        return doc.RootElement.Clone();
    }

    private static async Task<SeededLedgerAnalysisScenario> SeedLedgerAnalysisScenarioAsync(PmApiFactory factory)
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

        return new SeededLedgerAnalysisScenario(party.Id, building.Id, unit.Id, lease.Id);
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

    private sealed record SeededLedgerAnalysisScenario(Guid PartyId, Guid BuildingId, Guid UnitId, Guid LeaseId);

    [Fact]
    public async Task LedgerAnalysis_Document_And_Account_Cells_Are_Clickable_With_True_Display_EndToEnd()
    {
        await using var factory = new PmApiFactory(_fixture);
        await SeedLedgerAnalysisScenarioAsync(factory);
        using var client = factory.CreateClient();

        using var response = await client.PostAsJsonAsync(
            "/api/reports/accounting.ledger.analysis/execute",
            new ReportExecutionRequestDto(
                Parameters: new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
                {
                    ["from_utc"] = "2026-02-01",
                    ["to_utc"] = "2026-04-30"
                },
                Filters: null,
                Layout: new ReportLayoutDto(
                    RowGroups:
                    [
                        new ReportGroupingDto("account_display"),
                        new ReportGroupingDto("document_display")
                    ],
                    Measures: [new ReportMeasureSelectionDto("debit_amount")],
                    ShowDetails: false,
                    ShowSubtotals: true,
                    ShowSubtotalsOnSeparateRows: false,
                    ShowGrandTotals: true),
                Offset: 0,
                Limit: 100),
            CancellationToken.None);

        response.EnsureSuccessStatusCode();
        var payload = await response.Content.ReadFromJsonAsync<ReportExecutionResponseDto>(Json, CancellationToken.None);
        payload.Should().NotBeNull();
        var sheet = payload!.Sheet;

        var accountGroup = sheet.Rows.First(x => x.RowKind == ReportRowKind.Group && x.OutlineLevel == 0);
        accountGroup.Cells[0].Action.Should().NotBeNull();
        accountGroup.Cells[0].Action!.Kind.Should().Be(ReportCellActionKinds.OpenReport);
        accountGroup.Cells[0].Action!.Report!.ReportCode.Should().Be("accounting.account_card");

        var documentGroup = sheet.Rows.First(x => x.RowKind == ReportRowKind.Group && x.OutlineLevel == 1 && x.Cells[0].Action?.Kind == ReportCellActionKinds.OpenDocument);
        documentGroup.Cells[0].Display.Should().StartWith("Receivable ");
        documentGroup.Cells[0].Action.Should().NotBeNull();
        documentGroup.Cells[0].Action!.Kind.Should().Be(ReportCellActionKinds.OpenDocument);
        documentGroup.Cells[0].Action!.DocumentType.Should().NotBeNullOrWhiteSpace();
        documentGroup.Cells[0].Action!.DocumentId.Should().NotBeNull();
    }

    [Fact]
    public async Task LedgerAnalysis_Pivot_Column_Headers_Show_True_Display_And_Are_Clickable_EndToEnd()
    {
        await using var factory = new PmApiFactory(_fixture);
        await SeedLedgerAnalysisScenarioAsync(factory);
        using var client = factory.CreateClient();

        using var response = await client.PostAsJsonAsync(
            "/api/reports/accounting.ledger.analysis/execute",
            new ReportExecutionRequestDto(
                Parameters: new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
                {
                    ["from_utc"] = "2026-02-01",
                    ["to_utc"] = "2026-04-30"
                },
                Filters: null,
                Layout: new ReportLayoutDto(
                    RowGroups: [new ReportGroupingDto("period_utc", ReportTimeGrain.Day)],
                    ColumnGroups:
                    [
                        new ReportGroupingDto("account_display"),
                        new ReportGroupingDto("document_display")
                    ],
                    Measures: [new ReportMeasureSelectionDto("debit_amount")],
                    ShowDetails: false,
                    ShowSubtotals: true,
                    ShowSubtotalsOnSeparateRows: false,
                    ShowGrandTotals: true),
                Offset: 0,
                Limit: 200),
            CancellationToken.None);

        response.EnsureSuccessStatusCode();
        var payload = await response.Content.ReadFromJsonAsync<ReportExecutionResponseDto>(Json, CancellationToken.None);
        payload.Should().NotBeNull();
        payload!.Sheet.HeaderRows.Should().NotBeNull();
        var headerRows = payload.Sheet.HeaderRows!;

        var accountHeader = headerRows[0].Cells.First(x => x.Display == "1100 — Accounts Receivable - Tenants");
        accountHeader.Action.Should().NotBeNull();
        accountHeader.Action!.Kind.Should().Be(ReportCellActionKinds.OpenReport);
        accountHeader.Action!.Report!.ReportCode.Should().Be("accounting.account_card");
        accountHeader.Action!.Report!.Parameters!["from_utc"].Should().Be("2026-02-01");
        accountHeader.Action!.Report!.Parameters!["to_utc"].Should().Be("2026-04-30");
        accountHeader.Action!.Report!.Filters.Should().ContainKey("account_id");

        var documentHeader = headerRows[1].Cells.First(x => x.Display!.StartsWith("Receivable ", StringComparison.OrdinalIgnoreCase));
        documentHeader.Action.Should().NotBeNull();
        documentHeader.Action!.Kind.Should().Be(ReportCellActionKinds.OpenDocument);
        documentHeader.Action!.DocumentType.Should().NotBeNullOrWhiteSpace();
        documentHeader.Action!.DocumentId.Should().NotBeNull();
    }
}
