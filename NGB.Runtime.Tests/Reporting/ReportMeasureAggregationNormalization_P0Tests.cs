using FluentAssertions;
using NGB.Contracts.Reporting;
using NGB.Runtime.Reporting;
using NGB.Runtime.Reporting.Definitions;
using Xunit;

namespace NGB.Runtime.Tests.Reporting;

public sealed class ReportMeasureAggregationNormalization_P0Tests
{
    [Fact]
    public void Validator_Allows_Implicit_Default_Aggregation_For_SumDefaultMeasure()
    {
        var definition = new AccountingLedgerAnalysisDefinitionSource().GetDefinitions().Single();
        var request = new ReportExecutionRequestDto(
            Parameters: new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["from_utc"] = "2026-03-01",
                ["to_utc"] = "2026-03-31"
            },
            Layout: new ReportLayoutDto(
                Measures: [new ReportMeasureSelectionDto("debit_amount")]),
            Offset: 0,
            Limit: 100);

        var act = () => new ReportLayoutValidator().Validate(definition, request);

        act.Should().NotThrow();
    }

    [Fact]
    public void Planner_Normalizes_Implicit_Default_Aggregation_To_Sum_For_DebitMeasure()
    {
        var definition = new ReportDefinitionRuntimeModel(new AccountingLedgerAnalysisDefinitionSource().GetDefinitions().Single());
        var request = new ReportExecutionRequestDto(
            Layout: new ReportLayoutDto(
                Measures: [new ReportMeasureSelectionDto("debit_amount")]),
            Offset: 0,
            Limit: 100);
        var context = new ReportExecutionContext(definition, request, definition.GetEffectiveLayout(request));

        var plan = new ReportExecutionPlanner().BuildPlan(context);

        plan.Measures.Should().ContainSingle();
        plan.Measures[0].Aggregation.Should().Be(ReportAggregationKind.Sum);
        plan.Measures[0].OutputCode.Should().Be("debit_amount__sum");
    }
}
