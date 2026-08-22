using FluentAssertions;
using NGB.Contracts.Reporting;
using NGB.Runtime.Reporting;
using NGB.Tools.Exceptions;
using Xunit;

namespace NGB.Runtime.Tests.Reporting;

public sealed class ReportingDefinitionEdgesFullCoverageTests
{
    [Fact]
    public void DatasetField_FromDto_RejectsNullAndTimeGrainsOnNonTimeField()
    {
        Action missing = () => ReportDatasetFieldDefinition.FromDto(null!);
        Action invalidKind = () => ReportDatasetFieldDefinition.FromDto(
            Field("attribute", ReportFieldKind.Attribute, [ReportTimeGrain.Month]));

        missing.Should().Throw<NgbConfigurationViolationException>();
        invalidKind.Should().Throw<NgbConfigurationViolationException>()
            .WithMessage("*declares time grains but is not a time field*");
    }

    [Fact]
    public void DatasetField_SupportsNullAndConfiguredTimeGrainsWithEmptyFallback()
    {
        var unrestricted = ReportDatasetFieldDefinition.FromDto(Field("time", ReportFieldKind.Time));
        unrestricted.SupportsTimeGrain(null).Should().BeTrue();
        unrestricted.SupportsTimeGrain(ReportTimeGrain.Day).Should().BeFalse();

        var configured = ReportDatasetFieldDefinition.FromDto(
            Field("period", ReportFieldKind.Time, [ReportTimeGrain.Month]));
        configured.SupportsTimeGrain(null).Should().BeTrue();
        configured.SupportsTimeGrain(ReportTimeGrain.Month).Should().BeTrue();
        configured.SupportsTimeGrain(ReportTimeGrain.Day).Should().BeFalse();
    }

    [Fact]
    public void DatasetMeasure_CoversNullEmptySupportedSingleFallbackAndUnsupportedAggregation()
    {
        Action missing = () => ReportDatasetMeasureDefinition.FromDto(null!);
        missing.Should().Throw<NgbConfigurationViolationException>();

        var unrestricted = ReportDatasetMeasureDefinition.FromDto(Measure("all"));
        unrestricted.SupportsAggregation(ReportAggregationKind.Count).Should().BeTrue();

        var single = ReportDatasetMeasureDefinition.FromDto(
            Measure("average", [ReportAggregationKind.Average]));
        single.SupportsAggregation(ReportAggregationKind.Average).Should().BeTrue();
        single.SupportsAggregation(ReportAggregationKind.Max).Should().BeFalse();
        single.ResolveAggregation(ReportAggregationKind.Average).Should().Be(ReportAggregationKind.Average);
        single.ResolveAggregation(ReportAggregationKind.Sum).Should().Be(ReportAggregationKind.Average);
        single.ResolveAggregation(ReportAggregationKind.Max).Should().Be(ReportAggregationKind.Max);

        var multiple = ReportDatasetMeasureDefinition.FromDto(
            Measure("multiple", [ReportAggregationKind.Average, ReportAggregationKind.Max]));
        multiple.ResolveAggregation(ReportAggregationKind.Sum).Should().Be(ReportAggregationKind.Sum);
    }

    [Fact]
    public void RuntimeModel_CoversDefaultsExplicitValuesDatasetAndEffectiveLayoutEdges()
    {
        Action missingDefinition = () => new ReportDefinitionRuntimeModel(null!);
        missingDefinition.Should().Throw<NgbConfigurationViolationException>();

        var defaults = new ReportDefinitionRuntimeModel(new ReportDefinitionDto("report", "Report"));
        defaults.Definition.ReportCode.Should().Be("report");
        defaults.ReportCodeNorm.Should().Be("report");
        defaults.Capabilities.Should().NotBeNull();
        defaults.DefaultLayout.Should().NotBeNull();
        defaults.Dataset.Should().BeNull();

        Action missingRequest = () => defaults.GetEffectiveLayout(null!);
        missingRequest.Should().Throw<NgbArgumentRequiredException>();
        defaults.GetEffectiveLayout(new ReportExecutionRequestDto()).Should().BeSameAs(defaults.DefaultLayout);

        var capabilities = new ReportCapabilitiesDto(AllowsVariants: true);
        var layout = new ReportLayoutDto(ShowGrandTotals: false);
        var explicitModel = new ReportDefinitionRuntimeModel(new ReportDefinitionDto(
            "explicit",
            "Explicit",
            Dataset: new ReportDatasetDto("dataset", [], []),
            Capabilities: capabilities,
            DefaultLayout: layout));
        var requestLayout = new ReportLayoutDto(ShowDetails: true);

        explicitModel.Capabilities.Should().BeSameAs(capabilities);
        explicitModel.DefaultLayout.Should().BeSameAs(layout);
        explicitModel.Dataset.Should().NotBeNull();
        explicitModel.GetEffectiveLayout(new ReportExecutionRequestDto(Layout: requestLayout))
            .Should().BeSameAs(requestLayout);
    }

    private static ReportFieldDto Field(
        string code,
        ReportFieldKind kind,
        IReadOnlyList<ReportTimeGrain>? grains = null)
        => new(code, code, "string", kind, SupportedTimeGrains: grains);

    private static ReportMeasureDto Measure(
        string code,
        IReadOnlyList<ReportAggregationKind>? aggregations = null)
        => new(code, code, "decimal", aggregations);
}
