using System.Globalization;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using EquipmentInspection.Definitions;
using Microsoft.Extensions.DependencyInjection;
using NGB.Application.Abstractions.Services;
using NGB.Contracts.Documents;
using NGB.Contracts.Reporting;
using NGB.Contracts.Services;
using Xunit;
using static EquipmentInspection.IntegrationTests.InspectionRequests;

namespace EquipmentInspection.IntegrationTests;

public sealed class SpecializedReportHttpTests
{
    private const string ExecutePath = "/api/reports/" + InspectionReports.OutcomeRate + "/execute";

    [Fact]
    public async Task Specialized_summary_returns_real_counts_and_an_undefined_rate_for_an_empty_selection()
    {
        await using var host = new InspectionHost();
        await host.StartAsync();
        using var admin = await host.CreateClientAsync("administrator");
        var empty = await PostAsync<ReportExecutionResponseDto>(admin, ExecutePath, Request());
        var emptyRow = Assert.Single(empty.Sheet.Rows);
        Assert.Equal(ReportRowKind.Total, emptyRow.RowKind);
        Assert.Equal(0, emptyRow.Cells[0].Value!.Value.GetInt64());
        Assert.Equal(0, emptyRow.Cells[1].Value!.Value.GetInt64());
        Assert.Null(emptyRow.Cells[2].Value);
        Assert.Equal("No inspections", emptyRow.Cells[2].Display);

        var equipment = await PostAsync<CatalogItemDto>(admin, "/api/catalogs/example.equipment",
            Payload(("display", "Outcome report bench")));
        foreach (var result in new[] { "Passed", "Passed", "Passed", "Rejected", "Follow-up" })
        {
            var document = await PostAsync<DocumentDto>(admin, "/api/documents/example.inspection",
                CompleteInspectionPayload(equipment.Id, result));
            var path = "/api/documents/example.inspection/" + document.Id;
            var state = await admin.GetFromJsonAsync<DocumentEditorStateDto>(path + "/editor-state", Json);
            using var posted = await ExecutePostAsync(admin, path, state!.DocumentVersion, document.Id.ToString());
            await EnsureSuccessAsync(posted);
        }
        await PostAsync<DocumentDto>(admin, "/api/documents/example.inspection",
            Payload(("equipment_id", equipment.Id), ("result", "Passed")));

        var response = await PostAsync<ReportExecutionResponseDto>(admin, ExecutePath, Request());
        Assert.Equal("inspection-outcome-rate", response.Diagnostics!["executor"]);
        Assert.Equal(["total", "passed", "percentage"], response.Sheet.Columns.Select(column => column.Code));
        var row = Assert.Single(response.Sheet.Rows);
        Assert.Equal([5m, 3m, 60m], row.Cells.Select(cell => cell.Value!.Value.GetDecimal()));
        Assert.Equal("60%", row.Cells[2].Display);
        Assert.False(response.HasMore);
        Assert.Null(response.NextCursor);
        Assert.Equal(1, response.Total);
        var outsidePeriod = await PostAsync<ReportExecutionResponseDto>(admin, ExecutePath,
            Request() with { Parameters = new Dictionary<string, string> { ["from_utc"] = "2000-01-01", ["to_utc"] = "2000-01-02" } });
        Assert.Equal(0, Assert.Single(outsidePeriod.Sheet.Rows).Cells[0].Value!.Value.GetInt64());
    }

    [Fact]
    public async Task Specialized_execution_keeps_shared_admission_authorization_and_cancellation()
    {
        await using var host = new InspectionHost();
        await host.StartAsync();
        using var admin = await host.CreateClientAsync("administrator");
        using var reader = await host.CreateClientAsync("reader");
        using var anonymous = host.CreateAnonymousClient();
        var definition = (await admin.GetFromJsonAsync<ReportDefinitionDto>(
            "/api/report-definitions/" + InspectionReports.OutcomeRate, Json))!;
        Assert.Equal(ReportExecutionMode.Canonical, definition.Mode);
        Assert.False(definition.Capabilities!.AllowsFilters);
        Assert.False(definition.Capabilities.AllowsXlsxExport);
        using var denied = await reader.PostAsJsonAsync(ExecutePath, Request());
        using var unauthenticated = await anonymous.PostAsJsonAsync(ExecutePath, Request());
        Assert.Equal(HttpStatusCode.Forbidden, denied.StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, unauthenticated.StatusCode);

        foreach (var invalid in new[]
        {
            Request() with { Parameters = null },
            Request() with { Offset = 1 },
            Request() with { DisablePaging = true },
            Request() with { Cursor = "unknown" },
            Request() with { Filters = new Dictionary<string, ReportFilterValueDto> { ["result"] = new(JsonSerializer.SerializeToElement("Passed")) } }
        })
        {
            using var rejected = await admin.PostAsJsonAsync(ExecutePath, invalid);
            Assert.Equal(HttpStatusCode.BadRequest, rejected.StatusCode);
        }

        await using var scope = host.Services.CreateAsyncScope();
        var executor = scope.ServiceProvider.GetServices<IReportSpecializedPlanExecutor>()
            .Single(item => item.ReportCode == InspectionReports.OutcomeRate);
        using var cancelled = new CancellationTokenSource();
        await cancelled.CancelAsync();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => executor.ExecuteAsync(definition, Request(), cancelled.Token));
        await host.LinkUserAsync("reader", true, InspectionHost.AdministratorRoleCode, "Application administrator");
        await PostAsync<ReportExecutionResponseDto>(reader, ExecutePath, Request());
        await host.DeactivateUserAsync("reader");
        using var inactive = await reader.PostAsJsonAsync(ExecutePath, Request());
        Assert.Equal(HttpStatusCode.Forbidden, inactive.StatusCode);
    }

    private static ReportExecutionRequestDto Request()
    {
        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        return new ReportExecutionRequestDto(Limit: 1, Parameters: new Dictionary<string, string>
        {
            ["from_utc"] = today.AddDays(-1).ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
            ["to_utc"] = today.AddDays(1).ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)
        });
    }
}
