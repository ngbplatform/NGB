using FluentAssertions;
using NGB.Contracts.Reporting;
using NGB.Core.Reporting.Exceptions;
using NGB.Runtime.IntegrationTests.Infrastructure;
using Xunit;

namespace NGB.Runtime.IntegrationTests.Reporting;

[Collection(PostgresCollection.Name)]
public sealed class LedgerAnalysis_Composable_EndToEnd_P0Tests(PostgresTestFixture fixture)
    : IntegrationTestBase(fixture)
{
    [Fact]
    public async Task ExecuteAsync_FlatHierarchy_RendersSingleHierarchyColumn_AndClickableDocumentAndAccountCells()
    {
        using var host = ComposableReportingIntegrationTestHelpers.CreateHost(Fixture.ConnectionString);
        await ComposableReportingIntegrationTestHelpers.SeedMinimalCoAAsync(host);

        await ComposableReportingIntegrationTestHelpers.CreatePostedAccountingDocumentAsync(
            host,
            number: "IT-LA-001",
            dateUtc: new DateTime(2026, 2, 5, 0, 0, 0, DateTimeKind.Utc),
            debitCode: "50",
            creditCode: "90.1",
            amount: 100m);

        var response = await ComposableReportingIntegrationTestHelpers.ExecuteLedgerAnalysisAsync(
            host,
            new ReportExecutionRequestDto(
                Parameters: new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
                {
                    ["from_utc"] = "2026-02-01",
                    ["to_utc"] = "2026-03-31"
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

        response.Diagnostics.Should().ContainKey("engine").WhoseValue.Should().Be("runtime");
        response.Diagnostics.Should().ContainKey("executor").WhoseValue.Should().Be("postgres-foundation");
        response.Sheet.Columns.Select(x => x.Code).Should().Equal("__row_hierarchy", "debit_amount__sum");
        response.Sheet.Columns[0].Title.Should().Be("Account\nPeriod\nDocument");
        response.Sheet.Meta!.HasRowOutline.Should().BeTrue();

        var accountGroup = response.Sheet.Rows.First(
            x => x.RowKind == ReportRowKind.Group
                 && x.OutlineLevel == 0
                 && x.Cells[0].Display == "50 — Cash");
        accountGroup.Cells[0].Action.Should().NotBeNull();
        accountGroup.Cells[0].Action!.Kind.Should().Be(ReportCellActionKinds.OpenReport);
        accountGroup.Cells[0].Action!.Report!.ReportCode.Should().Be("accounting.account_card");
        accountGroup.Cells[0].Action!.Report!.Filters.Should().ContainKey("account_id");
        ComposableReportingIntegrationTestHelpers.ReadDecimalCell(accountGroup.Cells[1]).Should().Be(100m);

        response.Sheet.Rows.Should().Contain(
            x => x.RowKind == ReportRowKind.Group
                 && x.OutlineLevel == 1
                 && x.Cells[0].Display == "February 2026");

        response.Sheet.Rows.Should().Contain(
            x => x.RowKind == ReportRowKind.Group
                 && x.OutlineLevel == 2
                 && x.Cells[0].Display != null
                 && x.Cells[0].Display!.StartsWith("IT Document A IT-LA-001", StringComparison.OrdinalIgnoreCase)
                 && x.Cells[0].Action != null
                 && x.Cells[0].Action!.Kind == ReportCellActionKinds.OpenDocument
                 && x.Cells[0].Action!.DocumentType == "it_doc_a"
                 && x.Cells[0].Action!.DocumentId != Guid.Empty);

        response.Sheet.Rows.Should().NotContain(x => x.RowKind == ReportRowKind.Subtotal);
        response.Sheet.Rows.Should().Contain(x => x.RowKind == ReportRowKind.Total && x.Cells[0].Display == "Total");
    }

    [Fact]
    public async Task ExecuteAsync_PivotLayout_RendersClickableDisplayHeaders()
    {
        using var host = ComposableReportingIntegrationTestHelpers.CreateHost(Fixture.ConnectionString);
        await ComposableReportingIntegrationTestHelpers.SeedMinimalCoAAsync(host);

        await ComposableReportingIntegrationTestHelpers.CreatePostedAccountingDocumentAsync(
            host,
            number: "IT-LA-101",
            dateUtc: new DateTime(2026, 2, 5, 0, 0, 0, DateTimeKind.Utc),
            debitCode: "50",
            creditCode: "90.1",
            amount: 100m);

        var response = await ComposableReportingIntegrationTestHelpers.ExecuteLedgerAnalysisAsync(
            host,
            new ReportExecutionRequestDto(
                Parameters: new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
                {
                    ["from_utc"] = "2026-02-01",
                    ["to_utc"] = "2026-03-31"
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
                    ShowGrandTotals: true),
                Offset: 0,
                Limit: 200));

        response.Diagnostics.Should().ContainKey("engine").WhoseValue.Should().Be("runtime");
        response.Sheet.Meta!.IsPivot.Should().BeTrue();
        response.Sheet.Meta!.HasColumnGroups.Should().BeTrue();
        response.Sheet.HeaderRows.Should().NotBeNull();
        response.Sheet.HeaderRows!.Count.Should().BeGreaterThanOrEqualTo(2);

        var accountHeader = response.Sheet.HeaderRows[0].Cells.First(x => x.Display == "50 — Cash");
        accountHeader.Action.Should().NotBeNull();
        accountHeader.Action!.Kind.Should().Be(ReportCellActionKinds.OpenReport);
        accountHeader.Action!.Report!.ReportCode.Should().Be("accounting.account_card");
        accountHeader.Action!.Report!.Parameters!["from_utc"].Should().Be("2026-02-01");
        accountHeader.Action!.Report!.Parameters!["to_utc"].Should().Be("2026-03-31");
        accountHeader.Action!.Report!.Filters.Should().ContainKey("account_id");

        var documentHeader = response.Sheet.HeaderRows[1].Cells.First(
            x => x.Display != null
                 && x.Display.StartsWith("IT Document A IT-LA-101", StringComparison.OrdinalIgnoreCase));
        documentHeader.Action.Should().NotBeNull();
        documentHeader.Action!.Kind.Should().Be(ReportCellActionKinds.OpenDocument);
        documentHeader.Action!.DocumentType.Should().Be("it_doc_a");
        documentHeader.Action!.DocumentId.Should().NotBeEmpty();
    }

    [Fact]
    public async Task ExecuteAsync_InvalidLayout_FailsFast_WithUserFriendlyValidation()
    {
        using var host = ComposableReportingIntegrationTestHelpers.CreateHost(Fixture.ConnectionString);
        await ComposableReportingIntegrationTestHelpers.SeedMinimalCoAAsync(host);

        var act = async () => await ComposableReportingIntegrationTestHelpers.ExecuteLedgerAnalysisAsync(
            host,
            new ReportExecutionRequestDto(
                Parameters: new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
                {
                    ["from_utc"] = "2026-02-01",
                    ["to_utc"] = "2026-03-31"
                },
                Layout: new ReportLayoutDto(
                    RowGroups: [new ReportGroupingDto("account_display")],
                    ColumnGroups: [new ReportGroupingDto("account_display")],
                    Measures: [new ReportMeasureSelectionDto("debit_amount", ReportAggregationKind.Sum)]),
                Offset: 0,
                Limit: 50));

        var ex = await act.Should().ThrowAsync<ReportLayoutValidationException>();
        ex.Which.ErrorCode.Should().Be(ReportLayoutValidationException.Code);
        ex.Which.Message.Should().Contain("already selected as a row grouping");
        ex.Which.Context.Should().ContainKey("fieldPath");
        ex.Which.Context["fieldPath"].Should().Be("layout.columnGroups[0].fieldCode");
    }
}
