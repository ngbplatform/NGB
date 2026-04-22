using FluentAssertions;
using NGB.Contracts.Reporting;
using NGB.Core.Reporting.Exceptions;
using NGB.Runtime.Reporting;
using Xunit;

namespace NGB.Runtime.Tests.Reporting;

public sealed class ReportLayoutValidator_Canonical_P0Tests
{
    [Fact]
    public void Validate_When_Required_Parameter_Missing_Throws_Validation_Error()
    {
        var sut = new ReportLayoutValidator();
        var definition = BuildDefinition();

        var act = () => sut.Validate(
            definition,
            new ReportExecutionRequestDto(
                Parameters: new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
                {
                    ["from_utc"] = "2026-03-01"
                },
                Offset: 0,
                Limit: 50));

        act.Should().Throw<ReportLayoutValidationException>()
            .WithMessage("*'To' is required.*");
    }

    [Fact]
    public void Validate_When_Required_Canonical_Filter_Missing_Throws_Validation_Error()
    {
        var sut = new ReportLayoutValidator();
        var definition = BuildAccountScopedDefinition();

        var act = () => sut.Validate(
            definition,
            new ReportExecutionRequestDto(
                Parameters: new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
                {
                    ["from_utc"] = "2026-03-01",
                    ["to_utc"] = "2026-03-31"
                },
                Offset: 0,
                Limit: 50));

        act.Should().Throw<ReportLayoutValidationException>()
            .WithMessage("*'Account' is required.*");
    }

    [Fact]
    public void Validate_When_Unknown_Canonical_Filter_Is_Provided_Throws_Validation_Error()
    {
        var sut = new ReportLayoutValidator();
        var definition = BuildDefinition();

        var act = () => sut.Validate(
            definition,
            new ReportExecutionRequestDto(
                Parameters: new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
                {
                    ["from_utc"] = "2026-03-01",
                    ["to_utc"] = "2026-03-31"
                },
                Filters: new Dictionary<string, ReportFilterValueDto>(StringComparer.OrdinalIgnoreCase)
                {
                    ["unknown_filter"] = new(System.Text.Json.JsonSerializer.SerializeToElement("x"))
                },
                Offset: 0,
                Limit: 50));

        act.Should().Throw<ReportLayoutValidationException>()
            .WithMessage("*not available as a filter in this report*");
    }

    private static ReportDefinitionDto BuildAccountScopedDefinition()
        => new(
            ReportCode: "accounting.account_card",
            Name: "Account Card",
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
            ],
            Filters:
            [
                new ReportFilterFieldDto("account_id", "Account", "uuid", IsRequired: true),
                new ReportFilterFieldDto("property_id", "Property", "uuid", IsMulti: true)
            ]);

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
            ],
            Filters:
            [
                new ReportFilterFieldDto("property_id", "Property", "uuid", IsMulti: true)
            ]);
}
