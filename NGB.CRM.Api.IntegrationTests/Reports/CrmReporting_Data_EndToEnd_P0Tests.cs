using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using NGB.Application.Abstractions.Services;
using NGB.CRM.Api.IntegrationTests.Infrastructure;
using NGB.CRM.Api.IntegrationTests.Support;
using NGB.Contracts.Common;
using NGB.Contracts.Reporting;
using Npgsql;
using Xunit;

namespace NGB.CRM.Api.IntegrationTests.Reports;

[Collection(CrmSeededReportingCollection.Name)]
public sealed class CrmReporting_Data_EndToEnd_P0Tests(CrmSeededReportingFixture fixture)
{
    [Fact]
    public async Task Seeded_Demo_Baseline_Rejects_Writes()
    {
        await using var connection = new NpgsqlConnection(fixture.ReadOnlyConnectionString);
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand(
            "UPDATE public.cat_crm_account SET display = display WHERE FALSE;",
            connection);

        var write = async () => await command.ExecuteNonQueryAsync();

        var exception = await write.Should().ThrowAsync<PostgresException>();
        exception.Which.SqlState.Should().Be("25006");
    }

    [Theory]
    [InlineData(CrmCodes.SalesPipelineReport, "amount", 2421500)]
    [InlineData(CrmCodes.OpportunityHistoryReport, "amount", 4721750)]
    [InlineData(CrmCodes.LeadConversionFunnelReport, "lead_count", 98)]
    [InlineData(CrmCodes.ActivitySummaryReport, "activity_count", 33)]
    [InlineData(CrmCodes.QuoteRegisterReport, "amount", 1490977.5)]
    public async Task Seeded_Demo_Data_Executes_Canonical_Reports(string reportCode, string measureCode, decimal expectedSum)
    {
        await using var scope = fixture.Services.CreateAsyncScope();

        var reports = scope.ServiceProvider.GetRequiredService<IReportEngine>();

        var response = await reports.ExecuteAsync(
            reportCode,
            new ReportExecutionRequestDto(DisablePaging: true),
            CancellationToken.None);

        response.Sheet.Rows.Should().NotBeEmpty();
        CrmIntegrationTestHelpers.SumMeasure(response, measureCode).Should().Be(expectedSum);
    }

    [Fact]
    public async Task Seeded_Demo_Displays_Documents_And_Report_Row_Groups_With_Business_Names()
    {
        await using var scope = fixture.Services.CreateAsyncScope();

        var documents = scope.ServiceProvider.GetRequiredService<IDocumentService>();
        var reports = scope.ServiceProvider.GetRequiredService<IReportEngine>();

        var quotes = await documents.GetPageAsync(
            CrmCodes.Quote,
            new PageRequestDto(Offset: 0, Limit: 10, Search: null),
            CancellationToken.None);

        quotes.Items.Should().Contain(x =>
            !string.IsNullOrWhiteSpace(x.Display)
            && x.Display.StartsWith("Quote Q-2026-", StringComparison.Ordinal)
            && x.Display.Contains('/', StringComparison.Ordinal));

        foreach (var reportCode in new[]
                 {
                     CrmCodes.ActivitySummaryReport,
                     CrmCodes.QuoteRegisterReport,
                     CrmCodes.OpportunityHistoryReport
                 })
        {
            var response = await reports.ExecuteAsync(
                reportCode,
                new ReportExecutionRequestDto(DisablePaging: true),
                CancellationToken.None);

            var hierarchyDisplays = response.Sheet.Rows
                .Select(row => row.Cells.FirstOrDefault()?.Display ?? row.Cells.FirstOrDefault()?.Value?.ToString())
                .Where(x => !string.IsNullOrWhiteSpace(x))
                .Select(x => x!)
                .ToArray();

            hierarchyDisplays.Should().NotBeEmpty();
            hierarchyDisplays.Where(IsGuid).Should().BeEmpty($"{reportCode} should group by display fields, not raw ids");
        }
    }

    [Fact]
    public async Task Seeded_Demo_Report_Group_Cells_Expose_Drilldown_Actions()
    {
        await using var scope = fixture.Services.CreateAsyncScope();

        var reports = scope.ServiceProvider.GetRequiredService<IReportEngine>();

        var sales = await ExecuteGroupedAsync(
            reports,
            CrmCodes.SalesPipelineReport,
            ["customer_display", "opportunity_display"],
            "amount");
        AssertCatalogAction(sales, CrmCodes.Account);
        AssertDocumentAction(sales, CrmCodes.LeadConversion);

        var activities = await ExecuteGroupedAsync(
            reports,
            CrmCodes.ActivitySummaryReport,
            ["customer_display", "contact_display", "outcome"],
            "activity_count");
        AssertCatalogAction(activities, CrmCodes.Account);
        AssertCatalogAction(activities, CrmCodes.Contact);

        var quotes = await ExecuteGroupedAsync(
            reports,
            CrmCodes.QuoteRegisterReport,
            ["customer_display", "contact_display", "currency"],
            "quote_count");
        AssertCatalogAction(quotes, CrmCodes.Account);
        AssertCatalogAction(quotes, CrmCodes.Contact);

        var history = await ExecuteGroupedAsync(
            reports,
            CrmCodes.OpportunityHistoryReport,
            ["customer_display", "opportunity_display", "stage_display"],
            "amount");
        AssertDocumentAction(history, CrmCodes.LeadConversion);
        AssertCatalogAction(history, CrmCodes.Account);
        AssertCatalogAction(history, CrmCodes.OpportunityStage);

        var funnel = await ExecuteGroupedAsync(
            reports,
            CrmCodes.LeadConversionFunnelReport,
            ["document_display"],
            "lead_count");
        Actions(funnel).Should().Contain(x =>
            x.Kind == ReportCellActionKinds.OpenDocument
            && x.DocumentId.HasValue
            && !string.IsNullOrWhiteSpace(x.DocumentType));
    }

    private static bool IsGuid(string value)
        => Guid.TryParse(value, out var parsed) && parsed != Guid.Empty;

    private static async Task<ReportExecutionResponseDto> ExecuteGroupedAsync(
        IReportEngine reports,
        string reportCode,
        IReadOnlyList<string> rowGroups,
        string measureCode)
    {
        return await reports.ExecuteAsync(
            reportCode,
            new ReportExecutionRequestDto(
                Layout: new ReportLayoutDto(
                    RowGroups: rowGroups.Select(static x => new ReportGroupingDto(x)).ToArray(),
                    Measures: [new ReportMeasureSelectionDto(measureCode)],
                    ShowDetails: false),
                DisablePaging: true),
            CancellationToken.None);
    }

    private static IEnumerable<ReportCellActionDto> Actions(ReportExecutionResponseDto response)
        => response.Sheet.Rows
            .SelectMany(static x => x.Cells)
            .Select(static x => x.Action)
            .OfType<ReportCellActionDto>();

    private static void AssertCatalogAction(ReportExecutionResponseDto response, string catalogType)
    {
        Actions(response).Should().Contain(x =>
            x.Kind == ReportCellActionKinds.OpenCatalog
            && x.CatalogType == catalogType
            && x.CatalogId.HasValue);
    }

    private static void AssertDocumentAction(ReportExecutionResponseDto response, string documentType)
    {
        Actions(response).Should().Contain(x =>
            x.Kind == ReportCellActionKinds.OpenDocument
            && x.DocumentType == documentType
            && x.DocumentId.HasValue);
    }
}
