using FluentAssertions;
using NGB.Contracts.Metadata;
using NGB.Contracts.Reporting;
using NGB.Core.Reporting.Exceptions;
using NGB.Runtime.Reporting.Canonical;
using NGB.Tools.Extensions;
using NGB.Tools.Normalization;
using Xunit;

namespace NGB.Runtime.Tests.Reporting;

public sealed class CanonicalReportExecutionHelper_P0Tests
{
    [Fact]
    public void BuildDimensionScopes_Uses_Filter_Lookup_Metadata_Without_Vertical_Literals()
    {
        var definition = new ReportDefinitionDto(
            ReportCode: "accounting.trial_balance",
            Name: "Trial Balance",
            Mode: ReportExecutionMode.Canonical,
            Filters:
            [
                new ReportFilterFieldDto("building_id", "Building", "uuid", Lookup: new CatalogLookupSourceDto("demo.building")),
                new ReportFilterFieldDto("contract_id", "Contract", "uuid", Lookup: new DocumentLookupSourceDto(["demo.contract"]))
            ]);

        var buildingId = Guid.CreateVersion7();
        var contractId = Guid.CreateVersion7();
        var request = new ReportExecutionRequestDto(
            Filters: new Dictionary<string, ReportFilterValueDto>(StringComparer.OrdinalIgnoreCase)
            {
                ["building_id"] = new(System.Text.Json.JsonSerializer.SerializeToElement(new[] { buildingId }), IncludeDescendants: true),
                ["contract_id"] = new(System.Text.Json.JsonSerializer.SerializeToElement(contractId))
            });

        var scopes = CanonicalReportExecutionHelper.BuildDimensionScopes(definition, request);

        scopes.Should().NotBeNull();
        scopes!.Should().HaveCount(2);
        scopes.Should().Contain(x => x.DimensionId == DeterministicGuid.Create($"Dimension|{CodeNormalizer.NormalizeCodeNorm("demo.building", "dimensionCode")}") && x.IncludeDescendants);
        scopes.Should().Contain(x => x.DimensionId == DeterministicGuid.Create($"Dimension|{CodeNormalizer.NormalizeCodeNorm("demo.contract", "dimensionCode")}") && !x.IncludeDescendants);
    }

    [Theory]
    [InlineData("True", true)]
    [InlineData("False", false)]
    [InlineData("true", true)]
    [InlineData("false", false)]
    [InlineData("Yes", true)]
    [InlineData("No", false)]
    [InlineData("1", true)]
    [InlineData("0", false)]
    public void GetOptionalBoolFilter_Accepts_User_Facing_Boolean_String_Values(string raw, bool expected)
    {
        var definition = new ReportDefinitionDto(
            ReportCode: "accounting.general_journal",
            Name: "General Journal",
            Mode: ReportExecutionMode.Canonical,
            Filters:
            [
                new ReportFilterFieldDto("is_storno", "Storno", "bool")
            ]);

        var request = new ReportExecutionRequestDto(
            Filters: new Dictionary<string, ReportFilterValueDto>(StringComparer.OrdinalIgnoreCase)
            {
                ["is_storno"] = new(System.Text.Json.JsonSerializer.SerializeToElement(raw))
            });

        var value = CanonicalReportExecutionHelper.GetOptionalBoolFilter(definition, request, "is_storno");

        value.Should().Be(expected);
    }

    [Fact]
    public void GetRequiredDateOnlyParameter_When_Missing_Returns_User_Friendly_Message()
    {
        var definition = new ReportDefinitionDto(
            ReportCode: "accounting.trial_balance",
            Name: "Trial Balance",
            Mode: ReportExecutionMode.Canonical,
            Parameters:
            [
                new ReportParameterMetadataDto("from_utc", "date", true, Label: "From"),
                new ReportParameterMetadataDto("to_utc", "date", true, Label: "To")
            ]);

        var act = () => CanonicalReportExecutionHelper.GetRequiredDateOnlyParameter(definition, new ReportExecutionRequestDto(), "to_utc");

        act.Should().Throw<ReportLayoutValidationException>()
            .WithMessage("To is required.");
    }

    [Fact]
    public void GetOptionalGuidFilter_When_Invalid_Returns_User_Friendly_Message()
    {
        var definition = new ReportDefinitionDto(
            ReportCode: "pm.building.summary",
            Name: "Building Summary",
            Mode: ReportExecutionMode.Canonical,
            Filters:
            [
                new ReportFilterFieldDto("building_id", "Building", "uuid")
            ]);

        var request = new ReportExecutionRequestDto(
            Filters: new Dictionary<string, ReportFilterValueDto>(StringComparer.OrdinalIgnoreCase)
            {
                ["building_id"] = new(System.Text.Json.JsonSerializer.SerializeToElement("not-a-guid"))
            });

        var act = () => CanonicalReportExecutionHelper.GetOptionalGuidFilter(definition, request, "building_id");

        act.Should().Throw<ReportLayoutValidationException>()
            .WithMessage("Select a valid Building.");
    }

    [Fact]
    public void GetOptionalBoolFilter_When_Invalid_Returns_User_Friendly_Message()
    {
        var definition = new ReportDefinitionDto(
            ReportCode: "accounting.general_journal",
            Name: "General Journal",
            Mode: ReportExecutionMode.Canonical,
            Filters:
            [
                new ReportFilterFieldDto("is_storno", "Storno", "bool")
            ]);

        var request = new ReportExecutionRequestDto(
            Filters: new Dictionary<string, ReportFilterValueDto>(StringComparer.OrdinalIgnoreCase)
            {
                ["is_storno"] = new(System.Text.Json.JsonSerializer.SerializeToElement("maybe"))
            });

        var act = () => CanonicalReportExecutionHelper.GetOptionalBoolFilter(definition, request, "is_storno");

        act.Should().Throw<ReportLayoutValidationException>()
            .WithMessage("Select Yes or No for Storno.");
    }
}
