using FluentAssertions;
using NGB.Contracts.Reporting;
using NGB.Runtime.Reporting.Rendering;
using Xunit;

namespace NGB.Runtime.Tests.Reporting;

public sealed class ReportCellFormatter_P0Tests
{
    [Theory]
    [InlineData("period_utc__day", ReportTimeGrain.Day, "03/14/2026")]
    [InlineData("period_utc__week", ReportTimeGrain.Week, "Week of 03/14/2026")]
    [InlineData("period_utc__month", ReportTimeGrain.Month, "March 2026")]
    [InlineData("period_utc__quarter", ReportTimeGrain.Quarter, "Q1 2026")]
    [InlineData("period_utc__year", ReportTimeGrain.Year, "2026")]
    public void BuildCell_TimeGrained_Period_Uses_UserFacing_Display(string columnCode, ReportTimeGrain timeGrain, string expected)
    {
        var sut = new ReportCellFormatter();
        var column = new ReportSheetColumnDto(columnCode, "Period", "datetime", SemanticRole: "row-group");

        var cell = sut.BuildCell(new DateOnly(2026, 3, 14), column);

        cell.Display.Should().Be(expected);
        sut.FormatGroupLabel(new DateOnly(2026, 3, 14), timeGrain).Should().Be(expected);
    }
}
