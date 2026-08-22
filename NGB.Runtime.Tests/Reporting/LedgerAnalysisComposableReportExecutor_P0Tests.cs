using FluentAssertions;
using System.Text.Json;
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
    public async Task Constructor_ReportCode_AndNullArguments_AreCovered()
    {
        var planner = new ReportExecutionPlanner();
        var tabular = new StubTabularExecutor();
        var reader = new StubLedgerAnalysisFlatDetailReader();
        var sut = new LedgerAnalysisComposableReportExecutor(planner, tabular, reader);

        sut.ReportCode.Should().Be("accounting.ledger.analysis");

        Action nullPlanner = () => new LedgerAnalysisComposableReportExecutor(null!, tabular, reader);
        Action nullTabular = () => new LedgerAnalysisComposableReportExecutor(planner, null!, reader);
        Action nullReader = () => new LedgerAnalysisComposableReportExecutor(planner, tabular, null!);
        nullPlanner.Should().Throw<NgbConfigurationViolationException>();
        nullTabular.Should().Throw<NgbConfigurationViolationException>();
        nullReader.Should().Throw<NgbConfigurationViolationException>();

        var nullDefinition = () => sut.ExecuteAsync(null!, FlatRequest(), default);
        var nullRequest = () => sut.ExecuteAsync(Definition(), null!, default);
        (await nullDefinition.Should().ThrowAsync<NgbArgumentRequiredException>()).Which.ParamName.Should().Be("definition");
        (await nullRequest.Should().ThrowAsync<NgbArgumentRequiredException>()).Which.ParamName.Should().Be("request");
    }

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

    [Fact]
    public async Task ExecuteAsync_EveryCursorIneligibilityReason_FallsBackToTabularExecutor()
    {
        var definition = Definition();
        var cases = new (ReportDefinitionDto Definition, ReportExecutionRequestDto Request)[]
        {
            (definition with { Mode = ReportExecutionMode.Canonical }, FlatRequest()),
            (definition with { Dataset = null, DefaultLayout = new ReportLayoutDto() }, new ReportExecutionRequestDto(Layout: new ReportLayoutDto())),
            (definition, FlatRequest(layout: FlatLayout(rowGroups: [new ReportGroupingDto("account_display")]))),
            (definition, FlatRequest(layout: FlatLayout(columnGroups: [new ReportGroupingDto("account_display")]))),
            (definition, FlatRequest(layout: FlatLayout(detailFields: []))),
            (definition, FlatRequest(layout: FlatLayout(showGrandTotals: true))),
            (definition, FlatRequest(layout: FlatLayout(measures: [new ReportMeasureSelectionDto("debit_amount", ReportAggregationKind.Min)]))),
            (definition, FlatRequest(offset: 1))
        };

        foreach (var item in cases)
        {
            var tabular = new StubTabularExecutor();
            var reader = new StubLedgerAnalysisFlatDetailReader();
            var sut = new LedgerAnalysisComposableReportExecutor(new ReportExecutionPlanner(), tabular, reader);

            await sut.ExecuteAsync(item.Definition, item.Request, default);

            tabular.WasCalled.Should().BeTrue();
            reader.WasCalled.Should().BeFalse();
        }
    }

    [Fact]
    public async Task ExecuteAsync_UnsupportedSortShapesFallBack_WhileDefaultPeriodSortUsesCursorReader()
    {
        var unsupportedSorts = new IReadOnlyList<ReportSortDto>[]
        {
            [new ReportSortDto("period_utc"), new ReportSortDto("account_display")],
            [new ReportSortDto("debit_amount")],
            [new ReportSortDto("period_utc", AppliesToColumnAxis: true)],
            [new ReportSortDto("period_utc", TimeGrain: ReportTimeGrain.Day)],
            [new ReportSortDto("period_utc", ReportSortDirection.Desc)],
            [new ReportSortDto("account_display")]
        };

        foreach (var sorts in unsupportedSorts)
        {
            var tabular = new StubTabularExecutor();
            var reader = new StubLedgerAnalysisFlatDetailReader();
            var sut = new LedgerAnalysisComposableReportExecutor(new ReportExecutionPlanner(), tabular, reader);

            await sut.ExecuteAsync(Definition(), FlatRequest(sorts: sorts), default);

            tabular.WasCalled.Should().BeTrue();
            reader.WasCalled.Should().BeFalse();
        }

        var supportedTabular = new StubTabularExecutor();
        var supportedReader = new StubLedgerAnalysisFlatDetailReader();
        var supported = new LedgerAnalysisComposableReportExecutor(new ReportExecutionPlanner(), supportedTabular, supportedReader);

        await supported.ExecuteAsync(
            Definition(),
            FlatRequest(sorts: [new ReportSortDto("period_utc")]),
            default);

        supportedReader.WasCalled.Should().BeTrue();
        supportedTabular.WasCalled.Should().BeFalse();
    }

    [Theory]
    [InlineData("from_utc", null)]
    [InlineData("from_utc", "")]
    [InlineData("from_utc", "not-a-date")]
    [InlineData("to_utc", null)]
    [InlineData("to_utc", "   ")]
    [InlineData("to_utc", "2026-02-30")]
    public async Task ExecuteAsync_MissingBlankOrInvalidRequiredDate_ThrowsConfigurationViolation(
        string parameterCode,
        string? value)
    {
        var parameters = BuildParameters().ToDictionary(x => x.Key, x => x.Value, StringComparer.OrdinalIgnoreCase);
        if (value is null)
            parameters.Remove(parameterCode);
        else
            parameters[parameterCode] = value;
        var sut = new LedgerAnalysisComposableReportExecutor(
            new ReportExecutionPlanner(),
            new StubTabularExecutor(),
            new StubLedgerAnalysisFlatDetailReader());

        var action = () => sut.ExecuteAsync(Definition(), FlatRequest(parameters: parameters), default);

        await action.Should().ThrowAsync<NgbConfigurationViolationException>()
            .WithMessage($"*{parameterCode}*");
    }

    [Fact]
    public async Task ExecuteAsync_MaximumToDate_ThrowsWhenExclusiveBoundaryCannotBeRepresented()
    {
        var parameters = BuildParameters().ToDictionary(x => x.Key, x => x.Value, StringComparer.OrdinalIgnoreCase);
        parameters["to_utc"] = "9999-12-31";
        var sut = new LedgerAnalysisComposableReportExecutor(
            new ReportExecutionPlanner(),
            new StubTabularExecutor(),
            new StubLedgerAnalysisFlatDetailReader());

        var action = () => sut.ExecuteAsync(Definition(), FlatRequest(parameters: parameters), default);

        await action.Should().ThrowAsync<ArgumentOutOfRangeException>();
    }

    [Fact]
    public async Task ExecuteAsync_DecodesCursor_ClonesPredicates_AndMapsFallbackFields()
    {
        var cursor = new LedgerAnalysisFlatDetailCursor(
            new DateTime(2026, 2, 7, 0, 0, 0, DateTimeKind.Utc),
            long.MaxValue,
            "credit");
        var selectedAccount = Guid.CreateVersion7();
        var reader = new StubLedgerAnalysisFlatDetailReader();
        var eligible = new LedgerAnalysisComposableReportExecutor(
            new ReportExecutionPlanner(),
            new StubTabularExecutor(),
            reader);

        await eligible.ExecuteAsync(
            Definition(),
            FlatRequest(
                cursor: LedgerAnalysisDetailCursorCodec.Encode(cursor),
                filters: new Dictionary<string, ReportFilterValueDto>
                {
                    ["account_id"] = new(JsonSerializer.SerializeToElement(selectedAccount))
                }),
            default);

        reader.LastRequest!.Cursor.Should().Be(cursor);
        reader.LastRequest.Predicates.Should().ContainSingle();
        reader.LastRequest.Predicates[0].Value.GetGuid().Should().Be(selectedAccount);

        var tabular = new StubTabularExecutor();
        var fallback = new LedgerAnalysisComposableReportExecutor(
            new ReportExecutionPlanner(),
            tabular,
            new StubLedgerAnalysisFlatDetailReader());
        await fallback.ExecuteAsync(
            Definition(),
            FlatRequest(
                offset: 1,
                filters: new Dictionary<string, ReportFilterValueDto>
                {
                    ["account_id"] = new(JsonSerializer.SerializeToElement(selectedAccount))
                }),
            default);

        tabular.DetailFields.Should().HaveCount(2);
        tabular.Predicates.Should().ContainSingle();
        tabular.Predicates[0].Filter.Value.GetGuid().Should().Be(selectedAccount);
        tabular.Parameters.Should().HaveCount(2);
        tabular.Paging.Should().Be(new ReportPlanPaging(0, 2, null));
        tabular.Request!.Offset.Should().Be(0);
        tabular.Request.Cursor.Should().BeNull();
    }

    private static ReportDefinitionDto Definition()
        => new AccountingLedgerAnalysisDefinitionSource().GetDefinitions().Single();

    private static ReportExecutionRequestDto FlatRequest(
        IReadOnlyList<ReportSortDto>? sorts = null,
        int offset = 0,
        string? cursor = null,
        IReadOnlyDictionary<string, string>? parameters = null,
        IReadOnlyDictionary<string, ReportFilterValueDto>? filters = null,
        ReportLayoutDto? layout = null)
        => new(
            Parameters: parameters ?? BuildParameters(),
            Filters: filters,
            Cursor: cursor,
            Layout: layout ?? FlatLayout(sorts: sorts),
            Offset: offset,
            Limit: 2);

    private static ReportLayoutDto FlatLayout(
        IReadOnlyList<ReportGroupingDto>? rowGroups = null,
        IReadOnlyList<ReportGroupingDto>? columnGroups = null,
        IReadOnlyList<string>? detailFields = null,
        IReadOnlyList<ReportMeasureSelectionDto>? measures = null,
        IReadOnlyList<ReportSortDto>? sorts = null,
        bool showGrandTotals = false)
        => new(
            RowGroups: rowGroups,
            ColumnGroups: columnGroups,
            DetailFields: detailFields ?? ["period_utc", "account_display"],
            Measures: measures ?? [new ReportMeasureSelectionDto("debit_amount", ReportAggregationKind.Sum)],
            Sorts: sorts,
            ShowDetails: false,
            ShowSubtotals: false,
            ShowSubtotalsOnSeparateRows: false,
            ShowGrandTotals: showGrandTotals);

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
        public ReportExecutionRequestDto? Request { get; private set; }
        public IReadOnlyList<ReportPlanFieldSelection> DetailFields { get; private set; } = [];
        public IReadOnlyList<ReportPlanPredicate> Predicates { get; private set; } = [];
        public IReadOnlyList<ReportPlanParameter> Parameters { get; private set; } = [];
        public ReportPlanPaging? Paging { get; private set; }

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
            Request = request;
            DetailFields = detailFields;
            Predicates = predicates;
            Parameters = parameters;
            Paging = paging;
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
