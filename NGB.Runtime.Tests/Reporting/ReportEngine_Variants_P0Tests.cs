using FluentAssertions;
using NGB.Application.Abstractions.Services;
using NGB.Contracts.Reporting;
using NGB.Runtime.Reporting;
using NGB.Runtime.Reporting.Definitions;
using Xunit;

namespace NGB.Runtime.Tests.Reporting;

public sealed class ReportEngine_Variants_P0Tests
{
    [Fact]
    public async Task ExecuteAsync_WhenVariantCodeIsProvided_Resolves_Layout_And_Parameters_From_Variant()
    {
        var executor = new CapturingPlanExecutor();
        var sut = new ReportEngine(
            new ReportDefinitionCatalog([new StubDefinitionSource()]),
            new ReportLayoutValidator(),
            new ReportExecutionPlanner(),
            executor,
            new ReportSheetBuilder(),
            new ReportVariantRequestResolver(new StubVariantService()));

        var response = await sut.ExecuteAsync(
            "accounting.ledger.analysis",
            new ReportExecutionRequestDto(VariantCode: "month-end"),
            CancellationToken.None);

        response.Sheet.Columns.Select(x => x.Code).Should().Contain("debit_amount__sum");
        executor.CapturedParameters.Should().Contain(x => x.ParameterCode == "from_utc" && x.Value == "2026-03-01");
        executor.CapturedParameters.Should().Contain(x => x.ParameterCode == "to_utc" && x.Value == "2026-03-31");
        executor.CapturedMeasures.Single().MeasureCode.Should().Be("debit_amount");
        executor.CapturedMeasures.Single().Aggregation.Should().Be(ReportAggregationKind.Sum);
    }

    private sealed class StubDefinitionSource : IReportDefinitionSource
    {
        public IReadOnlyList<ReportDefinitionDto> GetDefinitions()
            => [new AccountingLedgerAnalysisDefinitionSource().GetDefinitions().Single()];
    }

    private sealed class StubVariantService : IReportVariantService
    {
        public Task<IReadOnlyList<ReportVariantDto>> GetAllAsync(string reportCode, CancellationToken ct)
            => Task.FromResult<IReadOnlyList<ReportVariantDto>>([]);

        public Task<ReportVariantDto?> GetAsync(string reportCode, string variantCode, CancellationToken ct)
            => Task.FromResult<ReportVariantDto?>(new ReportVariantDto(
                VariantCode: variantCode,
                ReportCode: reportCode,
                Name: "Month End",
                Layout: new ReportLayoutDto(
                    RowGroups: [new ReportGroupingDto("account_display")],
                    Measures: [new ReportMeasureSelectionDto("debit_amount")],
                    ShowSubtotals: true,
                    ShowGrandTotals: true),
                Parameters: new Dictionary<string, string>
                {
                    ["from_utc"] = "2026-03-01",
                    ["to_utc"] = "2026-03-31"
                },
                IsDefault: false,
                IsShared: true));

        public Task<ReportVariantDto> SaveAsync(ReportVariantDto variant, CancellationToken ct)
            => Task.FromResult(variant);

        public Task DeleteAsync(string reportCode, string variantCode, CancellationToken ct)
            => Task.CompletedTask;
    }

    private sealed class CapturingPlanExecutor : IReportPlanExecutor
    {
        public IReadOnlyList<ReportPlanMeasure> CapturedMeasures { get; private set; } = [];
        public IReadOnlyList<ReportPlanParameter> CapturedParameters { get; private set; } = [];

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
            CapturedMeasures = measures;
            CapturedParameters = parameters;

            return Task.FromResult(new ReportDataPage(
                Columns:
                [
                    new ReportDataColumn("account_display", "Account", "string", "row-group"),
                    new ReportDataColumn("debit_amount__sum", "Debit", "decimal", "measure")
                ],
                Rows:
                [
                    new ReportDataRow(new Dictionary<string, object?>
                    {
                        ["account_display"] = "1100 — Accounts Receivable",
                        ["debit_amount__sum"] = 100m
                    })
                ],
                Offset: paging.Offset,
                Limit: paging.Limit,
                Total: 1,
                HasMore: false));
        }
    }
}
