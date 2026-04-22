using FluentAssertions;
using NGB.Application.Abstractions.Services;
using NGB.Contracts.Reporting;
using NGB.Runtime.Reporting;
using NGB.Runtime.Reporting.Canonical;
using Xunit;

namespace NGB.Runtime.Tests.Reporting;

public sealed class ReportEngine_Canonical_P0Tests
{
    [Fact]
    public async Task ExecuteAsync_Canonical_Report_Uses_Shared_Plan_Executor_Path()
    {
        var planExecutor = new CompositeReportPlanExecutor(tabularExecutor: null, specializedExecutors: [new StubSpecializedExecutor()]);
        var engine = new ReportEngine(
            new ReportDefinitionCatalog([new StubDefinitionSource(BuildDefinition())]),
            new ReportLayoutValidator(),
            new ReportExecutionPlanner(),
            planExecutor,
            new ReportSheetBuilder());

        var response = await engine.ExecuteAsync(
            "accounting.trial_balance",
            new ReportExecutionRequestDto(
                Parameters: new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
                {
                    ["from_utc"] = "2026-03-01",
                    ["to_utc"] = "2026-03-31"
                },
                Offset: 0,
                Limit: 50),
            CancellationToken.None);

        response.Diagnostics.Should().NotBeNull();
        response.Diagnostics!["engine"].Should().Be("runtime");
        response.Diagnostics!["executor"].Should().Be("stub-specialized");
        response.Sheet.Meta!.Title.Should().Be("Trial Balance");
        response.Sheet.Meta.Diagnostics!["sheetBuilder"].Should().Be("prebuilt-v1");
    }

    private static ReportDefinitionDto BuildDefinition()
        => new(
            ReportCode: "accounting.trial_balance",
            Name: "Trial Balance",
            Group: "Accounting",
            Mode: ReportExecutionMode.Canonical,
            Capabilities: new ReportCapabilitiesDto(
                AllowsFilters: true,
                AllowsRowGroups: false,
                AllowsColumnGroups: false,
                AllowsMeasures: false,
                AllowsDetailFields: false,
                AllowsSorting: false,
                AllowsShowDetails: false,
                AllowsSubtotals: false,
                AllowsGrandTotals: true),
            Parameters:
            [
                new ReportParameterMetadataDto("from_utc", "date", true),
                new ReportParameterMetadataDto("to_utc", "date", true)
            ]);

    private sealed class StubDefinitionSource(ReportDefinitionDto definition) : IReportDefinitionSource
    {
        public IReadOnlyList<ReportDefinitionDto> GetDefinitions() => [definition];
    }

    private sealed class StubSpecializedExecutor : IReportSpecializedPlanExecutor
    {
        public string ReportCode => "accounting.trial_balance";

        public Task<ReportDataPage> ExecuteAsync(ReportDefinitionDto definition, ReportExecutionRequestDto request, CancellationToken ct)
            => Task.FromResult(CanonicalReportExecutionHelper.CreatePrebuiltPage(
                sheet: new ReportSheetDto([], [], new ReportSheetMetaDto(Title: definition.Name)),
                offset: 0,
                limit: request.Limit,
                total: 0,
                hasMore: false,
                diagnostics: new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
                {
                    ["executor"] = "stub-specialized"
                }));
    }
}
