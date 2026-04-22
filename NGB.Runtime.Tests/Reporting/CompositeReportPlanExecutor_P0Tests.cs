using FluentAssertions;
using NGB.Application.Abstractions.Services;
using NGB.Contracts.Reporting;
using NGB.Runtime.Reporting;
using NGB.Tools.Exceptions;
using Xunit;

namespace NGB.Runtime.Tests.Reporting;

public sealed class CompositeReportPlanExecutor_P0Tests
{
    [Fact]
    public async Task ExecuteAsync_WhenSpecializedExecutorExists_RoutesToSpecializedPath()
    {
        var tabular = new StubTabularExecutor();
        var specialized = new StubSpecializedExecutor("accounting.trial_balance", "specialized");
        var sut = new CompositeReportPlanExecutor(tabular, [specialized]);

        var result = await sut.ExecuteAsync(
            BuildDefinition("accounting.trial_balance"),
            new ReportExecutionRequestDto(Offset: 3, Limit: 7),
            reportCode: "accounting.trial_balance",
            datasetCode: "ignored.dataset",
            rowGroups: [],
            columnGroups: [],
            detailFields: [],
            measures: [],
            sorts: [],
            predicates: [],
            parameters: [],
            paging: new ReportPlanPaging(3, 7),
            ct: CancellationToken.None);

        specialized.WasCalled.Should().BeTrue();
        tabular.WasCalled.Should().BeFalse();
        result.Diagnostics.Should().NotBeNull();
        result.Diagnostics!["executor"].Should().Be("specialized");
    }

    [Fact]
    public async Task ExecuteAsync_WhenSpecializedExecutorMissing_FallsBackToTabularPath()
    {
        var tabular = new StubTabularExecutor();
        var sut = new CompositeReportPlanExecutor(tabular, []);

        var result = await sut.ExecuteAsync(
            BuildDefinition("accounting.ledger.analysis", ReportExecutionMode.Composable),
            new ReportExecutionRequestDto(Offset: 5, Limit: 11),
            reportCode: "accounting.ledger.analysis",
            datasetCode: "accounting.ledger.analysis",
            rowGroups: [],
            columnGroups: [],
            detailFields: [],
            measures: [],
            sorts: [],
            predicates: [],
            parameters: [],
            paging: new ReportPlanPaging(5, 11),
            ct: CancellationToken.None);

        tabular.WasCalled.Should().BeTrue();
        result.Offset.Should().Be(5);
        result.Limit.Should().Be(11);
        result.Diagnostics.Should().NotBeNull();
        result.Diagnostics!["executor"].Should().Be("tabular");
    }

    [Fact]
    public void Constructor_WhenDuplicateSpecializedReportCodesExist_Throws()
    {
        var first = new StubSpecializedExecutor("accounting.trial_balance", "first");
        var second = new StubSpecializedExecutor("ACCOUNTING.TRIAL_BALANCE", "second");

        var act = () => new CompositeReportPlanExecutor(new StubTabularExecutor(), [first, second]);

        act.Should().Throw<NgbConfigurationViolationException>()
            .WithMessage("*accounting.trial_balance*registered more than once*");
    }

    private static ReportDefinitionDto BuildDefinition(string reportCode, ReportExecutionMode mode = ReportExecutionMode.Canonical)
        => new(
            ReportCode: reportCode,
            Name: reportCode,
            Mode: mode,
            Capabilities: new ReportCapabilitiesDto(
                AllowsFilters: true,
                AllowsRowGroups: true,
                AllowsColumnGroups: true,
                AllowsMeasures: true,
                AllowsDetailFields: true,
                AllowsSorting: true,
                AllowsShowDetails: true,
                AllowsSubtotals: true,
                AllowsGrandTotals: true));

    private sealed class StubSpecializedExecutor(string reportCode, string executorName) : IReportSpecializedPlanExecutor
    {
        public bool WasCalled { get; private set; }
        public string ReportCode { get; } = reportCode;

        public Task<ReportDataPage> ExecuteAsync(ReportDefinitionDto definition, ReportExecutionRequestDto request, CancellationToken ct)
        {
            WasCalled = true;
            return Task.FromResult(new ReportDataPage(
                Columns: [],
                Rows: [],
                Offset: request.Offset,
                Limit: request.Limit,
                Total: 0,
                HasMore: false,
                Diagnostics: new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
                {
                    ["executor"] = executorName
                }));
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
                Total: 0,
                HasMore: false,
                Diagnostics: new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
                {
                    ["executor"] = "tabular"
                }));
        }
    }
}
