using FluentAssertions;
using NGB.Contracts.Common;
using NGB.Contracts.Reporting;
using NGB.Core.Reporting.Exceptions;
using NGB.Runtime.Reporting.Canonical;
using Xunit;

namespace NGB.Runtime.Tests.Reporting;

public sealed class CanonicalReportPageDataLimitTests
{
    [Theory]
    [InlineData(500, 0, 500)]
    [InlineData(500, 1, 499)]
    [InlineData(500, 2, 498)]
    [InlineData(499, 1, 499)]
    [InlineData(499, 2, 498)]
    [InlineData(498, 2, 498)]
    [InlineData(1, 2, 1)]
    [InlineData(1000, 1, 499)]
    [InlineData(int.MaxValue, 2, 498)]
    [InlineData(0, 1, 50)]
    [InlineData(-1, 2, 50)]
    public void Source_page_reserves_only_the_space_needed_at_the_rendered_boundary(int requested, int reserved, int expected)
    {
        var definition = Definition(500);
        CanonicalReportExecutionHelper.ResolvePageDataLimit(definition, new(Limit: requested), 50, reserved)
            .Should().Be(expected);
    }

    [Theory]
    [InlineData(1, 0, 1)]
    [InlineData(2, 1, 1)]
    [InlineData(3, 2, 1)]
    [InlineData(100, 2, 98)]
    [InlineData(int.MaxValue, 2, 500)]
    public void Uses_definition_budget_instead_of_a_hard_coded_page_size(int maxRows, int reserved, int expected)
        => CanonicalReportExecutionHelper.ResolvePageDataLimit(Definition(maxRows), new(Limit: 500), 50, reserved)
            .Should().Be(expected);

    [Theory]
    [InlineData(0, 0)]
    [InlineData(1, 1)]
    [InlineData(2, 2)]
    [InlineData(1, 2)]
    [InlineData(int.MinValue, 2)]
    public void Impossible_budget_fails_before_querying_instead_of_issuing_a_zero_or_negative_limit(int maxRows, int reserved)
    {
        var act = () => CanonicalReportExecutionHelper.ResolvePageDataLimit(Definition(maxRows), new(Limit: 500), 50, reserved);
        act.Should().Throw<ReportLayoutValidationException>().WithMessage("*at least one data row*");
    }

    [Theory]
    [InlineData(0, 50)]
    [InlineData(500, 500)]
    public void Unspecified_visible_cap_preserves_existing_requested_or_default_limit(int requested, int expected)
        => CanonicalReportExecutionHelper.ResolvePageDataLimit(Definition(null), new(Limit: requested), 50, 2)
            .Should().Be(expected);

    [Theory]
    [InlineData(0, 50)]
    [InlineData(500, 500)]
    public void Missing_capabilities_preserves_requested_or_default_limit(int requested, int expected)
        => CanonicalReportExecutionHelper.ResolvePageDataLimit(new("test.canonical", "Test"), new(Limit: requested), 50, 2)
            .Should().Be(expected);

    [Fact]
    public void Materialized_execution_preserves_overflow_detection_instead_of_truncating()
        => CanonicalReportExecutionHelper.ResolvePageDataLimit(Definition(500), new(Limit: 500, DisablePaging: true), 50, 2)
            .Should().Be(PagingLimits.MaxMaterializedRows + 1);

    [Theory]
    [InlineData(0, 0)]
    [InlineData(-1, 1)]
    [InlineData(50, -1)]
    public void Invalid_reservation_arguments_are_rejected(int defaultLimit, int reserved)
    {
        var act = () => CanonicalReportExecutionHelper.ResolvePageDataLimit(Definition(500), new(), defaultLimit, reserved);
        act.Should().Throw<ArgumentOutOfRangeException>();
    }

    private static ReportDefinitionDto Definition(int? maxRows)
        => new("test.canonical", "Test", Capabilities: new ReportCapabilitiesDto(MaxVisibleRows: maxRows));
}
