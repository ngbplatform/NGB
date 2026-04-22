using FluentAssertions;
using NGB.Accounting.Reports.LedgerAnalysis;
using NGB.Application.Abstractions.Services;
using NGB.Contracts.Reporting;
using NGB.Persistence.Readers.Reports;
using NGB.Runtime.Reporting;
using NGB.Runtime.Reporting.Definitions;
using NGB.Runtime.Reporting.Internal;
using NGB.Tools.Exceptions;
using Xunit;

namespace NGB.Runtime.Tests.Reporting;

public sealed class LedgerAnalysisComposableReportExecutor_P0Tests
{
    [Fact]
    public async Task ExecuteAsync_WhenLayoutIsTrueFlatDetail_UsesCursorReader()
    {
        var tabular = new StubTabularExecutor();
        var reader = new StubLedgerAnalysisFlatDetailReader
        {
            Page = new LedgerAnalysisFlatDetailPage(
                Rows:
                [
                    new LedgerAnalysisFlatDetailRow(new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase)
                    {
                        ["period_utc"] = new DateTime(2026, 2, 7, 0, 0, 0, DateTimeKind.Utc),
                        ["account_display"] = "1100 — Accounts Receivable - Tenants",
                        ["document_display"] = "Receivable RC-2026-000001",
                        ["debit_amount__sum"] = 70m
                    })
                ],
                HasMore: true,
                NextCursor: new LedgerAnalysisFlatDetailCursor(new DateTime(2026, 2, 7, 0, 0, 0, DateTimeKind.Utc), 42, "debit"))
        };
        var sut = new LedgerAnalysisComposableReportExecutor(new ReportExecutionPlanner(), tabular, reader);

        var result = await sut.ExecuteAsync(
            new AccountingLedgerAnalysisDefinitionSource().GetDefinitions().Single(),
            new ReportExecutionRequestDto(
                Parameters: BuildParameters(),
                Layout: new ReportLayoutDto(
                    DetailFields: ["period_utc", "account_display", "document_display"],
                    Measures: [new ReportMeasureSelectionDto("debit_amount", ReportAggregationKind.Sum)],
                    ShowDetails: false,
                    ShowSubtotals: false,
                    ShowSubtotalsOnSeparateRows: false,
                    ShowGrandTotals: false),
                Offset: 0,
                Limit: 2),
            CancellationToken.None);

        reader.WasCalled.Should().BeTrue();
        tabular.WasCalled.Should().BeFalse();
        reader.LastRequest.Should().NotBeNull();
        reader.LastRequest!.DatasetCode.Should().Be("accounting.ledger.analysis");
        result.Rows.Should().HaveCount(1);
        result.HasMore.Should().BeTrue();
        result.NextCursor.Should().Be(LedgerAnalysisDetailCursorCodec.Encode(new LedgerAnalysisFlatDetailCursor(new DateTime(2026, 2, 7, 0, 0, 0, DateTimeKind.Utc), 42, "debit")));
        result.Diagnostics.Should().NotBeNull();
        result.Diagnostics!["executor"].Should().Be("runtime-ledger-analysis-flat-detail");
    }

    [Fact]
    public async Task ExecuteAsync_WhenLayoutIsGrouped_FallsBackToBoundedTabularPath()
    {
        var tabular = new StubTabularExecutor();
        var reader = new StubLedgerAnalysisFlatDetailReader();
        var sut = new LedgerAnalysisComposableReportExecutor(new ReportExecutionPlanner(), tabular, reader);

        var result = await sut.ExecuteAsync(
            new AccountingLedgerAnalysisDefinitionSource().GetDefinitions().Single(),
            new ReportExecutionRequestDto(
                Parameters: BuildParameters(),
                Layout: new ReportLayoutDto(
                    RowGroups: [new ReportGroupingDto("account_display")],
                    Measures: [new ReportMeasureSelectionDto("debit_amount", ReportAggregationKind.Sum)],
                    Sorts: [new ReportSortDto("account_display")],
                    ShowDetails: false,
                    ShowSubtotals: true,
                    ShowSubtotalsOnSeparateRows: false,
                    ShowGrandTotals: true),
                Offset: 0,
                Limit: 5),
            CancellationToken.None);

        reader.WasCalled.Should().BeFalse();
        tabular.WasCalled.Should().BeTrue();
        result.Diagnostics.Should().NotBeNull();
        result.Diagnostics!["executor"].Should().Be("tabular");
    }

    [Fact]
    public async Task ExecuteAsync_WhenCursorIsProvidedForUnsupportedLayout_Throws()
    {
        var tabular = new StubTabularExecutor();
        var reader = new StubLedgerAnalysisFlatDetailReader();
        var sut = new LedgerAnalysisComposableReportExecutor(new ReportExecutionPlanner(), tabular, reader);

        var act = () => sut.ExecuteAsync(
            new AccountingLedgerAnalysisDefinitionSource().GetDefinitions().Single(),
            new ReportExecutionRequestDto(
                Parameters: BuildParameters(),
                Cursor: LedgerAnalysisDetailCursorCodec.Encode(new LedgerAnalysisFlatDetailCursor(new DateTime(2026, 2, 7, 0, 0, 0, DateTimeKind.Utc), 42, "debit")),
                Layout: new ReportLayoutDto(
                    RowGroups: [new ReportGroupingDto("account_display")],
                    Measures: [new ReportMeasureSelectionDto("debit_amount", ReportAggregationKind.Sum)],
                    ShowDetails: false,
                    ShowSubtotals: true,
                    ShowSubtotalsOnSeparateRows: false,
                    ShowGrandTotals: true),
                Offset: 0,
                Limit: 5),
            CancellationToken.None);

        await act.Should().ThrowAsync<NgbArgumentInvalidException>()
            .WithMessage("*Cursor paging is supported only for flat detail ledger analysis mode*");
    }

    [Fact]
    public async Task ExecuteAsync_WhenPagingIsDisabled_Ignores_Cursor_For_Flat_Detail_Mode()
    {
        var tabular = new StubTabularExecutor();
        var reader = new StubLedgerAnalysisFlatDetailReader
        {
            Page = new LedgerAnalysisFlatDetailPage(
                Rows:
                [
                    new LedgerAnalysisFlatDetailRow(new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase)
                    {
                        ["period_utc"] = new DateTime(2026, 2, 7, 0, 0, 0, DateTimeKind.Utc),
                        ["account_display"] = "1100 — Accounts Receivable - Tenants",
                        ["debit_amount__sum"] = 70m
                    })
                ],
                HasMore: false,
                NextCursor: null)
        };
        var sut = new LedgerAnalysisComposableReportExecutor(new ReportExecutionPlanner(), tabular, reader);

        var result = await sut.ExecuteAsync(
            new AccountingLedgerAnalysisDefinitionSource().GetDefinitions().Single(),
            new ReportExecutionRequestDto(
                Parameters: BuildParameters(),
                Cursor: "ignored-invalid-cursor",
                DisablePaging: true,
                Layout: new ReportLayoutDto(
                    DetailFields: ["period_utc", "account_display"],
                    Measures: [new ReportMeasureSelectionDto("debit_amount", ReportAggregationKind.Sum)],
                    ShowDetails: false,
                    ShowSubtotals: false,
                    ShowSubtotalsOnSeparateRows: false,
                    ShowGrandTotals: false),
                Offset: 0,
                Limit: 2),
            CancellationToken.None);

        reader.WasCalled.Should().BeTrue();
        reader.LastRequest.Should().NotBeNull();
        reader.LastRequest!.DisablePaging.Should().BeTrue();
        reader.LastRequest.Cursor.Should().BeNull();
        result.Limit.Should().Be(1);
        result.HasMore.Should().BeFalse();
    }

    private static IReadOnlyDictionary<string, string> BuildParameters()
        => new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["from_utc"] = "2026-02-01",
            ["to_utc"] = "2026-04-30"
        };

    private sealed class StubLedgerAnalysisFlatDetailReader : ILedgerAnalysisFlatDetailReader
    {
        public bool WasCalled { get; private set; }
        public LedgerAnalysisFlatDetailPageRequest? LastRequest { get; private set; }
        public LedgerAnalysisFlatDetailPage? Page { get; init; }

        public Task<LedgerAnalysisFlatDetailPage> GetPageAsync(LedgerAnalysisFlatDetailPageRequest request, CancellationToken ct = default)
        {
            WasCalled = true;
            LastRequest = request;
            return Task.FromResult(Page ?? new LedgerAnalysisFlatDetailPage([], false, null));
        }
    }

    private sealed class StubTabularExecutor : ITabularReportPlanExecutor
    {
        public bool WasCalled { get; private set; }

        public Task<ReportDataPage> ExecuteAsync(
            ReportDefinitionDto definition,
            ReportExecutionRequestDto request,
            string reportCode,
            string? datasetCode,
            IReadOnlyList<ReportPlanGrouping> rowGroups,
            IReadOnlyList<ReportPlanGrouping> columnGroups,
            IReadOnlyList<ReportPlanFieldSelection> detailFields,
            IReadOnlyList<ReportPlanMeasure> measures,
            IReadOnlyList<ReportPlanSort> sorts,
            IReadOnlyList<ReportPlanPredicate> predicates,
            IReadOnlyList<ReportPlanParameter> parameters,
            ReportPlanPaging paging,
            CancellationToken ct)
        {
            WasCalled = true;
            return Task.FromResult(new ReportDataPage(
                Columns: [],
                Rows: [],
                Offset: paging.Offset,
                Limit: paging.Limit,
                Total: null,
                HasMore: false,
                Diagnostics: new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
                {
                    ["executor"] = "tabular"
                }));
        }
    }
}
