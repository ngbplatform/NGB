using FluentAssertions;
using NGB.Contracts.Reporting;
using NGB.Runtime.Reporting;
using NGB.Tools.Exceptions;
using Xunit;

namespace NGB.Runtime.Tests.Reporting;

public sealed class ReportDatasetDefinitionFullCoverageTests
{
    [Fact]
    public void Constructor_WhenDtoIsNull_ThrowsConfigurationViolation()
    {
        var act = () => new ReportDatasetDefinition(null!);

        act.Should().Throw<NgbConfigurationViolationException>()
            .WithMessage("*dataset definition is not configured*");
    }

    [Fact]
    public void Constructor_WithNullCollections_CreatesEmptyDefinition()
    {
        var definition = new ReportDatasetDefinition(new ReportDatasetDto(" test.dataset "));

        definition.DatasetCodeNorm.Should().Be("test.dataset");
        definition.Dataset.DatasetCode.Should().Be(" test.dataset ");
        definition.Fields.Should().BeEmpty();
        definition.Measures.Should().BeEmpty();
    }

    [Fact]
    public void Constructor_WithDuplicateNormalizedFieldCode_ThrowsConfigurationViolation()
    {
        var dto = new ReportDatasetDto(
            "test.dataset",
            Fields:
            [
                Field(" Account "),
                Field("ACCOUNT")
            ]);

        var act = () => new ReportDatasetDefinition(dto);

        act.Should().Throw<NgbConfigurationViolationException>()
            .WithMessage("*duplicate field code 'account'*");
    }

    [Fact]
    public void Constructor_WithDuplicateNormalizedMeasureCode_ThrowsConfigurationViolation()
    {
        var dto = new ReportDatasetDto(
            "test.dataset",
            Measures:
            [
                Measure(" Amount "),
                Measure("AMOUNT")
            ]);

        var act = () => new ReportDatasetDefinition(dto);

        act.Should().Throw<NgbConfigurationViolationException>()
            .WithMessage("*duplicate measure code 'amount'*");
    }

    [Fact]
    public void FieldCapabilities_AndLookups_HandlePositiveNegativeAndBoundaryCases()
    {
        var definition = new ReportDatasetDefinition(
            new ReportDatasetDto(
                "test.dataset",
                Fields:
                [
                    new ReportFieldDto(
                        "period",
                        "Period",
                        "date",
                        ReportFieldKind.Time,
                        IsFilterable: true,
                        IsGroupable: true,
                        IsSortable: true,
                        IsSelectable: true,
                        SupportedTimeGrains: [ReportTimeGrain.Month]),
                    Field("plain")
                ],
                Measures:
                [
                    Measure("amount", [ReportAggregationKind.Sum]),
                    Measure("unrestricted")
                ]));

        definition.TryGetField(" PERIOD ", out var period).Should().BeTrue();
        period.CodeNorm.Should().Be("period");
        definition.TryGetField("missing", out _).Should().BeFalse();
        definition.IsFilterableField("period").Should().BeTrue();
        definition.IsGroupableField("period").Should().BeTrue();
        definition.IsSortableField("period").Should().BeTrue();
        definition.IsSelectableField("period").Should().BeTrue();
        definition.IsFilterableField("plain").Should().BeFalse();
        definition.IsGroupableField("plain").Should().BeFalse();
        definition.IsSortableField("plain").Should().BeFalse();
        definition.IsSelectableField("plain").Should().BeFalse();
        definition.SupportsTimeGrain("period", null).Should().BeTrue();
        definition.SupportsTimeGrain("period", ReportTimeGrain.Month).Should().BeTrue();
        definition.SupportsTimeGrain("period", ReportTimeGrain.Year).Should().BeFalse();
        definition.SupportsTimeGrain("missing", ReportTimeGrain.Month).Should().BeFalse();

        definition.TryGetMeasure(" AMOUNT ", out var amount).Should().BeTrue();
        amount.CodeNorm.Should().Be("amount");
        definition.TryGetMeasure("missing", out _).Should().BeFalse();
        definition.SupportsAggregation("amount", ReportAggregationKind.Sum).Should().BeTrue();
        definition.SupportsAggregation("amount", ReportAggregationKind.Average).Should().BeFalse();
        definition.SupportsAggregation("unrestricted", ReportAggregationKind.Average).Should().BeTrue();
        definition.SupportsAggregation("missing", ReportAggregationKind.Sum).Should().BeFalse();
        definition.ResolveAggregation("amount", ReportAggregationKind.Sum).Should().Be(ReportAggregationKind.Sum);
    }

    [Fact]
    public void ResolveAggregation_WhenMeasureDoesNotExist_ThrowsConfigurationViolation()
    {
        var definition = new ReportDatasetDefinition(new ReportDatasetDto("test.dataset"));

        var act = () => definition.ResolveAggregation(" Missing ", ReportAggregationKind.Sum);

        act.Should().Throw<NgbConfigurationViolationException>()
            .WithMessage("*does not define measure 'missing'*");
    }

    private static ReportFieldDto Field(string code)
        => new(code, code, "string", ReportFieldKind.Dimension);

    private static ReportMeasureDto Measure(
        string code,
        IReadOnlyList<ReportAggregationKind>? supportedAggregations = null)
        => new(code, code, "decimal", supportedAggregations);
}
